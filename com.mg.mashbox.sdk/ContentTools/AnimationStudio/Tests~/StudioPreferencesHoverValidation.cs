#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
public static class StudioPreferencesHoverValidation
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window,uint command);
    private static MashBoxStudioViewport owner;
    private static StudioControlsPreferences preferences;
    private static ToolChannelPane pane;
    private static int phase,checks,frameBefore;
    private static bool started;
    private static double due;
    private static IntPtr ownerHandle;
    private static DateTime settingsWritten,hotkeysWritten;
    private static string errors="";
    private static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    private static void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors+=text+"\n"+stack+"\n";}
    [InitializeOnLoadMethod]
    private static void AutoRunNative()
    {
        if(!Application.isBatchMode && Environment.GetCommandLineArgs().Contains("-studioHoverValidation")) Run();
    }
    public static void Run(){if(started)return;started=true;EditorApplication.delayCall+=Begin;}
    private static void Begin()
    {
        try
        {
            Application.logMessageReceived+=Log;
            owner=ScriptableObject.CreateInstance<MashBoxStudioViewport>();owner.dockableHost=!StudioSettings.Current.floatingStudioWindow;
            if(owner.dockableHost)owner.Show();else owner.ShowUtility();owner.Focus();ownerHandle=GetActiveWindow();
            StudioControlsPreferences.Open();preferences=Resources.FindObjectsOfTypeAll<StudioControlsPreferences>().Single();
            Check(preferences.wantsMouseMove,"Preferences receive pointer movement");
            Check(preferences.wantsMouseEnterLeaveWindow,"Preferences receive pointer enter/leave");
            var preferencesHandle=GetActiveWindow();
            Check(preferencesHandle!=IntPtr.Zero&&preferencesHandle!=ownerHandle,"Distinct native Preferences window");
            Check(GetWindow(preferencesHandle,4)==ownerHandle,"Preferences are natively owned by Studio");
            ProfilerDriver.profileEditor=true;ProfilerDriver.enabled=true;
            due=EditorApplication.timeSinceStartup+.5;EditorApplication.update+=Tick;
        }
        catch(Exception ex){Finish(ex);}
    }
    private static int PaintsSince(int frame)
    {
        int count=0;
        for(int f=Math.Max(frame,ProfilerDriver.firstFrameIndex);f<=ProfilerDriver.lastFrameIndex;f++)
            using(var raw=ProfilerDriver.GetRawFrameDataView(f,0))
                if(raw.valid)for(int i=0;i<raw.sampleCount;i++)if(raw.GetSampleName(i)=="StudioControlsPreferences.Paint")count++;
        return count;
    }
    private static void Tick()
    {
        if(EditorApplication.timeSinceStartup<due)return;
        try
        {
            switch(phase++)
            {
                case 0:
                    StudioPreferenceStore.Flush();settingsWritten=File.GetLastWriteTimeUtc(StudioPreferenceStore.SettingsPath);hotkeysWritten=File.GetLastWriteTimeUtc(StudioPreferenceStore.HotkeysPath);
                    frameBefore=ProfilerDriver.lastFrameIndex;
                    preferences.SendEvent(new Event{type=EventType.MouseMove,mousePosition=new Vector2(60,62)});due=EditorApplication.timeSinceStartup+.02;break;
                case 1:
                    if(!Application.isBatchMode) Check(PaintsSince(frameBefore)>0,"Pointer movement schedules a native Preferences paint");
                    frameBefore=ProfilerDriver.lastFrameIndex;
                    preferences.SendEvent(new Event{type=EventType.MouseLeaveWindow,mousePosition=new Vector2(-1,-1)});due=EditorApplication.timeSinceStartup+.02;break;
                case 2:
                    if(!Application.isBatchMode) Check(PaintsSince(frameBefore)>0,"Pointer leaving schedules a native Preferences paint");
                    StudioPreferenceStore.Flush();
                    Check(settingsWritten==File.GetLastWriteTimeUtc(StudioPreferenceStore.SettingsPath),"Hover does not write settings");
                    Check(hotkeysWritten==File.GetLastWriteTimeUtc(StudioPreferenceStore.HotkeysPath),"Hover does not write hotkeys");
                    preferences.Close();preferences=null;
                    owner.Focus();ownerHandle=GetActiveWindow();
                    pane=ToolChannelPane.Open("Ownership regression",()=>{},owner:owner);
                    Check(GetWindow(GetActiveWindow(),4)==ownerHandle,"ToolChannelPane retains native Studio ownership (active="+GetActiveWindow()+", owner="+GetWindow(GetActiveWindow(),4)+", expected="+ownerHandle+")");
                    pane.Close();pane=null;Check(errors.Length==0,"No native GUI errors: "+errors);Finish(null);break;
            }
        }
        catch(Exception ex){Finish(ex);}
    }
    private static void Finish(Exception error)
    {
        ProfilerDriver.enabled=false;EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
        File.WriteAllText(Path.Combine(Application.dataPath,"../preferences-hover-validation.txt"),error==null?"PASS "+checks+" hover/ownership assertions":"FAIL phase "+phase+"\n"+error+"\n"+errors);
        if(preferences)preferences.Close();if(pane)pane.Close();if(owner)owner.Close();EditorApplication.Exit(error==null?0:1);
    }
}
#endif
