#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio {
internal static class StudioPaneShortcutValidation {
 static int checks;
 static readonly BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
 static object Get(object obj,string name)=>obj.GetType().GetField(name,flags).GetValue(obj);
 static void Call(object obj,string name,params object[] args)=>obj.GetType().GetMethod(name,flags).Invoke(obj,args);
 public static void Run(){
  string result=Path.Combine(Application.dataPath,"../pane-shortcuts-result.txt");
  try {
   var property=StudioShortcutFocusGuard.Dispatch;bool prior=(bool)property.GetValue(null);
   var first=new StudioShortcutFocusGuard();var second=new StudioShortcutFocusGuard();first.Acquire();second.Acquire();first.Release();
   Check(!(bool)property.GetValue(null),"Focus transfer keeps shared guard");second.Release();Check((bool)property.GetValue(null)==prior,"Last owner restores prior dispatch");
   var doc=ScriptableObject.CreateInstance<StudioModelDocument>();
   var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
   StudioUndo.IncrementCurrentGroup();StudioUndo.RegisterCompleteObjectUndo(doc,"Create regression cube");
   doc.nodes.Add(new StudioModelDocument.Node{name="Cube",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles});StudioUndo.IncrementCurrentGroup();
   using(var editor=new StudioModelEditor(()=>{})){
    editor.Load(doc);editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);editor.Components.Add(0);
    Call(editor,"OpenBevelTool");Call(editor,"UpdateQueuedLiveTool");
    var pane=(ToolChannelPane)Get(editor,"bevelToolPane");Check(Get(editor,"livePreview")!=null,"Bevel preview exists");
    using(var blocked=UnityEngine.UIElements.KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=KeyCode.B,modifiers=EventModifiers.Control})){
     Call(pane,"ShieldShortcut",blocked);Check(blocked.isPropagationStopped,"Retained root stops Ctrl B before global dispatch");
    }
    using(var paste=UnityEngine.UIElements.KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=KeyCode.V,modifiers=EventModifiers.Control})){
     Call(pane,"ShieldShortcut",paste);Check(!paste.isPropagationStopped,"Retained root allows field paste");
    }
    var key=new Event{type=EventType.KeyDown,keyCode=KeyCode.Z,modifiers=EventModifiers.Control};
    Call(pane,"HandlePaneHistory",key);Check(key.type==EventType.Used,"Pane consumes Ctrl Z");Check(Get(editor,"livePreview")!=null,"Undo deferred beyond GUI event");
    EditorApplication.delayCall-=(EditorApplication.CallbackFunction)Delegate.CreateDelegate(typeof(EditorApplication.CallbackFunction),pane,pane.GetType().GetMethod("ApplyQueuedHistory",flags));
    Call(pane,"ApplyQueuedHistory");Check(Get(editor,"livePreview")==null,"First Undo cancels preview");Check(doc.nodes.Count==1&&editor.Transforms.Length==1,"Pending Undo preserves source object");
    Check(pane,"Undo keeps pane open");
    editor.Components.Clear();editor.Components.Add(0);Call(editor,"OpenBevelTool");
    var kind=Enum.Parse(editor.GetType().GetNestedType("LiveTool",BindingFlags.NonPublic),"Bevel");Call(editor,"QueueLiveTool",kind);Call(editor,"UpdateQueuedLiveTool");
    Check(Get(editor,"livePreview")!=null,"Another preview starts after cancellation");
    StudioUndo.PerformUndo();
    Check(doc.nodes.Count==0&&editor.Transforms.Length==0,"Native Undo removing source rebuilds preview hierarchy");
    Check(Get(editor,"livePreview")==null,"Native Undo clears live transaction");
    Check(!editor.HasBlendShape&&!editor.VisibleWireframeNodes().Any(),"Empty restored selection safe for UI/wireframes");
    Check(!pane.ExecuteCommand(false),"Pane disables Apply after source removal");pane.Close();
   }
   StudioUndo.ClearUndo(doc);StudioUndo.ClearUndo(mesh);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(doc);
   File.WriteAllText(result,"PASS "+checks+" tool-pane shortcut/Undo assertions");EditorApplication.Exit(0);
  }catch(Exception e){File.WriteAllText(result,"FAIL "+e);EditorApplication.Exit(1);}
 }
}
internal static class StudioPaneNativeValidation {
 static bool sawBevel;static int history;static double start,finished;static ToolChannelPane pane;
 public static void Run(){EditorApplication.delayCall+=Open;}
 static void Open(){
  start=EditorApplication.timeSinceStartup;
  EditorApplication.update+=Tick;
  pane=ToolChannelPane.Open("Bevel Input Validation",()=>{
   var evt=Event.current;if(evt.type==EventType.KeyDown&&evt.keyCode==KeyCode.B&&(evt.control||evt.command))sawBevel=true;
   EditorGUILayout.FloatField("Width",.1f);GUILayout.Label("Native test: Ctrl+B then Ctrl+Z");
  },owner:SceneView.lastActiveSceneView,history:redo=>{history++;finished=EditorApplication.timeSinceStartup;});
  pane.rootVisualElement.RegisterCallback<UnityEngine.UIElements.KeyUpEvent>(evt=>{
   if(evt.keyCode==KeyCode.B&&EditorWindow.focusedWindow==pane)sawBevel=true;
  },UnityEngine.UIElements.TrickleDown.TrickleDown);
  EditorApplication.update+=Tick;
 }
 static void Tick(){
  if(sawBevel&&history>0&&EditorApplication.timeSinceStartup-finished>1){
   bool build=Resources.FindObjectsOfTypeAll<EditorWindow>().Any(w=>w.GetType().Name.Contains("BuildProfile")||w.GetType().Name.Contains("BuildPlayer"));
   File.WriteAllText(Path.Combine(Application.dataPath,"../pane-native-result.txt"),!build&&pane?"PASS native focused pane consumes Ctrl+B and routes Ctrl+Z; no Build popup":"FAIL pane/build state");EditorApplication.Exit(build?1:0);
  }else if(EditorApplication.timeSinceStartup-start>90){File.WriteAllText(Path.Combine(Application.dataPath,"../pane-native-result.txt"),"TIMEOUT");EditorApplication.Exit(1);}
 }
}
}
#endif
