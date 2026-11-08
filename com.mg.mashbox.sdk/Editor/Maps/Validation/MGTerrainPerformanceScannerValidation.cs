#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
internal static class MGTerrainPerformanceScannerValidation
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static MGTerrainPerformanceScannerValidation() { EditorApplication.delayCall += Requested; }
    private static void Requested()
    {
        string request = Path.Combine(Path.GetTempPath(), "mg-performance-scanner.request");
        if (!File.Exists(request) || File.ReadAllText(request).Trim().Replace('\\', '/') != Application.dataPath.Replace('\\', '/')) return;
        File.Delete(request);
        Run();
        string repaintRequest = Path.Combine(Path.GetTempPath(), "mg-performance-scanner-repaint.request");
        if (File.Exists(repaintRequest))
        {
            File.Delete(repaintRequest);
            MGTerrainScannerRepaintValidation.Begin();
        }
        string scanRequest = Path.Combine(Path.GetTempPath(), "mg-performance-scanner-scene.request");
        if (File.Exists(scanRequest))
        {
            File.Delete(scanRequest);
            var panel = new MapPerformanceScannerPanel();
            var result = panel.ScanScene();
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "mg-performance-scanner-scene.txt"),
                result == null ? "Cancelled" : (string)typeof(MapPerformanceScannerPanel).GetMethod("BuildTextReport", Flags).Invoke(panel, null));
        }
    }

    [MenuItem("Tools/MashBox/MG Terrain/Validation/Performance Scanner")]
    public static void Run()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("MG scanner validation");
        root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward }, triangles = new[] { 0, 1, 2 } };
        var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
        var map = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        var array = new Texture2DArray(8, 8, 4, TextureFormat.RGBA32, true);
        var compressed = new Texture2D(8, 8, TextureFormat.DXT1, true);
        var compressedArray = new Texture2DArray(8, 8, 4, TextureFormat.BC7, true);
        string sampleMemory = "";
        string output = Path.Combine(Path.GetTempPath(), "mg-performance-scanner.txt");
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        object Field(object source, string name) => source.GetType().GetField(name, Flags).GetValue(source);
        try
        {
            Check(MapPerformanceScannerPanel.EstimateTextureStorageBytes(array) == 1360, "Array memory must include all four slices and every mip.");
            Check(MapPerformanceScannerPanel.EstimateTextureStorageBytes(map) == 256, "2D texture memory changed unexpectedly.");
            Check(MapPerformanceScannerPanel.EstimateTextureStorageBytes(compressed) == 56, "DXT1 block sizes and small mip levels must be respected.");
            Check(MapPerformanceScannerPanel.EstimateTextureStorageBytes(compressedArray) == 448, "BC7 arrays must count block-rounded mips across every slice.");
            var readableMemory = MapPerformanceScannerPanel.MeasureTextureMemory(compressed);
            Check(readableMemory.GpuBytes == 56 && readableMemory.CpuBytes == 56 && readableMemory.RuntimeBytes == 112, "Readable CPU copies must be explicit in runtime totals.");
            compressed.Apply(false, true);
            Check(MapPerformanceScannerPanel.MeasureTextureMemory(compressed).RuntimeBytes == 56, "Unreadable textures must not include editor CPU copies.");
            var sample = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Maps/Cotswold Ridge/[00] SCENES/COTSWOLD RIDGE [YAKY] [V01]/MG Terrain Data/MG Terrain World_Tile_-2_-2_Distant_NormalWS.png");
            if (sample != null)
            {
                var estimate = MapPerformanceScannerPanel.MeasureTextureMemory(sample);
                var storage = typeof(Editor).Assembly.GetType("UnityEditor.TextureUtil").GetMethod("GetStorageMemorySizeLong", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                Check(estimate.ImportedBytes == (long)storage.Invoke(null, new object[] { sample }), "Imported bytes must match the texture Inspector.");
                Check(estimate.CpuBytes == 0, "The non-readable imported normal texture must not count the Editor's CPU cache.");
                sampleMemory = $" Sample texture: imported {EditorUtility.FormatBytes(estimate.ImportedBytes)}, runtime GPU {EditorUtility.FormatBytes(estimate.GpuBytes)}, CPU copy {EditorUtility.FormatBytes(estimate.CpuBytes)}, format {sample.graphicsFormat}, mip levels {sample.mipmapCount}.";
            }
            var terrains = new List<MGTerrain>();
            for (int index = 0; index < 2; index++)
            {
                var go = new GameObject("Tile " + index);
                go.transform.SetParent(root.transform, false);
                var terrain = go.AddComponent<MGTerrain>();
                terrain.MeshFilter.sharedMesh = mesh;
                terrain.MeshRenderer.sharedMaterial = material;
                terrain.SetControlMaps(map, map);
                int prototype = terrain.FindOrAddPrototype(mesh, material, MGTerrain.InstanceKind.Detail);
                terrain.AddDensityDetailLayer(prototype, map, 1, 1, 1, 1, 42, 10000);
                terrain.AddInstanceLocal(prototype, Vector3.zero, Quaternion.identity, Vector3.one);
                var meshes = new List<Mesh>();
                terrain.GetPerformanceSourceMeshes(prototype, meshes, out int parts, out long vertices);
                Check(meshes.Count == 1 && parts == 1 && vertices == 3, "Direct mesh prototypes must expose exactly one source part.");
                Check(terrain.CaptureMemoryUsageSnapshot().CachedDetailCellCount == 0, "Resource inspection must not build streaming cells.");
                terrains.Add(terrain);
            }
            var panel = new MapPerformanceScannerPanel();
            panel.CollectMgTerrains(terrains);
            Check(((IList)Field(panel, "mgTerrainInfos")).Count == 2, "Both MG tiles must be scanned.");
            Check(((IList)Field(panel, "meshInfos")).Count == 1, "Shared surface and prototype meshes must be counted once across tiles.");
            Check(((HashSet<Texture>)Field(panel, "terrainTextures")).Contains(map), "Control and density maps must be collected.");
            Check((long)Field(panel, "mgTerrainDetailDraws") == 0 && (long)Field(panel, "mgTerrainPlacedDraws") == 0, "Inactive terrain must retain resource accounting without submitting draws.");
            Check((long)Field(panel, "mgTerrainResidentBytes") >= 96, "Serialized instance memory must be included for both tiles.");
            Check(MapPerformanceScannerPanel.ClampReportPage(100, 0) == 0, "Empty reports must stay on page zero.");
            Check(MapPerformanceScannerPanel.ClampReportPage(100, 26) == 1, "Pages must clamp after report size changes.");
            Check(MapPerformanceScannerPanel.ClampReportPage(-1, 200) == 0, "Negative pages must clamp.");
            var ensureCache = typeof(MapPerformanceScannerPanel).GetMethod("EnsureDisplayCache", Flags);
            ensureCache.Invoke(panel, null);
            var cachedRows = Field(panel, "cachedLargestMeshes");
            long cachedBytes = (long)Field(panel, "cachedMgTerrainMeshBytes");
            for (int iteration = 0; iteration < 100; iteration++) ensureCache.Invoke(panel, null);
            Check(ReferenceEquals(cachedRows, Field(panel, "cachedLargestMeshes")) && (long)Field(panel, "cachedMgTerrainMeshBytes") == cachedBytes,
                "GUI redraws must reuse sorted rows and MG memory totals.");
            string report = "PASS: Inspector storage match; compressed blocks/mips/arrays; explicit readable CPU copies; display cache reuse; pagination bounds; texture array slices/mips; 2D texture size; direct mesh prototypes; shared mesh deduplication across MG tiles; control/density maps; inactive draw suppression; serialized instance memory; source inspection does not build detail cells." + sampleMemory;
            File.WriteAllText(output, report);
            Debug.Log(report);
        }
        catch (Exception exception) { File.WriteAllText(output, "FAIL: " + exception); Debug.LogException(exception); }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            foreach (var resource in new UnityEngine.Object[] { mesh, material, map, array, compressed, compressedArray }) UnityEngine.Object.DestroyImmediate(resource);
            EditorSceneManager.ClosePreviewScene(scene);
            EditorUtility.ClearProgressBar();
        }
    }
}
#endif
