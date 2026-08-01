using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public class MapPerformanceScanner : EditorWindow
{
    class MaterialInfo
    {
        public Material material;
        public Shader shader;
        public string[] keywords;
        public List<Texture> textures;
        public bool isDecal;
        public int lightmapIndex;
    }

    class RendererIssue
    {
        public Renderer renderer;
        public List<Material> materials;
        public string reason;
    }

    class BatchKey
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
            if (obj is not BatchKey other) return false;

            return shader == other.shader &&
                   keywordSignature == other.keywordSignature &&
                   lightmapIndex == other.lightmapIndex;
        }
    }

    private List<MaterialInfo> materialInfos = new();
    private Dictionary<BatchKey, List<MaterialInfo>> batches = new();
    private HashSet<Material> processedMaterials = new();
    private List<RendererIssue> rendererIssues = new();

    private int totalDrawCalls;
    private int decalDrawCalls;
    private long totalTextureMemory;

    float performanceScore = 100f;
    string performanceGrade = "A";
    Color scoreColor = Color.green;

    private Vector2 scroll;

    //[MenuItem("MashBox/Map Tools/Map Performance Scanner")]
    public static void Open()
    {
        GetWindow<MapPerformanceScanner>("Map Scanner");
    }

    void OnGUI()
    {
        GUILayout.Space(10);

        if (GUILayout.Button("Scan Scene", GUILayout.Height(40)))
        {
            ScanScene();
        }

        GUILayout.Space(10);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawResults();
        EditorGUILayout.EndScrollView();
    }

    void ScanScene()
    {
        materialInfos.Clear();
        batches.Clear();
        processedMaterials.Clear();
        rendererIssues.Clear();

        totalDrawCalls = 0;
        decalDrawCalls = 0;
        totalTextureMemory = 0;

        CollectRenderers();
        CollectDecals();
        BuildBatches();
        CalculateDrawCalls();
        CalculateTextureMemory();
        CalculatePerformanceScore();

        Debug.Log("Scan Complete");
    }

    // =========================
    // COLLECTION
    // =========================

    void CollectRenderers()
    {
        var renderers = FindSceneObjects<Renderer>();

        foreach (var renderer in renderers)
        {
            if (IsUnderChallengesRoot(renderer.transform))
                continue;

            if (renderer.GetComponent<DecalProjector>() != null)
                continue;

            var mats = renderer.sharedMaterials.Where(m => m != null).ToList();

            foreach (var mat in mats)
                CollectMaterial(mat, false, renderer.lightmapIndex);

            if (mats.Count > 1)
            {
                var variants = mats.Select(GetVariantKey).Distinct().ToList();

                if (variants.Count > 1)
                {
                    rendererIssues.Add(new RendererIssue
                    {
                        renderer = renderer,
                        materials = mats,
                        reason = GetDifferenceReason(mats)
                    });
                }
            }
        }
    }

    void CollectDecals()
    {
        var decals = FindSceneObjects<DecalProjector>();

        foreach (var decal in decals)
        {
            if (IsUnderChallengesRoot(decal.transform))
                continue;

            if (decal.material != null)
                CollectMaterial(decal.material, true, -1);
        }
    }


    private static T[] FindSceneObjects<T>() where T : UnityEngine.Object
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
    bool IsUnderChallengesRoot(Transform target)
    {
        var current = target;
        while (current != null)
        {
            if (string.Equals(current.name, "Challenges", System.StringComparison.OrdinalIgnoreCase))
                return true;

            current = current.parent;
        }

        return false;
    }

    void CollectMaterial(Material mat, bool isDecal, int lightmapIndex)
    {
        if (processedMaterials.Contains(mat))
            return;

        processedMaterials.Add(mat);

        var keywords = mat.enabledKeywords
            .Select(k => k.name)
            .OrderBy(k => k)
            .ToArray();

        var textures = GetTextures(mat);

        materialInfos.Add(new MaterialInfo
        {
            material = mat,
            shader = mat.shader,
            keywords = keywords,
            textures = textures,
            isDecal = isDecal,
            lightmapIndex = lightmapIndex
        });
    }

    // =========================
    // BATCHING
    // =========================

    void BuildBatches()
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

    void CalculateDrawCalls()
    {
        int batchCount = batches.Count;
        decalDrawCalls = materialInfos.Count(m => m.isDecal);

        totalDrawCalls = batchCount + decalDrawCalls;
    }

    // =========================
    // TEXTURES
    // =========================

    void CalculateTextureMemory()
    {
        HashSet<Texture> seen = new();

        foreach (var mat in materialInfos)
        {
            foreach (var tex in mat.textures)
            {
                if (tex == null || !seen.Add(tex))
                    continue;

                totalTextureMemory += EstimateTextureSizeBytes(tex);
            }
        }
    }

    long EstimateTextureSizeBytes(Texture tex)
    {
        string path = AssetDatabase.GetAssetPath(tex);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (importer != null)
        {
            int width = Mathf.Min(tex.width, importer.maxTextureSize);
            int height = Mathf.Min(tex.height, importer.maxTextureSize);

            TextureImporterFormat format = importer.GetPlatformTextureSettings("Standalone").format;

            if (format == TextureImporterFormat.Automatic)
                format = importer.textureCompression == TextureImporterCompression.Uncompressed
                    ? TextureImporterFormat.RGBA32
                    : TextureImporterFormat.DXT5;

            int bpp = format == TextureImporterFormat.DXT1 ? 4 :
                      (format == TextureImporterFormat.DXT5 || format == TextureImporterFormat.BC7) ? 8 : 32;

            long size = (long)width * height * bpp / 8;

            if (importer.mipmapEnabled)
                size = (long)(size * 1.33f);

            return size;
        }

        return (long)tex.width * tex.height * 4;
    }

    List<Texture> GetTextures(Material mat)
    {
        return MapPerformanceMaterialTextureCollector.GetTextures(mat);
    }

    // =========================
    // VARIANTS
    // =========================

    string GetVariantKey(Material mat)
    {
        var keywords = mat.enabledKeywords
            .Select(k => k.name)
            .OrderBy(k => k);

        return mat.shader.name + "|" + string.Join(";", keywords);
    }

    string GetDifferenceReason(List<Material> mats)
    {
        var shaders = mats.Select(m => m.shader.name).Distinct().ToList();

        if (shaders.Count > 1)
            return "Different shaders";

        var allKeywords = mats.SelectMany(m => m.enabledKeywords.Select(k => k.name)).Distinct();

        var diffs = allKeywords.Where(k => !mats.All(m => m.IsKeywordEnabled(k))).Take(5);

        return diffs.Any() ? "Keyword mismatch: " + string.Join(", ", diffs) : "Unknown difference";
    }

    // =========================
    // PERFORMANCE SCORE
    // =========================

    void CalculatePerformanceScore()
    {
        float maxDrawCalls = 2000f;
        float maxTextureMemory = 2f * 1024f * 1024f * 1024f;

        // 🔴 New thresholds
        float maxDecals = 5f;
        float maxShaderVariants = 6f;

        float rendererPenalty = Mathf.Clamp01(rendererIssues.Count / 100f);
        float drawPenalty = Mathf.Clamp01(totalDrawCalls / maxDrawCalls);
        float texturePenalty = Mathf.Clamp01(totalTextureMemory / maxTextureMemory);

        float decalPenalty = Mathf.Clamp01(decalDrawCalls / maxDecals);
        float shaderPenalty = Mathf.Clamp01(batches.Count / maxShaderVariants);

        float penalty =
            rendererPenalty * 0.4f +
            drawPenalty * 0.2f +
            texturePenalty * 0.15f +
            decalPenalty * 0.15f +
            shaderPenalty * 0.1f;

        performanceScore = Mathf.Clamp(100f - penalty * 100f, 0f, 100f);

        if (performanceScore > 85) { performanceGrade = "A"; scoreColor = Color.green; }
        else if (performanceScore > 70) { performanceGrade = "B"; scoreColor = Color.yellow; }
        else if (performanceScore > 50) { performanceGrade = "C"; scoreColor = new Color(1f, 0.5f, 0f); }
        else { performanceGrade = "D"; scoreColor = Color.red; }
    }

    void DrawScoreBreakdown()
    {
        EditorGUILayout.LabelField("=== SCORE BREAKDOWN ===", EditorStyles.boldLabel);

        DrawMetric("Renderer Issues", rendererIssues.Count, 10);
        DrawMetric("Draw Calls", totalDrawCalls, 2000);
        DrawMetric("Decals", decalDrawCalls, 5);
        DrawMetric("Shader Variants", batches.Count, 6);
        DrawMetric("Texture Memory", totalTextureMemory, 2L * 1024L * 1024L * 1024L, true);
    }
    void DrawMetric(string label, float value, float threshold, bool isBytes = false)
    {
        float ratio = value / threshold;

        Color color =
            ratio < 0.5f ? Color.green :
            ratio < 1f ? Color.yellow :
            Color.red;

        GUI.color = color;

        string display = isBytes ? FormatBytes((long)value) : value.ToString();

        EditorGUILayout.LabelField($"{label}: {display}");

        GUI.color = Color.white;
    }
    // =========================
    // UI
    // =========================

    void DrawResults()
    {
        DrawPerformanceScore();

        DrawScoreBreakdown();
        EditorGUILayout.Space(10);
        
        EditorGUILayout.LabelField("=== SCAN RESULTS ===", EditorStyles.boldLabel);

        EditorGUILayout.LabelField("Materials", materialInfos.Count.ToString());
        EditorGUILayout.LabelField("Shader Variants (Batches)", batches.Count.ToString());
        EditorGUILayout.LabelField("Decal Draw Calls", decalDrawCalls.ToString());
        EditorGUILayout.LabelField("Texture Memory", FormatBytes(totalTextureMemory));

        GUILayout.Space(20);

        DrawShaderFragmentation();

        GUILayout.Space(10);
        DrawTopOffenders();

        GUILayout.Space(10);
        DrawDecals();

        GUILayout.Space(10);
        DrawRendererIssues();
    }

    void DrawPerformanceScore()
    {
        GUIStyle big = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 24,
            alignment = TextAnchor.MiddleCenter
        };

        GUI.color = scoreColor;
        GUILayout.Label($"Performance Score: {performanceScore:F0} ({performanceGrade})", big);
        GUI.color = Color.white;
    }

    void DrawShaderFragmentation()
    {
        EditorGUILayout.LabelField("=== SHADER FRAGMENTATION ===", EditorStyles.boldLabel);

        var grouped = materialInfos
            .Where(m => !m.isDecal)
            .GroupBy(m => m.shader);

        foreach (var group in grouped.OrderByDescending(g => g.Count()))
        {
            var variants = group
                .Select(m => string.Join(";", m.keywords))
                .Distinct()
                .Count();

            EditorGUILayout.LabelField(
                $"{group.Key.name} → {variants} variants ({group.Count()} materials)"
            );
        }
    }

    void DrawTopOffenders()
    {
        EditorGUILayout.LabelField("=== TOP SHADER VARIANTS ===", EditorStyles.boldLabel);

        var worst = batches
            .OrderByDescending(b => b.Value.Count)
            .Take(10);

        foreach (var batch in worst)
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Select", GUILayout.Width(60)))
                Selection.objects = batch.Value.Select(m => m.material).ToArray();

            EditorGUILayout.LabelField(
                $"{batch.Key.shader.name} | LM:{batch.Key.lightmapIndex} ({batch.Value.Count})"
            );

            EditorGUILayout.EndHorizontal();
        }
    }

    void DrawDecals()
    {
        EditorGUILayout.LabelField("=== DECALS ===", EditorStyles.boldLabel);

        var decals = materialInfos.Where(m => m.isDecal).ToList();

        EditorGUILayout.LabelField($"Total Decals: {decals.Count}");

        foreach (var d in decals.Take(20))
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Select", GUILayout.Width(60)))
                Selection.activeObject = d.material;

            EditorGUILayout.LabelField(d.material.name);

            EditorGUILayout.EndHorizontal();
        }
    }

    void DrawRendererIssues()
    {
        EditorGUILayout.LabelField("=== NON-BATCHING RENDERERS ===", EditorStyles.boldLabel);

        if (rendererIssues.Count == 0)
        {
            EditorGUILayout.LabelField("None 🎉");
            return;
        }

        foreach (var issue in rendererIssues)
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Select", GUILayout.Width(60)))
                Selection.activeObject = issue.renderer.gameObject;

            EditorGUILayout.LabelField($"{issue.renderer.name} → {issue.reason}");

            EditorGUILayout.EndHorizontal();
        }
    }

    string FormatBytes(long bytes)
    {
        if (bytes > 1024 * 1024 * 1024)
            return $"{(bytes / (1024f * 1024f * 1024f)):F2} GB";

        if (bytes > 1024 * 1024)
            return $"{(bytes / (1024f * 1024f)):F2} MB";

        if (bytes > 1024)
            return $"{(bytes / 1024f):F2} KB";

        return $"{bytes} B";
    }
}
