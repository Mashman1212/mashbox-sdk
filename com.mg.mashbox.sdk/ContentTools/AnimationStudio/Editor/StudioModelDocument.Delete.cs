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
        internal void DeleteObjects(IEnumerable<int> selected)
        {
            var remove = WithDescendants(selected);
            if (remove.Count == 0) return;
            // Resolve and validate every affected renderer before changing the document.
            Func<int, int> survivor = bone => {
                while (bone >= 0 && remove.Contains(bone)) bone = nodes[bone].parent;
                if (bone < 0) throw new InvalidOperationException("Cannot delete this rig's root while keeping its skinned mesh: no surviving parent can receive the skin weights.");
                return bone;
            };
            var worlds = new Dictionary<int, Matrix4x4>();
            Func<int, Matrix4x4> world = null;
            world = i => {
                if (worlds.TryGetValue(i, out var cached)) return cached;
                var n = nodes[i];
                return worlds[i] = (n.parent < 0 ? Matrix4x4.identity : world(n.parent)) *
                    Matrix4x4.TRS(n.positionOffset + n.position, n.rotation, n.scale);
            };
            var replacements = new Dictionary<int, Mesh>();
            try
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    var n = nodes[i];
                    if (remove.Contains(i) || !n.skinned) continue;
                    if (remove.Contains(n.rootBone)) survivor(n.rootBone);
                    if (!n.bones.Any(remove.Contains)) continue;
                    if (!n.mesh || n.mesh.bindposes.Length != n.bones.Length)
                        throw new InvalidOperationException("Cannot repair skin on '" + n.name + "': its bone and bind-pose counts differ.");
                    var poses = n.mesh.bindposes;
                    for (int b = 0; b < n.bones.Length; b++)
                    {
                        int old = n.bones[b];
                        if (!remove.Contains(old)) continue;
                        int parent = survivor(old);
                        var parentWorld = world(parent);
                        if (Mathf.Abs(parentWorld.determinant) < 1e-8f)
                            throw new InvalidOperationException("Cannot transfer skin weights to a parent with zero scale: " + nodes[parent].name);
                        // Keep the current deformation exactly: P * (inverse(P) * B * bind) == B * bind.
                        // Retain palette slots and all vertex influences, including >4 weights per vertex.
                        poses[b] = parentWorld.inverse * world(old) * poses[b];
                    }
                    var mesh = Instantiate(n.mesh); replacements.Add(i, mesh);
                    mesh.name = n.mesh.name; mesh.bindposes = poses;
                }
                foreach (var mesh in replacements.Values)
                    if (AssetDatabase.Contains(this)) AssetDatabase.AddObjectToAsset(mesh, this);
            }
            catch
            {
                foreach (var mesh in replacements.Values) if (mesh) DestroyImmediate(mesh, true);
                throw;
            }
            var remap = new int[nodes.Count]; int next = 0;
            for (int i = 0; i < nodes.Count; i++) remap[i] = remove.Contains(i) ? -1 : next++;
            Undo.RegisterCompleteObjectUndo(this, "Delete model objects and repair skin");
            var remaining = new List<Node>();
            // Resolve all old hierarchy indices before mutating any node's parent index.
            var repairedBones = nodes.Select((n,i) => remove.Contains(i) ? Array.Empty<int>() : n.bones.Select(b => b >= 0 ? remap[remove.Contains(b) ? survivor(b) : b] : b).ToArray()).ToArray();
            var repairedRoots = nodes.Select((n,i) => !remove.Contains(i) && n.rootBone >= 0 ? remap[remove.Contains(n.rootBone) ? survivor(n.rootBone) : n.rootBone] : -1).ToArray();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (remove.Contains(i)) continue;
                var node = nodes[i];
                if (node.parent >= 0) node.parent = remap[node.parent];
                node.rootBone = repairedRoots[i]; node.bones = repairedBones[i];
                if (replacements.TryGetValue(i, out var mesh)) node.mesh = mesh;
                remaining.Add(node);
            }
            // Retain old and new mesh subassets so Undo/Redo restores both the rig and its skin.
            nodes = remaining; EditorUtility.SetDirty(this);
        }
    }
}
