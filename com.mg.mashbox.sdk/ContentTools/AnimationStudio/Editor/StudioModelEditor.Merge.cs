using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private ToolChannelPane mergeToolWindow;
        private static readonly GUIContent MergeDistanceContent = new GUIContent("Distance", "Maximum distance between selected vertices in world units. Connected distance groups merge to their center.");
        private float mergeDistance = ToolChannelPane.GetFloatDefault("Merge Vertices", "Distance", .001f);
        private bool CanMergeVertices => mode == ComponentMode.Vertex && ActiveMesh && CanEditComponents() &&
            !Document.nodes[Active].skinned && ActiveMesh.blendShapeCount == 0 && ActiveMesh.bindposes.Length == 0 && MergeSelectionCount() >= 2;
        private int MergeSelectionCount()
        {
            if (!ActiveMesh) return 0;
            var positions = ActiveMesh.vertices;
            var points = new HashSet<Vector3>();
            foreach (int i in Components) if (i >= 0 && i < positions.Length) points.Add(positions[i]);
            return points.Count;
        }
        private void CloseMergeToolWindow() { if (mergeToolWindow) mergeToolWindow.Close(); mergeToolWindow = null; }
        internal void MergeSelectedVertices()
        {
            if (!CanMergeVertices) throw new InvalidOperationException("Select at least two vertices on an editable static mesh.");
            if (MergeSelectionCount() == 2) { ApplyMergeVertices(); return; }
            if (mergeToolWindow) { mergeToolWindow.Focus(); return; }
            mergeToolWindow = ToolChannelPane.Open("Merge Vertices", DrawMergeVerticesTool, () => mergeToolWindow = null,
                execute: () => ApplyLiveTool(LiveTool.Merge), canExecute: () => CanRunLiveTool(LiveTool.Merge) && !float.IsNaN(mergeDistance) && !float.IsInfinity(mergeDistance) && mergeDistance >= 0,
                failed: ex => { error = ex.Message; repaint(); }, owner: ToolChannelPaneOwner,
                preview: () => QueueLiveTool(LiveTool.Merge), cancelPreview: () => CancelLiveTool(LiveTool.Merge));
            QueueLiveTool(LiveTool.Merge);
        }
        private void DrawMergeVerticesTool()
        {
            EditorGUIUtility.labelWidth = 106;
            EditorGUILayout.LabelField("Selected vertices", HasLiveTool(LiveTool.Merge) ? liveSelection.Length.ToString() : mode == ComponentMode.Vertex ? MergeSelectionCount().ToString() : "0");
            mergeDistance = ToolChannelPane.FloatParameter("Merge Vertices", "Distance", MergeDistanceContent, .001f, 0);
            bool valid = !float.IsNaN(mergeDistance) && !float.IsInfinity(mergeDistance) && mergeDistance >= 0;
            if (!valid) EditorGUILayout.HelpBox("Enter a finite, nonnegative distance.", MessageType.Warning);

        }
        private void ApplyMergeVertices() => ApplyLiveTool(LiveTool.Merge);
    }
}
