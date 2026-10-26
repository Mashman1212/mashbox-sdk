#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio {
internal static class StudioRecorderNativeValidation {
 static double started,captured;
 static string binding;
 public static void Run(){EditorApplication.delayCall+=Open;}
 static void Open(){
  started=EditorApplication.timeSinceStartup;
  var id=StudioModelCommands.Register("Mesh Tools","Bevel…").Value;
  StudioShortcutRecorder.Open(StudioShortcuts.Find(id),-1,b=>{binding=b.ToString();captured=EditorApplication.timeSinceStartup;});
  EditorApplication.update+=Tick;
 }
 static void Tick(){
  if(binding!=null&&EditorApplication.timeSinceStartup-captured>1){
   bool build=Resources.FindObjectsOfTypeAll<EditorWindow>().Any(w=>w.GetType().Name.Contains("BuildProfile")||w.GetType().Name.Contains("BuildPlayer"));
   File.WriteAllText(Path.Combine(Application.dataPath,"../native-recorder-result.txt"),(build?"FAIL Build window opened; ":"PASS native input captured without Build window; ")+binding);
   EditorApplication.Exit(build?1:0);
  }else if(EditorApplication.timeSinceStartup-started>90){File.WriteAllText(Path.Combine(Application.dataPath,"../native-recorder-result.txt"),"TIMEOUT awaiting physical chord");EditorApplication.Exit(1);}
 }
}
}
#endif
