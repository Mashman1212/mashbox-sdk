#if UNITY_EDITOR
using System;
using System.Linq;
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainWorldEditor
    {
        bool m_PaintWorld = true;
        void DrawWorldTools(MGTerrainWorld world)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("World Detail Quality", EditorStyles.boldLabel);
            var maps = new HashSet<Texture2D>();
            foreach (var tile in world.Chunks)
                foreach (var layer in tile.DensityDetailLayers)
                {
                    if (layer == null) continue;
                    if (layer.DensityMap != null) maps.Add(layer.DensityMap);
                    if (layer.SizeMap != null) maps.Add(layer.SizeMap);
                }
            long bytes = maps.Sum(map => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(map));
            EditorGUILayout.LabelField("Unique Density / Size Maps", maps.Count.ToString());
            EditorGUILayout.LabelField("Density / Size Texture Memory", EditorUtility.FormatBytes(bytes));
            using (new EditorGUILayout.HorizontalScope())
                foreach (var preset in new[] { MGTerrain.DetailQualityPreset.Low, MGTerrain.DetailQualityPreset.Medium, MGTerrain.DetailQualityPreset.High, MGTerrain.DetailQualityPreset.Ultra })
                    if (GUILayout.Button(preset.ToString()))
                    {
                        Undo.RecordObject(world, "World Detail Quality");
                        Undo.RecordObjects(world.Chunks.Cast<UnityEngine.Object>().ToArray(), "World Detail Quality");
                        world.ApplyQualityPreset(preset);
                        EditorUtility.SetDirty(world);
                    }
            m_PaintWorld = EditorGUILayout.Foldout(m_PaintWorld, "Paint World Density / Detail Prototypes", true);
            if (m_EditChunk == null || m_EditChunk.World != world) m_EditChunk = world.Chunks.FirstOrDefault();
            if (m_PaintWorld && m_EditChunk != null)
            {
                var next = (MGTerrain)EditorGUILayout.ObjectField("Detail Definitions From", m_EditChunk, typeof(MGTerrain), true);
                if (next != null && next.World == world && next != m_EditChunk)
                {
                    if (m_ChunkEditor != null) DestroyImmediate(m_ChunkEditor);
                    m_EditChunk = next;
                }
                EditorGUILayout.HelpBox("Choose a detail below and paint in the Scene view with the world selected. Matching layers on intersected tiles receive the stroke. Tiles without a unique matching prototype are skipped with a warning. Each tile retains its own painted distribution.", MessageType.None);
                CreateCachedEditor(m_EditChunk, typeof(MGTerrainEditor), ref m_ChunkEditor);
                ((MGTerrainEditor)m_ChunkEditor).DrawWorldDetailControls();
            }
            DrawWorldBakes(world);
        }

        void DrawWorldBakes(MGTerrainWorld world)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bake All World Tiles", EditorStyles.boldLabel);
            serializedObject.Update();
            var resolution = serializedObject.FindProperty("CaptureResolution");
            resolution.intValue = EditorGUILayout.IntPopup("Capture Resolution Per Tile", resolution.intValue, new[] { "2K", "4K" }, new[] { 2048, 4096 });
            EditorGUILayout.PropertyField(serializedObject.FindProperty("CaptureExposure"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("CaptureDetailTilt"));
            var spacing = serializedObject.FindProperty("DistantMeshSpacing");
            spacing.floatValue = Mathf.Max(.25f, EditorGUILayout.FloatField("Distant Mesh Spacing (m)", spacing.floatValue));
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("Shared settings, separate outputs per tile. Appearance captures include normal maps. Distant surface captures also generate height maps and meshes. Uses current scene sunlight with fixed exposure.", MessageType.None);
            using (new EditorGUI.DisabledScope(Application.isPlaying || world.Chunks.Count == 0))
            {
                if (GUILayout.Button("Bake All Top-Down Terrain Appearance Maps")) BakeWorld(world, false);
                if (GUILayout.Button("Bake All Distant Surfaces + Height Maps")) BakeWorld(world, true);
            }
        }

        static void BakeWorld(MGTerrainWorld world, bool distant)
        {
            string folder = TerrainToMeshConverter.ToProjectAssetPath(EditorUtility.OpenFolderPanel("World Bake Output Folder", Application.dataPath, ""));
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder)) return;
            var chunks = world.Chunks.Where(t => t != null && t.isActiveAndEnabled && t.MeshRenderer != null).ToArray();
            // A per-tile capture cannot safely assign its result to a shared material.
            var shared = chunks.GroupBy(t => t.MeshRenderer.sharedMaterial).Where(g => g.Key != null && g.Count() > 1).Select(g => g.Key).ToHashSet();
            try
            {
                for (int i = 0; i < chunks.Length; i++)
                {
                    var tile = chunks[i];
                    if (EditorUtility.DisplayCancelableProgressBar("World Terrain Bake", tile.name, i / (float)chunks.Length)) break;
                    if (shared.Contains(tile.MeshRenderer.sharedMaterial))
                    {
                        var material = new Material(tile.MeshRenderer.sharedMaterial);
                        string materialPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/Tile_" + i + "_Capture.mat");
                        AssetDatabase.CreateAsset(material, materialPath);
                        Undo.RecordObject(tile.MeshRenderer, "Tile Capture Material");
                        tile.MeshRenderer.sharedMaterial = material;
                    }
                    var editor = (MGTerrainEditor)CreateEditor(tile);
                    try
                    {
                        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/Tile_" + i + (distant ? "_Distant.png" : "_Appearance.png"));
                        editor.BakeWorldTile(world, distant, path);
                    }
                    finally { DestroyImmediate(editor); }
                }
                AssetDatabase.SaveAssets();
            }
            catch (Exception error) { Debug.LogException(error, world); }
            finally { EditorUtility.ClearProgressBar(); }
        }
    }

    public sealed partial class MGTerrainEditor
    {
        internal void DrawWorldDetailControls()
        {
            serializedObject.Update();
            var terrain = (MGTerrain)target;
            DrawDetailFoliagePalettes(terrain);
            DrawDetailLayerVisibility(terrain);
            DrawPrototypeGrid(terrain);
            DrawDetailPainter(terrain);
            serializedObject.ApplyModifiedProperties();
        }
        internal void BakeWorldTile(MGTerrainWorld world, bool distant, string path)
        {
            m_FarBakeResolution = world.CaptureResolution;
            m_CaptureExposure = world.CaptureExposure;
            m_CaptureDetailTilt = world.CaptureDetailTilt;
            m_CaptureSyncTimeOfDay = true;
            m_CaptureNormalMap = true;
            m_DistantSpacing = world.DistantMeshSpacing;
            CaptureTerrainAppearance((MGTerrain)target, distant, path);
        }
    }
}
#endif


