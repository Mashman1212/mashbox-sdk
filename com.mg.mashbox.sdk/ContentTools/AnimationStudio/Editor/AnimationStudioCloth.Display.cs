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
                var materials = Enumerable.Range(0, count).Select(slot => clothTextured &&
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
            if (ClothEditable)
                clothMaterialSlotsOpen = StudioMeshMaterialPreview.DrawSlots(ClothAssignedMaterials(clothRendererIndex),
                    clothMeshRows[clothRendererIndex].sourceMesh.subMeshCount, clothMaterialSlotsOpen,
                    (slot, material) => ClothAction(() => AssignClothMaterial(clothRendererIndex, slot, material)));
        }

        private void DrawClothToolbar()
        {
            using (new GUILayout.HorizontalScope())
            {
                if (ClothToolbarButton(0, clothTextured, "Textured — show assigned materials"))
                { clothTextured = !clothTextured; ApplyClothDisplay(); }
                if (ClothToolbarButton(1, clothWireframe, "Wireframe — show garment edges"))
                { clothWireframe = !clothWireframe; viewport.MarkDirty(MashBoxStudioViewport.DirtyState.Scene); }
                if (ClothToolbarButton(4, clothPaintXray, "X-ray — show occluded weight colors and wireframe. Does not change which vertices the brush paints."))
                { clothPaintXray = !clothPaintXray; viewport.MarkDirty(MashBoxStudioViewport.DirtyState.Overlay); }
                if (ClothToolbarButton(5, clothShowColliders, "Show collisions � show or hide body collider outlines"))
                { clothShowColliders = !clothShowColliders; viewport.MarkDirty(MashBoxStudioViewport.DirtyState.Overlay); }
                GUILayout.Space(10);
                using (new EditorGUI.DisabledScope(!CanSimulateCloth))
                    if (ClothToolbarButton(2, clothPainting, "Paint movement limits — toggle painting. Ctrl + middle-drag or [ / ] changes brush size."))
                    {
                        EndClothStroke(); clothPainting = !clothPainting;
                        if (clothPainting) clothTool = 0;
                        viewport.MarkDirty(MashBoxStudioViewport.DirtyState.Overlay);
                    }
                using (new EditorGUI.DisabledScope(!CanSimulateCloth || clothSession == null))
                    if (ClothToolbarButton(3, clothTool == 1 && !clothPainting && clothSession != null,
                        "Grab particles — toggle click-and-drag pulling during simulation. Start Play first. Ctrl + middle-drag or [ / ] changes grab radius."))
                    {
                        EndClothStroke(); clothTool = clothTool == 1 ? 0 : 1;
                        if (clothTool == 1) clothPainting = false;
                        viewport.MarkDirty(MashBoxStudioViewport.DirtyState.Overlay);
                    }
                GUILayout.FlexibleSpace();
            }
        }

        private static bool ClothToolbarButton(int icon, bool active, string tooltip)
        {
            var rect = GUILayoutUtility.GetRect(24, 24, GUILayout.Width(24), GUILayout.Height(24));
            GUILayout.Space(4);
            bool clicked = GUI.Button(rect, new GUIContent("", tooltip), GUIStyle.none);
            if (Event.current.type != EventType.Repaint) return clicked;
            EditorGUI.DrawRect(rect, rect.Contains(Event.current.mousePosition) ? new Color(.10f,.12f,.12f) : new Color(.035f,.04f,.04f));
            var color = GUI.enabled ? (active ? new Color(.5f,1,.87f) : new Color(.75f,.81f,.81f)) : new Color(.35f,.38f,.38f);
            if (active) EditorGUI.DrawRect(new Rect(rect.x+4,rect.yMax-2,16,2), color);
            using (new Handles.DrawingScope(color, Matrix4x4.identity))
            {
                Vector3 P(float x, float y) => new Vector3(rect.center.x+x,rect.center.y+y,0);
                if (icon == 0) StudioMeshMaterialPreview.DrawTextureIcon(rect, color);
                else if (icon == 1)
                {
                    Handles.DrawAAPolyLine(1.2f,P(-8,-8),P(8,-8),P(8,8),P(-8,8),P(-8,-8));
                    Handles.DrawAAPolyLine(1.2f,P(0,-8),P(0,8));
                    Handles.DrawAAPolyLine(1.2f,P(-8,0),P(8,0));
                }
                else if (icon == 2)
                {
                    Handles.DrawAAPolyLine(2,P(-1,1),P(7,-8),P(9,-6),P(1,3));
                    Handles.DrawAAPolyLine(1.5f,P(-1,1),P(-5,2),P(-7,7),P(-2,6),P(1,3));
                }
                else if (icon == 5)
                {
                    // Capsule outline distinguishes collision visibility from X-ray.
                    Handles.DrawAAPolyLine(1.4f, P(-5,-4), P(-5,4));
                    Handles.DrawAAPolyLine(1.4f, P(5,-4), P(5,4));
                    Handles.DrawWireArc(P(0,-4), Vector3.forward, Vector3.left, 180, 5);
                    Handles.DrawWireArc(P(0,4), Vector3.forward, Vector3.right, 180, 5);
                    Handles.DrawAAPolyLine(1, P(0,-9), P(0,9));
                }
                else if (icon == 4)
                {
                    Handles.DrawAAPolyLine(1.4f,P(-10,0),P(-5,-5),P(0,-6),P(5,-5),P(10,0),P(5,5),P(0,6),P(-5,5),P(-10,0));
                    Handles.DrawWireDisc(P(0,0),Vector3.forward,3);
                }
                else
                {
                    // Open hand distinguishes direct grabbing from painting.
                    Handles.DrawAAPolyLine(1.4f,P(-4,8),P(-8,1),P(-8,-1),P(-6,-2),P(-3,1),
                        P(-3,-7),P(-1,-8),P(0,-7),P(0,-2),P(0,-9),P(2,-9),P(3,-8),P(3,-2),
                        P(3,-7),P(5,-7),P(6,-6),P(6,-1),P(6,-4),P(8,-4),P(9,-3),P(9,3),P(6,8),P(-4,8));
                }
            }
            return clicked;
        }

        private void AssignClothMaterial(int index, int slot, Material material)
        {
            if (index < 0 || index >= clothMeshRows.Length) return;
            var mesh = clothMeshRows[index].sourceMesh;
            var materials = StudioMeshMaterialPreview.Assign(ClothAssignedMaterials(index), mesh.subMeshCount, slot, material);
            clothMaterialAssignments.RemoveAll(a => a.mesh == mesh);
            clothMaterialAssignments.Add(new ClothMaterialAssignment { mesh = mesh, materials = materials });
            clothTextured = true; ApplyClothDisplay();
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
                Handles.zTest = clothPaintXray ? CompareFunction.Always : CompareFunction.LessEqual;
                using (new Handles.DrawingScope(new Color(.06f, .12f, .12f, .8f), ClothRenderer.transform.localToWorldMatrix))
                    Handles.DrawLines(clothWireLines);
            }
            finally { Handles.zTest = previous; }
        }

        private void ReleaseClothDisplay()
        {
            DisposeClothWeightSurface();
            if (clothNeutralMaterial) DestroyImmediate(clothNeutralMaterial);
            clothNeutralMaterial = null; clothSourceMaterials = null;
            clothWireSource = null; clothWireEdges = null; clothWireLines = null;
        }
    }
}
