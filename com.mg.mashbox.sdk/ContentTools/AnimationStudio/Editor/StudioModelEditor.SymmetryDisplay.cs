using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private Mesh symmetryDisplayMesh;
        private StudioModelDocument.Node symmetryDisplayNode;
        private int symmetryDisplayVersion, symmetryDisplayNodeCount, symmetryDisplayAxis;
        private int[] symmetryDisplayPolygons, symmetryDisplayGeometry;
        private bool symmetryDisplayWorld, symmetryDisplayWeld;
        private ComponentMode symmetryDisplayMode;
        private Matrix4x4 symmetryDisplayMatrix;
        private readonly HashSet<int> symmetryDisplaySource = new HashSet<int>();
        private readonly HashSet<int> symmetryDisplaySelection = new HashSet<int>();
#if MASHBOX_STUDIO_TESTS
        internal int SymmetryDisplayBuilds;
#endif
        private void InvalidateSymmetryDisplay() { symmetryDisplayMesh = null; }

        // Display the primary and symmetry portions together without changing
        // the primary selection, its pivot, selection Undo or tool inputs.
        private HashSet<int> SymmetryDisplayComponents()
        {
            if (!symmetryEnabled || pivot || mode == ComponentMode.Object || !Document ||
                !ActiveMesh || Active < 0 || Components.Count == 0 || objects.Count != 1) return Components;
            var node = Document.nodes[Active];
            int version = EditorUtility.GetDirtyCount(ActiveMesh);
            var matrix = Transforms[Active].localToWorldMatrix;
            bool same = symmetryDisplayMesh == ActiveMesh && ReferenceEquals(symmetryDisplayNode,node) &&
                symmetryDisplayNodeCount == Document.nodes.Count && symmetryDisplayMode == mode &&
                symmetryDisplayAxis == symmetryAxis && symmetryDisplayWorld == symmetryWorld && symmetryDisplayWeld == weld &&
                ReferenceEquals(symmetryDisplayPolygons,node.cutPolygons) && ReferenceEquals(symmetryDisplayGeometry,node.geometryVertices) &&
                symmetryDisplayMatrix.Equals(matrix) && symmetryDisplaySource.SetEquals(Components);
            // Position-only drags keep their captured correspondence. Rebuild
            // once after release, rather than remapping the mesh every sample.
            if (same && (version == symmetryDisplayVersion || GUIUtility.hotControl != 0)) return symmetryDisplaySelection;

            symmetryDisplayMesh = ActiveMesh; symmetryDisplayNode = node; symmetryDisplayVersion = version;
            symmetryDisplayNodeCount = Document.nodes.Count; symmetryDisplayMode = mode;
            symmetryDisplayAxis = symmetryAxis; symmetryDisplayWorld = symmetryWorld; symmetryDisplayWeld = weld;
            symmetryDisplayPolygons = node.cutPolygons; symmetryDisplayGeometry = node.geometryVertices; symmetryDisplayMatrix = matrix;
            symmetryDisplaySource.Clear(); symmetryDisplaySource.UnionWith(Components);
            symmetryDisplaySelection.Clear(); symmetryDisplaySelection.UnionWith(Components);
            try
            {
                var symmetry = CaptureSymmetry();
                if (symmetry != null) symmetryDisplaySelection.UnionWith(SymmetryComponentSet(Components,symmetry));
            }
            catch (InvalidOperationException)
            {
                // Selection remains usable on unsupported/unmatched topology.
                // The existing edit preflight reports its actionable guidance.
            }
#if MASHBOX_STUDIO_TESTS
            SymmetryDisplayBuilds++;
#endif
            return symmetryDisplaySelection;
        }
    }
}
