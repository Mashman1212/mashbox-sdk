using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using MashBoxSDK.AnimationStudio;

public static class StudioToolbarPlacementValidation
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    [Serializable] class Placement { public string containerId;public int index,dockPosition,layout;public bool floating; }
    static int checks;static MashBoxStudioViewport view;
    static void Check(bool yes,string message){if(!yes)throw new Exception(message);checks++;}
    static Placement State(Overlay overlay)=>JsonUtility.FromJson<Placement>(JsonUtility.ToJson(new SaveData(overlay)));
    static void Call(string method)=>typeof(MashBoxStudioViewport).GetMethod(method,F).Invoke(view,null);
    static Dictionary<string,Placement> Capture()=>view.overlayCanvas.overlays.Where(x=>x is ToolbarOverlay&&x.GetType().Assembly==typeof(MashBoxStudioViewport).Assembly).ToDictionary(x=>x.id,State);
    static void Unmoved(Dictionary<string,Placement> before)
    {
        foreach(var entry in before)
        {
            var actual=State(view.overlayCanvas.overlays.Single(x=>x.id==entry.Key));var expected=entry.Value;
            Check(actual.containerId==expected.containerId,"Moved dock: "+entry.Key);
            Check(actual.dockPosition==expected.dockPosition,"Changed dock group: "+entry.Key);
            Check(actual.index==expected.index,"Changed dock order: "+entry.Key);
            Check(actual.layout==expected.layout,"Changed layout: "+entry.Key);
        }
    }
    public static void Run(){EditorApplication.delayCall+=()=>{
        string result;
        try
        {
            AnimationStudioWindow.Open();var studio=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(studio,new object[]{true});
            view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(x=>x.Studio==studio);Call("InitializeStudioToolbars");
            var before=Capture();var definitions=StudioCustomToolbars.Read();var added=StudioCustomToolbars.Create("Left default");added.commands.Add("Modify/Move");definitions.Add(added);StudioCustomToolbars.Save(definitions);
            var overlay=view.overlayCanvas.overlays.Single(x=>x.id==added.id);var state=State(overlay);
            Check(state.containerId=="overlay-toolbar__top","New toolbar did not use top dock");
            Check(state.dockPosition==0,"New toolbar used right/trailing dock group");Check(state.layout==1,"New toolbar not horizontal");Unmoved(before);
            before=Capture();added.name="Renamed left toolbar";added.commands.Add("Modify/Rotate");StudioCustomToolbars.Save(definitions);Unmoved(before);
            // An explicit user placement remains authoritative, including the right dock.
            view.overlayCanvas.RestoreOverlay(overlay,JsonUtility.FromJson<SaveData>("{\"containerId\":\"overlay-toolbar__right\",\"dockPosition\":0,\"index\":0,\"displayed\":true,\"layout\":2}"));
            Call("FlushStoredToolbarSave");before=Capture();added.commands.Add("Modify/Scale");StudioCustomToolbars.Save(definitions);Unmoved(before);
            Call("FlushStoredToolbarSave");string saved=StudioPreferenceStore.GetString("Studio.Toolbars");Call("RestoreStoredToolbars");Unmoved(before);
            Check(saved==StudioPreferenceStore.GetString("Studio.Toolbars"),"Reload rewrote saved placement");
            result="PASS "+checks+" left/default dock and existing/user placement preservation checks";
        }catch(Exception e){result=e.ToString();}
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../toolbar-placement-result.txt")),result);EditorApplication.Exit(result.StartsWith("PASS")?0:1);
    };}
}
