using System;
using System.IO;
using System.Linq;
using MashBoxSDK.MapTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

[InitializeOnLoad]
internal static class MGTerrainBakeCompressionValidation
{
    static MGTerrainBakeCompressionValidation() { EditorApplication.delayCall += Requested; }
    private static void Requested()
    {
        string request = Path.Combine(Path.GetTempPath(), "mg-bake-compression.request");
        if (!File.Exists(request) || File.ReadAllText(request).Trim().Replace('\\', '/') != Application.dataPath.Replace('\\', '/')) return;
        File.Delete(request);
        Run();
    }

    [MenuItem("Tools/MashBox/MG Terrain/Validation/Baked Map Compression")]
    internal static void Run()
    {
        string folder = "Assets/__MGBakeCompression_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        var normal = new Texture2D(64, 64, TextureFormat.RGBA32, true, true);
        var colour = new Texture2D(64, 64, TextureFormat.RGBA32, true, false);
        var height = new Texture2D(8, 8, TextureFormat.RFloat, false, true);
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        string result = "";
        try
        {
            var normals = new Color32[4096];
            for (int z = 0; z < 64; z++) for (int x = 0; x < 64; x++)
            {
                float nx = (x / 63f - .5f) * 1.2f, nz = (z / 63f - .5f);
                var n = new Vector3(nx, Mathf.Sqrt(Mathf.Max(0, 1 - nx * nx - nz * nz)), nz);
                normals[z * 64 + x] = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1);
            }
            normal.SetPixels32(normals); normal.Apply(true, false);
            colour.SetPixels32(normals); colour.Apply(true, false);
            string normalPath = folder + "/Tile_Distant_NormalWS.png", colourPath = folder + "/Tile_Distant.png";
            Texture2D savedNormal;
            using (var transaction = new MGTerrainAssetTransaction())
            {
                savedNormal = MGTerrainAssetStore.SaveMap(normal, normalPath, true, transaction);
                MGTerrainAssetStore.SaveMap(colour, colourPath, false, transaction);
                transaction.Commit();
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(normalPath);
            Check(importer.textureType == TextureImporterType.Default && !importer.sRGBTexture && !importer.convertToNormalmap && !importer.flipGreenChannel,
                "World normals must retain raw linear RGB with no normal-map swizzle.");
            Check(importer.textureCompression == TextureImporterCompression.CompressedHQ && !importer.isReadable && importer.mipmapEnabled, "Normal capture import policy mismatch.");
            Check(importer.GetPlatformTextureSettings("Standalone").format == TextureImporterFormat.BC7, "Desktop captures must use BC7.");
            Check(((TextureImporter)AssetImporter.GetAtPath(colourPath)).sRGBTexture, "Colour captures must remain sRGB.");
            Check(GraphicsFormatUtility.IsCompressedFormat(savedNormal.graphicsFormat), "Saved world normal was not compressed.");

            // Sample the compressed image as the shader does: RGB * 2 - 1, normalized.
            var previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            var rt = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
            float maximumError = 0;
            try
            {
                GL.sRGBWrite = false; Graphics.Blit(savedNormal, rt); RenderTexture.active = rt;
                readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); readback.Apply();
                var decoded = readback.GetPixels32();
                for (int index = 0; index < decoded.Length; index++)
                {
                    Vector3 Decode(Color32 c) => new Vector3(c.r / 255f * 2 - 1, c.g / 255f * 2 - 1, c.b / 255f * 2 - 1).normalized;
                    maximumError = Mathf.Max(maximumError, Vector3.Angle(Decode(normals[index]), Decode(decoded[index])));
                }
                Check(maximumError < 5f, $"Normal RGB decoding changed; maximum angular error {maximumError:F2} degrees.");
            }
            finally { GL.sRGBWrite = previousSrgb; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(readback); }

            string guid = AssetDatabase.AssetPathToGUID(normalPath);
            var stale = importer.GetPlatformTextureSettings("Standalone"); stale.format = TextureImporterFormat.RGBA32; importer.SetPlatformTextureSettings(stale);
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
            using (var transaction = new MGTerrainAssetTransaction()) { MGTerrainAssetStore.SaveMap(normal, normalPath, true, transaction); transaction.Commit(); }
            Check(AssetDatabase.AssetPathToGUID(normalPath) == guid, "Rebaking must preserve capture references.");
            Check(((TextureImporter)AssetImporter.GetAtPath(normalPath)).GetPlatformTextureSettings("Standalone").format == TextureImporterFormat.BC7, "Rebake must replace stale uncompressed overrides.");
            string beforeMeta = File.ReadAllText(normalPath + ".meta");
            using (var transaction = new MGTerrainAssetTransaction()) { MGTerrainAssetStore.SaveMap(colour, normalPath, false, transaction); }
            Check(File.ReadAllText(normalPath + ".meta") == beforeMeta, "Canceled captures must restore importer settings.");

            string nativePath = folder + "/Tile_Appearance_NormalWS.asset";
            using (var transaction = new MGTerrainAssetTransaction()) { MGTerrainAssetStore.SaveMap(normal, nativePath, true, transaction); transaction.Commit(); }
            string nativeGuid = AssetDatabase.AssetPathToGUID(nativePath);
            using (var transaction = new MGTerrainAssetTransaction()) { MGTerrainAssetStore.SaveMap(normal, nativePath, true, transaction); transaction.Commit(); }
            Check(AssetDatabase.LoadAssetAtPath<Texture2D>(nativePath).format == TextureFormat.BC7 && AssetDatabase.AssetPathToGUID(nativePath) == nativeGuid,
                "Legacy native appearance maps must compress in place without changing GUIDs.");
            var raw = height.GetPixelData<float>(0);
            for (int index = 0; index < raw.Length; index++) raw[index] = index / 17f;
            height.Apply(false, false);
            var encoded = MGTerrainHeightEncoding.Encode(height, out _);
            try
            {
                var expected = encoded.GetPixelData<ushort>(0).ToArray();
                using (var transaction = new MGTerrainAssetTransaction()) { MGTerrainAssetStore.Save(encoded, folder + "/Tile_Distant_Height.asset", transaction); transaction.Commit(); }
                var savedHeight = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/Tile_Distant_Height.asset");
                Check(savedHeight.format == TextureFormat.R16 && expected.SequenceEqual(savedHeight.GetPixelData<ushort>(0).ToArray()), "Height data must retain exact R16 values.");
            }
            finally { if (!EditorUtility.IsPersistent(encoded)) UnityEngine.Object.DestroyImmediate(encoded); }
            Check(!MGTerrainBakeCompressionRepair.IsGeneratedAppearancePath(folder + "/Tile_Distant_Height.asset") &&
                !MGTerrainBakeCompressionRepair.IsGeneratedAppearancePath(folder + "/Tile_Layer_0_Density.asset"), "Repair must exclude height and paint data.");
            result = $"PASS: colour and world-normal PNG compression; BC7 desktop format; linear RGB normal decoding (max error {maximumError:F2} degrees); sRGB colour; stale override replacement; repeated-bake GUID stability; importer rollback; native appearance BC7; exact R16 height preservation; repair excludes precision data.";
            Debug.Log(result);
        }
        catch (Exception exception) { result = "FAIL: " + exception; Debug.LogException(exception); }
        finally
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "mg-bake-compression-result.txt"), result);
            foreach (var texture in new[] { normal, colour, height }) UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.DeleteAsset(folder);
            EditorUtility.ClearProgressBar();
        }
    }
}
