using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;using System.Diagnostics;using System.Threading;using UnityEditor;using UnityEngine;using Object=UnityEngine.Object;
namespace MashBoxSDK.AnimationStudio {
internal static class MaterialPerformanceValidation {
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 static string Out(string suffix)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../material-performance-"+suffix));
 static FileSystemWatcher bridge;
 [InitializeOnLoadMethod]static void Boot(){if(!Application.dataPath.EndsWith("ThemeIsolationProject/Assets"))return;var context=SynchronizationContext.Current;bridge=new FileSystemWatcher(Path.GetDirectoryName(Out("refresh.txt")),"material-performance-refresh.txt"){NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite};bridge.Created+=(s,e)=>context.Post(_=>{if(File.Exists(Out("refresh.txt"))){File.Delete(Out("refresh.txt"));AssetDatabase.Refresh();}},null);bridge.EnableRaisingEvents=true;AssemblyReloadEvents.beforeAssemblyReload+=()=>bridge.Dispose();if(File.Exists(Out("request.txt")))EditorApplication.delayCall+=Run;}
 public static void Run(){if(!Application.dataPath.EndsWith("ThemeIsolationProject/Assets"))return;string label=File.ReadAllText(Out("request.txt")).Trim();File.Delete(Out("request.txt"));var log=new List<string>();StudioModelEditor editor=null;StudioModelDocument doc=null;
 try{
  var importer=(ModelImporter)AssetImporter.GetAtPath("Assets/fighterJetCutTest.fbx");if(!importer.isReadable||importer.materialImportMode!=ModelImporterMaterialImportMode.None){importer.isReadable=true;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.SaveAndReimport();}
  var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/fighterJetCutTest.fbx");var source=model.GetComponentsInChildren<MeshFilter>(true).Select(x=>x.sharedMesh).Where(m=>m).OrderByDescending(m=>m.vertexCount).First();
  doc=ScriptableObject.CreateInstance<StudioModelDocument>();for(int i=0;i<6;i++){var node=new StudioModelDocument.Node{name="fighterJetCutTest "+i,mesh=Object.Instantiate(source),position=new Vector3(i*10,0,0)};var topo=new StudioMeshCuts(node);node.cutPolygons=topo.Faces.SelectMany(f=>new[]{f.Submesh,f.Vertices.Count}.Concat(f.Vertices)).ToArray();node.cutTriangles=node.mesh.triangles;node.geometryVertices=StudioSubdivisionData.Geometry(node);doc.nodes.Add(node);}
  editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(0);int slot=doc.nodes[0].mesh.subMeshCount;var vertices=doc.nodes[0].mesh.vertices;var normals=doc.nodes[0].mesh.normals;var uv=doc.nodes[0].mesh.uv;
  log.Add("vertices-per-mesh="+source.vertexCount+" scene-vertices="+(source.vertexCount*6)+" faces-per-mesh="+new StudioMeshCuts(doc.nodes[0]).Faces.Count);
  void Time(string name,int target){var watch=Stopwatch.StartNew();typeof(StudioModelEditor).GetMethod("AssignSelectionMaterialSlot",F).Invoke(editor,new object[]{target});watch.Stop();log.Add(name+"="+watch.Elapsed.TotalMilliseconds.ToString("F2")+" ms");}
  Time("one-object-first",slot);Time("one-object-warm",0);
  editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);var faces=(List<StudioMeshCuts.Face>)typeof(StudioModelEditor).GetMethod("Faces",F).Invoke(editor,null);editor.Components.UnionWith(Enumerable.Range(0,Math.Min(200,faces.Count)));Time("200-faces-first",slot);Time("200-faces-warm",0);
  editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Object);var selected=(HashSet<int>)typeof(StudioModelEditor).GetField("objects",F).GetValue(editor);selected.UnionWith(Enumerable.Range(0,6));Time("six-objects-first",slot);Time("six-objects-warm",0);
  selected.Clear();selected.Add(0);Time("one-object-with-six-history-bases",slot);
  if(!vertices.SequenceEqual(doc.nodes[0].mesh.vertices)||!normals.SequenceEqual(doc.nodes[0].mesh.normals)||!uv.SequenceEqual(doc.nodes[0].mesh.uv))throw new Exception("Vertex streams changed");log.Add("PASS vertex streams unchanged; six meshes assigned");
  StudioMaterialSlotValidation.Run();
 }catch(Exception e){log.Add("FAIL "+e);}finally{editor?.Dispose();if(doc){foreach(var n in doc.nodes)Object.DestroyImmediate(n.mesh);Object.DestroyImmediate(doc);}File.WriteAllLines(Out(label+".txt"),log);}
 }
}
}
