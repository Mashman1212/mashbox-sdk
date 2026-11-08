using System;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainBakeTextureCompression
    {
        // Appearance captures contain colour or RGB-encoded WORLD-space normals.
        // Keep Default + linear for normals: NormalMap/BC5 importing would repack
        // channels and lose the third component expected by MG_Lit_Trail.
        internal static void Configure(TextureImporter importer, bool linear, int maximumSize)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = !linear;
            importer.convertToNormalmap = false;
            importer.flipGreenChannel = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.fadeout = false;
            importer.mipMapsPreserveCoverage = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = maximumSize;
            importer.isReadable = false;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.compressionQuality = 100;
            importer.crunchedCompression = false;

            var defaults = importer.GetDefaultPlatformTextureSettings();
            defaults.format = TextureImporterFormat.Automatic;
            defaults.textureCompression = TextureImporterCompression.CompressedHQ;
            defaults.compressionQuality = 100;
            defaults.crunchedCompression = false;
            defaults.maxTextureSize = maximumSize;
            importer.SetPlatformTextureSettings(defaults);

            // Explicit desktop format avoids Automatic selecting low precision BC1.
            // Other targets use their automatic high-quality colour compression.
            var desktop = importer.GetPlatformTextureSettings("Standalone");
            desktop.maxTextureSize = desktop.overridden ? Mathf.Min(desktop.maxTextureSize, maximumSize) : maximumSize;
            desktop.name = "Standalone";
            desktop.overridden = true;
            desktop.format = TextureImporterFormat.BC7;
            desktop.textureCompression = TextureImporterCompression.CompressedHQ;
            desktop.compressionQuality = 100;
            desktop.crunchedCompression = false;
            importer.SetPlatformTextureSettings(desktop);
        }

        internal static Texture2D CompressedNativeCopy(Texture2D generated, bool linear)
        {
            if (!generated.isReadable) throw new InvalidOperationException("Baked appearance data must be readable before saving.");
            var copy = new Texture2D(generated.width, generated.height, TextureFormat.RGBA32, true, linear)
            { name = generated.name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            try
            {
                copy.SetPixels32(generated.GetPixels32(0));
                copy.Apply(true, false);
                EditorUtility.CompressTexture(copy, TextureFormat.BC7, TextureCompressionQuality.Best);
                copy.Apply(false, false);
                return copy;
            }
            catch { UnityEngine.Object.DestroyImmediate(copy); throw; }
        }
    }
}
