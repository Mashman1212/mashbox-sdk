#if UNITY_EDITOR_WIN
using System.Collections.Generic;
using MashBoxSDK.Exporting;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.ContentTools.Editor
{
    public class MapContentDatabase : ScriptableObject
    {
        public const string AssetFolder = "Assets/Content/Map Pack Data";
        public const string AssetPath = AssetFolder + "/MapContentDatabase.asset";

        public List<MapContentPackDefinition> Packs = new();
        public List<MapBundleEntry> entries = new();

        public static MapContentDatabase GetOrCreate()
        {
            EnsureFolderExists("Assets/Content");
            EnsureFolderExists(AssetFolder);

            var database = AssetDatabase.LoadAssetAtPath<MapContentDatabase>(AssetPath);
            if (database != null)
                return database;

            database = CreateInstance<MapContentDatabase>();
            AssetDatabase.CreateAsset(database, AssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return database;
        }

        private static void EnsureFolderExists(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
                return;

            var parent = System.IO.Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            var folder = System.IO.Path.GetFileName(assetPath);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolderExists(parent);

            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(folder))
                AssetDatabase.CreateFolder(parent, folder);
        }
    }
}
#endif
