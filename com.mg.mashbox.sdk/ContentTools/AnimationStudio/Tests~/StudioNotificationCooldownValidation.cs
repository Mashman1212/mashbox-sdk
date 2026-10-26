using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using MashBoxSDK.AnimationStudio;
public static class StudioNotificationCooldownValidation {
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
 static int checks;
 static object Get(object o,string name)=>o.GetType().GetField(name,F).GetValue(o);
 static void Set(object o,string name,object value)=>o.GetType().GetField(name,F).SetValue(o,value);
 static void Call(object o,string name)=>o.GetType().GetMethod(name,F).Invoke(o,null);
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
 public static void Run(){int exit=0;var view=ScriptableObject.CreateInstance<MashBoxStudioViewport>();bool hidden=StudioSettings.Current.hideStatusBar;
 try {
 StudioSettings.Current.hideStatusBar=false;view.position=new Rect(0,0,1200,800);view.Show();view.BindStatusBar(true);
 var stack=(VisualElement)Get(view,"statusBar");var messages=(IList)Get(view,"statusMessages");var last=(Dictionary<string,double>)Get(view,"statusLastShown");
 Check(stack.childCount==0,"Initially empty");
 view.ShowStatusMessage("Repeated error");var first=messages[0];double started=(double)Get(first,"started");
 for(int i=0;i<100;i++)view.ShowStatusMessage("Repeated error");
 Check(stack.childCount==1&&messages.Count==1,"100 repeats produce one toast");Check((double)Get(first,"started")==started,"Repeats do not refresh expiry");
 view.ShowStatusMessage("  Repeated error  ");Check(stack.childCount==1,"Whitespace variant is deduplicated");
 last["Repeated error"]=EditorApplication.timeSinceStartup-3;
 view.ShowStatusMessage("Repeated error");Check(stack.childCount==1&&(double)Get(first,"started")==started,"Visible duplicate suppressed after cooldown");
 Call(view,"ClearStatusMessages");view.ShowStatusMessage("Evicted error");
 for(int i=0;i<100;i++){view.ShowStatusMessage("Distinct "+i);Check(stack.childCount<=6&&messages.Count<=6,"Six-toasts maximum during burst");}
 Check(stack.childCount==6&&((Label)stack[0]).text=="Distinct 94"&&((Label)stack[5]).text=="Distinct 99","Newest six retained in order");
 Check(((Label)first.GetType().GetField("label",F).GetValue(first)).parent==null,"Removed labels detached");
 view.ShowStatusMessage("Evicted error");Check(((Label)stack[5]).text=="Distinct 99","Evicted repeat still on cooldown");
 last["Evicted error"]=EditorApplication.timeSinceStartup-2.1;view.ShowStatusMessage("Evicted error");Check(((Label)stack[5]).text=="Evicted error"&&stack.childCount==6,"Cooldown permits message again after two seconds");
 foreach(var entry in messages)Set(entry,"started",EditorApplication.timeSinceStartup-100);Call(view,"TickStatusMessages");Check(messages.Count==0&&stack.childCount==0&&stack.style.display.value==DisplayStyle.None,"Independent expiration empties and hides stack");
 view.ShowStatusMessage("Evicted error");Check(stack.childCount==0,"Expiry does not bypass cooldown");
 last["Evicted error"]=EditorApplication.timeSinceStartup-3;view.ShowStatusMessage("Evicted error");Check(stack.childCount==1,"Expired message may recur after cooldown");
 var label=(Label)stack[0];Check(label.pickingMode==PickingMode.Ignore&&stack.pickingMode==PickingMode.Ignore,"No tool-input interception");
 Check(label.style.minHeight.value.value==30&&label.style.borderTopLeftRadius.value.value==4,"Existing card layout retained");
 view.BindStatusBar(false);Check(messages.Count==0&&stack.childCount==0&&last.Count==0,"Mode change clears toasts and cooldowns");
 view.BindStatusBar(true);view.ShowStatusMessage("Evicted error");Check(stack.childCount==1,"Mode change permits a fresh notification");
 Call(view,"ClearStatusMessages");StudioSettings.Current.hideStatusBar=true;view.ShowStatusMessage("Hidden error");Check(stack.childCount==0&&last.Count==0,"Hidden notifications consume no cooldown");
 File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../notification-cooldown-result.txt")),"PASS "+checks+" notification burst/cooldown/expiry/lifecycle assertions");
 }catch(Exception e){exit=1;File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../notification-cooldown-result.txt")),e.ToString());}
 finally{StudioSettings.Current.hideStatusBar=hidden;if(view)view.Close();}EditorApplication.Exit(exit);
 }
}