using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MashBoxSDK.MapTools;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;
public static class DetailWorkspaceProbe
{
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    const string Report="../validation-result.txt";
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
            File.WriteAllText("Assets/Probe.shader", "Shader \"Hidden/MultiDetailProbe\" { Properties { _GrassUseArrays(\"Arrays\", Float)=1 } SubShader { Pass {} } }");
            AssetDatabase.ImportAsset("Assets/Probe.shader",ImportAssetOptions.ForceSynchronousImport);
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
            Call(editor,"SelectDetailPrototype",0,false,false);Call(editor,"SelectDetailPrototype",1,true,false);
            var masks=(Dictionary<(int,int),int>)Get(editor,"m_DetailSubIdSelections");
            masks[(0,0)]=1; masks[(1,0)]=4; masks[(0,1)]=2;
            Set(editor,"m_SelectedPrototype",0); Set(editor,"m_GrassPaintPopulation",0);
            Set(editor,"m_SubIdSelectionPrototype",0); Set(editor,"m_SubIdSelectionPopulation",0);
            Set(editor,"m_GrassSubIdMask",4);
            Call(editor,"SyncDetailSubIdSelection",tile);
            Check(masks[(0,0)]==1 && masks[(1,0)]==4 && (int)Get(editor,"m_GrassSubIdMask")==1,"Restoring active row does not copy last row selection");
            Set(editor,"m_GrassSubIdMask",4); Call(editor,"SelectDetailSubIds",tile,1,true,false);
            Check(masks[(0,0)]==3 && masks[(1,0)]==4,"Checkbox toggles only clicked row, even with stale current mask");
            Set(editor,"m_SelectedPrototype",1); Call(editor,"SelectDetailSubIds",tile,0,false,false);
            Check(masks[(0,0)]==3 && masks[(1,0)]==1,"Plain click replaces only clicked row selection");
            Set(editor,"m_SelectedPrototype",0); Set(editor,"m_GrassSubIdMask",1); Call(editor,"SyncDetailSubIdSelection",tile);
            Check((int)Get(editor,"m_GrassSubIdMask")==3 && masks[(0,1)]==2,"Row selection survives repeated synchronization and population remains independent");
            var selected=(HashSet<int>)Get(editor,"m_SelectedDetailPrototypes"); Check(selected.SetEquals(new[]{0,1}),"Both prototype layers stay selected");
            UnityEngine.Object.DestroyImmediate(editor);File.AppendAllText(Report,"ALL PASSED\n");EditorApplication.Exit(0);
        }
        catch(Exception error){File.AppendAllText(Report,"FAIL: "+error+"\n");Debug.LogException(error);EditorApplication.Exit(1);}
    }
}
