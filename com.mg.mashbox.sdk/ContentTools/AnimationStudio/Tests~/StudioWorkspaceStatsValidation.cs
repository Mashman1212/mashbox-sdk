using System;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;
public static class StudioWorkspaceStatsValidation
{
    static int checks;
    static void Check(bool pass,string message){if(!pass)throw new Exception(message);checks++;Debug.Log("PASS "+message);}
    public static void Run()
    {
        bool existed=EditorPrefs.HasKey(StudioSettings.PreferenceKey);string saved=EditorPrefs.GetString(StudioSettings.PreferenceKey);
        var view=ScriptableObject.CreateInstance<MashBoxStudioViewport>();
        try
        {
            view.position=new Rect(0,0,1400,800);view.BindModes(()=>{},true);view.BindWorkspaceStatistics();
            view.SetWorkspaceStatsVisible(false);
            var button=view.rootVisualElement.Q<IMGUIContainer>("mashbox-workspace-stats-button");
            var box=view.rootVisualElement.Q<IMGUIContainer>("mashbox-workspace-stats");
            Check(button.style.display.value==DisplayStyle.Flex && box.style.display.value==DisplayStyle.None,"FPS icon is present with box initially hidden");
            Check(button.style.height.value.value==24 && button.style.width.value.value==24 && button.style.top.value.value==32,"FPS icon matches the existing viewport toolbar buttons");
            float right=button.style.right.value.value;view.ResizePanel(2,450);
            Check(button.style.right.value.value>right && button.style.right.value.value==box.style.right.value.value,"FPS icon and box stay anchored beside Tool Options while resizing");
            view.SetWorkspaceStatsVisible(true);
            Check(box.style.display.value==DisplayStyle.Flex && box.pickingMode==PickingMode.Ignore,"FPS box toggles on and remains click-through");
            for(int i=0;i<=60;i++)view.RecordWorkspaceFrame(100+i/60d);
            Check(Math.Abs(view.WorkspaceFps-60)<.1,"Workspace FPS counts actual viewport frames over elapsed editor time");
            view.RecordWorkspaceFrame(110);
            Check(view.WorkspaceFps==0,"Long idle gaps reset FPS measurement");
            for(int i=1;i<=30;i++)view.RecordWorkspaceFrame(110+i/30d);
            Check(Math.Abs(view.WorkspaceFps-30)<.1,"FPS adapts to slower viewport rendering");
            view.BindModes(()=>{},false);
            Check(button.style.display.value==DisplayStyle.None && box.style.display.value==DisplayStyle.None && view.WorkspaceFps==0,"Leaving modeling hides stats and stops sampling");
            view.BindModes(()=>{},true);
            Check(box.style.display.value==DisplayStyle.Flex,"Returning to modeling restores visible FPS box");
            view.SetWorkspaceStatsVisible(false);view.RecordWorkspaceFrame(120);
            Check(view.WorkspaceFps==0,"Disabling stats stops frame sampling");
            StudioSettings.Reload();Check(!StudioSettings.Current.showWorkspaceFps,"FPS toggle preference persists");
            Debug.Log("STUDIO_WORKSPACE_STATS_VALIDATION_SUCCESS "+checks+" checks");
        }
        finally
        {
            Object.DestroyImmediate(view);
            if(existed)EditorPrefs.SetString(StudioSettings.PreferenceKey,saved);else EditorPrefs.DeleteKey(StudioSettings.PreferenceKey);
            StudioSettings.Reload();
        }
    }
}
