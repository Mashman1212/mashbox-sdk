using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    // Isolated project only: requires a disposable skinned FBX at this path and exits Unity.
    public static class StudioSkinningProbeAnchorValidation
    {
        private const string ModelPath = "Assets/SkinnerProbe.fbx";
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            Debug.Log("PASS: " + message);
        }

        private static void CheckModel()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Check(renderers.Length > 0, "Fixture contains imported skinned meshes");
            Check(renderers.All(renderer => renderer.probeAnchor == root.transform), "Imported FBX anchors reference its root");
        }

        public static void Run()
        {
            try
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
                importer.userData = "OtherTool:preserve me\r\n";
                importer.SaveAndReimport();
                var unmarked = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                Check(unmarked.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r => r.probeAnchor == null),
                    "Unmarked FBX is unaffected");
                string previousData = importer.userData;
                Check(StudioSkinningProbeAnchors.Configure(importer), "First export configures the importer policy");
                var data = importer.userData;
                Check(data.StartsWith(previousData, StringComparison.Ordinal), "Unrelated importer metadata is preserved");
                Check(!StudioSkinningProbeAnchors.Configure(importer) && importer.userData == data, "Repeated export configuration is idempotent");
                importer.SaveAndReimport();
                CheckModel();
                AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                CheckModel();
                Check(((ModelImporter)AssetImporter.GetAtPath(ModelPath)).userData == data, "Policy survives forced reimport");
                var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
                try
                {
                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/SkinnerProbe.prefab");
                    Check(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r => r.probeAnchor == prefab.transform),
                        "Prefab created from exported model retains root anchors");
                }
                finally { Object.DestroyImmediate(root); }
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        public static void AfterRestart()
        {
            try
            {
                CheckModel();
                Check(!StudioSkinningProbeAnchors.Configure((ModelImporter)AssetImporter.GetAtPath(ModelPath)),
                    "Export policy persists across editor restarts");
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
