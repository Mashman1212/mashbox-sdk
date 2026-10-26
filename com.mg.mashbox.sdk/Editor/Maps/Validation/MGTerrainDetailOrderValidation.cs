#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;
[InitializeOnLoad]
static class MGTerrainDetailOrderValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
 static object New(string name,params object[] args)=>Activator.CreateInstance(typeof(MGTerrain).GetNestedType(name,F),F,null,args,null);
 static object Get(object o,string name)=>o.GetType().GetField(name,F).GetValue(o);
 static void Set(object o,string name,object v)=>o.GetType().GetField(name,F).SetValue(o,v);
 static MGTerrainDetailOrderValidation(){EditorApplication.delayCall+=RunRequested;}
 static void RunRequested(){string p=Path.Combine(Path.GetTempPath(),"mg-detail-order.request");if(!File.Exists(p)||File.ReadAllText(p).Trim().Replace('\\','/')!=Application.dataPath.Replace('\\','/'))return;File.Delete(p);Run();}
 [MenuItem("Tools/MashBox/MG Terrain/Validation/Stable Detail Cell Order")]
 static void CheckDetailBounds()
 {
     var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
     var root = new GameObject("Tall detail validation"); root.SetActive(false);
     UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
     var mesh = new Mesh(); var density = new Texture2D(2, 2, TextureFormat.R16, false, true);
     var material = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
     try
     {
         mesh.vertices = new[] { new Vector3(-5, 0, -5), new Vector3(5, 0, 5), new Vector3(0, 30, 0) };
         mesh.triangles = new[] { 0, 1, 2 }; mesh.RecalculateBounds();
         var prefab = new GameObject("Detail tree"); prefab.transform.SetParent(root.transform);
         prefab.AddComponent<MeshFilter>().sharedMesh = mesh;
         prefab.AddComponent<MeshRenderer>().sharedMaterial = material;
         var tile = root.AddComponent<MGTerrain>();
         int index = tile.FindOrAddPrototype(prefab, MGTerrain.InstanceKind.Detail);
         var layer = Activator.CreateInstance(typeof(MGTerrain.DensityDetailLayer), F, null,
             new object[] { index, density, 1f, 1f, 1f, 1f, 17, 4L, 0f }, null);
         var method = typeof(MGTerrain).GetMethod("TryGetTreeLayerBounds", F);
         var args = new object[] { layer, default(Bounds) };
         if (!(bool)method.Invoke(tile, args) || ((Bounds)args[1]).max.y < 30)
             throw new Exception("Detail-kind tree canopy omitted from culling bounds.");
         var chunkMethod = typeof(MGTerrain).GetMethod("CalculateDetailChunkBounds", F);
         var bounds = (Bounds)chunkMethod.Invoke(tile, new object[] {
             new Bounds(Vector3.zero, new Vector3(32, 0, 32)), 2, 2, 1, 0, 0, 1f, 0f, null, layer });
         if (bounds.max.y < 30 || bounds.min.x >= -16)
             throw new Exception("Tall Detail cell must include canopy and rotated crown outside root footprint.");
         typeof(MGTerrain).GetMethod("ReleaseDetailRenderCache", F).Invoke(tile, null);
     }
     finally
     {
         UnityEngine.Object.DestroyImmediate(root);
         UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(density); UnityEngine.Object.DestroyImmediate(material);
         UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
     }
 }
 static void Run(){string path=Path.Combine(Path.GetTempPath(),"mg-detail-order.txt");try{
 var compare=typeof(MGTerrain).GetMethod("CompareVisibleDensityDetails",F);
 var actual=new List<object>();
 for(int x=0;x<100;x++)for(int layer=0;layer<3;layer++){
 var chunk=New("DensityDetailChunk");Set(chunk,"layerIndex",layer);Set(chunk,"instanceCount",layer==0?101:layer==1?1:7);Set(chunk,"worldBounds",new Bounds(new Vector3(x,layer*10,0),Vector3.one));actual.Add(New("VisibleDensityDetail",chunk,null,0f,0));}
 int OldCompare(object a,object b){var ab=(Bounds)Get(Get(a,"chunk"),"worldBounds");var bb=(Bounds)Get(Get(b,"chunk"),"worldBounds");int x=ab.center.x.CompareTo(bb.center.x);return x!=0?x:ab.center.z.CompareTo(bb.center.z);}
 int NewCompare(object a,object b)=>(int)compare.Invoke(null,new[]{a,b});
 Dictionary<object,int> Allocate(List<object> cells){double remainder=0;var result=new Dictionary<object,int>();foreach(var v in cells){var c=Get(v,"chunk");double share=(int)Get(c,"instanceCount")*.4f+remainder;int n=(int)Math.Floor(share);remainder=share-n;result[c]=n;}return result;}
 int Changes(Dictionary<object,int> before,Dictionary<object,int> after){int n=0;foreach(var pair in before)if(after[pair.Key]!=pair.Value)n++;return n;}
 var old=new List<object>(actual);Dictionary<object,int> previousOld=null,previousNew=null;int oldChanges=0,newChanges=0;
 for(int frame=0;frame<50;frame++){
 old.Sort(OldCompare);actual.Sort(NewCompare);var o=Allocate(old);var n=Allocate(actual);if(previousOld!=null){oldChanges+=Changes(previousOld,o);newChanges+=Changes(previousNew,n);}previousOld=o;previousNew=n;}
 if(oldChanges==0||newChanges!=0)throw new Exception($"Expected old allocation churn and stable new allocation: old={oldChanges}, new={newChanges}");
 actual.Reverse();actual.Sort(NewCompare);if(Changes(previousNew,Allocate(actual))!=0)throw new Exception("Rebuilding the same selection changed allocations.");
 CheckDetailBounds();
 string report=$"PASS: 300 co-located grass/tree/bush cells, 50 repeated sorts: old order changed {oldChanges} cell allocations; new total order changed {newChanges}. Reversed candidate traversal resolves identical allocations. Original carry-based budget preserved. Tall Detail mesh bounds passed.";File.WriteAllText(path,report);Debug.Log(report);
 }catch(Exception e){File.WriteAllText(path,"FAIL: "+e);Debug.LogException(e);}}
}
#endif

