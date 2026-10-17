using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioModelDocument
    {
        internal void DeleteObjects(IEnumerable<int> selected)
        {
            var remove = WithDescendants(selected);
            if (remove.Count == 0) return;
            for (int i = 0; i < nodes.Count; i++)
                if (!remove.Contains(i) && nodes[i].skinned && (remove.Contains(nodes[i].rootBone) || nodes[i].bones.Any(remove.Contains)))
                    throw new InvalidOperationException("This bone is used by a remaining skinned mesh. Select its complete model hierarchy to delete it.");
            var remap = new int[nodes.Count]; int next = 0;
            for (int i = 0; i < nodes.Count; i++) remap[i] = remove.Contains(i) ? -1 : next++;
            Undo.RegisterCompleteObjectUndo(this, "Delete model objects");
            var remaining = new List<Node>();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (remove.Contains(i)) continue;
                var node = nodes[i];
                if (node.parent >= 0) node.parent = remap[node.parent];
                if (node.rootBone >= 0) node.rootBone = remap[node.rootBone];
                for (int b = 0; b < node.bones.Length; b++) if (node.bones[b] >= 0) node.bones[b] = remap[node.bones[b]];
                remaining.Add(node);
            }
            // Keep owned mesh subassets available for Undo/Redo, just as import does.
            nodes = remaining; EditorUtility.SetDirty(this);
        }
    }
}
