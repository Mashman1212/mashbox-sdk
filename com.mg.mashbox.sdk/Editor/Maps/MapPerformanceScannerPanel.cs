using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

[Serializable]
public class MapPerformanceScannerPanel
{
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
    [SerializeField] private int totalDrawCalls;
    [SerializeField] private int decalDrawCalls;
    [SerializeField] private long totalTextureMemory;
    [SerializeField] private float performanceScore = 100f;
    [SerializeField] private string performanceGrade = "A";
    [SerializeField] private Color scoreColor = Color.green;
    [SerializeField] private Vector2 scroll;
    [SerializeField] private bool hasScanResults;

    private readonly Dictionary<BatchKey, List<MaterialInfo>> batches = new();
    private readonly HashSet<Material> processedMaterials = new();

    public void DrawGUI(bool embeddedInParentWindow = false)
    {
        EditorGUILayout.Space(embeddedInParentWindow ? 6f : 10f);
        EditorGUILayout.HelpBox(
            "Scan the loaded scene to review shader fragmentation, decal usage, texture memory, and renderers that are likely preventing batching.",
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

    private void ScanScene()
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

        hasScanResults = true;
        Debug.Log("[MashBox] Performance scan complete.");
    }

    private void CollectRenderers()
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

    private void CollectDecals()
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
        decalDrawCalls = materialInfos.Count(m => m.isDecal);
        totalDrawCalls = batches.Count + decalDrawCalls;
    }

    private void CalculateTextureMemory()
    {
        var seen = new HashSet<Texture>();

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

    private long EstimateTextureSizeBytes(Texture tex)
    {
        var path = AssetDatabase.GetAssetPath(tex);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (importer != null)
        {
            var width = Mathf.Min(tex.width, importer.maxTextureSize);
            var height = Mathf.Min(tex.height, importer.maxTextureSize);
            var format = importer.GetPlatformTextureSettings("Standalone").format;

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
        var texturePenalty = Mathf.Clamp01(totalTextureMemory / maxTextureMemory);
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
        EditorGUILayout.LabelField("Score Breakdown", EditorStyles.boldLabel);
        DrawMetric("Renderer Issues", rendererIssues.Count, 10f);
        DrawMetric("Draw Calls", totalDrawCalls, 2000f);
        DrawMetric("Decals", decalDrawCalls, 5f);
        DrawMetric("Shader Variants", batches.Count, 6f);
        DrawMetric("Texture Memory", totalTextureMemory, 2L * 1024L * 1024L * 1024L, true);
    }

    private void DrawMetric(string label, float value, float threshold, bool isBytes = false)
    {
        var ratio = value / threshold;
        var color = ratio < 0.5f ? Color.green : ratio < 1f ? Color.yellow : Color.red;

        GUI.color = color;
        var display = isBytes ? FormatBytes((long)value) : value.ToString();
        EditorGUILayout.LabelField($"{label}: {display}");
        GUI.color = Color.white;
    }

    private void DrawResults()
    {
        DrawPerformanceScore();
        DrawScoreBreakdown();
        EditorGUILayout.Space(10f);

        EditorGUILayout.LabelField("Scan Results", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Materials", materialInfos.Count.ToString());
        EditorGUILayout.LabelField("Shader Variants (Batches)", batches.Count.ToString());
        EditorGUILayout.LabelField("Decal Draw Calls", decalDrawCalls.ToString());
        EditorGUILayout.LabelField("Texture Memory", FormatBytes(totalTextureMemory));

        GUILayout.Space(20f);
        DrawShaderFragmentation();

        GUILayout.Space(10f);
        DrawTopOffenders();

        GUILayout.Space(10f);
        DrawDecals();

        GUILayout.Space(10f);
        DrawRendererIssues();
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
