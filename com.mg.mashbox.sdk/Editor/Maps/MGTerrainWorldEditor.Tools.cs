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
        void DrawWorldQualityPresets(MGTerrainWorld world)
        {
            EditorGUILayout.Space();
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
                        serializedObject.Update();
                    }
        }

        void DrawWorldTools(MGTerrainWorld world)
        {
            m_PaintWorld = true;
            EditorGUILayout.LabelField("Paint Details", EditorStyles.boldLabel);
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
                        string materialPath = MGTerrainSceneAssets.UniquePath(tile, "Capture", ".mat");
                        AssetDatabase.CreateAsset(material, materialPath);
                        Undo.RecordObject(tile.MeshRenderer, "Tile Capture Material");
                        tile.MeshRenderer.sharedMaterial = material;
                    }
                    var editor = (MGTerrainEditor)CreateEditor(tile);
                    try
                    {
                        string path = MGTerrainSceneAssets.UniquePath(tile, distant ? "Distant" : "Appearance", ".png");
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
            m_WorldDetailControls = true;
            try { DrawPrototypeGrid(terrain); }
            finally { m_WorldDetailControls = false; }
            DrawWorldDetailMapBrowser(terrain);
            DrawDetailPainter(terrain);
            serializedObject.ApplyModifiedProperties();
        }
        int m_MapBrowserScope, m_MapBrowserLayer, m_MapBrowserPage;
        string m_MapBrowserFilter = "";
        void DrawWorldDetailMapBrowser(MGTerrain source)
        {
            if (source.World == null) return;
            var layers = Enumerable.Range(0, source.DensityDetailLayerCount)
                .Where(i => source.DensityDetailLayers[i] != null
                    && source.DensityDetailLayers[i].PrototypeIndex == m_SelectedPrototype
                    && !source.DensityDetailLayers[i].PaletteSourceOnly).ToArray();
            if (layers.Length == 0) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Selected Detail Maps", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("The editable map fields above belong to the reference tile: " + source.name
                + ". Each matching tile has its own map assignments. This browser inspects them without changing assignments.", MessageType.None);
            m_MapBrowserLayer = Mathf.Clamp(m_MapBrowserLayer, 0, layers.Length - 1);
            if (layers.Length > 1)
                m_MapBrowserLayer = EditorGUILayout.Popup("Detail Layer", m_MapBrowserLayer,
                    layers.Select(i => "Layer " + (i + 1) + " / Population " + source.DensityDetailLayers[i].GrassPopulation).ToArray());
            int layerIndex = layers[m_MapBrowserLayer];
            int scope = GUILayout.Toolbar(m_MapBrowserScope, new[] { "Reference Tile", "All Tiles" });
            if (scope != m_MapBrowserScope) { m_MapBrowserScope = scope; m_MapBrowserPage = 0; }
            var tiles = source.World.GetComponentsInChildren<MGTerrain>(true)
                .Where(t => t.GetComponentInParent<MGTerrainWorld>(true) == source.World).ToArray();
            if (scope == 0) tiles = new[] { source };
            else
            {
                string filter = EditorGUILayout.TextField("Find Tile", m_MapBrowserFilter);
                if (filter != m_MapBrowserFilter) { m_MapBrowserFilter = filter; m_MapBrowserPage = 0; }
                if (!string.IsNullOrWhiteSpace(filter))
                    tiles = tiles.Where(t => t.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            }
            const int pageSize = 8;
            int pages = Mathf.Max(1, (tiles.Length + pageSize - 1) / pageSize);
            m_MapBrowserPage = Mathf.Clamp(m_MapBrowserPage, 0, pages - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(m_MapBrowserPage == 0))
                    if (GUILayout.Button("Previous", GUILayout.Width(70))) m_MapBrowserPage--;
                GUILayout.Label($"{tiles.Length} tiles / Page {m_MapBrowserPage + 1} of {pages}");
                using (new EditorGUI.DisabledScope(m_MapBrowserPage + 1 == pages))
                    if (GUILayout.Button("Next", GUILayout.Width(55))) m_MapBrowserPage++;
            }
            foreach (var tile in tiles.Skip(m_MapBrowserPage * pageSize).Take(pageSize))
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    // Return values deliberately ignored: references are inspectable/pingable, not editable here.
                    EditorGUILayout.ObjectField("Tile", tile, typeof(MGTerrain), true);
                    int match = FindWorldFloodLayer(source, layerIndex, tile);
                    if (match < 0)
                    {
                        EditorGUILayout.HelpBox("No unique matching detail layer. Painting cannot resolve this tile for the selected layer.", MessageType.Warning);
                        continue;
                    }
                    var layer = tile.DensityDetailLayers[match];
                    DrawMapReference("Density", layer.DensityMap, "No density map assigned");
                    DrawMapReference("Size", layer.SizeMap, "No size overrides (multiplier 1)");
                    DrawMapReference("Grass IDs", layer.GrassIdMap, "No per-texel grass-ID map");
                    if (!tile.isActiveAndEnabled) EditorGUILayout.LabelField("Tile is inactive", EditorStyles.miniLabel);
                }
        }

        static void DrawMapReference(string label, Texture2D map, string empty)
        {
            EditorGUILayout.ObjectField(label, map, typeof(Texture2D), false);
            EditorGUILayout.LabelField(map != null
                ? $"{map.width} × {map.height} / {map.format} / {EditorUtility.FormatBytes(UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(map))}"
                : empty, EditorStyles.miniLabel);
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


