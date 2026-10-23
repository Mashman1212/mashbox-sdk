#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using UnityEditor.Overlays;
public static class StudioPreferencesValidation
{
    private static int checks;
    private static string result => Path.GetFullPath(Path.Combine(Application.dataPath,"../preferences-result.txt"));
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    public static void Run()
    {
        try
        {
            string folder = StudioPreferenceStore.DirectoryPath;
            Check(folder == Path.Combine(Path.GetDirectoryName(Application.dataPath),"MashBoxStudio","Preferences"), "Project-root preference path");
            StudioSettings.Current.undoLimit = 73; StudioSettings.Current.gridSpacing = .5f; StudioSettings.Save();
            ToolChannelPane.SetFloatDefault("Bevel Edges", "Width", .27f); ToolChannelPane.FlushDefaults();
            StudioPreferenceStore.SetFloat(MashBoxStudioViewport.LeftWidthKey, 301);
            StudioPreferenceStore.SetBounds("Studio.Bounds", new Rect(210,140,1200,850));
            StudioPreferenceStore.SetString("Studio.Mode", "Modeling");
            StudioPreferenceStore.Flush();
            Check(File.Exists(StudioPreferenceStore.SettingsPath),"Settings written outside Assets");
            StudioSettings.Reload();
            Check(StudioSettings.Current.undoLimit==73 && StudioSettings.Current.gridSpacing==.5f,"Settings reload from disk-backed configuration");
            typeof(StudioPreferenceStore).GetField("data",Flags).SetValue(null,null);
            Check(StudioPreferenceStore.GetFloat(MashBoxStudioViewport.LeftWidthKey,0)==301,"Store reconstructed from file");
            Check(StudioPreferenceStore.GetBounds("Studio.Bounds",default)==new Rect(210,140,1200,850),"Window bounds JSON round trip");
            string legacy = "MashBox.Studio.Validation."+Guid.NewGuid();
            EditorPrefs.SetFloat(legacy,8);
            typeof(StudioPreferenceStore).GetField("migrateLegacy",Flags).SetValue(null,true);
            Check(StudioPreferenceStore.GetFloat(legacy,1)==8,"Legacy float migration");
            EditorPrefs.SetFloat(legacy,9);
            Check(StudioPreferenceStore.GetFloat(legacy,1)==8,"File value takes precedence over legacy registry"); EditorPrefs.DeleteKey(legacy);
            StudioShortcuts.Set(StudioShortcut.Edge,new[]{new StudioShortcuts.Binding(KeyCode.F8)});
            string hotkeyExport=Path.Combine(folder,"ExportedHotkeys.json"); StudioShortcuts.Export(hotkeyExport);
            string hotkeys = File.ReadAllText(StudioPreferenceStore.HotkeysPath);
            Check(hotkeys.Contains("MashBoxStudio.Hotkeys"),"Dedicated versioned Hotkeys.json");
            StudioShortcuts.Set(StudioShortcut.Edge,new[]{new StudioShortcuts.Binding(KeyCode.F7)});
            StudioShortcuts.Import(hotkeyExport);
            Check(StudioShortcuts.Label(StudioShortcut.Edge)=="F8","Hotkey import updates cached labels immediately");
            Check(StudioSettings.Current.undoLimit==73,"Hotkey import preserves general settings");
            string exported=Path.Combine(folder,"ExportedSettings.json"); StudioPreferenceStore.ExportSettings(exported);
            StudioSettings.Current.undoLimit=9; StudioSettings.Save();
            StudioShortcuts.Set(StudioShortcut.Edge,new[]{new StudioShortcuts.Binding(KeyCode.F6)});
            ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.7f); ToolChannelPane.FlushDefaults();
            StudioPreferenceStore.ImportSettings(exported);
            Check(StudioSettings.Current.undoLimit==73,"Settings import restores preferences");
            Check(StudioShortcuts.Label(StudioShortcut.Edge)=="F6","Settings import leaves hotkeys alone");
            Check(Mathf.Abs(ToolChannelPane.GetFloatDefault("Bevel Edges","Width",0)-.27f)<.0001f,"Settings import invalidates cached tool defaults");
            string invalid=Path.Combine(folder,"Invalid.json"); File.WriteAllText(invalid,"{}");
            bool rejected=false;try{StudioPreferenceStore.ImportSettings(invalid);}catch(InvalidDataException){rejected=true;}
            Check(rejected&&StudioSettings.Current.undoLimit==73,"Invalid settings rejected without changing active settings");
            rejected=false;try{StudioShortcuts.Import(invalid);}catch(InvalidDataException){rejected=true;}
            Check(rejected&&StudioShortcuts.Label(StudioShortcut.Edge)=="F6","Invalid hotkeys rejected without changing bindings");
            File.WriteAllText(invalid,File.ReadAllText(hotkeyExport).Replace("\"version\": 1","\"version\": 999"));
            rejected=false;try{StudioShortcuts.Import(invalid);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"Unsupported hotkey schema rejected");
            StudioPreferenceStore.SetString("MashBox.LastTake."+Application.dataPath,"Assets/Example.asset");
            Check(StudioPreferenceStore.GetString("MashBox.LastTake.OtherProject")=="Assets/Example.asset","Project-root-local keys portable between project directories");
            ToolChannelPane pane=ToolChannelPane.Open("First",()=>{}, initialSize:new Vector2(300,160));
            pane.position=new Rect(330,240,360,220); var actualPaneBounds=pane.position; pane.Close();
            var second=ToolChannelPane.Open("Second",()=>{});
            Check(second.position==actualPaneBounds,"All tool panes share actual native position and size (native="+actualPaneBounds+", restored="+second.position+")");
            bool canceled=false;
            second.Close(); second=ToolChannelPane.Open("Second",()=>{},cancelPreview:()=>canceled=true);
            var third=ToolChannelPane.Open("Third",()=>{});
            Check(Resources.FindObjectsOfTypeAll<ToolChannelPane>().Length==1&&canceled,"Opening another tool closes previous pane and cancels uncommitted preview");third.Close();
            var studio=ScriptableObject.CreateInstance<AnimationStudioWindow>();
            Check((bool)typeof(AnimationStudioWindow).GetField("modelingMode",Flags).GetValue(studio),"Studio controller restores Modeling mode");
            typeof(AnimationStudioWindow).GetField("modelingMode",Flags).SetValue(studio,false);
            StudioPreferenceStore.SetString("Studio.Mode","Animation");
            UnityEngine.Object.DestroyImmediate(studio);
            studio=ScriptableObject.CreateInstance<AnimationStudioWindow>();
            Check(!(bool)typeof(AnimationStudioWindow).GetField("modelingMode",Flags).GetValue(studio),"Studio controller restores Animation mode");
            typeof(AnimationStudioWindow).GetField("controlSize",Flags).SetValue(studio,2.2f);studio.SaveAnimationConfiguration();
            typeof(AnimationStudioWindow).GetField("controlSize",Flags).SetValue(studio,1f);studio.LoadAnimationConfiguration();
            Check((float)typeof(AnimationStudioWindow).GetField("controlSize",Flags).GetValue(studio)==2.2f,"Animation UI configuration round trip");
            UnityEngine.Object.DestroyImmediate(studio);
            var view=ScriptableObject.CreateInstance<MashBoxStudioViewport>();view.Show();
            typeof(MashBoxStudioViewport).GetMethod("InitializeStudioToolbars",Flags).Invoke(view,null);
            var overlay=view.overlayCanvas.overlays.First(o=>o is ToolbarOverlay&&o.GetType().Assembly==typeof(MashBoxStudioViewport).Assembly);
            overlay.displayed=false;
            typeof(MashBoxStudioViewport).GetMethod("SaveStoredToolbars",Flags).Invoke(view,null);
            overlay.displayed=true;
            typeof(MashBoxStudioViewport).GetMethod("RestoreStoredToolbars",Flags).Invoke(view,null);
            Check(!overlay.displayed,"Native toolbar visibility restored from project JSON");
            Check(StudioPreferenceStore.GetString("Studio.Toolbars").Contains("containerId"),"Toolbar docking state stored alongside settings");
            bool wasFloating=overlay.floating;
            overlay.Undock();
            typeof(MashBoxStudioViewport).GetMethod("RestoreStoredToolbars",Flags).Invoke(view,null);
            Check(overlay.floating==wasFloating,"Native toolbar docking restored after undocking");
            view.Close(); StudioPreferenceStore.Flush();
            Check(File.Exists(StudioPreferenceStore.SettingsPath+".bak"),"Atomic replacement retains backup");
            Check(!Directory.GetFiles(folder,"*.tmp").Any(),"No temporary writes left behind");
            File.WriteAllText(result,"PASS "+checks+" preference assertions");EditorApplication.Exit(0);
        }
        catch(Exception ex){File.WriteAllText(result,"FAIL after "+checks+" assertions\n"+ex);EditorApplication.Exit(1);}
    }
    public static void VerifyFreshLaunch()
    {
        try
        {
            Check(StudioSettings.Current.undoLimit==73,"Settings survive fresh editor launch");
            Check(StudioShortcuts.Label(StudioShortcut.Edge)=="F6","Dedicated hotkeys survive fresh editor launch");
            Check(Mathf.Abs(ToolChannelPane.GetFloatDefault("Bevel Edges","Width",0)-.27f)<.0001f,"Tool defaults survive fresh editor launch");
            Check(StudioPreferenceStore.GetString("Studio.Mode")=="Animation","Last mode survives fresh editor launch");
            Check(StudioPreferenceStore.GetBounds("ToolChannelPane.Bounds",default).width==360,"Shared pane size survives fresh editor launch");
            File.WriteAllText(Path.Combine(Application.dataPath,"../preferences-fresh-launch-result.txt"),"PASS "+checks+" fresh-launch assertions");EditorApplication.Exit(0);
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(Application.dataPath,"../preferences-fresh-launch-result.txt"),"FAIL\n"+ex);EditorApplication.Exit(1);}
    }
}
#endif
