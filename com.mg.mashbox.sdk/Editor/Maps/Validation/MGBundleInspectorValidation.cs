#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Threading;
using MashBoxSDK.MapTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class MGBundleInspectorValidation
{
    static MGBundleInspectorValidation() { EditorApplication.delayCall += Requested; }
    private static void Requested()
    {
        string request = Path.Combine(Path.GetTempPath(), "mg-bundle-inspector.request");
        if (!File.Exists(request) || File.ReadAllText(request).Trim().Replace('\\', '/') != Application.dataPath.Replace('\\', '/')) return;
        File.Delete(request);
        Run();
    }

    [MenuItem("Tools/MashBox/Validation/Bundle Inspector")]
    internal static void Run()
    {
        string folder = "Assets/__MGBundleInspectorValidation_" + Guid.NewGuid().ToString("N");
        string output = Path.Combine(Path.GetTempPath(), "MGBundleInspectorValidation", Guid.NewGuid().ToString("N"));
        string resultPath = Path.Combine(Path.GetTempPath(), "mg-bundle-inspector-validation.txt");
        var original = SceneManager.GetActiveScene();
        Scene scene = default;
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        try
        {
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            string shaderPath = folder + "/Inspector.shader";
            File.WriteAllText(shaderPath, "Shader \"Hidden/MashBox/BundleInspectorValidation\" { Properties { _MainTex (\"Texture\", 2D) = \"white\" {} _Layers (\"Layers\", 2DArray) = \"\" {} } SubShader { Pass { } } }");
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
            var texture = new Texture2D(8, 8) { name = "MG Inspector, \"Texture\"" };
            texture.Apply();
            var array = new Texture2DArray(8, 8, 4, TextureFormat.RGBA32, true) { name = "MG Detail Array" };
            array.Apply();
            var mesh = new Mesh { name = "MG Inspector Mesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward }, triangles = new[] { 0, 1, 2 } };
            var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(shaderPath)) { name = "MG Inspector Material" };
            AssetDatabase.CreateAsset(texture, folder + "/Texture.asset");
            AssetDatabase.CreateAsset(array, folder + "/Array.asset");
            AssetDatabase.CreateAsset(mesh, folder + "/Mesh.asset");
            material.SetTexture("_MainTex", texture);
            material.SetTexture("_Layers", array);
            AssetDatabase.CreateAsset(material, folder + "/Material.mat");
            texture.name = "MG Inspector, \"Texture\"";
            array.name = "MG Detail Array";
            mesh.name = "MG Inspector Mesh";
            material.name = "MG Inspector Material";
            foreach (var asset in new UnityEngine.Object[] { texture, array, mesh, material }) EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var go = new GameObject("Bundle inspector validation object");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            string scenePath = folder + "/Scene.unity";
            Check(EditorSceneManager.SaveScene(scene, scenePath), "Could not save validation scene.");
            SceneManager.SetActiveScene(original);
            var reader = new MGBundleArchiveReader(Path.Combine(EditorApplication.applicationContentsPath, "Tools"));
            MGBundleInventory last = null;
            foreach (bool sceneBundle in new[] { false, true })
            {
                string buildFolder = Path.Combine(output, sceneBundle ? "Scene" : "Assets");
                Directory.CreateDirectory(buildFolder);
                var build = new AssetBundleBuild
                {
                    assetBundleName = "inspector-test.bundle",
                    assetNames = sceneBundle ? new[] { scenePath } : new[] { folder + "/Material.mat", folder + "/Mesh.asset" }
                };
                Check(BuildPipeline.BuildAssetBundles(buildFolder, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression, EditorUserBuildSettings.activeBuildTarget) != null, "Validation bundle build failed.");
                string bundle = Path.Combine(buildFolder, build.assetBundleName);
                last = reader.Inspect(bundle, CancellationToken.None);
                Check(last.Assets.Any(entry => entry.Category == MGBundleCategory.Meshes && entry.Name == mesh.name), "Embedded mesh missing from " + buildFolder);
                Check(last.Assets.Any(entry => entry.Category == MGBundleCategory.Materials && entry.Name == material.name), "Material missing from " + buildFolder);
                Check(last.Assets.Any(entry => entry.Type == "Texture2DArray" && entry.Name == array.name), "Texture array missing from " + buildFolder);
                Check(last.Assets.Any(entry => entry.Name == texture.name), "Texture name was not decoded correctly.");
                Check(MGBundleInspectorPanel.Filter(last, MGBundleCategory.Textures, "mg detail array", 0).Count == 1, "Category/search filtering failed.");
                Check(MGBundleInspectorPanel.BuildCsv(last).Contains("\"MG Inspector, \"\"Texture\"\"\""), "CSV quoting failed.");
                try { reader.Inspect(bundle + ".manifest", CancellationToken.None); throw new Exception("Manifest must be rejected."); }
                catch (InvalidDataException) { }
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    try { reader.Inspect(bundle, cancellation.Token); throw new Exception("Canceled inspection must stop."); }
                    catch (OperationCanceledException) { }
                }
                // A second inspection confirms handles were released and the archive can reopen.
                Check(reader.Inspect(bundle, CancellationToken.None).Assets.Count == last.Assets.Count, "Archive reopen changed inventory.");
            }
            string report = $"PASS: built asset and scene bundles; embedded textures, texture arrays, meshes and materials; decoded names; category/search filtering; CSV escaping; manifest rejection; cancellation; archive reopen. Scene inventory: {last.Assets.Count} objects in {last.Files.Count} files. Fixtures: {output}";
            File.WriteAllText(resultPath, report);
            Debug.Log(report);
        }
        catch (Exception exception) { File.WriteAllText(resultPath, "FAIL: " + exception); Debug.LogException(exception); }
        finally
        {
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            if (original.IsValid()) SceneManager.SetActiveScene(original);
            AssetDatabase.DeleteAsset(folder);
        }
    }
}
#endif
