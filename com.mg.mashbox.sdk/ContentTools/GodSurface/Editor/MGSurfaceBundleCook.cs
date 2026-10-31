using System;
using System.Linq;
using MashBoxSDK.Exporting;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace MG.GodSurface.Editor
{
    [InitializeOnLoad]
    public static class MGSurfaceBundleCook
    {
        static MGSurfaceBundleCook() => MGBundleBuildPreparation.BeforeBuild += Prepare;
        public static bool ShouldCook(MGSurfaceWorld world) => world && world.autoCookOnBundleBuild && world.isActiveAndEnabled && world.renderInPlayMode;
        static void Prepare(string[] paths)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new BuildFailedException("Exit Play Mode before building God Prop scenes.");
            foreach (string path in paths) PrepareScene(path);
        }
        public static int PrepareScene(string path)
        {
            var previousActive = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(path);
            bool existed = scene.IsValid(), opened = !existed || !scene.isLoaded;
            MGSurfaceWorld[] worlds = Array.Empty<MGSurfaceWorld>();
            int undo = -1;
            try
            {
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                MGSurfaceSingleManager.Validate(scene);
                worlds = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MGSurfaceWorld>(true)).Where(ShouldCook).ToArray();
                if (worlds.Length == 0) return 0;
                // Check every opted-in root before applying anything to this scene.
                foreach (var world in worlds)
                {
                    var check = MGSurfaceRootSetup.Inspect(world);
                    if (check.conflicts.Count > 0)
                        throw new InvalidOperationException($"{world.name}: conflicting combiners/instancers: {string.Join(", ", check.conflicts.Select(c => c.name))}. Use Check Props on God Prop Root to resolve them.");
                    if (check.eligible == 0) throw new InvalidOperationException($"{world.name}: no eligible props. Use Check Props on God Prop Root.");
                }
                Undo.IncrementCurrentGroup(); undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Auto cook God Props for bundle");
                foreach (var world in worlds)
                {
                    bool preview = world.previewInEditor;
                    try
                    {
                        string result = MGSurfaceRootSetup.Cook(world, world.cookResolution, world.cookCompress, world.cookVertexAO,
                            MGSurfaceWorldInspector.BakeSettings(world));
                        Debug.Log($"[God Props] Auto-cooked {world.name} for bundle: {result}", world);
                    }
                    finally
                    {
                        world.Release(); world.previewInEditor = preview;
                        EditorUtility.SetDirty(world); PrefabUtility.RecordPrefabInstancePropertyModifications(world);
                        world.RequestRebuild();
                    }
                }
                // Save before BuildPipeline starts: generated meshes/materials must be in its dependency graph.
                AssetDatabase.SaveAssets();
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the cooked scene.");
                return worlds.Length;
            }
            catch (Exception ex)
            {
                foreach (var world in worlds) if (world) world.Release();
                if (undo >= 0)
                {
                    Undo.RevertAllDownToGroup(undo);
                }
                throw new BuildFailedException($"God Prop auto-cook failed for {path}: {ex.Message}");
            }
            finally
            {
                if (undo >= 0) Undo.CollapseUndoOperations(undo);
                foreach (var world in worlds) if (world) world.RequestRebuild();
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
                if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, !existed);
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
