using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using MashBoxSDK.AnimationStudio;

public static class StudioToolbarPersistenceValidation
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static int checks; static MashBoxStudioViewport view;
    [Serializable] class NativeState { public string id,containerId,contents;public int index,dockPosition,layout;public bool displayed,floating,collapsed; }
    [Serializable] class Entry { public string id;public int index;public NativeState state; }
    [Serializable] class Layout { public bool enabled;public List<Entry> items; }
    [Serializable] class Snapshot { public string layout,definitions;public List<NativeState> states=new List<NativeState>();public Vector2 floatingPosition;public string floatingId; }
    [Serializable] class PreferenceEntry { public string key,value; }
    [Serializable] class Preferences { public List<PreferenceEntry> entries; }
    static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+name));
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static void Call(string method)=>typeof(MashBoxStudioViewport).GetMethod(method,F).Invoke(view,null);
    static bool Queued=>(bool)typeof(MashBoxStudioViewport).GetField("storedToolbarSaveQueued",F).GetValue(view);
    static NativeState State(Overlay overlay)=>JsonUtility.FromJson<NativeState>(JsonUtility.ToJson(new SaveData(overlay)));
    static void Open()
    {
        AnimationStudioWindow.Open();var studio=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
        typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(studio,new object[]{true});
        view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(x=>x.Studio==studio);Call("InitializeStudioToolbars");
    }
    static void Dock(Overlay overlay,string container,int index,int layout)
    {
        view.overlayCanvas.RestoreOverlay(overlay,JsonUtility.FromJson<SaveData>("{\"containerId\":\""+container+"\",\"index\":"+index+",\"dockPosition\":0,\"displayed\":true,\"layout\":"+layout+"}"));
    }
    static void FlushQueued(){Check(Queued,"Native layout action did not queue settings save");Call("FlushStoredToolbarSave");Check(!Queued,"Save callback did not clear its queue");}
    static void AssertSaved()
    {
        var saved=JsonUtility.FromJson<Layout>(StudioPreferenceStore.GetString("Studio.Toolbars"));
        foreach(var overlay in view.overlayCanvas.overlays.Where(x=>x is ToolbarOverlay&&x.GetType().Assembly==typeof(MashBoxStudioViewport).Assembly))
        {
            var native=State(overlay);var stored=saved.items.Single(x=>x.id==overlay.id).state;
            Check(stored.containerId==native.containerId,"Dock side not saved: "+overlay.id);
            Check(stored.index==native.index,"Dock order not saved: "+overlay.id);
            Check(stored.dockPosition==native.dockPosition,"Dock section not saved: "+overlay.id);
            Check(stored.layout==native.layout,"Orientation not saved: "+overlay.id);
            Check(stored.contents==native.contents,"Native toolbar state not saved: "+overlay.id);
        }
    }
    public static void Write(){EditorApplication.delayCall+=()=>Run(false);}
    public static void Read(){EditorApplication.delayCall+=()=>Run(true);}
    static void Run(bool read)
    {
        string result;
        try
        {
            if(!read)StudioPreferenceStore.SetString("Studio.Toolbars","{\"enabled\":false,\"items\":[]}");
            Open();
            if(!read)
            {
                var definition=StudioCustomToolbars.Create("Saved toolbar");definition.commands.AddRange(new[]{"Modify/Move","Modify/Rotate"});
                StudioCustomToolbars.Save(new List<StudioCustomToolbarDefinition>{definition});Call("FlushStoredToolbarSave");
                var bars=view.overlayCanvas.overlays.Where(x=>x is ToolbarOverlay&&x.GetType().Assembly==typeof(MashBoxStudioViewport).Assembly).ToArray();
                Check(bars.Length>=6,"Toolbar registry missing custom/default overlays");
                foreach(var place in new[]{"overlay-toolbar__left","overlay-toolbar__right","overlay-toolbar__bottom","overlay-toolbar__top"})
                {Dock(bars[0],place,0,place.EndsWith("left")||place.EndsWith("right")?2:1);FlushQueued();AssertSaved();}
                Dock(bars[1],"overlay-toolbar__top",0,1);Dock(bars[0],"overlay-toolbar__top",1,1);FlushQueued();
                Call("CancelStoredToolbarSave");Dock(bars[0],"overlay-toolbar__top",0,1);FlushQueued();AssertSaved();
                var handlers=(System.Collections.IDictionary)typeof(MashBoxStudioViewport).GetField("toolbarDockingHandlers",F).GetValue(view);
                Check(handlers.Count==bars.Length,"Native docking-completed hooks missing: "+handlers.Count+" versus "+bars.Length);
                for(int i=0;i<20;i++)Call("QueueStoredToolbarSave");
                int queued=(EditorApplication.delayCall?.GetInvocationList()??Array.Empty<Delegate>()).Count(x=>ReferenceEquals(x.Target,view)&&x.Method.Name=="FlushStoredToolbarSave");
                Check(queued==1,"Layout events were not coalesced");Call("FlushStoredToolbarSave");
                bars[2].displayed=!bars[2].displayed;FlushQueued();AssertSaved();
                bars[3].collapsed=!bars[3].collapsed;FlushQueued();AssertSaved();
                view.overlayCanvas.overlaysEnabled=!view.overlayCanvas.overlaysEnabled;FlushQueued();
                Check(JsonUtility.FromJson<Layout>(StudioPreferenceStore.GetString("Studio.Toolbars")).enabled==view.overlayCanvas.overlaysEnabled,"Canvas enabled state not saved");
                view.overlayCanvas.overlaysEnabled=false;Call("FlushStoredToolbarSave");
                Dock(bars[0],"overlay-toolbar__left",0,2);Dock(bars[1],"overlay-toolbar__bottom",0,1);
                var floating=bars.Single(x=>x.id==definition.id);floating.Undock();floating.floatingPosition=new Vector2(340,150);FlushQueued();AssertSaved();
                string raw=StudioPreferenceStore.GetString("Studio.Toolbars");
                bars[0].displayed=!bars[0].displayed;Check(Queued,"Visibility event missing before read-only restore");
                Call("RestoreStoredToolbars");Check(!Queued,"Restore left a pending write");Check(StudioPreferenceStore.GetString("Studio.Toolbars")==raw,"Restoration overwrote saved layout");
                StudioPreferenceStore.Flush();
                var disk=JsonUtility.FromJson<Preferences>(File.ReadAllText(StudioPreferenceStore.SettingsPath));
                Check(disk.entries.Single(x=>x.key=="Studio.Toolbars").value==raw,"Layout missing from settings JSON on disk");
                Check(disk.entries.Any(x=>x.key==StudioCustomToolbars.PreferenceKey),"Custom contents missing from settings JSON");
                var snapshot=new Snapshot{layout=raw,definitions=StudioPreferenceStore.GetString(StudioCustomToolbars.PreferenceKey),floatingPosition=floating.floatingPosition,floatingId=floating.id};
                snapshot.states.AddRange(bars.Select(State));File.WriteAllText(Out("toolbar-persistence-expected.json"),JsonUtility.ToJson(snapshot));
            }
            else
            {
                var snapshot=JsonUtility.FromJson<Snapshot>(File.ReadAllText(Out("toolbar-persistence-expected.json")));
                Check(StudioPreferenceStore.GetString("Studio.Toolbars")==snapshot.layout,"Fresh startup rewrote stored layout");
                Check(StudioPreferenceStore.GetString(StudioCustomToolbars.PreferenceKey)==snapshot.definitions,"Fresh startup changed custom contents");
                foreach(var expected in snapshot.states)
                {
                    var bar=view.overlayCanvas.overlays.Single(x=>x.id==expected.id);var actual=State(bar);
                    Check(actual.containerId==expected.containerId,"Fresh startup lost docking side: "+bar.id);
                    Check(actual.index==expected.index,"Fresh startup lost dock order: "+bar.id);
                    Check(actual.dockPosition==expected.dockPosition,"Fresh startup lost dock section: "+bar.id);
                    Check(actual.layout==expected.layout,"Fresh startup lost horizontal/vertical layout: "+bar.id);
                    Check(actual.displayed==expected.displayed,"Fresh startup lost visibility: "+bar.id);
                    Check(bar.collapsed==expected.collapsed,"Fresh startup lost collapsed state: "+bar.id);
                }
                var floating=view.overlayCanvas.overlays.Single(x=>x.id==snapshot.floatingId);
                Check(floating.floating,"Fresh startup lost floating toolbar");
                Check(Vector2.Distance(floating.floatingPosition,snapshot.floatingPosition)<1,"Fresh startup lost floating position");
            }
            result="PASS "+checks+(read?" fresh-process toolbar layout/contents restoration checks":" native layout-event/settings JSON/coalescing/read-only restoration checks");
        }catch(Exception ex){result=ex.ToString();}
        File.WriteAllText(Out(read?"toolbar-persistence-read-result.txt":"toolbar-persistence-write-result.txt"),result);
        EditorApplication.Exit(result.StartsWith("PASS")?0:1);
    }
}
