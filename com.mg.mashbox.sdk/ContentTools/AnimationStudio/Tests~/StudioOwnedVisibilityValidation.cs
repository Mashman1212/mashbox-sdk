#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
public static class StudioOwnedVisibilityValidation {
 const BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic;
 static int checks;static List<IntPtr> windows=new List<IntPtr>();
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateWindowEx(uint ex,string cls,string title,uint style,int x,int y,int w,int h,IntPtr owner,IntPtr menu,IntPtr instance,IntPtr args);
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")]static extern bool IsWindow(IntPtr window);
 [DllImport("user32.dll")]static extern bool DestroyWindow(IntPtr window);
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
 static void Call(string name,params object[] args)=>typeof(StudioWindowOwnership).GetMethod(name,S).Invoke(null,args);
 static IntPtr Make(string name,IntPtr owner){var h=CreateWindowEx(0x80,"STATIC","StudioVisibilityTest "+name,0x80000000,-20000,-20000,80,60,owner,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);if(h==IntPtr.Zero)throw new Exception("Create test HWND failed: "+Marshal.GetLastWin32Error());windows.Add(h);ShowWindow(h,4);return h;}
 static void Event(uint type,IntPtr window)=>Call("OnVisibilityEvent",IntPtr.Zero,type,window,0,0,0u,0u);
 public static void Run(){Exception failure=null;try{
  var studio=Make("owner",IntPtr.Zero);var pane=Make("pane",studio);var preferences=Make("preferences",studio);var recorder=Make("recorder",preferences);var unrelated=Make("unrelated",IntPtr.Zero);
  Call("TrackVisibility",pane,studio);Call("TrackVisibility",preferences,studio);Call("TrackVisibility",recorder,preferences);
  Check((IntPtr)typeof(StudioWindowOwnership).GetField("objectVisibilityHook",S).GetValue(null)!=IntPtr.Zero,"Window event hook installed");
  Check((IntPtr)typeof(StudioWindowOwnership).GetField("minimizeHook",S).GetValue(null)!=IntPtr.Zero,"Minimize event hook installed");
  Check(IsWindowVisible(pane)&&IsWindowVisible(preferences)&&IsWindowVisible(recorder),"Initial windows visible");
  ShowWindow(studio,0);Event(0x8003,studio);
  Check(!IsWindowVisible(pane),"Hidden Studio hides pane");Check(!IsWindowVisible(preferences),"Hidden Studio hides Preferences");Check(!IsWindowVisible(recorder),"Hidden Preferences hides recorder");
  Check(IsWindow(pane)&&IsWindow(recorder),"Hiding retains live windows");Check(IsWindowVisible(unrelated),"Unrelated windows unchanged");
  ShowWindow(studio,4);Event(0x8002,studio);
  Check(IsWindowVisible(pane)&&IsWindowVisible(preferences)&&IsWindowVisible(recorder),"Restore returns nested panes");
  ShowWindow(studio,0);Event(0x8003,studio);ShowWindow(pane,4);Event(0x8002,pane);
  Check(!IsWindowVisible(pane),"Late child show cannot escape hidden owner");
  DestroyWindow(pane);Event(0x8001,pane);ShowWindow(studio,4);Event(0x8002,studio);
  Check(!IsWindow(pane),"Closed hidden pane not resurrected");
  var manual=Make("manually hidden",studio);Call("TrackVisibility",manual,studio);ShowWindow(manual,0);Event(0x8003,manual);
  ShowWindow(studio,0);Event(0x8003,studio);ShowWindow(studio,4);Event(0x8002,studio);
  Check(!IsWindowVisible(manual),"Previously hidden window is not shown by restore");
  Call("StopVisibilityEvents");
  Check((IntPtr)typeof(StudioWindowOwnership).GetField("objectVisibilityHook",S).GetValue(null)==IntPtr.Zero,"Hooks released");
  Check(((Dictionary<IntPtr,IntPtr>)typeof(StudioWindowOwnership).GetField("nativeLinks",S).GetValue(null)).Count==0,"Native links cleared");
 }catch(Exception e){failure=e;}finally{
  Call("StopVisibilityEvents");for(int i=windows.Count-1;i>=0;i--)if(IsWindow(windows[i]))DestroyWindow(windows[i]);
  File.WriteAllText(Path.Combine(Application.dataPath,"../owned-visibility-result.txt"),failure==null?"PASS "+checks+" native HWND visibility assertions; event callback driven directly, no desktop input":"FAIL after "+checks+": "+failure);EditorApplication.Exit(failure==null?0:1);
 }}
}
#endif
