using System;
using System.IO;
using System.Reflection;
using MashBoxSDK.MapTools;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;
public static class DetailSelectionProbe
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    const string Report = "C:/Users/matth/.codex/visualizations/2026/11/02/01a19ca8-03f2-7823-8496-6dd65058b3ef/DetailSelection/result.txt";
    static void Set(object o, string field, object value) => o.GetType().GetField(field, Flags).SetValue(o, value);
    static void Check(bool ok, string text) { if (!ok) throw new Exception(text); File.AppendAllText(Report, "PASS: " + text + "\n"); }
    public static void Run()
    {
        try
        {
            File.WriteAllText(Report, "Checked Sub-ID brush regression\n");
            var go = new GameObject("Probe"); go.SetActive(false);
            var terrain = go.AddComponent<MGTerrain>();
            var mesh = new Mesh { vertices = new[] { Vector3.zero, new Vector3(4,0,0), new Vector3(0,0,4), new Vector3(4,0,4) }, triangles = new[] { 0,2,1,1,2,3 } };
            mesh.RecalculateBounds(); go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var ids = new Texture2D(4,4,TextureFormat.R8,false,true);
            var idPixels = ids.GetPixelData<byte>(0); for(int i=0;i<16;i++) idPixels[i]=(byte)(i%2); ids.Apply();
            using(var data = new SerializedObject(terrain))
            {
                data.FindProperty("m_Prototypes").arraySize=1;
                var layers=data.FindProperty("m_DensityDetailLayers"); layers.arraySize=1;
                var layer=layers.GetArrayElementAtIndex(0);
                layer.FindPropertyRelative("m_PrototypeIndex").intValue=0;
                layer.FindPropertyRelative("m_GrassIdMap").objectReferenceValue=ids;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            var editor=(MGTerrainEditor)UnityEditor.Editor.CreateEditor(terrain,typeof(MGTerrainEditor));
            Set(editor,"m_RandomGrassSubIds",true); Set(editor,"m_GrassStrokeSubIdMask",2); Set(editor,"m_PaintDetailIndex",0);
            var state=typeof(MGTerrainEditor).Assembly.GetType("MashBoxSDK.MapTools.MBEditorToolState");
            state.GetProperty("BrushRadius",Flags).SetValue(null,100f); state.GetProperty("BrushStrength",Flags).SetValue(null,1f);
            var dab=typeof(MGTerrainEditor).GetMethod("PaintDetailDab",Flags);
            var size=new Texture2D(4,4,TextureFormat.RHalf,false,true);
            var pixels=size.GetPixelData<ushort>(0); for(int i=0;i<16;i++)pixels[i]=Mathf.FloatToHalf(1);
            Set(editor,"m_StrokeMap",size); Set(editor,"m_PaintChannel",1); Set(editor,"m_PaintSize",3f);
            dab.Invoke(editor,new object[]{terrain,new Vector3(2,0,2),false});
            pixels=size.GetPixelData<ushort>(0);
            for(int i=0;i<16;i++) if(i%2==0 ? pixels[i]!=Mathf.FloatToHalf(1) : Mathf.HalfToFloat(pixels[i])<=1) throw new Exception("Size affected an unchecked ID or missed a checked ID");
            Check(true,"Size strokes affect checked types only");
            Set(editor,"m_PaintChannel",2); Set(editor,"m_PaintHealth",.2f);
            for(int i=0;i<16;i++)pixels[i]=Mathf.FloatToHalf(1);
            dab.Invoke(editor,new object[]{terrain,new Vector3(2,0,2),false});
            pixels=size.GetPixelData<ushort>(0);
            for(int i=0;i<16;i++) if(i%2==0 ? pixels[i]!=Mathf.FloatToHalf(1) : Mathf.HalfToFloat(pixels[i])>=1) throw new Exception("Health selection mismatch");
            Check(true,"Health strokes affect checked types only");
            Set(editor,"m_GrassStrokeSubIdMask",0); for(int i=0;i<16;i++)pixels[i]=Mathf.FloatToHalf(1);
            dab.Invoke(editor,new object[]{terrain,new Vector3(2,0,2),false});
            pixels=size.GetPixelData<ushort>(0); for(int i=0;i<16;i++)if(pixels[i]!=Mathf.FloatToHalf(1))throw new Exception("Empty selection painted");
            Check(true,"No checked types paints nothing");
            var random=typeof(MGTerrainEditor).GetMethod("RandomGrassSubId",Flags);
            int seen=0;
            for(int i=0;i<128;i++) { byte id=(byte)random.Invoke(null,new object[]{i,i*2,7,10,0}); if(id!=1&&id!=3)throw new Exception("Unchecked type in mix"); seen|=1<<id; if(id!=(byte)random.Invoke(null,new object[]{i,i*2,7,10,0}))throw new Exception("Mix changed across repeated samples"); }
            Check(seen==10,"Mix uses only checked types and remains repeatable");
            UnityEngine.Object.DestroyImmediate(editor);
            File.AppendAllText(Report,"ALL PASSED\n"); EditorApplication.Exit(0);
        }
        catch(Exception error) { File.AppendAllText(Report,"FAIL: "+error+"\n"); Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
