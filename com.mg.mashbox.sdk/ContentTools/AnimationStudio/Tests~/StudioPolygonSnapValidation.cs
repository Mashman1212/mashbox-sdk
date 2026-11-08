using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEngine;
namespace MashBoxSDK.AnimationStudio {
 public sealed class StudioPolygonSnapValidation:EditorWindow {
  const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;static StudioPolygonSnapValidation host;static readonly List<string> log=new List<string>();bool ran;
  static string Out(string n)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../PolygonSnap/"+n));
  [InitializeOnLoadMethod]static void Boot(){if(Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets")&&File.Exists(Out("request.txt")))EditorApplication.update+=Begin;}
  static void Begin(){EditorApplication.update-=Begin;File.Delete(Out("request.txt"));host=GetWindow<StudioPolygonSnapValidation>(true,"Polygon snap check",false);host.ran=false;log.Clear();host.position=new Rect(50,50,512,512);host.Repaint();}
  static void Check(bool b,string s){if(!b)throw new Exception(s);log.Add("PASS "+s);}
  static void Set(StudioModelEditor e,string n,object v)=>typeof(StudioModelEditor).GetField(n,F).SetValue(e,v);
  static object Call(StudioModelEditor e,string n,params object[] a)=>typeof(StudioModelEditor).GetMethod(n,F).Invoke(e,a);
  void OnGUI(){if(ran||Event.current.type!=EventType.Repaint)return;ran=true;var original=Event.current;var doc=CreateInstance<StudioModelDocument>();var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Quad);doc.nodes.Add(new StudioModelDocument.Node{name="Existing selected quad",mesh=mesh});var editor=new StudioModelEditor(()=>{});editor.Load(doc);var view=CreateInstance<SceneView>();
   try{
    view.camera.transform.SetPositionAndRotation(new Vector3(0,0,-5),Quaternion.identity);view.camera.nearClipPlane=.01f;view.camera.farClipPlane=100;view.camera.pixelRect=new Rect(0,0,512,512);Handles.SetCamera(new Rect(0,0,512,512),view.camera);
    editor.SelectObject(0);Set(editor,"cutTool",StudioModelEditor.CutTool.Polygon);Set(editor,"polygonPlane",new Plane(Vector3.back,new Vector3(0,0,2)));
    Vector3 target=editor.Transforms[0].TransformPoint(mesh.vertices[0]);var mouse=HandleUtility.WorldToGUIPoint(target);var before=mesh.vertices;
    Event.current=new Event{type=EventType.KeyDown,keyCode=KeyCode.V};Call(editor,"HandlePivotSnap",view);Check((bool)typeof(StudioModelEditor).GetField("pivotSnapHeld",F).GetValue(editor),"Configured vertex hold is enabled in Polygon mode");
    object[] args={view,mouse,Vector3.zero};Check((bool)Call(editor,"TryPolygonPoint",args)&&((Vector3)args[2]-target).sqrMagnitude<1e-8f,"Vertex snap reaches selected source mesh at exact world depth, outside placement plane");
    Event.current=new Event{type=EventType.MouseDown,button=0,mousePosition=mouse};Call(editor,"PolygonSceneGUI",view);var placed=(List<Vector3>)typeof(StudioModelEditor).GetField("polygonPoints",F).GetValue(editor);Check(placed.Count==1&&(placed[0]-target).sqrMagnitude<1e-8f,"Mouse placement commits the exact snapped preview target");
    Set(editor,"gridSnapHeld",true);args=new object[]{view,mouse,Vector3.zero};Check((bool)Call(editor,"TryPolygonPoint",args)&&((Vector3)args[2]-target).sqrMagnitude<1e-8f,"Vertex snapping retains precedence over held grid");
    args=new object[]{view,new Vector2(-2000,-2000),Vector3.zero};Check(!(bool)Call(editor,"TryPolygonPoint",args),"Missing held vertex target does not place an unsnapped point");
    Event.current=new Event{type=EventType.KeyUp,keyCode=KeyCode.V};Call(editor,"HandlePivotSnap",view);Check(!(bool)typeof(StudioModelEditor).GetField("pivotSnapHeld",F).GetValue(editor),"Vertex release clears held state");
    Event.current=new Event{type=EventType.KeyDown,keyCode=KeyCode.C};Call(editor,"HandlePivotSnap",view);var a=editor.Transforms[0].TransformPoint(mesh.vertices[0]);var b=editor.Transforms[0].TransformPoint(mesh.vertices[1]);var middle=(a+b)*.5f;
    args=new object[]{view,HandleUtility.WorldToGUIPoint(middle),Vector3.zero};Check((bool)Call(editor,"TryPolygonPoint",args)&&((Vector3)args[2]-middle).sqrMagnitude<1e-7f,"Finite edge snap includes selected source mesh");
    args=new object[]{view,HandleUtility.WorldToGUIPoint(b+(b-a)*4),Vector3.zero};Check((bool)Call(editor,"TryPolygonPoint",args)&&((Vector3)args[2]-b).sqrMagnitude<1e-7f,"Captured edge clamps to endpoint instead of continuing");
    Call(editor,"StopCutTool");Check(!(bool)typeof(StudioModelEditor).GetField("pivotSnapHeld",F).GetValue(editor)&&!(bool)typeof(StudioModelEditor).GetField("gridSnapHeld",F).GetValue(editor),"Tool exit clears snap holds and edge capture");Check(mesh.vertices.SequenceEqual(before),"Snapping never moves existing selected mesh");
    var free=new Vector3(.2f,.3f,2);args=new object[]{view,HandleUtility.WorldToGUIPoint(free),Vector3.zero};Check((bool)Call(editor,"TryPolygonPoint",args)&&((Vector3)args[2]-free).sqrMagnitude<1e-7f,"Unsnapped placement and snap picker share native GUI ray coordinates");
    foreach(var frame in new[]{Quaternion.identity,Quaternion.Euler(90,0,0),Quaternion.Euler(0,0,90)}){var p=frame*new Vector3(.26f,0,.74f);var normal=frame*Vector3.up;Check(StudioModelEditor.TrySnapFreeMoveToGrid(new Ray(p+normal*4,-normal),.25f,frame,out var snapped)&&(snapped-frame*new Vector3(.25f,0,.75f)).sqrMagnitude<1e-8f,"Grid snapping matches perspective/top, front or side grid plane");}
   }catch(Exception e){log.Add("FAIL "+e);}finally{Event.current=original;editor.Dispose();DestroyImmediate(view);DestroyImmediate(mesh);DestroyImmediate(doc);File.WriteAllLines(Out("result.txt"),log);EditorApplication.delayCall+=()=>{if(host)host.Close();};}
  }
 }
}
