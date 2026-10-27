using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEditor.Overlays;using UnityEngine;using MashBoxSDK.AnimationStudio;
public static class StudioToolbarDefaultsValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 static int checks;
 static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
 static void Call(MashBoxStudioViewport view,string name)=>typeof(MashBoxStudioViewport).GetMethod(name,F).Invoke(view,null);
 static void Set(MashBoxStudioViewport view,string name,object value)=>typeof(MashBoxStudioViewport).GetField(name,F).SetValue(view,value);
 public static void Run(){EditorApplication.delayCall+=()=>{
  MashBoxStudioViewport view=null;string result;
  try{
   AnimationStudioWindow.Open();var studio=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(studio,new object[]{true});view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(x=>x.Studio==studio);Call(view,"InitializeStudioToolbars");
   var bars=view.overlayCanvas.overlays.Where(o=>o is ToolbarOverlay&&o.GetType().Assembly==typeof(MashBoxStudioViewport).Assembly).ToArray();Check(bars.Length>=5,"Missing registered Studio toolbars");
   var registry=(List<Overlay>)typeof(MashBoxStudioViewport).GetField("studioToolbarOverlays",F).GetValue(view);
   registry.Clear();Set(view,"restoredStoredToolbars",false);StudioPreferenceStore.DeleteKey("Studio.Toolbars");Call(view,"RestoreStoredToolbars");Check(!(bool)typeof(MashBoxStudioViewport).GetField("restoredStoredToolbars",F).GetValue(view),"Empty registry incorrectly completed restoration");
   Call(view,"InitializeStudioToolbars");Set(view,"studioToolbarVersion",2);
   foreach(var bar in bars)bar.displayed=false;view.overlayCanvas.overlaysEnabled=false;StudioPreferenceStore.DeleteKey("Studio.Toolbars");Set(view,"restoredStoredToolbars",false);
   Call(view,"RestoreStoredToolbars");Check(view.overlayCanvas.overlaysEnabled,"Missing preferences left canvas disabled");foreach(var bar in bars)Check(bar.displayed,"Missing preferences left toolbar hidden: "+bar.id);
   Check(StudioPreferenceStore.GetString("Studio.Toolbars")==string.Empty,"Restoring defaults replaced the empty preference with a saved layout");
   var hidden=bars[0];hidden.displayed=false;Call(view,"SaveStoredToolbars");string saved=StudioPreferenceStore.GetString("Studio.Toolbars");bool floating=hidden.floating;
   hidden.displayed=true;hidden.Undock();Call(view,"RestoreStoredToolbars");Check(!hidden.displayed,"Saved hidden toolbar was overridden");Check(hidden.floating==floating,"Saved toolbar docking changed");Check(StudioPreferenceStore.GetString("Studio.Toolbars")==saved,"Restoration rewrote saved preferences");
   view.overlayCanvas.overlaysEnabled=false;Call(view,"SaveStoredToolbars");view.overlayCanvas.overlaysEnabled=true;Call(view,"RestoreStoredToolbars");Check(!view.overlayCanvas.overlaysEnabled,"Saved disabled toolbar canvas was overridden");
   result="PASS "+checks+" first-run visibility, deferred registry and saved-layout preservation checks";
  }catch(Exception e){result=e.ToString();}
  finally{if(view)view.Close();}
  File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../toolbar-defaults-result.txt")),result);EditorApplication.Exit(result.StartsWith("PASS")?0:1);
 };}
}
