using System;using System.IO;using System.Linq;using System.Reflection;using UnityEditor;using UnityEngine;using MashBoxSDK.AnimationStudio;
internal static class StudioSliceEndpointValidation
{
 static string Out(string n)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+n));
 [InitializeOnLoadMethod]static void Request(){if(Application.dataPath.EndsWith("SelectionUndoProject/Assets")&&File.Exists(Out("slice-endpoints-request.txt"))){File.Delete(Out("slice-endpoints-request.txt"));EditorApplication.delayCall+=Run;}}
 static object Call(StudioModelEditor e,string name,params object[] a)=>typeof(StudioModelEditor).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(e,a);
 public static void Run(){if(!Application.dataPath.EndsWith("SelectionUndoProject/Assets"))return;StudioModelEditor editor=null;StudioModelDocument doc=null;int checks=0;
 try{
  void Check(bool p,string m){checks++;if(!p)throw new Exception(m);}
  doc=ScriptableObject.CreateInstance<StudioModelDocument>();var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);doc.nodes.Add(new StudioModelDocument.Node{name="Close diagonal",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles,geometryVertices=StudioSubdivisionData.Primitive(mesh),scale=new Vector3(10,.4f,1),rotation=Quaternion.Euler(0,0,45)});
  editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(0);var view=EditorWindow.GetWindow<SceneView>();var camera=view.camera;camera.transform.position=new Vector3(0,0,-3);camera.transform.rotation=Quaternion.identity;camera.orthographic=false;camera.fieldOfView=60;camera.nearClipPlane=.01f;camera.ResetProjectionMatrix();
  Vector2 Project(Vector3 p){var q=camera.WorldToScreenPoint(p);return new Vector2(q.x,camera.pixelHeight-q.y)/EditorGUIUtility.pixelsPerPoint;}
  var a=Project(new Vector3(-.8f,0,0));var b=Project(new Vector3(.8f,0,0));var center=Project(Vector3.zero);var bounds=(Rect)Call(editor,"MultiProjectedBounds",view);
  Check(bounds.Contains(a)&&bounds.Contains(b),"both empty endpoints inside bounds");
  Check(!(bool)Call(editor,"SlicePointerOnMesh",view,a)&&!(bool)Call(editor,"SlicePointerOnMesh",view,b),"off-surface endpoints accepted");
  Check((bool)Call(editor,"SlicePointerOnMesh",view,center),"on-mesh endpoint detected");
  Check((bool)Call(editor,"SliceSegmentCrossesMesh",view,a,b),"finite inside-bounds segment crosses actual surface");
  Check(!(bool)Call(editor,"SliceSegmentCrossesMesh",view,b,Project(new Vector3(1.5f,0,0))),"extension-only crossing rejected");
  Check(bounds.width>camera.pixelWidth/EditorGUIUtility.pixelsPerPoint||bounds.height>camera.pixelHeight/EditorGUIUtility.pixelsPerPoint,"mesh bounds extend outside viewport");
  var field=typeof(StudioModelEditor).GetField("cutTopology",BindingFlags.Instance|BindingFlags.NonPublic);field.SetValue(editor,new StudioMeshCuts(doc.nodes[0]));
  var strokes=(System.Collections.ICollection)Call(editor,"MultiSlice",view,a,b);Check(strokes.Count>0,"Multi-Cut preview accepts same close-up segment");
  File.WriteAllText(Out("slice-endpoints-result.txt"),"PASS "+checks+" checks: close-up diagonal mesh, off-mesh endpoints inside projected bounds, actual mesh hit, finite crossing, extension-only miss, bounds larger than viewport, shared Multi-Cut preview. No physical mouse-event test.");
 }catch(Exception e){File.WriteAllText(Out("slice-endpoints-result.txt"),"FAIL "+checks+" "+e);}finally{editor?.Dispose();if(doc){foreach(var n in doc.nodes)if(n.mesh)UnityEngine.Object.DestroyImmediate(n.mesh);UnityEngine.Object.DestroyImmediate(doc);}}}
}
