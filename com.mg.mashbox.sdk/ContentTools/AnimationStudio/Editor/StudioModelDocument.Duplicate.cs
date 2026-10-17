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
                        name = n.name, anchorType = n.anchorType, sourcePath = n.sourcePath, parent = Remap(n.parent),
                        position = n.position, positionOffset = n.positionOffset, rotation = n.rotation,
                        scale = n.scale, pivotRotation = n.pivotRotation, active = n.active, skinned = n.skinned,
                        materials = n.materials?.ToArray() ?? System.Array.Empty<Material>(),
                        bones = n.bones?.Select(Remap).ToArray() ?? System.Array.Empty<int>(), rootBone = Remap(n.rootBone),
                        cutPolygons = n.cutPolygons?.ToArray(), cutTriangles = n.cutTriangles?.ToArray()
                    };
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
            Undo.RegisterCompleteObjectUndo(this, "Duplicate model objects");
            nodes.AddRange(copies);
            // Keep mesh copies in memory for Undo/Redo. Explicit Save attaches them to the asset.
            EditorUtility.SetDirty(this);
            return roots.ToArray();
        }
    }
}
