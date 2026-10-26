using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class MashBoxStudioViewport
    {
        private IMGUIContainer workspaceStats;
        private readonly GUIContent workspaceFpsContent = new GUIContent("— FPS", "Viewport FPS");
        private GUIStyle workspaceFpsStyle;
        private double workspaceSampleStart = -1, workspaceLastFrame = -1;
        private int workspaceSampleFrames;
        internal double WorkspaceFps { get; private set; }
        internal void BindWorkspaceStatistics()
        {
            BindViewportControl(ref workspaceStats, "mashbox-workspace-stats", DrawWorkspaceStats);
            workspaceStats.style.width = 80; workspaceStats.style.height = 24;
            workspaceStats.pickingMode = PickingMode.Ignore;
            UpdatePanelLayout(); UpdateWorkspaceStatisticsState();
        }
        private void LayoutWorkspaceStatistics(float rightWidth)
        {
            float right = PanelMargin + rightWidth + DividerWidth + 8;
            if (workspaceStats != null) { workspaceStats.style.top = ModelingPanelTop; workspaceStats.style.right = right; }
        }
        private void UpdateWorkspaceStatisticsState()
        {
            if (workspaceStats == null) return;
            bool visible = Studio && Studio.IsModelingMode && !StudioSettings.Current.hideViewportFps;
            workspaceStats.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) StopWorkspaceStatistics();
        }
        private void DrawWorkspaceStats()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (workspaceFpsStyle == null) workspaceFpsStyle = new GUIStyle(EditorStyles.label);
            // Reapply presentation after hot reload, which can retain the cached GUIStyle.
            workspaceFpsStyle.alignment = TextAnchor.MiddleRight;
            workspaceFpsStyle.fontSize = 11;
            workspaceFpsStyle.normal.textColor = new Color(.82f, .82f, .82f, 1);
            var color = GUI.contentColor;
            var tint = GUI.color;
            var enabled = GUI.enabled;
            GUI.enabled = true;
            GUI.color = Color.white;
            GUI.contentColor = Color.black;
            workspaceFpsStyle.Draw(new Rect(1, 1, 79, 24), workspaceFpsContent, false, false, false, false);
            GUI.contentColor = Color.white;
            workspaceFpsStyle.Draw(new Rect(0, 0, 79, 24), workspaceFpsContent, false, false, false, false);
            GUI.contentColor = color;
            GUI.color = tint;
            GUI.enabled = enabled;
        }
        internal void RecordWorkspaceFrame(double now)
        {
            if (!Studio || !Studio.IsModelingMode || StudioSettings.Current.hideViewportFps) return;
            if (workspaceLastFrame < 0 || now < workspaceLastFrame || now - workspaceLastFrame > 1)
            // Sample only naturally requested viewport frames. Idle retains the
            // last reading and starts a fresh sample when interaction resumes.
            { workspaceSampleStart = now; workspaceSampleFrames = 0; }
            else
            {
                workspaceSampleFrames++;
                double elapsed = now - workspaceSampleStart;
                if (elapsed >= .5)
                {
                    WorkspaceFps = workspaceSampleFrames / elapsed;
                    workspaceFpsContent.text = System.Math.Ceiling(WorkspaceFps).ToString("F0") + " FPS";
                    workspaceSampleStart = now; workspaceSampleFrames = 0;
                }
            }
            workspaceLastFrame = now;
        }
        private void StopWorkspaceStatistics()
        {
            workspaceSampleStart = workspaceLastFrame = -1; workspaceSampleFrames = 0; WorkspaceFps = 0;
            workspaceFpsContent.text = "— FPS";
        }
    }
}
