using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        internal bool HasEditableMesh => Part != null && !Part.referenceOnly;
        internal bool CanSkinSelected => HasEditableMesh && Part.role != StudioSkinningDocument.MeshRole.Headgear && !Part.bound;
        internal bool CanUnskinSelected => HasEditableMesh && Part.bound;
        internal void SkinSelected()
        {
            if (!CanSkinSelected) return;
            AutoBind();
        }
        internal void UnskinSelected()
        {
            if (!CanUnskinSelected) return;
            EndStroke(); PausePreviewAnimation();
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(new Object[] { Document, Part.mesh }, "Unskin");
            var skin = new StudioSkinWeights(Part.mesh);
            foreach (var influences in skin.Values) influences.Clear();
            skin.Apply(Part.mesh);
            Part.mesh.bindposes = Array.Empty<Matrix4x4>();
            Part.bound = false;
            Part.stitchedVertices = null;
            Part.stitchedWeights = null;
            Part.stitchedVertexCount = 0;
            Rebuild(); Changed(); RecordSkinTutorialAction("unbind");
        }
        internal void SetSelectedMeshRole(bool bust)
        {
            if (HasEditableMesh) SetMeshRole(Part,bust ? StudioSkinningDocument.MeshRole.Bust : StudioSkinningDocument.MeshRole.Clothing);
        }
        internal bool SelectedIsBust => HasEditableMesh && Part.role == StudioSkinningDocument.MeshRole.Bust;
        internal bool SelectedIsHeadgear => HasEditableMesh && Part.role == StudioSkinningDocument.MeshRole.Headgear;
        internal void MarkSelectedHeadgear() { if (HasEditableMesh) SetMeshRole(Part,StudioSkinningDocument.MeshRole.Headgear); }
        internal bool SelectedIsBody => HasEditableMesh && Part.role == StudioSkinningDocument.MeshRole.Body;
        internal void MarkSelectedBody()
        { if (HasEditableMesh) SetMeshRole(Part,StudioSkinningDocument.MeshRole.Body); }
    }
}
