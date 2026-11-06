using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioModelDocument
    {
        internal int[] DuplicateObjects(IEnumerable<int> selected)
        {
            var included = WithDescendants(selected);
            if (included.Count == 0) return System.Array.Empty<int>();
            // Include descendants once, even when both parent and child are selected.
            var sourceIndices = included.OrderBy(i => i).ToArray();
            var remap = sourceIndices.Select((source, index) => (source, index: nodes.Count + index)).ToDictionary(p => p.source, p => p.index);
            int Remap(int index) => remap.TryGetValue(index, out int copy) ? copy : index;
            var copies = new List<Node>();
            var roots = new List<int>();
            try
            {
                foreach (int index in sourceIndices)
                {
                    var n = nodes[index];
                    if (!string.IsNullOrEmpty(n.anchorType) && !included.Contains(n.parent))
                        throw new System.InvalidOperationException("Create another anchor type, or duplicate its parent model to keep anchor names exact.");
                    var copy = new Node {
                        spline = n.spline?.Copy(), name = n.name, vehiclePart = n.vehiclePart?.Copy(), anchorType = n.anchorType, sourcePath = n.sourcePath, sourceNodePath = n.sourceNodePath, sourceMeshAsset = n.sourceMeshAsset, parent = Remap(n.parent),
                        position = n.position, positionOffset = n.positionOffset, rotation = n.rotation,
                        scale = n.scale, pivotRotation = n.pivotRotation, frozenRotation = n.frozenRotation, frozenPivotBaseline = n.frozenPivotBaseline, active = n.active, skinned = n.skinned,
                        materials = n.materials?.ToArray() ?? System.Array.Empty<Material>(), previewTextureSlice = n.previewTextureSlice, vertexAOBaked = n.vertexAOBaked,
                        bones = n.bones?.Select(Remap).ToArray() ?? System.Array.Empty<int>(), rootBone = Remap(n.rootBone),
                        normalGroups = n.normalGroups?.ToArray(), geometryVertices = n.geometryVertices?.ToArray(), subdivision = n.subdivision?.Copy(), modifiers = n.modifiers?.Copy(), cutPolygons = n.cutPolygons?.ToArray(), cutTriangles = n.cutTriangles?.ToArray(), blendShape = n.blendShape?.Copy()
                    };
                    copy.constraints = n.constraints?.Select(c => c.Copy()).ToList() ?? new List<StudioModelConstraintData>();
                    copies.Add(copy);
                    if (n.mesh) { copy.mesh = Instantiate(n.mesh); copy.mesh.name = n.mesh.name; }
                    if (!included.Contains(n.parent))
                    {
                        copy.name = ObjectNames.GetUniqueName(nodes.Concat(copies.Take(copies.Count - 1)).Where(s => s.parent == copy.parent).Select(s => s.name).ToArray(), n.name);
                        roots.Add(remap[index]);
                    }
                }
            }
            catch
            {
                foreach (var copy in copies) if (copy.mesh) DestroyImmediate(copy.mesh);
                throw;
            }
            var constraintIds = sourceIndices.Select((source, i) => (oldId: nodes[source].constraintId, newId: copies[i].constraintId)).Where(p => !string.IsNullOrEmpty(p.oldId)).ToDictionary(p => p.oldId, p => p.newId);
            foreach(var copy in copies) foreach(var op in copy.modifiers?.operations??new List<StudioModifierOperation>())
                if(op.kind=="Sweep"&&op.sweep!=null)
                {
                    if(!string.IsNullOrEmpty(op.sweep.profileAncestorId)&&constraintIds.TryGetValue(op.sweep.profileAncestorId,out var profileAncestor))op.sweep.profileAncestorId=profileAncestor;
                    if(!string.IsNullOrEmpty(op.sweep.profileId)&&constraintIds.TryGetValue(op.sweep.profileId,out var profile))op.sweep.profileId=profile;
                    if(constraintIds.TryGetValue(op.sweep.sourceId,out var source))op.sweep.sourceId=source;
                    if(!string.IsNullOrEmpty(op.sweep.ancestorId)&&constraintIds.TryGetValue(op.sweep.ancestorId,out var ancestor))op.sweep.ancestorId=ancestor;
                }
            foreach (var copy in copies) foreach (var c in copy.constraints)
            {
                foreach (var s in c.sources) if (!string.IsNullOrEmpty(s.nodeId) && constraintIds.TryGetValue(s.nodeId, out var id)) s.nodeId = id;
                if (!string.IsNullOrEmpty(c.worldUpId) && constraintIds.TryGetValue(c.worldUpId, out var up)) c.worldUpId = up;
            }
            Undo.RegisterCompleteObjectUndo(this, "Duplicate model objects");
            nodes.AddRange(copies);
            // Keep mesh copies in memory for Undo/Redo. Explicit Save attaches them to the asset.
            EditorUtility.SetDirty(this);
            return roots.ToArray();
        }
    }
}
