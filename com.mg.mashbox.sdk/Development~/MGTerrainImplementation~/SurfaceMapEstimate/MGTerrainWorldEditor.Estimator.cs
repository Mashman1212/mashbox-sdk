#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainWorldEditor
    {
        bool m_ShowDataEstimator = true, m_ShowEstimatorMaps;
        float m_EstimateKilometres = 10;
        int m_EstimateTileCount, m_EstimateMapCount, m_EstimateUniqueCount;
        double m_EstimateGpuPerTile, m_EstimateDiskPerTile, m_EstimateMeshPerTile;
        long m_EstimateCurrentMemory, m_EstimateCurrentDisk;
        bool m_EstimateScanned, m_EstimateHasUnsavedMaps;
        readonly Dictionary<string, int> m_EstimateMapGroups = new Dictionary<string, int>();

        static readonly string[] SurfaceMapProperties = { "_ControlMap1", "_ControlMap2", "_FarRangeAppearanceMap", "_FarRangeAppearanceNormalMap", "_DistantSurfaceHeightMap" };
        static readonly string[] SurfaceMapLabels = { "Control Map 1", "Control Map 2", "Appearance RGB", "Appearance Normal (WS)", "Height" };
        readonly int[] m_SurfaceAssigned = new int[5];
        readonly double[] m_SurfaceBytes = new double[5];
        readonly bool[] m_PlanSurfaceMap = new bool[5];
        readonly int[] m_PlannedSurfaceResolution = { 512, 512, 2048, 2048, 2048 };
        double m_EstimateDetailGpuPerTile;
        int m_EstimateDetailSlots;

        double DrawSurfaceMapEstimate(long projectedTiles)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("MG Lit Trail Surface Maps", EditorStyles.boldLabel);
            double plannedPerTile = 0;
            for (int i = 0; i < SurfaceMapLabels.Length; i++)
            {
                double divisor = Math.Max(1, m_EstimateTileCount);
                EditorGUILayout.LabelField(SurfaceMapLabels[i],
                    $"{m_SurfaceAssigned[i]} / {m_EstimateTileCount} tiles · {EstimateBytes(m_SurfaceBytes[i] / divisor * projectedTiles)}");
                m_PlanSurfaceMap[i] = EditorGUILayout.ToggleLeft("Include missing " + SurfaceMapLabels[i], m_PlanSurfaceMap[i]);
                if (!m_PlanSurfaceMap[i]) continue;
                m_PlannedSurfaceResolution[i] = EditorGUILayout.IntPopup("Planned Resolution", m_PlannedSurfaceResolution[i],
                    new[] { "256", "512", "1K", "2K", "4K", "8K" }, new[] { 256, 512, 1024, 2048, 4096, 8192 });
                double missingFraction = m_EstimateTileCount > 0 ? 1 - m_SurfaceAssigned[i] / divisor : 1;
                long pixels = 0;
                for (int n = m_PlannedSurfaceResolution[i]; n > 0; n /= 2) pixels += (long)n * n;
                // Planning is explicit and conservative; assigned maps use their real formats.
                plannedPerTile += missingFraction * pixels * (i == 4 ? 2 : 4);
            }
            EditorGUILayout.HelpBox("Assigned maps use their actual resolutions, formats and mip levels. Missing-map planning assumes RGBA32 for controls/RGB/world normals and R16 for height, with a full mip chain. These are estimates only; no textures are created. Planned file sizes are unknown until saved.", MessageType.None);
            return plannedPerTile;
        }

        void DrawWorldDataEstimator(MGTerrainWorld world)
        {
            EditorGUILayout.Space();
            m_ShowDataEstimator = EditorGUILayout.Foldout(m_ShowDataEstimator, "World Data Estimator", true);
            if (!m_ShowDataEstimator) return;
            if (!m_EstimateScanned) RefreshDataEstimate(world);
            using (new EditorGUILayout.HorizontalScope())
                foreach (int km in new[] { 1, 2, 5, 10, 25, 50, 100 })
                    if (GUILayout.Toggle(Mathf.Approximately(m_EstimateKilometres, km), km + " km", EditorStyles.miniButton))
                        m_EstimateKilometres = km;
            m_EstimateKilometres = Mathf.Clamp(EditorGUILayout.FloatField("Square World Side (km)", m_EstimateKilometres), .1f, 100f);
            if (float.IsNaN(m_EstimateKilometres)) m_EstimateKilometres = 10;
            if (GUILayout.Button("Refresh From Current Tiles / Maps")) RefreshDataEstimate(world);
            double tileSize = Math.Max(1, world.TileSize);
            long side = (long)Math.Ceiling(m_EstimateKilometres * 1000.0 / tileSize);
            long total = side * side;
            EditorGUILayout.LabelField("Tile Grid", $"{side:N0} × {side:N0} = {total:N0} tiles");
            EditorGUILayout.LabelField("Rounded World Coverage", $"{side * tileSize / 1000:0.###} × {side * tileSize / 1000:0.###} km");
            EditorGUILayout.LabelField("Sampled Tiles", m_EstimateTileCount.ToString("N0"));
            EditorGUILayout.LabelField("Current Map Slots / Unique Maps", $"{m_EstimateMapCount:N0} / {m_EstimateUniqueCount:N0}");
            EditorGUILayout.LabelField("Current Unique Map Memory", EstimateBytes(m_EstimateCurrentMemory));
            EditorGUILayout.LabelField("Current Map Asset Files", EstimateBytes(m_EstimateCurrentDisk));
            if (m_EstimateTileCount == 0)
                EditorGUILayout.HelpBox("No tiles are available to sample. Mesh creation settings and any enabled surface-map plans are estimated.", MessageType.Info);
            double mapsPerTile = m_EstimateTileCount > 0 ? m_EstimateDetailSlots / (double)m_EstimateTileCount : 0;
            double plannedSurface = DrawSurfaceMapEstimate(total);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Projected Full World", EditorStyles.boldLabel);
            double mesh = m_EstimateTileCount > 0 ? m_EstimateMeshPerTile : CreationMeshBytes(world);
            EditorGUILayout.LabelField("Average Detail Texture / Tile", EstimateBytes(m_EstimateDetailGpuPerTile));
            EditorGUILayout.LabelField("Detail Map Slots", $"≈ {Math.Ceiling(mapsPerTile * total):N0}");
            EditorGUILayout.LabelField("Detail Texture Buffers", EstimateBytes(m_EstimateDetailGpuPerTile * total));
            EditorGUILayout.LabelField("Assigned Surface Map Buffers", EstimateBytes((m_EstimateGpuPerTile - m_EstimateDetailGpuPerTile) * total));
            EditorGUILayout.LabelField("Planned Missing Surface Maps", EstimateBytes(plannedSurface * total));
            EditorGUILayout.LabelField("Map Files on Disk (maps only)", "≈ " + EstimateBytes(m_EstimateDiskPerTile * total));
            EditorGUILayout.LabelField("Surface Mesh Buffers", EstimateBytes(mesh * total));
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Total Mesh + Texture Buffers", "≈ " + EstimateBytes((mesh + m_EstimateGpuPerTile + plannedSurface) * total), EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Sum of detail maps, assigned/planned surface maps and mesh buffers. Excludes CPU copies, other assets and engine overhead.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("Disk files are a separate measurement, not added to this total. Saved texture assets can be larger because of serialization. Complete world disk size is not estimated here.", EditorStyles.wordWrappedMiniLabel);
            }
            m_ShowEstimatorMaps = EditorGUILayout.Foldout(m_ShowEstimatorMaps, "Map Resolutions / Formats", true);
            if (m_ShowEstimatorMaps)
                foreach (var group in m_EstimateMapGroups.OrderBy(p => p.Key))
                    EditorGUILayout.LabelField(group.Key, group.Value + " slots", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.HelpBox("Projection repeats the average current tile configuration across a square world using the configured tile size. It assumes independent maps per tile, but preserves sharing within a tile. Empty tiles reduce the average. Refresh after painting or changing maps. No assets are generated.", MessageType.None);
            EditorGUILayout.HelpBox("Payload uses mesh buffers and the current texture formats, including mip levels—not build download size or a frame-rate prediction. Asset-file estimates use current saved files. CPU-readable copies, detail instances, colliders, other material textures, shared grass arrays and engine overhead are excluded from projected totals. Streaming can reduce loaded memory; it does not reduce the full world's stored data.", MessageType.None);
            if (m_EstimateHasUnsavedMaps)
                EditorGUILayout.HelpBox("Some maps are unsaved or have no accessible asset file. The disk estimate is incomplete until those maps are saved and refreshed.", MessageType.Warning);
        }

        static string EstimateBytes(double bytes) => EditorUtility.FormatBytes((long)Math.Min(long.MaxValue, Math.Max(0, bytes)));
        static double CreationMeshBytes(MGTerrainWorld world)
        {
            double points = Math.Max(2, world.TileResolution), vertices = points * points;
            return vertices * 48 + 6 * (points - 1) * (points - 1) * (vertices > 65535 ? 4 : 2);
        }
        static long TexturePayload(Texture2D map)
        {
            var format = map.graphicsFormat;
            long block = GraphicsFormatUtility.GetBlockSize(format);
            long bw = Math.Max(1, GraphicsFormatUtility.GetBlockWidth(format));
            long bh = Math.Max(1, GraphicsFormatUtility.GetBlockHeight(format));
            long bytes = 0;
            for (int mip = 0; mip < map.mipmapCount; mip++)
                bytes += ((Math.Max(1, map.width >> mip) + bw - 1) / bw)
                    * ((Math.Max(1, map.height >> mip) + bh - 1) / bh) * block;
            return bytes;
        }
        void RefreshDataEstimate(MGTerrainWorld world)
        {
            var tiles = OwnedTiles(world);
            m_EstimateTileCount = tiles.Length;
            m_EstimateMapCount = m_EstimateUniqueCount = 0;
            m_EstimateGpuPerTile = m_EstimateDiskPerTile = m_EstimateMeshPerTile = 0;
            m_EstimateCurrentMemory = m_EstimateCurrentDisk = 0;
            m_EstimateHasUnsavedMaps = false;
            m_EstimateDetailGpuPerTile = 0;
            m_EstimateDetailSlots = 0;
            Array.Clear(m_SurfaceAssigned, 0, m_SurfaceAssigned.Length);
            Array.Clear(m_SurfaceBytes, 0, m_SurfaceBytes.Length);
            m_EstimateMapGroups.Clear();
            var allMaps = new HashSet<Texture2D>();
            var allFiles = new HashSet<string>();
            foreach (var tile in tiles)
            {
                var maps = new HashSet<Texture2D>();
                var files = new HashSet<string>();
                void Add(Texture2D map, string kind, int layer)
                {
                    if (map == null) return;
                    m_EstimateMapCount++;
                    m_EstimateDetailSlots++;
                    var definition = tile.DensityDetailLayers[layer];
                    var prototype = (uint)definition.PrototypeIndex < tile.Prototypes.Count ? tile.Prototypes[definition.PrototypeIndex] : null;
                    string detail = prototype?.Prefab != null ? prototype.Prefab.name
                        : prototype?.Mesh != null ? prototype.Mesh.name : "Detail " + definition.PrototypeIndex;
                    string key = $"{detail} / Layer {layer + 1} · {kind} · {map.width}×{map.height} {map.format}";
                    m_EstimateMapGroups.TryGetValue(key, out int count);
                    m_EstimateMapGroups[key] = count + 1;
                    maps.Add(map);
                }
                for (int i = 0; i < tile.DensityDetailLayerCount; i++)
                {
                    var layer = tile.DensityDetailLayers[i];
                    if (layer == null) continue;
                    Add(layer.DensityMap, "Density", i);
                    Add(layer.SizeMap, "Size", i);
                    Add(layer.GrassIdMap, "Grass IDs", i);
                }
                foreach (var map in maps) m_EstimateDetailGpuPerTile += TexturePayload(map);
                var materials = tile.MeshRenderer != null ? tile.MeshRenderer.sharedMaterials : Array.Empty<Material>();
                for (int role = 0; role < SurfaceMapProperties.Length; role++)
                {
                    var roleMaps = new HashSet<Texture2D>();
                    foreach (var material in materials)
                        if (material != null && material.HasProperty(SurfaceMapProperties[role])
                            && material.GetTexture(SurfaceMapProperties[role]) is Texture2D surface)
                            roleMaps.Add(surface);
                    if (roleMaps.Count > 0) m_SurfaceAssigned[role]++;
                    foreach (var surface in roleMaps)
                    {
                        m_EstimateMapCount++;
                        string key = $"Surface · {SurfaceMapLabels[role]} · {surface.width}×{surface.height} {surface.format}";
                        m_EstimateMapGroups.TryGetValue(key, out int count);
                        m_EstimateMapGroups[key] = count + 1;
                        if (maps.Add(surface)) m_SurfaceBytes[role] += TexturePayload(surface);
                    }
                }
                foreach (var map in maps)
                {
                    m_EstimateGpuPerTile += TexturePayload(map);
                    if (allMaps.Add(map)) m_EstimateCurrentMemory += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(map);
                    string path = AssetDatabase.GetAssetPath(map);
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) { m_EstimateHasUnsavedMaps = true; continue; }
                    long length = new FileInfo(path).Length;
                    if (files.Add(path)) m_EstimateDiskPerTile += length;
                    if (allFiles.Add(path)) m_EstimateCurrentDisk += length;
                }
                var mesh = tile.MeshFilter != null ? tile.MeshFilter.sharedMesh : null;
                if (mesh == null) continue;
                for (int buffer = 0; buffer < mesh.vertexBufferCount; buffer++)
                    m_EstimateMeshPerTile += (double)mesh.vertexCount * mesh.GetVertexBufferStride(buffer);
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    m_EstimateMeshPerTile += mesh.GetIndexCount(sub) * (mesh.indexFormat == IndexFormat.UInt32 ? 4.0 : 2.0);
            }
            m_EstimateUniqueCount = allMaps.Count;
            if (tiles.Length > 0)
            {
                m_EstimateGpuPerTile /= tiles.Length;
                m_EstimateDetailGpuPerTile /= tiles.Length;
                m_EstimateDiskPerTile /= tiles.Length;
                m_EstimateMeshPerTile /= tiles.Length;
            }
            m_EstimateScanned = true;
        }
    }
}
#endif
