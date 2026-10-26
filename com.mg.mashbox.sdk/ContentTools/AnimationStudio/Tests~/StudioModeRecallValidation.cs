#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio {
internal static class StudioModeRecallValidation {
 static int checks;
 static BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
 static void Set(object obj,string name,object value)=>obj.GetType().GetField(name,flags).SetValue(obj,value);
 static void Call(object obj,string name)=>obj.GetType().GetMethod(name,flags).Invoke(obj,null);
 static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;}
 public static void Run(){
  string result=Path.Combine(Application.dataPath,"../mode-recall-result.txt");
  string prior=StudioPreferenceStore.GetString("Studio.Mode","Animation");
  try {
   var controller=ScriptableObject.CreateInstance<AnimationStudioWindow>();
   var view=ScriptableObject.CreateInstance<MashBoxStudioViewport>();view.Show();view.Studio=controller;
   Set(controller,"viewport",view);Set(controller,"modelingMode",true);Set(view,"storedConfigurationInitialized",true);
   Check(controller.ToolbarModelEditor==null,"Simulated Modeling before editor creation");
   view.SaveStoredConfiguration();Check(StudioPreferenceStore.GetString("Studio.Mode")=="Modeling","Persists selected Modeling with no temporary editor");
   Call(controller,"EnsureModelEditor");view.SaveStoredConfiguration();Check(StudioPreferenceStore.GetString("Studio.Mode")=="Modeling","Persists Modeling with live editor");
   Call(controller,"ReleaseModelEditor");view.SaveStoredConfiguration();
   Check(controller.IsModelingMode&&controller.ToolbarModelEditor==null,"Teardown retains actual selected mode");
   Check(StudioPreferenceStore.GetString("Studio.Mode")=="Modeling","Window event after teardown cannot overwrite mode");
   StudioPreferenceStore.Flush();
   var reopened=ScriptableObject.CreateInstance<AnimationStudioWindow>();Check(reopened.IsModelingMode,"New controller restores Modeling");UnityEngine.Object.DestroyImmediate(reopened);
   Set(controller,"modelingMode",false);view.SaveStoredConfiguration();Check(StudioPreferenceStore.GetString("Studio.Mode")=="Animation","Animation choice also persists");
   reopened=ScriptableObject.CreateInstance<AnimationStudioWindow>();Check(!reopened.IsModelingMode,"New controller restores Animation");UnityEngine.Object.DestroyImmediate(reopened);
   Set(controller,"modelingMode",true);Call(controller,"OnDisable");view.SaveStoredConfiguration();
   Check(StudioPreferenceStore.GetString("Studio.Mode")=="Modeling","Disable then geometry/focus save preserves Modeling");
   Set(controller,"viewport",null);view.Studio=null;view.Close();UnityEngine.Object.DestroyImmediate(controller);
   StudioPreferenceStore.SetString("Studio.Mode",prior);StudioPreferenceStore.Flush();
   File.WriteAllText(result,"PASS "+checks+" mode recall assertions");EditorApplication.Exit(0);
  }catch(Exception e){StudioPreferenceStore.SetString("Studio.Mode",prior);File.WriteAllText(result,"FAIL "+e);EditorApplication.Exit(1);}
 }
}
}
#endif
