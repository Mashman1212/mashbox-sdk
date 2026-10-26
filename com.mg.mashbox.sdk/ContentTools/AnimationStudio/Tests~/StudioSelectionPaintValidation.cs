using System;using System.IO;using System.Linq;using System.Reflection;using UnityEditor;using UnityEngine;using MashBoxSDK.AnimationStudio;
public static class StudioSelectionPaintValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic; static int checks; static string stage;
 static object Call(object o,string name,params object[] args)=>o.GetType().GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(o,args);
 static void Check(bool ok,string message){if(!ok)throw new Exception(stage+": "+message);checks++;}
 static void Reset(){UnityEditor.Undo.ClearAll();StudioUndo.ClearForTests();StudioUndo.IncrementCurrentGroup();}
 static void History(bool redo=false){UnityEditor.Undo.FlushUndoRecordObjects();StudioUndo.IncrementCurrentGroup();if(redo)StudioUndo.PerformRedo();else StudioUndo.PerformUndo();}
 public static void Run(){string report=Path.GetFullPath(Path.Combine(Application.dataPath,"../../selection-paint-result.txt"));try{Validate();File.WriteAllText(report,"PASS "+checks+" paint selection assertions");EditorApplication.Exit(0);}catch(Exception e){File.WriteAllText(report,e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}}
 static void Validate(){
 var doc=StudioModelDocument.CreateEmpty(StudioModelDocument.NewWorkspacePath("Paint Validation"));doc.AddPrimitive(PrimitiveType.Cube);
 using(var e=new StudioModelEditor(()=>{})){e.Load(doc);e.SelectObject(0);
 foreach(var mode in new[]{StudioModelEditor.ComponentMode.Vertex,StudioModelEditor.ComponentMode.Edge,StudioModelEditor.ComponentMode.Face}){
 stage=mode.ToString();e.SetToolbarComponentMode(mode);e.Components.Clear();e.Components.Add(2);Reset();var mesh=e.ActiveMesh;var vertices=mesh.vertices;var triangles=mesh.triangles;var transforms=e.Transforms.ToArray();int dirty=EditorUtility.GetDirtyCount(mesh);
 Check((bool)Call(e,"BeginSelectionPaint",null,KeyCode.Tab,false),"Paint did not begin");
 Check((bool)Call(e,"ApplySelectionPaintHit",0),"First paint hit not selected");Check((bool)Call(e,"ApplySelectionPaintHit",1),"Second paint hit not selected");Check(!(bool)Call(e,"ApplySelectionPaintHit",1),"Repeated hit should be a no-op");Check(e.Components.SetEquals(new[]{0,1,2}),"Paint replaced existing selection");Check(StudioUndo.RetainedActions==0,"History recorded per sample");
 Call(e,"FinishSelectionPaint",false);Check(StudioUndo.RetainedActions==1,"Stroke not one action");History();Check(e.Components.SetEquals(new[]{2}),"Undo did not restore source selection");Check(e.Transforms.SequenceEqual(transforms),"Undo rebuilt scene");History(true);Check(e.Components.SetEquals(new[]{0,1,2}),"Redo did not restore painted selection");
 Reset();Check((bool)Call(e,"BeginSelectionPaint",null,KeyCode.Tab,true),"Unpaint did not begin");Call(e,"ApplySelectionPaintHit",0);Call(e,"ApplySelectionPaintHit",1);Call(e,"FinishSelectionPaint",false);Check(e.Components.SetEquals(new[]{2}),"Unpaint cleared unrelated components");Check(StudioUndo.RetainedActions==1,"Unpaint not one action");History();Check(e.Components.SetEquals(new[]{0,1,2}),"Unpaint undo failed");
 Reset();Call(e,"BeginSelectionPaint",null,KeyCode.Tab,true);Call(e,"ApplySelectionPaintHit",0);Call(e,"FinishSelectionPaint",true);Check(e.Components.SetEquals(new[]{0,1,2}),"Cancel failed");Check(StudioUndo.RetainedActions==0,"Cancel added undo");
 Call(e,"BeginSelectionPaint",null,KeyCode.Tab,false);Call(e,"ApplySelectionPaintHit",1);Call(e,"FinishSelectionPaint",false);Check(StudioUndo.RetainedActions==0,"No-op stroke added undo");
 Check(e.ActiveMesh==mesh&&mesh.vertices.SequenceEqual(vertices)&&mesh.triangles.SequenceEqual(triangles)&&EditorUtility.GetDirtyCount(mesh)==dirty,"Paint mutated mesh");
 Call(e,"BeginSelectionPaint",null,KeyCode.Tab,true);Call(e,"ApplySelectionPaintHit",0);e.ReleaseShortcutHolds();Check(e.Components.SetEquals(new[]{1,2})&&StudioUndo.RetainedActions==1,"Focus release did not commit one stroke");
 }
 e.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Object);Check(!(bool)Call(e,"BeginSelectionPaint",null,KeyCode.Tab,false),"Object-mode paint allowed");
 }
 Check(StudioShortcuts.Pressed(StudioShortcut.PaintSelection,new Event{type=EventType.KeyDown,keyCode=KeyCode.Tab}),"Tab default missing");Check(StudioShortcuts.Pressed(StudioShortcut.UnpaintSelection,new Event{type=EventType.KeyDown,keyCode=KeyCode.Tab,modifiers=EventModifiers.Control}),"Ctrl+Tab default missing");
 }
}
