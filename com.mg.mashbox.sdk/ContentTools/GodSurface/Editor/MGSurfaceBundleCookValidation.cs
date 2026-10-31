using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MashBoxSDK.Exporting;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
namespace MG.GodSurface.Editor
{
    public static class MGSurfaceBundleCookValidation
    {
        public static string Run()
        {
            var log = new StringBuilder();
            void Check(bool pass, string text) { if (!pass) throw new Exception(text); log.AppendLine("PASS " + text); }
            var active = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            string folder = AssetDatabase.GenerateUniqueAssetPath("Assets/Editor/GodPropBundleValidation");
            var baked = new List<string>();
            string scenePath = folder + "/Props.unity";
            try
            {
                AssetDatabase.CreateFolder("Assets/Editor", Path.GetFileName(folder));
                var material = new Material(Shader.Find(MGSurfaceBaker.SourceShader)); AssetDatabase.CreateAsset(material, folder + "/Source.mat");
                var root = new GameObject("Bundle prop root"); SceneManager.MoveGameObjectToScene(root, scene);
                var world = root.AddComponent<MGSurfaceWorld>(); world.roots = new[] { root.transform };
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(cube, scene);
                cube.transform.SetParent(root.transform, false); cube.GetComponent<MeshRenderer>().sharedMaterial = material;
                var collider = cube.GetComponent<BoxCollider>();
                world.cookResolution = 128; world.cookCompress = false; world.cookVertexAO = true; world.cookAOSamples = 16; world.cookAOReach = .25f;
                EditorSceneManager.SaveScene(scene, scenePath);
                MGBundleBuildPreparation.Prepare(new[] { scenePath });
                Check(!world.autoCookOnBundleBuild && !world.surfaceLibrary, "Auto-cook defaults off and leaves the scene unchanged");
                world.autoCookOnBundleBuild = true; world.enabled = false;
                Check(MGSurfaceBundleCook.PrepareScene(scenePath) == 0, "Disabled roots are skipped");
                world.enabled = true; world.renderInPlayMode = false;
                Check(MGSurfaceBundleCook.PrepareScene(scenePath) == 0, "Roots with runtime rendering off are skipped");
                world.renderInPlayMode = true;
                var conflict = cube.AddComponent<RuntimeMeshCombiner>(); conflict.enabled = false;
                EditorSceneManager.SaveScene(scene);
                byte[] before = File.ReadAllBytes(scenePath); bool refused = false;
                try { MGBundleBuildPreparation.Prepare(new[] { scenePath }); } catch (BuildFailedException) { refused = true; }
                Check(refused && !world.surfaceLibrary && before.SequenceEqual(File.ReadAllBytes(scenePath)), "Combiner conflicts abort preparation without saving changes");
                Object.DestroyImmediate(conflict);
                EditorSceneManager.SaveScene(scene);
                MGBundleBuildPreparation.Prepare(new[] { scenePath, scenePath, folder + "/Source.mat" });
                Check(world.surfaceLibrary && !scene.isDirty, "Bundle hook cooks and saves the opted-in scene");
                baked.Add(Path.GetDirectoryName(AssetDatabase.GetAssetPath(world.surfaceLibrary)).Replace('\\','/'));
                Check(world.surfaceLibrary.geometry && world.surfaceLibrary.geometry.shapes.Length == 1, "Bundle auto-cook includes geometry IDs");
                Check(world.surfaceLibrary.resolution == 128 && !world.surfaceLibrary.compressed, "Auto-cook uses serialized texture settings");
                Check(cube.GetComponent<MeshFilter>().sharedMesh.colors.Length > 0 && collider && collider.enabled, "Auto-cook bakes vertex AO and preserves the collider");
                Check(!world.previewInEditor && !cube.GetComponent<MeshRenderer>().forceRenderingOff, "Auto-cook preserves preview-off state");
                var firstLibrary = world.surfaceLibrary;
                EditorSceneManager.CloseScene(scene, true);
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                Check(MGSurfaceBundleCook.PrepareScene(scenePath) == 1, "Closed scene can be cooked during a bundle build");
                Check(!SceneManager.GetSceneByPath(scenePath).isLoaded && SceneManager.GetActiveScene() == active, "Closed scenes and active scene are restored after preparation");
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                world = scene.GetRootGameObjects()[0].GetComponent<MGSurfaceWorld>();
                baked.Add(Path.GetDirectoryName(AssetDatabase.GetAssetPath(world.surfaceLibrary)).Replace('\\','/'));
                Check(world.autoCookOnBundleBuild && world.cookResolution == 128 && world.cookAOSamples == 16 && Mathf.Approximately(world.cookAOReach,.25f), "Toggle and bake settings survive closing and reopening the scene");
                Check(world.surfaceLibrary != firstLibrary, "Later bundle builds produce a fresh bake");
                string libraryPath = AssetDatabase.GetAssetPath(world.surfaceLibrary);
                Check(AssetDatabase.GetDependencies(scenePath, true).Contains(libraryPath), "Cooked library is a scene dependency before BuildPipeline starts");
                var geometry = world.surfaceLibrary.geometry;
                var dependencies = AssetDatabase.GetDependencies(scenePath, true);
                Check(geometry && new UnityEngine.Object[] { geometry, geometry.carrier, geometry.vertices, geometry.indices, geometry.chunks }
                    .All(asset => dependencies.Contains(AssetDatabase.GetAssetPath(asset))), "Reopened scene includes all geometry dependencies before BuildPipeline starts");
                log.AppendLine("SUCCESS"); return log.ToString();
            }
            finally
            {
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                foreach (string path in baked.Distinct()) AssetDatabase.DeleteAsset(path);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
