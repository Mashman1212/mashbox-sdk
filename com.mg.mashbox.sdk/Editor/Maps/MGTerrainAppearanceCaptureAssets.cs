using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainAppearanceCaptureAssets
    {
        internal const string ColourProperty = "_FarRangeAppearanceMap";
        internal const string NormalProperty = "_FarRangeAppearanceNormalMap";

        internal static Texture2D Assigned(Material material, string property)
            => material != null && material.HasProperty(property) ? material.GetTexture(property) as Texture2D : null;

        internal static string TerrainPrefix(string terrainName)
        {
            string safeName = string.IsNullOrWhiteSpace(terrainName) ? "Terrain" : terrainName.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars()) safeName = safeName.Replace(invalid, '_');
            safeName = safeName.TrimEnd('.', ' ');
            return (string.IsNullOrEmpty(safeName) ? "Terrain" : safeName) + "_";
        }

        internal static string WithTerrainPrefix(string path, string terrainName)
        {
            string prefix = TerrainPrefix(terrainName);
            string filename = Path.GetFileName(path);
            if (!filename.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) filename = prefix + filename;
            return Path.Combine(Path.GetDirectoryName(path) ?? "", filename).Replace('\\', '/');
        }

        internal static string ReusablePath(Texture2D texture, string terrainName)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            // Captures are PNGs. Never write PNG bytes over an .asset, source
            // image of another format, built-in texture or package resource.
            return path.StartsWith("Assets/", StringComparison.Ordinal)
                && Path.GetFileName(path).StartsWith(TerrainPrefix(terrainName), StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)
                && AssetImporter.GetAtPath(path) is TextureImporter ? path : null;
        }

        internal static string NormalPath(Texture2D assigned, string colourPath, string terrainName)
        {
            string existing = ReusablePath(assigned, terrainName);
            if (!string.IsNullOrEmpty(existing) && !string.Equals(existing, colourPath, StringComparison.OrdinalIgnoreCase))
                return existing;
            return AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(colourPath, null) + "_NormalWS.png");
        }

        internal static void WritePng(string assetPath, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) throw new InvalidOperationException("Capture PNG is empty.");
            string root = Path.GetFullPath(Application.dataPath);
            string absolute = Path.GetFullPath(Path.Combine(root, "..", assetPath));
            if (!absolute.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetExtension(absolute), ".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Capture destination must be a PNG inside Assets.");
            if (File.Exists(absolute) && !AssetDatabase.MakeEditable(assetPath))
                throw new IOException("Cannot write the assigned capture texture: " + assetPath);
            // Replace only after the complete image is written. Keep the .meta
            // file so references and GUIDs survive every recapture.
            string temporary = absolute + "." + Guid.NewGuid().ToString("N") + ".capturetmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(absolute)) File.Replace(temporary, absolute, null);
                else File.Move(temporary, absolute);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        internal static void Assign(Material material, string property, Texture2D texture)
        {
            if (material == null || texture == null || !material.HasProperty(property)
                || material.GetTexture(property) == texture) return;
            Undo.RecordObject(material, "Assign Terrain Appearance Capture");
            material.SetTexture(property, texture);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
        }
    }
}
