using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
 public sealed class StudioSplineCutValidation:EditorWindow
 {
  const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
  static readonly List<string> log=new List<string>();bool ran;
  static string Out(string file)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+file));
  public static void Begin(){if(!Application.dataPath.Replace('\\','/').EndsWith("/SplineCut/Project/Assets"))throw new Exception("Disposable project only");GetWindow<StudioSplineCutValidation>(true,"Spline cut validation",false).Repaint();}
  static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,F).Invoke(o,args);
  static void Check(bool value,string name){if(!value)throw new Exception(name);log.Add("PASS "+name);}
  static bool Near(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<1e-9f;
  static StudioSpline Curve(bool closed=false){var s=new StudioSpline{kind=StudioSplineKind.Bezier,closed=closed};foreach(var p in new[]{new Vector3(-2,0,0),new Vector3(0,2,1),new Vector3(3,0,0),new Vector3(4,2,-1)})s.knots.Add(new StudioSpline.Knot{position=p,automatic=true});s.RefreshAutomaticTangents();return s;}
  void OnGUI()
  {
   if(ran||Event.current.type!=EventType.Repaint)return;ran=true;
   var doc=CreateInstance<StudioModelDocument>();StudioModelEditor editor=null;
   try
   {
    var s=Curve();var original=s.Copy();var pieces=s.Cut(1,.37f);
    Check(pieces.Length==2&&pieces.All(p=>!p.closed)&&Near(pieces[0].knots.Last().position,pieces[1].knots[0].position),"Open cut creates two pieces sharing only the endpoint position");
    bool exact=true;for(int i=0;i<=50;i++){float t=i/50f;exact&=Near(pieces[0].Evaluate(1,t),s.Evaluate(1,t*.37f))&&Near(pieces[1].Evaluate(0,t),s.Evaluate(1,.37f+t*.63f));}
    Check(exact,"Bezier split retains both halves of the exact original cubic");
    pieces[0].RefreshAutomaticTangents();pieces[1].RefreshAutomaticTangents();
    Check(Near(pieces[0].Evaluate(0,.4f),s.Evaluate(0,.4f))&&Near(pieces[1].Evaluate(1,.6f),s.Evaluate(2,.6f)),"Automatic refresh preserves adjoining spans");
    Check(s.knots.Count==original.knots.Count&&s.knots.Zip(original.knots,(a,b)=>Near(a.outgoing,b.outgoing)).All(x=>x),"Preparing a cut does not mutate the source");
    var atKnot=s.Cut(1,0);atKnot[0].RefreshAutomaticTangents();atKnot[1].RefreshAutomaticTangents();
    Check(atKnot[0].knots.Count==2&&atKnot[1].knots.Count==3&&Near(atKnot[0].Evaluate(0,.5f),s.Evaluate(0,.5f)),"Existing-knot cut keeps endpoint tangents and avoids an extra knot");
    bool rejected=false;try{s.Cut(0,0);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Open endpoint rejects a zero-segment piece");
    var loop=Curve(true);var opened=loop.Cut(3,.4f).Single();opened.RefreshAutomaticTangents();
    exact=!opened.closed&&opened.SegmentCount==loop.SegmentCount+1&&Near(opened.knots[0].position,opened.knots.Last().position);
    for(int i=0;i<=50;i++){float t=i/50f;exact&=Near(opened.Evaluate(0,t),loop.Evaluate(3,.4f+t*.6f))&&Near(opened.Evaluate(4,t),loop.Evaluate(3,t*.4f));}
    Check(exact,"Cutting the closed seam opens one shape-preserving curve with coincident independent endpoints");
    Check(loop.Cut(0,0).Single().knots.Count==loop.knots.Count+1,"Closed existing-knot cut keeps every original span");
    s.kind=StudioSplineKind.Linear;pieces=s.Cut(0,.25f);Check(Near(pieces[0].Evaluate(0,.5f),s.Evaluate(0,.125f)),"Linear cut preserves line shape");
    doc.nodes.Add(new StudioModelDocument.Node{name="Parent",position=new Vector3(4,2,1),rotation=Quaternion.Euler(20,30,10),scale=new Vector3(2,1,3)});
    doc.nodes.Add(new StudioModelDocument.Node{name="Path",parent=0,position=new Vector3(1,2,3),rotation=Quaternion.Euler(10,15,30),scale=new Vector3(1,2,1),spline=Curve()});
    editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(1);Call(editor,"EditSpline");Call(editor,"SetSplineCut");
    string id=doc.nodes[1].constraintId;Call(editor,"CutSplineAtPoint",1,.37f);
    Check(doc.nodes.Count==3&&doc.nodes[1].constraintId==id&&doc.nodes[2].constraintId!=id&&doc.nodes[2].parent==0,"Editor cut retains source identity and creates a distinct sibling");
    var transforms=(Transform[])typeof(StudioModelEditor).GetProperty("Transforms",F).GetValue(editor);
    Check(Near(transforms[1].TransformPoint(doc.nodes[1].spline.knots.Last().position),transforms[2].TransformPoint(doc.nodes[2].spline.knots[0].position)),"Both cut endpoints retain world placement under a rotated scaled parent");
    UnityEditor.Undo.FlushUndoRecordObjects();StudioUndo.IncrementCurrentGroup();StudioUndo.PerformUndo();Check(doc.nodes.Count==2&&doc.nodes[1].spline.knots.Count==4,"One Undo restores source and removes second piece");
    StudioUndo.PerformRedo();Check(doc.nodes.Count==3,"Redo restores both pieces");
    editor.SelectObject(1);Call(editor,"SetSplineCut");Call(editor,"ActivateTool",Tool.Move);Check(!(bool)typeof(StudioModelEditor).GetField("splineCut",F).GetValue(editor),"Ordinary transform switching exits cut placement");
    Check(StudioCommandIcons.Get("Curves/Cut at Point")!=null,"Cut command has a shared cached icon");
   }
   catch(Exception e){log.Add("FAIL "+e);}
   finally{editor?.Dispose();DestroyImmediate(doc);File.WriteAllLines(Out("result.txt"),log);EditorApplication.delayCall+=()=>EditorApplication.Exit(log.Any(x=>x.StartsWith("FAIL"))?1:0);}
  }
 }
}
