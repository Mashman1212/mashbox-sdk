using System;
using System.IO;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainSceneAssets
    {
        internal static string SafeName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string name = new string((value ?? "").Select(c => char.IsControl(c) || invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
            return string.IsNullOrEmpty(name) ? "Terrain" : name;
        }
        internal static string Folder(MGTerrain terrain)
        {
            var scene = terrain.gameObject.scene;
            string path = scene.path;
            string parent = !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal)
                ? Path.GetDirectoryName(path).Replace('\\', '/') : "Assets";
            string sceneName = string.IsNullOrEmpty(path) ? "Unsaved Scene" : Path.GetFileNameWithoutExtension(path);
            string folderName = SafeName(sceneName) + "_MG Terrain Data";
            string folder = parent + "/" + folderName;
            if (!AssetDatabase.IsValidFolder(folder))
            {
                string guid = AssetDatabase.CreateFolder(parent, folderName);
                if (string.IsNullOrEmpty(guid)) throw new IOException("Cannot create terrain data folder: " + folder);
                folder = AssetDatabase.GUIDToAssetPath(guid);
            }
            return folder;
        }
        internal static string UniquePath(MGTerrain terrain, string purpose, string extension = ".asset")
        {
            var world = terrain.World != null ? terrain.World : terrain.GetComponentInParent<MGTerrainWorld>(true);
            string prefix = SafeName(world != null ? world.name : "MG Terrain") + "_" + SafeName(terrain.name);
            return AssetDatabase.GenerateUniqueAssetPath(Folder(terrain) + "/" + prefix + "_" + SafeName(purpose) + extension);
        }
        internal static void Create(UnityEngine.Object asset, MGTerrain terrain, string purpose)
        {
            string path = UniquePath(terrain, purpose);
            asset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
        }
    }
}
