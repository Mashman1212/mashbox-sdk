using System;
using System.Collections.Generic;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
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
        private void CloseMergeToolWindow() { }
        internal void MergeToCenter()
        {
            StopTargetWeld();
            if (!CanMergeVertices) throw new InvalidOperationException("Select at least two vertices on an editable static mesh.");
            ApplyLiveTool(LiveTool.Merge);
            // The immediate transaction already recorded its source/result
            // selection alongside the meshes; the outer command adds no extra step.
            EstablishSelectionBaseline();
        }
        internal void MergeSelectedVertices() => MergeToCenter();
    }
}
