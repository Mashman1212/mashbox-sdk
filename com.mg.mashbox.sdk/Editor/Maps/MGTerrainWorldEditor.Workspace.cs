#if UNITY_EDITOR
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainWorldEditor
    {
        bool m_Workspace;
        string m_TileSearch = "";
        bool m_ShowImport;
        bool m_ShowEstimates;
        bool m_ShowCreationDensity;

        internal void SetWorkspacePage(int page)
        {
            m_Workspace = true;
            m_Tab = (WorldTab)Mathf.Clamp(page, 0, 5);
            m_AddTiles = false;
            m_ShowChunk = false;
            ClearPlacementPreviews();
            if (m_ChunkEditor != null) { DestroyImmediate(m_ChunkEditor); m_ChunkEditor = null; }
            SceneView.RepaintAll();
        }

        internal void DrawWorkspace()
        {
#if !UNITY_6000_0_OR_NEWER
            EditorGUILayout.HelpBox("MG Terrain World requires Unity 6 or newer.", MessageType.Warning);
#else
            var world = (MGTerrainWorld)target;
            serializedObject.Update();
            switch (m_Tab)
            {
                case WorldTab.Tiles: DrawTilesTab(world); break;
                case WorldTab.Details:
                    if (world.Chunks.Count == 0)
                        EditorGUILayout.HelpBox("Create or adopt your first terrain tile on the Tiles page, then return here to paint details.", MessageType.Info);
                    else DrawWorldTools(world);
                    break;
                case WorldTab.Settings: DrawRenderingPage(world); break;
                case WorldTab.Baking: DrawWorldBakes(world); break;
                case WorldTab.Collision: DrawWorldCollision(world); break;
                case WorldTab.Diagnostics:
                    MGTerrainGizmos.DrawSettings(false);
                    MGTerrainDiagnosticTable.Header("Rendering counters", "Last prepared camera");
                    MGTerrainDiagnosticTable.Metric(1, "Registered tiles", world.Chunks.Count.ToString("N0"));
                    MGTerrainDiagnosticTable.Metric(2, "Shared renderers", world.SharedRendererCount.ToString("N0"));
                    MGTerrainDiagnosticTable.Metric(3, "Submitted details", world.LastSubmittedDetailInstances.ToString("N0"));
                    MGTerrainDiagnosticTable.Metric(4, "Enabled surface renderers", world.Chunks.Sum(c => c != null ? c.ActiveSurfaceRendererCount : 0).ToString("N0"));
                    EditorGUILayout.HelpBox("Counts reflect the last prepared camera before frustum and occlusion culling. Game cameras choose LOD independently.", MessageType.None);
                    DrawSurfaceLodPreview(world);
                    DrawWorldDataEstimator(world);
                    break;
            }
#endif
        }

        void DrawRenderingPage(MGTerrainWorld world)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Surface geometry", EditorStyles.boldLabel);
                Property("m_SurfaceChunkDistance", "Render chunk distance (m)");
                Property("m_SurfaceShaderDistance", "Tessellation distance (m)");
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField("Far shader", MGTerrain.SurfaceFarShader, typeof(Shader), false);
                EditorGUILayout.LabelField("Shader chunks (near / far)", $"{world.Chunks.Sum(c => c != null ? c.NearSurfaceShaderChunks : 0)} / {world.Chunks.Sum(c => c != null ? c.FarSurfaceShaderChunks : 0)}");
                EditorGUILayout.HelpBox("0 = no tessellation. Positive distances keep nearby chunks tessellated. POM remains enabled. Counts reflect the last prepared camera, before frustum culling; Scene and Game cameras select independently. Terrain components must be enabled.", MessageType.Info);
                EditorGUILayout.LabelField("Baked tiles", $"{world.Chunks.Count(c => c != null && c.BakedSurfaceLod != null)} / {world.Chunks.Count}");
                if (MGTerrainSurfaceLodBaker.IsBaking)
                {
                    if (GUILayout.Button("Cancel HLOD bake")) MGTerrainSurfaceLodBaker.Cancel();
                }
                else if (GUILayout.Button("Bake Surface HLOD")) MGTerrainSurfaceLodBaker.Bake(world);
                EditorGUILayout.HelpBox("Bake after editing terrain geometry. Distant render chunks combine into one reduced mesh per terrain tile. Scene and Game cameras only select saved meshes; missing or stale bakes retain the original. See Diagnostics for each tile's selection reason.", MessageType.Info);
                Property("m_SurfaceLodEnabled", "Enable baked surface HLOD");
                using (new EditorGUI.DisabledScope(!serializedObject.FindProperty("m_SurfaceLodEnabled").boolValue))
                {
                    Property("m_SurfaceLodMinDistance", "LOD start distance (m)");
                    Property("m_SurfaceLodPixelError", "Allowed pixel error");
                }
            }
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Foliage visibility", EditorStyles.boldLabel);
                Property("m_DetailDistance", "Maximum detail distance (m)");
                Property("m_UnloadMargin", "Unload margin (m)");
                Property("m_MaxVisibleDetailInstances", "Maximum detail instances");
                EditorGUILayout.LabelField("Zero is unlimited. Shared across tiles, before GPU culling.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField($"Selected / requested: {world.LastSubmittedDetailInstances:N0} / {world.LastRequestedDetailInstances:N0}");
                EditorGUILayout.LabelField("A distance of zero uses each prototype's distance.", EditorStyles.wordWrappedMiniLabel);
            }
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Streaming budget", EditorStyles.boldLabel);
                Property("m_DetailCellsBuiltPerFrame", "Cell builds per frame");
                Property("m_DetailMeshUploadsPerFrame", "Mesh uploads per frame");
                Property("m_MaxPendingDetailBuilds", "Pending builds");
                Property("m_CachedDetailCells", "Cached detail cells");
                Property("m_PreloadAllDetailCells", "Preload all detail cells");
                if (serializedObject.FindProperty("m_PreloadAllDetailCells").boolValue)
                    EditorGUILayout.HelpBox("Preloading prepares the entire world's GPU detail cells before gameplay. Large worlds can take longer to start and use more GPU memory.", MessageType.Warning);
            }
            if (serializedObject.ApplyModifiedProperties())
            {
                world.ApplySharedQuality();
                SceneView.RepaintAll();
            }
            DrawWorldQuality(world);
            DrawWorldFarRangeMaterials(world);
        }

        void Property(string name, string label) => EditorGUILayout.PropertyField(serializedObject.FindProperty(name), new GUIContent(label), true);
    }
}
#endif
