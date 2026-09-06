#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using DetailQualityPreset = MashBoxSDK.Maps.TerrainSystem.MGTerrain.DetailQualityPreset;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace MashBoxSDK.MapTools
{
    [CustomEditor(typeof(MGTerrain))]
    public sealed class MGTerrainEditor : Editor
    {
        int m_FloodDensity = ushort.MaxValue;
        readonly List<Material> m_DetailSourceMaterials = new List<Material>();
        bool m_DetailPainting, m_AdjustDetailBrush, m_PreviousEditing;
        bool m_HasDetailAdjustSurface;
        Vector2 m_DetailAdjustMouse;
        Vector3 m_DetailAdjustPoint, m_DetailAdjustNormal;
        int m_PaintDetailIndex, m_PaintChannel, m_PaintDensity = 32;
        float m_PaintSize = 1f;
        Texture2D m_StrokeMap;
        Rect m_PendingPaintRegion;
        bool m_HasPendingPaint;
        double m_NextPaintPreview;
        int m_StrokeUndo = -1;
        Vector3 m_LastDetailDab;
        readonly HashSet<Texture2D> m_PaintCopies = new HashSet<Texture2D>();
        Tool m_PreviousTool;
        float m_LoftEdgeWidth = 1f;
        float m_LoftEdgeVariation = 0.5f;
        float m_LoftNoiseScale = 2f;
        int m_LoftEdgeSeed = 12345;
        static readonly int[] DetailInstanceCapSteps =
        {
            4096,
            8192,
            16384,
            32768,
            65536,
            131072,
            262144,
            524288
        };

        static readonly string[] DetailInstanceCapLabels =
        {
            "4,096",
            "8,192",
            "16,384",
            "32,768",
            "65,536",
            "131,072",
            "262,144",
            "524,288"
        };

        static readonly int[] VisibleInstanceBudgetSteps =
        {
            25000,
            50000,
            75000,
            100000,
            150000,
            200000,
            300000,
            400000,
            500000,
            750000,
            1000000,
            0
        };

        static readonly string[] VisibleInstanceBudgetLabels =
        {
            "25,000",
            "50,000",
            "75,000",
            "100,000",
            "150,000",
            "200,000",
            "300,000",
            "400,000",
            "500,000",
            "750,000",
            "1,000,000",
            "Unlimited"
        };

        SerializedProperty m_HeightOnlySculpt;
        SerializedProperty m_DrawInstances;
        SerializedProperty m_DrawInstancesInEditMode;
        SerializedProperty m_DefaultTreeDistance;
        SerializedProperty m_DefaultDetailDistance;
        SerializedProperty m_ControlMap1;
        SerializedProperty m_ControlMap2;
        SerializedProperty m_Prototypes;
        SerializedProperty m_DensityDetailLayers;
        SerializedProperty m_DetailFoliagePalettes;
        SerializedProperty m_DetailChunkCells;
        SerializedProperty m_OverallDetailDensity;
        SerializedProperty m_MaxCachedDetailChunks;
        SerializedProperty m_MaxDetailChunksBuiltPerLayerPerFrame;
        SerializedProperty m_UseDetailDensityLod;
        SerializedProperty m_FullDetailDensityDistance;
        SerializedProperty m_MidDetailDensityDistance;
        SerializedProperty m_MidDetailDensity;
        SerializedProperty m_FarDetailDensity;
        SerializedProperty m_DetailDensityLodHysteresis;
        SerializedProperty m_DebugDrawDensityDetailCells;
        SerializedProperty m_MaxDensityDetailDistance;
        SerializedProperty m_MaxVisibleDenseDetailInstances;
        SerializedProperty m_DistantDetailBudgetReserve;
        SerializedProperty m_UseBatchRendererGroup;
        SerializedProperty m_UseGpuProceduralDetailGeneration;
        SerializedProperty m_PrewarmFixedDetailCells;
        SerializedProperty m_RetainFixedDetailCells;
        SerializedProperty m_DetailStreamingRefreshDistance;
        SerializedProperty m_DetailStreamingRefreshAngle;
        SerializedProperty m_CombineDenseDetailMeshes;
        SerializedProperty m_DenseDetailShadows;
        SerializedProperty m_MaxCombinedDetailVerticesPerChunk;
        SerializedProperty m_MaxDetailVerticesPerUpload;
        SerializedProperty m_MaxDetailMeshUploadsPerFrame;
        SerializedProperty m_MaxPendingDetailBuilds;
        bool m_ShowMemoryUsage;
        bool m_HasMemoryUsageSnapshot;
        MGTerrain.MemoryUsageSnapshot m_MemoryUsageSnapshot;

        void OnEnable()
        {
            Undo.undoRedoPerformed += RefreshPaintUndo;
            EditorApplication.update += UpdateDetailPaintPreview;
            m_HeightOnlySculpt = serializedObject.FindProperty("m_HeightOnlySculpt");
            m_DrawInstances = serializedObject.FindProperty("m_DrawInstances");
            m_DrawInstancesInEditMode = serializedObject.FindProperty("m_DrawInstancesInEditMode");
            m_DefaultTreeDistance = serializedObject.FindProperty("m_DefaultTreeDistance");
            m_DefaultDetailDistance = serializedObject.FindProperty("m_DefaultDetailDistance");
            m_ControlMap1 = serializedObject.FindProperty("m_ControlMap1");
            m_ControlMap2 = serializedObject.FindProperty("m_ControlMap2");
            m_Prototypes = serializedObject.FindProperty("m_Prototypes");
            m_DensityDetailLayers = serializedObject.FindProperty("m_DensityDetailLayers");
            m_DetailFoliagePalettes = serializedObject.FindProperty("m_DetailFoliagePalettes");
            m_DetailChunkCells = serializedObject.FindProperty("m_DetailChunkCells");
            m_OverallDetailDensity = serializedObject.FindProperty("m_OverallDetailDensity");
            m_MaxCachedDetailChunks = serializedObject.FindProperty("m_MaxCachedDetailChunks");
            m_MaxDetailChunksBuiltPerLayerPerFrame = serializedObject.FindProperty("m_MaxDetailChunksBuiltPerLayerPerFrame");
            m_UseDetailDensityLod = serializedObject.FindProperty("m_UseDetailDensityLod");
            m_FullDetailDensityDistance = serializedObject.FindProperty("m_FullDetailDensityDistance");
            m_MidDetailDensityDistance = serializedObject.FindProperty("m_MidDetailDensityDistance");
            m_MidDetailDensity = serializedObject.FindProperty("m_MidDetailDensity");
            m_FarDetailDensity = serializedObject.FindProperty("m_FarDetailDensity");
            m_DetailDensityLodHysteresis = serializedObject.FindProperty("m_DetailDensityLodHysteresis");
            m_DebugDrawDensityDetailCells = serializedObject.FindProperty("m_DebugDrawDensityDetailCells");
            m_MaxDensityDetailDistance = serializedObject.FindProperty("m_MaxDensityDetailDistance");
            m_MaxVisibleDenseDetailInstances = serializedObject.FindProperty("m_MaxVisibleDenseDetailInstances");
            m_DistantDetailBudgetReserve = serializedObject.FindProperty("m_DistantDetailBudgetReserve");
            m_UseBatchRendererGroup = serializedObject.FindProperty("m_UseBatchRendererGroup");
            m_UseGpuProceduralDetailGeneration = serializedObject.FindProperty("m_UseGpuProceduralDetailGeneration");
            m_PrewarmFixedDetailCells = serializedObject.FindProperty("m_PrewarmFixedDetailCells");
            m_RetainFixedDetailCells = serializedObject.FindProperty("m_RetainFixedDetailCells");
            m_DetailStreamingRefreshDistance = serializedObject.FindProperty("m_DetailStreamingRefreshDistance");
            m_DetailStreamingRefreshAngle = serializedObject.FindProperty("m_DetailStreamingRefreshAngle");
            m_CombineDenseDetailMeshes = serializedObject.FindProperty("m_CombineDenseDetailMeshes");
            m_DenseDetailShadows = serializedObject.FindProperty("m_DenseDetailShadows");
            m_MaxCombinedDetailVerticesPerChunk = serializedObject.FindProperty("m_MaxCombinedDetailVerticesPerChunk");
            m_MaxDetailVerticesPerUpload = serializedObject.FindProperty("m_MaxDetailVerticesPerUpload");
            m_MaxDetailMeshUploadsPerFrame = serializedObject.FindProperty("m_MaxDetailMeshUploadsPerFrame");
            m_MaxPendingDetailBuilds = serializedObject.FindProperty("m_MaxPendingDetailBuilds");
        }

        public override void OnInspectorGUI()
        {
            MGTerrain terrain = (MGTerrain)target;
            serializedObject.Update();

            EditorGUILayout.LabelField("MG Terrain", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The surface is a normal mesh, so its MeshRenderer can use any material. MG Brush paints the mesh/control maps and stores Decor strokes as GPU instances.",
                MessageType.Info);

            DrawMappyToolLauncher(terrain);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Surface Mesh", terrain.MeshFilter, typeof(MeshFilter), true);
                EditorGUILayout.ObjectField("Surface Collider", terrain.MeshCollider, typeof(MeshCollider), true);
            }

            DrawSurfaceColliders(terrain);
            EditorGUILayout.PropertyField(m_HeightOnlySculpt);
            EditorGUILayout.PropertyField(m_DrawInstances);
            if (m_DrawInstances.boolValue)
                EditorGUILayout.PropertyField(m_DrawInstancesInEditMode);
            EditorGUILayout.PropertyField(m_DefaultTreeDistance);
            EditorGUILayout.PropertyField(m_DefaultDetailDistance);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Paint Control Maps", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_ControlMap1);
            EditorGUILayout.PropertyField(m_ControlMap2);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Instanced Foliage", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Prototype Count", terrain.Prototypes.Count.ToString("N0"));
            EditorGUILayout.LabelField("Instance Count", terrain.InstanceCount.ToString("N0"));
            EditorGUILayout.PropertyField(m_Prototypes, true);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Density Details", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Dense grass is stored as compact R16 density maps. Visible chunks are generated deterministically and GPU-instanced near each camera; the full grass population is never serialized into the scene.",
                MessageType.Info);
            EditorGUILayout.LabelField("Density Layers", terrain.DensityDetailLayerCount.ToString("N0"));
            EditorGUILayout.LabelField("Represented Details", terrain.RepresentedDensityDetailCount.ToString("N0"));
            DrawDetailFoliagePalettes(terrain);
            DrawDetailQualityPresets(terrain);
            DrawMemoryUsage(terrain);
            EditorGUILayout.Slider(m_OverallDetailDensity, 0f, 1f,
                new GUIContent("Overall Detail Density", "Main density multiplier: 0 hides density details, 1 uses full available density. Distance LOD multiplies this value. Cell capacity and the visible instance budget remain upper limits; the combined-mesh vertex limit does not affect density."));
            EditorGUILayout.PropertyField(
                m_MaxDensityDetailDistance,
                new GUIContent("Max Detail Distance", "Global maximum rendering distance for density-map grass/details. A prototype can use a shorter distance; 0 removes this global ceiling."));
            EditorGUILayout.PropertyField(
                m_DetailChunkCells,
                new GUIContent("Near Cell Size (Texels)", "Streaming cell width in density-map texels. The density ceiling is fixed per world area: 32,768 instances per 50 × 50 metres per layer, independent of this cell size."));
            EditorGUILayout.PropertyField(m_MaxCachedDetailChunks);
            EditorGUILayout.PropertyField(m_MaxDetailChunksBuiltPerLayerPerFrame);
            EditorGUILayout.PropertyField(
                m_UseDetailDensityLod,
                new GUIContent("Distance Density LOD", "Keep nearby cells dense while deterministically thinning farther cells."));
            if (m_UseDetailDensityLod.boolValue)
            {
                EditorGUI.indentLevel++;
                int nearCellSize = Mathf.Clamp(m_DetailChunkCells.intValue, 8, 64);
                EditorGUILayout.LabelField(
                    "HLOD Cell Sizes",
                    m_UseBatchRendererGroup.boolValue
                        ? $"Fixed {nearCellSize} (reused by Near / Mid / Far)"
                        : $"Near {nearCellSize} / Mid {nearCellSize * 2} / Far {nearCellSize * 4}");
                EditorGUILayout.PropertyField(
                    m_FullDetailDensityDistance,
                    new GUIContent("Full Density Distance", "Cells inside this distance render at 100% generated density."));
                EditorGUILayout.PropertyField(
                    m_MidDetailDensityDistance,
                    new GUIContent("Mid Density End", "Cells after Full Density Distance and up to here use Mid Density."));
                EditorGUILayout.Slider(
                    m_MidDetailDensity,
                    0.01f,
                    1f,
                    new GUIContent("Mid Density", "Fraction of authored details generated in the middle distance band."));
                EditorGUILayout.Slider(
                    m_FarDetailDensity,
                    0.01f,
                    1f,
                    new GUIContent("Far Density", "Fraction generated after Mid Density End and before the prototype draw distance."));
                EditorGUILayout.PropertyField(
                    m_DetailDensityLodHysteresis,
                    new GUIContent("LOD Hysteresis", "Distance margin around density boundaries. Cells do not change density again until they move completely across this margin."));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.PropertyField(
                m_DebugDrawDensityDetailCells,
                new GUIContent(
                    "Debug Draw HLOD Cells",
                    "Draw the currently rendered density-detail cells and the camera-centered LOD/hysteresis boundaries while this terrain is selected."));
            if (m_DebugDrawDensityDetailCells.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "Cell colors: green = Near/full density, yellow = Mid, red = Far. Strong spheres are the LOD boundaries; faint paired spheres show the hysteresis enter/exit limits. Blue is Max Detail Distance. Hysteresis prevents boundary chatter—it does not fade density.",
                    MessageType.None);
            }
            DrawVisibleInstanceBudgetSlider(m_MaxVisibleDenseDetailInstances);
            EditorGUILayout.Slider(
                m_DistantDetailBudgetReserve,
                0f,
                0.75f,
                new GUIContent(
                    "Distance Budget Reserve",
                    "Fraction of the visible budget guaranteed to middle/far HLOD cells when nearby grass alone exceeds the cap. Unused reserve is automatically returned to the near field."));
            EditorGUILayout.PropertyField(
                m_UseBatchRendererGroup,
                new GUIContent(
                    "GPU Resident Renderer",
                    "Use BatchRendererGroup/DOTS instancing for dense details in Play Mode. This removes the 1,023-instance draw limit and keeps instance matrices in a persistent GPU buffer. The packed renderer remains the automatic fallback."));
            if (m_UseBatchRendererGroup.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(
                    m_UseGpuProceduralDetailGeneration,
                    new GUIContent(
                        "GPU Procedural Generation",
                        "Keep compact density spans on the CPU and generate only submitted transforms with a compute shader. This avoids per-blade CPU matrices and all cell mesh combining."));
                EditorGUILayout.PropertyField(
                    m_PrewarmFixedDetailCells,
                    new GUIContent("Prewarm Cells On First Render", "Build all fixed cells inside the initial detail radius in one startup pass. Camera movement then enables/disables cached cells instead of regenerating LOD variants."));
                EditorGUILayout.PropertyField(
                    m_RetainFixedDetailCells,
                    new GUIContent("Retain Built Cells", "Never evict a fixed cell after it has been generated during this play session. Revisiting terrain is hitch-free but memory grows with explored area."));
                EditorGUILayout.PropertyField(
                    m_DetailStreamingRefreshDistance,
                    new GUIContent("Refresh After Moving", "Reuse the current GPU-resident cell set until the main camera moves this far."));
                EditorGUILayout.PropertyField(
                    m_DetailStreamingRefreshAngle,
                    new GUIContent("Refresh After Rotating", "Reuse the current GPU-resident cell set until the main camera rotates this many degrees."));
                EditorGUI.indentLevel--;
                EditorGUILayout.HelpBox(
                    m_UseGpuProceduralDetailGeneration.boolValue
                        ? "GPU Procedural stores occupied density spans rather than blade matrices. Compute expands only the submitted budget directly into the BRG buffer; cell meshes are never combined. Only the MainCamera refreshes the resident set."
                        : "GPU Resident uses fixed CPU-matrix cells: a cell is generated once at full density, while Near/Mid/Far only alter the submitted instance prefix.",
                    MessageType.None);
            }
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField(
                    new GUIContent(
                        "Last Camera Detail Load",
                        "Logical density-detail instances available in visible HLOD cells versus the number actually submitted after the visible budget."),
                    new GUIContent($"{terrain.LastVisibleDensityDetailInstances:N0} visible / {terrain.LastSubmittedDensityDetailInstances:N0} submitted"));
                EditorGUILayout.LabelField(
                    new GUIContent(
                        "Last Camera Draw Submissions",
                        "MG Terrain draw submissions for the last rendered camera. HDRP normally executes each alpha-clipped submission once in Deferred Depth Prepass and once in GBuffer, so the Frame Debugger shows approximately this count in each pass."),
                    new GUIContent(terrain.LastDensityDetailDrawCalls.ToString("N0")));
                EditorGUILayout.LabelField(
                    "Active Renderer",
                    terrain.IsGpuProceduralDensityDetailActive
                        ? "GPU Procedural + Resident (BRG)"
                        : terrain.IsDensityDetailBrgActive
                            ? "GPU Resident (BRG)"
                            : "Packed / Combined Fallback");
                if (terrain.IsGpuProceduralDensityDetailActive)
                    EditorGUILayout.LabelField(
                        new GUIContent("Transforms Regenerated Last Update", "Number of GPU instance transforms rewritten on the last detail update. Unchanged cell ranges retain their existing transforms; zero means all ranges were reused. Includes prefab mesh parts."),
                        new GUIContent(terrain.LastRegeneratedDetailInstances.ToString("N0")));
                EditorGUILayout.LabelField(
                    new GUIContent(
                        "Prototype Part Batching",
                        "Source prefab renderer/submesh parts compared with the same-material parts MG Terrain consolidated for dense instancing."),
                    new GUIContent($"{terrain.LastDensityDetailSourceParts:N0} source / {terrain.LastDensityDetailBatchedParts:N0} batched"));
            }
            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField("Packed / Combined Fallback", EditorStyles.boldLabel);
            if (m_UseBatchRendererGroup.boolValue && Application.isPlaying && terrain.IsDensityDetailBrgActive)
            {
                EditorGUILayout.HelpBox(
                    "The combined-mesh limits below are inactive while GPU Resident (BRG) is active. They only configure the automatic fallback renderer and are not clipping distant BRG cells.",
                    MessageType.None);
            }
            EditorGUILayout.PropertyField(m_CombineDenseDetailMeshes);
            EditorGUILayout.PropertyField(
                m_DenseDetailShadows,
                new GUIContent("Dense Detail Shadows", "Master switch for density-map shadows. Each prototype's Shadow Casting setting is still respected."));
            if (m_CombineDenseDetailMeshes.boolValue)
            {
                EditorGUILayout.PropertyField(m_MaxCombinedDetailVerticesPerChunk,
                    new GUIContent("Fallback Combine Vertex Limit", "Only limits fallback mesh combining. Larger populations use instanced drawing instead. This setting does not reduce detail density."));
                EditorGUILayout.PropertyField(
                    m_MaxDetailVerticesPerUpload,
                    new GUIContent("Vertices Per Upload", "Hard limit for one packed GPU vertex-buffer upload. Lower values reduce camera-movement spikes but create more draw calls."));
                EditorGUILayout.PropertyField(
                    m_MaxDetailMeshUploadsPerFrame,
                    new GUIContent("Mesh Uploads Per Frame", "Maximum completed worker-built detail chunks uploaded to the GPU per rendered frame."));
                EditorGUILayout.PropertyField(
                    m_MaxPendingDetailBuilds,
                    new GUIContent("Pending Worker Builds", "Maximum detail chunks being assembled in background worker tasks."));
            }
            DrawFarGrassBake(terrain);
            DrawDetailPainter(terrain);
            DrawDensityLayersWithFlood(terrain);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Clear Details Under Lofts", EditorStyles.boldLabel);
            m_LoftEdgeWidth = EditorGUILayout.Slider(new GUIContent("Edge Feather (Metres)", "Positive feathers outside the loft; negative feathers inside, preserving grass near its edge. Reapply from the original map to restore previously removed grass."), m_LoftEdgeWidth, -10f, 10f);
            m_LoftEdgeVariation = EditorGUILayout.Slider("Edge Variation", m_LoftEdgeVariation, 0f, 1f);
            m_LoftNoiseScale = Mathf.Max(0.01f, EditorGUILayout.FloatField("Edge Patch Size (Metres)", m_LoftNoiseScale));
            m_LoftEdgeSeed = EditorGUILayout.IntField("Edge Seed", m_LoftEdgeSeed);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
                if (GUILayout.Button("Remove Density Details Under Lofts..."))
                {
                    ClearDetailsUnderLofts(terrain);
                    GUIUtility.ExitGUI();
                }

            bool changed = serializedObject.ApplyModifiedProperties();
            if (changed)
            {
                terrain.ApplyControlMapsToMaterial();
                terrain.InvalidateRenderCache();
                EditorUtility.SetDirty(terrain);
                SceneView.RepaintAll();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Conform Instances"))
                {
                    Undo.RecordObject(terrain, "Conform MG Terrain Instances");
                    terrain.ConformInstancesToSurface();
                    EditorUtility.SetDirty(terrain);
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button("Rebuild Render Cache"))
                {
                    terrain.InvalidateRenderCache();
                    SceneView.RepaintAll();
                }
            }

            using (new EditorGUI.DisabledScope(terrain.InstanceCount == 0))
            {
                if (GUILayout.Button("Clear All Instances...")
                    && EditorUtility.DisplayDialog(
                        "Clear MG Terrain Instances?",
                        $"Remove all {terrain.InstanceCount:N0} detail and tree instances from '{terrain.name}'? This can be undone.",
                        "Clear Instances",
                        "Cancel"))
                {
                    Undo.RecordObject(terrain, "Clear MG Terrain Instances");
                    terrain.ClearInstances();
                    EditorUtility.SetDirty(terrain);
                    SceneView.RepaintAll();
                }
            }
        }

        void ClearDetailsUnderLofts(MGTerrain terrain)
        {
            serializedObject.ApplyModifiedProperties();
            if (terrain.MeshFilter == null || terrain.MeshFilter.sharedMesh == null) return;
            var triangles = new List<Vector2>();
            Vector3 scaleX = terrain.transform.TransformVector(Vector3.right);
            Vector3 scaleZ = terrain.transform.TransformVector(Vector3.forward);
            float sx = scaleX.magnitude, sz = scaleZ.magnitude;
            if (sx < 0.00001f || sz < 0.00001f) return;
            var lofts = UnityEngine.Object.FindObjectsByType<MashBoxSDK.Maps.Spline.MultiSplineLoft>(FindObjectsSortMode.None);
            foreach (var loft in lofts)
            {
                if (!loft.isActiveAndEnabled || loft.gameObject.scene != terrain.gameObject.scene) continue;
                MeshFilter filter = loft.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;
                if (!mesh.isReadable)
                {
                    EditorUtility.DisplayDialog("Cannot Read Loft", $"Enable mesh Read/Write for '{loft.name}' and try again. No maps were changed.", "OK");
                    return;
                }
                Matrix4x4 matrix = terrain.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Vector3[] vertices = mesh.vertices;
                foreach (int vertex in mesh.triangles)
                {
                    Vector3 p = matrix.MultiplyPoint3x4(vertices[vertex]);
                    triangles.Add(new Vector2(p.x * sx, p.z * sz));
                }
            }
            if (triangles.Count == 0)
            {
                EditorUtility.DisplayDialog("No Lofts Found", "No active Multi Spline Loft meshes were found in this terrain's scene.", "OK");
                return;
            }
            var maps = new HashSet<Texture2D>();
            foreach (var layer in terrain.DensityDetailLayers)
                if (layer != null)
                {
                    if (layer.DensityMap != null) maps.Add(layer.DensityMap);
                    if (layer.PaletteSourceMap != null) maps.Add(layer.PaletteSourceMap);
                }
            foreach (var binding in terrain.DetailFoliagePalettes)
                if (binding != null && binding.SourceDensityMap != null) maps.Add(binding.SourceDensityMap);
            if (maps.Count == 0) return;
            foreach (Texture2D map in maps)
                if (!map.isReadable || map.format != TextureFormat.R16)
                {
                    EditorUtility.DisplayDialog("Cannot Read Density Map", $"'{map.name}' must be a readable R16 texture. No maps were changed.", "OK");
                    return;
                }
            if (!EditorUtility.DisplayDialog("Clear Density Under Lofts?",
                $"Clear the projected footprints of active Multi Spline Lofts in this scene, including raised lofts, from this terrain's {maps.Count} density/source maps?\n\nThe edge feathers {(m_LoftEdgeWidth < 0f ? "inward" : "outward")} over up to {Mathf.Abs(m_LoftEdgeWidth)} metres with seeded variation. Individual Decor instances and trees are unchanged. New maps preserve the original painting; Undo restores assignments but keeps the created assets. This only removes density; use the original map to restore grass.", "Clear Under Lofts", "Cancel")) return;
            var results = new Dictionary<Texture2D, ushort[]>();
            var replacements = new Dictionary<Texture2D, Texture2D>();
            var counts = new Dictionary<Texture2D, long>();
            Bounds bounds = terrain.MeshFilter.sharedMesh.bounds;
            float minX = bounds.min.x * sx, minZ = bounds.min.z * sz;
            float sizeX = bounds.size.x * sx, sizeZ = bounds.size.z * sz;
            if (sizeX <= 0f || sizeZ <= 0f) return;
            try
            {
                foreach (Texture2D map in maps)
                {
                    float dx = sizeX / map.width, dz = sizeZ / map.height;
                    var keep = new float[map.width * map.height];
                    for (int p = 0; p < keep.Length; p++) keep[p] = 1f;
                    for (int t = 0; t < triangles.Count; t += 3)
                    {
                        if (t % 768 == 0 && EditorUtility.DisplayCancelableProgressBar("Clear Details Under Lofts", map.name, t / (float)triangles.Count)) return;
                        Vector2 a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                        if (Mathf.Abs(LoftCross(b - a, c - a)) < 0.000001f) continue;
                        float feather = Mathf.Max(0f, m_LoftEdgeWidth);
                        int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x, c.x) - feather - minX) / dx));
                        int x1 = Mathf.Min(map.width - 1, Mathf.FloorToInt((Mathf.Max(a.x, b.x, c.x) + feather - minX) / dx));
                        int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y, c.y) - feather - minZ) / dz));
                        int z1 = Mathf.Min(map.height - 1, Mathf.FloorToInt((Mathf.Max(a.y, b.y, c.y) + feather - minZ) / dz));
                        for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            int pixel = z * map.width + x;
                            if (keep[pixel] == 0f) continue;
                            var p = new Vector2(minX + (x + 0.5f) * dx, minZ + (z + 0.5f) * dz);
                            float u = LoftCross(b - a, p - a), v = LoftCross(c - b, p - b), w = LoftCross(a - c, p - c);
                            if ((u >= 0 && v >= 0 && w >= 0) || (u <= 0 && v <= 0 && w <= 0)) { keep[pixel] = 0f; continue; }
                            if (feather <= 0f) continue;
                            float distance = Mathf.Min(LoftEdgeDistance(p, a, b), LoftEdgeDistance(p, b, c), LoftEdgeDistance(p, c, a));
                            float noise = Mathf.PerlinNoise(p.x / m_LoftNoiseScale + (m_LoftEdgeSeed & 65535), p.y / m_LoftNoiseScale + ((m_LoftEdgeSeed >> 16) & 65535));
                            float reach = feather * Mathf.Lerp(1f, Mathf.Lerp(0.2f, 1f, noise), m_LoftEdgeVariation);
                            keep[pixel] = Mathf.Min(keep[pixel], Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distance / reach)));
                        }
                    }
                    if (m_LoftEdgeWidth < 0f)
                        FeatherInsideLoft(keep, map.width, map.height, dx, dz, minX, minZ);
                    ushort[] values = map.GetPixelData<ushort>(0).ToArray();
                    bool changed = false;
                    for (int p = 0; p < values.Length; p++)
                    {
                        ushort value = (ushort)Mathf.RoundToInt(values[p] * keep[p]);
                        changed |= value != values[p];
                        values[p] = value;
                    }
                    if (changed) results.Add(map, values);
                }
                if (results.Count == 0) { EditorUtility.DisplayDialog("Clear Details Under Lofts", "No occupied density texels overlap the loft footprints or feathered edges.", "OK"); return; }
                const string folder = "Assets/MGTerrainLoftMasks";
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "MGTerrainLoftMasks");
                foreach (var result in results)
                {
                    Texture2D source = result.Key;
                    var output = new Texture2D(source.width, source.height, TextureFormat.R16, false, true)
                    { name = source.name + "_LoftsCleared", filterMode = source.filterMode, wrapMode = source.wrapMode };
                    output.SetPixelData(result.Value, 0);
                    output.Apply(false, false);
                    AssetDatabase.CreateAsset(output, AssetDatabase.GenerateUniqueAssetPath(folder + "/LoftsCleared.asset"));
                    replacements.Add(source, output);
                    long total = 0;
                    foreach (ushort value in result.Value) total += value;
                    counts.Add(output, total);
                }
                Undo.RecordObject(terrain, "Clear MG Terrain Details Under Lofts");
                serializedObject.Update();
                SerializedProperty property = serializedObject.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue is Texture2D original && replacements.TryGetValue(original, out Texture2D replacement))
                        property.objectReferenceValue = replacement;
                for (int index = 0; index < m_DensityDetailLayers.arraySize; index++)
                {
                    SerializedProperty layer = m_DensityDetailLayers.GetArrayElementAtIndex(index);
                    var map = layer.FindPropertyRelative("m_DensityMap").objectReferenceValue as Texture2D;
                    if (map != null && counts.TryGetValue(map, out long total)) layer.FindPropertyRelative("m_RepresentedInstanceCount").longValue = total;
                }
                serializedObject.ApplyModifiedProperties();
                terrain.InvalidateRenderCache();
                EditorUtility.SetDirty(terrain);
                EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
                SceneView.RepaintAll();
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        void FeatherInsideLoft(float[] keep, int width, int height, float dx, float dz, float minX, float minZ)
        {
            // Distance to the outside of the entire projected footprint, not to
            // individual triangle edges: internal mesh seams must stay cleared.
            var distance = new float[keep.Length];
            for (int i = 0; i < distance.Length; i++) distance[i] = keep[i] == 0f ? float.PositiveInfinity : 0f;
            float diagonal = Mathf.Sqrt(dx * dx + dz * dz);
            for (int z = 0; z < height; z++)
            for (int x = 0; x < width; x++)
            {
                int i = z * width + x;
                if (x > 0) distance[i] = Mathf.Min(distance[i], distance[i - 1] + dx);
                if (z > 0)
                {
                    distance[i] = Mathf.Min(distance[i], distance[i - width] + dz);
                    if (x > 0) distance[i] = Mathf.Min(distance[i], distance[i - width - 1] + diagonal);
                    if (x + 1 < width) distance[i] = Mathf.Min(distance[i], distance[i - width + 1] + diagonal);
                }
            }
            for (int z = height - 1; z >= 0; z--)
            for (int x = width - 1; x >= 0; x--)
            {
                int i = z * width + x;
                if (x + 1 < width) distance[i] = Mathf.Min(distance[i], distance[i + 1] + dx);
                if (z + 1 < height)
                {
                    distance[i] = Mathf.Min(distance[i], distance[i + width] + dz);
                    if (x > 0) distance[i] = Mathf.Min(distance[i], distance[i + width - 1] + diagonal);
                    if (x + 1 < width) distance[i] = Mathf.Min(distance[i], distance[i + width + 1] + diagonal);
                }
            }
            for (int z = 0; z < height; z++)
            for (int x = 0; x < width; x++)
            {
                int i = z * width + x;
                if (keep[i] != 0f) continue;
                float noise = Mathf.PerlinNoise((minX + (x + 0.5f) * dx) / m_LoftNoiseScale + (m_LoftEdgeSeed & 65535),
                    (minZ + (z + 0.5f) * dz) / m_LoftNoiseScale + ((m_LoftEdgeSeed >> 16) & 65535));
                float reach = -m_LoftEdgeWidth * Mathf.Lerp(1f, Mathf.Lerp(0.2f, 1f, noise), m_LoftEdgeVariation);
                float insideDistance = Mathf.Max(0f, distance[i] - 0.5f * Mathf.Min(dx, dz));
                keep[i] = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(insideDistance / reach));
            }
        }

        static float LoftCross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static float LoftEdgeDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 edge = b - a;
            float t = edge.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, edge) / edge.sqrMagnitude) : 0f;
            return Vector2.Distance(p, a + t * edge);
        }

        float m_ColliderCellSize = 50f;

        void DrawSurfaceColliders(MGTerrain terrain)
        {
            EditorGUILayout.LabelField("Surface Collision", EditorStyles.boldLabel);
            m_ColliderCellSize = EditorGUILayout.Slider("Collider Cell Size (Metres)", m_ColliderCellSize, 10f, 200f);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Build / Rebuild Child Colliders"))
                {
                    serializedObject.ApplyModifiedProperties();
                    BuildSurfaceColliders(terrain);
                    GUIUtility.ExitGUI();
                }
                if (terrain.SurfaceColliderChunks.Count > 0 && GUILayout.Button("Use Master Collider"))
                {
                    serializedObject.ApplyModifiedProperties();
                    var source = terrain.MeshFilter;
                    if (source == null || source.sharedMesh == null) return;
                    MeshCollider master = terrain.MeshCollider;
                    if (master == null) master = Undo.AddComponent<MeshCollider>(source.gameObject);
                    Undo.RecordObject(master, "Restore Terrain Master Collider");
                    master.sharedMesh = source.sharedMesh;
                    master.enabled = true;
                    foreach (var chunk in terrain.SurfaceColliderChunks)
                        if (chunk != null) { Undo.RecordObject(chunk, "Restore Terrain Master Collider"); chunk.enabled = false; }
                    serializedObject.Update();
                    serializedObject.FindProperty("m_MeshCollider").objectReferenceValue = master;
                    serializedObject.ApplyModifiedProperties();
                    EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
                    Physics.SyncTransforms();
                }
            }
            if (terrain.SurfaceColliderChunks.Count > 0)
                EditorGUILayout.HelpBox($"{terrain.SurfaceColliderChunks.Count} saved child collider meshes. Detail, vertex and splat brushes resolve these to this terrain. Rebuild after changing surface geometry; collider meshes retain the original UV channels. Old mesh assets remain available for Undo/recovery.", MessageType.Info);
        }

        void BuildSurfaceColliders(MGTerrain terrain)
        {
            MeshFilter filter = terrain.MeshFilter;
            Mesh source = filter != null ? filter.sharedMesh : null;
            if (source == null || !source.isReadable)
            { EditorUtility.DisplayDialog("Terrain Colliders", "A readable surface mesh is required.", "OK"); return; }
            Vector3[] vertices = source.vertices;
            int[] triangles = source.triangles;
            if (triangles.Length == 0) return;
            float sx = Mathf.Max(.0001f, filter.transform.TransformVector(Vector3.right).magnitude);
            float sz = Mathf.Max(.0001f, filter.transform.TransformVector(Vector3.forward).magnitude);
            var groups = new Dictionary<Vector2Int, List<int>>();
            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 center = (vertices[triangles[t]] + vertices[triangles[t + 1]] + vertices[triangles[t + 2]]) / 3f - source.bounds.min;
                var key = new Vector2Int(Mathf.FloorToInt(center.x * sx / m_ColliderCellSize), Mathf.FloorToInt(center.z * sz / m_ColliderCellSize));
                if (!groups.TryGetValue(key, out var indices)) groups.Add(key, indices = new List<int>());
                indices.Add(triangles[t]); indices.Add(triangles[t + 1]); indices.Add(triangles[t + 2]);
            }
            if (groups.Count > 1024)
            { EditorUtility.DisplayDialog("Terrain Colliders", "More than 1,024 chunks requested. Increase Collider Cell Size.", "OK"); return; }
            var sourceUvs = new List<Vector4>[8];
            for (int channel = 0; channel < 8; channel++) { sourceUvs[channel] = new List<Vector4>(); source.GetUVs(channel, sourceUvs[channel]); }
            const string folder = "Assets/MGTerrainColliders";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "MGTerrainColliders");
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/TerrainColliders.asset");
            var root = new GameObject("MG Terrain Collider Chunks");
            root.transform.SetParent(terrain.transform, false);
            root.layer = terrain.gameObject.layer;
            root.tag = terrain.gameObject.tag;
            var colliders = new List<MeshCollider>();
            MeshCollider original = terrain.MeshCollider;
            foreach (var group in groups)
            {
                var remap = new Dictionary<int, int>();
                var localVertices = new List<Vector3>();
                var originalIndices = new List<int>();
                var indices = new List<int>();
                foreach (int sourceIndex in group.Value)
                {
                    if (!remap.TryGetValue(sourceIndex, out int index))
                    {
                        index = localVertices.Count;
                        remap.Add(sourceIndex, index);
                        originalIndices.Add(sourceIndex);
                        localVertices.Add(terrain.transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[sourceIndex])));
                    }
                    indices.Add(index);
                }
                var mesh = new Mesh { name = $"Terrain Collider {group.Key.x},{group.Key.y}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(localVertices);
                mesh.SetTriangles(indices, 0);
                for (int channel = 0; channel < 8; channel++)
                {
                    if (sourceUvs[channel].Count != vertices.Length) continue;
                    var uv = new List<Vector4>(originalIndices.Count);
                    foreach (int index in originalIndices) uv.Add(sourceUvs[channel][index]);
                    mesh.SetUVs(channel, uv);
                }
                mesh.RecalculateBounds();
                if (colliders.Count == 0) AssetDatabase.CreateAsset(mesh, assetPath);
                else AssetDatabase.AddObjectToAsset(mesh, assetPath);
                var child = new GameObject(mesh.name);
                child.transform.SetParent(root.transform, false);
                child.layer = original != null ? original.gameObject.layer : terrain.gameObject.layer;
                child.tag = original != null ? original.gameObject.tag : terrain.gameObject.tag;
                var collider = child.AddComponent<MeshCollider>();
                if (original != null) { collider.sharedMaterial = original.sharedMaterial; collider.cookingOptions = original.cookingOptions; collider.contactOffset = original.contactOffset; }
                collider.sharedMesh = mesh;
                colliders.Add(collider);
            }
            Undo.RegisterCreatedObjectUndo(root, "Build Terrain Child Colliders");
            serializedObject.Update();
            var rootProperty = serializedObject.FindProperty("m_SurfaceColliderRoot");
            if (rootProperty.objectReferenceValue is Transform oldRoot && oldRoot.parent == terrain.transform)
                Undo.DestroyObjectImmediate(oldRoot.gameObject);
            rootProperty.objectReferenceValue = root.transform;
            var chunksProperty = serializedObject.FindProperty("m_SurfaceColliderChunks");
            chunksProperty.arraySize = colliders.Count;
            for (int i = 0; i < colliders.Count; i++) chunksProperty.GetArrayElementAtIndex(i).objectReferenceValue = colliders[i];
            serializedObject.ApplyModifiedProperties();
            if (original != null) { Undo.RecordObject(original, "Build Terrain Child Colliders"); original.enabled = false; }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            Physics.SyncTransforms();
            SceneView.RepaintAll();
        }

        void DrawDensityLayersWithFlood(MGTerrain terrain)
        {
            m_DensityDetailLayers.isExpanded = EditorGUILayout.Foldout(
                m_DensityDetailLayers.isExpanded, "Density Detail Layers", true);
            if (!m_DensityDetailLayers.isExpanded) return;
            using (new EditorGUI.IndentLevelScope())
            {
                int previousCount = m_DensityDetailLayers.arraySize;
                EditorGUILayout.PropertyField(m_DensityDetailLayers.FindPropertyRelative("Array.size"));
                for (int added = previousCount; added < m_DensityDetailLayers.arraySize; added++)
                {
                    var fresh = m_DensityDetailLayers.GetArrayElementAtIndex(added);
                    ResetDetailPainting(fresh);
                    foreach (string field in new[] { "m_MinWidth", "m_MaxWidth", "m_MinHeight", "m_MaxHeight", "m_SizeMultiplier", "m_WidthMultiplier", "m_HeightMultiplier" })
                        fresh.FindPropertyRelative(field).floatValue = 1f;
                    fresh.FindPropertyRelative("m_YOffset").floatValue = 0f;
                    fresh.FindPropertyRelative("m_Seed").intValue = UnityEngine.Random.Range(1, int.MaxValue);
                }
                for (int index = 0; index < m_DensityDetailLayers.arraySize; index++)
                {
                    SerializedProperty layer = m_DensityDetailLayers.GetArrayElementAtIndex(index);
                    EditorGUILayout.PropertyField(layer, new GUIContent($"Element {index}"), true);
                    if (!layer.isExpanded) continue;
                    using (new EditorGUI.IndentLevelScope())
                    {
                        using (new EditorGUI.DisabledScope(Application.isPlaying))
                        if (GUILayout.Button("Start Empty (This Detail Only)..."))
                        {
                            if (EditorUtility.DisplayDialog("Start Detail Empty?", "Remove this element's density and size map assignments? Other details and the original texture assets are preserved. The first density stroke creates a new independent map. Undo restores the assignments.", "Start Empty", "Cancel"))
                            {
                                FinishDetailStroke();
                                ResetDetailPainting(layer);
                                serializedObject.ApplyModifiedProperties();
                                terrain.InvalidateRenderCache();
                                m_PaintDetailIndex = index;
                                m_PaintChannel = 0;
                                SceneView.RepaintAll();
                            }
                            GUIUtility.ExitGUI();
                        }
                        terrain.GetDetailSourceMaterials(layer.FindPropertyRelative("m_PrototypeIndex").intValue, m_DetailSourceMaterials);
                        EditorGUILayout.LabelField("Detail Materials", EditorStyles.boldLabel);
                        if (m_DetailSourceMaterials.Count == 0)
                            EditorGUILayout.LabelField("No renderable materials assigned.");
                        foreach (Material material in m_DetailSourceMaterials)
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                using (new EditorGUI.DisabledScope(true))
                                    EditorGUILayout.ObjectField(material, typeof(Material), false);
                                if (GUILayout.Button("Ping", GUILayout.Width(45f)))
                                    EditorGUIUtility.PingObject(material);
                                if (GUILayout.Button("Select", GUILayout.Width(55f)))
                                    Selection.activeObject = material;
                            }
                        }
                        m_FloodDensity = EditorGUILayout.IntSlider(new GUIContent("Flood Density", "Instances per density texel. 65,535 is the maximum R16 value. Rendering still respects distance and instance budgets."), m_FloodDensity, 1, ushort.MaxValue);
                        using (new EditorGUI.DisabledScope(Application.isPlaying || layer.FindPropertyRelative("m_DensityMap").objectReferenceValue == null))
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("Flood This Detail..."))
                            {
                                FloodDensityLayer(terrain, index, false);
                                GUIUtility.ExitGUI();
                            }
                            if (GUILayout.Button("Flood + Keep Only This Detail..."))
                            {
                                FloodDensityLayer(terrain, index, true);
                                GUIUtility.ExitGUI();
                            }
                        }
                    }
                }
            }
        }

        static void ResetDetailPainting(SerializedProperty layer)
        {
            layer.FindPropertyRelative("m_DensityMap").objectReferenceValue = null;
            layer.FindPropertyRelative("m_SizeMap").objectReferenceValue = null;
            layer.FindPropertyRelative("m_RepresentedInstanceCount").longValue = 0;
            layer.FindPropertyRelative("m_PaletteSourceOnly").boolValue = false;
            layer.FindPropertyRelative("m_GeneratedByPalette").objectReferenceValue = null;
            layer.FindPropertyRelative("m_PaletteSourceMap").objectReferenceValue = null;
            layer.FindPropertyRelative("m_PaletteEntryIndex").intValue = -1;
        }

        int m_FarBakeResolution = 2048;
        float m_FarBakeOcclusion = .25f;
        float m_FarBakeClumpSize = 1.5f;
        float m_FarBakeCoverage = 1f;
        float m_CaptureExposure = 12f;
        Texture2D m_LastAppearanceCapture;

        void DrawFarGrassBake(MGTerrain terrain)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Terrain Appearance Capture", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Captures the actual terrain, painted details and visible scene geometry from above into one RGB texture. Tiled capture temporarily removes detail distance/quality thinning and enables detail shadows. Sun shadows and lighting are baked permanently into the image; this is not unlit albedo. Fog and lens effects are excluded. No shader or material is modified. UVs: terrain mesh local X/Z bounds, not mesh UV2.", MessageType.Info);
            m_FarBakeResolution = EditorGUILayout.IntPopup("Capture Resolution", m_FarBakeResolution, new[] { "2K", "4K" }, new[] { 2048, 4096 });
            m_CaptureExposure = EditorGUILayout.Slider(new GUIContent("Fixed Exposure (EV100)", "Higher values make the capture darker. Uses one fixed exposure across every tile; does not inherit automatic exposure."), m_CaptureExposure, -4f, 20f);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Last Capture", m_LastAppearanceCapture, typeof(Texture2D), false);
            using (new EditorGUI.DisabledScope(Application.isPlaying || !terrain.isActiveAndEnabled))
                if (GUILayout.Button("Capture Terrain Appearance..."))
                { serializedObject.ApplyModifiedProperties(); CaptureTerrainAppearance(terrain); GUIUtility.ExitGUI(); }
        }

        void CaptureTerrainAppearance(MGTerrain terrain)
        {
            if (terrain.MeshFilter == null || terrain.MeshFilter.sharedMesh == null || !(RenderPipelineManager.currentPipeline is HDRenderPipeline))
            { EditorUtility.DisplayDialog("Terrain Capture", "An active HDRP pipeline and a terrain mesh are required.", "OK"); return; }
            Transform surface = terrain.MeshFilter.transform;
            if (Vector3.Dot(surface.up, Vector3.up) < .999f || Mathf.Abs(Vector3.Dot(surface.right, surface.forward)) > .001f)
            { EditorUtility.DisplayDialog("Terrain Capture", "Capture currently requires an upright terrain (Y rotation is supported).", "OK"); return; }
            Bounds bounds = terrain.MeshFilter.sharedMesh.bounds;
            float metresX = surface.TransformVector(Vector3.right * bounds.size.x).magnitude;
            float metresZ = surface.TransformVector(Vector3.forward * bounds.size.z).magnitude;
            if (metresX <= .01f || metresZ <= .01f) return;
            string folderPreference = "MashBox.MGTerrain.AppearanceCapture.LastFolder." + Application.dataPath;
            string captureFolder = EditorPrefs.GetString(folderPreference, "Assets");
            if (!AssetDatabase.IsValidFolder(captureFolder)) captureFolder = "Assets";
            string path = EditorUtility.SaveFilePanelInProject("Save Terrain Appearance PNG", "TerrainAppearance", "png", "Save the rendered RGB terrain texture as a PNG image.", captureFolder);
            if (string.IsNullOrEmpty(path)) return;
            EditorPrefs.SetString(folderPreference, System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
            // Never overwrite a user's previous capture.
            path = AssetDatabase.GenerateUniqueAssetPath(System.IO.Path.ChangeExtension(path, ".png").Replace('\\', '/'));
            int resolution = m_FarBakeResolution;
            int tiles = Mathf.NextPowerOfTwo(Mathf.Max(4, Mathf.CeilToInt(Mathf.Max(metresX, metresZ) / 48f)));
            if (tiles > 64)
            { EditorUtility.DisplayDialog("Terrain Capture", "This terrain is too large for a single capture. Capture smaller terrain sections (up to 3072 metres per side).", "OK"); return; }
            foreach (var layer in terrain.DensityDetailLayers)
                if (layer != null && !layer.PaletteSourceOnly && layer.DensityMap != null && (!layer.DensityMap.isReadable || layer.DensityMap.format != TextureFormat.R16))
                { EditorUtility.DisplayDialog("Terrain Capture", "All detail density maps must be readable R16 textures.", "OK"); return; }

            GameObject captureObject = null;
            VolumeProfile profile = null;
            RenderTexture target = null;
            Texture2D texture = null;
            RenderTexture previousActive = RenderTexture.active;
            var lights = new List<(HDAdditionalLightData data, int resolution, bool useOverride, ShadowUpdateMode update)>();
            bool captureStarted = false;
            EditorApplication.LockReloadAssemblies();
            try
            {
                captureObject = new GameObject("MG Terrain Capture (Temporary)") { hideFlags = HideFlags.HideAndDontSave };
                Camera camera = captureObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.allowHDR = true;
                camera.allowMSAA = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.cullingMask = ~0;
                camera.nearClipPlane = .1f;
                float height = surface.TransformVector(Vector3.up * bounds.size.y).magnitude;
                camera.farClipPlane = height + 100f;
                camera.transform.rotation = Quaternion.LookRotation(-surface.up, surface.forward);
                var hd = captureObject.AddComponent<HDAdditionalCameraData>();
                hd.volumeLayerMask = ~0;
                hd.customRenderingSettings = true;
                foreach (FrameSettingsField field in new[] { FrameSettingsField.AtmosphericScattering, FrameSettingsField.Volumetrics,
                    FrameSettingsField.DepthOfField, FrameSettingsField.MotionBlur, FrameSettingsField.Bloom,
                    FrameSettingsField.ChromaticAberration, FrameSettingsField.Vignette, FrameSettingsField.FilmGrain,
                    FrameSettingsField.LensDistortion, FrameSettingsField.ColorGrading, FrameSettingsField.Tonemapping })
                {
                    hd.renderingPathCustomFrameSettings.SetEnabled(field, false);
                    hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
                }
                foreach (FrameSettingsField field in new[] { FrameSettingsField.ShadowMaps, FrameSettingsField.ExposureControl, FrameSettingsField.Postprocess })
                {
                    hd.renderingPathCustomFrameSettings.SetEnabled(field, true);
                    hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
                }
                var volume = captureObject.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = float.MaxValue;
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.hideFlags = HideFlags.HideAndDontSave;
                volume.sharedProfile = profile;
                var exposure = profile.Add<Exposure>();
                exposure.mode.Override(ExposureMode.Fixed);
                exposure.fixedExposure.Override(m_CaptureExposure);
                exposure.compensation.Override(0f);
                var shadows = profile.Add<HDShadowSettings>();
                shadows.maxShadowDistance.Override(camera.farClipPlane + Mathf.Max(metresX, metresZ) / tiles);
                // A single cascade concentrates the shadow map on this small orthographic tile.
                shadows.cascadeShadowSplitCount.Override(1);
                foreach (HDAdditionalLightData light in UnityEngine.Object.FindObjectsByType<HDAdditionalLightData>(FindObjectsSortMode.None))
                {
                    if (!light.TryGetComponent<Light>(out var source) || !source.enabled || source.type != LightType.Directional || source.shadows == LightShadows.None) continue;
                    lights.Add((light, light.shadowResolution.@override, light.shadowResolution.useOverride, light.shadowUpdateMode));
                    light.SetShadowResolution(4096);
                    light.SetShadowResolutionOverride(true);
                    light.shadowUpdateMode = ShadowUpdateMode.EveryFrame;
                }
                int tilePixels = resolution / tiles;
                const int border = 32;
                int capturePixels = tilePixels + border * 2;
                float expansion = capturePixels / (float)tilePixels;
                camera.aspect = metresX / metresZ;
                camera.orthographicSize = metresZ / tiles * expansion * .5f;
                target = new RenderTexture(capturePixels, capturePixels, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                { name = "MG Terrain Capture Tile", hideFlags = HideFlags.HideAndDontSave };
                target.Create();
                texture = new Texture2D(resolution, resolution, TextureFormat.RGB24, true, false)
                { name = terrain.name + "_Appearance", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
                var request = new RenderPipeline.StandardRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("The active render pipeline does not support camera capture requests.");
                terrain.BeginAppearanceCapture(camera);
                captureStarted = true;
                long completedPixels = 0;
                var layerSubmissions = new long[terrain.DensityDetailLayerCount];
                bool CaptureTile(int pixelX, int pixelZ, int pixels)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Terrain Appearance Capture", "Rendering all layers at density 1 (oversized tiles subdivide automatically)", completedPixels / (float)(resolution * resolution))) return false;
                    int paddedPixels = pixels + border * 2;
                    if (target.width != paddedPixels)
                    {
                        if (RenderTexture.active == target) RenderTexture.active = previousActive;
                        target.Release();
                        DestroyImmediate(target);
                        target = new RenderTexture(paddedPixels, paddedPixels, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                        { name = "MG Terrain Capture Tile", hideFlags = HideFlags.HideAndDontSave };
                        target.Create();
                        request.destination = target;
                    }
                    camera.orthographicSize = metresZ * paddedPixels / resolution * .5f;
                    camera.transform.position = surface.TransformPoint(new Vector3(bounds.min.x + (pixelX + pixels * .5f) * bounds.size.x / resolution,
                        bounds.max.y, bounds.min.z + (pixelZ + pixels * .5f) * bounds.size.z / resolution)) + surface.up * 20f;
                    terrain.PrepareAppearanceCaptureTile();
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    if (!terrain.AppearanceCaptureNeedsSubdivision)
                        RenderPipeline.SubmitRenderRequest(camera, request);
                    if (terrain.AppearanceCaptureNeedsSubdivision)
                    {
                        if (pixels <= 1) throw new InvalidOperationException("Even the smallest capture tile exceeds the safe detail capacity. No incomplete PNG was saved.");
                        int half = pixels / 2;
                        return CaptureTile(pixelX, pixelZ, half) && CaptureTile(pixelX + half, pixelZ, half)
                            && CaptureTile(pixelX, pixelZ + half, half) && CaptureTile(pixelX + half, pixelZ + half, half);
                    }
                    if (!terrain.AppearanceCaptureTileComplete)
                        throw new InvalidOperationException("The detail renderer did not submit every visible instance for this tile. No incomplete PNG was saved; check the Console for rendering errors.");
                    for (int layer = 0; layer < layerSubmissions.Length; layer++)
                        layerSubmissions[layer] += terrain.AppearanceCaptureLayerSubmissions[layer];
                    RenderTexture.active = target;
                    texture.ReadPixels(new Rect(border, border, pixels, pixels), pixelX, pixelZ, false);
                    completedPixels += (long)pixels * pixels;
                    return true;
                }
                for (int z = 0; z < tiles; z++)
                    for (int x = 0; x < tiles; x++)
                        if (!CaptureTile(x * tilePixels, z * tilePixels, tilePixels)) return;
                texture.Apply(true, false);
                byte[] png = texture.EncodeToPNG();
                if (png == null || png.Length == 0)
                    throw new InvalidOperationException("Could not encode the terrain capture as PNG.");
                string absolutePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", path));
                using (var file = new System.IO.FileStream(absolutePath, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write))
                    file.Write(png, 0, png.Length);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    throw new InvalidOperationException("The PNG was saved, but Unity could not import it as a texture: " + path);
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4;
                importer.maxTextureSize = resolution;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.isReadable = false;
                importer.SaveAndReimport();
                m_LastAppearanceCapture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                EditorGUIUtility.PingObject(m_LastAppearanceCapture);
                for (int layer = 0; layer < layerSubmissions.Length; layer++)
                    Debug.Log($"[MG Terrain Capture] Layer {layer}: {layerSubmissions[layer]:N0} instance submissions across completed tiles (includes overlapping borders).", terrain);
                Debug.Log($"Terrain appearance saved to {path}. RGB maps local X/Z bounds {bounds.min} to {bounds.max}; lighting and shadows are baked. No shader/material was changed.", terrain);
            }
            catch (Exception exception) { Debug.LogException(exception, terrain); EditorUtility.DisplayDialog("Terrain Capture Failed", exception.Message, "OK"); }
            finally
            {
                if (captureStarted) terrain.EndAppearanceCapture();
                foreach (var light in lights)
                    if (light.data != null)
                    {
                        light.data.SetShadowResolution(light.resolution);
                        light.data.SetShadowResolutionOverride(light.useOverride);
                        light.data.shadowUpdateMode = light.update;
                    }
                RenderTexture.active = previousActive;
                if (captureObject != null) DestroyImmediate(captureObject);
                if (profile != null)
                {
                    foreach (var component in profile.components) if (component != null) DestroyImmediate(component);
                    DestroyImmediate(profile);
                }
                if (target != null) { target.Release(); DestroyImmediate(target); }
                if (texture != null) DestroyImmediate(texture);
                EditorUtility.ClearProgressBar();
                EditorApplication.UnlockReloadAssemblies();
                SceneView.RepaintAll();
            }
        }

        void BakeFarGrass(MGTerrain terrain)
        {
            if (terrain.MeshFilter == null || terrain.MeshFilter.sharedMesh == null) return;
            var layers = new List<MGTerrain.DensityDetailLayer>();
            foreach (var layer in terrain.DensityDetailLayers)
            {
                if (layer == null || layer.PaletteSourceOnly || layer.DensityMap == null) continue;
                if (!layer.DensityMap.isReadable || layer.DensityMap.format != TextureFormat.R16)
                { EditorUtility.DisplayDialog("Far Grass Bake", "All participating density maps must be readable R16 textures.", "OK"); return; }
                layers.Add(layer);
            }
            if (layers.Count == 0) return;
            int size = m_FarBakeResolution;
            var pixels = new Color32[size * size];
            Bounds bounds = terrain.MeshFilter.sharedMesh.bounds;
            float metresX = terrain.MeshFilter.transform.TransformVector(Vector3.right * bounds.size.x).magnitude;
            float metresZ = terrain.MeshFilter.transform.TransformVector(Vector3.forward * bounds.size.z).magnitude;
            try
            {
                for (int y = 0; y < size; y++)
                {
                    if ((y & 31) == 0 && EditorUtility.DisplayCancelableProgressBar("Far Grass Bake", "Building colour / coverage and soft clump occlusion", y / (float)size)) return;
                    float v = (y + .5f) / size;
                    for (int x = 0; x < size; x++)
                    {
                        float u = (x + .5f) / size;
                        Color colour = Color.black;
                        float total = 0f;
                        foreach (var layer in layers)
                        {
                            float density = layer.DensityMap.GetPixelBilinear(u, v).r * 65535f;
                            if (density <= 0) continue;
                            float weight = Mathf.Clamp01(density / 2000f) * m_FarBakeCoverage;
                            float seed = (layer.Seed & 65535) * .013f;
                            float noise = Mathf.PerlinNoise(u * metresX / m_FarBakeClumpSize + seed, v * metresZ / m_FarBakeClumpSize + seed * .73f);
                            float darkening = 1f - m_FarBakeOcclusion * (1f - noise);
                            Color albedo = layer.FarBakeColor.linear * layer.ShaderTint.linear;
                            colour += albedo * (weight * darkening);
                            total += weight;
                        }
                        // RGB remains unpremultiplied, including low-coverage edges.
                        colour = total > 0 ? (colour / total).gamma : layers[0].FarBakeColor;
                        colour.a = Mathf.Clamp01(total);
                        pixels[y * size + x] = colour;
                    }
                }
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false)
                { name = terrain.name + "_FarGrass", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
                texture.SetPixels32(pixels);
                texture.Apply(true, false);
                const string folder = "Assets/MGTerrainFarGrass";
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "MGTerrainFarGrass");
                AssetDatabase.CreateAsset(texture, AssetDatabase.GenerateUniqueAssetPath(folder + "/FarGrass.asset"));
                Undo.RecordObject(terrain, "Bake Far Grass");
                serializedObject.Update();
                serializedObject.FindProperty("m_FarGrassBake").objectReferenceValue = texture;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(terrain);
                EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
                AssetDatabase.SaveAssets();
                SceneView.RepaintAll();
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        void DrawDetailPainter(MGTerrain terrain)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Paint Details", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(Application.isPlaying || terrain.DensityDetailLayerCount == 0))
            {
                if (GUILayout.Button(m_DetailPainting ? "Stop Painting Details" : "Paint Details in Scene"))
                    SetDetailPainting(!m_DetailPainting);
            }
            if (!m_DetailPainting) return;
            var labels = new string[terrain.DensityDetailLayerCount];
            for (int i = 0; i < labels.Length; i++)
            {
                var detail = terrain.DensityDetailLayers[i];
                int p = detail.PrototypeIndex;
                string name = p >= 0 && p < terrain.Prototypes.Count && terrain.Prototypes[p] != null && terrain.Prototypes[p].Prefab != null
                    ? terrain.Prototypes[p].Prefab.name : "Prototype " + p;
                labels[i] = i + ": " + name;
            }
            if (labels.Length == 0) { SetDetailPainting(false); return; }
            m_PaintDetailIndex = EditorGUILayout.Popup("Detail", Mathf.Clamp(m_PaintDetailIndex, 0, labels.Length - 1), labels);
            m_PaintChannel = GUILayout.Toolbar(m_PaintChannel, new[] { "Density", "Size" });
            var selectedDetail = terrain.DensityDetailLayers[m_PaintDetailIndex];
            EditorGUILayout.LabelField("Selected Detail Density", selectedDetail.RepresentedInstanceCount.ToString("N0"));
            terrain.GetDetailSourceMaterials(selectedDetail.PrototypeIndex, m_DetailSourceMaterials);
            if (m_DetailSourceMaterials.Count == 0)
                EditorGUILayout.HelpBox("This prototype has no renderable mesh/material. Check its Prefab, enabled MeshRenderer and MeshFilter (including LOD0), or assign Mesh and Material directly. Painting cannot display this detail until that is fixed.", MessageType.Warning);
            if (selectedDetail.DensityMap == null)
                EditorGUILayout.HelpBox("No density map yet. A density stroke on this terrain will create it automatically.", MessageType.Info);
            MBEditorToolState.BrushRadius = EditorGUILayout.Slider("Brush Radius", MBEditorToolState.BrushRadius, .1f, 10f);
            MBEditorToolState.BrushStrength = EditorGUILayout.Slider("Brush Strength", MBEditorToolState.BrushStrength, .01f, 1f);
            if (m_PaintChannel == 0)
                m_PaintDensity = EditorGUILayout.IntSlider("Target Density / Texel", m_PaintDensity, 1, 2000);
            else
                m_PaintSize = EditorGUILayout.Slider("Target Size Multiplier", m_PaintSize, .05f, 4f);
            EditorGUILayout.HelpBox("Drag to paint with soft falloff. Shift erases density or restores size to 1. Ctrl + middle-drag: horizontal = radius, vertical = strength. Alt navigates; Esc stops. Size multiplies existing width and height; it does not change density. Painted cells refresh live during the stroke. The first stroke makes a working copy; original maps are preserved.", MessageType.Info);
            if (terrain.DensityDetailLayers[m_PaintDetailIndex].GeneratedByPalette != null)
                EditorGUILayout.HelpBox("This is a palette-generated layer. Baking the palette again can replace this layer and its painting.", MessageType.Warning);
            if (!terrain.HasSurfaceCollider)
                EditorGUILayout.HelpBox("Enable the terrain's Mesh Collider to brush its surface.", MessageType.Warning);
            if (terrain.DensityDetailLayers[m_PaintDetailIndex].PaletteSourceOnly)
                EditorGUILayout.HelpBox("This layer is a palette source, not rendered directly. Paint its density with the palette workflow and bake it, or select a rendered detail layer here.", MessageType.Warning);
        }

        void SetDetailPainting(bool enabled)
        {
            if (enabled == m_DetailPainting) return;
            FinishDetailStroke();
            m_DetailPainting = enabled;
            if (enabled)
            {
                m_PreviousTool = Tools.current;
                m_PreviousEditing = MBEditorToolState.ActiveEditing;
                MBEditorToolState.ActiveEditing = false;
                Tools.current = Tool.None;
            }
            else
            {
                EndDetailBrushAdjustment();
                GUIUtility.hotControl = 0;
                Tools.current = m_PreviousTool;
                MBEditorToolState.ActiveEditing = m_PreviousEditing;
            }
            SceneView.RepaintAll();
            Repaint();
        }

        void OnDisable()
        {
            SetDetailPainting(false);
            Undo.undoRedoPerformed -= RefreshPaintUndo;
            EditorApplication.update -= UpdateDetailPaintPreview;
        }

        void RefreshPaintUndo()
        {
            foreach (Texture2D map in m_PaintCopies)
                if (map != null && map.isReadable) { map.Apply(false, false); EditorUtility.SetDirty(map); }
            if (target is MGTerrain terrain) terrain.InvalidateRenderCache();
            SceneView.RepaintAll();
            Repaint();
        }

        void OnSceneGUI()
        {
            if (!m_DetailPainting) return;
            var terrain = (MGTerrain)target;
            if (Application.isPlaying || MBEditorToolState.ActiveEditing || Tools.current != Tool.None)
            { SetDetailPainting(false); return; }
            Event e = Event.current;
            int control = GUIUtility.GetControlID("MGDetailPaint".GetHashCode(), FocusType.Passive);
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            { SetDetailPainting(false); e.Use(); return; }
            if (e.type == EventType.Layout && !e.alt) HandleUtility.AddDefaultControl(control);
            if (e.type == EventType.MouseDown && e.button == 2 && e.control && !e.alt)
            {
                FinishDetailStroke();
                m_AdjustDetailBrush = true;
                m_DetailAdjustMouse = e.mousePosition;
                Physics.SyncTransforms();
                var surface = terrain.MeshCollider;
                m_HasDetailAdjustSurface = false;
                if (terrain.RaycastSurface(HandleUtility.GUIPointToWorldRay(e.mousePosition), out RaycastHit anchor, float.MaxValue))
                {
                    m_HasDetailAdjustSurface = true;
                    m_DetailAdjustPoint = anchor.point;
                    m_DetailAdjustNormal = anchor.normal;
                }
                GUIUtility.hotControl = control;
                EditorGUIUtility.SetWantsMouseJumping(1);
                e.Use();
                SceneView.RepaintAll();
            }
            if (m_AdjustDetailBrush && e.type == EventType.MouseDrag && e.button == 2)
            {
                MBEditorToolState.BrushRadius *= Mathf.Exp(e.delta.x * .01f);
                MBEditorToolState.BrushStrength -= e.delta.y * .005f;
                e.Use(); Repaint(); SceneView.RepaintAll();
            }
            if (e.rawType == EventType.MouseUp || e.type == EventType.Ignore)
            {
                FinishDetailStroke();
                EndDetailBrushAdjustment();
                if (GUIUtility.hotControl == control) { GUIUtility.hotControl = 0; e.Use(); }
                SceneView.RepaintAll();
            }
            if (m_AdjustDetailBrush)
            {
                DrawDetailBrushAdjustment();
                return;
            }
            if (e.alt) return;
            // Only hit the selected terrain, not nearby props or loft colliders.
            Physics.SyncTransforms();
            var collider = terrain.MeshCollider;
            if (!terrain.RaycastSurface(HandleUtility.GUIPointToWorldRay(e.mousePosition), out RaycastHit hit, float.MaxValue)) return;
            Handles.color = m_PaintChannel == 0 ? Color.green : Color.cyan;
            Handles.DrawWireDisc(hit.point, hit.normal, MBEditorToolState.BrushRadius);
            Handles.Label(hit.point, m_PaintChannel == 0 ? "Detail density" : $"Detail size ×{(e.shift ? 1f : m_PaintSize):0.00}");
            if (e.type == EventType.MouseMove) SceneView.RepaintAll();
            if (e.button != 0 || (e.type != EventType.MouseDown && e.type != EventType.MouseDrag)) return;
            if (e.type == EventType.MouseDown)
            {
                if (!BeginDetailStroke(terrain)) return;
                GUIUtility.hotControl = control;
                m_LastDetailDab = hit.point;
                PaintDetailDab(terrain, hit.point, e.shift);
            }
            else if (m_StrokeMap != null)
            {
                float spacing = Mathf.Max(.05f, MBEditorToolState.BrushRadius * .2f);
                float distance = Vector3.Distance(m_LastDetailDab, hit.point);
                int steps = Mathf.Min(128, Mathf.FloorToInt(distance / spacing));
                Vector3 start = m_LastDetailDab;
                for (int i = 1; i <= steps; i++)
                {
                    m_LastDetailDab = Vector3.MoveTowards(start, hit.point, i * spacing);
                    PaintDetailDab(terrain, m_LastDetailDab, e.shift);
                }
            }
            e.Use();
        }

        void EndDetailBrushAdjustment()
        {
            if (!m_AdjustDetailBrush) return;
            m_AdjustDetailBrush = false;
            m_HasDetailAdjustSurface = false;
            EditorGUIUtility.SetWantsMouseJumping(0);
        }

        void DrawDetailBrushAdjustment()
        {
            float radius = MBEditorToolState.BrushRadius;
            float strength = MBEditorToolState.BrushStrength;
            if (m_HasDetailAdjustSurface)
            {
                Color previousColor = Handles.color;
                Handles.color = new Color(1f, .82f, .12f, 1f);
                Handles.DrawWireDisc(m_DetailAdjustPoint, m_DetailAdjustNormal, radius);
                Handles.color = Color.Lerp(new Color(1f, .25f, .12f, .9f), new Color(.2f, 1f, .35f, .95f), strength);
                Handles.DrawWireDisc(m_DetailAdjustPoint + m_DetailAdjustNormal * HandleUtility.GetHandleSize(m_DetailAdjustPoint) * .002f,
                    m_DetailAdjustNormal, radius * strength);
                Handles.color = previousColor;
            }
            Handles.BeginGUI();
            var panel = new Rect(m_DetailAdjustMouse.x + 18f, m_DetailAdjustMouse.y + 18f, 250f, 50f);
            // Keep feedback visible when adjustment starts near a Scene view edge.
            var view = SceneView.currentDrawingSceneView;
            if (view != null)
            {
                panel.x = Mathf.Clamp(panel.x, 0f, Mathf.Max(0f, view.position.width - panel.width));
                panel.y = Mathf.Clamp(panel.y, 0f, Mathf.Max(0f, view.position.height - panel.height - 30f));
            }
            GUI.Box(panel, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(new Rect(panel.x + 8f, panel.y + 4f, panel.width - 16f, 18f),
                $"Radius  {radius:0.00}   (drag horizontally)", EditorStyles.miniBoldLabel);
            EditorGUI.ProgressBar(new Rect(panel.x + 8f, panel.y + 27f, panel.width - 16f, 16f),
                strength, $"Strength  {strength:0.00}   (drag vertically)");
            Handles.EndGUI();
        }

        bool BeginDetailStroke(MGTerrain terrain)
        {
            if ((uint)m_PaintDetailIndex >= terrain.DensityDetailLayerCount) return false;
            var detail = terrain.DensityDetailLayers[m_PaintDetailIndex];
            if (detail.PaletteSourceOnly || detail.PrototypeIndex < 0 || detail.PrototypeIndex >= terrain.Prototypes.Count)
            { EditorUtility.DisplayDialog("Cannot Paint Detail", "Select a rendered detail layer with a valid prototype, not a palette source-only layer.", "OK"); return false; }
            Texture2D source = m_PaintChannel == 0 ? detail.DensityMap : detail.SizeMap;
            TextureFormat format = m_PaintChannel == 0 ? TextureFormat.R16 : TextureFormat.RHalf;
            if (source != null && (!source.isReadable || source.format != format))
            { EditorUtility.DisplayDialog("Cannot Paint Detail", $"The map must be readable {format}.", "OK"); return false; }
            Undo.IncrementCurrentGroup();
            m_StrokeUndo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Paint MG Terrain Detail");
            Undo.RegisterCompleteObjectUndo(terrain, "Paint MG Terrain Detail");
            bool shared = false;
            foreach (var other in terrain.DensityDetailLayers)
                if (other != detail && source != null && (other.DensityMap == source || other.SizeMap == source)) shared = true;
            if (source == null || shared || !m_PaintCopies.Contains(source))
            {
                int width = detail.DensityMap != null ? detail.DensityMap.width : 512;
                int height = detail.DensityMap != null ? detail.DensityMap.height : 512;
                var copy = source != null ? Instantiate(source) : new Texture2D(width, height, format, false, true);
                copy.name = terrain.name + (m_PaintChannel == 0 ? "_DetailDensity" : "_DetailSize");
                copy.wrapMode = TextureWrapMode.Clamp;
                copy.filterMode = FilterMode.Bilinear;
                if (source == null)
                {
                    var pixels = copy.GetPixelData<ushort>(0);
                    ushort neutral = m_PaintChannel == 0 ? (ushort)0 : Mathf.FloatToHalf(1f);
                    for (int i = 0; i < pixels.Length; i++) pixels[i] = neutral;
                    copy.Apply(false, false);
                }
                const string folder = "Assets/MGTerrainDetailPaint";
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "MGTerrainDetailPaint");
                AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + (m_PaintChannel == 0 ? "Density" : "Size") + ".asset"));
                serializedObject.Update();
                var layer = m_DensityDetailLayers.GetArrayElementAtIndex(m_PaintDetailIndex);
                layer.FindPropertyRelative(m_PaintChannel == 0 ? "m_DensityMap" : "m_SizeMap").objectReferenceValue = copy;
                serializedObject.ApplyModifiedProperties();
                source = copy;
                m_PaintCopies.Add(copy);
            }
            m_StrokeMap = source;
            m_HasPendingPaint = false;
            m_NextPaintPreview = 0;
            Undo.RegisterCompleteObjectUndo(m_StrokeMap, "Paint MG Terrain Detail");
            return true;
        }

        void PaintDetailDab(MGTerrain terrain, Vector3 point, bool erase)
        {
            if (m_StrokeMap == null || terrain.MeshFilter == null || terrain.MeshFilter.sharedMesh == null) return;
            Bounds bounds = terrain.MeshFilter.sharedMesh.bounds;
            Vector3 center = terrain.transform.InverseTransformPoint(point);
            float radius = MBEditorToolState.BrushRadius;
            float rx = radius / Mathf.Max(.0001f, terrain.transform.TransformVector(Vector3.right).magnitude);
            float rz = radius / Mathf.Max(.0001f, terrain.transform.TransformVector(Vector3.forward).magnitude);
            int w = m_StrokeMap.width, h = m_StrokeMap.height;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((center.x - rx - bounds.min.x) / bounds.size.x * w), 0, w - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((center.x + rx - bounds.min.x) / bounds.size.x * w), 0, w - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((center.z - rz - bounds.min.z) / bounds.size.z * h), 0, h - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((center.z + rz - bounds.min.z) / bounds.size.z * h), 0, h - 1);
            var pixels = m_StrokeMap.GetPixelData<ushort>(0);
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
            {
                Vector3 delta = new Vector3(bounds.min.x + (x + .5f) / w * bounds.size.x - center.x, 0, bounds.min.z + (z + .5f) / h * bounds.size.z - center.z);
                float d = terrain.transform.TransformVector(delta).magnitude / radius;
                if (d >= 1) continue;
                float influence = Mathf.SmoothStep(1, 0, d) * MBEditorToolState.BrushStrength;
                int index = z * w + x;
                float previous = m_PaintChannel == 0 ? pixels[index] : Mathf.HalfToFloat(pixels[index]);
                float goal = m_PaintChannel == 0 ? (erase ? 0 : Mathf.Clamp(m_PaintDensity, 1, 2000)) : (erase ? 1 : m_PaintSize);
                float next = Mathf.Lerp(previous, goal, influence);
                pixels[index] = m_PaintChannel == 0 ? (ushort)Mathf.RoundToInt(next) : Mathf.FloatToHalf(next);
            }
            Rect region = Rect.MinMaxRect(x0 / (float)w, z0 / (float)h, (x1 + 1f) / w, (z1 + 1f) / h);
            m_PendingPaintRegion = m_HasPendingPaint
                ? Rect.MinMaxRect(Mathf.Min(m_PendingPaintRegion.xMin, region.xMin), Mathf.Min(m_PendingPaintRegion.yMin, region.yMin), Mathf.Max(m_PendingPaintRegion.xMax, region.xMax), Mathf.Max(m_PendingPaintRegion.yMax, region.yMax))
                : region;
            m_HasPendingPaint = true;
        }

        void UpdateDetailPaintPreview()
        {
            if (m_StrokeMap == null || !m_HasPendingPaint || EditorApplication.timeSinceStartup < m_NextPaintPreview) return;
            FlushDetailPaintPreview();
        }

        void FlushDetailPaintPreview()
        {
            if (m_StrokeMap == null || !m_HasPendingPaint) return;
            m_StrokeMap.Apply(false, false);
            if (target is MGTerrain terrain) terrain.RefreshDetailPaintRegion(m_PaintDetailIndex, m_PendingPaintRegion);
            m_HasPendingPaint = false;
            m_NextPaintPreview = EditorApplication.timeSinceStartup + .1;
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        void FinishDetailStroke()
        {
            if (m_StrokeMap == null) return;
            FlushDetailPaintPreview();
            m_StrokeMap.Apply(false, false);
            EditorUtility.SetDirty(m_StrokeMap);
            if (target is MGTerrain terrain)
            {
                if (m_PaintChannel == 0)
                {
                    terrain.RefreshDetailPaintRegion(m_PaintDetailIndex, default, true);
                }
                EditorUtility.SetDirty(terrain);
                if (terrain.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            }
            // Leave the texture dirty for the normal project save workflow.
            // Saving here stalls the editor after every brush stroke.
            Undo.CollapseUndoOperations(m_StrokeUndo);
            m_StrokeMap = null;
            m_StrokeUndo = -1;
            SceneView.RepaintAll();
        }

        void FloodDensityLayer(MGTerrain terrain, int index, bool onlyThisDetail)
        {
            serializedObject.ApplyModifiedProperties();
            SerializedProperty layer = m_DensityDetailLayers.GetArrayElementAtIndex(index);
            var source = layer.FindPropertyRelative("m_DensityMap").objectReferenceValue as Texture2D;
            if (source == null) return;
            int prototype = layer.FindPropertyRelative("m_PrototypeIndex").intValue;
            if (prototype < 0 || prototype >= terrain.Prototypes.Count)
            {
                EditorUtility.DisplayDialog("Cannot Flood Detail", "Assign a valid Prototype Index first.", "OK");
                return;
            }
            long represented = (long)source.width * source.height * m_FloodDensity;
            string isolation = onlyThisDetail
                ? "Other density layer entries will be removed from this terrain (their texture assets are kept). Palette bindings remain; rebaking them can add layers again.\n\n"
                : "Other density layers will remain active.\n\n";
            if (!EditorUtility.DisplayDialog("Flood MG Terrain Detail?",
                $"Fill Element {index}, prototype {prototype}, across the entire {source.width} × {source.height} map at {m_FloodDensity:N0} per texel?\n\n"
                + $"This represents {represented:N0} instances; draw budgets and distance limits still apply.\n\n"
                + isolation + "A new density texture will be created, preserving the original painting. Undo restores the terrain assignment; the new texture asset remains available.",
                "Flood Density", "Cancel")) return;

            const string folder = "Assets/MGTerrainDensityTests";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets", "MGTerrainDensityTests");
            var filled = new Texture2D(source.width, source.height, TextureFormat.R16, false, true)
            {
                name = source.name + "_Flood",
                filterMode = source.filterMode,
                wrapMode = source.wrapMode
            };
            var pixels = filled.GetPixelData<ushort>(0);
            for (int pixel = 0; pixel < pixels.Length; pixel++) pixels[pixel] = (ushort)m_FloodDensity;
            filled.Apply(false, false);
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/DetailFlood.asset");
            AssetDatabase.CreateAsset(filled, path);
            Undo.RecordObject(terrain, "Flood MG Terrain Detail");
            serializedObject.Update();
            layer = m_DensityDetailLayers.GetArrayElementAtIndex(index);
            layer.FindPropertyRelative("m_DensityMap").objectReferenceValue = filled;
            layer.FindPropertyRelative("m_RepresentedInstanceCount").longValue = represented;
            layer.FindPropertyRelative("m_PaletteSourceOnly").boolValue = false;
            layer.FindPropertyRelative("m_GeneratedByPalette").objectReferenceValue = null;
            layer.FindPropertyRelative("m_PaletteSourceMap").objectReferenceValue = null;
            layer.FindPropertyRelative("m_PaletteEntryIndex").intValue = -1;
            if (onlyThisDetail)
                for (int other = m_DensityDetailLayers.arraySize - 1; other >= 0; other--)
                    if (other != index) m_DensityDetailLayers.DeleteArrayElementAtIndex(other);
            serializedObject.ApplyModifiedProperties();
            terrain.InvalidateRenderCache();
            EditorUtility.SetDirty(terrain);
            if (terrain.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            SceneView.RepaintAll();
        }

        void DrawDetailFoliagePalettes(MGTerrain terrain)
        {
            EditorGUILayout.Space(5f);
            EditorGUILayout.LabelField("Detail Foliage Palettes", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Paint one R16 master density map, then bake it into natural variant layers. The bake adds broad density breakup, coherent clumps, soft edges, slope/height filtering and rare hero plants without adding runtime procedural work.",
                MessageType.Info);
            EditorGUILayout.PropertyField(m_DetailFoliagePalettes, true);

            bool needsBake = false;
            int missingSources = 0;
            for (int index = 0; index < terrain.DetailFoliagePalettes.Count; index++)
            {
                MGTerrain.DetailFoliagePaletteBinding binding = terrain.DetailFoliagePalettes[index];
                if (binding != null && binding.Enabled && binding.SourceDensityMap == null)
                    missingSources++;
                if (binding != null && binding.Enabled && binding.NeedsBake)
                {
                    needsBake = true;
                }
            }
            if (missingSources > 0)
            {
                bool canAutoAssign = terrain.TryGetAutomaticDetailFoliageSource(out Texture2D automaticSource, out int sourceCount);
                if (canAutoAssign)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.HelpBox(
                            $"{missingSources:N0} palette binding(s) need a source mask. The existing '{automaticSource.name}' density map can be assigned automatically.",
                            MessageType.Warning);
                        if (GUILayout.Button("Use Existing", GUILayout.Width(92f), GUILayout.Height(38f)))
                        {
                            serializedObject.ApplyModifiedProperties();
                            Undo.RecordObject(terrain, "Assign Detail Foliage Source Map");
                            for (int index = 0; index < terrain.DetailFoliagePalettes.Count; index++)
                                terrain.TryAssignAutomaticDetailFoliageSource(terrain.DetailFoliagePalettes[index], out _);
                            EditorUtility.SetDirty(terrain);
                            serializedObject.Update();
                        }
                    }
                }
                else if (sourceCount > 1)
                {
                    EditorGUILayout.HelpBox(
                        $"This terrain has {sourceCount:N0} possible master density maps. Assign the intended one to Source Density Map above.",
                        MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "No painted density map exists yet. Create or paint a density detail layer, then use that R16 map as Source Density Map.",
                        MessageType.Warning);
                }
            }
            if (needsBake)
                EditorGUILayout.HelpBox("A palette or painted source mask has changed. Preview / Bake to refresh its generated density layers.", MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create Palette..."))
                    CreateDetailFoliagePalette(terrain);
                using (new EditorGUI.DisabledScope(terrain.DetailFoliagePalettes.Count == 0))
                {
                    if (GUILayout.Button("Preview / Bake"))
                        BakeDetailFoliagePalettes(terrain, false);
                    if (GUILayout.Button("Reseed + Bake"))
                        BakeDetailFoliagePalettes(terrain, true);
                }
            }

            int generatedLayers = MGDetailFoliagePaletteBaker.CountExistingGeneratedLayers(terrain);
            using (new EditorGUI.DisabledScope(generatedLayers == 0))
            {
                if (GUILayout.Button($"Clear Generated Layers... ({generatedLayers:N0})")
                    && EditorUtility.DisplayDialog(
                        "Clear Generated Detail Foliage?",
                        $"Remove {generatedLayers:N0} generated density layers from '{terrain.name}'? The source masks and generated texture assets will be preserved.",
                        "Clear Generated Layers",
                        "Cancel"))
                {
                    serializedObject.ApplyModifiedProperties();
                    int removed = MGDetailFoliagePaletteBaker.ClearGeneratedLayers(terrain);
                    serializedObject.Update();
                    m_HasMemoryUsageSnapshot = false;
                    Debug.Log($"MG Terrain removed {removed:N0} generated foliage-palette layers from '{terrain.name}'.", terrain);
                }
            }
            EditorGUILayout.Space(3f);
        }

        void CreateDetailFoliagePalette(MGTerrain terrain)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create MG Detail Foliage Palette",
                $"{terrain.name}_DetailFoliagePalette",
                "asset",
                "Choose where to save the reusable foliage palette asset.");
            if (string.IsNullOrEmpty(path))
                return;

            var palette = CreateInstance<MGDetailFoliagePalette>();
            palette.name = System.IO.Path.GetFileNameWithoutExtension(path);
            palette.ConfigureNaturalStarterSet();
            terrain.TryGetAutomaticDetailFoliageSource(out Texture2D automaticSource, out _);
            if (terrain.TryGetDensityDetailPrototype(automaticSource, out MGTerrain.Prototype sourcePrototype))
                palette.TrySeedPrimaryPrototype(sourcePrototype);
            AssetDatabase.CreateAsset(palette, path);
            AssetDatabase.SaveAssets();
            Undo.RecordObject(terrain, "Add MG Detail Foliage Palette");
            terrain.AddDetailFoliagePalette(palette, automaticSource);
            EditorUtility.SetDirty(terrain);
            serializedObject.Update();
            Selection.activeObject = palette;
            EditorGUIUtility.PingObject(palette);
        }

        void BakeDetailFoliagePalettes(MGTerrain terrain, bool randomizeSeeds)
        {
            serializedObject.ApplyModifiedProperties();
            int existing = MGDetailFoliagePaletteBaker.CountExistingGeneratedLayers(terrain);
            if (existing > 0
                && !EditorUtility.DisplayDialog(
                    randomizeSeeds ? "Reseed Detail Foliage?" : "Refresh Detail Foliage Bake?",
                    $"Replace {existing:N0} generated density layers on '{terrain.name}'? The master masks remain untouched and this terrain change can be undone.",
                    randomizeSeeds ? "Reseed + Bake" : "Bake",
                    "Cancel"))
            {
                serializedObject.Update();
                return;
            }

            MGDetailFoliagePaletteBaker.BakeResult result = MGDetailFoliagePaletteBaker.BakeAll(terrain, randomizeSeeds);
            serializedObject.Update();
            m_HasMemoryUsageSnapshot = false;
            if (!string.IsNullOrEmpty(result.error))
                EditorUtility.DisplayDialog("Detail Foliage Palette Bake", result.error, "OK");
            if (result.layerCount > 0)
            {
                Debug.Log(
                    $"MG Terrain baked {result.paletteCount:N0} foliage palette(s) into {result.layerCount:N0} compact density layers "
                    + $"representing {result.instanceCount:N0} details on '{terrain.name}'.",
                    terrain);
            }
        }

        static void DrawDetailInstanceCapSlider(SerializedProperty property)
        {
            var label = new GUIContent(
                "Max Instances Per Cell",
                "Hard safety cap for one streamed HLOD cell. Larger values preserve very dense grass in larger cells, but require more build time, upload bandwidth, GPU memory, and rendering work.");
            int selectedIndex = FindNearestDetailInstanceCapIndex(property.intValue);
            Rect row = EditorGUILayout.GetControlRect();
            Rect controls = EditorGUI.PrefixLabel(row, label);
            const float popupWidth = 82f;
            const float gap = 5f;
            var sliderRect = new Rect(
                controls.x,
                controls.y,
                Mathf.Max(1f, controls.width - popupWidth - gap),
                controls.height);
            var popupRect = new Rect(
                sliderRect.xMax + gap,
                controls.y,
                popupWidth,
                controls.height);

            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            selectedIndex = Mathf.RoundToInt(GUI.HorizontalSlider(
                sliderRect,
                selectedIndex,
                0,
                DetailInstanceCapSteps.Length - 1));
            selectedIndex = EditorGUI.Popup(
                popupRect,
                Mathf.Clamp(selectedIndex, 0, DetailInstanceCapSteps.Length - 1),
                DetailInstanceCapLabels);
            if (EditorGUI.EndChangeCheck())
                property.intValue = DetailInstanceCapSteps[selectedIndex];
            EditorGUI.showMixedValue = false;
        }

        static int FindNearestDetailInstanceCapIndex(int value)
        {
            int nearestIndex = 0;
            long nearestDistance = long.MaxValue;
            for (int i = 0; i < DetailInstanceCapSteps.Length; i++)
            {
                long distance = Math.Abs((long)value - DetailInstanceCapSteps[i]);
                if (distance >= nearestDistance)
                    continue;

                nearestDistance = distance;
                nearestIndex = i;
            }

            return nearestIndex;
        }

        void DrawDetailQualityPresets(MGTerrain terrain)
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField(
                new GUIContent(
                    "Detail Quality Preset",
                    "Applies a balanced group of distance, HLOD density, cell-cache, streaming, shadow, and visible-instance settings. Every field remains editable after applying a preset."),
                EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Low", "60 m, 25k visible details, aggressively thinned distance grass, no detail shadows.")))
                    ApplyDetailQualityPreset(terrain, DetailQualityPreset.Low);
                if (GUILayout.Button(new GUIContent("Medium", "90 m, 50k visible details, dense foreground and economical distance grass, no detail shadows.")))
                    ApplyDetailQualityPreset(terrain, DetailQualityPreset.Medium);
                if (GUILayout.Button(new GUIContent("High", "130 m, 100k visible details, dense foreground with smoother middle-distance coverage, no detail shadows.")))
                    ApplyDetailQualityPreset(terrain, DetailQualityPreset.High);
                if (GUILayout.Button(new GUIContent("Ultra", "180 m, 200k visible details, high distance density and detail shadows where the prototype permits them.")))
                    ApplyDetailQualityPreset(terrain, DetailQualityPreset.Ultra);
            }
        }

        void ApplyDetailQualityPreset(MGTerrain terrain, DetailQualityPreset preset)
        {
            Undo.RecordObject(terrain, $"Apply MG Terrain {preset} Detail Quality");
            terrain.ApplyDetailQualityPreset(preset);
            serializedObject.Update();
            EditorUtility.SetDirty(terrain);
            m_HasMemoryUsageSnapshot = false;
            SceneView.RepaintAll();
        }

        void DrawMemoryUsage(MGTerrain terrain)
        {
            m_ShowMemoryUsage = EditorGUILayout.Foldout(
                m_ShowMemoryUsage,
                "Memory Usage (Estimated)",
                true);
            if (!m_ShowMemoryUsage)
                return;

            bool refreshMemoryUsage = GUILayout.Button("Refresh Memory Usage");
            if (!m_HasMemoryUsageSnapshot || refreshMemoryUsage)
            {
                m_MemoryUsageSnapshot = terrain.CaptureMemoryUsageSnapshot();
                m_HasMemoryUsageSnapshot = true;
            }

            EditorGUI.indentLevel++;
            DrawMemoryRow("Surface Mesh", m_MemoryUsageSnapshot.SurfaceMeshBytes);
            DrawMemoryRow("Control Maps", m_MemoryUsageSnapshot.ControlMapBytes);
            DrawMemoryRow("Density Maps", m_MemoryUsageSnapshot.DensityMapBytes);
            DrawMemoryRow("Serialized Instances", m_MemoryUsageSnapshot.SerializedInstanceBytes);
            DrawMemoryRow("Streamed Mesh Cache", m_MemoryUsageSnapshot.StreamedCombinedMeshBytes);
            DrawMemoryRow("Matrix Buffers", m_MemoryUsageSnapshot.MatrixBufferBytes);
            DrawMemoryRow("Compact Cell Spans", m_MemoryUsageSnapshot.ProceduralCellDataBytes);
            DrawMemoryRow("Pending Worker Builds", m_MemoryUsageSnapshot.PendingBuildBytes);
            DrawMemoryRow("CPU Source Caches", m_MemoryUsageSnapshot.CpuSourceCacheBytes);
            DrawMemoryRow("Runtime Materials", m_MemoryUsageSnapshot.RuntimeMaterialBytes);
            EditorGUILayout.LabelField(
                "Cached Detail Cells",
                m_MemoryUsageSnapshot.CachedDetailCellCount.ToString("N0"));
            EditorGUILayout.LabelField(
                "Combined Mesh Slices",
                m_MemoryUsageSnapshot.CombinedMeshSliceCount.ToString("N0"));
            EditorGUILayout.LabelField(
                "Pending Build Slices",
                m_MemoryUsageSnapshot.PendingBuildCount.ToString("N0"));
            EditorGUILayout.LabelField(
                "Estimated Total",
                FormatBytes(m_MemoryUsageSnapshot.TotalBytes),
                EditorStyles.boldLabel);
            EditorGUI.indentLevel--;

            long transientDetailBytes = m_MemoryUsageSnapshot.StreamedCombinedMeshBytes
                + m_MemoryUsageSnapshot.MatrixBufferBytes
                + m_MemoryUsageSnapshot.PendingBuildBytes
                + m_MemoryUsageSnapshot.CpuSourceCacheBytes;
            if (transientDetailBytes >= 512L * 1024L * 1024L)
            {
                EditorGUILayout.HelpBox(
                    $"The streamed detail working set is about {FormatBytes(transientDetailBytes)}. Lower Max Cached Detail Chunks and/or Max Instances Per Cell to reduce it.",
                    MessageType.Warning);
            }
            EditorGUILayout.HelpBox(
                "This is this terrain's referenced/streamed estimate. Unity's Total Allocated Memory also includes the editor, the full scene, render targets, packages, and other loaded assets, so the two numbers will not match exactly.",
                MessageType.None);
        }

        static void DrawMemoryRow(string label, long bytes) =>
            EditorGUILayout.LabelField(label, FormatBytes(bytes));

        static string FormatBytes(long bytes)
        {
            const double kilo = 1024.0;
            const double mega = kilo * 1024.0;
            const double giga = mega * 1024.0;
            if (bytes >= giga)
                return $"{bytes / giga:0.00} GB";
            if (bytes >= mega)
                return $"{bytes / mega:0.0} MB";
            if (bytes >= kilo)
                return $"{bytes / kilo:0.0} KB";
            return $"{bytes:N0} B";
        }

        static void DrawVisibleInstanceBudgetSlider(SerializedProperty property)
        {
            var label = new GUIContent(
                "Visible Instance Budget",
                "Maximum density-map details this terrain submits to one camera. Full-density nearby cells are kept first; middle and far cells share the remaining budget. Higher values cost more GPU rendering time.");
            int selectedIndex = FindNearestVisibleInstanceBudgetIndex(property.intValue);
            Rect row = EditorGUILayout.GetControlRect();
            Rect controls = EditorGUI.PrefixLabel(row, label);
            const float popupWidth = 88f;
            const float gap = 5f;
            var sliderRect = new Rect(
                controls.x,
                controls.y,
                Mathf.Max(1f, controls.width - popupWidth - gap),
                controls.height);
            var popupRect = new Rect(
                sliderRect.xMax + gap,
                controls.y,
                popupWidth,
                controls.height);

            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            selectedIndex = Mathf.RoundToInt(GUI.HorizontalSlider(
                sliderRect,
                selectedIndex,
                0,
                VisibleInstanceBudgetSteps.Length - 1));
            selectedIndex = EditorGUI.Popup(
                popupRect,
                Mathf.Clamp(selectedIndex, 0, VisibleInstanceBudgetSteps.Length - 1),
                VisibleInstanceBudgetLabels);
            if (EditorGUI.EndChangeCheck())
                property.intValue = VisibleInstanceBudgetSteps[selectedIndex];
            EditorGUI.showMixedValue = false;
        }

        static int FindNearestVisibleInstanceBudgetIndex(int value)
        {
            if (value <= 0)
                return VisibleInstanceBudgetSteps.Length - 1;

            int nearestIndex = 0;
            long nearestDistance = long.MaxValue;
            // The final entry is Unlimited (serialized as zero), so only compare
            // finite presets when snapping an existing numeric value.
            for (int i = 0; i < VisibleInstanceBudgetSteps.Length - 1; i++)
            {
                long distance = Math.Abs((long)value - VisibleInstanceBudgetSteps[i]);
                if (distance >= nearestDistance)
                    continue;

                nearestDistance = distance;
                nearestIndex = i;
            }

            return nearestIndex;
        }

        static void DrawMappyToolLauncher(MGTerrain terrain)
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Edit With MashBox Mappy", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent(
                        "Paint",
                        "Activate the MashBox Mappy panel in Splat Map mode for this terrain.")))
                {
                    ActivateMappyBrush(terrain, MBBrushMode.SplatMap);
                }

                if (GUILayout.Button(new GUIContent(
                        "Sculpt",
                        "Activate mesh sculpting in the MashBox Mappy panel for this terrain.")))
                {
                    ActivateMappy(terrain, MBEditorAuthoringMode.MeshSculpt);
                }

                if (GUILayout.Button(new GUIContent(
                        "Decor",
                        "Activate the MashBox Mappy panel's Decor brush for instanced details and trees.")))
                {
                    ActivateMappyBrush(terrain, MBBrushMode.Decor);
                }
            }

            if (GUILayout.Button(new GUIContent(
                    "Vertex Paint",
                    "Activate vertex-color painting in the MashBox Mappy panel for this terrain mesh.")))
            {
                ActivateMappyBrush(terrain, MBBrushMode.Painter);
            }

            EditorGUILayout.Space(4f);
        }

        static void ActivateMappyBrush(MGTerrain terrain, MBBrushMode brushMode)
        {
            MBEditorToolState.BrushMode = brushMode;
            ActivateMappy(terrain, MBEditorAuthoringMode.Brush);
        }

        static void ActivateMappy(MGTerrain terrain, MBEditorAuthoringMode mode)
        {
            if (terrain == null)
                return;

            // The Scene overlay and its headless tool host are driven entirely
            // by shared state. Do not create/focus MashBoxMapToolsWindow here;
            // these component shortcuts should stay inside the Mappy panel.
            if (Selection.activeGameObject != terrain.gameObject)
                Selection.activeGameObject = terrain.gameObject;
            MBEditorToolState.RequestMode(mode);
            MBEditorToolState.ActiveEditing = true;
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        [MenuItem("GameObject/MashBox/Terrain/Convert Selected Unity Terrain to MG Terrain...", false, 10)]
        static void ConvertSelectedTerrain()
        {
            Terrain source = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<Terrain>()
                : null;
            if (source == null || source.terrainData == null)
                return;

            string absoluteFolder = EditorUtility.SaveFolderPanel(
                "Choose MG Terrain Asset Folder",
                Application.dataPath,
                source.name + "_MGTerrain");
            if (string.IsNullOrEmpty(absoluteFolder))
                return;
            string assetFolder = TerrainToMeshConverter.ToProjectAssetPath(absoluteFolder);
            if (string.IsNullOrEmpty(assetFolder))
            {
                EditorUtility.DisplayDialog("MG Terrain", "Choose a folder inside this project's Assets folder.", "OK");
                return;
            }

            var options = new TerrainConversionOptions
            {
                ConvertMesh = true,
                AddMeshCollider = true,
                ExportSplatMaps = true,
                ConvertTrees = true,
                ConvertDetails = true,
                DisableSourceTerrain = true,
                MaximumMeshResolution = 513
            };
            TerrainConversionSummary summary = TerrainToMeshConverter.Analyze(source);
            if (summary.TreeCount > TerrainToMeshConverter.MaxTreeGameObjects)
            {
                EditorUtility.DisplayDialog(
                    "MG Terrain Instance Limit",
                    $"This Terrain has {summary.TreeCount:N0} trees. Tree conversion is limited to {TerrainToMeshConverter.MaxTreeGameObjects:N0} serialized instances. Dense details do not count against this limit because they use density maps.",
                    "OK");
                return;
            }
            if (!TerrainToMeshConverter.ConfirmLargeGameObjectConversions(summary, options))
                return;

            try
            {
                GameObject result = TerrainToMeshConverter.Convert(source, assetFolder, options);
                Selection.activeGameObject = result;
                EditorGUIUtility.PingObject(result);
                EditorSceneManager.MarkSceneDirty(result.scene);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, source);
                EditorUtility.DisplayDialog("MG Terrain Conversion Failed", exception.Message, "OK");
            }
        }

        [MenuItem("GameObject/MashBox/Terrain/Convert Selected Unity Terrain to MG Terrain...", true)]
        static bool ValidateConvertSelectedTerrain()
        {
            return Selection.activeGameObject != null
                && Selection.activeGameObject.GetComponent<Terrain>() != null;
        }
    }
}

#endif
