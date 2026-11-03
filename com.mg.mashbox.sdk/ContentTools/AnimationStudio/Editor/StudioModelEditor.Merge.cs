using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Linq;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private ToolChannelPane rangeWeldToolPane;
        private const string RangeWeldTool = "Weld";
        private bool CanRangeWeldVertices
        {
            get
            {
                if (mode != ComponentMode.Vertex || !ActiveMesh || !CanEditComponents() || Document.nodes[Active].skinned ||
                    ActiveMesh.blendShapeCount != 0 || ActiveMesh.bindposes.Length != 0) return false;
                var geometry = StudioSubdivisionData.Geometry(Document.nodes[Active]);
                return Components.Where(i => i >= 0 && i < geometry.Length).Select(i => geometry[i]).Distinct().Take(2).Count() == 2;
            }
        }
        private void OpenRangeWeldTool()
        {
            StopTargetWeld();
            PrepareMeshSelection(ComponentMode.Vertex, () => CanRangeWeldVertices,
                "Select at least two vertices on an editable static mesh to weld within a range.", ComponentMode.Vertex);
            if (rangeWeldToolPane) { QueueLiveTool(LiveTool.Weld); rangeWeldToolPane.Repaint(); rangeWeldToolPane.Focus(); return; }
            rangeWeldToolPane = ToolChannelPane.Open("Weld", DrawRangeWeldTool, () => rangeWeldToolPane = null,
                execute: () => ApplyLiveTool(LiveTool.Weld), canExecute: () => CanRunLiveTool(LiveTool.Weld),
                failed: ex => { error = ex.Message; repaint(); }, owner: ToolChannelPaneOwner,
                preview: () => QueueLiveTool(LiveTool.Weld), cancelPreview: () => CancelLiveTool(LiveTool.Weld),
                history: PerformWorkspaceHistory, repeat: PerformRepeatLastAction);
            QueueLiveTool(LiveTool.Weld);
        }
        private void DrawRangeWeldTool()
        {
            float range = ToolChannelPane.GetFloatDefault(RangeWeldTool, "Range", .001f);
            EditorGUI.BeginChangeCheck();
            float edited = StudioSceneUnits.Field(new GUIContent("Range", "Maximum world-space distance between selected vertices to join. Connected nearby vertices form one cluster; each cluster welds to its average position."), range);
            if (EditorGUI.EndChangeCheck() && !float.IsNaN(edited) && !float.IsInfinity(edited))
                ToolChannelPane.SetFloatDefault(RangeWeldTool, "Range", Mathf.Max(0, edited));
            GUILayout.Label("Only selected vertices within range are welded. Apply keeps the pane open; Cancel restores the unapplied preview.", StudioTheme.Styles.wordWrappedMiniLabel);
        }
        private bool CanMergeVertices => mode == ComponentMode.Vertex && ActiveMesh && CanEditComponents() &&
            !Document.nodes[Active].skinned && ActiveMesh.blendShapeCount == 0 && ActiveMesh.bindposes.Length == 0 && MergeSelectionCount() >= 2;
        private int MergeSelectionCount()
        {
            if (!ActiveMesh) return 0;
            var positions = ActiveMesh.vertices; var points = new HashSet<Vector3>();
            foreach (int i in Components) if (i >= 0 && i < positions.Length) points.Add(positions[i]);
            return points.Count;
        }
        // The shared live-tool completion hook remains; this immediate command
        // owns no distance field or floating parameter pane.
        private void CloseMergeToolWindow() { if (rangeWeldToolPane) rangeWeldToolPane.Close(); rangeWeldToolPane = null; }
        internal void MergeToCenter()
        {
            StopTargetWeld();
            PrepareMeshSelection(ComponentMode.Vertex, () => CanMergeVertices,
                "Select at least two vertices on an editable static mesh to merge.", ComponentMode.Vertex);
            ApplyLiveTool(LiveTool.Merge);
            // The immediate transaction already recorded its source/result
            // selection alongside the meshes; the outer command adds no extra step.
            EstablishSelectionBaseline();
        }
        internal void MergeSelectedVertices() => MergeToCenter();
    }
}
