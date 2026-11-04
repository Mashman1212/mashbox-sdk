using System;
using UnityEditor;
using Undo = MashBoxSDK.AnimationStudio.StudioUndo;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioModelDocument
    {
        internal void RemoveMesh(int index)
        {
            if (index < 0 || index >= nodes.Count) throw new ArgumentOutOfRangeException(nameof(index));
            var node = nodes[index];
            if (!node.mesh) return;
            Undo.RegisterCompleteObjectUndo(this, "Remove object mesh");
            // Detach, never destroy: another instance and Undo may still own the mesh.
            node.mesh = null;
            node.blendShape = null;
            node.cutPolygons = node.cutTriangles = node.normalGroups = node.geometryVertices = null;
            node.subdivision = new StudioSubdivisionSettings();
            node.modifiers = null;
            node.materials = Array.Empty<UnityEngine.Material>();
            node.previewTextureSlice = 0;
            node.vertexAOBaked = false;
            node.skinned = false;
            node.bones = Array.Empty<int>();
            node.rootBone = -1;
            EditorUtility.SetDirty(this);
        }
    }
}
