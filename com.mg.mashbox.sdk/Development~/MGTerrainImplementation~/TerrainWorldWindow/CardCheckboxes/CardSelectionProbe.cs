using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MashBoxSDK.MapTools;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;
public static class CardSelectionProbe
{
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    const string Report="C:/Users/matth/.codex/visualizations/2026/11/02/01a19ca8-03f2-7823-8496-6dd65058b3ef/CardCheckboxes/result.txt";
    static object Get(object o,string n)=>o.GetType().GetField(n,F).GetValue(o);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,F).SetValue(o,v);
    static object Call(object o,string n,params object[] args)=>o.GetType().GetMethod(n,F).Invoke(o,args);
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);File.AppendAllText(Report,"PASS: "+message+"\n");}
    public static void Run()
    {
        try
        {
            File.WriteAllText(Report,"Multi-detail selection regression\n");
            var sourceTexture=new Texture2D(1,1);AssetDatabase.CreateAsset(sourceTexture,"Assets/Source.asset");
            string guid=AssetDatabase.AssetPathToGUID("Assets/Source.asset");
            var material=new Material(Shader.Find("Hidden/MultiDetailProbe"));AssetDatabase.CreateAsset(material,"Assets/Material.mat");
            var importer=AssetImporter.GetAtPath("Assets/Material.mat");
            importer.userData="MGGrassArrays={\"layers\":[{\"name\":\"Zero\",\"textures\":[\""+guid+"\"]},{\"name\":\"One\",\"textures\":[\""+guid+"\"]},{\"name\":\"Two\",\"textures\":[\""+guid+"\"]}]}";
            importer.SaveAndReimport();
            var mesh=new Mesh{vertices=new[]{Vector3.zero,new Vector3(4,0,0),new Vector3(0,0,4),new Vector3(4,0,4)},triangles=new[]{0,2,1,1,2,3}};mesh.RecalculateBounds();
            var go=new GameObject("Tile");var tile=go.AddComponent<MGTerrain>();go.GetComponent<MeshFilter>().sharedMesh=mesh;
            var densities=new Texture2D[3];var ids=new Texture2D[3];
            using(var data=new SerializedObject(tile))
            {
                var prototypes=data.FindProperty("m_Prototypes");prototypes.arraySize=3;
                var layers=data.FindProperty("m_DensityDetailLayers");layers.arraySize=3;
                for(int i=0;i<3;i++)
                {
                    var proto=prototypes.GetArrayElementAtIndex(i);proto.FindPropertyRelative("m_Mesh").objectReferenceValue=mesh;proto.FindPropertyRelative("m_Material").objectReferenceValue=material;
                    densities[i]=new Texture2D(4,4,TextureFormat.R16,false,true);ids[i]=new Texture2D(4,4,TextureFormat.R8,false,true);
                    var zeroDensity=densities[i].GetPixelData<ushort>(0);var zeroIds=ids[i].GetPixelData<byte>(0);
                    for(int p=0;p<16;p++){zeroDensity[p]=0;zeroIds[p]=0;}densities[i].Apply();ids[i].Apply();
                    var layer=layers.GetArrayElementAtIndex(i);layer.FindPropertyRelative("m_PrototypeIndex").intValue=i;layer.FindPropertyRelative("m_UseGrassArray").boolValue=true;
                    layer.FindPropertyRelative("m_DensityMap").objectReferenceValue=densities[i];layer.FindPropertyRelative("m_GrassIdMap").objectReferenceValue=ids[i];
                    layer.FindPropertyRelative("m_MinWidth").floatValue=i+1;layer.FindPropertyRelative("m_MaxWidth").floatValue=i+2;
                    layer.FindPropertyRelative("m_SizeMultiplier").floatValue=1;layer.FindPropertyRelative("m_WidthMultiplier").floatValue=1;layer.FindPropertyRelative("m_HeightMultiplier").floatValue=1;
                }
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            var editor=(MGTerrainEditor)UnityEditor.Editor.CreateEditor(tile,typeof(MGTerrainEditor));
            var originalWidths=new float[3];for(int i=0;i<3;i++)originalWidths[i]=tile.DensityDetailLayers[i].MinWidth;
            var copies=(HashSet<Texture2D>)Get(editor,"m_PaintCopies");foreach(var map in densities)copies.Add(map);foreach(var map in ids)copies.Add(map);
            Call(editor,"SelectDetailPrototype",0,false,false);Call(editor,"SelectDetailPrototype",1,true,false);
            var selected=(HashSet<int>)Get(editor,"m_SelectedDetailPrototypes");Check(selected.SetEquals(new[]{0,1}),"Ctrl-click selects both prototype layers");
            var masks=(Dictionary<(int,int),int>)Get(editor,"m_DetailSubIdSelections");masks[(0,0)]=2;masks[(1,0)]=4;
            Call(editor,"RefreshDetailPaintTargets",tile);Check(((ICollection<MGTerrain.DensityDetailLayer>)Get(editor,"m_SelectedPaintLayers")).Count==2,"Both selected layers remain paint targets after refresh");
            Set(editor,"m_PaintChannel",0);Set(editor,"m_PaintDensity",3f);Set(editor,"m_RandomGrassSubIds",true);
            var state=typeof(MGTerrainEditor).Assembly.GetType("MashBoxSDK.MapTools.MBEditorToolState");state.GetProperty("BrushRadius",F).SetValue(null,100f);state.GetProperty("BrushStrength",F).SetValue(null,1f);
            Call(editor,"BeginMultiDetailStroke");Call(editor,"PaintSelectedDetailDab",tile,new Vector3(2,0,2),false,false);
            for(int layer=0;layer<2;layer++)
            {
                var density=tile.DensityDetailLayers[layer].DensityMap.GetPixelData<ushort>(0);var pixels=tile.DensityDetailLayers[layer].GrassIdMap.GetPixelData<byte>(0);
                for(int i=0;i<16;i++)if(density[i]==0||pixels[i]!=layer+1)throw new Exception("Paint did not preserve separate per-layer masks");
            }
            Check(true,"One stroke paints both layers using each layer's own checked Sub-ID");
            var untouched=tile.DensityDetailLayers[2].DensityMap.GetPixelData<ushort>(0);for(int i=0;i<16;i++)if(untouched[i]!=0)throw new Exception("Unselected layer painted");
            Check(true,"Unselected layer remains unchanged");Call(editor,"FinishDetailStroke");
            Call(editor,"SelectDetailPrototype",0,false,false);Call(editor,"SelectDetailPrototype",2,false,true);Check(selected.SetEquals(new[]{0,1,2}),"Shift-click selects the prototype range");
            Call(editor,"SelectDetailPrototype",1,true,false);Check(selected.SetEquals(new[]{0,2}),"Ctrl-click toggles a selected prototype off");
            for(int i=0;i<3;i++)if(!Mathf.Approximately(tile.DensityDetailLayers[i].MinWidth,originalWidths[i]))throw new Exception("Selection changed per-layer settings: "+i+" from "+originalWidths[i]+" to "+tile.DensityDetailLayers[i].MinWidth);
            Check(true,"Different per-layer width settings are preserved");
            Call(editor,"SetDetailSettingsTarget",tile,0);
            Call(editor,"SelectDetailSubIds",tile,0,false,false);Check((int)Get(editor,"m_GrassSubIdMask")==1,"Plain Sub-ID click selects only that type");
            Call(editor,"SelectDetailSubIds",tile,1,true,false);Check((int)Get(editor,"m_GrassSubIdMask")==3,"Ctrl-click or checkbox adds a Sub-ID");
            Call(editor,"SelectDetailSubIds",tile,0,true,false);Check((int)Get(editor,"m_GrassSubIdMask")==2,"Ctrl-click or checkbox removes a Sub-ID");
            Call(editor,"SelectDetailSubIds",tile,2,false,false);Call(editor,"SelectDetailSubIds",tile,0,false,true);Check((int)Get(editor,"m_GrassSubIdMask")==7,"Shift-click selects a Sub-ID range");
            Call(editor,"SelectDetailSubIds",tile,2,true,false);Check((int)Get(editor,"m_GrassSubIdMask")==3,"Checkbox toggles a range member independently");
            Call(editor,"SetDetailSettingsTarget",tile,2);Call(editor,"SelectDetailSubIds",tile,2,false,false);
            Call(editor,"SetDetailSettingsTarget",tile,0);Check((int)Get(editor,"m_GrassSubIdMask")==3,"Sub-ID choices remain independent between prototype rows");
            Check(selected.SetEquals(new[]{0,2}),"Editing Sub-ID selections keeps both prototype layers selected");
            UnityEngine.Object.DestroyImmediate(editor);File.AppendAllText(Report,"ALL PASSED\n");EditorApplication.Exit(0);
        }
        catch(Exception error){File.AppendAllText(Report,"FAIL: "+error+"\n");Debug.LogException(error);EditorApplication.Exit(1);}
    }
}
