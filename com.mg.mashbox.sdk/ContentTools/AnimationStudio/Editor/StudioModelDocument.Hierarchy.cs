using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioModelDocument
    {
        internal HashSet<int> WithDescendants(IEnumerable<int> selected)
        {
            var included = new HashSet<int>(selected.Where(i => i >= 0 && i < nodes.Count));
            var children = new Dictionary<int, List<int>>();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!children.TryGetValue(nodes[i].parent, out var list)) children[nodes[i].parent] = list = new List<int>();
                list.Add(i);
            }
            var pending = new Queue<int>(included);
            while (pending.Count > 0)
                if (children.TryGetValue(pending.Dequeue(), out var list))
                    foreach (int child in list) if (included.Add(child)) pending.Enqueue(child);
            return included;
        }
        private sealed class ParentChange
        {
            internal int index;
            internal Vector3 position, offset, scale;
            internal Quaternion rotation, pivotRotation;
        }
        internal bool CanReparent(IEnumerable<int> selection, int parent, Transform[] transforms, out string reason)
        {
            return PlanReparent(selection, parent, transforms, out _, out reason);
        }
        private bool PlanReparent(IEnumerable<int> selection, int parent, Transform[] transforms, out List<ParentChange> changes, out string reason)
        {
            changes = new List<ParentChange>(); reason = null;
            if (parent < -1 || parent >= nodes.Count || transforms.Length != nodes.Count) { reason = "The hierarchy changed. Start the drag again."; return false; }
            var selected = new HashSet<int>(selection.Where(i => i >= 0 && i < nodes.Count));
            if (selected.Count == 0) { reason = "Select an object to reparent."; return false; }
            for (int p = parent; p >= 0; p = nodes[p].parent)
                if (selected.Contains(p)) { reason = "An object cannot be parented to itself or its descendants."; return false; }
            bool SelectedAncestor(int i)
            { for (int p = nodes[i].parent; p >= 0; p = nodes[p].parent) if (selected.Contains(p)) return true; return false; }
            var roots = selected.Where(i => !SelectedAncestor(i) && nodes[i].parent != parent).OrderBy(i => i).ToArray();
            var target = parent < 0 ? null : transforms[parent];
            var inverse = target ? target.worldToLocalMatrix : Matrix4x4.identity;
            var names = new HashSet<string>(nodes.Where((n,i) => n.parent == parent && !roots.Contains(i)).Select(n => n.name));
            foreach (int i in roots)
            {
                var node = nodes[i];
                if (!string.IsNullOrEmpty(node.anchorType) && !names.Add(node.name)) { reason = "This parent already has " + node.name + ". Anchor names must stay unique."; return false; }
                names.Add(node.name);
                var matrix = inverse * transforms[i].localToWorldMatrix;
                Vector3 position = matrix.GetColumn(3), x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
                var scale = new Vector3(x.magnitude, y.magnitude, z.magnitude);
                if (!FiniteHierarchyVector(position) || !FiniteHierarchyVector(scale) || Mathf.Min(scale.x, scale.y, scale.z) < 1e-7f)
                { reason = "Cannot preserve this transform under a zero or invalid parent scale."; return false; }
                if (matrix.determinant < 0) scale.x = -scale.x;
                var rotation = Quaternion.LookRotation(z / scale.z, y / scale.y);
                var rebuilt = Matrix4x4.TRS(position, rotation, scale);
                for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++)
                    if (Mathf.Abs(matrix[row,col] - rebuilt[row,col]) > 1e-4f * Mathf.Max(1, Mathf.Abs(matrix[row,col])))
                    { reason = "This parent would skew the object. Freeze its nonuniform scale before reparenting."; return false; }
                var offset = Vector3.zero;
                if (node.positionOffset != Vector3.zero)
                    offset = inverse.MultiplyPoint3x4(transforms[i].parent ? transforms[i].parent.TransformPoint(node.positionOffset) : node.positionOffset);
                var worldRotation = (target ? target.rotation : Quaternion.identity) * rotation;
                changes.Add(new ParentChange { index = i, position = position - offset, offset = offset, rotation = rotation, scale = scale,
                    pivotRotation = Quaternion.Inverse(worldRotation) * transforms[i].rotation * node.pivotRotation });
            }
            if (changes.Count == 0) { reason = "Already under this parent."; return false; }
            return true;
        }
        private static bool FiniteHierarchyVector(Vector3 value) => !float.IsNaN(value.sqrMagnitude) && !float.IsInfinity(value.sqrMagnitude);
        internal void ReparentObjects(IEnumerable<int> selected, int parent, Transform[] transforms)
        {
            if (!PlanReparent(selected, parent, transforms, out var changes, out var reason)) throw new InvalidOperationException(reason);
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(parent < 0 ? "Unparent objects" : "Parent objects");
            Undo.RegisterCompleteObjectUndo(this, "Reparent objects");
            foreach (var change in changes)
            {
                var node = nodes[change.index]; node.parent = parent;
                node.position = change.position; node.positionOffset = change.offset;
                node.rotation = change.rotation; node.scale = change.scale; node.pivotRotation = change.pivotRotation;
            }
            EditorUtility.SetDirty(this); Undo.CollapseUndoOperations(group);
        }
    }
}
