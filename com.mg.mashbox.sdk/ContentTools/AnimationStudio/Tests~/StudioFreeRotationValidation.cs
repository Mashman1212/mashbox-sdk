using System;using System.IO;using System.Linq;using System.Reflection;using MashBoxSDK.AnimationStudio;using UnityEditor;using UnityEngine;using Object=UnityEngine.Object;
public static class StudioFreeRotationValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;const BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic;
 static readonly Type Disc=typeof(Editor).Assembly.GetType("UnityEditorInternal.Disc");
 public static void RunBefore()=>Run(false);public static void RunAfter()=>Run(true);
 static void Run(bool strict){int failures=0;string report="";StudioControlsValidationHost host=null;try{host=ScriptableObject.CreateInstance<StudioControlsValidationHost>();host.position=new Rect(100,100,800,600);host.ShowUtility();foreach(bool free in new[]{true,false})foreach(bool local in new[]{false,true})foreach(bool multi in new[]{false,true}){
 var doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.nodes.Add(new StudioModelDocument.Node{name="Rotated A",mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube),rotation=Quaternion.Euler(37,69,23),position=new Vector3(3,-2,5)});if(multi)doc.nodes.Add(new StudioModelDocument.Node{name="Rotated B",mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube),rotation=Quaternion.Euler(15,-42,71),position=new Vector3(5,-1,5)});var e=new StudioModelEditor(()=>{});var cameraObject=new GameObject("Native rotation test camera",typeof(Camera));
 try{e.Load(doc);e.SelectObject(0);if(multi)e.SelectObject(1,true);typeof(StudioModelEditor).GetField("local",F).SetValue(e,local);typeof(StudioModelEditor).GetField("tool",F).SetValue(e,Tool.Rotate);var center=e.Transforms[e.Active].position;var initial=e.Transforms.Select(t=>t.rotation).ToArray();var positions=e.Transforms.Select(t=>t.position).ToArray();var camera=cameraObject.GetComponent<Camera>();camera.transform.position=center+new Vector3(4,3,-6);camera.transform.LookAt(center);camera.pixelRect=new Rect(0,0,800,600);var draw=typeof(StudioModelEditor).GetMethod("DrawHandle",F);Vector2 start=default;int nearest=0;
 host.Drive(()=>{typeof(HandleUtility).GetMethod("BeginHandles",S).Invoke(null,null);Handles.SetCamera(new Rect(0,0,800,600),camera);float size=HandleUtility.GetHandleSize(center)*StudioSettings.Current.handleSize;start=HandleUtility.WorldToGUIPoint(center+camera.transform.right*size*1.1f);Event.current.mousePosition=start;draw.Invoke(e,null);var ids=(Handles.RotationHandleIds)typeof(StudioModelEditor).GetField("rotationHandleIds",F).GetValue(e);nearest=free?ids.xyz:ids.y;},EventType.Layout);if(nearest==0)throw new Exception("No native rotation ring at the test point");
 Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();host.Drive(()=>{Handles.SetCamera(new Rect(0,0,800,600),camera);HandleUtility.nearestControl=nearest;Event.current=new Event{type=EventType.MouseDown,button=0,mousePosition=start};draw.Invoke(e,null);if(GUIUtility.hotControl!=nearest)throw new Exception("Native rotation was not captured");},EventType.MouseDown);
 float worst=0;Vector2 pointer=start;float angle=0;Quaternion freeTurn=Quaternion.identity;
 for(int step=0;step<8;step++){host.Drive(()=>{Handles.SetCamera(new Rect(0,0,800,600),camera);Event.current=new Event{type=EventType.Layout,mousePosition=pointer};draw.Invoke(e,null);},EventType.Layout);var delta=step==7?Vector2.zero:new Vector2(step<3?8:-4,step<5?3:-7);pointer+=delta;host.Drive(()=>{Handles.SetCamera(new Rect(0,0,800,600),camera);Event.current=new Event{type=EventType.MouseDrag,button=0,mousePosition=pointer,delta=delta};draw.Invoke(e,null);},EventType.MouseDrag);
 if(free){var axisDelta=camera.transform.TransformDirection(new Vector3(-delta.y,-delta.x,0));freeTurn=Quaternion.AngleAxis(delta.magnitude,axisDelta.normalized)*freeTurn;}angle=-(float)Disc.GetField("s_RotationDist",S).GetValue(null);var axis=(Vector3)Disc.GetField("s_StartAxis",S).GetValue(null);var turn=free?freeTurn:Quaternion.AngleAxis(angle,axis);for(int i=0;i<initial.Length;i++){worst=Mathf.Max(worst,Quaternion.Angle(e.Transforms[i].rotation,turn*initial[i]));if(Vector3.Distance(e.Transforms[i].position,center+turn*(positions[i]-center))>.001f)worst=Mathf.Max(worst,1);}}
 bool passed=worst<.08f;if(!passed)failures++;report+=$"free={free} local={local} multi={multi} nativeAngle={angle:F3} maxRotationError={worst:F3} passed={passed}\n";
 host.Drive(()=>{Handles.SetCamera(new Rect(0,0,800,600),camera);Event.current=new Event{type=EventType.MouseUp,button=0,mousePosition=pointer};draw.Invoke(e,null);},EventType.MouseUp);Undo.CollapseUndoOperations(group);Undo.FlushUndoRecordObjects();Undo.PerformUndo();for(int i=0;i<initial.Length;i++)if(Quaternion.Angle(e.Transforms[i].rotation,initial[i])>.05f)throw new Exception("Undo did not restore rotation");Undo.PerformRedo();
 }finally{GUIUtility.hotControl=0;e.Dispose();Undo.ClearUndo(doc);foreach(var node in doc.nodes)Object.DestroyImmediate(node.mesh);Object.DestroyImmediate(doc);Object.DestroyImmediate(cameraObject);}}
 report+="COMPLETE failures="+failures+"\n";}catch(Exception error){report+="FAIL "+error;failures++;Debug.LogException(error);}finally{if(host)host.Close();File.WriteAllText(Path.GetFullPath(Application.dataPath+"/../../free-rotation."+(strict?"after":"before")+".txt"),report);EditorApplication.Exit(strict&&failures>0?1:0);}}
}

public sealed class StudioControlsValidationHost : EditorWindow
{
    private Action action;
    private Exception failure;
    private bool ran;
    private bool continuous;
    private EventType expected;
    internal void Drive(Action callback,EventType type=EventType.KeyDown)
    {
        action=callback;ran=false;failure=null;expected=type;
        SendEvent(new Event { type=type,keyCode=KeyCode.F12 });
        action=null;
        if(failure!=null)throw failure;
        if(!ran)throw new Exception("Validation host did not receive its IMGUI event");
    }
    internal void Draw(Action callback)
    {
        action=callback;continuous=true;failure=null;
        SendEvent(new Event { type=EventType.Layout });
        SendEvent(new Event { type=EventType.Repaint });
        action=null;continuous=false;
        if(failure!=null)throw failure;
    }
    private void OnGUI()
    {
        if(action==null || (!continuous && Event.current.type!=expected))return;
        var callback=action;if(!continuous)action=null;ran=true;
        var previous=new Event(Event.current);
        try { callback(); }
        catch(Exception error) { failure=error; }
        finally { Event.current=previous;if(Event.current.isKey)Event.current.Use(); }
    }
}
