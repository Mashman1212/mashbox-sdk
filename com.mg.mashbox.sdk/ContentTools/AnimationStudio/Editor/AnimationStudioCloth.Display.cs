using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Cloth;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [Serializable] private sealed class ClothMaterialAssignment
        {
            public Mesh mesh;
            public Material[] materials;
        }
        [SerializeField] private List<ClothMaterialAssignment> clothMaterialAssignments = new List<ClothMaterialAssignment>();
        [SerializeField] private bool clothTextured, clothWireframe;
        private Material clothNeutralMaterial;
        private Material[][] clothSourceMaterials;
        private Mesh clothWireSource;
        private int[] clothWireEdges;
        private Vector3[] clothWireLines;
        private bool clothMaterialSlotsOpen = true;

        private void InitializeClothDisplay()
        {
            clothSourceMaterials = clothRenderers.Select(r => r.sharedMaterials).ToArray();
            clothNeutralMaterial = new Material(Shader.Find("MashBox/Studio/Preview")) {
                name = "Cloth neutral preview", hideFlags = HideFlags.HideAndDontSave
            };
            clothNeutralMaterial.SetColor("_Color", new Color(.7f, .74f, .78f));
            clothNeutralMaterial.SetFloat("_Cull", 0);
            ApplyClothDisplay();
        }

        private Material[] ClothAssignedMaterials(int index)
        {
            var mesh = clothMeshRows[index].sourceMesh;
            return clothMaterialAssignments.Find(a => a.mesh == mesh)?.materials ?? clothSourceMaterials[index];
        }

        private void ApplyClothDisplay()
        {
            if (!clothNeutralMaterial || clothSourceMaterials == null) return;
            for (int i = 0; i < clothRenderers.Length; i++)
            {
                int count = Mathf.Max(1, clothMeshRows[i].sourceMesh.subMeshCount);
                var assigned = ClothAssignedMaterials(i);
                var materials = Enumerable.Range(0, count).Select(slot => clothTextured && !clothMeshRows[i].referenceOnly &&
                    assigned != null && slot < assigned.Length && assigned[slot] ? assigned[slot] : clothNeutralMaterial).ToArray();
                clothRenderers[i].sharedMaterials = materials;
                if (clothSession != null && clothSession.Source == clothRenderers[i])
                    foreach (var renderer in clothRig.Root.GetComponentsInChildren<MeshRenderer>(true))
                        if (MashBoxClothSession.GetMesh(renderer) == clothSession.Output) renderer.sharedMaterials = materials;
            }
            viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
        }

        private void DrawClothDisplayControls()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool textured = GUILayout.Toggle(clothTextured, new GUIContent("Textured", "Show assigned materials on user meshes"), "Button");
                bool wire = GUILayout.Toggle(clothWireframe, new GUIContent("Wireframe", "Show quad-style edges on the selected garment"), "Button");
                if (textured != clothTextured) { clothTextured = textured; ApplyClothDisplay(); }
                if (wire != clothWireframe) { clothWireframe = wire; viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene); }
            }
            if (ClothEditable)
                clothMaterialSlotsOpen = StudioMeshMaterialPreview.DrawSlots(ClothAssignedMaterials(clothRendererIndex),
                    clothMeshRows[clothRendererIndex].sourceMesh.subMeshCount, clothMaterialSlotsOpen,
                    (slot, material) => ClothAction(() => AssignClothMaterial(clothRendererIndex, slot, material)));
        }

        private void AssignClothMaterial(int index, int slot, Material material)
        {
            if (index < 0 || index >= clothMeshRows.Length || clothMeshRows[index].referenceOnly) return;
            EndClothStroke(); StopClothSession();
            var mesh = clothMeshRows[index].sourceMesh;
            var materials = StudioMeshMaterialPreview.Assign(ClothAssignedMaterials(index), mesh.subMeshCount, slot, material);
            clothMaterialAssignments.RemoveAll(a => a.mesh == mesh);
            clothMaterialAssignments.Add(new ClothMaterialAssignment { mesh = mesh, materials = materials });
            SelectClothMesh(index); clothTextured = true; ApplyClothDisplay();
        }

        private void HandleClothMaterialDrop(SceneView view)
        {
            if (!StudioMeshMaterialPreview.IsMaterialDrag || !viewport.ModelCanvasRect.Contains(Event.current.mousePosition) ||
                viewport.IsOverSkinningPanels(Event.current.mousePosition)) return;
            var ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            float nearest = float.PositiveInfinity; int hit = -1, slot = -1;
            Mesh baked = null;
            try
            {
                for (int i = 0; i < clothRenderers.Length; i++)
                {
                    var renderer = clothRenderers[i];
                    if (!renderer.gameObject.activeSelf) continue;
                    var mesh = MashBoxClothSession.GetMesh(renderer);
                    if (clothSession != null && clothSession.Source == renderer) mesh = clothSession.Output;
                    else if (renderer is SkinnedMeshRenderer skin)
                    {
                        if (!baked) baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                        skin.BakeMesh(baked, true); mesh = baked;
                    }
                    if (!mesh || !mesh.isReadable) continue;
                    if (StudioMeshMaterialPreview.Hit(mesh, renderer.transform.localToWorldMatrix, ray, ref nearest, out int s))
                    { hit = i; slot = s; }
                }
            }
            finally { if (baked) DestroyImmediate(baked); }
            if (hit < 0 || clothMeshRows[hit].referenceOnly)
            { DragAndDrop.visualMode = DragAndDropVisualMode.Rejected; Event.current.Use(); return; }
            StudioMeshMaterialPreview.Drop(m => ClothAction(() => AssignClothMaterial(hit, slot, m)));
        }

        private void DrawClothWireframe()
        {
            if (!clothWireframe || !ClothVisible || Event.current.type != EventType.Repaint) return;
            var mesh = MashBoxClothSession.GetMesh(ClothRenderer);
            if (!mesh || !mesh.isReadable) return;
            if (clothWireSource != mesh)
            {
                clothWireSource = mesh;
                var diagonals = StudioQuadWire.Diagonals(mesh);
                var edges = new HashSet<ulong>(); var indices = new List<int>();
                void Edge(int a, int b)
                {
                    if (a > b) { int t = a; a = b; b = t; }
                    ulong key = ((ulong)(uint)a << 32) | (uint)b;
                    if (a == b || diagonals.Contains(key) || !edges.Add(key)) return;
                    indices.Add(a); indices.Add(b);
                }
                var triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                { Edge(triangles[i], triangles[i+1]); Edge(triangles[i+1], triangles[i+2]); Edge(triangles[i+2], triangles[i]); }
                clothWireEdges = indices.ToArray(); clothWireLines = new Vector3[indices.Count];
            }
            if (clothSession == null) RefreshClothPickMesh();
            var vertices = clothSession != null ? clothSession.Vertices : clothPickVertices;
            for (int i = 0; i < clothWireEdges.Length; i++) clothWireLines[i] = vertices[clothWireEdges[i]];
            var previous = Handles.zTest;
            try
            {
                Handles.zTest = CompareFunction.LessEqual;
                using (new Handles.DrawingScope(new Color(.06f, .12f, .12f, .8f), ClothRenderer.transform.localToWorldMatrix))
                    Handles.DrawLines(clothWireLines);
            }
            finally { Handles.zTest = previous; }
        }

        private void ReleaseClothDisplay()
        {
            if (clothNeutralMaterial) DestroyImmediate(clothNeutralMaterial);
            clothNeutralMaterial = null; clothSourceMaterials = null;
            clothWireSource = null; clothWireEdges = null; clothWireLines = null;
        }
    }
}
