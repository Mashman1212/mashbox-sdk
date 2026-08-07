using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MashBoxSDK.EditorResources;
using MashBoxSDK.Exporting;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

internal static class MapPerformanceMaterialTextureCollector
{
    private const int MgLitTrailLayerCount = 8;
    private const string MgLitTrailShaderNameFragment = "MG_Lit_Trail";
    private const string MgLitTrailTerrainLayerTagPrefix = "MashBox.MGLitTrail.TerrainLayer.";
    private static readonly string[] MgLitTrailBaseMapArrayProperties =
        { "_BaseMapArray", "_TrailAlbedoArray", "_AlbedoArray" };
    private static readonly string[] MgLitTrailHeightArrayProperties =
        { "_HeightMapArray", "_TrailHeightArray", "_HeightArray" };
    private static readonly string[] MgLitTrailSurfaceArrayProperties =
        { "_SurfaceMapArray", "_TrailSurfaceArray", "_SurfaceArray" };

    public static List<Texture> GetTextures(Material material)
    {
        var textures = new HashSet<Texture>();
        if (material == null || material.shader == null)
            return textures.ToList();

        Shader shader = material.shader;
        int propertyCount = shader.GetPropertyCount();
        for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
        {
            if (shader.GetPropertyType(propertyIndex) != ShaderPropertyType.Texture)
                continue;

            Texture texture = material.GetTexture(shader.GetPropertyName(propertyIndex));
            if (texture != null)
                textures.Add(texture);
        }

        // Before array migration, MG_Lit_Trail stored resolved source textures on the
        // material. Once all generated arrays are assigned, TerrainLayer GUID tags are
        // editor-only authoring metadata and must not be counted as runtime textures.
        if (shader.name.IndexOf(MgLitTrailShaderNameFragment, StringComparison.OrdinalIgnoreCase) >= 0 &&
            !HasGeneratedTrailArrays(material))
            AddMgLitTrailTerrainLayerTextures(material, textures);

        return textures.ToList();
    }

    private static void AddMgLitTrailTerrainLayerTextures(Material material, HashSet<Texture> textures)
    {
        for (int layerIndex = 0; layerIndex < MgLitTrailLayerCount; layerIndex++)
        {
            string tagName = MgLitTrailTerrainLayerTagPrefix + layerIndex.ToString("00");
            string terrainLayerGuid = material.GetTag(tagName, false, string.Empty);
            if (string.IsNullOrEmpty(terrainLayerGuid))
                continue;

            string terrainLayerPath = AssetDatabase.GUIDToAssetPath(terrainLayerGuid);
            TerrainLayer terrainLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(terrainLayerPath);
            if (terrainLayer == null)
                continue;

            if (terrainLayer.diffuseTexture != null)
                textures.Add(terrainLayer.diffuseTexture);
            if (terrainLayer.normalMapTexture != null)
                textures.Add(terrainLayer.normalMapTexture);
            if (terrainLayer.maskMapTexture != null)
                textures.Add(terrainLayer.maskMapTexture);
        }
    }

    private static bool HasGeneratedTrailArrays(Material material)
    {
        return HasTextureArray(material, MgLitTrailBaseMapArrayProperties) &&
               HasTextureArray(material, MgLitTrailHeightArrayProperties) &&
               HasTextureArray(material, MgLitTrailSurfaceArrayProperties);
    }

    private static bool HasTextureArray(Material material, string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (material.HasProperty(propertyName) &&
                material.GetTexture(propertyName) is Texture2DArray)
                return true;
        }

        return false;
    }
}

[Serializable]
public sealed class MapPerformanceScanResult
{
    public float PerformanceScore { get; internal set; }
    public float PerformanceScoreBeforeRuntimeCombining { get; internal set; }
    public float RuntimeCombinerScoreGain { get; internal set; }
    public long DrawSubmissionsBeforeRuntimeCombining { get; internal set; }
    public long DrawSubmissionsAfterRuntimeCombining { get; internal set; }
    public long SharedMemoryBytes { get; internal set; }
    public long TextureMemoryBytes { get; internal set; }
    public long MeshMemoryBytes { get; internal set; }
    public long RuntimeCombinedMeshMemoryBytes { get; internal set; }
    public long TerrainDataMemoryBytes { get; internal set; }
    public long TerrainSplatMemoryBytes { get; internal set; }
    public long LightmapMemoryBytes { get; internal set; }
    public long LightProbeMemoryBytes { get; internal set; }
    public long ReflectionProbeMemoryBytes { get; internal set; }
    public long PostVolumeMemoryBytes { get; internal set; }
    public List<string> OversizedTextures { get; internal set; } = new();
    public List<string> UnsupportedShaders { get; internal set; } = new();
}

[Serializable]
public class MapPerformanceScannerPanel
{
    public const int MaximumTextureDimension = 4096;
    public const float MinimumPublishPerformanceScore = 60f;
    private const string MashBoxPackageName = "com.mg.mashbox.sdk";
    private const string SupportedTerrainShaderName = "HDRP/TerrainLit";
    private const string SupportedDecalShaderName = "HDRP/Decal";
    private const string HdrpLitShaderName = "HDRP/Lit";
    private const string MgLitBasicShaderName = "MGShaders/HDRP/Lit/MG_Lit_Basic";
    private const string FoldoutPreferencePrefix = "MashBox.PerformanceScanner.Foldout.";
    private const float DrawPerfectScoreThreshold = 200f;
    private const float DrawMaximumPenaltyThreshold = 2000f;
    private const int ShaderVariantPerfectScoreThreshold = 6;
    private const int ShaderVariantMaximumPenaltyThreshold = 12;
    private const int DecalMaterialPerfectScoreThreshold = 1;
    private const int DecalMaterialMaximumPenaltyThreshold = 5;
    private const int DecalProjectorPerfectScoreThreshold = 299;
    private const int DecalProjectorMaximumPenaltyThreshold = 1000;
    private const long SharedMemoryPerfectScoreBytes = 1L * 1024L * 1024L * 1024L;
    private const long SharedMemoryMaximumPenaltyBytes = 3L * 1024L * 1024L * 1024L;
    private const long MaximumInstancesPerBatch = 1023L;

    [Serializable]
    private class MaterialInfo
    {
        public Material material;
        public Shader shader;
        public string[] keywords;
        public List<Texture> textures;
        public bool isDecal;
        public int lightmapIndex;
        public bool isSupportedShader;
        public string shaderAssetPath;
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
    private class RuntimeMeshCombinerInfo
    {
        public RuntimeMeshCombiner combiner;
        public long estimatedMemoryBytes;
        public long estimatedVertexBytes;
        public long estimatedIndexBytes;
        public long estimatedOutputVertices;
        public long indexCount;
        public int sourceMeshCount;
        public int sourceSubMeshCount;
        public int materialCount;
        public int skippedUnreadableMeshCount;
        public long sourceDrawSubmissions;
        public long combinedDrawSubmissions;
        public int sourceRendererIssueCount;
        public int combinedRendererIssueCount;
        public bool executesInCurrentState;
        public bool combineOnAwake;
        public bool disableSourceRenderers;
        public bool addMeshCollider;
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
        public int detailPatchesPerAxis;
        public long visibleDetailChunkBudgetPerPrototype;
        public long detailInstanceCount;
        public int treePrototypeCount;
        public int treeInstanceCount;
        public long surfaceChunkCount;
        public long surfaceDrawSubmissions;
        public long occupiedDetailChunkCount;
        public long detailChunkCount;
        public long detailDrawSubmissions;
        public long treeDrawSubmissions;
        public bool usesInstancedTerrain;
        public bool contributesDrawSubmissions;
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
    [SerializeField] private long estimatedDrawCallsAfterRuntimeCombining;
    [SerializeField] private int estimatedRendererIssuesAfterRuntimeCombining;
    [SerializeField] private long sceneRendererDrawCalls;
    [SerializeField] private long terrainSurfaceChunkCount;
    [SerializeField] private long terrainSurfaceDrawCalls;
    [SerializeField] private long terrainDetailChunkCount;
    [SerializeField] private long terrainDetailDrawCalls;
    [SerializeField] private long terrainTreeDrawCalls;
    [SerializeField] private int decalDrawCalls;
    [SerializeField] private long totalTextureMemory;
    [SerializeField] private long totalMeshMemory;
    [SerializeField] private long totalRuntimeCombinedMeshMemory;
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
    [SerializeField] private List<RuntimeMeshCombinerInfo> runtimeMeshCombinerInfos = new();
    [SerializeField] private List<TerrainInfo> terrainInfos = new();
    [SerializeField] private List<TextureInfo> textureInfos = new();
    [SerializeField] private bool showValidationSummary = true;
    [SerializeField] private bool showScoreBreakdown = true;
    [SerializeField] private bool showScanResultsSummary = true;
    [SerializeField] private bool showPostVolumeDetails = true;
    [SerializeField] private bool showTextureDetails = true;
    [SerializeField] private bool showTerrainDetails = true;
    [SerializeField] private bool showMeshDetails = true;
    [SerializeField] private bool showRuntimeMeshCombinerDetails = true;
    [SerializeField] private bool showShaderFragmentationDetails = true;
    [SerializeField] private bool showShaderBatchGroupDetails = true;
    [SerializeField] private bool showDecalDetails = true;
    [SerializeField] private bool showRendererIssueDetails = true;
    [SerializeField] private float performanceScore = 100f;
    [SerializeField] private float performanceScoreBeforeRuntimeCombining = 100f;
    [SerializeField] private float runtimeCombinerScoreGain;
    [SerializeField] private string performanceGrade = "A";
    [SerializeField] private Color scoreColor = Color.green;
    [SerializeField] private Vector2 scroll;
    [SerializeField] private bool hasScanResults;

    [NonSerialized] private string cachedTargetGameName;
    [NonSerialized] private Texture2D cachedTargetGameLogo;
    [NonSerialized] private GUIStyle gridCellStyle;
    [NonSerialized] private GUIStyle gridHeaderCellStyle;
    [NonSerialized] private GUIStyle reportFoldoutStyle;
    [NonSerialized] private bool shaderConversionQueued;
    [NonSerialized] private int repairedTemplateMaterialCount;
    [NonSerialized] private bool scanQueued;
    [NonSerialized] private bool scanRunning;
    [NonSerialized] private string scanStatus;
    [NonSerialized] private string scanError;

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
    private readonly HashSet<Material> decalMaterials = new();
    private readonly Dictionary<Mesh, MeshInfo> meshLookup = new();

    public void DrawGUI(bool embeddedInParentWindow = false)
    {
        EditorGUILayout.Space(embeddedInParentWindow ? 6f : 10f);
        DrawTargetGame();
        EditorGUILayout.Space(4f);
        EditorGUILayout.HelpBox(
            "Scan the loaded scene to review shared-memory usage, terrain density, unique meshes, shader fragmentation, decals, and renderers that are likely preventing batching.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            bool scanAvailable = !scanQueued && !scanRunning;
            string scanButtonLabel = scanRunning
                ? "Scanning Scene..."
                : scanQueued
                    ? "Starting Scan..."
                    : "Scan Scene";
            using (new EditorGUI.DisabledScope(!scanAvailable))
            {
                Rect scanRect = GUILayoutUtility.GetRect(
                    new GUIContent(scanButtonLabel),
                    GUI.skin.button,
                    GUILayout.Height(40f),
                    GUILayout.ExpandWidth(true));
                Event current = Event.current;
                bool directClick = scanAvailable
                    && current != null
                    && current.type == EventType.MouseDown
                    && current.button == 0
                    && scanRect.Contains(current.mousePosition);
                if (directClick)
                {
                    // SDK navigation uses this same direct MouseDown route. It is
                    // deliberately independent of IMGUI hotControl so a Scene tool
                    // that missed MouseUp cannot swallow this action.
                    GUIUtility.hotControl = 0;
                    GUIUtility.keyboardControl = 0;
                    GUI.changed = true;
                    current.Use();
                }

                bool buttonClicked = GUI.Button(scanRect, scanButtonLabel);
                if (directClick || buttonClicked)
                    QueueSceneScan();
            }

            using (new EditorGUI.DisabledScope(!hasScanResults))
            {
                if (GUILayout.Button("Save Text Report...", GUILayout.Height(40), GUILayout.Width(170f)))
                    SaveTextReport();
            }
        }

        EditorGUILayout.Space(10f);

        if (!string.IsNullOrWhiteSpace(scanError))
            EditorGUILayout.HelpBox(scanError, MessageType.Error);
        else if (!string.IsNullOrWhiteSpace(scanStatus))
            EditorGUILayout.HelpBox(scanStatus, scanRunning || scanQueued ? MessageType.Info : MessageType.None);

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

    private void QueueSceneScan()
    {
        if (scanQueued || scanRunning)
            return;

        scanQueued = true;
        scanError = null;
        scanStatus = "Performance scan queued...";
        EditorApplication.delayCall -= RunQueuedSceneScan;
        EditorApplication.delayCall += RunQueuedSceneScan;
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    private void RunQueuedSceneScan()
    {
        EditorApplication.delayCall -= RunQueuedSceneScan;
        if (!scanQueued || scanRunning)
            return;

        scanQueued = false;
        scanRunning = true;
        scanStatus = "Scanning the loaded scene...";
        scanError = null;
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

        try
        {
            MapPerformanceScanResult result = ScanScene();
            scanStatus = result != null
                ? "Performance scan complete."
                : "Performance scan cancelled.";
        }
        catch (Exception exception)
        {
            hasScanResults = false;
            scanStatus = null;
            scanError = $"Performance scan failed: {exception.Message}";
            Debug.LogException(exception);
        }
        finally
        {
            scanRunning = false;
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
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
            runtimeMeshCombinerInfos.Clear();
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
            decalMaterials.Clear();

            totalDrawCalls = 0;
            estimatedDrawCallsAfterRuntimeCombining = 0;
            estimatedRendererIssuesAfterRuntimeCombining = 0;
            sceneRendererDrawCalls = 0;
            terrainSurfaceChunkCount = 0;
            terrainSurfaceDrawCalls = 0;
            terrainDetailChunkCount = 0;
            terrainDetailDrawCalls = 0;
            terrainTreeDrawCalls = 0;
            decalDrawCalls = 0;
            totalTextureMemory = 0;
            totalMeshMemory = 0;
            totalRuntimeCombinedMeshMemory = 0;
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
            repairedTemplateMaterialCount = 0;
            lightProbeCount = 0;
            reflectionProbeCount = 0;
            postVolumeCount = 0;
            postVolumeProfileCount = 0;
            performanceScoreBeforeRuntimeCombining = 100f;
            runtimeCombinerScoreGain = 0f;

            CollectRenderers();
            CollectAdditionalMeshes();
            CollectRuntimeMeshCombiners();
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

            if (repairedTemplateMaterialCount > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log(
                    $"[MashBox] Performance scan repaired template state on " +
                    $"{repairedTemplateMaterialCount:N0} material{(repairedTemplateMaterialCount == 1 ? string.Empty : "s")}.");
            }

            hasScanResults = true;
            Debug.Log("[MashBox] Performance scan complete.");
            return new MapPerformanceScanResult
            {
                PerformanceScore = performanceScore,
                PerformanceScoreBeforeRuntimeCombining = performanceScoreBeforeRuntimeCombining,
                RuntimeCombinerScoreGain = runtimeCombinerScoreGain,
                DrawSubmissionsBeforeRuntimeCombining = totalDrawCalls,
                DrawSubmissionsAfterRuntimeCombining = estimatedDrawCallsAfterRuntimeCombining,
                SharedMemoryBytes = totalMapMemory,
                TextureMemoryBytes = totalTextureMemory,
                MeshMemoryBytes = totalMeshMemory,
                RuntimeCombinedMeshMemoryBytes = totalRuntimeCombinedMeshMemory,
                TerrainDataMemoryBytes = totalTerrainDataMemory,
                TerrainSplatMemoryBytes = totalTerrainSplatMemory,
                LightmapMemoryBytes = totalLightmapMemory,
                LightProbeMemoryBytes = totalLightProbeMemory,
                ReflectionProbeMemoryBytes = totalReflectionProbeMemory,
                PostVolumeMemoryBytes = totalPostVolumeTextureMemory + totalPostVolumeDataMemory,
                OversizedTextures = textureInfos
                    .Where(info => IsOversizedTexture(info.texture))
                    .Select(GetOversizedTextureDescription)
                    .ToList(),
                UnsupportedShaders = materialInfos
                    .Where(info => !info.isSupportedShader)
                    .Select(GetUnsupportedShaderDescription)
                    .Distinct()
                    .OrderBy(description => description)
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

    private void SaveTextReport()
    {
        if (!hasScanResults)
            return;

        var scene = SceneManager.GetActiveScene();
        var sceneName = string.IsNullOrWhiteSpace(scene.name) ? "Map" : scene.name;
        var safeSceneName = new string(sceneName
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)
            .ToArray());
        var defaultFileName = $"{safeSceneName}_PerformanceReport_{DateTime.Now:yyyy-MM-dd_HHmm}.txt";
        var savePath = EditorUtility.SaveFilePanel(
            "Save Map Performance Report",
            Application.dataPath,
            defaultFileName,
            "txt");

        if (string.IsNullOrWhiteSpace(savePath))
            return;

        try
        {
            File.WriteAllText(savePath, BuildTextReport(), new UTF8Encoding(false));
            Debug.Log($"[MashBox] Saved map performance report to '{savePath}'.");
            EditorUtility.RevealInFinder(savePath);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[MashBox] Could not save map performance report: {exception}");
            EditorUtility.DisplayDialog(
                "Could Not Save Report",
                $"The performance report could not be saved.\n\n{exception.Message}",
                "OK");
        }
    }

    private string BuildTextReport()
    {
        var report = new StringBuilder(16 * 1024);
        var scene = SceneManager.GetActiveScene();
        var selectedGameName = EditorPrefs.GetString("ModIo.CurrentGame", string.Empty);
        var targetGame = GameRegistry.Find(selectedGameName);

        void Section(string title)
        {
            report.AppendLine();
            report.AppendLine(title);
            report.AppendLine(new string('=', title.Length));
        }

        void Metric(string label, object value)
        {
            report.Append(label.PadRight(38));
            report.AppendLine(value?.ToString() ?? string.Empty);
        }

        report.AppendLine("MASHBOX MAP PERFORMANCE REPORT");
        report.AppendLine("==============================");
        Metric("Generated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        Metric("Scene", string.IsNullOrWhiteSpace(scene.path) ? scene.name : scene.path);
        Metric("Target Game", targetGame?.DisplayName ?? "Not selected");
        Metric("Texture Import Target", EditorUserBuildSettings.activeBuildTarget);
        Metric("Projected Runtime Score", $"{performanceScore:F1} ({performanceGrade})");
        Metric("Score Before Runtime Combining", $"{performanceScoreBeforeRuntimeCombining:F1}");
        Metric("Runtime Combiner Score Change", runtimeCombinerScoreGain.ToString("+0.0;-0.0;0.0"));

        Section("PUBLISHING SUMMARY");
        Metric("Estimated Shared Memory", FormatBytes(totalMapMemory));
        if (targetGame != null && targetGame.MapSharedMemoryBudgetBytes > 0)
        {
            var usage = totalMapMemory / (double)targetGame.MapSharedMemoryBudgetBytes * 100d;
            Metric("Target Shared-Memory Budget", FormatBytes(targetGame.MapSharedMemoryBudgetBytes));
            Metric("Budget Used", $"{usage:F1}%");
            Metric("Budget Status", totalMapMemory <= targetGame.MapSharedMemoryBudgetBytes ? "PASS" : "OVER BUDGET");
        }
        else
        {
            Metric("Target Shared-Memory Budget", "Unavailable - select a supported game");
        }

        var oversizedTextures = textureInfos.Where(info => IsOversizedTexture(info.texture)).ToList();
        Metric("Textures Above 4K", oversizedTextures.Count == 0 ? "0 - PASS" : $"{oversizedTextures.Count:N0} - VALIDATION ERROR");
        var unsupportedShaderMaterials = materialInfos.Where(info => !info.isSupportedShader).ToList();
        Metric(
            "Unsupported Shader Materials",
            unsupportedShaderMaterials.Count == 0 ? "0 - PASS" : $"{unsupportedShaderMaterials.Count:N0} - VALIDATION ERROR");

        Section("SCORE BREAKDOWN");
        Metric("Renderer Issues Before", rendererIssues.Count.ToString("N0"));
        Metric("Renderer Issues After Combining", $"{estimatedRendererIssuesAfterRuntimeCombining:N0} / 100 scoring threshold");
        Metric("Draw Submissions Before", totalDrawCalls.ToString("N0"));
        Metric("Draw Submissions After Combining", estimatedDrawCallsAfterRuntimeCombining.ToString("N0"));
        Metric("Estimated Draw Change", FormatDrawDifference(totalDrawCalls, estimatedDrawCallsAfterRuntimeCombining));
        Metric("Estimated Runtime Draw Submissions", $"{estimatedDrawCallsAfterRuntimeCombining:N0} (perfect at {DrawPerfectScoreThreshold:N0} or fewer; full penalty at {DrawMaximumPenaltyThreshold:N0})");
        Metric(
            "Decal Materials",
            $"{decalMaterials.Count:N0} (perfect with fewer than 2; full penalty at {DecalMaterialMaximumPenaltyThreshold:N0})");
        Metric(
            "Active Decal Projectors",
            $"{decalDrawCalls:N0} (perfect with fewer than 300; full penalty at {DecalProjectorMaximumPenaltyThreshold:N0})");
        Metric("Shader Variants", $"{batches.Count:N0} (perfect at {ShaderVariantPerfectScoreThreshold:N0} or fewer; full penalty at {ShaderVariantMaximumPenaltyThreshold:N0})");
        Metric("Texture Memory (Part of Shared Memory)", FormatBytes(totalTextureMemory));
        Metric("Unique Mesh Memory", $"{FormatBytes(totalMeshMemory)} ({meshInfos.Count:N0} meshes, {totalMeshUses:N0} uses)");
        Metric("Runtime Combined Mesh Memory", $"{FormatBytes(totalRuntimeCombinedMeshMemory)} ({runtimeMeshCombinerInfos.Count:N0} combiners)");
        var uniqueTerrainTextureMemory = textureInfos.Where(info => info.usedByTerrain).Sum(info => info.memoryBytes);
        var estimatedTerrainMemory = totalTerrainDataMemory + uniqueTerrainTextureMemory + totalTerrainLightmapMemory;
        Metric("Estimated Terrain Memory", $"{FormatBytes(estimatedTerrainMemory)} (within shared memory)");
        Metric("Terrain Data Memory", FormatBytes(totalTerrainDataMemory));
        Metric("Unique Terrain Texture Memory", FormatBytes(uniqueTerrainTextureMemory));
        Metric("Terrain Splat Map Memory", $"{FormatBytes(totalTerrainSplatMemory)} (included in texture memory)");
        Metric("Terrain Lightmap Memory", $"{FormatBytes(totalTerrainLightmapMemory)} (included in texture memory)");
        Metric("Estimated Shared Memory", $"{FormatBytes(totalMapMemory)} (perfect at {FormatBytes(SharedMemoryPerfectScoreBytes)} or less; full penalty at {FormatBytes(SharedMemoryMaximumPenaltyBytes)})");

        Section("HOW TO IMPROVE THE SCORE");
        foreach (var line in GetScoreImprovementGuidanceLines())
            report.AppendLine("- " + line);

        Section("SCENE AND RENDERING");
        Metric("Materials", materialInfos.Count.ToString("N0"));
        Metric("Shader Variants (Batches)", batches.Count.ToString("N0"));
        Metric("Estimated Draw Submissions", totalDrawCalls.ToString("N0"));
        Metric("Scene Renderer Submissions", sceneRendererDrawCalls.ToString("N0"));
        Metric("Terrain Surface Chunks", terrainSurfaceChunkCount.ToString("N0"));
        Metric("Terrain Surface Batch Submissions", terrainSurfaceDrawCalls.ToString("N0"));
        Metric("Estimated Visible Detail Chunk Groups", terrainDetailChunkCount.ToString("N0"));
        Metric("Terrain Detail Chunk Submissions", terrainDetailDrawCalls.ToString("N0"));
        Metric("Terrain Tree Batch Submissions", terrainTreeDrawCalls.ToString("N0"));
        Metric("Unique Decal Materials", decalMaterials.Count.ToString("N0"));
        Metric("Active Decal Projectors", decalDrawCalls.ToString("N0"));

        Section("MEMORY SUMMARY");
        Metric("Texture Memory", FormatBytes(totalTextureMemory));
        Metric("Unique Mesh Memory", $"{FormatBytes(totalMeshMemory)} ({meshInfos.Count:N0} meshes, {totalMeshUses:N0} uses)");
        Metric("Runtime Combined Mesh Memory", $"{FormatBytes(totalRuntimeCombinedMeshMemory)} ({runtimeMeshCombinerInfos.Count:N0} combiners)");
        Metric("Terrain Data Memory", FormatBytes(totalTerrainDataMemory));
        Metric("Terrain Splat Map Memory", FormatBytes(totalTerrainSplatMemory));
        Metric("All Lightmap Memory", FormatBytes(totalLightmapMemory));
        Metric("Terrain Lightmap Memory", FormatBytes(totalTerrainLightmapMemory));
        Metric("Light Probe Data", $"{FormatBytes(totalLightProbeMemory)} ({lightProbeCount:N0} probes)");
        Metric("Reflection Probe Memory", $"{FormatBytes(totalReflectionProbeMemory)} ({reflectionProbeCount:N0} probes)");
        Metric("Post Volume Texture Memory", FormatBytes(totalPostVolumeTextureMemory));
        Metric("Post Volume Profile Data", FormatBytes(totalPostVolumeDataMemory));
        Metric("Estimated Shared Memory", FormatBytes(totalMapMemory));

        Section("TERRAIN SUMMARY");
        Metric("Terrain Count", terrainInfos.Count.ToString("N0"));
        Metric("Detail Instances", totalDetailInstances.ToString("N0"));
        Metric("Tree Instances", totalTreeInstances.ToString("N0"));
        Metric("Unique Prototype Materials", terrainPrototypeMaterialCount.ToString("N0"));
        foreach (var info in terrainInfos.OrderByDescending(item => item.terrainDataMemory + item.textureMemory))
        {
            var drawState = info.contributesDrawSubmissions ? "active" : "disabled/inactive - zero draws";
            report.AppendLine(
                $"- {info.terrain?.name ?? "Missing Terrain"}: TerrainData {FormatBytes(info.terrainDataMemory)}, " +
                $"textures {FormatBytes(info.textureMemory)}, height {info.heightmapResolution}, splat layers {info.alphamapLayers}, " +
                $"details {info.detailInstanceCount:N0}/{info.detailPrototypeCount:N0} prototypes, " +
                $"detail grid {info.detailPatchesPerAxis:N0}x{info.detailPatchesPerAxis:N0}, visible budget/prototype {info.visibleDetailChunkBudgetPerPrototype:N0}, " +
                $"trees {info.treeInstanceCount:N0}/{info.treePrototypeCount:N0} prototypes, " +
                $"surface chunks/batches {info.surfaceChunkCount:N0}/{info.surfaceDrawSubmissions:N0}, " +
                $"detail chunks occupied/visible/submissions {info.occupiedDetailChunkCount:N0}/{info.detailChunkCount:N0}/{info.detailDrawSubmissions:N0}, " +
                $"tree batches {info.treeDrawSubmissions:N0}, " +
                $"surface instancing {(info.usesInstancedTerrain ? "on" : "off")}, {drawState}");
        }

        Section($"TEXTURES BY MEMORY ({textureInfos.Count:N0} UNIQUE)");
        if (textureInfos.Count == 0)
            report.AppendLine("None");
        foreach (var info in textureInfos.Where(info => info.texture != null))
        {
            var oversizedLabel = IsOversizedTexture(info.texture) ? "VALIDATION ERROR >4K | " : string.Empty;
            var location = string.IsNullOrWhiteSpace(info.assetPath) ? "runtime/generated" : info.assetPath;
            report.AppendLine(
                $"- {oversizedLabel}{info.texture.name}: {FormatBytes(info.memoryBytes)} | " +
                $"{info.texture.width:N0} x {info.texture.height:N0} | {info.texture.GetType().Name} | " +
                $"{GetTextureSourceDescription(info)} | {location}");
        }

        Section($"LARGEST UNIQUE MESHES ({meshInfos.Count:N0} TOTAL)");
        if (meshInfos.Count == 0)
            report.AppendLine("None");
        foreach (var info in meshInfos.OrderByDescending(item => item.memoryBytes))
        {
            report.AppendLine(
                $"- {info.mesh?.name ?? "Missing Mesh"}: {FormatBytes(info.memoryBytes)} | " +
                $"{(info.mesh != null ? info.mesh.vertexCount : 0):N0} vertices | scene uses {info.sceneUses:N0}, " +
                $"terrain instance uses {info.terrainUses:N0}, collider uses {info.colliderUses:N0}");
        }

        Section($"RUNTIME MESH COMBINERS ({runtimeMeshCombinerInfos.Count:N0})");
        Metric("Total Estimated Combined Memory", FormatBytes(totalRuntimeCombinedMeshMemory));
        if (runtimeMeshCombinerInfos.Count == 0)
            report.AppendLine("None");
        foreach (var info in runtimeMeshCombinerInfos.OrderByDescending(item => item.estimatedMemoryBytes))
        {
            var combinerPath = info.combiner != null ? GetTransformPath(info.combiner.transform) : "Missing RuntimeMeshCombiner";
            var trigger = info.combineOnAwake ? "Awake" : "Manual";
            var sourceState = info.disableSourceRenderers ? "sources disabled" : "sources retained";
            var runtimeDraws = !info.executesInCurrentState
                ? info.sourceDrawSubmissions
                : info.disableSourceRenderers
                    ? info.combinedDrawSubmissions
                    : info.sourceDrawSubmissions + info.combinedDrawSubmissions;
            var drawChange = GetDrawChangeDescription(info);
            var activeState = info.executesInCurrentState ? "active" : "disabled/inactive - zero combined draws";
            var collider = info.addMeshCollider ? ", adds MeshCollider" : string.Empty;
            var unreadable = info.skippedUnreadableMeshCount > 0
                ? $", skipped unreadable meshes {info.skippedUnreadableMeshCount:N0}"
                : string.Empty;
            report.AppendLine(
                $"- {combinerPath}: {FormatBytes(info.estimatedMemoryBytes)} " +
                $"(vertices {FormatBytes(info.estimatedVertexBytes)}, indices {FormatBytes(info.estimatedIndexBytes)}) | " +
                $"estimated output vertices {info.estimatedOutputVertices:N0}, indices {info.indexCount:N0}, " +
                $"materials {info.materialCount:N0}, source meshes {info.sourceMeshCount:N0}, " +
                $"source submeshes {info.sourceSubMeshCount:N0}, draws {info.sourceDrawSubmissions:N0} -> " +
                $"{runtimeDraws:N0} " +
                $"({drawChange}), {activeState}, trigger {trigger}, {sourceState}{collider}{unreadable}");
        }

        Section("SHADER FRAGMENTATION");
        report.AppendLine(
            $"Supported shaders are shaders supplied by the MashBox SDK package, Unity's {SupportedTerrainShaderName} terrain shader, and template-enforced {SupportedDecalShaderName} decal shader. Any other shader is a publishing error.");
        report.AppendLine(
            "A shader batch group requires the same shader, enabled shader keywords, and Lightmap ID. " +
            "Using the same shader on different Lightmap IDs creates separate batch groups because the renderers sample different lightmap textures.");
        report.AppendLine(
            "Tip: Where practical, place objects using the same shader onto one lightmap atlas. When using Bakery, consolidate lightmap groups where possible; a well-packed single 4K lightmap is often sufficient for a typical map, provided texel density and visual quality remain acceptable.");
        report.AppendLine();
        var shaderGroups = materialInfos.GroupBy(info => info.shader).OrderByDescending(group => group.Count()).ToList();
        if (shaderGroups.Count == 0)
            report.AppendLine("None");
        foreach (var group in shaderGroups)
        {
            var variants = group.Select(info => string.Join(";", info.keywords)).Distinct().Count();
            var supported = group.All(info => info.isSupportedShader);
            var status = supported ? "SUPPORTED" : "ERROR - UNSUPPORTED SHADER";
            var shaderPath = group.Select(info => info.shaderAssetPath).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
            report.AppendLine(
                $"- [{status}] {group.Key?.name ?? "Missing Shader"}: {variants:N0} variants, " +
                $"{group.Count():N0} materials" +
                (string.IsNullOrWhiteSpace(shaderPath) ? string.Empty : $" | {shaderPath}"));
        }

        Section("TOP SHADER BATCH GROUPS");
        var worstBatches = batches.OrderByDescending(batch => batch.Value.Count).Take(20).ToList();
        if (worstBatches.Count == 0)
            report.AppendLine("None");
        foreach (var batch in worstBatches)
        {
            var supported = batch.Value.All(info => info.isSupportedShader);
            report.AppendLine(
                $"- [{(supported ? "SUPPORTED" : "ERROR - UNSUPPORTED SHADER")}] " +
                $"{batch.Key.shader?.name ?? "Missing Shader"} | Lightmap ID: {FormatLightmapId(batch.Key.lightmapIndex)} | " +
                $"Materials in group: {batch.Value.Count:N0} | Shader keywords: " +
                $"{(string.IsNullOrWhiteSpace(batch.Key.keywordSignature) ? "<none>" : batch.Key.keywordSignature)}");
        }

        Section($"DECALS ({decalMaterials.Count:N0} materials, {decalDrawCalls:N0} active projectors)");
        var decals = materialInfos.Where(info => info.material != null && decalMaterials.Contains(info.material)).ToList();
        if (decals.Count == 0)
            report.AppendLine("None");
        foreach (var decal in decals)
        {
            var materialPath = decal.material != null ? AssetDatabase.GetAssetPath(decal.material) : string.Empty;
            report.AppendLine($"- {decal.material?.name ?? "Missing Material"} ({materialPath})");
        }

        Section($"NON-BATCHING RENDERERS ({rendererIssues.Count:N0})");
        if (rendererIssues.Count == 0)
            report.AppendLine("None");
        foreach (var issue in rendererIssues)
        {
            var rendererPath = issue.renderer != null ? GetTransformPath(issue.renderer.transform) : "Missing Renderer";
            var materials = string.Join(", ", issue.materials.Where(material => material != null).Select(material => material.name));
            report.AppendLine($"- {rendererPath}: {issue.reason} | Materials: {materials}");
        }

        report.AppendLine();
        report.AppendLine("NOTE");
        report.AppendLine("====");
        report.AppendLine(
            "Estimated Draw Submissions is a static runtime estimate. Terrain detail chunks are bounded by their configured draw distance; " +
            "actual per-frame draw calls still vary with camera location, terrain LOD, visibility, static batching, and GPU instancing.");
        report.AppendLine(
            $"Terrain surface and trees use instance batches of up to {MaximumInstancesPerBatch:N0} items when applicable. " +
            "Terrain detail density is grouped by occupied spatial detail chunk and prototype, then capped to the chunks that can fit inside the configured detail draw radius. Individual density instances inside a chunk do not add submissions. These estimated submissions feed directly into the performance score.");
        report.AppendLine(
            "Runtime combined mesh memory is an estimate of the generated vertex and index buffers. " +
            "Source mesh assets remain included separately because RuntimeMeshCombiner disables renderers but does not unload their meshes.");
        report.AppendLine(
            "If a combiner adds a MeshCollider, the collider shares the generated Mesh; platform-specific physics cooking overhead is not included in this editor estimate.");
        report.AppendLine(
            "Disabled components and inactive GameObjects contribute zero estimated draw submissions. Active manual RuntimeMeshCombiners are assumed to execute; the estimate uses one combined draw submission per non-null material and removes source draws only when Disable Source Renderers is enabled.");

        return report.ToString();
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
            var contributesDraws = RendererContributesDrawSubmissions(renderer);
            sceneRendererDrawCalls += EstimateRendererDrawSubmissions(renderer);
            CollectRendererMesh(renderer, 1, false);

            foreach (var mat in mats)
                CollectMaterial(mat, false, renderer.lightmapIndex);

            if (!contributesDraws || mats.Count <= 1)
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
            var contributesDraws = BehaviourContributesDrawSubmissions(terrain);
            var surfaceChunks = EstimateTerrainSurfaceChunkCount(data);
            var surfaceSubmissions = contributesDraws
                ? EstimateTerrainSurfaceDrawSubmissions(data, terrain.drawInstanced)
                : 0L;
            var visibleDetailChunkBudget = EstimateVisibleTerrainDetailChunkBudget(terrain, data);
            if (contributesDraws)
            {
                terrainSurfaceChunkCount += surfaceChunks;
                terrainSurfaceDrawCalls += surfaceSubmissions;
            }
            var info = new TerrainInfo
            {
                terrain = terrain,
                terrainData = data,
                terrainDataMemory = Math.Max(0L, Profiler.GetRuntimeMemorySizeLong(data)),
                heightmapResolution = data.heightmapResolution,
                alphamapLayers = data.alphamapLayers,
                detailPrototypeCount = detailPrototypes.Length,
                detailPatchesPerAxis = data.detailPatchCount,
                visibleDetailChunkBudgetPerPrototype = visibleDetailChunkBudget,
                treePrototypeCount = treePrototypes.Length,
                treeInstanceCount = data.treeInstanceCount,
                surfaceChunkCount = surfaceChunks,
                surfaceDrawSubmissions = surfaceSubmissions,
                usesInstancedTerrain = terrain.drawInstanced,
                contributesDrawSubmissions = contributesDraws
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

                var detailStats = GetDetailLayerStats(data, layer);
                info.detailInstanceCount += detailStats.instanceCount;
                info.occupiedDetailChunkCount += detailStats.occupiedChunkCount;
                var estimatedVisibleChunks = Math.Min(
                    detailStats.occupiedChunkCount,
                    visibleDetailChunkBudget);
                info.detailChunkCount += estimatedVisibleChunks;
                if (contributesDraws && estimatedVisibleChunks > 0)
                {
                    var submissions = EstimateTerrainDetailDrawSubmissions(
                        detailPrototypes[layer],
                        estimatedVisibleChunks);
                    info.detailDrawSubmissions += submissions;
                    terrainDetailChunkCount += estimatedVisibleChunks;
                    terrainDetailDrawCalls += submissions;
                }

                var prototype = detailPrototypes[layer];
                if (prototype.prototype != null)
                    CollectPrototypeResources(prototype.prototype, detailStats.instanceCount);

                if (prototype.prototypeTexture != null)
                    terrainTextures.Add(prototype.prototypeTexture);
            }

            var treeCounts = CountTreeInstances(data, treePrototypes.Length);
            for (var index = 0; index < treePrototypes.Length; index++)
            {
                if (treePrototypes[index].prefab != null)
                {
                    CollectPrototypeResources(treePrototypes[index].prefab, treeCounts[index]);
                    if (contributesDraws)
                    {
                        var submissions = EstimateInstancedPrototypeDrawSubmissions(
                            treePrototypes[index].prefab,
                            treeCounts[index]);
                        info.treeDrawSubmissions += submissions;
                        terrainTreeDrawCalls += submissions;
                    }
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

    private static (long instanceCount, long occupiedChunkCount) GetDetailLayerStats(TerrainData data, int layer)
    {
        try
        {
            var density = data.GetDetailLayer(0, 0, data.detailWidth, data.detailHeight, layer);
            var rowCount = density.GetLength(0);
            var columnCount = density.GetLength(1);
            var chunkColumns = Math.Max(1, data.detailPatchCount);
            var chunkRows = Math.Max(1, data.detailPatchCount);
            var occupiedChunks = new HashSet<int>();
            long instanceCount = 0;

            for (var row = 0; row < rowCount; row++)
            {
                for (var column = 0; column < columnCount; column++)
                {
                    var value = density[row, column];
                    if (value <= 0)
                        continue;

                    instanceCount += value;
                    var chunkRow = Math.Min(chunkRows - 1, row * chunkRows / Math.Max(1, rowCount));
                    var chunkColumn = Math.Min(chunkColumns - 1, column * chunkColumns / Math.Max(1, columnCount));
                    var chunkIndex = chunkRow * chunkColumns + chunkColumn;
                    occupiedChunks.Add(chunkIndex);
                }
            }

            return (instanceCount, occupiedChunks.Count);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MashBox] Could not read detail layer {layer} from {data.name}: {exception.Message}");
            return (0L, 0L);
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

    private void CollectRuntimeMeshCombiners()
    {
        var combiners = FindSceneObjects<RuntimeMeshCombiner>();
        for (var combinerIndex = 0; combinerIndex < combiners.Length; combinerIndex++)
        {
            var combiner = combiners[combinerIndex];
            if (combiner == null || IsUnderChallengesRoot(combiner.transform))
                continue;

            if (ShouldReportProgress(combinerIndex, combiners.Length))
            {
                ReportProgress(
                    $"Estimating runtime mesh combiner {combinerIndex + 1:N0} of {combiners.Length:N0}: {combiner.name}",
                    0.40f);
            }

            var serializedCombiner = new SerializedObject(combiner);
            var includeInactiveChildren = GetSerializedBool(serializedCombiner, "includeInactiveChildren");
            var includeDisabledRenderers = GetSerializedBool(serializedCombiner, "includeDisabledRenderers");
            var includeRootRenderer = GetSerializedBool(serializedCombiner, "includeRootRenderer");
            var combinedObjectName = GetSerializedString(serializedCombiner, "combinedObjectName", "Combined Mesh");
            var info = new RuntimeMeshCombinerInfo
            {
                combiner = combiner,
                executesInCurrentState = combiner.isActiveAndEnabled,
                combineOnAwake = GetSerializedBool(serializedCombiner, "combineOnAwake", true),
                disableSourceRenderers = GetSerializedBool(serializedCombiner, "disableSourceRenderers", true),
                addMeshCollider = GetSerializedBool(serializedCombiner, "addMeshCollider")
            };
            var materials = new HashSet<Material>();
            long runtimeVertexThresholdCount = 0;

            foreach (var meshFilter in combiner.GetComponentsInChildren<MeshFilter>(includeInactiveChildren))
            {
                if (meshFilter == null || meshFilter.sharedMesh == null)
                    continue;
                if (!includeRootRenderer && meshFilter.transform == combiner.transform)
                    continue;
                if (meshFilter.transform.parent == combiner.transform &&
                    string.Equals(meshFilter.name, combinedObjectName, StringComparison.Ordinal))
                {
                    continue;
                }

                var owningCombiner = meshFilter.GetComponentInParent<RuntimeMeshCombiner>();
                if (owningCombiner != null && owningCombiner != combiner)
                    continue;

                var meshRenderer = meshFilter.GetComponent<MeshRenderer>();
                if (meshRenderer == null || (!includeDisabledRenderers && !meshRenderer.enabled))
                    continue;

                var mesh = meshFilter.sharedMesh;
                if (!mesh.isReadable)
                {
                    info.skippedUnreadableMeshCount++;
                    continue;
                }

                var materialsForRenderer = meshRenderer.sharedMaterials;
                var usedMesh = false;
                var vertexStride = GetTotalVertexStride(mesh);
                for (var subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
                {
                    var material = subMeshIndex < materialsForRenderer.Length
                        ? materialsForRenderer[subMeshIndex]
                        : null;
                    materials.Add(material);

                    var subMeshIndexCount = (long)mesh.GetIndexCount(subMeshIndex);
                    info.indexCount += subMeshIndexCount;
                    info.estimatedOutputVertices += GetSubMeshReferencedVertexCount(mesh, subMeshIndex);
                    info.sourceSubMeshCount++;
                    usedMesh = true;
                }

                if (!usedMesh)
                    continue;

                info.estimatedVertexBytes += GetEstimatedCombinedVertexBytes(mesh, vertexStride);
                runtimeVertexThresholdCount += mesh.vertexCount;
                info.sourceMeshCount++;
                info.sourceDrawSubmissions += EstimateRendererDrawSubmissions(meshRenderer);
                if (rendererIssues.Any(issue => issue.renderer == meshRenderer))
                    info.sourceRendererIssueCount++;
            }

            info.materialCount = materials.Count;
            var outputIndexSize = runtimeVertexThresholdCount > 65535 ? 4L : 2L;
            info.estimatedIndexBytes = info.indexCount * outputIndexSize;

            // CombineMeshes can duplicate vertices shared by different material submeshes. Scale the
            // source vertex data by the measured output/source vertex ratio to account for that.
            if (runtimeVertexThresholdCount > 0 && info.estimatedOutputVertices > 0)
            {
                info.estimatedVertexBytes = (long)Math.Ceiling(
                    info.estimatedVertexBytes * (info.estimatedOutputVertices / (double)runtimeVertexThresholdCount));
            }

            info.estimatedMemoryBytes = info.estimatedVertexBytes + info.estimatedIndexBytes;
            if (info.sourceMeshCount > 0 && info.indexCount > 0 && info.estimatedOutputVertices > 0)
            {
                var drawableMaterials = materials.Where(material => material != null).ToList();
                info.combinedDrawSubmissions = drawableMaterials.Count;
                info.combinedRendererIssueCount = drawableMaterials
                    .Select(GetVariantKey)
                    .Distinct()
                    .Take(2)
                    .Count() > 1
                    ? 1
                    : 0;
            }
            runtimeMeshCombinerInfos.Add(info);
        }
    }

    private static bool GetSerializedBool(SerializedObject serializedObject, string propertyName, bool fallback = false)
    {
        var property = serializedObject.FindProperty(propertyName);
        return property != null ? property.boolValue : fallback;
    }

    private static string GetSerializedString(SerializedObject serializedObject, string propertyName, string fallback)
    {
        var property = serializedObject.FindProperty(propertyName);
        return property != null && !string.IsNullOrEmpty(property.stringValue) ? property.stringValue : fallback;
    }

    private static int GetTotalVertexStride(Mesh mesh)
    {
        if (mesh == null)
            return 0;

        var stride = 0;
        for (var stream = 0; stream < mesh.vertexBufferCount; stream++)
            stride += mesh.GetVertexBufferStride(stream);

        return stride;
    }

    private static long GetEstimatedCombinedVertexBytes(Mesh mesh, int vertexStride)
    {
        if (mesh == null)
            return 0;

        if (vertexStride > 0)
            return (long)mesh.vertexCount * vertexStride;

        return Math.Max(0L, Profiler.GetRuntimeMemorySizeLong(mesh));
    }

    private static long GetSubMeshReferencedVertexCount(Mesh mesh, int subMeshIndex)
    {
        try
        {
            return new HashSet<int>(mesh.GetIndices(subMeshIndex)).Count;
        }
        catch
        {
            // A readable mesh should expose indices, but retain a conservative estimate if Unity
            // rejects an unusual topology or imported mesh layout.
            return mesh != null ? mesh.vertexCount : 0;
        }
    }

    private static long GetEstimatedDrawSavings(RuntimeMeshCombinerInfo info)
    {
        if (info == null || !info.executesInCurrentState)
            return 0;

        return info.disableSourceRenderers
            ? info.sourceDrawSubmissions - info.combinedDrawSubmissions
            : -info.combinedDrawSubmissions;
    }

    private static string GetDrawChangeDescription(RuntimeMeshCombinerInfo info)
    {
        var savings = GetEstimatedDrawSavings(info);
        if (savings > 0)
            return $"saves {savings:N0}";
        if (savings < 0)
            return $"adds {-savings:N0}";

        return "no draw change";
    }

    private static string FormatDrawDifference(long before, long after)
    {
        var savings = before - after;
        if (savings > 0)
            return $"{savings:N0} fewer";
        if (savings < 0)
            return $"{-savings:N0} more";

        return "No change";
    }

    private static string FormatLightmapId(int lightmapIndex)
    {
        return lightmapIndex < 0
            ? "Not lightmapped (-1)"
            : lightmapIndex.ToString();
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
        if (!RendererContributesDrawSubmissions(renderer))
            return 0;

        // Each populated material slot can submit its corresponding mesh submesh. Extra material
        // slots also submit another pass over the last submesh, so count the actual populated slots.
        return renderer.sharedMaterials.LongCount(material => material != null);
    }

    private static bool RendererContributesDrawSubmissions(Renderer renderer)
    {
        return renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy;
    }

    private static bool BehaviourContributesDrawSubmissions(Behaviour behaviour)
    {
        return behaviour != null && behaviour.enabled && behaviour.gameObject.activeInHierarchy;
    }

    private static long EstimateTerrainSurfaceChunkCount(TerrainData data)
    {
        if (data == null)
            return 0;

        // Terrain visibility and LOD are camera dependent. A 64x64 heightmap region gives us a
        // stable maximum-detail chunk estimate that can then be grouped by the active renderer.
        const int estimatedPatchResolution = 64;
        var patchesPerAxis = Math.Max(1L, (data.heightmapResolution - 1L + estimatedPatchResolution - 1L) / estimatedPatchResolution);
        return patchesPerAxis * patchesPerAxis;
    }

    private static long EstimateTerrainSurfaceDrawSubmissions(TerrainData data, bool usesInstancing)
    {
        if (data == null)
            return 0;

        var chunkCount = EstimateTerrainSurfaceChunkCount(data);
        var layerPasses = Math.Max(1L, (data.alphamapLayers + 3L) / 4L);
        var surfaceBatches = usesInstancing ? CalculateInstanceBatchCount(chunkCount) : chunkCount;
        return surfaceBatches * layerPasses;
    }

    private static long EstimateVisibleTerrainDetailChunkBudget(Terrain terrain, TerrainData data)
    {
        if (terrain == null || data == null || terrain.detailObjectDistance <= 0f)
            return 0L;

        var chunkColumns = Math.Max(1L, data.detailPatchCount);
        var chunkRows = Math.Max(1L, data.detailPatchCount);
        var chunkWidth = Math.Max(0.01d, data.size.x / chunkColumns);
        var chunkDepth = Math.Max(0.01d, data.size.z / chunkRows);
        var radiusInChunkColumns = terrain.detailObjectDistance / chunkWidth;
        var radiusInChunkRows = terrain.detailObjectDistance / chunkDepth;

        // Detail distance is radial around the camera. The ellipse area gives a stable upper
        // estimate of the number of spatial chunks that can be visible simultaneously.
        var visibleChunkArea = (long)Math.Ceiling(
            Math.PI * radiusInChunkColumns * radiusInChunkRows);
        return Math.Min(chunkColumns * chunkRows, Math.Max(1L, visibleChunkArea));
    }

    private static long EstimateTerrainDetailDrawSubmissions(
        DetailPrototype prototype,
        long occupiedChunkCount)
    {
        if (prototype == null || occupiedChunkCount <= 0)
            return 0;

        var prototypePasses = prototype.prototype != null
            ? Math.Max(1L, prototype.prototype.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => !IsEditorOnly(renderer.transform))
                .Sum(renderer => (long)renderer.sharedMaterials.Count(material => material != null)))
            : 1L;

        // Terrain details are submitted by spatial detail chunks. Density values inside a chunk
        // change its instance population, not the number of chunk/prototype submissions.
        return occupiedChunkCount * prototypePasses;
    }

    private static long EstimateInstancedPrototypeDrawSubmissions(GameObject prefab, long instanceCount)
    {
        if (prefab == null || instanceCount <= 0)
            return 0;

        var batchesForInstances = CalculateInstanceBatchCount(instanceCount);
        var prototypePasses = prefab.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => !IsEditorOnly(renderer.transform))
            .Sum(renderer => (long)renderer.sharedMaterials.Count(material => material != null));

        return batchesForInstances * Math.Max(1L, prototypePasses);
    }

    private static long CalculateInstanceBatchCount(long instanceCount)
    {
        return instanceCount <= 0
            ? 0L
            : (instanceCount + MaximumInstancesPerBatch - 1L) / MaximumInstancesPerBatch;
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
                decalMaterials.Add(decal.material);
                if (BehaviourContributesDrawSubmissions(decal))
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

        // SDK shaders have a template-defined keyword contract. Repair stale imported materials
        // before recording variants so the report reflects the state they will use at runtime.
        if (MashBoxSDK.Shaders.ShaderEnforcer.SynchronizeTemplateState(mat))
            repairedTemplateMaterialCount++;

        var keywords = mat.enabledKeywords
            .Select(k => k.name)
            .OrderBy(k => k)
            .ToArray();
        var shaderAssetPath = mat.shader != null ? AssetDatabase.GetAssetPath(mat.shader) : string.Empty;

        materialInfos.Add(new MaterialInfo
        {
            material = mat,
            shader = mat.shader,
            keywords = keywords,
            textures = GetTextures(mat),
            isDecal = isDecal,
            lightmapIndex = lightmapIndex,
            isSupportedShader = IsSupportedShader(mat.shader, shaderAssetPath),
            shaderAssetPath = shaderAssetPath
        });
    }

    private static bool IsSupportedShader(Shader shader, string shaderAssetPath)
    {
        if (shader == null)
            return false;

        if (string.Equals(shader.name, SupportedTerrainShaderName, StringComparison.Ordinal))
            return true;

        if (string.Equals(shader.name, SupportedDecalShaderName, StringComparison.Ordinal))
            return true;

        if (string.IsNullOrWhiteSpace(shaderAssetPath))
            return false;

        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(shaderAssetPath);
        return packageInfo != null &&
               string.Equals(packageInfo.name, MashBoxPackageName, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUnsupportedShaderDescription(MaterialInfo info)
    {
        var shaderName = info?.shader != null ? info.shader.name : "Missing Shader";
        var materialName = info?.material != null ? info.material.name : "Missing Material";
        var materialPath = info?.material != null ? AssetDatabase.GetAssetPath(info.material) : string.Empty;
        return $"{shaderName} used by {materialName}" +
               (string.IsNullOrWhiteSpace(materialPath) ? string.Empty : $" ({materialPath})");
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
        totalDrawCalls = sceneRendererDrawCalls + terrainSurfaceDrawCalls + terrainDetailDrawCalls + terrainTreeDrawCalls + decalDrawCalls;
        estimatedDrawCallsAfterRuntimeCombining = totalDrawCalls;
        estimatedRendererIssuesAfterRuntimeCombining = rendererIssues.Count;

        foreach (var info in runtimeMeshCombinerInfos)
        {
            if (!info.executesInCurrentState ||
                info.sourceMeshCount == 0 || info.indexCount == 0 || info.estimatedOutputVertices == 0)
                continue;

            if (info.disableSourceRenderers)
            {
                estimatedDrawCallsAfterRuntimeCombining -= info.sourceDrawSubmissions;
                estimatedRendererIssuesAfterRuntimeCombining -= info.sourceRendererIssueCount;
            }

            estimatedDrawCallsAfterRuntimeCombining += info.combinedDrawSubmissions;
            estimatedRendererIssuesAfterRuntimeCombining += info.combinedRendererIssueCount;
        }

        estimatedDrawCallsAfterRuntimeCombining = Math.Max(0L, estimatedDrawCallsAfterRuntimeCombining);
        estimatedRendererIssuesAfterRuntimeCombining = Math.Max(0, estimatedRendererIssuesAfterRuntimeCombining);

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
        totalRuntimeCombinedMeshMemory = runtimeMeshCombinerInfos.Sum(info => info.estimatedMemoryBytes);
        totalMeshUses = meshInfos.Sum(info => info.sceneUses + info.terrainUses + info.colliderUses);
        terrainPrototypeMaterialCount = terrainPrototypeMaterials.Count;
        totalTerrainSplatMemory = textureInfos.Where(info => info.usedByTerrainSplat).Sum(info => info.memoryBytes);
        totalLightmapMemory = textureInfos.Where(info => info.usedByLightmap).Sum(info => info.memoryBytes);
        totalTerrainLightmapMemory = textureInfos.Where(info => info.usedByTerrainLightmap).Sum(info => info.memoryBytes);
        totalReflectionProbeMemory = textureInfos.Where(info => info.usedByReflectionProbe).Sum(info => info.memoryBytes);
        totalPostVolumeTextureMemory = textureInfos.Where(info => info.usedByPostVolume).Sum(info => info.memoryBytes);
        totalMapMemory = totalTextureMemory + totalMeshMemory + totalRuntimeCombinedMeshMemory +
                         totalTerrainDataMemory + totalLightProbeMemory + totalPostVolumeDataMemory;
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
        return MapPerformanceMaterialTextureCollector.GetTextures(mat);
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
        var memoryBeforeRuntimeCombining = Math.Max(0L, totalMapMemory - totalRuntimeCombinedMeshMemory);
        performanceScoreBeforeRuntimeCombining = CalculatePerformanceScoreValue(
            rendererIssues.Count,
            totalDrawCalls,
            memoryBeforeRuntimeCombining);
        performanceScore = CalculatePerformanceScoreValue(
            estimatedRendererIssuesAfterRuntimeCombining,
            estimatedDrawCallsAfterRuntimeCombining,
            totalMapMemory);
        runtimeCombinerScoreGain = performanceScore - performanceScoreBeforeRuntimeCombining;

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

    private float CalculatePerformanceScoreValue(int rendererIssueCount, long drawSubmissions, long mapMemoryBytes)
    {
        var rendererPenalty = Mathf.Clamp01(rendererIssueCount / 100f);
        var drawPenalty = Mathf.InverseLerp(DrawPerfectScoreThreshold, DrawMaximumPenaltyThreshold, drawSubmissions);
        var memoryPenalty = Mathf.InverseLerp(SharedMemoryPerfectScoreBytes, SharedMemoryMaximumPenaltyBytes, mapMemoryBytes);
        var decalMaterialPenalty = Mathf.InverseLerp(
            DecalMaterialPerfectScoreThreshold,
            DecalMaterialMaximumPenaltyThreshold,
            decalMaterials.Count);
        var decalProjectorPenalty = Mathf.InverseLerp(
            DecalProjectorPerfectScoreThreshold,
            DecalProjectorMaximumPenaltyThreshold,
            decalDrawCalls);
        var decalPenalty = (decalMaterialPenalty + decalProjectorPenalty) * 0.5f;
        var shaderPenalty = Mathf.InverseLerp(
            ShaderVariantPerfectScoreThreshold,
            ShaderVariantMaximumPenaltyThreshold,
            batches.Count);

        var penalty =
            rendererPenalty * 0.4f +
            drawPenalty * 0.2f +
            memoryPenalty * 0.15f +
            decalPenalty * 0.15f +
            shaderPenalty * 0.1f;

        return Mathf.Clamp(100f - penalty * 100f, 0f, 100f);
    }

    private void DrawValidationSummary()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var unsupportedShaderGroups = materialInfos
            .Where(info => !info.isSupportedShader)
            .GroupBy(info => info.shader)
            .OrderByDescending(group => group.Count())
            .ToList();
        foreach (var group in unsupportedShaderGroups)
        {
            var materialNames = group
                .Where(info => info.material != null)
                .Select(info => info.material.name)
                .Distinct()
                .Take(6)
                .ToList();
            var hiddenMaterialCount = Math.Max(0, group.Select(info => info.material).Distinct().Count() - materialNames.Count);
            var hiddenMaterials = hiddenMaterialCount > 0 ? $", and {hiddenMaterialCount:N0} more" : string.Empty;
            errors.Add(
                $"Unsupported shader: {group.Key?.name ?? "Missing Shader"}. " +
                $"Used by {group.Count():N0} material{(group.Count() == 1 ? string.Empty : "s")}: " +
                $"{(materialNames.Count > 0 ? string.Join(", ", materialNames) : "missing material")}{hiddenMaterials}. " +
                $"Use a MashBox SDK shader, {SupportedTerrainShaderName}, or template-enforced {SupportedDecalShaderName}.");
        }

        var oversizedTextures = textureInfos.Where(info => IsOversizedTexture(info.texture)).ToList();
        if (oversizedTextures.Count > 0)
        {
            var textureNames = string.Join(", ", oversizedTextures.Take(6).Select(info => info.texture.name));
            var hiddenTextureCount = oversizedTextures.Count - 6;
            var hiddenTextures = hiddenTextureCount > 0 ? $", and {hiddenTextureCount:N0} more" : string.Empty;
            errors.Add(
                $"{oversizedTextures.Count:N0} texture{(oversizedTextures.Count == 1 ? " is" : "s are")} above " +
                $"{MaximumTextureDimension:N0}px: {textureNames}{hiddenTextures}. Reduce the imported Max Size before publishing.");
        }

        var selectedGameName = EditorPrefs.GetString("ModIo.CurrentGame", string.Empty);
        var targetGame = GameRegistry.Find(selectedGameName);
        if (targetGame != null &&
            targetGame.MapSharedMemoryBudgetBytes > 0 &&
            totalMapMemory > targetGame.MapSharedMemoryBudgetBytes)
        {
            errors.Add(
                $"Shared memory is over the {targetGame.DisplayName} budget: " +
                $"{FormatBytes(totalMapMemory)} used / {FormatBytes(targetGame.MapSharedMemoryBudgetBytes)} allowed.");
        }

        if (performanceScore <= MinimumPublishPerformanceScore)
        {
            errors.Add(
                $"Performance score is {performanceScore:F0}. Publishing requires a score above " +
                $"{MinimumPublishPerformanceScore:F0}.");
        }

        var skippedUnreadableMeshes = runtimeMeshCombinerInfos.Sum(info => info.skippedUnreadableMeshCount);
        if (skippedUnreadableMeshes > 0)
        {
            warnings.Add(
                $"Runtime Mesh Combiner skipped {skippedUnreadableMeshes:N0} unreadable source " +
                $"mesh{(skippedUnreadableMeshes == 1 ? string.Empty : "es")}; its estimates are incomplete.");
        }

        var accentColor = errors.Count > 0
            ? new Color(0.58f, 0.12f, 0.12f)
            : warnings.Count > 0
                ? new Color(0.55f, 0.34f, 0.08f)
                : new Color(0.20f, 0.46f, 0.24f);
        var status = errors.Count > 0
            ? $"{errors.Count:N0} error{(errors.Count == 1 ? string.Empty : "s")}, {warnings.Count:N0} warning{(warnings.Count == 1 ? string.Empty : "s")}"
            : warnings.Count > 0
                ? $"0 errors, {warnings.Count:N0} warning{(warnings.Count == 1 ? string.Empty : "s")}"
                : "PASS";

        showValidationSummary = DrawReportFoldout(
            showValidationSummary,
            $"Validation Summary ({status})",
            accentColor,
            "ValidationSummary");
        if (!showValidationSummary)
            return;

        if (errors.Count == 0 && warnings.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No performance publishing validation errors were detected.",
                MessageType.Info);
            return;
        }

        foreach (var error in errors)
            EditorGUILayout.HelpBox("ERROR: " + error, MessageType.Error);
        foreach (var warning in warnings)
            EditorGUILayout.HelpBox("WARNING: " + warning, MessageType.Warning);
    }

    private void DrawScoreBreakdown()
    {
        showScoreBreakdown = DrawReportFoldout(
            showScoreBreakdown,
            "Score Breakdown",
            new Color(0.18f, 0.36f, 0.56f),
            "ScoreBreakdown");
        if (!showScoreBreakdown)
            return;

        DrawScoredMetric("Renderer Issues (Runtime)", estimatedRendererIssuesAfterRuntimeCombining, 0f, 100f);
        DrawScoredMetric(
            "Estimated Draw Submissions (Runtime)",
            estimatedDrawCallsAfterRuntimeCombining,
            DrawPerfectScoreThreshold,
            DrawMaximumPenaltyThreshold);
        DrawTableRow("Terrain Surface Chunks", terrainSurfaceChunkCount.ToString("N0"));
        DrawTableRow("Terrain Surface Batch Submissions", terrainSurfaceDrawCalls.ToString("N0"));
        DrawTableRow("Estimated Visible Detail Chunk Groups", terrainDetailChunkCount.ToString("N0"));
        DrawTableRow("Terrain Detail Chunk Submissions", terrainDetailDrawCalls.ToString("N0"));
        DrawTableRow("Terrain Tree Batch Submissions", terrainTreeDrawCalls.ToString("N0"));
        DrawScoredMetric(
            "Decal Materials",
            decalMaterials.Count,
            DecalMaterialPerfectScoreThreshold,
            DecalMaterialMaximumPenaltyThreshold);
        DrawScoredMetric(
            "Active Decal Projectors",
            decalDrawCalls,
            DecalProjectorPerfectScoreThreshold,
            DecalProjectorMaximumPenaltyThreshold);
        DrawScoredMetric(
            "Shader Variants",
            batches.Count,
            ShaderVariantPerfectScoreThreshold,
            ShaderVariantMaximumPenaltyThreshold);
        DrawTableRow("Texture Memory (part of shared memory)", FormatBytes(totalTextureMemory));
        DrawTableRow("Unique Mesh Memory (part of shared memory)", $"{FormatBytes(totalMeshMemory)} ({meshInfos.Count:N0} meshes, {totalMeshUses:N0} uses)");
        DrawTableRow(
            "Runtime Combined Mesh Memory (part of shared memory)",
            $"{FormatBytes(totalRuntimeCombinedMeshMemory)} ({runtimeMeshCombinerInfos.Count:N0} combiners)");
        var uniqueTerrainTextureMemory = textureInfos.Where(info => info.usedByTerrain).Sum(info => info.memoryBytes);
        var estimatedTerrainMemory = totalTerrainDataMemory + uniqueTerrainTextureMemory + totalTerrainLightmapMemory;
        DrawTableRow("Estimated Terrain Memory (within shared memory)", FormatBytes(estimatedTerrainMemory));
        DrawTableRow("Terrain Data Memory (part of shared memory)", FormatBytes(totalTerrainDataMemory));
        DrawTableRow("Unique Terrain Textures (part of shared memory)", FormatBytes(uniqueTerrainTextureMemory));
        DrawTableRow("Terrain Splat Maps (included in texture memory)", FormatBytes(totalTerrainSplatMemory));
        DrawTableRow("Terrain Lightmaps (included in texture memory)", FormatBytes(totalTerrainLightmapMemory));
        DrawScoredMetric(
            "Estimated Shared Memory",
            totalMapMemory,
            SharedMemoryPerfectScoreBytes,
            SharedMemoryMaximumPenaltyBytes,
            true);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("How to Increase This Score", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            string.Join("\n", GetScoreImprovementGuidanceLines().Select(line => "- " + line)),
            MessageType.Info);
    }

    private List<string> GetScoreImprovementGuidanceLines()
    {
        const float rendererMaxPoints = 40f;
        const float drawMaxPoints = 20f;
        const float memoryMaxPoints = 15f;
        const float decalMaxPoints = 15f;
        const float shaderMaxPoints = 10f;
        const float rendererThreshold = 100f;

        var nextTarget = performanceScore > 85f ? 100f : performanceScore > 70f ? 85.1f : performanceScore > 50f ? 70.1f : 50.1f;
        var nextGrade = performanceScore > 70f ? "A" : performanceScore > 50f ? "B" : "C";
        var milestone = performanceScore >= 99.95f
            ? "This scan receives a perfect 100; every modeled input is within its perfect-score allowance."
            : performanceScore > 85f
            ? $"You already have an A. There are {100f - performanceScore:F1} modeled points still available."
            : $"Reaching grade {nextGrade} needs about {nextTarget - performanceScore:F1} more points.";

        var opportunities = new List<(float pointsLost, string guidance)>
        {
            (
                rendererMaxPoints * Mathf.Clamp01(estimatedRendererIssuesAfterRuntimeCombining / rendererThreshold),
                $"Renderer issues cost {rendererMaxPoints * Mathf.Clamp01(estimatedRendererIssuesAfterRuntimeCombining / rendererThreshold):F1} points. " +
                "Use the Renderer Issues list to fix material/shader differences or combine compatible renderers; each issue removed below 100 recovers 0.4 points."),
            (
                drawMaxPoints * Mathf.InverseLerp(DrawPerfectScoreThreshold, DrawMaximumPenaltyThreshold, estimatedDrawCallsAfterRuntimeCombining),
                $"Draw submissions cost {drawMaxPoints * Mathf.InverseLerp(DrawPerfectScoreThreshold, DrawMaximumPenaltyThreshold, estimatedDrawCallsAfterRuntimeCombining):F1} points. " +
                $"The first {DrawPerfectScoreThreshold:N0} are free. Reduce visible renderers/material slots, terrain chunks, details, and trees, or use runtime mesh combining; every 90 submissions removed between {DrawPerfectScoreThreshold:N0} and {DrawMaximumPenaltyThreshold:N0} recovers 1 point."),
            (
                memoryMaxPoints * Mathf.InverseLerp(SharedMemoryPerfectScoreBytes, SharedMemoryMaximumPenaltyBytes, totalMapMemory),
                $"Shared memory costs {memoryMaxPoints * Mathf.InverseLerp(SharedMemoryPerfectScoreBytes, SharedMemoryMaximumPenaltyBytes, totalMapMemory):F1} points. " +
                "The first 1 GB is free. Lower texture import sizes, remove unused unique meshes, simplify mesh data, reduce terrain height/splat resolutions, and avoid unnecessary combined-mesh copies; about 137 MB removed between 1 GB and 3 GB recovers 1 point."),
            (
                decalMaxPoints * 0.5f *
                (Mathf.InverseLerp(DecalMaterialPerfectScoreThreshold, DecalMaterialMaximumPenaltyThreshold, decalMaterials.Count) +
                 Mathf.InverseLerp(DecalProjectorPerfectScoreThreshold, DecalProjectorMaximumPenaltyThreshold, decalDrawCalls)),
                $"Decals cost {decalMaxPoints * 0.5f * (Mathf.InverseLerp(DecalMaterialPerfectScoreThreshold, DecalMaterialMaximumPenaltyThreshold, decalMaterials.Count) + Mathf.InverseLerp(DecalProjectorPerfectScoreThreshold, DecalProjectorMaximumPenaltyThreshold, decalDrawCalls)):F1} points. " +
                "One unique decal material and up to 299 active Decal Projectors are free. Reuse decal materials and remove or disable unnecessary projectors."),
            (
                shaderMaxPoints * Mathf.InverseLerp(ShaderVariantPerfectScoreThreshold, ShaderVariantMaximumPenaltyThreshold, batches.Count),
                $"Shader variants cost {shaderMaxPoints * Mathf.InverseLerp(ShaderVariantPerfectScoreThreshold, ShaderVariantMaximumPenaltyThreshold, batches.Count):F1} points. " +
                $"The first {ShaderVariantPerfectScoreThreshold:N0} are free. Reuse materials with matching shaders and keywords; each excess variant removed up to {ShaderVariantMaximumPenaltyThreshold:N0} recovers about 1.7 points.")
        };

        var lines = new List<string> { milestone };
        lines.AddRange(opportunities
            .Where(opportunity => opportunity.pointsLost > 0.01f)
            .OrderByDescending(opportunity => opportunity.pointsLost)
            .Take(3)
            .Select(opportunity => opportunity.guidance));

        if (lines.Count == 1 && performanceScore < 99.95f)
            lines.Add("No points are currently being lost by the modeled score inputs.");

        return lines;
    }

    private void DrawScoredMetric(string label, float value, float perfectThreshold, float maximumPenaltyThreshold, bool isBytes = false)
    {
        var color = value <= perfectThreshold ? Color.green : value < maximumPenaltyThreshold ? Color.yellow : Color.red;
        var displayValue = isBytes ? FormatBytes((long)value) : value.ToString("N0");
        var perfectValue = isBytes ? FormatBytes((long)perfectThreshold) : perfectThreshold.ToString("N0");
        DrawTableRow(label, $"{displayValue} (perfect <= {perfectValue})", false, color);
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

    private void DrawCategorySummary(params (string label, string value)[] metrics)
    {
        DrawTableRow("Category Totals & Estimates", "Value", true);
        foreach (var metric in metrics)
            DrawTableRow(metric.label, metric.value);
        EditorGUILayout.Space(4f);
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
        DrawValidationSummary();
        DrawScoreBreakdown();
        EditorGUILayout.HelpBox(
            "Estimated Draw Submissions is a static runtime estimate. Actual per-frame draw calls vary with camera location, terrain LOD, visibility, static batching, and GPU instancing. " +
            $"Terrain surface and trees are grouped into batches of up to {MaximumInstancesPerBatch:N0} when applicable. Terrain detail density is grouped by occupied spatial chunk and prototype, capped by the configured detail draw radius, so individual instances inside a chunk do not multiply submissions. These estimates feed directly into the score.",
            MessageType.Info);
        EditorGUILayout.Space(10f);

        showScanResultsSummary = DrawReportFoldout(
            showScanResultsSummary,
            "Scan Results",
            new Color(0.18f, 0.40f, 0.46f),
            "ScanResultsOverview");
        if (showScanResultsSummary)
        {
            DrawTableRow("Metric", "Value", true);

        DrawTableSectionHeader("Scene & Rendering");
        DrawTableRow("Materials", materialInfos.Count.ToString("N0"));
        DrawTableRow("Shader Variants (Batches)", batches.Count.ToString("N0"));
        var unsupportedShaderMaterialCount = materialInfos.Count(info => !info.isSupportedShader);
        DrawTableRow(
            "Unsupported Shader Materials",
            unsupportedShaderMaterialCount == 0 ? "0 - PASS" : $"{unsupportedShaderMaterialCount:N0} - VALIDATION ERROR",
            false,
            unsupportedShaderMaterialCount == 0 ? Color.green : Color.red);
        DrawTableRow("Draw Submissions Before Combining", totalDrawCalls.ToString("N0"));
        DrawTableRow("Projected Runtime Draw Submissions", estimatedDrawCallsAfterRuntimeCombining.ToString("N0"));
        DrawTableRow(
            "Runtime Combiner Draw Change",
            FormatDrawDifference(totalDrawCalls, estimatedDrawCallsAfterRuntimeCombining));
        DrawTableRow("Scene Renderer Submissions", sceneRendererDrawCalls.ToString("N0"));
        DrawTableRow("Terrain Surface Chunks", terrainSurfaceChunkCount.ToString("N0"));
        DrawTableRow("Terrain Surface Batch Submissions", terrainSurfaceDrawCalls.ToString("N0"));
        DrawTableRow("Estimated Visible Detail Chunk Groups", terrainDetailChunkCount.ToString("N0"));
        DrawTableRow("Terrain Detail Chunk Submissions", terrainDetailDrawCalls.ToString("N0"));
        DrawTableRow("Terrain Tree Batch Submissions", terrainTreeDrawCalls.ToString("N0"));
        DrawTableRow("Unique Decal Materials", decalMaterials.Count.ToString("N0"));
        DrawTableRow("Active Decal Projectors", decalDrawCalls.ToString("N0"));
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
        DrawTableRow(
            "Runtime Combined Mesh Memory",
            $"{FormatBytes(totalRuntimeCombinedMeshMemory)} ({runtimeMeshCombinerInfos.Count:N0} combiners)");
        DrawTableRow("Estimated Shared Memory", FormatBytes(totalMapMemory), false, scoreColor);

        DrawTableSectionHeader("Terrain", true);
        DrawTableRow("Terrain Count", terrainInfos.Count.ToString("N0"));
        DrawTableRow("Terrain Data Memory", FormatBytes(totalTerrainDataMemory));
        DrawTableRow("Terrain Splat Map Memory", FormatBytes(totalTerrainSplatMemory));
        DrawTableRow("Terrain Lightmap Memory", FormatBytes(totalTerrainLightmapMemory));
        DrawTableRow("Detail Instances", totalDetailInstances.ToString("N0"));
        DrawTableRow("Tree Instances", totalTreeInstances.ToString("N0"));
        DrawTableRow("Surface Batch Submissions", terrainSurfaceDrawCalls.ToString("N0"));
        DrawTableRow("Estimated Visible Detail Chunk Groups", terrainDetailChunkCount.ToString("N0"));
        DrawTableRow("Detail Chunk Submissions", terrainDetailDrawCalls.ToString("N0"));
        DrawTableRow("Tree Batch Submissions", terrainTreeDrawCalls.ToString("N0"));
        DrawTableRow("Unique Prototype Materials", $"{terrainPrototypeMaterialCount:N0} (included in shader variants)");

        DrawTableSectionHeader("Lighting & Probes");
        DrawTableRow("All Lightmap Memory", FormatBytes(totalLightmapMemory));
        DrawTableRow("Light Probe Data", $"{FormatBytes(totalLightProbeMemory)} ({lightProbeCount:N0} probes)");
            DrawTableRow("Reflection Probe Memory", $"{FormatBytes(totalReflectionProbeMemory)} ({reflectionProbeCount:N0} probes)");
        }

        DrawPostVolumeUsage();

        GUILayout.Space(10f);
        DrawTextureUsage();

        GUILayout.Space(10f);
        DrawTerrainUsage();

        GUILayout.Space(10f);
        DrawMeshUsage();

        GUILayout.Space(10f);
        DrawRuntimeMeshCombinerUsage();

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
        showPostVolumeDetails = DrawReportFoldout(
            showPostVolumeDetails,
            $"Post Volume Usage ({postVolumeCount:N0} volumes, {postVolumeProfileCount:N0} profiles)",
            new Color(0.38f, 0.22f, 0.48f),
            "PostVolumes");
        if (!showPostVolumeDetails)
            return;

        DrawCategorySummary(
            ("Scene Volumes", postVolumeCount.ToString("N0")),
            ("Unique Profiles", postVolumeProfileCount.ToString("N0")),
            ("Texture Memory", FormatBytes(totalPostVolumeTextureMemory)),
            ("Profile Data Memory", FormatBytes(totalPostVolumeDataMemory)),
            ("Combined Post Volume Memory", FormatBytes(totalPostVolumeTextureMemory + totalPostVolumeDataMemory)));
    }

    private void DrawTextureUsage()
    {
        showTextureDetails = DrawReportFoldout(
            showTextureDetails,
            $"Textures by Memory ({textureInfos.Count:N0} unique)",
            new Color(0.16f, 0.36f, 0.55f),
            "Textures");

        if (!showTextureDetails)
            return;

        var oversizedTextureCount = textureInfos.Count(info => IsOversizedTexture(info.texture));
        DrawCategorySummary(
            ("Unique Textures", textureInfos.Count.ToString("N0")),
            ("Total Texture Memory", FormatBytes(totalTextureMemory)),
            ("Textures Above 4K", oversizedTextureCount == 0 ? "0 - PASS" : $"{oversizedTextureCount:N0} - VALIDATION ERROR"),
            ("Import Target", EditorUserBuildSettings.activeBuildTarget.ToString()));

        var columns = new[] { -108f, 1.7f, 0.8f, 0.9f, 0.8f, 1.5f, 2.5f };
        DrawDataGridHeader(
            new[] { "Actions", "Texture", "Memory", "Dimensions", "Type", "Used By", "Asset Location" },
            columns);

        var rowIndex = 0;
        foreach (var info in textureInfos)
        {
            if (info.texture == null)
                continue;

            var oversized = IsOversizedTexture(info.texture);
            var source = GetTextureSourceDescription(info);
            var location = string.IsNullOrEmpty(info.assetPath) ? "runtime/generated" : info.assetPath;
            var textureLabel = oversized ? $"VALIDATION ERROR >4K\n{info.texture.name}" : info.texture.name;
            DrawDataGridRow(
                new[]
                {
                    GUIContent.none,
                    new GUIContent(textureLabel, info.texture.name),
                    new GUIContent(FormatBytes(info.memoryBytes)),
                    new GUIContent($"{info.texture.width:N0} x {info.texture.height:N0}"),
                    new GUIContent(info.texture.GetType().Name),
                    new GUIContent(source),
                    new GUIContent(location, location)
                },
                columns,
                rowIndex++,
                false,
                rect => DrawGridButtons(
                    rect,
                    ("Select", () => Selection.activeObject = info.texture),
                    ("Ping", () => EditorGUIUtility.PingObject(info.texture))),
                oversized ? new Color(0.40f, 0.10f, 0.10f, 1f) : null);
        }
    }

    private bool DrawReportFoldout(bool expanded, string title, Color accentColor, string preferenceKey)
    {
        var fullPreferenceKey = FoldoutPreferencePrefix + preferenceKey;
        expanded = EditorPrefs.GetBool(fullPreferenceKey, expanded);
        var rect = EditorGUILayout.GetControlRect(false, 27f);
        var border = EditorGUIUtility.isProSkin
            ? new Color(0.38f, 0.38f, 0.38f, 1f)
            : new Color(0.48f, 0.48f, 0.48f, 1f);
        var background = EditorGUIUtility.isProSkin
            ? Color.Lerp(new Color(0.14f, 0.14f, 0.14f, 1f), accentColor, 0.72f)
            : Color.Lerp(new Color(0.94f, 0.94f, 0.94f, 1f), accentColor, 0.32f);
        if (rect.Contains(Event.current.mousePosition))
            background = Color.Lerp(background, Color.white, EditorGUIUtility.isProSkin ? 0.08f : 0.16f);

        EditorGUI.DrawRect(rect, border);
        var innerRect = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
        EditorGUI.DrawRect(innerRect, background);
        var foldoutRect = new Rect(innerRect.x + 5f, innerRect.y, innerRect.width - 10f, innerRect.height);
        var nextExpanded = EditorGUI.Foldout(foldoutRect, expanded, title, true, ReportFoldoutStyle);
        if (nextExpanded != expanded)
            EditorPrefs.SetBool(fullPreferenceKey, nextExpanded);
        return nextExpanded;
    }

    private GUIStyle ReportFoldoutStyle => reportFoldoutStyle ??= CreateReportFoldoutStyle();

    private static GUIStyle CreateReportFoldoutStyle()
    {
        var style = new GUIStyle(EditorStyles.foldout)
        {
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        var textColor = EditorGUIUtility.isProSkin ? Color.white : new Color(0.12f, 0.12f, 0.12f, 1f);
        style.normal.textColor = textColor;
        style.hover.textColor = textColor;
        style.focused.textColor = textColor;
        style.active.textColor = textColor;
        style.onNormal.textColor = textColor;
        style.onHover.textColor = textColor;
        style.onFocused.textColor = textColor;
        style.onActive.textColor = textColor;
        return style;
    }

    private GUIStyle GridCellStyle => gridCellStyle ??= CreateGridCellStyle(EditorStyles.label, false);

    private GUIStyle GridHeaderCellStyle => gridHeaderCellStyle ??= CreateGridCellStyle(EditorStyles.miniBoldLabel, true);

    private static GUIStyle CreateGridCellStyle(GUIStyle baseStyle, bool header)
    {
        return new GUIStyle(baseStyle)
        {
            wordWrap = true,
            alignment = header ? TextAnchor.MiddleLeft : TextAnchor.UpperLeft,
            padding = new RectOffset(6, 6, 4, 4)
        };
    }

    private void DrawDataGridHeader(string[] headings, float[] columnSpecifications)
    {
        DrawDataGridRow(
            headings.Select(heading => new GUIContent(heading)).ToArray(),
            columnSpecifications,
            -1,
            true,
            null);
    }

    private void DrawDataGridRow(
        GUIContent[] cells,
        float[] columnSpecifications,
        int rowIndex,
        bool header = false,
        Action<Rect> drawActionCell = null,
        Color? rowHighlight = null)
    {
        if (cells == null || columnSpecifications == null || cells.Length != columnSpecifications.Length)
            return;

        var estimatedWidth = Mathf.Max(260f, EditorGUIUtility.currentViewWidth - 64f);
        var estimatedColumnWidths = ResolveGridColumnWidths(estimatedWidth, columnSpecifications);
        var style = header ? GridHeaderCellStyle : GridCellStyle;
        var rowHeight = header ? 26f : 24f;
        for (var index = 0; index < cells.Length; index++)
        {
            if (drawActionCell != null && index == 0)
                continue;

            rowHeight = Mathf.Max(
                rowHeight,
                style.CalcHeight(cells[index] ?? GUIContent.none, Mathf.Max(24f, estimatedColumnWidths[index] - 4f)) + 2f);
        }

        var rowRect = EditorGUILayout.GetControlRect(false, rowHeight);
        var columnWidths = ResolveGridColumnWidths(rowRect.width, columnSpecifications);
        var background = header
            ? (EditorGUIUtility.isProSkin ? new Color(0.18f, 0.18f, 0.18f, 1f) : new Color(0.80f, 0.80f, 0.80f, 1f))
            : rowHighlight ?? (rowIndex % 2 == 0
                ? (EditorGUIUtility.isProSkin ? new Color(0.15f, 0.15f, 0.15f, 1f) : new Color(0.92f, 0.92f, 0.92f, 1f))
                : (EditorGUIUtility.isProSkin ? new Color(0.19f, 0.19f, 0.19f, 1f) : new Color(0.86f, 0.86f, 0.86f, 1f)));

        var x = rowRect.x;
        for (var index = 0; index < cells.Length; index++)
        {
            var cellRect = new Rect(x, rowRect.y, columnWidths[index], rowRect.height);
            DrawGridCell(cellRect, cells[index] ?? GUIContent.none, style, background);
            if (drawActionCell != null && index == 0)
                drawActionCell(cellRect);
            x += columnWidths[index];
        }
    }

    private static float[] ResolveGridColumnWidths(float totalWidth, float[] specifications)
    {
        var widths = new float[specifications.Length];
        var fixedWidth = specifications.Where(specification => specification < 0f).Sum(specification => -specification);
        var flexibleWeight = specifications.Where(specification => specification > 0f).Sum();
        var flexibleWidth = Mathf.Max(1f, totalWidth - fixedWidth);

        for (var index = 0; index < specifications.Length; index++)
        {
            widths[index] = specifications[index] < 0f
                ? -specifications[index]
                : flexibleWidth * specifications[index] / Mathf.Max(0.01f, flexibleWeight);
        }

        return widths;
    }

    private static void DrawGridCell(Rect rect, GUIContent content, GUIStyle style, Color background)
    {
        var border = EditorGUIUtility.isProSkin
            ? new Color(0.32f, 0.32f, 0.32f, 1f)
            : new Color(0.52f, 0.52f, 0.52f, 1f);
        EditorGUI.DrawRect(rect, border);
        var innerRect = new Rect(rect.x + 1f, rect.y + 1f, Mathf.Max(0f, rect.width - 2f), Mathf.Max(0f, rect.height - 2f));
        EditorGUI.DrawRect(innerRect, background);
        GUI.Label(innerRect, content, style);
    }

    private static void DrawGridButton(Rect cellRect, string label, Action action)
    {
        var buttonRect = new Rect(cellRect.x + 4f, cellRect.y + 3f, Mathf.Max(1f, cellRect.width - 8f), 20f);
        if (GUI.Button(buttonRect, label))
            action?.Invoke();
    }

    private static void DrawGridButtons(Rect cellRect, (string label, Action action) first, (string label, Action action) second)
    {
        const float gap = 3f;
        var width = Mathf.Max(1f, (cellRect.width - 8f - gap) * 0.5f);
        var firstRect = new Rect(cellRect.x + 4f, cellRect.y + 3f, width, 20f);
        var secondRect = new Rect(firstRect.xMax + gap, cellRect.y + 3f, width, 20f);
        if (GUI.Button(firstRect, first.label))
            first.action?.Invoke();
        if (GUI.Button(secondRect, second.label))
            second.action?.Invoke();
    }

    private static string GetTextureSourceDescription(TextureInfo info)
    {
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

        return sources.Count > 0 ? string.Join(" + ", sources) : "scene texture";
    }

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
            return "<missing object>";

        var names = new Stack<string>();
        var current = transform;
        while (current != null)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names);
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
        showTerrainDetails = DrawReportFoldout(
            showTerrainDetails,
            $"Terrain Details ({terrainInfos.Count:N0})",
            new Color(0.28f, 0.48f, 0.16f),
            "Terrain");
        if (!showTerrainDetails)
            return;

        var uniqueTerrainTextureMemory = textureInfos
            .Where(info => info.usedByTerrain)
            .Sum(info => info.memoryBytes);
        var estimatedTerrainMemory = totalTerrainDataMemory + uniqueTerrainTextureMemory + totalTerrainLightmapMemory;
        var activeTerrainCount = terrainInfos.Count(info => info.contributesDrawSubmissions);
        DrawCategorySummary(
            ("Terrain Count", $"{terrainInfos.Count:N0} ({activeTerrainCount:N0} active)"),
            ("Estimated Terrain Memory", FormatBytes(estimatedTerrainMemory)),
            ("Terrain Data Memory", FormatBytes(totalTerrainDataMemory)),
            ("Unique Terrain Texture Memory", FormatBytes(uniqueTerrainTextureMemory)),
            ("Terrain Splat Map Memory", FormatBytes(totalTerrainSplatMemory)),
            ("Terrain Lightmap Memory", FormatBytes(totalTerrainLightmapMemory)),
            ("Surface Chunks / Submissions", $"{terrainSurfaceChunkCount:N0} / {terrainSurfaceDrawCalls:N0}"),
            ("Detail Instances", totalDetailInstances.ToString("N0")),
            ("Visible Detail Chunk Groups / Submissions", $"{terrainDetailChunkCount:N0} / {terrainDetailDrawCalls:N0}"),
            ("Tree Instances / Submissions", $"{totalTreeInstances:N0} / {terrainTreeDrawCalls:N0}"),
            ("Unique Prototype Materials", terrainPrototypeMaterialCount.ToString("N0")));

        if (terrainInfos.Count == 0)
        {
            EditorGUILayout.LabelField("No runtime terrains found.");
            return;
        }

        var columns = new[] { -66f, 1.6f, 1.1f, 1.4f, 2.1f, 1.2f, 0.9f };
        DrawDataGridHeader(
            new[] { "Action", "Terrain", "Memory", "Surface", "Details", "Trees", "State" },
            columns);

        var rowIndex = 0;
        foreach (var info in terrainInfos.OrderByDescending(item => item.terrainDataMemory + item.textureMemory))
        {
            DrawDataGridRow(
                new[]
                {
                    GUIContent.none,
                    new GUIContent(info.terrain.name, GetTransformPath(info.terrain.transform)),
                    new GUIContent($"TerrainData: {FormatBytes(info.terrainDataMemory)}\nTextures: {FormatBytes(info.textureMemory)}"),
                    new GUIContent(
                        $"Height: {info.heightmapResolution:N0}\nSplat layers: {info.alphamapLayers:N0}\n" +
                        $"Chunks: {info.surfaceChunkCount:N0}\nSubmissions: {info.surfaceDrawSubmissions:N0}\n" +
                        $"Instancing: {(info.usesInstancedTerrain ? "On" : "Off")}"),
                    new GUIContent(
                        $"Instances: {info.detailInstanceCount:N0}\nPrototypes: {info.detailPrototypeCount:N0}\n" +
                        $"Patch grid: {info.detailPatchesPerAxis:N0} x {info.detailPatchesPerAxis:N0}\n" +
                        $"Visible budget/prototype: {info.visibleDetailChunkBudgetPerPrototype:N0}\n" +
                        $"Occupied/visible: {info.occupiedDetailChunkCount:N0} / {info.detailChunkCount:N0}\n" +
                        $"Submissions: {info.detailDrawSubmissions:N0}"),
                    new GUIContent(
                        $"Instances: {info.treeInstanceCount:N0}\nPrototypes: {info.treePrototypeCount:N0}\n" +
                        $"Submissions: {info.treeDrawSubmissions:N0}"),
                    new GUIContent(info.contributesDrawSubmissions ? "Active" : "Disabled/inactive\nZero draws")
                },
                columns,
                rowIndex++,
                false,
                rect => DrawGridButton(rect, "Select", () => Selection.activeObject = info.terrain.gameObject));
        }
    }

    private void DrawMeshUsage()
    {
        var visibleMeshCount = Math.Min(20, meshInfos.Count);
        showMeshDetails = DrawReportFoldout(
            showMeshDetails,
            $"Largest Unique Meshes ({visibleMeshCount:N0} shown, {meshInfos.Count:N0} total)",
            new Color(0.48f, 0.33f, 0.12f),
            "Meshes");
        if (!showMeshDetails)
            return;

        DrawCategorySummary(
            ("Unique Meshes", meshInfos.Count.ToString("N0")),
            ("Meshes Shown", visibleMeshCount.ToString("N0")),
            ("Unique Mesh Memory", FormatBytes(totalMeshMemory)),
            ("Total Mesh Uses (Scene + Terrain + Colliders)", totalMeshUses.ToString("N0")));

        var columns = new[] { -66f, 2.5f, 0.9f, 0.9f, 0.8f, 1f, 0.9f };
        DrawDataGridHeader(
            new[] { "Action", "Mesh", "Memory", "Vertices", "Scene Uses", "Terrain Uses", "Collider Uses" },
            columns);

        var rowIndex = 0;
        foreach (var info in meshInfos.OrderByDescending(item => item.memoryBytes).Take(20))
        {
            DrawDataGridRow(
                new[]
                {
                    GUIContent.none,
                    new GUIContent(info.mesh.name, AssetDatabase.GetAssetPath(info.mesh)),
                    new GUIContent(FormatBytes(info.memoryBytes)),
                    new GUIContent(info.mesh.vertexCount.ToString("N0")),
                    new GUIContent(info.sceneUses.ToString("N0")),
                    new GUIContent(info.terrainUses.ToString("N0")),
                    new GUIContent(info.colliderUses.ToString("N0"))
                },
                columns,
                rowIndex++,
                false,
                rect => DrawGridButton(rect, "Select", () => Selection.activeObject = info.mesh));
        }
    }

    private void DrawRuntimeMeshCombinerUsage()
    {
        showRuntimeMeshCombinerDetails = DrawReportFoldout(
            showRuntimeMeshCombinerDetails,
            $"Runtime Mesh Combiners ({runtimeMeshCombinerInfos.Count:N0})",
            new Color(0.10f, 0.43f, 0.40f),
            "RuntimeMeshCombiners");
        if (!showRuntimeMeshCombinerDetails)
            return;

        var combinedOutputVertices = runtimeMeshCombinerInfos.Sum(info => info.estimatedOutputVertices);
        var combinedOutputIndices = runtimeMeshCombinerInfos.Sum(info => info.indexCount);
        DrawCategorySummary(
            ("Runtime Mesh Combiners", runtimeMeshCombinerInfos.Count.ToString("N0")),
            ("Estimated Combined Mesh Memory", FormatBytes(totalRuntimeCombinedMeshMemory)),
            ("Estimated Output Vertices", combinedOutputVertices.ToString("N0")),
            ("Estimated Output Indices", combinedOutputIndices.ToString("N0")),
            ("Draw Submissions Before / Runtime", $"{totalDrawCalls:N0} / {estimatedDrawCallsAfterRuntimeCombining:N0}"),
            ("Estimated Draw Change", FormatDrawDifference(totalDrawCalls, estimatedDrawCallsAfterRuntimeCombining)),
            ("Score Before / Runtime", $"{performanceScoreBeforeRuntimeCombining:F1} / {performanceScore:F1}"));

        if (runtimeMeshCombinerInfos.Count == 0)
        {
            EditorGUILayout.LabelField("None found in the scanned scene.");
            return;
        }

        var skippedUnreadableMeshes = runtimeMeshCombinerInfos.Sum(info => info.skippedUnreadableMeshCount);
        if (skippedUnreadableMeshes > 0)
        {
            EditorGUILayout.HelpBox(
                $"{skippedUnreadableMeshes:N0} unreadable source mesh{(skippedUnreadableMeshes == 1 ? " was" : "es were")} excluded from the estimates because RuntimeMeshCombiner will skip them at runtime.",
                MessageType.Warning);
        }

        var columns = new[] { -66f, 1.8f, 1.15f, 1.35f, 1.2f, 1.15f, 1.4f };
        DrawDataGridHeader(
            new[] { "Action", "Combiner Object", "Generated Memory", "Output Mesh", "Sources", "Draw Submissions", "Runtime Configuration" },
            columns);

        var rowIndex = 0;
        foreach (var info in runtimeMeshCombinerInfos.OrderByDescending(item => item.estimatedMemoryBytes))
        {
            var combinerName = info.combiner != null ? GetTransformPath(info.combiner.transform) : "Missing RuntimeMeshCombiner";
            var trigger = info.combineOnAwake ? "Awake" : "Manual";
            var sourceState = info.disableSourceRenderers ? "sources disabled" : "sources retained";
            var runtimeDraws = !info.executesInCurrentState
                ? info.sourceDrawSubmissions
                : info.disableSourceRenderers
                    ? info.combinedDrawSubmissions
                    : info.sourceDrawSubmissions + info.combinedDrawSubmissions;
            var drawChange = GetDrawChangeDescription(info);
            var activeState = info.executesInCurrentState ? "active" : "disabled/inactive - zero combined draws";
            var collider = info.addMeshCollider ? ", MeshCollider" : string.Empty;
            var unreadable = info.skippedUnreadableMeshCount > 0
                ? $"\nSkipped unreadable: {info.skippedUnreadableMeshCount:N0}"
                : string.Empty;
            DrawDataGridRow(
                new[]
                {
                    GUIContent.none,
                    new GUIContent(combinerName, combinerName),
                    new GUIContent(
                        $"Total: {FormatBytes(info.estimatedMemoryBytes)}\n" +
                        $"Vertices: {FormatBytes(info.estimatedVertexBytes)}\nIndices: {FormatBytes(info.estimatedIndexBytes)}"),
                    new GUIContent(
                        $"Vertices: {info.estimatedOutputVertices:N0}\nIndices: {info.indexCount:N0}\nMaterials: {info.materialCount:N0}"),
                    new GUIContent(
                        $"Meshes: {info.sourceMeshCount:N0}\nSubmeshes: {info.sourceSubMeshCount:N0}{unreadable}"),
                    new GUIContent(
                        $"Before: {info.sourceDrawSubmissions:N0}\nRuntime: {runtimeDraws:N0}\n{drawChange}"),
                    new GUIContent($"{activeState}\nTrigger: {trigger}\n{sourceState}{collider}")
                },
                columns,
                rowIndex++,
                false,
                rect => DrawGridButton(
                    rect,
                    "Select",
                    () =>
                    {
                        if (info.combiner != null)
                            Selection.activeObject = info.combiner.gameObject;
                    }));
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
        GUILayout.Label($"Projected Runtime Score: {performanceScore:F0} ({performanceGrade})", big);
        GUI.color = Color.white;

        if (runtimeMeshCombinerInfos.Count > 0)
        {
            var gainStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            var previousColor = GUI.contentColor;
            GUI.contentColor = runtimeCombinerScoreGain > 0.05f
                ? Color.green
                : runtimeCombinerScoreGain < -0.05f ? Color.red : Color.yellow;
            GUILayout.Label(
                $"Runtime mesh combining: {performanceScoreBeforeRuntimeCombining:F1} -> {performanceScore:F1} " +
                $"({runtimeCombinerScoreGain:+0.0;-0.0;0.0} points), estimated draws {totalDrawCalls:N0} -> {estimatedDrawCallsAfterRuntimeCombining:N0}",
                gainStyle);
            GUI.contentColor = previousColor;
            EditorGUILayout.HelpBox(
                "Disabled components and inactive GameObjects contribute zero estimated draws. Active manual combiners are assumed to execute. Draw gains replace source renderer submissions with one submission per combined non-null material; generated mesh memory is included as a cost.",
                MessageType.Info);
        }
    }

    private void DrawShaderFragmentation()
    {
        var shaderCount = materialInfos
            .Select(materialInfo => materialInfo.shader)
            .Distinct()
            .Count();
        var unsupportedShaderCount = materialInfos
            .Where(materialInfo => !materialInfo.isSupportedShader)
            .Select(materialInfo => materialInfo.shader)
            .Distinct()
            .Count();
        showShaderFragmentationDetails = DrawReportFoldout(
            showShaderFragmentationDetails,
            $"Shader Fragmentation ({shaderCount:N0} shaders, {unsupportedShaderCount:N0} unsupported)",
            unsupportedShaderCount > 0
                ? new Color(0.58f, 0.12f, 0.12f)
                : new Color(0.42f, 0.23f, 0.52f),
            "ShaderFragmentation");
        if (!showShaderFragmentationDetails)
            return;

        var supportedShaderCount = Math.Max(0, shaderCount - unsupportedShaderCount);
        var totalKeywordVariants = materialInfos
            .Where(info => info.material != null && info.material.shader != null)
            .Select(info => GetVariantKey(info.material))
            .Distinct()
            .Count();
        DrawCategorySummary(
            ("Unique Shaders", shaderCount.ToString("N0")),
            ("Supported / Unsupported Shaders", $"{supportedShaderCount:N0} / {unsupportedShaderCount:N0}"),
            ("Materials Scanned", materialInfos.Count.ToString("N0")),
            ("Unique Shader Keyword Variants", totalKeywordVariants.ToString("N0")),
            ("Shader Batch Groups", batches.Count.ToString("N0")));

        if (repairedTemplateMaterialCount > 0)
        {
            EditorGUILayout.HelpBox(
                $"Template enforcement repaired and saved {repairedTemplateMaterialCount:N0} " +
                $"material{(repairedTemplateMaterialCount == 1 ? string.Empty : "s")} during this scan. " +
                "The results below show the corrected runtime state.",
                MessageType.Info);
        }

        if (unsupportedShaderCount > 0)
        {
            EditorGUILayout.HelpBox(
                $"ERROR: {unsupportedShaderCount:N0} unsupported shader{(unsupportedShaderCount == 1 ? " was" : "s were")} found. " +
                $"Only shaders supplied by the MashBox SDK, Unity's {SupportedTerrainShaderName} terrain shader, and template-enforced {SupportedDecalShaderName} decals are supported. " +
                "Replace every red shader before publishing.",
                MessageType.Error);
        }

        EditorGUILayout.HelpBox(
            "A shader batch group requires the same shader, enabled shader keywords, and Lightmap ID. " +
            "Renderers using the same shader but different Lightmap IDs produce separate batches because they sample different lightmap textures. " +
            "Lightmap ID -1 means the material is used by a renderer that is not lightmapped.",
            MessageType.Info);
        EditorGUILayout.HelpBox(
            "Optimization tip: Where practical, place objects using the same shader onto one lightmap atlas. " +
            "When using Bakery, consolidate lightmap groups where possible. A well-packed single 4K lightmap is often sufficient for a typical map, as long as texel density and visual quality remain acceptable.",
            MessageType.Info);

        var grouped = materialInfos
            .GroupBy(m => m.shader);

        var columns = new[] { -220f, 2.6f, 1.6f, 1f, 1f };
        DrawDataGridHeader(new[] { "Action", "Shader", "Support Status", "Keyword Variants", "Materials" }, columns);
        var rowIndex = 0;
        foreach (var group in grouped.OrderByDescending(g => g.Count()))
        {
            var variants = group
                .Select(m => string.Join(";", m.keywords))
                .Distinct()
                .Count();
            var supported = group.All(materialInfo => materialInfo.isSupportedShader);
            var shaderPath = group.Select(materialInfo => materialInfo.shaderAssetPath)
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
            var groupMaterials = group
                .Where(materialInfo => materialInfo.material != null)
                .Select(materialInfo => materialInfo.material)
                .ToArray();

            DrawDataGridRow(
                new[]
                {
                    GUIContent.none,
                    new GUIContent(group.Key?.name ?? "Missing Shader", shaderPath),
                    new GUIContent(supported
                        ? "Supported"
                        : "ERROR: Unsupported shader\nUse an SDK shader, HDRP/TerrainLit, or HDRP/Decal."),
                    new GUIContent(variants.ToString("N0")),
                    new GUIContent(group.Count().ToString("N0"))
                },
                columns,
                rowIndex++,
                false,
                rect => DrawShaderGroupActions(rect, group.Key, groupMaterials),
                supported ? null : new Color(0.42f, 0.08f, 0.08f, 1f));
        }
    }

    private void DrawShaderGroupActions(Rect cellRect, Shader shader, Material[] materials)
    {
        if (shader == null || !string.Equals(shader.name, HdrpLitShaderName, StringComparison.Ordinal))
        {
            DrawGridButton(cellRect, "Select", () => Selection.objects = materials);
            return;
        }

        var selectRect = new Rect(cellRect.x + 4f, cellRect.y + 3f, 56f, 20f);
        if (GUI.Button(selectRect, "Select"))
            Selection.objects = materials;

        var convertRect = new Rect(
            selectRect.xMax + 4f,
            selectRect.y,
            Mathf.Max(1f, cellRect.xMax - selectRect.xMax - 8f),
            20f);
        using (new EditorGUI.DisabledScope(shaderConversionQueued))
        {
            var label = new GUIContent(
                "Convert All -> MG Basic",
                "Convert every material in this HDRP/Lit scanner group to MG_Lit_Basic.");
            if (GUI.Button(convertRect, label))
                QueueHdrpLitConversion(materials);
        }
    }

    private void QueueHdrpLitConversion(IEnumerable<Material> materials)
    {
        if (shaderConversionQueued)
            return;

        var candidates = materials
            .Where(material => material != null && material.shader != null &&
                               string.Equals(material.shader.name, HdrpLitShaderName, StringComparison.Ordinal))
            .Distinct()
            .ToArray();
        if (candidates.Length == 0)
            return;

        shaderConversionQueued = true;
        EditorApplication.delayCall += () =>
        {
            try
            {
                ConvertHdrpLitMaterials(candidates);
            }
            finally
            {
                shaderConversionQueued = false;
            }
        };
    }

    private void ConvertHdrpLitMaterials(Material[] materials)
    {
        if (materials == null || materials.Length == 0)
            return;

        if (!EditorUtility.DisplayDialog(
                "Convert HDRP/Lit Materials",
                $"Convert {materials.Length:N0} material{(materials.Length == 1 ? string.Empty : "s")} from " +
                $"{HdrpLitShaderName} to {MgLitBasicShaderName}?\n\n" +
                "Compatible material values will be preserved. Material variants whose shader is inherited from a parent will be skipped. " +
                "The operation can be reverted with Unity Undo.",
                "Convert All",
                "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        var undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Convert HDRP Lit Materials to MG Lit Basic");
        Undo.RecordObjects(materials, "Convert HDRP Lit Materials to MG Lit Basic");

        var converted = 0;
        var skipped = 0;
        var changedSceneMaterial = false;
        foreach (var material in materials)
        {
            if (material == null || material.shader == null ||
                !string.Equals(material.shader.name, HdrpLitShaderName, StringComparison.Ordinal))
            {
                skipped++;
                continue;
            }

            var hadNormalScale = material.HasProperty("_NormalScale");
            var normalScale = hadNormalScale ? material.GetFloat("_NormalScale") : 1f;

            MashBoxSDK.Shaders.ShaderEnforcer.EnforceLitBasicShader(material);
            if (material.shader == null || !string.Equals(material.shader.name, MgLitBasicShaderName, StringComparison.Ordinal))
            {
                skipped++;
                continue;
            }

            if (hadNormalScale && material.HasProperty("_NormalStrength"))
                material.SetFloat("_NormalStrength", normalScale);

            changedSceneMaterial |= string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(material));
            EditorUtility.SetDirty(material);
            converted++;
        }

        Undo.CollapseUndoOperations(undoGroup);
        AssetDatabase.SaveAssets();
        if (changedSceneMaterial && SceneManager.GetActiveScene().IsValid())
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        ScanScene();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        Debug.Log($"[MashBox] Converted {converted:N0} HDRP/Lit material{(converted == 1 ? string.Empty : "s")} " +
                  $"to MG_Lit_Basic; skipped {skipped:N0}.");

        if (skipped > 0)
        {
            EditorUtility.DisplayDialog(
                "HDRP/Lit Conversion Complete",
                $"Converted: {converted:N0}\nSkipped: {skipped:N0}\n\n" +
                "Skipped materials may be material variants with an inherited shader, or may have changed since the scan.",
                "OK");
        }
    }

    private void DrawTopOffenders()
    {
        var visibleBatchCount = Math.Min(10, batches.Count);
        showShaderBatchGroupDetails = DrawReportFoldout(
            showShaderBatchGroupDetails,
            $"Top Shader Batch Groups ({visibleBatchCount:N0} shown, {batches.Count:N0} total)",
            new Color(0.27f, 0.31f, 0.55f),
            "ShaderBatchGroups");
        if (!showShaderBatchGroupDetails)
            return;

        var materialsInBatchGroups = batches.Sum(batch => batch.Value.Count);
        var lightmapIdCount = batches.Keys.Select(key => key.lightmapIndex).Distinct().Count();
        DrawCategorySummary(
            ("Total Shader Batch Groups", batches.Count.ToString("N0")),
            ("Batch Groups Shown", visibleBatchCount.ToString("N0")),
            ("Materials in Batch Groups", materialsInBatchGroups.ToString("N0")),
            ("Distinct Lightmap IDs", lightmapIdCount.ToString("N0")));

        EditorGUILayout.LabelField(
            "Materials in group is the number that was previously displayed in parentheses.",
            EditorStyles.wordWrappedMiniLabel);

        var worst = batches
            .OrderByDescending(b => b.Value.Count)
            .Take(10)
            .ToList();

        var columns = new[] { -66f, 1.8f, 1.25f, 1.2f, 0.8f, 2.6f };
        DrawDataGridHeader(
            new[] { "Action", "Shader", "Support Status", "Lightmap ID", "Materials", "Enabled Shader Keywords" },
            columns);
        for (var rowIndex = 0; rowIndex < worst.Count; rowIndex++)
        {
            var batch = worst[rowIndex];
            var keywordList = string.IsNullOrWhiteSpace(batch.Key.keywordSignature)
                ? Array.Empty<string>()
                : batch.Key.keywordSignature.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var keywords = keywordList.Length == 0 ? "None" : string.Join("\n", keywordList);
            var supported = batch.Value.All(materialInfo => materialInfo.isSupportedShader);
            DrawDataGridRow(
                new[]
                {
                    GUIContent.none,
                    new GUIContent(batch.Key.shader?.name ?? "Missing Shader"),
                    new GUIContent(supported ? "Supported" : "ERROR: Unsupported shader"),
                    new GUIContent(FormatLightmapId(batch.Key.lightmapIndex)),
                    new GUIContent(batch.Value.Count.ToString("N0")),
                    new GUIContent(keywords, keywordList.Length == 0 ? "No shader keywords are enabled." : string.Join(", ", keywordList))
                },
                columns,
                rowIndex,
                false,
                rect => DrawGridButton(
                    rect,
                    "Select",
                    () => Selection.objects = batch.Value.Select(materialInfo => materialInfo.material).ToArray()),
                supported ? null : new Color(0.42f, 0.08f, 0.08f, 1f));
        }
    }

    private void DrawDecals()
    {
        var decals = materialInfos
            .Where(info => info.material != null && decalMaterials.Contains(info.material))
            .ToList();
        showDecalDetails = DrawReportFoldout(
            showDecalDetails,
            $"Decals ({decals.Count:N0})",
            new Color(0.55f, 0.28f, 0.10f),
            "Decals");
        if (!showDecalDetails)
            return;

        var uniqueDecalShaders = decals.Select(info => info.shader).Distinct().Count();
        var unsupportedDecalMaterials = decals.Count(info => !info.isSupportedShader);
        DrawCategorySummary(
            ("Decal Materials", $"{decals.Count:N0} (perfect with fewer than 2)"),
            ("Active Decal Projectors", $"{decalDrawCalls:N0} (perfect with fewer than 300)"),
            ("Unique Decal Shaders", uniqueDecalShaders.ToString("N0")),
            ("Unsupported Decal Materials", unsupportedDecalMaterials.ToString("N0")));

        var columns = new[] { -66f, 2f, 3f };
        DrawDataGridHeader(new[] { "Action", "Material", "Asset Location" }, columns);
        var visibleDecals = decals.Take(20).ToList();
        for (var rowIndex = 0; rowIndex < visibleDecals.Count; rowIndex++)
        {
            var decal = visibleDecals[rowIndex];
            var assetPath = AssetDatabase.GetAssetPath(decal.material);
            DrawDataGridRow(
                new[] { GUIContent.none, new GUIContent(decal.material.name), new GUIContent(assetPath, assetPath) },
                columns,
                rowIndex,
                false,
                rect => DrawGridButton(rect, "Select", () => Selection.activeObject = decal.material));
        }
    }

    private void DrawRendererIssues()
    {
        showRendererIssueDetails = DrawReportFoldout(
            showRendererIssueDetails,
            $"Non-Batching Renderers ({rendererIssues.Count:N0})",
            rendererIssues.Count > 0
                ? new Color(0.55f, 0.14f, 0.14f)
                : new Color(0.20f, 0.42f, 0.25f),
            "RendererIssues");
        if (!showRendererIssueDetails)
            return;

        DrawCategorySummary(
            ("Non-Batching Renderers Before Combining", rendererIssues.Count.ToString("N0")),
            ("Estimated Runtime Renderer Issues", estimatedRendererIssuesAfterRuntimeCombining.ToString("N0")),
            ("Estimated Issues Removed by Combining", Math.Max(0, rendererIssues.Count - estimatedRendererIssuesAfterRuntimeCombining).ToString("N0")));

        if (rendererIssues.Count == 0)
        {
            EditorGUILayout.LabelField("None");
            return;
        }

        var columns = new[] { -66f, 1.8f, 2.2f, 2f };
        DrawDataGridHeader(new[] { "Action", "Renderer", "Reason", "Materials" }, columns);
        for (var rowIndex = 0; rowIndex < rendererIssues.Count; rowIndex++)
        {
            var issue = rendererIssues[rowIndex];
            var rendererPath = issue.renderer != null ? GetTransformPath(issue.renderer.transform) : "Missing Renderer";
            var materials = string.Join("\n", issue.materials.Where(material => material != null).Select(material => material.name));
            DrawDataGridRow(
                new[]
                {
                    GUIContent.none,
                    new GUIContent(rendererPath, rendererPath),
                    new GUIContent(issue.reason),
                    new GUIContent(string.IsNullOrWhiteSpace(materials) ? "None" : materials)
                },
                columns,
                rowIndex,
                false,
                rect => DrawGridButton(
                    rect,
                    "Select",
                    () =>
                    {
                        if (issue.renderer != null)
                            Selection.activeObject = issue.renderer.gameObject;
                    }));
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
