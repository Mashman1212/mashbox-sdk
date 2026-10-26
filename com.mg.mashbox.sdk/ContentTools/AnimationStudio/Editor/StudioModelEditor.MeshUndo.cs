using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private readonly System.Collections.Generic.Dictionary<Mesh, int> undoMeshVertexCounts = new System.Collections.Generic.Dictionary<Mesh, int>();
        private bool RestoreMeshDeformations(string action)
        {
            // These actions preserve vertex/index layout. Structural edits and
            // unknown native actions keep the full rebuild/rebind fallback.
            if (action != "Transform mesh components" && action != "Scale mesh components") return false;
            if (!Document || Document.nodes.Count != Transforms.Length || previewNodes.Length != Transforms.Length) return false;
            for (int i = 0; i < Transforms.Length; i++)
            {
                var node = Document.nodes[i]; var transform = Transforms[i];
                if (!ReferenceEquals(node, previewNodes[i]) || !transform || node.parent < -1 || node.parent >= Transforms.Length) return false;
                if (transform.parent != (node.parent >= 0 ? Transforms[node.parent] : null) ||
                    !transform.localPosition.Equals(node.position + node.positionOffset) ||
                    !StudioModelDocument.Node.SameRotation(transform.localRotation, node.rotation) || !transform.localScale.Equals(node.scale) ||
                    transform.gameObject.activeSelf != node.active) return false;
                if (!node.mesh) continue;
                var filter = transform.GetComponent<MeshFilter>();
                var skin = transform.GetComponent<SkinnedMeshRenderer>();
                if (filter ? filter.sharedMesh != node.mesh : !skin || skin.sharedMesh != node.mesh) return false;
            }
            // Snapshot the entire changed set before position publication updates
            // the tracked dirty versions (including shared mesh instances).
            var changed = Document.nodes.Select(n => n.mesh).Where(m => m).Distinct()
                .Where(m => !undoMeshVersions.TryGetValue(m, out int version) || version != EditorUtility.GetDirtyCount(m)).ToArray();
            if (changed.Length == 0 || changed.Any(m => !m.isReadable || !undoMeshVertexCounts.TryGetValue(m, out int count) || count != m.vertexCount)) return false;
            if (Document.nodes.Any(n => changed.Contains(n.mesh) && (n.skinned || n.blendShape != null))) return false;
            channelUndoGroup = -1; resetChannelFocus = true;
            ResetTargetWeldGesture(false); ResetCutGesture(); ResetHandleExtrusion();
            moveDrag = null; scaleDrag = null; multiComponentGesture = null;
            moveDragControl = 0; gestureUndo = -1; componentRotationControl = worldObjectRotationControl = 0;
            cutTopology = null; cutMesh = null;
            foreach (var mesh in changed)
            {
                // Native Undo restores CPU data; explicitly invalidate this mesh's
                // bound GPU stream too, just as the safe full rebuild path does.
                var positions = mesh.vertices; mesh.vertices = positions; mesh.UploadMeshData(false);
                occlusionIndices.Remove(mesh);
                var nodes = Enumerable.Range(0, Document.nodes.Count).Where(i => Document.nodes[i].mesh == mesh).ToArray();
                using (new ComponentTargetScope(this, nodes[0])) PositionsChanged(markerPositions: positions);
                foreach (int node in nodes.Skip(1))
                    if (ComponentTargetAt(node, false) != null)
                        using (new ComponentTargetScope(this, node)) RefreshComponentPositions(0, positions);
            }
            CaptureUndoMeshVersions(); Changed(false); return true;
        }
    }
}
