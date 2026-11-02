using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
internal static class StudioBisectSplitValidation
{
 static int checks;
 static string PathFor(string file)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+file));
 static void Check(bool pass,string label){checks++;if(!pass)throw new Exception(label);}
 static object Call(StudioModelEditor e,string name,params object[] args)=>typeof(StudioModelEditor).GetMethods(BindingFlags.Instance|BindingFlags.NonPublic).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(e,args);
 static void Set(StudioModelEditor e,string name,object value)=>typeof(StudioModelEditor).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(e,value);
 [InitializeOnLoadMethod] static void Init(){if(!Application.dataPath.EndsWith("SelectionUndoProject/Assets"))return;var p=PathFor("bisect-split-request.txt");if(File.Exists(p)){File.Delete(p);EditorApplication.delayCall+=Run;}}
 static void Run()
 {
  try {
   foreach(float gap in new[]{0f,.01f})foreach(bool fill in new[]{false,true}) {
    var doc=ScriptableObject.CreateInstance<StudioModelDocument>();var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
    var node=new StudioModelDocument.Node{name="Cube",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles,geometryVertices=StudioSubdivisionData.Primitive(mesh)};
    doc.nodes.Add(node);doc.nodes.Add(new StudioModelDocument.Node{name="Shared",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles,geometryVertices=node.geometryVertices,position=new Vector3(3,0,0)});
    var original=mesh.vertices;var editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(0);
    try {
     Set(editor,"bisectPlane",new Plane(Vector3.right,Vector3.zero));Set(editor,"bisectPlaced",true);Set(editor,"bisectRepeatedParameters",(Vector4?)new Vector4(gap,fill?1:0,0,0));Call(editor,"ApplyBisect");
     Check(doc.nodes.Count==3,"two halves plus shared instance");Check(doc.nodes[0].mesh!=doc.nodes[2].mesh&&doc.nodes[0].mesh!=mesh,"independent meshes");
     Check(mesh.vertices.SequenceEqual(original),"shared source restored");Check((doc.nodes[0].modifiers==null||doc.nodes[0].modifiers.operations.Count==0)&&(doc.nodes[2].modifiers==null||doc.nodes[2].modifiers.operations.Count==0),"independent generated bases");
     foreach(int id in new[]{0,2}) {
      var n=doc.nodes[id];Check(new StudioMeshCuts(n).Faces.Count==(fill?6:5),"half face count");
      Check(n.normalGroups.All(g=>g>=0&&g<n.mesh.vertexCount),"bounded groups");
      var other=doc.nodes[id==0?2:0].mesh.vertices;
      var points=n.mesh.vertices;points[0]+=Vector3.up*.01f;n.mesh.vertices=points;
      Call(editor,"RecalculateGroupedNormals",n.mesh,n.normalGroups);
      Check(doc.nodes[id==0?2:0].mesh.vertices.SequenceEqual(other),"component edit independent");
      // Restore test edit before verifying the Bisect transaction itself.
      points[0]-=Vector3.up*.01f;n.mesh.vertices=points;
     }
     Check(editor.Transforms[0].GetComponent<MeshFilter>().sharedMesh==doc.nodes[0].mesh&&editor.Transforms[2].GetComponent<MeshFilter>().sharedMesh==doc.nodes[2].mesh,"renderers bind separate meshes");
     StudioUndo.PerformUndo();Check(doc.nodes.Count==2&&doc.nodes[0].mesh==mesh&&mesh.vertices.SequenceEqual(original),"Undo restores source and shared mesh");
     StudioUndo.PerformRedo();Check(doc.nodes.Count==3&&doc.nodes[0].mesh!=doc.nodes[2].mesh,"Redo restores separation");
    }finally{editor.Dispose();UnityEngine.Object.DestroyImmediate(doc);}
   }
   File.WriteAllText(PathFor("bisect-split-result.txt"),"PASS "+checks+" checks: cube zero/1cm, fill off/on, independent meshes, shared source preserved, bounded normal IDs and component normal recalculation, bound renderers, Undo/Redo. No physical drag or new cap-layout test.");
  }catch(Exception ex){File.WriteAllText(PathFor("bisect-split-result.txt"),"FAIL after "+checks+" checks: "+ex);}
 }
}

