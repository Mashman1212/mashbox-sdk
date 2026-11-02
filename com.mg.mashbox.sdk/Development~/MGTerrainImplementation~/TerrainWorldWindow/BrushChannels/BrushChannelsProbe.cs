using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MashBoxSDK.MapTools;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;
public static class BrushChannelsProbe
{
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    const string Report="C:/Users/matth/.codex/visualizations/2026/11/02/01a19ca8-03f2-7823-8496-6dd65058b3ef/BrushChannels/result.txt";
    static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,F).Invoke(o,a);
    static object Get(object o,string n)=>o.GetType().GetField(n,F).GetValue(o);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,F).SetValue(o,v);
    public static void Run()
    {
        try
        {
            File.WriteAllText(Report,"Brush channel toggle regression\n");
            var root=new GameObject("World");var world=root.AddComponent<MGTerrainWorld>();
            var go=new GameObject("Tile");go.transform.SetParent(root.transform);var tile=go.AddComponent<MGTerrain>();
            var mesh=new Mesh{vertices=new[]{Vector3.zero,new Vector3(4,0,0),new Vector3(0,0,4),new Vector3(4,0,4)},triangles=new[]{0,2,1,1,2,3}};mesh.RecalculateBounds();go.GetComponent<MeshFilter>().sharedMesh=mesh;
            var material=new Material(Shader.Find("Unlit/Color"));
            var maps=new[]{new Texture2D(4,4,TextureFormat.R16,false,true),new Texture2D(4,4,TextureFormat.RHalf,false,true),new Texture2D(4,4,TextureFormat.RHalf,false,true)};
            using(var data=new SerializedObject(tile))
            {
                var prototypes=data.FindProperty("m_Prototypes");prototypes.arraySize=1;var proto=prototypes.GetArrayElementAtIndex(0);
                proto.FindPropertyRelative("m_Mesh").objectReferenceValue=mesh;proto.FindPropertyRelative("m_Material").objectReferenceValue=material;
                var layers=data.FindProperty("m_DensityDetailLayers");layers.arraySize=1;var layer=layers.GetArrayElementAtIndex(0);
                layer.FindPropertyRelative("m_PrototypeIndex").intValue=0;
                layer.FindPropertyRelative("m_DensityMap").objectReferenceValue=maps[0];layer.FindPropertyRelative("m_SizeMap").objectReferenceValue=maps[1];layer.FindPropertyRelative("m_HealthMap").objectReferenceValue=maps[2];
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            world.RefreshChunks();
            var editor=(MGTerrainEditor)UnityEditor.Editor.CreateEditor(tile,typeof(MGTerrainEditor));
            var copies=(HashSet<Texture2D>)Get(editor,"m_PaintCopies");foreach(var map in maps)copies.Add(map);
            Call(editor,"SelectDetailPrototype",0,false,false);
            Set(editor,"m_PaintDensity",3f);Set(editor,"m_PaintSize",2f);Set(editor,"m_PaintHealth",.25f);
            var state=typeof(MGTerrainEditor).Assembly.GetType("MashBoxSDK.MapTools.MBEditorToolState");state.GetProperty("BrushRadius",F).SetValue(null,100f);state.GetProperty("BrushStrength",F).SetValue(null,1f);
            for(int mask=0;mask<8;mask++)
            {
                for(int c=0;c<3;c++)
                {
                    Call(editor,"SetDetailBrushChannel",c,(mask&(1<<c))!=0);
                    if((bool)Call(editor,"PaintsChannel",c)!=((mask&(1<<c))!=0))throw new Exception("Toggle state mismatch for mask "+mask);
                    var pixels=maps[c].GetPixelData<ushort>(0);for(int p=0;p<16;p++)pixels[p]=c==0?(ushort)10:Mathf.FloatToHalf(1);maps[c].Apply();
                }
                Call(editor,"BeginMultiDetailStroke");Call(editor,"PaintSelectedDetailDab",tile,new Vector3(2,0,2),false,false);Call(editor,"FinishDetailStroke");
                for(int c=0;c<3;c++)
                {
                    var pixels=maps[c].GetPixelData<ushort>(0);ushort initial=c==0?(ushort)10:Mathf.FloatToHalf(1);
                    for(int p=0;p<16;p++)if((pixels[p]!=initial)!=((mask&(1<<c))!=0))throw new Exception("Paint channel mismatch: mask "+mask+", channel "+c);
                }
                File.AppendAllText(Report,"PASS: channel combination "+mask+" changes only enabled maps.\n");
            }
            Tools.current=Tool.Move;Call(editor,"UpdateWorldDetailBrush",tile);
            if(!(bool)Get(editor,"m_DetailPainting"))throw new Exception("Valid channels did not activate the brush");
            for(int c=0;c<3;c++)Call(editor,"SetDetailBrushChannel",c,false);
            if((bool)Get(editor,"m_DetailPainting")||Tools.current!=Tool.Move)throw new Exception("All-off did not release the brush and restore the scene tool");
            Call(editor,"SetDetailBrushChannel",1,true);
            if(!(bool)Get(editor,"m_DetailPainting")||!(bool)Call(editor,"PaintsChannel",1)||(bool)Call(editor,"PaintsChannel",0))throw new Exception("Re-enabling Size did not reactivate only that channel");
            File.AppendAllText(Report,"PASS: all-off releases the brush; clicking Size reactivates it.\nALL PASSED\n");
            UnityEngine.Object.DestroyImmediate(editor);EditorApplication.Exit(0);
        }
        catch(Exception error){File.AppendAllText(Report,"FAIL: "+error+"\n");Debug.LogException(error);EditorApplication.Exit(1);}
    }
}
