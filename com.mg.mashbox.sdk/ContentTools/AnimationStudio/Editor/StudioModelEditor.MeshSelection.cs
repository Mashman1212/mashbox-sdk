using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private ToolChannelPane selectionActionPane;
        private bool executingSelectionAction;
        private void CloseSelectionActionPane() { if (selectionActionPane) selectionActionPane.Close(); selectionActionPane = null; }
        private void ChooseSelectionAction(string title, Action action, string guidance)
        {
            try { action(); }
            catch (InvalidOperationException)
            {
                OpenSelectionActionPane(title, action, guidance);
                throw;
            }
        }
        private void OpenSelectionActionPane(string title, Action action, string guidance)
        {
            selectionActionPane = ToolChannelPane.Open(title, () => {
                GUILayout.Label(guidance, StudioTheme.Styles.wordWrappedMiniLabel);
                GUILayout.Space(6);
                GUILayout.Label("Select components in the viewport, then Execute or Apply.", StudioTheme.Styles.wordWrappedMiniLabel);
            }, () => selectionActionPane = null, execute: () => {
                Exception failure = null;
                executingSelectionAction = true;
                try { Run(() => { try { action(); } catch (Exception e) { failure = e; throw; } }); }
                finally { executingSelectionAction = false; }
                if (failure != null) throw failure;
            }, canExecute: () => true, failed: e => { if (error != e.Message) error = e.Message; repaint(); },
                owner: ToolChannelPaneOwner, history: PerformWorkspaceHistory, repeat: PerformRepeatLastAction);
            FocusToolOptions?.Invoke(); repaint();
        }
        // Entry-point guidance belongs to commands, not disabled UI. Do not
        // convert arbitrary component IDs between modes or queue a future edit.
        private void PrepareMeshSelection(ComponentMode preferred, Func<bool> ready, string instruction, params ComponentMode[] allowed)
        {
            if (!allowed.Contains(mode))
            {
                if (Document) ChangeComponentMode(preferred, null);
                else mode = preferred;
            }
            FinishSelectionPaint(); StopTargetWeld(); StopCutTool(); EndPivotSnap(); CancelMarkingMenu();
            CameraOptionsVisible = LightingOptionsVisible = false; pivot = false; tool = Tool.None;
            if (!ready())
            {
                if (!executingSelectionAction) { CloseLiveToolPanes(); CloseCombineToolWindow(); }
                throw new InvalidOperationException(instruction);
            }
            repaint(); toolOptions?.Repaint();
        }
    }
}
