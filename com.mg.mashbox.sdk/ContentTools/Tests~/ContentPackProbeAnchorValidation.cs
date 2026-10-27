using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.ContentTools
{
    // Run in an isolated project. Entry points exit the editor.
    public static class ContentPackProbeAnchorValidation
    {
        private const string Folder = "Assets/ProbeAnchorValidation";
        private static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
            Debug.Log("PASS: " + message);
        }

        private static GameObject CreatePrefab(string name, bool skinned = true)
        {
            var root = new GameObject(name);
            var child = new GameObject("Inactive mesh");
            child.transform.SetParent(root.transform);
            if (skinned)
            {
                var renderer = child.AddComponent<SkinnedMeshRenderer>();
                renderer.rootBone = child.transform;
                renderer.probeAnchor = child.transform;
                root.AddComponent<SkinnedMeshRenderer>();
            }
            else child.AddComponent<MeshRenderer>().probeAnchor = child.transform;
            child.SetActive(false);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{Folder}/{name}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void CheckAnchors(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Check(root.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r => r.probeAnchor == root.transform),
                    path + " anchors persist and reference the outer prefab root");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static void Run()
        {
            try
            {
                AssetDatabase.CreateFolder("Assets", "ProbeAnchorValidation");
                var plain = CreatePrefab("Plain", false);
                var plainPath = AssetDatabase.GetAssetPath(plain);
                var plainBefore = File.ReadAllText(plainPath);
                Check(!ContentPackProbeAnchors.EnsurePrefab(plainPath) && plainBefore == File.ReadAllText(plainPath),
                    "Non-skinned prefab remains byte-for-byte unchanged");

                var item = CreatePrefab("Item");
                var itemPath = AssetDatabase.GetAssetPath(item);
                Check(ContentPackProbeAnchors.EnsurePrefab(itemPath), "Repairs null and custom anchors, including inactive children");
                CheckAnchors(itemPath);
                Check(item.GetComponentInChildren<SkinnedMeshRenderer>(true) != null, "Asset stays available after save");
                var itemBefore = File.ReadAllText(itemPath);
                Check(!ContentPackProbeAnchors.EnsurePrefab(itemPath) && itemBefore == File.ReadAllText(itemPath), "Second repair does not rewrite prefab");
                var childRenderer = item.transform.GetChild(0).GetComponent<SkinnedMeshRenderer>();
                Check(childRenderer.rootBone == childRenderer.transform, "Root bone remains unchanged");

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(item);
                var variantRenderer = instance.transform.GetChild(0).GetComponent<SkinnedMeshRenderer>();
                variantRenderer.probeAnchor = variantRenderer.transform;
                PrefabUtility.RecordPrefabInstancePropertyModifications(variantRenderer);
                var variantPath = Folder + "/Variant.prefab";
                PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
                Object.DestroyImmediate(instance);
                Check(ContentPackProbeAnchors.EnsurePrefab(variantPath), "Repairs explicit variant overrides");
                CheckAnchors(variantPath);
                Check(PrefabUtility.GetPrefabAssetType(AssetDatabase.LoadAssetAtPath<GameObject>(variantPath)) == PrefabAssetType.Variant,
                    "Variant retains its prefab inheritance");

                var outer = new GameObject("Outer");
                var nested = (GameObject)PrefabUtility.InstantiatePrefab(item);
                nested.transform.SetParent(outer.transform);
                var outerPath = Folder + "/Outer.prefab";
                var outerAsset = PrefabUtility.SaveAsPrefabAsset(outer, outerPath);
                Object.DestroyImmediate(outer);
                Check(ContentPackProbeAnchors.EnsurePrefab(outerPath), "Nested prefab anchor is overridden to the outer root");
                CheckAnchors(outerPath);
                Check(File.ReadAllText(itemPath) == itemBefore, "Nested source prefab is not modified by outer repair");

                var pack = ScriptableObject.CreateInstance<ContentPackDefinition>();
                pack._items.Add(item);
                pack._items.Add(outerAsset);
                pack._items.Add(item);
                pack._items.Add(null);
                AssetDatabase.CreateAsset(pack, Folder + "/Pack.asset");
                Check(ContentPackProbeAnchors.EnsurePack(pack), "Pack handles shared/duplicate and null entries");
                Check(pack.probeAnchorMigrationVersion == ContentPackProbeAnchors.MigrationVersion, "Successful migration is stamped");
                var packBefore = File.ReadAllText(Folder + "/Pack.asset");
                Check(ContentPackProbeAnchors.EnsurePack(pack) && packBefore == File.ReadAllText(Folder + "/Pack.asset"),
                    "Completed migration does not rewrite pack");

                var later = CreatePrefab("AddedLater");
                pack._items.Add(later);
                Check(ContentPackProbeAnchors.EnsurePack(pack), "Items added after migration are still enforced");
                CheckAnchors(AssetDatabase.GetAssetPath(later));

                var core = ScriptableObject.CreateInstance<ContentPackDefinition>();
                core.IsCorePack = true;
                var coreItem = CreatePrefab("CoreItem");
                core._coreBundleItems = new System.Collections.Generic.List<Object> { coreItem };
                Check(ContentPackProbeAnchors.EnsurePack(core), "Explicit core pack prefabs are covered");
                CheckAnchors(AssetDatabase.GetAssetPath(coreItem));
                Object.DestroyImmediate(core);

                var locked = CreatePrefab("Locked");
                var lockedPath = AssetDatabase.GetAssetPath(locked);
                var retry = ScriptableObject.CreateInstance<ContentPackDefinition>();
                retry._items.Add(locked);
                File.SetAttributes(lockedPath, File.GetAttributes(lockedPath) | FileAttributes.ReadOnly);
                try
                {
                    Check(!ContentPackProbeAnchors.EnsurePack(retry) && retry.probeAnchorMigrationVersion == 0,
                        "Failed save is not marked migrated (expected error above)");
                }
                finally { File.SetAttributes(lockedPath, File.GetAttributes(lockedPath) & ~FileAttributes.ReadOnly); }
                Check(ContentPackProbeAnchors.EnsurePack(retry), "Failed migration can be retried successfully");
                Object.DestroyImmediate(retry);

                // Leave an unmigrated pack on disk for the next process to discover automatically.
                var legacy = ScriptableObject.CreateInstance<ContentPackDefinition>();
                legacy._items.Add(CreatePrefab("LegacyItem"));
                AssetDatabase.CreateAsset(legacy, Folder + "/Legacy.asset");
                AssetDatabase.SaveAssets();
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        public static void AfterRestart()
        {
            // Allow the production update callback to discover and migrate the legacy pack.
            EditorApplication.update += VerifyMigration;
        }

        private static void VerifyMigration()
        {
            if (EditorApplication.isUpdating || EditorApplication.isCompiling) return;
            EditorApplication.update -= VerifyMigration;
            try
            {
                var pack = AssetDatabase.LoadAssetAtPath<ContentPackDefinition>(Folder + "/Legacy.asset");
                Check(pack.probeAnchorMigrationVersion == ContentPackProbeAnchors.MigrationVersion,
                    "Startup automatically discovers and migrates existing packs");
                CheckAnchors(Folder + "/LegacyItem.prefab");
                Check(File.ReadAllText(Folder + "/Legacy.asset").Contains("probeAnchorMigrationVersion: 1"),
                    "Migration completion is persisted on disk");
                var migrated = AssetDatabase.LoadAssetAtPath<ContentPackDefinition>(Folder + "/Pack.asset");
                Check(migrated.probeAnchorMigrationVersion == ContentPackProbeAnchors.MigrationVersion,
                    "Migration marker survives editor restart");
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
