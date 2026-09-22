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
                EditorGUILayout.HelpBox("Paint and edit shared details across the entire world. Visibility applies everywhere; painted density and size remain local to each area.", MessageType.None);
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
            var syncExposure = serializedObject.FindProperty("CaptureSyncSceneExposure");
            EditorGUILayout.PropertyField(syncExposure, new GUIContent("Sync Exposure to Scene"));
            using (new EditorGUI.DisabledScope(syncExposure.boolValue))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("CaptureExposure"));
            if (syncExposure.boolValue) EditorGUILayout.HelpBox(MGTerrainSceneExposure.Description(), MessageType.None);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("CaptureDetailTilt"));
            var spacing = serializedObject.FindProperty("DistantMeshSpacing");
            spacing.floatValue = Mathf.Max(.25f, EditorGUILayout.FloatField("Distant Height Sampling Spacing (m)", spacing.floatValue));
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("Shared settings, separate outputs per tile. Appearance captures include normal maps. Distant captures generate height maps for shader morphing; no separate distant meshes are generated. Uses current scene sunlight with fixed exposure.", MessageType.None);
            using (new EditorGUI.DisabledScope(Application.isPlaying || world.Chunks.Count == 0))
            {
                if (GUILayout.Button("Bake All Maps (Appearance, then Distant Morph Maps)"))
                    if (BakeWorld(world, false)) BakeWorld(world, true);
                if (GUILayout.Button("Clean Terrain Data...")) MGTerrainDataCleanup.Show(world);
            }
        }

        internal static bool BakeWorld(MGTerrainWorld world, bool distant)
        {

            var chunks = world.Chunks.Where(t => t != null && t.isActiveAndEnabled && t.MeshRenderer != null).ToArray();
            try
            {
                for (int i = 0; i < chunks.Length; i++)
                {
                    var tile = chunks[i];
                    if (EditorUtility.DisplayCancelableProgressBar("World Terrain Bake", (distant ? "Distant: " : "Appearance: ") + tile.name, i / (float)chunks.Length)) return false;
                    var editor = (MGTerrainEditor)CreateEditor(tile);
                    try
                    {
                        string path = MGTerrainAssetStore.MapPath(tile, distant ? "Distant" : "Appearance");
                        if (!editor.BakeWorldTile(world, distant, path)) return false;
                    }
                    finally { DestroyImmediate(editor); }
                }
                AssetDatabase.SaveAssets();
                return true;
            }
            catch (Exception error) { Debug.LogException(error, world); return false; }
            finally { EditorUtility.ClearProgressBar(); }
        }
    }

    public sealed partial class MGTerrainEditor
    {
        internal void DrawWorldDetailControls()
        {
            var terrain = (MGTerrain)target;
            PrepareSharedWorldDetails(terrain);
            serializedObject.Update();
            m_WorldDetailControls = true;
            try
            {
                DrawDetailFoliagePalettes(terrain);
                DrawDetailLayerVisibility(terrain);
                DrawPaintModeToggle(terrain);
                DrawDetailPainter(terrain);
                DrawPrototypeGrid(terrain);
                DrawWorldDetailMapBrowser(terrain);
                DrawLoftDetailOperations(terrain);
            }
            finally
            {
                serializedObject.ApplyModifiedProperties();
                CommitSharedWorldDetails(terrain);
                m_WorldDetailControls = false;
            }
        }
        int m_MapBrowserPage, m_MapBrowserPrototype = -1;
        sealed class DetailMapUsage
        {
            internal Texture2D Map;
            internal readonly HashSet<string> Roles = new HashSet<string>();
            internal readonly HashSet<int> Layers = new HashSet<int>();
            internal readonly HashSet<MGTerrain> Areas = new HashSet<MGTerrain>();
        }

        void DrawWorldDetailMapBrowser(MGTerrain source)
        {
            if (source.World == null) return;
            var layers = Enumerable.Range(0, source.DensityDetailLayerCount)
                .Where(i => source.DensityDetailLayers[i] != null
                    && source.DensityDetailLayers[i].PrototypeIndex == m_SelectedPrototype
                    && !source.DensityDetailLayers[i].PaletteSourceOnly).ToArray();
            if (layers.Length == 0) return;
            if (m_MapBrowserPrototype != m_SelectedPrototype)
            { m_MapBrowserPrototype = m_SelectedPrototype; m_MapBrowserPage = 0; }
            var maps = new Dictionary<Texture2D, DetailMapUsage>();
            var roles = new HashSet<string>();
            void Add(Texture2D map, string role, int layer, MGTerrain area)
            {
                if (map == null) return;
                if (!maps.TryGetValue(map, out var usage))
                    maps.Add(map, usage = new DetailMapUsage { Map = map });
                usage.Roles.Add(role); usage.Layers.Add(layer + 1); usage.Areas.Add(area); roles.Add(role);
            }
            foreach (var area in SharedTiles(source))
                foreach (int index in layers)
                {
                    int match = FindWorldPaintLayer(source, index, area);
                    if (match < 0) continue;
                    var layer = area.DensityDetailLayers[match];
                    Add(layer.DensityMap, "Density", index, area);
                    Add(layer.SizeMap, "Size", index, area);
                    Add(layer.GrassIdMap, "Grass IDs", index, area);
                    Add(layer.PaletteSourceMap, "Palette Source", index, area);
                }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Generated Maps", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"{maps.Count} unique assets for this detail across the world.", EditorStyles.miniLabel);
            if (!roles.Contains("Density")) EditorGUILayout.LabelField("Density: created when painted.", EditorStyles.miniLabel);
            if (!roles.Contains("Size")) EditorGUILayout.LabelField("Size: no map yet; default multiplier is 1.", EditorStyles.miniLabel);
            if (!roles.Contains("Grass IDs")) EditorGUILayout.LabelField("Grass IDs: no per-pixel ID maps.", EditorStyles.miniLabel);
            const int pageSize = 8;
            int pages = Mathf.Max(1, (maps.Count + pageSize - 1) / pageSize);
            m_MapBrowserPage = Mathf.Clamp(m_MapBrowserPage, 0, pages - 1);
            if (pages > 1)
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(m_MapBrowserPage == 0))
                        if (GUILayout.Button("Previous")) m_MapBrowserPage--;
                    GUILayout.Label($"Page {m_MapBrowserPage + 1} of {pages}");
                    using (new EditorGUI.DisabledScope(m_MapBrowserPage == pages - 1))
                        if (GUILayout.Button("Next")) m_MapBrowserPage++;
                }
            foreach (var usage in maps.Values.OrderBy(u => u.Map.name).Skip(m_MapBrowserPage * pageSize).Take(pageSize))
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(string.Join(" / ", usage.Roles.OrderBy(r => r)), EditorStyles.boldLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        // Asset references remain clickable, but assignments are managed by world painting.
                        EditorGUILayout.ObjectField(usage.Map, typeof(Texture2D), false);
                        if (GUILayout.Button("Ping", GUILayout.Width(45))) EditorGUIUtility.PingObject(usage.Map);
                    }
                    EditorGUILayout.LabelField($"{usage.Map.width} x {usage.Map.height} / {usage.Map.format}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField("Layers " + string.Join(", ", usage.Layers.OrderBy(i => i))
                        + $" / {usage.Areas.Count} area(s)" + (usage.Layers.Count > 1 ? " / Shared asset" : ""), EditorStyles.miniLabel);
                }
        }

        internal bool BakeWorldTile(MGTerrainWorld world, bool distant, string path)
        {
            m_FarBakeResolution = world.CaptureResolution;
            m_CaptureExposure = world.CaptureExposure;
            if (world.CaptureSyncSceneExposure && !MGTerrainSceneExposure.TryGet(out m_CaptureExposure, out string exposureError))
                throw new InvalidOperationException(exposureError);
            m_CaptureDetailTilt = world.CaptureDetailTilt;
            m_CaptureSyncTimeOfDay = true;
            m_CaptureNormalMap = true;
            m_DistantSpacing = world.DistantMeshSpacing;
            CaptureTerrainAppearance((MGTerrain)target, distant, path);
            return m_LastCaptureSucceeded;
        }
    }
}
#endif


