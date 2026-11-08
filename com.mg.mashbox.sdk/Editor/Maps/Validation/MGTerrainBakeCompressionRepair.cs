using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainBakeCompressionRepair
    {
        [MenuItem("Tools/MashBox/MG Terrain/Compress Existing Baked Appearance Maps")]
        internal static void Run()
        {
            var tiles = Selection.gameObjects.SelectMany(go => go.GetComponentsInChildren<MGTerrain>(true)).Distinct().ToArray();
            if (tiles.Length == 0)
            {
                EditorUtility.DisplayDialog("MG Terrain Compression", "Select an MG Terrain world or terrain tile to compress its existing baked colour and world-normal maps.", "OK");
                return;
            }
            try { Debug.Log($"[MG Terrain] Compressed {Repair(tiles):N0} baked appearance maps. Texture GUIDs and material assignments are preserved."); }
            catch (OperationCanceledException) { Debug.Log("[MG Terrain] Compression canceled; original assets restored."); }
            catch (Exception exception) { Debug.LogException(exception); }
            finally { EditorUtility.ClearProgressBar(); }
        }

        internal static int Repair(IEnumerable<MGTerrain> tiles)
        {
            var maps = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var tile in tiles)
            {
                if (tile == null || tile.MeshRenderer == null) continue;
                foreach (var material in tile.MeshRenderer.sharedMaterials)
                    foreach (var property in new[] { MGTerrainAppearanceCaptureAssets.ColourProperty, MGTerrainAppearanceCaptureAssets.NormalProperty })
                    {
                        var texture = MGTerrainAppearanceCaptureAssets.Assigned(material, property);
                        string path = AssetDatabase.GetAssetPath(texture);
                        if (texture == null || !IsGeneratedAppearancePath(path)) continue;
                        bool linear = property == MGTerrainAppearanceCaptureAssets.NormalProperty;
                        if (maps.TryGetValue(path, out bool previous) && previous != linear)
                            throw new InvalidOperationException("One baked texture is assigned as both colour and world-normal data: " + path);
                        maps[path] = linear;
                    }
            }
            using var transaction = new MGTerrainAssetTransaction();
            int index = 0;
            foreach (var pair in maps)
            {
                if (EditorUtility.DisplayCancelableProgressBar("Compress MG Terrain Baked Maps", pair.Key, index / (float)Math.Max(1, maps.Count)))
                    throw new OperationCanceledException();
                if (AssetImporter.GetAtPath(pair.Key) is TextureImporter importer)
                {
                    transaction.Track(pair.Key);
                    MGTerrainBakeTextureCompression.Configure(importer, pair.Value, importer.maxTextureSize);
                    importer.SaveAndReimport();
                }
                else
                {
                    var source = AssetDatabase.LoadAssetAtPath<Texture2D>(pair.Key);
                    if (source == null || source.format == TextureFormat.R16 || source.format == TextureFormat.RFloat || source.format == TextureFormat.R8)
                        throw new InvalidOperationException("An appearance slot references a precision data map: " + pair.Key);
                    var resources = new List<Object>();
                    try
                    {
                        var readable = PrefabStampAppearance.CloneMap(source, source.width, pair.Value, Color.black, resources);
                        MGTerrainAssetStore.SaveMap(readable, pair.Key, pair.Value, transaction);
                    }
                    finally { foreach (var resource in resources) if (resource != null) Object.DestroyImmediate(resource); }
                }
                index++;
            }
            transaction.Commit();
            return index;
        }

        internal static bool IsGeneratedAppearancePath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)) return false;
            string extension = Path.GetExtension(path);
            if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".asset", StringComparison.OrdinalIgnoreCase)) return false;
            string name = Path.GetFileNameWithoutExtension(path);
            return new[] { "_Distant", "_Distant_NormalWS", "_Appearance", "_Appearance_NormalWS", "_FarRangeAppearanceMap", "_FarRangeAppearanceNormalMap" }
                .Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }
    }
}
