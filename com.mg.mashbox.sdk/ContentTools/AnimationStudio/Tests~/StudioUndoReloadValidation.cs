using EditorPrefs = MashBoxSDK.AnimationStudio.StudioPreferenceStore;
using System;using System.IO;using MashBoxSDK.AnimationStudio;using UnityEditor;using UnityEngine;
public sealed class StudioUndoReloadValue:ScriptableObject { public int value; }
public static class StudioUndoReloadValidation
{
 const string Phase="MashBox.Studio.Test.UndoReload";
 [InitializeOnLoadMethod]static void Resume(){if(SessionState.GetInt(Phase,0)==1)EditorApplication.delayCall+=Verify;}
 public static void Run()
 {
  try{
   StudioUndo.ClearForTests();Undo.ClearAll();SessionState.SetString(Phase+".prefs",EditorPrefs.GetString(StudioSettings.PreferenceKey,""));SessionState.SetBool(Phase+".had",EditorPrefs.HasKey(StudioSettings.PreferenceKey));StudioSettings.Current.undoLimit=3;StudioSettings.Save();
   var value=ScriptableObject.CreateInstance<StudioUndoReloadValue>();value.hideFlags=HideFlags.HideAndDontSave;SessionState.SetInt(Phase+".object",value.GetInstanceID());
   for(int i=1;i<=2;i++){StudioUndo.IncrementCurrentGroup();int g=StudioUndo.GetCurrentGroup();StudioUndo.RegisterCompleteObjectUndo(value,"Reload test");value.value=i;StudioUndo.CollapseUndoOperations(g);StudioUndo.IncrementCurrentGroup();}
   SessionState.SetInt(Phase,1);EditorUtility.RequestScriptReload();
  }catch(Exception e){Finish(e);}
 }
 static void Verify()
 {
  try{
   var value=EditorUtility.InstanceIDToObject(SessionState.GetInt(Phase+".object",0)) as StudioUndoReloadValue;
   if(!value||StudioUndo.RetainedActions!=2)throw new Exception("Reload lost tokens or registry: "+StudioUndo.RetainedActions);
   StudioUndo.PerformUndo();if(value.value!=1)throw new Exception("Undo after reload failed");StudioUndo.PerformRedo();if(value.value!=2)throw new Exception("Redo after reload failed");
   StudioSettings.Current.undoLimit=1;StudioSettings.Save();StudioUndo.PerformUndo();StudioUndo.PerformUndo();if(value.value!=1)throw new Exception("Trimming after reload failed");Finish(null);
  }catch(Exception e){Finish(e);}
 }
 static void Finish(Exception error)
 {
  SessionState.SetInt(Phase,0);File.WriteAllText(Path.Combine(Application.dataPath,"../undo-reload-result.txt"),error==null?"PASS actual script reload retains native tagged history and enforces limit":"FAIL "+error);
  StudioUndo.ClearForTests();if(SessionState.GetBool(Phase+".had",false))EditorPrefs.SetString(StudioSettings.PreferenceKey,SessionState.GetString(Phase+".prefs",""));else EditorPrefs.DeleteKey(StudioSettings.PreferenceKey);EditorApplication.Exit(error==null?0:1);
 }
}
