using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEngine;
namespace MashBoxSDK.AnimationStudio {
 public static class StudioSplineSnapValidation {
  const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
  static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../../SplineSnap/"+name));
  [InitializeOnLoadMethod]static void Boot(){if(Application.dataPath.Replace('\\','/').EndsWith("/Splines/SplineProject/Assets")&&File.Exists(Out("request.txt")))EditorApplication.delayCall+=Run;}
  public static void Run(){if(!Application.dataPath.Replace('\\','/').EndsWith("/Splines/SplineProject/Assets"))return;File.Delete(Out("request.txt"));var log=new List<string>();void Check(bool b,string m){if(!b)throw new Exception(m);log.Add("PASS "+m);}var go=new GameObject("Snap camera");var camera=go.AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);camera.nearClipPlane=.01f;camera.pixelRect=new Rect(0,0,600,500);StudioModelEditor editor=null;StudioModelDocument doc=null;Mesh mesh=null;
   Vector2 Screen(Vector3 p){var s=camera.WorldToScreenPoint(p);return new Vector2(s.x,camera.pixelHeight-s.y);}void Reset()=>typeof(StudioModelEditor).GetField("edgeSnapLatched",F).SetValue(editor,false);
   try{
    var curve=new StudioSpline{kind=StudioSplineKind.Bezier,knots=new List<StudioSpline.Knot>{new StudioSpline.Knot{position=new Vector3(-2,-1,0),outgoing=new Vector3(1,3,2)},new StudioSpline.Knot{position=new Vector3(0,1,2),incoming=new Vector3(-1,-1,0),outgoing=new Vector3(1,0,0)},new StudioSpline.Knot{position=new Vector3(3,0,1),incoming=new Vector3(-1,2,0)}}};
    var expected=curve.Evaluate(0,.37f);Check(StudioSplineSnap.Closest(camera,Screen(expected),1,curve,Matrix4x4.identity,out var hit,out _,out _)&&(hit-expected).sqrMagnitude<1e-6f,"Perspective Bezier snapping returns the actual 3D cubic point: expected="+expected+", got="+hit+", error="+(hit-expected).magnitude+", height="+camera.pixelHeight);
    expected=curve.Evaluate(0,.001f);StudioSplineSnap.Closest(camera,Screen(expected),1,curve,Matrix4x4.identity,out hit,out _,out _);Check((hit-expected).sqrMagnitude<1e-6f,"Refinement includes minima near open endpoints");
    doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.nodes.Add(new StudioModelDocument.Node{name="Curve",spline=curve});editor=new StudioModelEditor(()=>{});editor.Load(doc);
    expected=curve.Evaluate(0,.4f);Check(editor.TryCapturedEdgeOrSplineSnap(camera,Screen(expected),1,out hit)&&(hit-expected).sqrMagnitude<1e-6f,"Shared edge/spline picker captures a spline within the existing radius");
    expected=curve.Evaluate(1,.8f);Check(editor.TryCapturedEdgeOrSplineSnap(camera,Screen(expected),1,out hit)&&(hit-expected).sqrMagnitude<1e-6f,"Captured spline follows across knots without releasing the hold");
    curve.knots[2].position+=Vector3.one*5;Check(editor.TryCapturedEdgeOrSplineSnap(camera,Screen(expected),1,out hit)&&(hit-expected).sqrMagnitude<1e-6f,"Captured world curve is immutable while the source changes");
    Reset();Check(!editor.TryCapturedEdgeOrSplineSnap(camera,new Vector2(-9000,-9000),1,out _),"Released capture restores initial acquisition radius");
    editor.SelectObject(0);Reset();Check(!editor.TryCapturedEdgeOrSplineSnap(camera,Screen(curve.Evaluate(0,.4f)),1,out _),"Moving object cannot snap to its own spline");
    var line=new StudioSpline{kind=StudioSplineKind.Linear,knots=new List<StudioSpline.Knot>{new StudioSpline.Knot{position=Vector3.left},new StudioSpline.Knot{position=Vector3.right}}};camera.orthographic=true;camera.orthographicSize=4;StudioSplineSnap.Closest(camera,Screen(Vector3.right*10),1,line,Matrix4x4.identity,out hit,out _,out _);Check((hit-Vector3.right).sqrMagnitude<1e-10f,"Open linear spline clamps at its finite endpoint");
    line.knots.Add(new StudioSpline.Knot{position=Vector3.up});line.closed=true;expected=line.Evaluate(2,.5f);StudioSplineSnap.Closest(camera,Screen(expected),1,line,Matrix4x4.identity,out hit,out _,out _);Check((hit-expected).sqrMagnitude<1e-8f,"Closed spline includes its seam span");
    doc.nodes[0].spline=line;mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Quad);doc.nodes.Add(new StudioModelDocument.Node{name="Mesh",position=new Vector3(0,-2,0),mesh=mesh});editor.Load(doc);expected=new Vector3(0,-2.5f,0);Reset();Check(editor.TryCapturedEdgeOrSplineSnap(camera,Screen(expected),1,out hit)&&(hit-expected).sqrMagnitude<1e-7f,"Mesh edges still win when nearer than a spline");
   }catch(Exception e){log.Add("FAIL "+e);}finally{editor?.Dispose();if(doc)UnityEngine.Object.DestroyImmediate(doc);if(mesh)UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(go);File.WriteAllLines(Out("result.txt"),log);EditorApplication.Exit(log.Any(x=>x.StartsWith("FAIL"))?1:0);}
  }
 }
}
