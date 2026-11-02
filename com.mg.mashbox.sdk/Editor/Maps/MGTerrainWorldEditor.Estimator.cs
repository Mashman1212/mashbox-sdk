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
    // Bounds are inexpensive; scanning all triangles is explicitly requested and cached.
    internal sealed class MGTerrainMeasurements
    {
        double surfaceArea;
        bool scanned;
        int skipped;
        int lastSignature;
        static void DrawArea(string label, double value, string tooltip = "")
        {
            EditorGUILayout.LabelField(new GUIContent(label + " (km²)", tooltip), new GUIContent($"{value / 1000000d:N3} km²"));
            EditorGUILayout.LabelField(new GUIContent(label + " (m²)", tooltip), new GUIContent($"{value:N0} m²"));
        }

        internal void Draw(MGTerrain[] tiles)
        {
            Bounds bounds = default;
            bool hasBounds = false;
            double footprint = 0;
            int valid = 0, active = 0, signature = 17;
            foreach (var tile in tiles)
            {
                if (tile == null) continue;
                if (tile.isActiveAndEnabled) active++;
                var filter = tile.MeshFilter;
                if (filter == null || filter.sharedMesh == null) continue;
                var mesh = filter.sharedMesh;
                var matrix = filter.transform.localToWorldMatrix;
                unchecked { signature = signature * 31 + mesh.GetInstanceID(); signature = signature * 31 + matrix.GetHashCode(); signature = signature * 31 + EditorUtility.GetDirtyCount(mesh); }
                var local = mesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (!hasBounds) { bounds = new Bounds(p, Vector3.zero); hasBounds = true; }
                    else bounds.Encapsulate(p);
                }
                var x = matrix.MultiplyVector(Vector3.right * local.size.x);
                var z = matrix.MultiplyVector(Vector3.forward * local.size.z);
                footprint += Math.Abs((double)x.x * z.z - (double)x.z * z.x);
                valid++;
            }
            if (lastSignature != signature) { scanned = false; lastSignature = signature; }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Current Terrain Dimensions", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Tiles", $"{tiles.Length:N0} total / {active:N0} active / {valid:N0} with meshes");
            if (!hasBounds) { EditorGUILayout.LabelField("No terrain meshes to measure."); return; }
            EditorGUILayout.LabelField("Width × Depth (X × Z)", $"{bounds.size.x:N1} × {bounds.size.z:N1} m");
            EditorGUILayout.LabelField("Width × Depth (km)", $"{bounds.size.x / 1000f:0.###} × {bounds.size.z / 1000f:0.###} km");
            EditorGUILayout.LabelField("Elevation Range (Y)", $"{bounds.min.y:N1} to {bounds.max.y:N1} m ({bounds.size.y:N1} m)");
            DrawArea("Bounding Rectangle", (double)bounds.size.x * bounds.size.z, "Includes empty gaps between tiles.");
            DrawArea("Tile Footprint Sum", footprint, "Sum of horizontal tile footprints. Gaps between tiles are excluded; overlapping tiles count separately. Cut-out holes are included in footprints.");
            if (GUILayout.Button(scanned ? "Refresh Mesh Surface Area" : "Calculate Mesh Surface Area (Including Slopes)"))
            {
                surfaceArea = 0; skipped = 0;
                foreach (var tile in tiles)
                {
                    var filter = tile != null ? tile.MeshFilter : null;
                    var mesh = filter != null ? filter.sharedMesh : null;
                    if (mesh == null || !mesh.isReadable) { skipped++; continue; }
                    var vertices = mesh.vertices;
                    var matrix = filter.transform.localToWorldMatrix;
                    for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    {
                        if (mesh.GetTopology(submesh) != MeshTopology.Triangles) continue;
                        var indices = mesh.GetTriangles(submesh);
                        for (int i = 0; i + 2 < indices.Length; i += 3)
                        {
                            var a = matrix.MultiplyVector(vertices[indices[i + 1]] - vertices[indices[i]]);
                            var b = matrix.MultiplyVector(vertices[indices[i + 2]] - vertices[indices[i]]);
                            surfaceArea += Vector3.Cross(a, b).magnitude * .5d;
                        }
                    }
                }
                scanned = true;
            }
            if (scanned)
            {
                DrawArea(skipped == 0 ? "Mesh Surface Area" : "Mesh Surface Area (Partial)", surfaceArea);
                if (skipped > 0) EditorGUILayout.HelpBox($"{skipped} tile(s) have missing or unreadable meshes and were excluded from surface area.", MessageType.Info);
            }
            EditorGUILayout.HelpBox("Measures existing tiles, including inactive tiles. X × Z is the overall horizontal span. Mesh surface area sums triangles including slopes; overlapping surfaces count separately.", MessageType.None);
        }
    }

    public sealed partial class MGTerrainWorldEditor
    {
        readonly MGTerrainMeasurements m_CurrentMeasurements = new MGTerrainMeasurements();

        bool m_ShowEstimatorMaps;
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
            float[] columns = { .29f, .17f, .20f, .15f, .19f };
            MGTerrainDiagnosticTable.Row(0, columns, new[] { "Surface map", "Assigned / slots", "Projected buffers", "Plan missing", "Resolution" }, heading: true);
            double plannedPerTile = 0;
            int row = 0;
            for (int i = 0; i < SurfaceMapLabels.Length; i++)
            {
                if (i == 1) continue;
                bool controls = i == 0;
                double divisor = Math.Max(1, m_EstimateTileCount);
                string label = controls ? "Control Maps 1 & 2" : SurfaceMapLabels[i];
                int assigned = controls ? m_SurfaceAssigned[0] + m_SurfaceAssigned[1] : m_SurfaceAssigned[i];
                double bytes = controls ? m_SurfaceBytes[0] + m_SurfaceBytes[1] : m_SurfaceBytes[i];
                int slots = m_EstimateTileCount * (controls ? 2 : 1);
                var cells = MGTerrainDiagnosticTable.Row(++row, columns, new[] { label, $"{assigned:N0} / {slots:N0}", EstimateBytes(bytes / divisor * projectedTiles), "", "" });
                var toggle = new Rect(cells[3].center.x - 8, cells[3].center.y - 9, 18, 18);
                m_PlanSurfaceMap[i] = EditorGUI.Toggle(toggle, new GUIContent("", "Include missing " + label + " in the projection. No textures are created."), m_PlanSurfaceMap[i]);
                using (new EditorGUI.DisabledScope(!m_PlanSurfaceMap[i]))
                    m_PlannedSurfaceResolution[i] = EditorGUI.IntPopup(cells[4], m_PlannedSurfaceResolution[i],
                        new[] { "256", "512", "1K", "2K", "4K", "8K" }, new[] { 256, 512, 1024, 2048, 4096, 8192 });
                if (!m_PlanSurfaceMap[i]) continue;
                int mapsPerTile = controls ? 2 : 1;
                double missingFraction = m_EstimateTileCount > 0 ? mapsPerTile - assigned / divisor : mapsPerTile;
                long pixels = 0;
                for (int n = m_PlannedSurfaceResolution[i]; n > 0; n /= 2) pixels += (long)n * n;
                plannedPerTile += missingFraction * pixels * (i == 4 ? 2 : 4);
            }
            return plannedPerTile;
        }


        void DrawWorldDataEstimator(MGTerrainWorld world)
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("World Data Estimator", EditorStyles.boldLabel);
                GUILayout.Label(new GUIContent(EditorGUIUtility.IconContent("console.infoicon.sml").image,
                    "Assigned maps use their actual resolutions, formats and mip levels. Missing-map planning assumes RGBA32 for controls/RGB/world normals and R16 for height, with a full mip chain. These are estimates only; no textures are created. Planned file sizes are unknown until saved.\n\nProjection repeats the average current tile configuration across a square world using the configured tile size. It assumes independent maps per tile, but preserves sharing within a tile. Empty tiles reduce the average. Refresh after painting or changing maps.\n\nThe total includes mesh and texture buffers. Disk files are a separate measurement. CPU-readable copies, detail instances, colliders, other material textures, shared grass arrays and engine overhead are excluded. Streaming can reduce loaded memory; it does not reduce stored data. This is not a build-size or frame-rate estimate."), GUILayout.Width(20), GUILayout.Height(18));
            }
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
            MGTerrainDiagnosticTable.Header("Current sample", "Value");
            MGTerrainDiagnosticTable.Metric(1, "Tile grid", $"{side:N0} × {side:N0} = {total:N0} tiles");
            MGTerrainDiagnosticTable.Metric(2, "Rounded world coverage", $"{side * tileSize / 1000:0.###} × {side * tileSize / 1000:0.###} km");
            MGTerrainDiagnosticTable.Metric(3, "Sampled tiles", m_EstimateTileCount.ToString("N0"));
            MGTerrainDiagnosticTable.Metric(4, "Current map slots / unique maps", $"{m_EstimateMapCount:N0} / {m_EstimateUniqueCount:N0}");
            MGTerrainDiagnosticTable.Metric(5, "Current unique map memory", EstimateBytes(m_EstimateCurrentMemory));
            MGTerrainDiagnosticTable.Metric(6, "Current map asset files", EstimateBytes(m_EstimateCurrentDisk));
            if (m_EstimateTileCount == 0)
                EditorGUILayout.HelpBox("No tiles are available to sample. Mesh creation settings and any enabled surface-map plans are estimated.", MessageType.Info);
            double mapsPerTile = m_EstimateTileCount > 0 ? m_EstimateDetailSlots / (double)m_EstimateTileCount : 0;
            double plannedSurface = DrawSurfaceMapEstimate(total);
            EditorGUILayout.Space();
            double mesh = m_EstimateTileCount > 0 ? m_EstimateMeshPerTile : CreationMeshBytes(world);
            MGTerrainDiagnosticTable.Header("Projected full world", "Estimate");
            MGTerrainDiagnosticTable.Metric(1, "Average detail texture / tile", EstimateBytes(m_EstimateDetailGpuPerTile));
            MGTerrainDiagnosticTable.Metric(2, "Detail map slots", $"≈ {Math.Ceiling(mapsPerTile * total):N0}");
            MGTerrainDiagnosticTable.Metric(3, "Detail texture buffers", EstimateBytes(m_EstimateDetailGpuPerTile * total));
            MGTerrainDiagnosticTable.Metric(4, "Assigned surface map buffers", EstimateBytes((m_EstimateGpuPerTile - m_EstimateDetailGpuPerTile) * total));
            MGTerrainDiagnosticTable.Metric(5, "Planned missing surface maps", EstimateBytes(plannedSurface * total));
            MGTerrainDiagnosticTable.Metric(6, "Surface mesh buffers", EstimateBytes(mesh * total));
            MGTerrainDiagnosticTable.Metric(7, "Total mesh + texture buffers", "≈ " + EstimateBytes((mesh + m_EstimateGpuPerTile + plannedSurface) * total), total: true);
            MGTerrainDiagnosticTable.Metric(8, "Map files on disk (separate from buffer total)", "≈ " + EstimateBytes(m_EstimateDiskPerTile * total));
            EditorGUILayout.Space();
            m_ShowEstimatorMaps = EditorGUILayout.Foldout(m_ShowEstimatorMaps, "Map Resolutions / Formats", true);
            if (m_ShowEstimatorMaps)
            {
                MGTerrainDiagnosticTable.Header("Map / layer / format", "Slots");
                int row = 0;
                foreach (var group in m_EstimateMapGroups.OrderBy(p => p.Key))
                    MGTerrainDiagnosticTable.Metric(++row, group.Key, group.Value.ToString("N0"));
            }
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
