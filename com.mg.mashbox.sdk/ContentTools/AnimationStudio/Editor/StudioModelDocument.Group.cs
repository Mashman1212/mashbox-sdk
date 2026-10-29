using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioModelDocument
    {
        internal int GroupObjects(int[] roots, Transform[] transforms, Vector3 center)
        {
            if (roots.Length == 0 || transforms.Length != nodes.Count || !FiniteHierarchyVector(center))
                throw new InvalidOperationException("Select objects to group.");
            int parent = nodes[roots[0]].parent;
            if (roots.Any(i => nodes[i].parent != parent)) parent = -1;
            var parentTransform = parent < 0 ? null : transforms[parent];
            var placement = parentTransform ? parentTransform.InverseTransformPoint(center) : center;
            var world = (parentTransform ? parentTransform.localToWorldMatrix : Matrix4x4.identity) * Matrix4x4.Translate(placement);
            if (Mathf.Abs(world.determinant) < 1e-8f) throw new InvalidOperationException("Cannot group under a zero scale.");
            var inverse = world.inverse;
            var changes = new List<ParentChange>();
            var names = new HashSet<string>();
            // Plan every child before adding the new parent or registering history.
            foreach (int i in roots)
            {
                var node = nodes[i];
                if (!string.IsNullOrEmpty(node.anchorType) && !names.Add(node.name))
                    throw new InvalidOperationException("Anchor names must stay unique under the new group.");
                names.Add(node.name);
                var matrix = inverse * transforms[i].localToWorldMatrix;
                Vector3 position = matrix.GetColumn(3), x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
                var scale = new Vector3(x.magnitude, y.magnitude, z.magnitude);
                if (!FiniteHierarchyVector(position) || !FiniteHierarchyVector(scale) || Mathf.Min(scale.x, scale.y, scale.z) < 1e-7f)
                    throw new InvalidOperationException("Cannot preserve a zero or invalid object scale while grouping.");
                if (matrix.determinant < 0) scale.x = -scale.x;
                var rotation = Quaternion.LookRotation(z / scale.z, y / scale.y);
                var rebuilt = Matrix4x4.TRS(position, rotation, scale);
                for (int row=0; row<4; row++) for (int col=0; col<4; col++)
                    if (Mathf.Abs(matrix[row,col]-rebuilt[row,col]) > 1e-4f*Mathf.Max(1,Mathf.Abs(matrix[row,col])))
                        throw new InvalidOperationException("Grouping here would skew an object. Freeze its nonuniform scale before grouping.");
                var offset = node.positionOffset == Vector3.zero ? Vector3.zero : inverse.MultiplyPoint3x4(
                    transforms[i].parent ? transforms[i].parent.TransformPoint(node.positionOffset) : node.positionOffset);
                var rotationInWorld = (parentTransform ? parentTransform.rotation : Quaternion.identity) * rotation;
                changes.Add(new ParentChange { index=i, position=position-offset, offset=offset, rotation=rotation, scale=scale,
                    pivotRotation=Quaternion.Inverse(rotationInWorld)*transforms[i].rotation*node.pivotRotation });
            }
            int group = nodes.Count;
            var selected = new HashSet<int>(roots);
            var oldOrder = OrderedChildren(parent);
            int insertion = oldOrder.TakeWhile(i => !selected.Contains(i)).Count(i => !selected.Contains(i));
            var order = oldOrder.Where(i => !selected.Contains(i)).ToList();
            order.Insert(Math.Min(insertion, order.Count), group);
            var oldParents = roots.Select(i => nodes[i].parent).Distinct().Where(p => p != parent)
                .ToDictionary(p => p, p => OrderedChildren(p).Where(i => !selected.Contains(i)).ToArray());
            Undo.RegisterCompleteObjectUndo(this, "Group objects");
            nodes.Add(new Node { name=ObjectNames.GetUniqueName(nodes.Where(n=>n.parent==parent).Select(n=>n.name).ToArray(), "Group"),
                parent=parent, position=placement, rotation=Quaternion.identity, scale=Vector3.one, pivotRotation=Quaternion.identity });
            for (int k=0; k<changes.Count; k++)
            {
                var change=changes[k];var node=nodes[change.index];
                node.parent=group;node.position=change.position;node.positionOffset=change.offset;
                node.rotation=change.rotation;node.scale=change.scale;node.pivotRotation=change.pivotRotation;node.siblingOrder=k;
            }
            for (int k=0; k<order.Count; k++) nodes[order[k]].siblingOrder=k;
            foreach (var siblings in oldParents.Values) for (int k=0; k<siblings.Length; k++) nodes[siblings[k]].siblingOrder=k;
            EditorUtility.SetDirty(this);return group;
        }
    }
}
