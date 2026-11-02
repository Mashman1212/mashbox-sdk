using System;using System.IO;using System.Linq;using System.Reflection;using UnityEditor;using UnityEngine;using MashBoxSDK.AnimationStudio;
internal static class StudioBisectPivotValidation
{
 static string Out(string n)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+n));
 [InitializeOnLoadMethod]static void Request(){if(Application.dataPath.EndsWith("SelectionUndoProject/Assets")&&File.Exists(Out("bisect-pivots-request.txt"))){File.Delete(Out("bisect-pivots-request.txt"));EditorApplication.delayCall+=Run;}}
 static void Set(StudioModelEditor e,string n,object v)=>typeof(StudioModelEditor).GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(e,v);
 public static void Run(){if(!Application.dataPath.EndsWith("SelectionUndoProject/Assets"))throw new InvalidOperationException("Disposable project required");int checks=0;try{foreach(bool fill in new[]{false,true}){
  void Check(bool p,string m){checks++;if(!p)throw new Exception(m);}
  var doc=ScriptableObject.CreateInstance<StudioModelDocument>();var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);var rotation=Quaternion.Euler(10,25,17);var position=new Vector3(2,3,4);var scale=new Vector3(2,3,.7f);
  var source=new StudioModelDocument.Node{name="Pivot cube",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles,geometryVertices=StudioSubdivisionData.Primitive(mesh),rotation=rotation,position=position,scale=scale};doc.nodes.Add(source);
  doc.nodes.Add(new StudioModelDocument.Node{name="Child",parent=0,position=new Vector3(1,2,3)});
  doc.nodes.Add(new StudioModelDocument.Node{name="Shared",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles,geometryVertices=source.geometryVertices});
  var original=mesh.vertices;var editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(0);var childBefore=editor.Transforms[1].position;var normal=rotation*Vector3.right;var center=position+normal*.2f;
  try{
   Set(editor,"bisectPlane",new Plane(normal,center));Set(editor,"bisectPlaced",true);Set(editor,"bisectRepeatedParameters",(Vector4?)new Vector4(.01f,fill?1:0,0,0));typeof(StudioModelEditor).GetMethod("ApplyBisect",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(editor,null);
   Check(doc.nodes.Count==4,"separated half");Check(Vector3.Distance(editor.Transforms[0].position,center-normal*.005f)<1e-5f,"source pivot at cut center");Check(Vector3.Distance(editor.Transforms[3].position,center+normal*.005f)<1e-5f,"new pivot at cut center");
   Check(Vector3.Distance(editor.Transforms[1].position,childBefore)<1e-5f,"child world pose fixed");Check(mesh.vertices.SequenceEqual(original),"shared source unchanged");
   var world=original.Select(v=>position+rotation*Vector3.Scale(scale,v)).ToArray();var result=new[]{0,3}.SelectMany(id=>doc.nodes[id].mesh.vertices.Select(v=>editor.Transforms[id].TransformPoint(v))).ToArray();Check(world.All(p=>result.Any(q=>Vector3.Distance(p,q)<1e-5f)),"original exterior vertices fixed");
   StudioUndo.PerformUndo();Check(doc.nodes.Count==3&&Vector3.Distance(doc.nodes[0].position,position)<1e-5f&&Vector3.Distance(doc.nodes[1].position,new Vector3(1,2,3))<1e-5f,"Undo restores pivots and child");
   StudioUndo.PerformRedo();Check(doc.nodes.Count==4,"Redo separation");Check(Vector3.Distance(doc.nodes[0].position,center-normal*.005f)<1e-5f&&Vector3.Distance(doc.nodes[3].position,center+normal*.005f)<1e-5f,"Redo cut pivots");
  }finally{editor.Dispose();UnityEngine.Object.DestroyImmediate(doc);}
 }File.WriteAllText(Out("bisect-pivots-result.txt"),"PASS "+checks+" checks: rotated/nonuniform cube, off-center 1cm cut, Fill off/on, both pivots on cut centers, fixed exterior/shared geometry and child world position, Undo/Redo.");}catch(Exception e){File.WriteAllText(Out("bisect-pivots-result.txt"),"FAIL "+checks+" "+e);}}
}

