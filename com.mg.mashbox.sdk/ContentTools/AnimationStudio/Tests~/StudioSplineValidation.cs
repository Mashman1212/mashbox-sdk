using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    public sealed class StudioSplineValidation : EditorWindow
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static readonly List<string> log=new List<string>(); bool ran;
        static string Out(string file)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+file));
        [InitializeOnLoadMethod] static void Boot(){if(Application.dataPath.Replace('\\','/').EndsWith("/SplineProject/Assets")&&File.Exists(Out("request.txt")))EditorApplication.delayCall+=Begin;}
        static void Begin(){File.Delete(Out("request.txt"));log.Clear();var w=GetWindow<StudioSplineValidation>(true,"Spline validation",false);w.ran=false;w.position=new Rect(60,60,600,500);w.Repaint();}
        static void Check(bool b,string message){if(!b)throw new Exception(message);log.Add("PASS "+message);}
        static object Call(object value,string name,params object[] args)=>value.GetType().GetMethod(name,F).Invoke(value,args);
        static void Set(object value,string name,object v)=>value.GetType().GetField(name,F).SetValue(value,v);
        static T Get<T>(object value,string name)=>(T)value.GetType().GetField(name,F).GetValue(value);
        static bool Near(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<1e-9f;
        static StudioSpline Curve()=>new StudioSpline{kind=StudioSplineKind.Bezier,knots=new List<StudioSpline.Knot>{new StudioSpline.Knot{position=new Vector3(-2,-1,0),outgoing=new Vector3(1,3,0)},new StudioSpline.Knot{position=new Vector3(2,1,0),incoming=new Vector3(-1,-3,0)},new StudioSpline.Knot{position=new Vector3(3,-1,1),incoming=Vector3.up}}};
        void OnGUI()
        {
            if(ran||Event.current.type!=EventType.Repaint)return;ran=true;
            var original=Event.current;StudioModelEditor editor=null;StudioModelDocument doc=null;SceneView view=null;
            try {
                var source=Curve();var split=source.Copy();split.Insert(0,.37f);
                Check(Enumerable.Range(0,101).All(i=>{float t=i/100f;return Near(source.Evaluate(0,t),t<=.37f?split.Evaluate(0,t/.37f):split.Evaluate(1,(t-.37f)/.63f));}),"Bezier insertion exactly preserves both subdivided spans");
                Check(Enumerable.Range(0,21).All(i=>Near(source.Evaluate(1,i/20f),split.Evaluate(2,i/20f))),"Insertion preserves the adjacent segment");
                var reverse=source.Copy();reverse.Reverse();Check(Enumerable.Range(0,21).All(i=>Near(source.Evaluate(0,i/20f),reverse.Evaluate(1,1-i/20f))),"Reverse swaps tangent sides and preserves curve shape");
                var closed=source.Copy();closed.closed=true;closed.Insert(2,.5f);closed.Validate();Check(closed.SegmentCount==4,"Closed-loop seam supports insertion");
                var linear=source.Copy();linear.SetKind(StudioSplineKind.Linear);var points=Enumerable.Range(0,21).Select(i=>linear.Evaluate(0,i/20f)).ToArray();linear.SetKind(StudioSplineKind.Bezier);Check(linear.knots.All(k=>k.automatic)&&linear.knots[1].tangent==StudioSplineTangent.Aligned,"Linear-to-Bezier conversion creates automatic smooth tangents");
                var handle=source.Copy();handle.knots[1].tangent=StudioSplineTangent.Mirrored;handle.SetHandle(1,false,new Vector3(2,1,0));Check(Near(handle.knots[1].incoming,-handle.knots[1].outgoing),"Mirrored handles stay equal and opposite");
                handle.knots[1].tangent=StudioSplineTangent.Aligned;float length=handle.knots[1].incoming.magnitude;handle.SetHandle(1,false,Vector3.up*4);Check(Mathf.Abs(handle.knots[1].incoming.magnitude-length)<1e-5f&&Near(handle.knots[1].incoming,Vector3.down*length),"Aligned handles preserve opposite length");
                handle.knots[1].tangent=StudioSplineTangent.Corner;var incoming=handle.knots[1].incoming;handle.SetHandle(1,false,Vector3.right);Check(Near(incoming,handle.knots[1].incoming),"Corner handles remain independent");
                bool rejected=false;try{source.Copy().Remove(new[]{0,1});}catch(InvalidOperationException){rejected=true;}Check(rejected,"Removal rejects too few remaining knots atomically");
                doc=CreateInstance<StudioModelDocument>();doc.SetWorkspacePath(Path.GetFullPath(Path.Combine(Application.dataPath,"../MashBoxStudio/Workspaces/spline-validation.workspace")));
                StudioUndo.IncrementCurrentGroup();int index=doc.AddSpline(source);UnityEditor.Undo.FlushUndoRecordObjects();StudioUndo.IncrementCurrentGroup();StudioUndo.PerformUndo();Check(doc.nodes.Count==0,"Native StudioUndo removes spline creation");StudioUndo.PerformRedo();Check(doc.nodes.Count==1&&doc.nodes[0].spline.knots.Count==3,"Native StudioRedo restores spline knots and tangents");
                var path=Path.GetFullPath(Path.Combine(Application.dataPath,"../MashBoxStudio/Workspaces/roundtrip.workspace"));Directory.CreateDirectory(Path.GetDirectoryName(path));if(File.Exists(path))File.Delete(path);StudioWorkspaceText.Write(doc,path);var restored=StudioWorkspaceText.Read(path);try{Check(JsonUtility.ToJson(restored.nodes[0].spline)==JsonUtility.ToJson(doc.nodes[0].spline),"Workspace JSON round-trip retains spline type, closure, positions and handles");}finally{DestroyImmediate(restored);}
                var copies=doc.DuplicateObjects(new[]{0});doc.nodes[copies[0]].spline.knots[0].position+=Vector3.one;Check(!Near(doc.nodes[0].spline.knots[0].position,doc.nodes[copies[0]].spline.knots[0].position),"Object duplication owns an independent spline copy");
                editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(0);Call(editor,"EditSpline");view=CreateInstance<SceneView>();view.camera.transform.SetPositionAndRotation(new Vector3(0,0,-10),Quaternion.identity);view.camera.nearClipPlane=.01f;view.camera.farClipPlane=100;view.camera.pixelRect=new Rect(0,0,600,500);Handles.SetCamera(new Rect(0,0,600,500),view.camera);
                var knots=Get<HashSet<int>>(editor,"splineKnots");knots.Add(0);Set(editor,"tool",Tool.None);var world=editor.Transforms[0].TransformPoint(doc.nodes[0].spline.knots[0].position);var mouse=HandleUtility.WorldToGUIPoint(world);
                Event.current=new Event{type=EventType.MouseDown,button=0,mousePosition=mouse};Call(editor,"SplineSceneGUI",view);Check(GUIUtility.hotControl!=0,"Knot pointer-down captures native input");
                var destination=world+new Vector3(.5f,.25f,0);Event.current=new Event{type=EventType.MouseDrag,button=0,mousePosition=HandleUtility.WorldToGUIPoint(destination)};Call(editor,"SplineSceneGUI",view);Event.current=new Event{type=EventType.MouseUp,button=0,mousePosition=HandleUtility.WorldToGUIPoint(destination)};Call(editor,"SplineSceneGUI",view);Check(Near(editor.Transforms[0].TransformPoint(doc.nodes[0].spline.knots[0].position),destination)&&GUIUtility.hotControl==0,"Native GUI drag moves the knot continuously and releases capture");
                UnityEditor.Undo.FlushUndoRecordObjects();StudioUndo.IncrementCurrentGroup();StudioUndo.PerformUndo();Check(Near(editor.Transforms[0].TransformPoint(doc.nodes[0].spline.knots[0].position),world),"One Undo restores the whole knot drag");StudioUndo.PerformRedo();Check(Near(editor.Transforms[0].TransformPoint(doc.nodes[0].spline.knots[0].position),destination),"Redo restores the knot drag");
                editor.SelectObject(0);Call(editor,"EditSpline");Set(editor,"tool",Tool.None);knots=Get<HashSet<int>>(editor,"splineKnots");knots.Add(1);var k=doc.nodes[0].spline.knots[1];var tip=editor.Transforms[0].TransformPoint(k.position+k.incoming);Event.current=new Event{type=EventType.MouseDown,button=0,mousePosition=HandleUtility.WorldToGUIPoint(tip)};Call(editor,"SplineSceneGUI",view);Check(Get<int>(editor,"splineHandle")==-1,"Tangent endpoint is selected separately from its knot");Event.current=new Event{type=EventType.KeyDown,keyCode=KeyCode.Escape};Call(editor,"SplineKey",Event.current);Check(GUIUtility.hotControl==0,"Escape rolls back an active handle gesture and releases capture");
                var commands=editor.ToolbarCommands(null);Check(commands.Any(c=>c.Key=="Curves/Insert Knot")&&commands.Any(c=>c.Key=="Curves/Mirrored Tangents"),"Curves menu actions enter shared toolbar/customization catalog");
                Check(new StudioCurvesToolbar().CreatePanelContent().childCount>=16,"Curves overlay includes every companion tool");
                foreach(var command in commands.Where(c=>c.Key.StartsWith("Curves/")))Check(StudioCommandIcons.Get(command.Key)!=null,"Icon: "+command.Label);
                Set(editor,"splineDraft",new StudioSpline{kind=StudioSplineKind.Bezier});Set(editor,"splineEditing",false);Set(editor,"polygonPlane",new Plane(Vector3.back,Vector3.zero));
                var p=HandleUtility.WorldToGUIPoint(new Vector3(-1,0,0));Event.current=new Event{type=EventType.MouseDown,button=0,mousePosition=p};Call(editor,"SplineSceneGUI",view);Event.current=new Event{type=EventType.MouseDrag,button=0,mousePosition=HandleUtility.WorldToGUIPoint(new Vector3(-1,1,0))};Call(editor,"SplineSceneGUI",view);Event.current=new Event{type=EventType.MouseUp,button=0,mousePosition=p};Call(editor,"SplineSceneGUI",view);var draft=Get<StudioSpline>(editor,"splineDraft");Check(draft.knots.Count==1&&Near(draft.knots[0].outgoing,Vector3.up)&&Near(draft.knots[0].incoming,Vector3.down),"Bezier click-drag creation produces mirrored handles");
                Event.current=new Event{type=EventType.KeyDown,keyCode=KeyCode.Escape};Call(editor,"SplineKey",Event.current);Check(Get<StudioSpline>(editor,"splineDraft")==null&&doc.nodes.Count==2,"Cancel discards draft without touching existing objects");
            }catch(Exception e){log.Add("FAIL "+e);}finally{Event.current=original;GUIUtility.hotControl=0;editor?.Dispose();if(view)DestroyImmediate(view);if(doc)DestroyImmediate(doc);File.WriteAllLines(Out("result.txt"),log);EditorApplication.delayCall+=()=>{if(this)Close();if(Environment.GetCommandLineArgs().Contains("-splineValidation"))EditorApplication.Exit(log.Any(x=>x.StartsWith("FAIL"))?1:0);};}
        }
    }
}
