using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.EditorResources;
using MashBoxSDK.Exporting;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Profiling;

[Serializable]
public sealed class MapPerformanceScanResult
{
    public float PerformanceScore { get; internal set; }
    public long SharedMemoryBytes { get; internal set; }
    public long TextureMemoryBytes { get; internal set; }
    public long MeshMemoryBytes { get; internal set; }
    public long TerrainDataMemoryBytes { get; internal set; }
    public long TerrainSplatMemoryBytes { get; internal set; }
    public long LightmapMemoryBytes { get; internal set; }
    public long LightProbeMemoryBytes { get; internal set; }
    public long ReflectionProbeMemoryBytes { get; internal set; }
    public long PostVolumeMemoryBytes { get; internal set; }
    public List<string> OversizedTextures { get; internal set; } = new();
}

[Serializable]
public class MapPerformanceScannerPanel
{
    public const int MaximumTextureDimension = 4096;

    [Serializable]
    private class MaterialInfo
    {
        public Material material;
        public Shader shader;
        public string[] keywords;
        public List<Texture> textures;
        public bool isDecal;
        public int lightmapIndex;
    }

    [Serializable]
    private class RendererIssue
    {
        public Renderer renderer;
        public List<Material> materials;
        public string reason;
    }

    [Serializable]
    private class MeshInfo
    {
        public Mesh mesh;
        public long memoryBytes;
        public long sceneUses;
        public long terrainUses;
        public long colliderUses;
    }

    [Serializable]
    private class TerrainInfo
    {
        public Terrain terrain;
        public TerrainData terrainData;
        public long terrainDataMemory;
        public long textureMemory;
        public int heightmapResolution;
        public int alphamapLayers;
        public int detailPrototypeCount;
        public long detailInstanceCount;
        public int treePrototypeCount;
        public int treeInstanceCount;
    }

    [Serializable]
    private class TextureInfo
    {
        public Texture texture;
        public long memoryBytes;
        public int materialUses;
        public bool usedByTerrain;
        public bool usedByTerrainSplat;
        public bool usedByLightmap;
        public bool usedByTerrainLightmap;
        public bool usedByReflectionProbe;
        public bool usedByPostVolume;
        public string assetPath;
    }

    private class BatchKey
    {
        public Shader shader;
        public string keywordSignature;
        public int lightmapIndex;

        public override int GetHashCode()
        {
            return (shader != null ? shader.GetHashCode() : 0) ^
                   (keywordSignature != null ? keywordSignature.GetHashCode() : 0) ^
                   lightmapIndex.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is not BatchKey other)
                return false;

            return shader == other.shader &&
                   keywordSignature == other.keywordSignature &&
                   lightmapIndex == other.lightmapIndex;
        }
    }

    [SerializeField] private List<MaterialInfo> materialInfos = new();
    [SerializeField] private List<RendererIssue> rendererIssues = new();
    [SerializeField] private long totalDrawCalls;
    [SerializeField] private long sceneRendererDrawCalls;
    [SerializeField] private long terrainSurfaceDrawCalls;
    [SerializeField] private long terrainInstanceDrawCalls;
    [SerializeField] private int decalDrawCalls;
    [SerializeField] private long totalTextureMemory;
    [SerializeField] private long totalMeshMemory;
    [SerializeField] private long totalTerrainDataMemory;
    [SerializeField] private long totalTerrainSplatMemory;
    [SerializeField] private long totalLightmapMemory;
    [SerializeField] private long totalTerrainLightmapMemory;
    [SerializeField] private long totalLightProbeMemory;
    [SerializeField] private long totalReflectionProbeMemory;
    [SerializeField] private long totalPostVolumeTextureMemory;
    [SerializeField] private long totalPostVolumeDataMemory;
    [SerializeField] private long totalMapMemory;
    [SerializeField] private long totalMeshUses;
    [SerializeField] private long totalDetailInstances;
    [SerializeField] private int totalTreeInstances;
    [SerializeField] private int terrainPrototypeMaterialCount;
    [SerializeField] private int lightProbeCount;
    [SerializeField] private int reflectionProbeCount;
    [SerializeField] private int postVolumeCount;
    [SerializeField] private int postVolumeProfileCount;
    [SerializeField] private List<MeshInfo> meshInfos = new();
    [SerializeField] private List<TerrainInfo> terrainInfos = new();
    [SerializeField] private List<TextureInfo> textureInfos = new();
    [SerializeField] private bool showTextureDetails = true;
    [SerializeField] private float performanceScore = 100f;
    [SerializeField] private string performanceGrade = "A";
    [SerializeField] private Color scoreColor = Color.green;
    [SerializeField] private Vector2 scroll;
    [SerializeField] private bool hasScanResults;

    [NonSerialized] private string cachedTargetGameName;
    [NonSerialized] private Texture2D cachedTargetGameLogo;

    private readonly Dictionary<BatchKey, List<MaterialInfo>> batches = new();
    private readonly HashSet<Material> processedMaterials = new();
    private readonly HashSet<Texture> terrainTextures = new();
    private readonly HashSet<Texture> terrainSplatTextures = new();
    private readonly HashSet<Texture> lightmapTextures = new();
    private readonly HashSet<Texture> terrainLightmapTextures = new();
    private readonly HashSet<int> includedLightmapIndices = new();
    private readonly HashSet<Texture> reflectionProbeTextures = new();
    private readonly HashSet<Texture> postVolumeTextures = new();
    private readonly HashSet<VolumeProfile> postVolumeProfiles = new();
    private readonly HashSet<TerrainData> processedTerrainData = new();
    private readonly HashSet<Material> terrainPrototypeMaterials = new();
    private readonly Dictionary<Mesh, MeshInfo> meshLookup = new();

    public void DrawGUI(bool embeddedInParentWindow = false)
    {
        EditorGUILayout.Space(embeddedInParentWindow ? 6f : 10f);
        DrawTargetGame();
        EditorGUILayout.Space(4f);
        EditorGUILayout.HelpBox(
            "Scan the loaded scene to review shared-memory usage, terrain density, unique meshes, shader fragmentation, decals, and renderers that are likely preventing batching.",
            MessageType.Info);

        if (GUILayout.Button("Scan Scene", GUILayout.Height(40)))
            ScanScene();

        EditorGUILayout.Space(10f);

        scroll = EditorGUILayout.BeginScrollView(scroll);

        if (!hasScanResults)
        {
            EditorGUILayout.HelpBox("Run a scan to populate the current scene's performance report.", MessageType.None);
        }
        else
        {
            DrawResults();
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawTargetGame()
    {
        var selectedGameName = EditorPrefs.GetString("ModIo.CurrentGame", string.Empty);
        var targetGame = GameRegistry.Find(selectedGameName);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (targetGame != null)
            {
                var logo = GetTargetGameLogo(targetGame);
                if (logo != null)
                {
                    var logoRect = GUILayoutUtility.GetRect(96f, 48f, GUILayout.Width(96f), GUILayout.Height(48f));
                    GUI.DrawTexture(logoRect, logo, ScaleMode.ScaleToFit, true);
                    GUILayout.Space(8f);
                }

                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField("Performance Target", EditorStyles.miniBoldLabel);
                    EditorGUILayout.LabelField(targetGame.DisplayName, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(
                        $"Map shared-memory budget: {FormatBytes(targetGame.MapSharedMemoryBudgetBytes)}",
                        EditorStyles.miniLabel);
                }
            }
            else
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField("Performance Target", EditorStyles.miniBoldLabel);
                    EditorGUILayout.LabelField("No supported target game selected", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(
                        "Select BMXS, ScootX, or ProjectX in MashBox SDK Setup.",
                        EditorStyles.miniLabel);
                }
            }

            GUILayout.FlexibleSpace();
        }
    }

    private Texture2D GetTargetGameLogo(GameDefinition targetGame)
    {
        if (targetGame == null)
            return null;

        if (!string.Equals(cachedTargetGameName, targetGame.DisplayName, StringComparison.Ordinal))
        {
            cachedTargetGameName = targetGame.DisplayName;
            cachedTargetGameLogo = AssetDatabase.LoadAssetAtPath<Texture2D>(
                MashBoxEditorResources.GetGameLogo(targetGame.DisplayName));
        }

        return cachedTargetGameLogo;
    }

    public MapPerformanceScanResult ScanScene()
    {
        try
        {
            ReportProgress("Preparing scene scan...", 0f);

            materialInfos.Clear();
            batches.Clear();
            processedMaterials.Clear();
            rendererIssues.Clear();
            meshInfos.Clear();
            terrainInfos.Clear();
            textureInfos.Clear();
            meshLookup.Clear();
            terrainTextures.Clear();
            terrainSplatTextures.Clear();
            lightmapTextures.Clear();
            terrainLightmapTextures.Clear();
            includedLightmapIndices.Clear();
            reflectionProbeTextures.Clear();
            postVolumeTextures.Clear();
            postVolumeProfiles.Clear();
            processedTerrainData.Clear();
            terrainPrototypeMaterials.Clear();

            totalDrawCalls = 0;
            sceneRendererDrawCalls = 0;
            terrainSurfaceDrawCalls = 0;
            terrainInstanceDrawCalls = 0;
            decalDrawCalls = 0;
            totalTextureMemory = 0;
            totalMeshMemory = 0;
            totalTerrainDataMemory = 0;
            totalTerrainSplatMemory = 0;
            totalLightmapMemory = 0;
            totalTerrainLightmapMemory = 0;
            totalLightProbeMemory = 0;
            totalReflectionProbeMemory = 0;
            totalPostVolumeTextureMemory = 0;
            totalPostVolumeDataMemory = 0;
            totalMapMemory = 0;
            totalMeshUses = 0;
            totalDetailInstances = 0;
            totalTreeInstances = 0;
            terrainPrototypeMaterialCount = 0;
            lightProbeCount = 0;
            reflectionProbeCount = 0;
            postVolumeCount = 0;
            postVolumeProfileCount = 0;

            CollectRenderers();
            CollectAdditionalMeshes();
            CollectTerrains();
            CollectLightingData();
            CollectPostVolumeData();
            CollectDecals();

            ReportProgress("Building shader variant groups...", 0.82f);
            BuildBatches();
            CalculateDrawCalls();

            ReportProgress("Calculating texture memory...", 0.87f);
            CalculateTextureMemory();

            ReportProgress("Finalizing shared-memory totals...", 0.97f);
            CalculateMemoryTotals();
            CalculatePerformanceScore();

            hasScanResults = true;
            Debug.Log("[MashBox] Performance scan complete.");
            return new MapPerformanceScanResult
            {
                PerformanceScore = performanceScore,
                SharedMemoryBytes = totalMapMemory,
                TextureMemoryBytes = totalTextureMemory,
                MeshMemoryBytes = totalMeshMemory,
                TerrainDataMemoryBytes = totalTerrainDataMemory,
                TerrainSplatMemoryBytes = totalTerrainSplatMemory,
                LightmapMemoryBytes = totalLightmapMemory,
                LightProbeMemoryBytes = totalLightProbeMemory,
                ReflectionProbeMemoryBytes = totalReflectionProbeMemory,
                PostVolumeMemoryBytes = totalPostVolumeTextureMemory + totalPostVolumeDataMemory,
                OversizedTextures = textureInfos
                    .Where(info => IsOversizedTexture(info.texture))
                    .Select(GetOversizedTextureDescription)
                    .ToList()
            };
        }
        catch (OperationCanceledException)
        {
            hasScanResults = false;
            Debug.Log("[MashBox] Performance scan cancelled.");
            return null;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static void ReportProgress(string message, float progress)
    {
        if (EditorUtility.DisplayCancelableProgressBar(
                "MashBox Map Performance Scan",
                message,
                Mathf.Clamp01(progress)))
        {
            throw new OperationCanceledException();
        }
    }

    private static bool ShouldReportProgress(int index, int total)
    {
        return index == 0 || index == total - 1 || index % 25 == 0;
    }

    private void CollectRenderers()
    {
        var renderers = FindSceneObjects<Renderer>();

        for (var index = 0; index < renderers.Length; index++)
        {
            if (ShouldReportProgress(index, renderers.Length))
                ReportProgress($"Scanning renderer {index + 1:N0} of {renderers.Length:N0}...", Mathf.Lerp(0.03f, 0.25f, (index + 1f) / Math.Max(1, renderers.Length)));

            var renderer = renderers[index];
            if (IsUnderChallengesRoot(renderer.transform))
                continue;

            if (renderer.GetComponent<DecalProjector>() != null)
                continue;

            if (renderer.lightmapIndex >= 0)
                includedLightmapIndices.Add(renderer.lightmapIndex);

            var mats = renderer.sharedMaterials.Where(m => m != null).ToList();
            sceneRendererDrawCalls += EstimateRendererDrawSubmissions(renderer);
            CollectRendererMesh(renderer, 1, false);

            foreach (var mat in mats)
                CollectMaterial(mat, false, renderer.lightmapIndex);

            if (mats.Count <= 1)
                continue;

            var variants = mats.Select(GetVariantKey).Distinct().ToList();
            if (variants.Count <= 1)
                continue;

            rendererIssues.Add(new RendererIssue
            {
                renderer = renderer,
                materials = mats,
                reason = GetDifferenceReason(mats)
            });
        }
    }

    private void CollectTerrains()
    {
        var terrains = FindSceneObjects<Terrain>();
        for (var terrainIndex = 0; terrainIndex < terrains.Length; terrainIndex++)
        {
            var terrain = terrains[terrainIndex];
            var terrainName = terrain != null ? terrain.name : "Missing Terrain";
            ReportProgress($"Scanning terrain {terrainIndex + 1:N0} of {terrains.Length:N0}: {terrainName}", Mathf.Lerp(0.40f, 0.68f, terrainIndex / (float)Math.Max(1, terrains.Length)));

            if (terrain == null || terrain.terrainData == null || IsUnderChallengesRoot(terrain.transform))
                continue;

            var data = terrain.terrainData;
            if (terrain.lightmapIndex >= 0)
                includedLightmapIndices.Add(terrain.lightmapIndex);
            var detailPrototypes = data.detailPrototypes;
            var treePrototypes = data.treePrototypes;
            terrainSurfaceDrawCalls += EstimateTerrainSurfaceDrawSubmissions(data);
            var info = new TerrainInfo
            {
                terrain = terrain,
                terrainData = data,
                terrainDataMemory = Math.Max(0L, Profiler.GetRuntimeMemorySizeLong(data)),
                heightmapResolution = data.heightmapResolution,
                alphamapLayers = data.alphamapLayers,
                detailPrototypeCount = detailPrototypes.Length,
                treePrototypeCount = treePrototypes.Length,
                treeInstanceCount = data.treeInstanceCount
            };

            if (terrain.materialTemplate != null)
                CollectMaterial(terrain.materialTemplate, false, -1);

            var localTextures = new HashSet<Texture>();
            foreach (var splatTexture in data.alphamapTextures)
            {
                if (splatTexture != null)
                    terrainSplatTextures.Add(splatTexture);
            }

            foreach (var texture in GetTerrainTextures(data))
            {
                if (texture == null || !localTextures.Add(texture))
                    continue;

                terrainTextures.Add(texture);
                info.textureMemory += GetRuntimeTextureSizeBytes(texture);
            }

            for (var layer = 0; layer < detailPrototypes.Length; layer++)
            {
                var terrainProgress = (terrainIndex + (layer + 1f) / Math.Max(1, detailPrototypes.Length)) / Math.Max(1, terrains.Length);
                ReportProgress($"Reading {terrain.name} detail layer {layer + 1:N0} of {detailPrototypes.Length:N0}...", Mathf.Lerp(0.40f, 0.68f, terrainProgress));

                var instanceCount = CountDetailInstances(data, layer);
                info.detailInstanceCount += instanceCount;
                if (instanceCount > 0)
                    terrainInstanceDrawCalls += EstimateTerrainDetailDrawSubmissions(data, detailPrototypes[layer]);

                var prototype = detailPrototypes[layer];
                if (prototype.prototype != null)
                    CollectPrototypeResources(prototype.prototype, instanceCount);

                if (prototype.prototypeTexture != null)
                    terrainTextures.Add(prototype.prototypeTexture);
            }

            var treeCounts = CountTreeInstances(data, treePrototypes.Length);
            for (var index = 0; index < treePrototypes.Length; index++)
            {
                if (treePrototypes[index].prefab != null)
                {
                    CollectPrototypeResources(treePrototypes[index].prefab, treeCounts[index]);
                    terrainInstanceDrawCalls += EstimateInstancedPrototypeDrawSubmissions(
                        treePrototypes[index].prefab,
                        treeCounts[index]);
                }
            }

            terrainInfos.Add(info);
            if (processedTerrainData.Add(data))
                totalTerrainDataMemory += info.terrainDataMemory;
            totalDetailInstances += info.detailInstanceCount;
            totalTreeInstances += info.treeInstanceCount;
        }
    }

    private void CollectLightingData()
    {
        ReportProgress("Collecting scene lightmaps...", 0.69f);

        var lightmaps = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
        foreach (var lightmapIndex in includedLightmapIndices)
        {
            if (lightmapIndex >= 0 && lightmapIndex < lightmaps.Length)
                CollectLightmapTextures(lightmaps[lightmapIndex], lightmapTextures);
        }

        foreach (var terrainInfo in terrainInfos)
        {
            var lightmapIndex = terrainInfo.terrain != null ? terrainInfo.terrain.lightmapIndex : -1;
            if (lightmapIndex >= 0 && lightmapIndex < lightmaps.Length)
                CollectLightmapTextures(lightmaps[lightmapIndex], terrainLightmapTextures);
        }

        ReportProgress("Measuring light probe data...", 0.72f);
        var lightProbes = LightmapSettings.lightProbes;
        if (lightProbes != null)
        {
            var allProbeGroups = FindAllSceneObjects<LightProbeGroup>();
            var allConfiguredProbes = 0;
            var includedConfiguredProbes = 0;
            foreach (var group in allProbeGroups)
            {
                if (group == null)
                    continue;

                var groupProbeCount = group.probePositions?.Length ?? 0;
                allConfiguredProbes += groupProbeCount;
                if (!IsEditorOnly(group.transform))
                    includedConfiguredProbes += groupProbeCount;
            }

            var includedRatio = allConfiguredProbes > 0
                ? Mathf.Clamp01(includedConfiguredProbes / (float)allConfiguredProbes)
                : 1f;

            lightProbeCount = Mathf.RoundToInt(lightProbes.count * includedRatio);
            totalLightProbeMemory = (long)(Math.Max(0L, Profiler.GetRuntimeMemorySizeLong(lightProbes)) * includedRatio);
            if (totalLightProbeMemory == 0 && lightProbeCount > 0)
                totalLightProbeMemory = lightProbeCount * 120L;
        }

        var reflectionProbes = FindSceneObjects<ReflectionProbe>();
        reflectionProbeCount = 0;
        for (var index = 0; index < reflectionProbes.Length; index++)
        {
            if (ShouldReportProgress(index, reflectionProbes.Length))
                ReportProgress($"Collecting reflection probe {index + 1:N0} of {reflectionProbes.Length:N0}...", Mathf.Lerp(0.72f, 0.76f, (index + 1f) / Math.Max(1, reflectionProbes.Length)));

            var probe = reflectionProbes[index];
            if (probe == null || IsUnderChallengesRoot(probe.transform))
                continue;

            reflectionProbeCount++;
            AddTexture(probe.bakedTexture, reflectionProbeTextures);
            AddTexture(probe.customBakedTexture, reflectionProbeTextures);
            AddTexture(probe.realtimeTexture, reflectionProbeTextures);
        }
    }

    private static void CollectLightmapTextures(LightmapData lightmap, HashSet<Texture> destination)
    {
        if (lightmap == null)
            return;

        AddTexture(lightmap.lightmapColor, destination);
        AddTexture(lightmap.lightmapDir, destination);
        AddTexture(lightmap.shadowMask, destination);
    }

    private static void AddTexture(Texture texture, HashSet<Texture> destination)
    {
        if (texture != null)
            destination.Add(texture);
    }

    private void CollectPostVolumeData()
    {
        var volumes = FindSceneObjects<Volume>();
        for (var index = 0; index < volumes.Length; index++)
        {
            if (ShouldReportProgress(index, volumes.Length))
                ReportProgress($"Scanning post volume {index + 1:N0} of {volumes.Length:N0}...", Mathf.Lerp(0.76f, 0.79f, (index + 1f) / Math.Max(1, volumes.Length)));

            var volume = volumes[index];
            if (volume == null || IsUnderChallengesRoot(volume.transform))
                continue;

            postVolumeCount++;
            var profile = volume.sharedProfile;
            if (profile == null || !postVolumeProfiles.Add(profile))
                continue;

            totalPostVolumeDataMemory += Math.Max(0L, Profiler.GetRuntimeMemorySizeLong(profile));

            foreach (var component in profile.components)
            {
                if (component == null)
                    continue;

                totalPostVolumeDataMemory += Math.Max(0L, Profiler.GetRuntimeMemorySizeLong(component));
                CollectSerializedTextures(component, postVolumeTextures);
            }
        }

        postVolumeProfileCount = postVolumeProfiles.Count;
    }

    private static void CollectSerializedTextures(UnityEngine.Object source, HashSet<Texture> destination)
    {
        var serializedObject = new SerializedObject(source);
        var property = serializedObject.GetIterator();
        if (!property.NextVisible(true))
            return;

        do
        {
            if (property.propertyType == SerializedPropertyType.ObjectReference &&
                property.objectReferenceValue is Texture texture)
            {
                destination.Add(texture);
            }
        }
        while (property.NextVisible(true));
    }

    private static long CountDetailInstances(TerrainData data, int layer)
    {
        try
        {
            var density = data.GetDetailLayer(0, 0, data.detailWidth, data.detailHeight, layer);
            long count = 0;
            foreach (var value in density)
                count += value;
            return count;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MashBox] Could not read detail layer {layer} from {data.name}: {exception.Message}");
            return 0;
        }
    }

    private static long[] CountTreeInstances(TerrainData data, int prototypeCount)
    {
        var counts = new long[prototypeCount];
        foreach (var instance in data.treeInstances)
        {
            if (instance.prototypeIndex >= 0 && instance.prototypeIndex < counts.Length)
                counts[instance.prototypeIndex]++;
        }

        return counts;
    }

    private void CollectPrototypeResources(GameObject prefab, long instanceCount)
    {
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (IsEditorOnly(renderer.transform))
                continue;

            foreach (var material in renderer.sharedMaterials)
            {
                if (material != null)
                {
                    terrainPrototypeMaterials.Add(material);
                    CollectMaterial(material, false, -1);
                }
            }

            CollectRendererMesh(renderer, instanceCount, true);
        }

        foreach (var collider in prefab.GetComponentsInChildren<MeshCollider>(true))
        {
            if (!IsEditorOnly(collider.transform))
                CollectMesh(collider.sharedMesh, instanceCount, true, true);
        }
    }

    private void CollectAdditionalMeshes()
    {
        var meshFilters = FindSceneObjects<MeshFilter>();
        for (var index = 0; index < meshFilters.Length; index++)
        {
            if (ShouldReportProgress(index, meshFilters.Length))
                ReportProgress($"Scanning mesh filter {index + 1:N0} of {meshFilters.Length:N0}...", Mathf.Lerp(0.25f, 0.32f, (index + 1f) / Math.Max(1, meshFilters.Length)));

            var meshFilter = meshFilters[index];
            if (IsUnderChallengesRoot(meshFilter.transform) || meshFilter.GetComponent<MeshRenderer>() != null)
                continue;

            CollectMesh(meshFilter.sharedMesh, 1, false, false);
        }

        var colliders = FindSceneObjects<MeshCollider>();
        for (var index = 0; index < colliders.Length; index++)
        {
            if (ShouldReportProgress(index, colliders.Length))
                ReportProgress($"Scanning mesh collider {index + 1:N0} of {colliders.Length:N0}...", Mathf.Lerp(0.32f, 0.40f, (index + 1f) / Math.Max(1, colliders.Length)));

            var collider = colliders[index];
            if (IsUnderChallengesRoot(collider.transform))
                continue;

            CollectMesh(collider.sharedMesh, 1, false, true);
        }
    }

    private void CollectRendererMesh(Renderer renderer, long useCount, bool terrainPrototype)
    {
        Mesh mesh = null;

        if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
            mesh = skinnedMeshRenderer.sharedMesh;
        else if (renderer is MeshRenderer)
        {
            var meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null)
                mesh = meshFilter.sharedMesh;
        }
        else if (renderer is ParticleSystemRenderer particleSystemRenderer)
            mesh = particleSystemRenderer.mesh;

        CollectMesh(mesh, useCount, terrainPrototype, false);
    }

    private static long EstimateRendererDrawSubmissions(Renderer renderer)
    {
        if (renderer == null)
            return 0;

        // Each populated material slot can submit its corresponding mesh submesh. Extra material
        // slots also submit another pass over the last submesh, so count the actual populated slots.
        return renderer.sharedMaterials.LongCount(material => material != null);
    }

    private static long EstimateTerrainSurfaceDrawSubmissions(TerrainData data)
    {
        if (data == null)
            return 0;

        // Unity terrain draw counts are camera/LOD dependent. Use maximum-detail 64x64 heightmap
        // regions and one surface pass per four terrain layers as a conservative scene estimate.
        const int estimatedPatchResolution = 64;
        var patchesPerAxis = Math.Max(1L, (data.heightmapResolution - 1L + estimatedPatchResolution - 1L) / estimatedPatchResolution);
        var layerPasses = Math.Max(1L, (data.alphamapLayers + 3L) / 4L);
        return patchesPerAxis * patchesPerAxis * layerPasses;
    }

    private static long EstimateTerrainDetailDrawSubmissions(TerrainData data, DetailPrototype prototype)
    {
        if (data == null || prototype == null)
            return 0;

        var resolutionPerPatch = Math.Max(1, data.detailResolutionPerPatch);
        var patchesPerAxis = Math.Max(1L, (data.detailResolution + (long)resolutionPerPatch - 1L) / resolutionPerPatch);
        var prototypePasses = prototype.prototype != null
            ? Math.Max(1L, prototype.prototype.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => !IsEditorOnly(renderer.transform))
                .Sum(renderer => (long)renderer.sharedMaterials.Count(material => material != null)))
            : 1L;

        return patchesPerAxis * patchesPerAxis * prototypePasses;
    }

    private static long EstimateInstancedPrototypeDrawSubmissions(GameObject prefab, long instanceCount)
    {
        if (prefab == null || instanceCount <= 0)
            return 0;

        const long maximumInstancesPerBatch = 1023L;
        var batchesForInstances = (instanceCount + maximumInstancesPerBatch - 1L) / maximumInstancesPerBatch;
        var prototypePasses = prefab.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => !IsEditorOnly(renderer.transform))
            .Sum(renderer => (long)renderer.sharedMaterials.Count(material => material != null));

        return batchesForInstances * Math.Max(1L, prototypePasses);
    }

    private void CollectMesh(Mesh mesh, long useCount, bool terrainPrototype, bool colliderUse)
    {
        if (mesh == null)
            return;

        if (!meshLookup.TryGetValue(mesh, out var info))
        {
            info = new MeshInfo
            {
                mesh = mesh,
                memoryBytes = GetMeshMemoryBytes(mesh)
            };
            meshLookup.Add(mesh, info);
            meshInfos.Add(info);
        }

        if (colliderUse)
            info.colliderUses += useCount;
        else if (terrainPrototype)
            info.terrainUses += useCount;
        else
            info.sceneUses += useCount;
    }

    private static long GetMeshMemoryBytes(Mesh mesh)
    {
        var runtimeSize = Profiler.GetRuntimeMemorySizeLong(mesh);
        if (runtimeSize > 0)
            return runtimeSize;

        long vertexBytes = 0;
        for (var stream = 0; stream < mesh.vertexBufferCount; stream++)
            vertexBytes += (long)mesh.vertexCount * mesh.GetVertexBufferStride(stream);

        long indexCount = 0;
        for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            indexCount += mesh.GetIndexCount(subMesh);

        var indexBytes = indexCount * (mesh.indexFormat == IndexFormat.UInt32 ? 4L : 2L);
        return vertexBytes + indexBytes;
    }

    private static IEnumerable<Texture> GetTerrainTextures(TerrainData data)
    {
        if (data.heightmapTexture != null)
            yield return data.heightmapTexture;
        if (data.holesTexture != null)
            yield return data.holesTexture;

        foreach (var texture in data.alphamapTextures)
            yield return texture;

        foreach (var layer in data.terrainLayers)
        {
            if (layer == null)
                continue;
            if (layer.diffuseTexture != null)
                yield return layer.diffuseTexture;
            if (layer.normalMapTexture != null)
                yield return layer.normalMapTexture;
            if (layer.maskMapTexture != null)
                yield return layer.maskMapTexture;
        }
    }

    private void CollectDecals()
    {
        var decals = FindSceneObjects<DecalProjector>();

        for (var index = 0; index < decals.Length; index++)
        {
            if (ShouldReportProgress(index, decals.Length))
                ReportProgress($"Scanning decal {index + 1:N0} of {decals.Length:N0}...", Mathf.Lerp(0.79f, 0.82f, (index + 1f) / Math.Max(1, decals.Length)));

            var decal = decals[index];
            if (IsUnderChallengesRoot(decal.transform))
                continue;

            if (decal.material != null)
            {
                decalDrawCalls++;
                CollectMaterial(decal.material, true, -1);
            }
        }
    }


    private static T[] FindSceneObjects<T>() where T : Component
    {
        return FindAllSceneObjects<T>()
            .Where(component => component != null && !IsEditorOnly(component.transform))
            .ToArray();
    }

    private static T[] FindAllSceneObjects<T>() where T : Component
    {
#if UNITY_6000_0_OR_NEWER
        return UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include);
#elif UNITY_2023_1_OR_NEWER
        return UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
#pragma warning disable 0618
        return UnityEngine.Object.FindObjectsOfType<T>(true);
#pragma warning restore 0618
#endif
    }

    private static bool IsEditorOnly(Transform target)
    {
        var current = target;
        while (current != null)
        {
            if (string.Equals(current.tag, "EditorOnly", StringComparison.Ordinal))
                return true;

            current = current.parent;
        }

        return false;
    }

    private bool IsUnderChallengesRoot(Transform target)
    {
        var current = target;
        while (current != null)
        {
            if (string.Equals(current.name, "Challenges", StringComparison.OrdinalIgnoreCase))
                return true;

            current = current.parent;
        }

        return false;
    }

    private void CollectMaterial(Material mat, bool isDecal, int lightmapIndex)
    {
        if (processedMaterials.Contains(mat))
            return;

        processedMaterials.Add(mat);

        var keywords = mat.enabledKeywords
            .Select(k => k.name)
            .OrderBy(k => k)
            .ToArray();

        materialInfos.Add(new MaterialInfo
        {
            material = mat,
            shader = mat.shader,
            keywords = keywords,
            textures = GetTextures(mat),
            isDecal = isDecal,
            lightmapIndex = lightmapIndex
        });
    }

    private void BuildBatches()
    {
        foreach (var mat in materialInfos.Where(m => !m.isDecal))
        {
            var key = new BatchKey
            {
                shader = mat.shader,
                keywordSignature = string.Join(";", mat.keywords),
                lightmapIndex = mat.lightmapIndex
            };

            if (!batches.ContainsKey(key))
                batches[key] = new List<MaterialInfo>();

            batches[key].Add(mat);
        }
    }

    private void CalculateDrawCalls()
    {
        totalDrawCalls = sceneRendererDrawCalls + terrainSurfaceDrawCalls + terrainInstanceDrawCalls + decalDrawCalls;
    }

    private void CalculateTextureMemory()
    {
        var seen = new HashSet<Texture>(terrainTextures);
        seen.UnionWith(lightmapTextures);
        seen.UnionWith(reflectionProbeTextures);
        seen.UnionWith(postVolumeTextures);
        var materialUses = new Dictionary<Texture, int>();

        foreach (var mat in materialInfos)
        {
            foreach (var tex in mat.textures)
            {
                if (tex != null)
                {
                    seen.Add(tex);
                    materialUses.TryGetValue(tex, out var useCount);
                    materialUses[tex] = useCount + 1;
                }
            }
        }

        var textures = seen.ToList();
        for (var index = 0; index < textures.Count; index++)
        {
            if (ShouldReportProgress(index, textures.Count))
                ReportProgress($"Measuring texture {index + 1:N0} of {textures.Count:N0}...", Mathf.Lerp(0.87f, 0.97f, (index + 1f) / Math.Max(1, textures.Count)));

            var texture = textures[index];
            var memoryBytes = GetRuntimeTextureSizeBytes(texture);
            totalTextureMemory += memoryBytes;

            materialUses.TryGetValue(texture, out var useCount);
            textureInfos.Add(new TextureInfo
            {
                texture = texture,
                memoryBytes = memoryBytes,
                materialUses = useCount,
                usedByTerrain = terrainTextures.Contains(texture),
                usedByTerrainSplat = terrainSplatTextures.Contains(texture),
                usedByLightmap = lightmapTextures.Contains(texture),
                usedByTerrainLightmap = terrainLightmapTextures.Contains(texture),
                usedByReflectionProbe = reflectionProbeTextures.Contains(texture),
                usedByPostVolume = postVolumeTextures.Contains(texture),
                assetPath = AssetDatabase.GetAssetPath(texture)
            });
        }

        textureInfos.Sort((left, right) => right.memoryBytes.CompareTo(left.memoryBytes));
    }

    private long GetRuntimeTextureSizeBytes(Texture texture)
    {
        var runtimeSize = Profiler.GetRuntimeMemorySizeLong(texture);
        return runtimeSize > 0 ? runtimeSize : EstimateTextureSizeBytes(texture);
    }

    private void CalculateMemoryTotals()
    {
        totalMeshMemory = meshInfos.Sum(info => info.memoryBytes);
        totalMeshUses = meshInfos.Sum(info => info.sceneUses + info.terrainUses + info.colliderUses);
        terrainPrototypeMaterialCount = terrainPrototypeMaterials.Count;
        totalTerrainSplatMemory = textureInfos.Where(info => info.usedByTerrainSplat).Sum(info => info.memoryBytes);
        totalLightmapMemory = textureInfos.Where(info => info.usedByLightmap).Sum(info => info.memoryBytes);
        totalTerrainLightmapMemory = textureInfos.Where(info => info.usedByTerrainLightmap).Sum(info => info.memoryBytes);
        totalReflectionProbeMemory = textureInfos.Where(info => info.usedByReflectionProbe).Sum(info => info.memoryBytes);
        totalPostVolumeTextureMemory = textureInfos.Where(info => info.usedByPostVolume).Sum(info => info.memoryBytes);
        totalMapMemory = totalTextureMemory + totalMeshMemory + totalTerrainDataMemory + totalLightProbeMemory + totalPostVolumeDataMemory;
    }

    private long EstimateTextureSizeBytes(Texture tex)
    {
        var path = AssetDatabase.GetAssetPath(tex);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (importer != null)
        {
            // Texture dimensions are read from the imported object, not the source file. Unity has
            // already applied the default and active-platform max-size overrides at this point.
            var width = tex.width;
            var height = tex.height;
            var platformName = BuildPipeline.GetBuildTargetName(EditorUserBuildSettings.activeBuildTarget);
            var platformSettings = importer.GetPlatformTextureSettings(platformName);
            var format = platformSettings.overridden
                ? platformSettings.format
                : importer.GetPlatformTextureSettings("DefaultTexturePlatform").format;

            if (format == TextureImporterFormat.Automatic)
            {
                format = importer.textureCompression == TextureImporterCompression.Uncompressed
                    ? TextureImporterFormat.RGBA32
                    : TextureImporterFormat.DXT5;
            }

            var bpp = format == TextureImporterFormat.DXT1 ? 4 :
                      (format == TextureImporterFormat.DXT5 || format == TextureImporterFormat.BC7) ? 8 : 32;

            long size = (long)width * height * bpp / 8;
            if (importer.mipmapEnabled)
                size = (long)(size * 1.33f);

            return size;
        }

        return (long)tex.width * tex.height * 4;
    }

    private List<Texture> GetTextures(Material mat)
    {
        var textures = new List<Texture>();
        var shader = mat.shader;
        var count = shader.GetPropertyCount();

        for (var i = 0; i < count; i++)
        {
            if (shader.GetPropertyType(i) != ShaderPropertyType.Texture)
                continue;

            var name = shader.GetPropertyName(i);
            var tex = mat.GetTexture(name);

            if (tex != null)
                textures.Add(tex);
        }

        return textures;
    }

    private string GetVariantKey(Material mat)
    {
        var keywords = mat.enabledKeywords
            .Select(k => k.name)
            .OrderBy(k => k);

        return mat.shader.name + "|" + string.Join(";", keywords);
    }

    private string GetDifferenceReason(List<Material> mats)
    {
        var shaders = mats.Select(m => m.shader.name).Distinct().ToList();
        if (shaders.Count > 1)
            return "Different shaders";

        var allKeywords = mats.SelectMany(m => m.enabledKeywords.Select(k => k.name)).Distinct();
        var diffs = allKeywords.Where(k => !mats.All(m => m.IsKeywordEnabled(k))).Take(5);

        return diffs.Any() ? "Keyword mismatch: " + string.Join(", ", diffs) : "Unknown difference";
    }

    private void CalculatePerformanceScore()
    {
        const float maxDrawCalls = 2000f;
        const float maxTextureMemory = 2f * 1024f * 1024f * 1024f;
        const float maxDecals = 5f;
        const float maxShaderVariants = 6f;

        var rendererPenalty = Mathf.Clamp01(rendererIssues.Count / 100f);
        var drawPenalty = Mathf.Clamp01(totalDrawCalls / maxDrawCalls);
        var texturePenalty = Mathf.Clamp01(totalMapMemory / (maxTextureMemory * 1.5f));
        var decalPenalty = Mathf.Clamp01(decalDrawCalls / maxDecals);
        var shaderPenalty = Mathf.Clamp01(batches.Count / maxShaderVariants);

        var penalty =
            rendererPenalty * 0.4f +
            drawPenalty * 0.2f +
            texturePenalty * 0.15f +
            decalPenalty * 0.15f +
            shaderPenalty * 0.1f;

        performanceScore = Mathf.Clamp(100f - penalty * 100f, 0f, 100f);

        if (performanceScore > 85f)
        {
            performanceGrade = "A";
            scoreColor = Color.green;
        }
        else if (performanceScore > 70f)
        {
            performanceGrade = "B";
            scoreColor = Color.yellow;
        }
        else if (performanceScore > 50f)
        {
            performanceGrade = "C";
            scoreColor = new Color(1f, 0.5f, 0f);
        }
        else
        {
            performanceGrade = "D";
            scoreColor = Color.red;
        }
    }

    private void DrawScoreBreakdown()
    {
        DrawTableSectionHeader("Score Breakdown");
        DrawMetric("Renderer Issues", rendererIssues.Count, 10f);
        DrawMetric("Estimated Draw Submissions", totalDrawCalls, 2000f);
        DrawMetric("Decals", decalDrawCalls, 5f);
        DrawMetric("Shader Variants", batches.Count, 6f);
        DrawMetric("Texture Memory (Imported/Runtime)", totalTextureMemory, 2L * 1024L * 1024L * 1024L, true);
        DrawMetric("Estimated Shared Memory", totalMapMemory, 3L * 1024L * 1024L * 1024L, true);
    }

    private void DrawMetric(string label, float value, float threshold, bool isBytes = false)
    {
        var ratio = value / threshold;
        var color = ratio < 0.5f ? Color.green : ratio < 1f ? Color.yellow : Color.red;
        var display = isBytes ? FormatBytes((long)value) : value.ToString("N0");
        DrawTableRow(label, display, false, color);
    }

    private void DrawTableSectionHeader(string title, bool terrainHighlight = false)
    {
        var rect = EditorGUILayout.GetControlRect(false, 24f);
        var background = terrainHighlight
            ? (EditorGUIUtility.isProSkin ? new Color(0.25f, 0.38f, 0.18f, 1f) : new Color(0.68f, 0.82f, 0.52f, 1f))
            : (EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f, 1f) : new Color(0.72f, 0.72f, 0.72f, 1f));

        DrawTableCell(rect, title, EditorStyles.boldLabel, background, null);
    }

    private void DrawTableRow(string label, string value, bool columnHeader = false, Color? valueColor = null)
    {
        var rowRect = EditorGUILayout.GetControlRect(false, columnHeader ? 23f : 21f);
        const float preferredMetricColumnWidth = 360f;
        var metricColumnWidth = Mathf.Min(preferredMetricColumnWidth, Mathf.Max(80f, rowRect.width - 100f));

        const float dividerWidth = 5f;
        var labelRect = new Rect(rowRect.x, rowRect.y, metricColumnWidth - dividerWidth * 0.5f, rowRect.height);
        var valueRect = new Rect(
            rowRect.x + metricColumnWidth + dividerWidth * 0.5f,
            rowRect.y,
            Mathf.Max(1f, rowRect.width - metricColumnWidth - dividerWidth * 0.5f),
            rowRect.height);
        var dividerRect = new Rect(
            rowRect.x + metricColumnWidth - dividerWidth * 0.5f,
            rowRect.y,
            dividerWidth,
            rowRect.height);

        var background = columnHeader
            ? (EditorGUIUtility.isProSkin ? new Color(0.18f, 0.18f, 0.18f, 1f) : new Color(0.80f, 0.80f, 0.80f, 1f))
            : (EditorGUIUtility.isProSkin ? new Color(0.15f, 0.15f, 0.15f, 1f) : new Color(0.91f, 0.91f, 0.91f, 1f));
        var style = columnHeader ? EditorStyles.miniBoldLabel : EditorStyles.label;

        DrawTableCell(labelRect, label, style, background, null);
        DrawTableCell(valueRect, value, style, background, valueColor);
        EditorGUI.DrawRect(dividerRect, EditorGUIUtility.isProSkin
            ? new Color(0.32f, 0.32f, 0.32f, 1f)
            : new Color(0.52f, 0.52f, 0.52f, 1f));
    }

    private static void DrawTableCell(Rect rect, string text, GUIStyle style, Color background, Color? textColor)
    {
        var border = EditorGUIUtility.isProSkin
            ? new Color(0.32f, 0.32f, 0.32f, 1f)
            : new Color(0.52f, 0.52f, 0.52f, 1f);
        EditorGUI.DrawRect(rect, border);

        var innerRect = new Rect(rect.x + 1f, rect.y + 1f, Mathf.Max(0f, rect.width - 2f), Mathf.Max(0f, rect.height - 2f));
        EditorGUI.DrawRect(innerRect, background);

        var textRect = new Rect(innerRect.x + 6f, innerRect.y, Mathf.Max(0f, innerRect.width - 12f), innerRect.height);
        var previousContentColor = GUI.contentColor;
        if (textColor.HasValue)
            GUI.contentColor = textColor.Value;
        GUI.Label(textRect, new GUIContent(text, text), style);
        GUI.contentColor = previousContentColor;
    }

    private void DrawResults()
    {
        DrawPerformanceScore();
        DrawScoreBreakdown();
        EditorGUILayout.HelpBox(
            "Estimated Draw Submissions is a conservative static scene estimate before camera culling. " +
            "Actual per-frame draw calls vary with terrain LOD, visibility, static batching, and GPU instancing.",
            MessageType.Info);
        EditorGUILayout.Space(10f);

        EditorGUILayout.LabelField("Scan Results", EditorStyles.boldLabel);
        DrawTableRow("Metric", "Value", true);

        DrawTableSectionHeader("Scene & Rendering");
        DrawTableRow("Materials", materialInfos.Count.ToString("N0"));
        DrawTableRow("Shader Variants (Batches)", batches.Count.ToString("N0"));
        DrawTableRow("Estimated Draw Submissions", totalDrawCalls.ToString("N0"));
        DrawTableRow("Scene Renderer Submissions", sceneRendererDrawCalls.ToString("N0"));
        DrawTableRow("Terrain Surface Submissions", terrainSurfaceDrawCalls.ToString("N0"));
        DrawTableRow("Terrain Detail/Tree Submissions", terrainInstanceDrawCalls.ToString("N0"));
        DrawTableRow("Decal Draw Calls", decalDrawCalls.ToString("N0"));
        DrawTableRow("Texture Import Target", EditorUserBuildSettings.activeBuildTarget.ToString());

        DrawTableSectionHeader("Memory Summary");
        DrawTableRow("Texture Memory (Imported/Runtime)", FormatBytes(totalTextureMemory));
        var oversizedTextureCount = textureInfos.Count(info => IsOversizedTexture(info.texture));
        DrawTableRow(
            "Textures Above 4K",
            oversizedTextureCount > 0 ? $"{oversizedTextureCount:N0} - VALIDATION ERROR" : "0",
            false,
            oversizedTextureCount > 0 ? Color.red : Color.green);
        DrawTableRow("Unique Mesh Memory", $"{FormatBytes(totalMeshMemory)} ({meshInfos.Count:N0} meshes, {totalMeshUses:N0} uses)");
        DrawTableRow("Estimated Shared Memory", FormatBytes(totalMapMemory), false, scoreColor);

        DrawTableSectionHeader("Terrain", true);
        DrawTableRow("Terrain Count", terrainInfos.Count.ToString("N0"));
        DrawTableRow("Terrain Data Memory", FormatBytes(totalTerrainDataMemory));
        DrawTableRow("Terrain Splat Map Memory", FormatBytes(totalTerrainSplatMemory));
        DrawTableRow("Terrain Lightmap Memory", FormatBytes(totalTerrainLightmapMemory));
        DrawTableRow("Detail Instances", totalDetailInstances.ToString("N0"));
        DrawTableRow("Tree Instances", totalTreeInstances.ToString("N0"));
        DrawTableRow("Unique Prototype Materials", $"{terrainPrototypeMaterialCount:N0} (included in shader variants)");

        DrawTableSectionHeader("Lighting & Probes");
        DrawTableRow("All Lightmap Memory", FormatBytes(totalLightmapMemory));
        DrawTableRow("Light Probe Data", $"{FormatBytes(totalLightProbeMemory)} ({lightProbeCount:N0} probes)");
        DrawTableRow("Reflection Probe Memory", $"{FormatBytes(totalReflectionProbeMemory)} ({reflectionProbeCount:N0} probes)");

        DrawPostVolumeUsage();

        GUILayout.Space(10f);
        DrawTextureUsage();

        GUILayout.Space(10f);
        DrawTerrainUsage();

        GUILayout.Space(10f);
        DrawMeshUsage();

        GUILayout.Space(20f);
        DrawShaderFragmentation();

        GUILayout.Space(10f);
        DrawTopOffenders();

        GUILayout.Space(10f);
        DrawDecals();

        GUILayout.Space(10f);
        DrawRendererIssues();
    }

    private void DrawPostVolumeUsage()
    {
        DrawTableSectionHeader("Post Volume Usage");
        DrawTableRow("Scene Volumes", postVolumeCount.ToString("N0"));
        DrawTableRow("Unique Profiles", postVolumeProfileCount.ToString("N0"));
        DrawTableRow("Texture Memory", FormatBytes(totalPostVolumeTextureMemory));
        DrawTableRow("Profile Data Memory", FormatBytes(totalPostVolumeDataMemory));
        DrawTableRow("Combined Post Volume Memory", FormatBytes(totalPostVolumeTextureMemory + totalPostVolumeDataMemory));
    }

    private void DrawTextureUsage()
    {
        showTextureDetails = EditorGUILayout.Foldout(
            showTextureDetails,
            $"Textures by Memory ({textureInfos.Count} unique)",
            true,
            EditorStyles.foldoutHeader);

        if (!showTextureDetails)
            return;

        foreach (var info in textureInfos)
        {
            if (info.texture == null)
                continue;

            var oversized = IsOversizedTexture(info.texture);
            var rowRect = EditorGUILayout.BeginHorizontal();
            if (oversized && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rowRect, new Color(0.55f, 0.08f, 0.08f, EditorGUIUtility.isProSkin ? 0.70f : 0.28f));

            if (GUILayout.Button("Select", GUILayout.Width(52f)))
                Selection.activeObject = info.texture;

            if (GUILayout.Button("Ping", GUILayout.Width(44f)))
                EditorGUIUtility.PingObject(info.texture);

            var sources = new List<string>();
            if (info.usedByTerrainSplat)
                sources.Add("terrain splat");
            else if (info.usedByTerrain)
                sources.Add("terrain");
            if (info.usedByTerrainLightmap)
                sources.Add("terrain lightmap");
            else if (info.usedByLightmap)
                sources.Add("lightmap");
            if (info.usedByReflectionProbe)
                sources.Add("reflection probe");
            if (info.usedByPostVolume)
                sources.Add("post volume");
            if (info.materialUses > 0)
                sources.Add($"{info.materialUses} material(s)");

            var source = sources.Count > 0 ? string.Join(" + ", sources) : "scene texture";
            var location = string.IsNullOrEmpty(info.assetPath) ? "runtime/generated" : info.assetPath;

            var previousContentColor = GUI.contentColor;
            if (oversized)
                GUI.contentColor = Color.red;

            EditorGUILayout.LabelField(
                $"{(oversized ? "VALIDATION ERROR >4K | " : string.Empty)}{info.texture.name}: {FormatBytes(info.memoryBytes)} | " +
                $"{info.texture.width:N0} x {info.texture.height:N0} | {info.texture.GetType().Name} | {source} | {location}",
                oversized ? EditorStyles.boldLabel : EditorStyles.label);

            GUI.contentColor = previousContentColor;

            EditorGUILayout.EndHorizontal();
        }
    }

    private static bool IsOversizedTexture(Texture texture)
    {
        return texture != null &&
               (texture.width > MaximumTextureDimension || texture.height > MaximumTextureDimension);
    }

    private static string GetOversizedTextureDescription(TextureInfo info)
    {
        if (info?.texture == null)
            return "Missing texture";

        var location = string.IsNullOrWhiteSpace(info.assetPath) ? "runtime/generated" : info.assetPath;
        return $"{info.texture.name} ({info.texture.width:N0} x {info.texture.height:N0}) - {location}";
    }

    private void DrawTerrainUsage()
    {
        EditorGUILayout.LabelField("Terrain Details", EditorStyles.boldLabel);

        if (terrainInfos.Count == 0)
        {
            EditorGUILayout.LabelField("No runtime terrains found.");
            return;
        }

        foreach (var info in terrainInfos.OrderByDescending(item => item.terrainDataMemory + item.textureMemory))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select", GUILayout.Width(60f)))
                Selection.activeObject = info.terrain.gameObject;

            EditorGUILayout.LabelField(
                $"{info.terrain.name}: TerrainData {FormatBytes(info.terrainDataMemory)}, terrain textures {FormatBytes(info.textureMemory)}, " +
                $"height {info.heightmapResolution}, splat layers {info.alphamapLayers}, " +
                $"details {info.detailInstanceCount:N0}/{info.detailPrototypeCount} prototypes, " +
                $"trees {info.treeInstanceCount:N0}/{info.treePrototypeCount} prototypes");
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawMeshUsage()
    {
        EditorGUILayout.LabelField("Largest Unique Meshes", EditorStyles.boldLabel);

        foreach (var info in meshInfos.OrderByDescending(item => item.memoryBytes).Take(20))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select", GUILayout.Width(60f)))
                Selection.activeObject = info.mesh;

            EditorGUILayout.LabelField(
                $"{info.mesh.name}: {FormatBytes(info.memoryBytes)} | {info.mesh.vertexCount:N0} vertices | " +
                $"scene uses {info.sceneUses:N0}, terrain instance uses {info.terrainUses:N0}, collider uses {info.colliderUses:N0}");
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawPerformanceScore()
    {
        var big = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 24,
            alignment = TextAnchor.MiddleCenter
        };

        GUI.color = scoreColor;
        GUILayout.Label($"Performance Score: {performanceScore:F0} ({performanceGrade})", big);
        GUI.color = Color.white;
    }

    private void DrawShaderFragmentation()
    {
        EditorGUILayout.LabelField("Shader Fragmentation", EditorStyles.boldLabel);

        var grouped = materialInfos
            .Where(m => !m.isDecal)
            .GroupBy(m => m.shader);

        foreach (var group in grouped.OrderByDescending(g => g.Count()))
        {
            var variants = group
                .Select(m => string.Join(";", m.keywords))
                .Distinct()
                .Count();

            EditorGUILayout.LabelField($"{group.Key.name} -> {variants} variants ({group.Count()} materials)");
        }
    }

    private void DrawTopOffenders()
    {
        EditorGUILayout.LabelField("Top Shader Variants", EditorStyles.boldLabel);

        var worst = batches
            .OrderByDescending(b => b.Value.Count)
            .Take(10);

        foreach (var batch in worst)
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Select", GUILayout.Width(60f)))
                Selection.objects = batch.Value.Select(m => m.material).ToArray();

            EditorGUILayout.LabelField($"{batch.Key.shader.name} | LM:{batch.Key.lightmapIndex} ({batch.Value.Count})");
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawDecals()
    {
        EditorGUILayout.LabelField("Decals", EditorStyles.boldLabel);

        var decals = materialInfos.Where(m => m.isDecal).ToList();
        EditorGUILayout.LabelField($"Total Decals: {decals.Count}");

        foreach (var decal in decals.Take(20))
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Select", GUILayout.Width(60f)))
                Selection.activeObject = decal.material;

            EditorGUILayout.LabelField(decal.material.name);
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawRendererIssues()
    {
        EditorGUILayout.LabelField("Non-Batching Renderers", EditorStyles.boldLabel);

        if (rendererIssues.Count == 0)
        {
            EditorGUILayout.LabelField("None");
            return;
        }

        foreach (var issue in rendererIssues)
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Select", GUILayout.Width(60f)))
                Selection.activeObject = issue.renderer.gameObject;

            EditorGUILayout.LabelField($"{issue.renderer.name} -> {issue.reason}");
            EditorGUILayout.EndHorizontal();
        }
    }

    private string FormatBytes(long bytes)
    {
        if (bytes > 1024L * 1024L * 1024L)
            return $"{(bytes / (1024f * 1024f * 1024f)):F2} GB";

        if (bytes > 1024L * 1024L)
            return $"{(bytes / (1024f * 1024f)):F2} MB";

        if (bytes > 1024L)
            return $"{(bytes / 1024f):F2} KB";

        return $"{bytes} B";
    }
}
