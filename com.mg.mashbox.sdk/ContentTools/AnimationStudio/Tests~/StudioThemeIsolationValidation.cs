using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
public class StudioThemeIsolationHost:EditorWindow {
 internal Action action;internal Exception failure;
 void OnGUI(){if(action==null)return;var callback=action;action=null;try{callback();}catch(Exception e){failure=e;}}
 internal void Drive(Action callback,EventType type=EventType.KeyDown){action=callback;failure=null;SendEvent(new Event{type=type,keyCode=KeyCode.F12});if(failure!=null)throw failure;if(action!=null)throw new Exception("No native GUI event");}
}
public static class StudioThemeIsolationValidation {
 static int checks;static StringBuilder log=new StringBuilder();
 static GUIStyleState[] States(GUIStyle s)=>new[]{s.normal,s.hover,s.active,s.focused,s.onNormal,s.onHover,s.onActive,s.onFocused};
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
 static void Same(GUIStyle a,GUIStyle b,string text){var x=States(a);var y=States(b);for(int i=0;i<8;i++){Check(x[i].textColor==y[i].textColor,text+" text state="+i);Check(x[i].background==y[i].background,text+" background state="+i);Check(x[i].scaledBackgrounds.SequenceEqual(y[i].scaledBackgrounds),text+" high-DPI state="+i);}Check(a.border.left==b.border.left&&a.border.right==b.border.right&&a.border.top==b.border.top&&a.border.bottom==b.border.bottom,text+" border");}
 public static void Run(){int exit=0;var host=ScriptableObject.CreateInstance<StudioThemeIsolationHost>();string original=StudioSettings.Current.theme;try{
 host.position=new Rect(20,20,600,300);host.ShowUtility();host.Drive(()=>{
 var styles=new[]{EditorStyles.numberField,EditorStyles.textField,EditorStyles.objectField,EditorStyles.popup,EditorStyles.helpBox,EditorStyles.label,EditorStyles.foldout,EditorStyles.toggle,EditorStyles.boldLabel};
 var originals=styles.Select(s=>new GUIStyle(s)).ToArray();log.AppendLine("Native numberField aliases textField="+ReferenceEquals(styles[0],styles[1]));
 foreach(var palette in StudioTheme.Palettes){StudioSettings.Current.theme=palette.Id;
 using(StudioTheme.Scope()){
 Check(EditorStyles.numberField.normal.background!=originals[0].normal.background,"Studio field gets theme background");
 var outer=styles.Select(s=>new GUIStyle(s)).ToArray();using(StudioTheme.Scope()){}for(int i=0;i<styles.Length;i++)Same(styles[i],outer[i],"Nested "+palette.Id+" field="+i);
 }for(int i=0;i<styles.Length;i++)Same(styles[i],originals[i],"Native restoration "+palette.Id+" field="+i);
 }
 });log.AppendLine("PASS "+checks+" native field/nesting/restoration assertions");
 }catch(Exception e){exit=1;log.AppendLine(e.ToString());}finally{StudioSettings.Current.theme=original;if(host)host.Close();}File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../theme-isolation-result.txt")),log.ToString());EditorApplication.Exit(exit);}
}
