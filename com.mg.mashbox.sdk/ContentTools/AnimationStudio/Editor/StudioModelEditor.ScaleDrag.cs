using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private sealed class ScaleDrag
        {
            internal int control;
            internal Vector3 center;
            internal Quaternion orientation;
            internal int[] nodes, vertices;
            internal Vector3[] positions, scales, meshVertices;
            internal Vector3 factor = Vector3.one;
        }
        private ScaleDrag scaleDrag;

        private ScaleDrag CaptureScaleDrag(int control, Vector3 center, Quaternion orientation, HashSet<int> indices, Vector3[] vertices)
        {
            var drag = new ScaleDrag { control = control, center = center, orientation = orientation };
            if (indices != null) { drag.vertices = indices.ToArray(); drag.meshVertices = vertices; }
            else
            {
                drag.nodes = objects.Where(i => !HasSelectedParent(i)).ToArray();
                drag.positions = drag.nodes.Select(i => Transforms[i].position).ToArray();
                drag.scales = drag.nodes.Select(i => Transforms[i].localScale).ToArray();
            }
            return drag;
        }

        // Check the squared magnitude as well: finite coordinates can still overflow
        // the bounds and distance calculations used for picking and camera framing.
        private static bool FiniteScalePoint(Vector3 value) => !float.IsNaN(value.sqrMagnitude) && !float.IsInfinity(value.sqrMagnitude);
        private static bool InvertibleScale(Vector3 value) => FiniteScalePoint(value) &&
            Mathf.Abs(value.x) >= 1e-6f && Mathf.Abs(value.y) >= 1e-6f && Mathf.Abs(value.z) >= 1e-6f;

        private bool ApplyScaleDrag(Vector3 factor)
        {
            var drag = scaleDrag;
            if (drag == null || !InvertibleScale(factor)) return false;
            Vector3 ScalePoint(Vector3 point) => drag.center + drag.orientation *
                Vector3.Scale(Quaternion.Inverse(drag.orientation) * (point - drag.center), factor);
            // ScaleHandle reports the total factor since mouse-down. Always apply it
            // to the captured geometry, never multiply it into the previous update.
            if (drag.vertices != null)
            {
                var transform = Transforms[Active];
                var values = (Vector3[])drag.meshVertices.Clone();
                foreach (int i in drag.vertices)
                {
                    var world = ScalePoint(transform.TransformPoint(drag.meshVertices[i]));
                    values[i] = transform.InverseTransformPoint(world);
                    if (!FiniteScalePoint(world) || !FiniteScalePoint(values[i])) return false;
                }
                PreserveFaceTopology();
                Undo.RegisterCompleteObjectUndo(ActiveMesh, "Scale mesh components");
                ActiveMesh.vertices = values; ActiveMesh.RecalculateNormals(); ActiveMesh.RecalculateBounds();
                if (ActiveMesh.uv.Length == ActiveMesh.vertexCount) ActiveMesh.RecalculateTangents();
                EditorUtility.SetDirty(ActiveMesh);
            }
            else
            {
                var positions = new Vector3[drag.nodes.Length]; var scales = new Vector3[drag.nodes.Length];
                // Validate the whole selection before changing any transform.
                for (int i = 0; i < drag.nodes.Length; i++)
                {
                    positions[i] = ScalePoint(drag.positions[i]);
                    scales[i] = Vector3.Scale(drag.scales[i], factor);
                    var parent = Transforms[drag.nodes[i]].parent;
                    var localPosition = parent ? parent.InverseTransformPoint(positions[i]) : positions[i];
                    if (!FiniteScalePoint(positions[i]) || !FiniteScalePoint(localPosition) || !InvertibleScale(scales[i])) return false;
                }
                Undo.RegisterCompleteObjectUndo(Document, "Scale model objects");
                for (int i = 0; i < drag.nodes.Length; i++)
                {
                    var target = Transforms[drag.nodes[i]];
                    target.position = positions[i]; target.localScale = scales[i];
                    Document.ReadTransform(drag.nodes[i], target);
                }
            }
            drag.factor = factor;
            Changed(drag.vertices != null);
            return true;
        }
    }
}
