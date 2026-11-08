using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public partial class MapPerformanceScannerPanel
{
    private const int CurrentTextureMemoryModelVersion = 1;
    [SerializeField] private int textureMemoryModelVersion;

    internal readonly struct TextureMemoryEstimate
    {
        internal readonly long ImportedBytes, GpuBytes, CpuBytes;
        internal long RuntimeBytes => GpuBytes + CpuBytes;
        internal TextureMemoryEstimate(long imported, long gpu, long cpu)
        { ImportedBytes = imported; GpuBytes = gpu; CpuBytes = cpu; }
    }

    // The texture Inspector uses this API for its imported/storage size. Resolve it
    // once, and only call it while scanning, never while drawing report rows.
    private static readonly Func<Texture, long> InspectorStorageSize = CreateInspectorStorageSizeReader();
    private static Func<Texture, long> CreateInspectorStorageSizeReader()
    {
        var method = typeof(Editor).Assembly.GetType("UnityEditor.TextureUtil")?.GetMethod(
            "GetStorageMemorySizeLong", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(Texture) }, null);
        return method != null ? (Func<Texture, long>)Delegate.CreateDelegate(typeof(Func<Texture, long>), method) : null;
    }

    internal static TextureMemoryEstimate MeasureTextureMemory(Texture texture)
    {
        if (texture == null) return default;
        long gpu = EstimateTextureStorageBytes(texture);
        long imported = InspectorStorageSize != null ? InspectorStorageSize(texture) : gpu;
        if (imported <= 0) imported = gpu;

        // Profiler.GetRuntimeMemorySizeLong includes editor-only source/preview copies.
        // The importer is the authority for whether an imported texture keeps a CPU
        // copy in a player, even if the Editor has readable source data cached.
        var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
        bool readable = importer != null ? importer.isReadable : texture switch
        {
            Texture2D value => value.isReadable,
            Texture2DArray value => value.isReadable,
            Texture3D value => value.isReadable,
            Cubemap value => value.isReadable,
            CubemapArray value => value.isReadable,
            _ => false
        };
        return new TextureMemoryEstimate(imported, gpu, readable ? gpu : 0L);
    }

    private const string TextureMemoryExplanation =
        "Imported matches Unity's texture Inspector size for the current import target. " +
        "Runtime includes the imported GPU format, all mip levels and array slices, plus a CPU copy when Read/Write is enabled. " +
        "Runtime texture estimates feed shared memory and scoring; editor-only profiler copies are excluded.";

    private string TextureMemoryDescription(TextureInfo info) =>
        $"Imported: {FormatBytes(info.importedBytes)}\nRuntime: {FormatBytes(info.memoryBytes)}";
}
