#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio {
internal static class StudioDisplayToggleValidation {
 static int checks;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
 public static void Run(){
  string result=Path.Combine(Application.dataPath,"../display-toggle-result.txt");
  var view=ScriptableObject.CreateInstance<MashBoxStudioViewport>();
  try { using(var editor=new StudioModelEditor(()=>{})) {
   editor.BindModelCommands(view,d=>editor.Load(d));
   var populate=typeof(StudioModelEditor).GetMethod("PopulateModelMenu",BindingFlags.NonPublic|BindingFlags.Instance);
   var modes=new[]{DrawCameraMode.Textured,DrawCameraMode.Wireframe,DrawCameraMode.TexturedWire};
   var labels=new[]{"Shaded","Wireframe","Shaded Wireframe"};
   for(int i=0;i<modes.Length;i++){
    var target=modes[i];
    var off=target==DrawCameraMode.TexturedWire?DrawCameraMode.Textured:DrawCameraMode.TexturedWire;
    foreach(var start in modes){
     editor.SetModelDrawMode(view,start);
     var menu=new StudioMenu("Mesh Display");
     populate.Invoke(editor,new object[]{"Mesh Display",menu,view,(Action<StudioModelDocument>)(d=>editor.Load(d))});
     var item=menu.Items.Single(x=>x.Text==labels[i]);
     Check(item.Checked==(start==target),"Menu state "+labels[i]);
     // Retain this callback to catch stale captured checked-state bugs.
     for(int press=0;press<3;press++){
      var expected=editor.ModelDrawMode==target?off:target;
      item.Action();Check(editor.ModelDrawMode==expected,"Repeated menu toggle "+labels[i]);
     }
    }
    var command=StudioShortcuts.Definitions.Single(x=>x.group=="Mesh Display"&&x.name==labels[i]);
    StudioShortcuts.Set(command.id,new[]{new StudioShortcuts.Binding(KeyCode.F10)});
    editor.SetModelDrawMode(view,off);
    for(int press=0;press<4;press++){
     var evt=new Event{type=EventType.KeyDown,keyCode=KeyCode.F10};
     Check(editor.TryExecuteModelCommand(evt,view)&&evt.type==EventType.Used,"Shortcut consumed");
     Check(editor.ModelDrawMode==(press%2==0?target:off),"Repeated shortcut toggle "+labels[i]);
    }
    StudioShortcuts.Reset(command.id);
   }
   // Ordinary actions remain repeatable actions, with no inferred checked-state logic.
   int calls=0;var actionMenu=new StudioMenu();
   actionMenu.AddItem(new GUIContent("One-shot operation"),false,()=>calls++);
   actionMenu.Items[0].Action();actionMenu.Items[0].Action();
   Check(calls==2&&!actionMenu.Items[0].Checked,"One-shot action executes once per press");
   File.WriteAllText(result,"PASS "+checks+" display toggle/action assertions");
  }
  UnityEngine.Object.DestroyImmediate(view);EditorApplication.Exit(0);
  }catch(Exception e){File.WriteAllText(result,"FAIL "+e);EditorApplication.Exit(1);}
 }
}
}
#endif
