using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public partial class MapPerformanceScannerPanel
{
    [Serializable]
    private sealed class MgTerrainInfo
    {
        public MGTerrain terrain;
        public long representedInstances;
        public long visibleCells;
        public long detailDraws;
        public long placedDraws;
        public long residentBytes;
        public int layers;
        public string backend;
    }

    [SerializeField] private List<MgTerrainInfo> mgTerrainInfos = new();
    [SerializeField] private List<Mesh> mgTerrainMeshes = new();
    private long MgTerrainMeshMemoryBytes => cachedMgTerrainMeshBytes;
    [SerializeField] private long mgTerrainResidentBytes;
    [SerializeField] private long mgTerrainDetailDraws;
    [SerializeField] private long mgTerrainPlacedDraws;

    private const string MgEstimateNote = "MG detail submissions are a camera-independent, full-density estimate assuming uniform painted coverage within each tile's draw-distance footprint. " +
        "Tile footprints are summed; frustum/occlusion culling, shared-world batch merging and density LOD can reduce actual draws. " +
        "BRG groups mesh/material parts; fallback estimates use cell meshes or 1,023-instance batches. " +
        "Cached runtime memory is measured at scan time; unbuilt streaming cells and future GPU allocations are not predicted.";

    private void CollectMgTerrains()
    {
        CollectMgTerrains(FindSceneObjects<MGTerrain>());
    }

    internal void CollectMgTerrains(IEnumerable<MGTerrain> terrains)
    {
        foreach (var terrain in terrains)
        {
            if (IsUnderChallengesRoot(terrain.transform)) continue;
            ReportProgress($"Scanning MG terrain: {terrain.name}", .69f);
            var quality = terrain.CaptureWorldQuality();
            var active = terrain.isActiveAndEnabled && quality.m_DrawInstances;
            var info = new MgTerrainInfo { terrain = terrain };
            var serialized = new SerializedObject(terrain);
            var brg = quality.m_UseBatchRendererGroup && SystemInfo.supportsInstancing && GraphicsSettings.currentRenderPipeline != null;
            info.backend = brg ? "BRG estimate" : quality.m_CombineDenseDetailMeshes ? "Combined cells estimate" : "Instanced fallback estimate";

            AddTexture(terrain.ControlMap1, terrainTextures);
            AddTexture(terrain.ControlMap2, terrainTextures);
            AddTexture(terrain.ControlMap1, terrainSplatTextures);
            AddTexture(terrain.ControlMap2, terrainSplatTextures);
            if (terrain.MeshFilter != null) CollectMesh(terrain.MeshFilter.sharedMesh, 0, true, false);
            if (terrain.MeshRenderer != null)
                foreach (var material in terrain.MeshRenderer.sharedMaterials)
                    CollectMgMaterial(material);

            // The regular renderer pass owns surface draw and mesh accounting, including
            // generated surface chunks and distant LOD renderers. Do not add those again.
            var instanceCounts = new long[terrain.Prototypes.Count];
            foreach (var instance in terrain.Instances)
                if ((uint)instance.PrototypeIndex < instanceCounts.Length) instanceCounts[instance.PrototypeIndex]++;

            var materialList = new List<Material>();
            var meshes = new List<Mesh>();
            var partCounts = new long[terrain.Prototypes.Count];
            var vertexCounts = new long[terrain.Prototypes.Count];
            for (int index = 0; index < terrain.Prototypes.Count; index++)
            {
                var prototype = terrain.Prototypes[index];
                if (prototype == null) continue;
                terrain.GetDetailSourceMaterials(index, materialList);
                foreach (var material in materialList)
                {
                    CollectMgMaterial(material);
                    if (material == null || material.shader == null || !material.shader.keywordSpace.FindKeyword("DOTS_INSTANCING_ON").isValid) brg = false;
                }
                terrain.GetPerformanceSourceMeshes(index, meshes, out int parts, out long vertices);
                partCounts[index] = parts;
                vertexCounts[index] = vertices;
                foreach (var mesh in meshes) { CollectMesh(mesh, instanceCounts[index], true, false); if (!mgTerrainMeshes.Contains(mesh)) mgTerrainMeshes.Add(mesh); }
                var count = instanceCounts[index];
                // Placed instances use ordinary DrawMeshInstanced, independently of BRG details.
                if (active && count > 0)
                    info.placedDraws += partCounts[index] * CalculateInstanceBatchCount(count);
            }

            info.backend = brg ? "BRG estimate" : quality.m_CombineDenseDetailMeshes ? "Combined cells estimate" : "Instanced fallback estimate";
            var bounds = terrain.MeshRenderer != null ? terrain.MeshRenderer.bounds : new Bounds(terrain.transform.position, Vector3.one);
            double area = Math.Max(.0001, (double)bounds.size.x * bounds.size.z);
            foreach (var layer in terrain.DensityDetailLayers)
            {
                if (layer == null) continue;
                AddTexture(layer.DensityMap, terrainTextures);
                AddTexture(layer.SizeMap, terrainTextures);
                AddTexture(layer.HealthMap, terrainTextures);
                AddTexture(layer.GrassIdMap, terrainTextures);
                if (!layer.RenderingEnabled || layer.PaletteSourceOnly || layer.DensityMap == null ||
                    (uint)layer.PrototypeIndex >= terrain.Prototypes.Count) continue;
                var prototype = terrain.Prototypes[layer.PrototypeIndex];
                if (prototype == null) continue;
                info.layers++;
                info.representedInstances += Math.Max(0, layer.RepresentedInstanceCount);
                if (!active || layer.RepresentedInstanceCount <= 0) continue;
                float distance = prototype.MaximumDrawDistance > 0 ? prototype.MaximumDrawDistance : Mathf.Max(bounds.size.x, bounds.size.z);
                if (prototype.Kind != MGTerrain.InstanceKind.Tree)
                {
                    float ceiling = terrain.World != null && terrain.World.isActiveAndEnabled ? terrain.World.DetailDistance :
                        serialized.FindProperty("m_MaxDensityDetailDistance")?.floatValue ?? 0;
                    if (ceiling > 0) distance = Mathf.Min(distance, ceiling);
                }
                double fraction = Math.Min(1d, Math.PI * distance * distance / area);
                long instances = (long)Math.Ceiling(layer.RepresentedInstanceCount * fraction * quality.m_OverallDetailDensity);
                int cellSize = Mathf.Clamp(quality.m_DetailChunkCells, 2, 64);
                long cells = (long)Math.Ceiling(layer.DensityMap.width / (double)cellSize) * (long)Math.Ceiling(layer.DensityMap.height / (double)cellSize);
                cells = Math.Min(instances, (long)Math.Ceiling(cells * fraction));
                if (instances == 0 || cells == 0) continue;
                info.visibleCells += cells;
                long groups = brg ? 1 : quality.m_CombineDenseDetailMeshes
                    ? Math.Max(cells, (long)Math.Ceiling(instances * (double)vertexCounts[layer.PrototypeIndex] / Math.Max(65535, quality.m_MaxCombinedDetailVerticesPerChunk)))
                    : Math.Max(cells, CalculateInstanceBatchCount(instances));
                info.detailDraws += groups * partCounts[layer.PrototypeIndex];
            }

            terrain.GetCachedSurfaceMeshes(meshes);
            foreach (var mesh in meshes) { CollectMesh(mesh, 0, true, false); if (!mgTerrainMeshes.Contains(mesh)) mgTerrainMeshes.Add(mesh); }
            var usage = terrain.CaptureMemoryUsageSnapshot();
            // Surface/control/density resources already enter the unique resource sets.
            info.residentBytes = usage.SerializedInstanceBytes + usage.StreamedCombinedMeshBytes + usage.MatrixBufferBytes +
                usage.ProceduralCellDataBytes + usage.PendingBuildBytes + usage.CpuSourceCacheBytes + usage.RuntimeMaterialBytes;
            mgTerrainResidentBytes += info.residentBytes;
            mgTerrainDetailDraws += info.detailDraws;
            mgTerrainPlacedDraws += info.placedDraws;
            mgTerrainInfos.Add(info);
        }
    }

    private void CollectMgMaterial(Material material)
    {
        if (material == null) return;
        terrainPrototypeMaterials.Add(material);
        CollectMaterial(material, false, -1);
        foreach (var texture in GetTextures(material)) terrainTextures.Add(texture);
    }

    private void DrawMgTerrainUsage()
    {
        if (mgTerrainInfos.Count == 0) return;
        showMgTerrainDetails = DrawReportFoldout(showMgTerrainDetails, $"MG Terrain ({mgTerrainInfos.Count:N0} tiles)", new Color(.28f, .48f, .16f), "MgTerrain");
        if (!showMgTerrainDetails) return;
        EditorGUILayout.HelpBox(MgEstimateNote, MessageType.Info);
        foreach (var info in DrawReportPage(mgTerrainInfos, ref mgTerrainPage))
        {
            DrawTableRow(info.terrain != null ? GetTransformPath(info.terrain.transform) : "Missing tile", info.backend);
            DrawTableRow("Detail layers / represented instances", $"{info.layers:N0} / {info.representedInstances:N0}");
            DrawTableRow("Estimated visible cells / detail submissions", $"{info.visibleCells:N0} / {info.detailDraws:N0}");
            DrawTableRow("Placed instance submissions", info.placedDraws.ToString("N0"));
            DrawTableRow("Additional cached runtime memory", FormatBytes(info.residentBytes));
        }
    }

    private void AppendMgTerrainReport(StringBuilder report)
    {
        if (mgTerrainInfos.Count == 0) return;
        report.AppendLine("\nMG TERRAIN");
        report.AppendLine(MgEstimateNote);
        foreach (var info in mgTerrainInfos)
            report.AppendLine($"{GetTransformPath(info.terrain.transform)}: {info.backend}; layers {info.layers:N0}; represented instances {info.representedInstances:N0}; " +
                $"visible cells {info.visibleCells:N0}; detail submissions {info.detailDraws:N0}; placed submissions {info.placedDraws:N0}; additional cached memory {FormatBytes(info.residentBytes)}");
    }

    private static string GetTextureDimensions(Texture texture)
    {
        var size = $"{texture.width:N0} x {texture.height:N0}";
        return texture is Texture2DArray array ? $"{size} x {array.depth:N0} slices" :
            texture is Texture3D volume ? $"{size} x {volume.depth:N0}" :
            texture is CubemapArray cubes ? $"{size} x {cubes.cubemapCount:N0} cubes" : size;
    }

    internal static long EstimateTextureStorageBytes(Texture texture)
    {
        if (texture == null) return 0;
        int depth = texture is Texture2DArray array ? array.depth : texture is CubemapArray cubes ? cubes.cubemapCount * 6 : texture is Cubemap ? 6 : 1;
        int volumeDepth = texture is Texture3D volume ? volume.depth : 1;
        int width = texture.width, height = texture.height;
        long bytes = 0;
        for (int mip = 0; mip < texture.mipmapCount; mip++)
        {
            bytes += (long)GraphicsFormatUtility.ComputeMipmapSize(width, height, volumeDepth, texture.graphicsFormat) * depth;
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            volumeDepth = Math.Max(1, volumeDepth / 2);
        }
        return bytes;
    }
}
