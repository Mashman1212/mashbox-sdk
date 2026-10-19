using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class MashBoxStudioViewport
    {
        private IMGUIContainer workspaceStatsButton, workspaceStats;
        private bool workspaceStatsTicking;
        private double workspaceSampleStart = -1, workspaceLastFrame = -1;
        private int workspaceSampleFrames;
        internal double WorkspaceFps { get; private set; }
        internal void BindWorkspaceStatistics()
        {
            BindViewportControl(ref workspaceStatsButton,"mashbox-workspace-stats-button",DrawWorkspaceStatsButton);
            BindViewportControl(ref workspaceStats,"mashbox-workspace-stats",DrawWorkspaceStats);
            workspaceStatsButton.style.width = workspaceStatsButton.style.height = 24;
            workspaceStats.style.width = 132; workspaceStats.style.height = 54;
            workspaceStats.pickingMode = PickingMode.Ignore;
            workspaceStats.style.borderLeftWidth = workspaceStats.style.borderRightWidth = 1;
            workspaceStats.style.borderTopWidth = workspaceStats.style.borderBottomWidth = 1;
            UpdatePanelLayout(); UpdateWorkspaceStatisticsState();
        }
        private void LayoutWorkspaceStatistics(float rightWidth)
        {
            float right = PanelMargin + rightWidth + DividerWidth + 8;
            if(workspaceStatsButton != null) { workspaceStatsButton.style.top = ModelingPanelTop; workspaceStatsButton.style.right = right; }
            if(workspaceStats != null) { workspaceStats.style.top = ModelingPanelTop + 32; workspaceStats.style.right = right; }
        }
        private void UpdateWorkspaceStatisticsState()
        {
            if(workspaceStatsButton == null) return;
            bool visible = panelsModeling && StudioSettings.Current.showWorkspaceFps;
            workspaceStatsButton.style.display = panelsModeling ? DisplayStyle.Flex : DisplayStyle.None;
            workspaceStats.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            var palette = StudioTheme.Current;
            workspaceStats.style.backgroundColor = palette.Panel;
            workspaceStats.style.borderLeftColor = workspaceStats.style.borderRightColor = palette.Border;
            workspaceStats.style.borderTopColor = workspaceStats.style.borderBottomColor = palette.Border;
            if(visible && !workspaceStatsTicking) { EditorApplication.update += TickWorkspaceStatistics; workspaceStatsTicking = true; }
            else if(!visible) StopWorkspaceStatistics();
        }
        internal void SetWorkspaceStatsVisible(bool visible)
        {
            StudioSettings.Current.showWorkspaceFps = visible; StudioSettings.Save();
            UpdateWorkspaceStatisticsState(); Repaint();
        }
        private void DrawWorkspaceStatsButton()
        {
            var rect = new Rect(0,0,24,24);
            bool visible = StudioSettings.Current.showWorkspaceFps;
            bool next = GUI.Toggle(rect,visible,new GUIContent("","Workspace stats — show FPS"),StudioTheme.Styles.overlayButton);
            if(next != visible) SetWorkspaceStatsVisible(next);
            if(Event.current.type != EventType.Repaint) return;
            var color = visible ? StudioTheme.Current.Accent : StudioTheme.Current.Text;
            for(int i=0;i<3;i++) EditorGUI.DrawRect(new Rect(5+i*5,17-(4+i*4),3,4+i*4),color);
        }
        private void DrawWorkspaceStats()
        {
            using(new EditorGUILayout.VerticalScope(StudioTheme.Styles.modelPanel))
            {
                GUILayout.Label("WORKSPACE",StudioTheme.Styles.boldLabel);
                GUILayout.Label(WorkspaceFps > 0 ? WorkspaceFps.ToString("F1") + " FPS" : "Measuring FPS…",StudioTheme.Styles.label);
            }
        }
        internal void RecordWorkspaceFrame(double now)
        {
            if(!panelsModeling || !StudioSettings.Current.showWorkspaceFps) return;
            if(workspaceLastFrame < 0 || now < workspaceLastFrame || now-workspaceLastFrame > 1)
            { workspaceSampleStart = now; workspaceSampleFrames = 0; WorkspaceFps = 0; }
            else
            {
                workspaceSampleFrames++;
                double elapsed = now-workspaceSampleStart;
                if(elapsed >= .5)
                { WorkspaceFps = workspaceSampleFrames/elapsed; workspaceSampleStart = now; workspaceSampleFrames = 0; }
            }
            workspaceLastFrame = now;
        }
        private void TickWorkspaceStatistics() { if(this && panelsModeling && StudioSettings.Current.showWorkspaceFps) Repaint(); else StopWorkspaceStatistics(); }
        private void StopWorkspaceStatistics()
        {
            EditorApplication.update -= TickWorkspaceStatistics; workspaceStatsTicking = false;
            workspaceSampleStart = workspaceLastFrame = -1; workspaceSampleFrames = 0; WorkspaceFps = 0;
        }
    }
}
