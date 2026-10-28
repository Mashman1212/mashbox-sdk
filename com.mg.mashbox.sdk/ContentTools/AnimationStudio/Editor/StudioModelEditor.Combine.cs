using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Undo = MashBoxSDK.AnimationStudio.StudioUndo;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private const string CombineTool = "Combine Meshes";
        private enum CombinePivot { FirstSelected, AverageCenter }
        private ToolChannelPane combineToolPane;
        private readonly List<StudioModelDocument.Node> objectSelectionOrder = new List<StudioModelDocument.Node>();
        private int[] OrderedObjectSelection()
        {
            if (!Document) { objectSelectionOrder.Clear(); return Array.Empty<int>(); }
            var selected = objects.Where(i => i >= 0 && i < Document.nodes.Count).ToArray();
            var current = new HashSet<StudioModelDocument.Node>(selected.Select(i => Document.nodes[i]));
            objectSelectionOrder.RemoveAll(n => !current.Contains(n));
            foreach (int i in selected) if (!objectSelectionOrder.Contains(Document.nodes[i])) objectSelectionOrder.Add(Document.nodes[i]);
            return objectSelectionOrder.Select(n => Document.nodes.IndexOf(n)).ToArray();
        }
        private int[] SelectedCombineInputs => Document ? Document.CombineInputs(OrderedObjectSelection()) : Array.Empty<int>();
        private bool CanCombineObjects => Document && mode == ComponentMode.Object && Document.CanCombineMeshes(SelectedCombineInputs, out _);
        private void CloseCombineToolWindow() { if (combineToolPane) combineToolPane.Close(); combineToolPane = null; }
        private void OpenCombineTool()
        {
            if (!CanCombineObjects) throw new InvalidOperationException("Select at least two static mesh objects in Object mode.");
            if (combineToolPane) { combineToolPane.Focus(); return; }
            combineToolPane = ToolChannelPane.Open(CombineTool, DrawCombineTool, () => combineToolPane = null,
                new Vector2(365,255), new Vector2(320,235), execute: CombineSelectedObjects, canExecute: () => CanCombineObjects,
                failed: ex => { error = ex.Message; repaint(); }, owner: ToolChannelPaneOwner, history: PerformWorkspaceHistory, repeat: PerformRepeatLastAction);
        }
        private void DrawCombineTool()
        {
            EditorGUIUtility.labelWidth = 170;
            EditorGUI.BeginChangeCheck();
            bool channels = EditorGUILayout.Toggle(new GUIContent("Combine UV channels", "Combine matching numbered UV slots. Off assigns each input's populated UV sets to separate output slots, up to eight."), ToolChannelPane.GetFloatDefault(CombineTool,"CombineUVs",1) >= .5f);
            if (EditorGUI.EndChangeCheck()) ToolChannelPane.SetFloatDefault(CombineTool,"CombineUVs",channels ? 1 : 0);
            EditorGUI.BeginChangeCheck();
            bool keepName = EditorGUILayout.Toggle(new GUIContent("Use first selected name", "Use the first selected mesh name without adding Combined. Existing root names still receive a unique suffix when needed."), ToolChannelPane.GetFloatDefault(CombineTool,"KeepFirstName",0) >= .5f);
            if (EditorGUI.EndChangeCheck()) ToolChannelPane.SetFloatDefault(CombineTool,"KeepFirstName",keepName ? 1 : 0);
            ToolChannelPane.EnumParameter(CombineTool,"Pivot",new GUIContent("Pivot"),CombinePivot.FirstSelected);
            GUILayout.Label("First Selected uses the first selected mesh origin. Average Center averages the selected meshes' world-space bounds centers. Orientation follows the first selected mesh.",StudioTheme.Styles.wordWrappedMiniLabel);
            GUILayout.Label("Apply combines the current selection and keeps this pane open. Execute combines and closes. Source objects and emptied ancestor groups are removed; Undo restores them.",StudioTheme.Styles.wordWrappedMiniLabel);
            if (Document && !Document.CanCombineMeshes(SelectedCombineInputs,out string reason)) GUILayout.Label(reason,StudioTheme.Styles.wordWrappedMiniLabel);
        }
        private void CombineSelectedObjects()
        {
            if (!CanCombineObjects) throw new InvalidOperationException("Select at least two static mesh objects in Object mode.");
            FinishLiveTool(); FinishChannelEdit();
            var before = CaptureSelection();
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Combine meshes");
            try
            {
                var parameters = repeatingAction != null && repeatingAction.kind == RepeatKind.Combine ? repeatingAction.parameters : new Vector4(
                    ToolChannelPane.GetFloatDefault(CombineTool,"CombineUVs",1),ToolChannelPane.GetFloatDefault(CombineTool,"Pivot",0),ToolChannelPane.GetFloatDefault(CombineTool,"KeepFirstName",0),0);
                int combined = Document.CombineMeshes(SelectedCombineInputs, Transforms,parameters.x >= .5f,parameters.y >= .5f,parameters.z >= .5f);
                CancelMarkingMenu(); EndPivotSnap(); StopCutTool(); pivot = false; marquee = false; gestureUndo = -1;
                objects.Clear(); ClearAllComponentSelections(); objects.Add(combined); Active = combined; search = ""; GUIUtility.hotControl = 0;
                Rebuild(); Changed(); RecordSelectionChange(before,false,"Combine meshes"); EstablishSelectionBaseline();
                RememberAction(RepeatKind.Combine,parameters); error = null;
            }
            finally { Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup(); }
        }
    }
}
