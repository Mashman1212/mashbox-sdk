#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.ContentTools
{
    /// <summary>Persists skinned mesh probe anchors on the owning content prefab.</summary>
    internal static class ContentPackProbeAnchors
    {
        internal const int MigrationVersion = 1;
        private static readonly ConcurrentQueue<ContentPackDefinition> Pending = new ConcurrentQueue<ContentPackDefinition>();
        private static bool scanExistingPacks;
        private static bool processing;

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            scanExistingPacks = true;
            EditorApplication.update += ProcessPending;
        }

        // OnValidate can run on a loading thread. Asset operations happen on the next safe editor update.
        internal static void Schedule(ContentPackDefinition pack) => Pending.Enqueue(pack);

        private static void ProcessPending()
        {
            if (processing || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode ||
                UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null) return;

            processing = true;
            try
            {
                if (scanExistingPacks)
                {
                    scanExistingPacks = false;
                    foreach (var guid in AssetDatabase.FindAssets("t:ContentPackDefinition"))
                    {
                        var pack = AssetDatabase.LoadAssetAtPath<ContentPackDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                        if (pack != null && pack.probeAnchorMigrationVersion < MigrationVersion)
                            Schedule(pack);
                    }
                }

                var packs = new HashSet<ContentPackDefinition>();
                while (Pending.TryDequeue(out var pack))
                    if (pack != null) packs.Add(pack);

                foreach (var pack in packs)
                    EnsurePack(pack);
            }
            finally { processing = false; }
        }

        internal static bool EnsurePack(ContentPackDefinition pack)
        {
            if (pack == null) return true;
            if (AssetDatabase.IsAssetImportWorkerProcess() || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Schedule(pack);
                return false;
            }

            bool success = true;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (pack._items != null)
                foreach (var item in pack._items)
                    if (item != null) paths.Add(AssetDatabase.GetAssetPath(item));

            if (pack.IsCorePack)
            {
                if (pack._coreBundleItems != null)
                    foreach (var item in pack._coreBundleItems)
                        if (item is GameObject) paths.Add(AssetDatabase.GetAssetPath(item));

                // Match the core builder's staged content as well as its explicit items.
                if (AssetDatabase.IsValidFolder(ContentPackDefinition.CORE_STAGING_ROOT))
                    foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { ContentPackDefinition.CORE_STAGING_ROOT }))
                        paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            foreach (var path in paths)
            {
                try { EnsurePrefab(path); }
                catch (Exception error)
                {
                    success = false;
                    Debug.LogError($"[Content Pack Lighting] '{pack.name}', '{path}': {error.Message}", pack);
                }
            }

            // A failed pack remains eligible for migration on the next editor load or sync.
            if (success && pack.probeAnchorMigrationVersion < MigrationVersion)
            {
                try
                {
                    var path = AssetDatabase.GetAssetPath(pack);
                    if (!string.IsNullOrEmpty(path)) RequireWritable(path);
                    pack.probeAnchorMigrationVersion = MigrationVersion;
                    EditorUtility.SetDirty(pack);
                    if (!string.IsNullOrEmpty(path))
                    {
                        AssetDatabase.SaveAssetIfDirty(pack);
                        if (EditorUtility.IsDirty(pack)) throw new IOException("Unity could not save the migration version.");
                    }
                }
                catch (Exception error)
                {
                    pack.probeAnchorMigrationVersion = 0;
                    success = false;
                    Debug.LogError($"[Content Pack Lighting] Could not save migration for '{pack.name}': {error.Message}", pack);
                }
            }
            return success;
        }

        internal static bool EnsurePrefab(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("Content items must be saved prefab assets.");
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) throw new InvalidOperationException("Could not load content prefab.");
            if (!NeedsRepair(asset)) return false;
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Save this skinned model as a .prefab so its probe anchors can be overridden.");

            RequireWritable(path);
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == path)
                throw new InvalidOperationException("Close this prefab's Prefab Mode, then sync the pack to apply the lighting fix.");

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer.probeAnchor == root.transform) continue;
                    renderer.probeAnchor = root.transform;
                    if (PrefabUtility.IsPartOfPrefabInstance(renderer))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                if (!saved) throw new IOException("Unity could not save the repaired prefab.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return true;
        }

        private static bool NeedsRepair(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.probeAnchor != root.transform) return true;
            return false;
        }

        private static void RequireWritable(string path)
        {
            var physicalPath = path;
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            if (package != null)
                physicalPath = Path.Combine(package.resolvedPath, path.Substring(package.assetPath.Length).TrimStart('/'));
            if (!AssetDatabase.IsOpenForEdit(path) || (File.GetAttributes(physicalPath) & FileAttributes.ReadOnly) != 0)
                throw new IOException($"'{path}' is read-only. Make it writable and sync the pack again.");
        }
    }
}
#endif
