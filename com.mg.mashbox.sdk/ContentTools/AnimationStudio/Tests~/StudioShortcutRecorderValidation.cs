#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio {
internal static class StudioShortcutRecorderValidation {
 static int checks;
 static readonly BindingFlags instance=BindingFlags.NonPublic|BindingFlags.Instance;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
 static void Call(object recorder,string method,params object[] args)=>recorder.GetType().GetMethod(method,instance).Invoke(recorder,args);
 static StudioShortcutRecorder Make(StudioShortcuts.Definition def,Action<StudioShortcuts.Binding> apply){
  var window=ScriptableObject.CreateInstance<StudioShortcutRecorder>();
  typeof(StudioShortcutRecorder).GetField("definition",instance).SetValue(window,def);
  typeof(StudioShortcutRecorder).GetField("apply",instance).SetValue(window,apply);
  window.ShowUtility();Call(window,"AcquireShortcutCapture");return window;
 }
 public static void Run(){
  string result=Path.Combine(Application.dataPath,"../shortcut-recorder-result.txt");
  var property=StudioShortcutFocusGuard.Dispatch;
  bool prior=property!=null&&(bool)property.GetValue(null);
  try {
   Check(property!=null,"Installed Unity shortcut dispatcher resolved");
   property.SetValue(null,true);
   var id=StudioModelCommands.Register("Mesh Tools","Bevel…").Value;
   var def=StudioShortcuts.Find(id);StudioShortcuts.Binding captured=null;
   var recorder=Make(def,b=>captured=b);
   Check(!(bool)property.GetValue(null),"Dispatcher blocked while listening");
   Call(recorder,"ReleaseShortcutCapture");Check((bool)property.GetValue(null),"Focus loss restores dispatcher");
   property.SetValue(null,false);Call(recorder,"AcquireShortcutCapture");Call(recorder,"ReleaseShortcutCapture");
   Check(!(bool)property.GetValue(null),"Previously disabled dispatcher remains disabled");
   property.SetValue(null,true);Call(recorder,"AcquireShortcutCapture");
   var modifier=new Event{type=EventType.KeyDown,keyCode=KeyCode.LeftControl,modifiers=EventModifiers.Control};Call(recorder,"HandleRecordingKey",modifier);
   Check(modifier.type==EventType.Used&&captured==null,"Modifier key consumed without assignment");
   var release=new Event{type=EventType.KeyUp,keyCode=KeyCode.LeftControl};Call(recorder,"HandleRecordingKey",release);
   Check(release.type==EventType.Used,"Key release consumed");
   var command=new Event{type=EventType.ExecuteCommand,commandName="BuildPlayer"};Call(recorder,"HandleRecordingKey",command);
   Check(command.type==EventType.Used,"Menu command blocked while recording");
   var key=new Event{type=EventType.KeyDown,keyCode=KeyCode.B,modifiers=EventModifiers.Control};Call(recorder,"HandleRecordingKey",key);
   Check(key.type==EventType.Used,"Ctrl B consumed");
   Check(captured==null&&!(bool)property.GetValue(null),"Primary key down keeps capture active");
   var up=new Event{type=EventType.KeyUp,keyCode=KeyCode.B,modifiers=EventModifiers.Control};Call(recorder,"HandleRecordingKey",up);
   Check(captured!=null&&captured.key==KeyCode.B&&captured.modifiers==StudioModifiers.Action,"Ctrl B captured as bevel binding");
   Check(up.type==EventType.Used&&!(bool)property.GetValue(null),"Primary key release consumed before dispatcher restoration");
   Call(recorder,"CloseAfterInput");
   Check((bool)property.GetValue(null),"Successful capture restores Unity shortcuts");
   captured=null;recorder=Make(def,b=>captured=b);
   var conflict=new Event{type=EventType.KeyDown,keyCode=KeyCode.W};Call(recorder,"HandleRecordingKey",conflict);
   Check(conflict.type==EventType.Used&&captured==null,"Conflicting assignment consumed and rejected");
   Check(!(bool)property.GetValue(null),"Conflict continues blocking dispatcher");
   recorder.Close();Check((bool)property.GetValue(null),"Cancellation restores dispatcher");
   recorder=Make(def,b=>{});Call(recorder,"ReleaseShortcutCapture");
   Check((bool)property.GetValue(null),"Reload release restores dispatcher");recorder.Close();
   property.SetValue(null,prior);File.WriteAllText(result,"PASS "+checks+" shortcut recorder assertions");EditorApplication.Exit(0);
  }catch(Exception e){if(property!=null)property.SetValue(null,prior);File.WriteAllText(result,"FAIL "+e);EditorApplication.Exit(1);}
 }
}
}
#endif
