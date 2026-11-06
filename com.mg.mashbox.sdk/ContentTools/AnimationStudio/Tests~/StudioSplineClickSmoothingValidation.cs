using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEngine;
namespace MashBoxSDK.AnimationStudio {
 public static class StudioSplineClickSmoothingValidation {
  const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
  static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../../SplineClickSmoothing/"+name));
  [InitializeOnLoadMethod]static void Boot(){if(Application.dataPath.Replace('\\','/').EndsWith("/Splines/SplineProject/Assets")&&File.Exists(Out("request.txt")))EditorApplication.delayCall+=Run;}
  public static void Run(){if(!Application.dataPath.Replace('\\','/').EndsWith("/Splines/SplineProject/Assets"))return;File.Delete(Out("request.txt"));var log=new List<string>();void Check(bool b,string m){if(!b)throw new Exception(m);log.Add("PASS "+m);}var editor=new StudioModelEditor(()=>{});
   try{var type=typeof(StudioModelEditor);var selected=(HashSet<int>)type.GetField("splineKnots",F).GetValue(editor);selected.Add(1);type.GetField("splinePlacementMouse",F).SetValue(editor,new Vector2(100,100));var method=type.GetMethod("UpdateSplinePlacementHandle",F);
    var s=new StudioSpline{kind=StudioSplineKind.Bezier,knots=new List<StudioSpline.Knot>{new StudioSpline.Knot{position=Vector3.left,automatic=true},new StudioSpline.Knot{position=Vector3.zero,automatic=true}}};s.RefreshAutomaticTangents();var before=s.knots[1].incoming;
    method.Invoke(editor,new object[]{s,Matrix4x4.identity,new Vector2(102,101),new Vector3(.01f,.01f,0)});Check(s.knots[1].automatic&&s.knots[1].incoming==before,"Click jitter preserves automatic mode and existing tangent");
    s.knots.Add(new StudioSpline.Knot{position=Vector3.up,automatic=true});s.RefreshAutomaticTangents();Check(s.knots[1].automatic&&s.knots[1].outgoing.sqrMagnitude>.01f&&(s.knots[1].incoming+s.knots[1].outgoing).sqrMagnitude<1e-10f,"Next click preserves a smooth join at the previous knot");
    method.Invoke(editor,new object[]{s,Matrix4x4.identity,new Vector2(110,100),Vector3.zero});Check(s.knots[1].automatic,"Snap target remaining at the knot cannot accidentally collapse automatic handles");
    method.Invoke(editor,new object[]{s,Matrix4x4.identity,new Vector2(110,100),Vector3.up});Check(!s.knots[1].automatic&&s.knots[1].outgoing==Vector3.up&&s.knots[1].incoming==Vector3.down,"Deliberate drag creates mirrored manual handles");
    method.Invoke(editor,new object[]{s,Matrix4x4.identity,new Vector2(101,100),Vector3.up*.01f});Check(!s.knots[1].automatic&&s.knots[1].outgoing==Vector3.up*.01f,"Intentional drag remains latched when returning inside click threshold");
    s.kind=StudioSplineKind.Linear;before=s.knots[1].outgoing;method.Invoke(editor,new object[]{s,Matrix4x4.identity,new Vector2(120,100),Vector3.right});Check(s.knots[1].outgoing==before,"Linear placement does not author hidden Bezier handles");
   }catch(Exception e){log.Add("FAIL "+e);}finally{editor.Dispose();File.WriteAllLines(Out("result.txt"),log);EditorApplication.Exit(log.Any(x=>x.StartsWith("FAIL"))?1:0);}
  }
 }
}
