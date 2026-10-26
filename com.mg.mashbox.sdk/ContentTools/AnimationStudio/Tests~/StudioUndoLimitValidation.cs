using System;using System.IO;using System.Linq;using MashBoxSDK.AnimationStudio;using UnityEditor;using UnityEngine;
public sealed class StudioUndoLimitValue:ScriptableObject { public int value; }
public static class StudioUndoLimitValidation
{
 static int checks;static string stage="start";static void Check(bool b,string m){if(!b)throw new Exception(stage+": "+m);checks++;}
 static void Edit(StudioUndoLimitValue v,int n){StudioUndo.IncrementCurrentGroup();int g=StudioUndo.GetCurrentGroup();StudioUndo.SetCurrentGroupName("Set studio value");StudioUndo.RegisterCompleteObjectUndo(v,"Set studio value");v.value=n;StudioUndo.CollapseUndoOperations(g);StudioUndo.IncrementCurrentGroup();}
 static void Reset(){StudioUndo.ClearForTests();Undo.ClearAll();}
 public static void Run()
 {
  string key=StudioSettings.PreferenceKey;bool had=true;string prior=StudioPreferenceStore.GetString(key,"");
  var a=ScriptableObject.CreateInstance<StudioUndoLimitValue>();var b=ScriptableObject.CreateInstance<StudioUndoLimitValue>();
  try{
   Reset();stage="preferences";StudioPreferenceStore.SetString(key,"{\"theme\":\"slate\"}");StudioSettings.Reload();Check(StudioSettings.Current.undoLimit==50,"Old preferences acquire default 50");Check(StudioSettings.Current.theme=="slate","Other preferences survive migration");StudioSettings.Current.undoLimit=7;StudioSettings.Save();StudioSettings.Reload();Check(StudioSettings.Current.undoLimit==7,"Limit persists in project preferences");StudioSettings.Current.undoLimit=0;StudioSettings.Save();Check(StudioSettings.Current.undoLimit==1,"Minimum is one action");StudioSettings.Current.undoLimit=int.MaxValue;StudioSettings.Save();Check(StudioSettings.Current.undoLimit==int.MaxValue,"No arbitrary upper cap");StudioSettings.Current.undoLimit=50;StudioSettings.Save();
   stage="default eviction";a.value=0;for(int i=1;i<=60;i++)Edit(a,i);Check(StudioUndo.RetainedActions==50&&StudioUndo.RetainedTokens==50,"Only latest 50 action identifiers remain");Check(Resources.FindObjectsOfTypeAll<StudioUndoToken>().Length==50,"Evicted identifiers are destroyed");for(int i=0;i<50;i++)StudioUndo.PerformUndo();Check(a.value==10,"Undo stops at oldest retained state");StudioUndo.PerformUndo();Check(a.value==10,"Evicted snapshots cannot be undone");for(int i=0;i<50;i++)StudioUndo.PerformRedo();Check(a.value==60,"Latest fifty actions redo correctly");
   stage="lower limit";StudioSettings.Current.undoLimit=3;StudioSettings.Save();Check(StudioUndo.RetainedActions==3,"Lowering limit immediately prunes old actions");for(int i=0;i<4;i++)StudioUndo.PerformUndo();Check(a.value==57,"Lower limit keeps newest three");StudioUndo.PerformRedo();Check(a.value==58,"Redo survives trimming");Edit(a,99);Check(StudioUndo.RetainedActions==2,"Editing after Undo abandons redo actions");StudioUndo.PerformRedo();Check(a.value==99,"Abandoned redo does not return");
   stage="complete gestures";Reset();a.value=b.value=0;StudioUndo.IncrementCurrentGroup();int group=StudioUndo.GetCurrentGroup();for(int i=1;i<=25;i++){StudioUndo.RecordObject(a,"Gesture");a.value=i;}StudioUndo.RegisterCompleteObjectUndo(b,"Gesture second object");b.value=4;StudioUndo.CollapseUndoOperations(group);StudioUndo.IncrementCurrentGroup();Check(StudioUndo.RetainedActions==1&&StudioUndo.RetainedTokens==1,"Repeated samples and multiple objects count once");StudioUndo.PerformUndo();Check(a.value==0&&b.value==0,"Gesture restores all source objects");StudioUndo.PerformRedo();Check(a.value==25&&b.value==4,"Gesture redo restores final samples");
   stage="collapsed groups";Reset();a.value=0;StudioUndo.IncrementCurrentGroup();group=StudioUndo.GetCurrentGroup();Edit(a,1);Edit(a,2);Edit(a,3);StudioUndo.CollapseUndoOperations(group);StudioUndo.IncrementCurrentGroup();Check(StudioUndo.RetainedActions==1,"Collapsed groups count as one action");StudioUndo.PerformUndo();Check(a.value==0,"Collapsed groups Undo together");StudioUndo.PerformRedo();Check(a.value==3,"Collapsed groups Redo together");
   stage="unrelated Unity history";Reset();a.value=b.value=0;StudioSettings.Current.undoLimit=2;StudioSettings.Save();Edit(a,1);Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(b,"Unrelated Unity edit");b.value=9;Undo.IncrementCurrentGroup();Edit(a,2);Edit(a,3);Check(StudioUndo.RetainedActions==2,"Unrelated edit does not consume Studio budget");Undo.PerformUndo();Undo.PerformUndo();Check(a.value==1,"Native Undo honors retained Studio entries");Undo.PerformUndo();Check(b.value==0,"Unrelated Unity edit was not cleared");Undo.PerformUndo();Check(a.value==1,"Oldest Studio edit was cleared individually");Undo.PerformRedo();Check(b.value==9,"Unrelated redo still works");
   stage="session restoration";Reset();a.value=0;StudioSettings.Current.undoLimit=3;StudioSettings.Save();Edit(a,1);Edit(a,2);StudioUndo.ReloadSessionForTests();Check(StudioUndo.RetainedActions==2,"Session metadata reload finds native tokens");Edit(a,3);Edit(a,4);Check(StudioUndo.RetainedActions==3,"Reloaded history enforces cap");for(int i=0;i<4;i++)StudioUndo.PerformUndo();Check(a.value==1,"Restored identifiers trim correct snapshots");
   stage="rollback";Reset();a.value=0;StudioUndo.IncrementCurrentGroup();group=StudioUndo.GetCurrentGroup();StudioUndo.RegisterCompleteObjectUndo(a,"Failed operation");a.value=8;StudioUndo.RevertAllDownToGroup(group);Check(a.value==0&&StudioUndo.RetainedActions==0,"Rollback releases canceled action");
   stage="modeling integration";Reset();StudioSettings.Current.undoLimit=2;StudioSettings.Save();
   var document=ScriptableObject.CreateInstance<StudioModelDocument>();var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);document.nodes.Add(new StudioModelDocument.Node{name="Undo channels",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles});
   using(var editor=new StudioModelEditor(()=>{}))
   {
    editor.Load(document);editor.SelectObject(0);
    for(int i=1;i<=5;i++)editor.SetObjectChannels(new Vector3(i,0,0),Vector3.zero,Vector3.one,true);
    typeof(StudioModelEditor).GetMethod("FinishChannelEdit",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(editor,null);
    Check(StudioUndo.RetainedActions==2,"Object channel edits use bounded modeling history");
    // Model the new input-event boundary before pressing Undo; direct method
    // calls above all ran in one synchronous fixture tick.
    StudioUndo.IncrementCurrentGroup();
    editor.PerformWorkspaceHistory(false);editor.PerformWorkspaceHistory(false);editor.PerformWorkspaceHistory(false);
    Check(document.nodes[0].position.x==3&&editor.Transforms[0].position.x==3,"Channel Undo stops at cap and refreshes scene transforms");
    editor.PerformWorkspaceHistory(true);Check(document.nodes[0].position.x==4&&editor.Transforms[0].position.x==4,"Channel Redo refreshes scene transforms (doc="+document.nodes[0].position.x+", scene="+editor.Transforms[0].position.x+")");
   }
   StudioUndo.ClearUndo(document);StudioUndo.ClearUndo(mesh);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(document);
   File.WriteAllText(Path.Combine(Application.dataPath,"../undo-limit-result.txt"),"PASS "+checks+" undo-limit assertions");
   Reset();if(had)StudioPreferenceStore.SetString(key,prior);else StudioPreferenceStore.DeleteKey(key);StudioSettings.Reload();UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);EditorApplication.Exit(0);
  }catch(Exception e){File.WriteAllText(Path.Combine(Application.dataPath,"../undo-limit-result.txt"),"FAIL "+stage+"\n"+e);Reset();if(had)StudioPreferenceStore.SetString(key,prior);else StudioPreferenceStore.DeleteKey(key);EditorApplication.Exit(1);}
 }
}
