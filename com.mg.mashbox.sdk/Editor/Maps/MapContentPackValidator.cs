#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MashBoxSDK.ContentTools;
using MashBoxSDK.Exporting;
using MashBoxSDK.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    public enum MapValidationSeverity
    {
        Warning,
        Error
    }

    [Serializable]
    public sealed class MapValidationIssue
    {
        public MapValidationSeverity Severity { get; }
        public string Message { get; }

        public MapValidationIssue(MapValidationSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }
    }

    public static class MapContentPackValidator
    {
        private const float MinimumRaceGateTopClearanceMeters = 3f;
        private const float RaceGateGroundProbeDistance = 1000f;
        private static int cachedPerformanceFrame = -1;
        private static MashBoxSDK.ContentTools.Editor.MapContentPackDefinition cachedPerformancePack;
        private static string cachedPerformanceGameName;
        private static MapPerformanceScanResult cachedPerformanceResult;

        public static List<MapValidationIssue> ValidateGameplayScene(Scene scene)
        {
            var issues = new List<MapValidationIssue>();
            EnsureSceneCamerasAreEditorOnly(scene);
            ValidateSpawnPoint(scene, issues);
            ValidateCollectibles(scene, issues);
            ValidateCollectLetterPlacement(scene, issues);
            ValidateCollectLetters(scene, issues);
            ValidateChallengeGroupScripts(scene, issues);
            ValidateRaceGateHeight(scene, issues);
            ValidateRealtimeReflectionProbes(scene, issues);
            ValidateRendererMaterials(scene, issues);
            ValidatePrefabReferences(scene, issues);
            return issues;
        }

        public static List<MapValidationIssue> Validate(
            MashBoxSDK.ContentTools.Editor.MapContentPackDefinition pack,
            bool forceOpenScene = false,
            bool includePerformanceScan = true)
        {
            var issues = new List<MapValidationIssue>();
            if (pack == null)
                return issues;

            if (pack.Scene == null)
                issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "A scene is required before this map can be built or published."));

            if (string.IsNullOrWhiteSpace(pack.MapName))
                issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "Map Name is required."));

            if (pack.Screenshot == null)
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "A screenshot is required. Use a 1920x1080 image."));
                if (includePerformanceScan)
                    ValidatePerformance(pack, issues, forceOpenScene);
                return issues;
            }

            var screenshotPath = AssetDatabase.GetAssetPath(pack.Screenshot);
            if (string.IsNullOrWhiteSpace(screenshotPath))
            {
                issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "Screenshot asset path could not be resolved."));
                if (includePerformanceScan)
                    ValidatePerformance(pack, issues, forceOpenScene);
                return issues;
            }

            var screenshotExtension = Path.GetExtension(screenshotPath);
            if (!string.Equals(screenshotExtension, ".png", StringComparison.OrdinalIgnoreCase))
                issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "Screenshot must be a PNG file."));

            var expectedScreenshotName = string.IsNullOrWhiteSpace(pack.MapName) ? pack.PackName : pack.MapName;
            var actualScreenshotName = Path.GetFileNameWithoutExtension(screenshotPath);
            if (!string.Equals(actualScreenshotName, expectedScreenshotName, StringComparison.Ordinal))
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Error,
                    $"Screenshot file name must match the map name. Expected '{expectedScreenshotName}.png' but found '{actualScreenshotName}{screenshotExtension}'."));
            }

            if (!TryGetSourceImageDimensions(pack.Screenshot, out var screenshotWidth, out var screenshotHeight))
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Error,
                    "Could not read the source screenshot size. Use a 1920x1080 PNG image."));
            }
            else if (screenshotWidth != 1920 || screenshotHeight != 1080)
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Error,
                    $"Screenshot must be 1920x1080. Current source size is {screenshotWidth}x{screenshotHeight}."));
            }

            ValidateSpawnPoint(pack, issues, forceOpenScene);
            ValidateSceneCameras(pack, issues, forceOpenScene);
            ValidateCollectibles(pack, issues, forceOpenScene);
            ValidateCollectLetterPlacement(pack, issues, forceOpenScene);
            ValidateCollectLetters(pack, issues, forceOpenScene);
            ValidateChallengeGroupScripts(pack, issues, forceOpenScene);
            ValidateRaceGateHeight(pack, issues, forceOpenScene);
            ValidateRealtimeReflectionProbes(pack, issues, forceOpenScene);
            ValidateRendererMaterials(pack, issues, forceOpenScene);
            ValidatePrefabReferences(pack, issues, forceOpenScene);
            ValidateDuplicateExportedLightmaps(pack, issues);
            if (includePerformanceScan)
                ValidatePerformance(pack, issues, forceOpenScene);

            return issues;
        }

        private static void ValidatePerformance(
            MashBoxSDK.ContentTools.Editor.MapContentPackDefinition pack,
            List<MapValidationIssue> issues,
            bool forceOpenScene)
        {
            if (pack?.Scene == null)
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Error,
                    "The performance scan could not run because the map has no scene."));
                return;
            }

            var gameName = pack.GameModMappings?
                .FirstOrDefault(mapping => mapping != null && mapping.IsPublishTarget)?
                .GameName;

            if (string.IsNullOrWhiteSpace(gameName))
                gameName = EditorPrefs.GetString("ModIo.CurrentGame", string.Empty);

            var game = GameRegistry.Find(gameName);
            var cacheGameName = game?.DisplayName ?? gameName ?? string.Empty;

            var scenePath = AssetDatabase.GetAssetPath(pack.Scene);
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Error,
                    "The performance scan could not resolve the map scene path."));
                return;
            }

            var scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.IsValid() || !scene.isLoaded ||
                !string.Equals(SceneManager.GetActiveScene().path, scenePath, StringComparison.OrdinalIgnoreCase))
            {
                if (!forceOpenScene)
                {
                    issues.Add(new MapValidationIssue(
                        MapValidationSeverity.Warning,
                        "Open the map scene or run full validation to calculate its performance score and shared-memory usage."));
                    return;
                }

                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    issues.Add(new MapValidationIssue(
                        MapValidationSeverity.Error,
                        "Performance validation was cancelled because the target scene could not be opened."));
                    return;
                }

                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Error,
                    "The performance scan could not load the map scene."));
                return;
            }

            var useCachedResult = cachedPerformanceFrame == Time.frameCount &&
                                  cachedPerformancePack == pack &&
                                  string.Equals(cachedPerformanceGameName, cacheGameName, StringComparison.OrdinalIgnoreCase);

            MapPerformanceScanResult result = useCachedResult ? cachedPerformanceResult : null;
            if (!useCachedResult)
            {
                try
                {
                    result = new MapPerformanceScannerPanel().ScanScene();
                    cachedPerformanceFrame = Time.frameCount;
                    cachedPerformancePack = pack;
                    cachedPerformanceGameName = cacheGameName;
                    cachedPerformanceResult = result;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    issues.Add(new MapValidationIssue(
                        MapValidationSeverity.Error,
                        $"The performance scan failed: {exception.Message}"));
                    return;
                }
            }

            if (result == null)
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Error,
                    "The performance scan was cancelled. A completed scan is required before publishing."));
                return;
            }

            if (result.OversizedTextures != null && result.OversizedTextures.Count > 0)
            {
                var shownTextures = string.Join("\n", result.OversizedTextures.Take(12).Select(texture => $"- {texture}"));
                var hiddenCount = result.OversizedTextures.Count - 12;
                var hiddenMessage = hiddenCount > 0 ? $"\n...and {hiddenCount} more." : string.Empty;

                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Error,
                    $"Textures above {MapPerformanceScannerPanel.MaximumTextureDimension:N0} pixels are not allowed in published maps. " +
                    $"Reduce the imported Max Size to {MapPerformanceScannerPanel.MaximumTextureDimension:N0} or lower, then scan again.\n" +
                    $"{shownTextures}{hiddenMessage}"));
            }

            if (result.UnsupportedShaders != null && result.UnsupportedShaders.Count > 0)
            {
                var shownShaders = string.Join("\n", result.UnsupportedShaders.Take(12).Select(shader => $"- {shader}"));
                var hiddenCount = result.UnsupportedShaders.Count - 12;
                var hiddenMessage = hiddenCount > 0 ? $"\n...and {hiddenCount} more." : string.Empty;

                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Error,
                    "Unsupported shaders are not allowed in published maps. Use shaders supplied by the MashBox SDK, " +
                    "or HDRP/TerrainLit for Unity terrain materials. Replace the unsupported material shaders, then scan again.\n" +
                    $"{shownShaders}{hiddenMessage}"));
            }

            if (result.PerformanceScore <= MapPerformanceScannerPanel.MinimumPublishPerformanceScore)
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Error,
                    $"Performance score is {result.PerformanceScore:F0}. Publishing requires a score above {MapPerformanceScannerPanel.MinimumPublishPerformanceScore:F0}."));
            }

            if (game == null)
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Warning,
                    "Select a detected game target to validate this map against its shared-memory budget."));
                return;
            }

            if (game.MapSharedMemoryBudgetBytes <= 0)
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Error,
                    $"No map shared-memory budget is configured for {game.DisplayName}."));
            }
            else if (result.SharedMemoryBytes > game.MapSharedMemoryBudgetBytes)
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Error,
                    $"Shared-memory usage is {FormatBytes(result.SharedMemoryBytes)}, which exceeds the {FormatBytes(game.MapSharedMemoryBudgetBytes)} map budget for {game.DisplayName}."));
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L)
                return $"{bytes / (1024f * 1024f * 1024f):F2} GB";
            if (bytes >= 1024L * 1024L)
                return $"{bytes / (1024f * 1024f):F2} MB";
            if (bytes >= 1024L)
                return $"{bytes / 1024f:F2} KB";
            return $"{bytes} B";
        }

        public static bool HasBlockingIssues(MashBoxSDK.ContentTools.Editor.MapContentPackDefinition pack)
        {
            return Validate(pack).Exists(issue => issue.Severity == MapValidationSeverity.Error);
        }

        public static bool TryGetSourceImageDimensions(Texture2D texture, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (texture == null)
                return false;

            var assetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrWhiteSpace(assetPath))
                return false;

            var fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
                return false;

            try
            {
                var bytes = File.ReadAllBytes(fullPath);
                var probe = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!ImageConversion.LoadImage(probe, bytes, markNonReadable: true))
                        return false;

                    width = probe.width;
                    height = probe.height;
                    return width > 0 && height > 0;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(probe);
                }
            }
            catch
            {
                return false;
            }
        }

        private static void ValidateSpawnPoint(MashBoxSDK.ContentTools.Editor.MapContentPackDefinition pack, List<MapValidationIssue> issues, bool forceOpenScene)
        {
            if (pack?.Scene == null)
                return;

            var scenePath = AssetDatabase.GetAssetPath(pack.Scene);
            if (string.IsNullOrWhiteSpace(scenePath))
                return;

            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedTemporarily = false;
            var previousActiveScene = SceneManager.GetActiveScene();

            if (forceOpenScene)
            {
                if ((!scene.IsValid() || !scene.isLoaded) &&
                    !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    issues.Add(new MapValidationIssue(
                        MapValidationSeverity.Error,
                        "Validation cancelled because the target scene could not be opened."));
                    return;
                }

                if (!scene.IsValid() || !scene.isLoaded || !string.Equals(previousActiveScene.path, scenePath, StringComparison.OrdinalIgnoreCase))
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }
            else if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                openedTemporarily = true;
            }

            try
            {
                var spawnPoints = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<MBSpawnLocation>(true))
                    .ToList();

                if (spawnPoints.Count == 0)
                {
                    issues.Add(new MapValidationIssue(
                        MapValidationSeverity.Error,
                        "No spawn point found. Add a MashBox Spawn Point to the scene."));
                    return;
                }

                if (spawnPoints.Any(spawn => !spawn.IsGrounded()))
                {
                    issues.Add(new MapValidationIssue(
                        MapValidationSeverity.Error,
                        "A spawn point is floating. Move it onto the ground so it can snap to a valid surface."));
                }
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                    EditorSceneManager.SaveScene(scene);

                if (openedTemporarily && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);

                if (!forceOpenScene && previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private static void ValidateSceneCameras(MashBoxSDK.ContentTools.Editor.MapContentPackDefinition pack, List<MapValidationIssue> issues, bool forceOpenScene)
        {
            if (pack?.Scene == null)
                return;

            var scenePath = AssetDatabase.GetAssetPath(pack.Scene);
            if (string.IsNullOrWhiteSpace(scenePath))
                return;

            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedTemporarily = false;
            var previousActiveScene = SceneManager.GetActiveScene();

            if (forceOpenScene)
            {
                if ((!scene.IsValid() || !scene.isLoaded) &&
                    !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    issues.Add(new MapValidationIssue(
                        MapValidationSeverity.Error,
                        "Validation cancelled because the target scene could not be opened."));
                    return;
                }

                if (!scene.IsValid() || !scene.isLoaded || !string.Equals(previousActiveScene.path, scenePath, StringComparison.OrdinalIgnoreCase))
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }
            else if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                openedTemporarily = true;
            }

            try
            {
                EnsureSceneCamerasAreEditorOnly(scene);
                ValidateSceneCameras(scene, issues);
            }
            finally
            {
                if (openedTemporarily && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);

                if (!forceOpenScene && previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private static void ValidateCollectLetters(MashBoxSDK.ContentTools.Editor.MapContentPackDefinition pack, List<MapValidationIssue> issues, bool forceOpenScene)
        {
            if (pack?.Scene == null)
                return;

            var scenePath = AssetDatabase.GetAssetPath(pack.Scene);
            if (string.IsNullOrWhiteSpace(scenePath))
                return;

            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedTemporarily = false;
            var previousActiveScene = SceneManager.GetActiveScene();

            if (forceOpenScene)
            {
                if ((!scene.IsValid() || !scene.isLoaded) &&
                    !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    issues.Add(new MapValidationIssue(
                        MapValidationSeverity.Error,
                        "Validation cancelled because the target scene could not be opened."));
                    return;
                }

                if (!scene.IsValid() || !scene.isLoaded || !string.Equals(previousActiveScene.path, scenePath, StringComparison.OrdinalIgnoreCase))
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }
            else if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                openedTemporarily = true;
            }

            try
            {
                ValidateCollectLetters(scene, issues);
            }
            finally
            {
                if (openedTemporarily && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);

                if (!forceOpenScene && previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private static void ValidateCollectibles(MashBoxSDK.ContentTools.Editor.MapContentPackDefinition pack, List<MapValidationIssue> issues, bool forceOpenScene)
        {
            if (pack?.Scene == null)
                return;

            var scenePath = AssetDatabase.GetAssetPath(pack.Scene);
            if (string.IsNullOrWhiteSpace(scenePath))
                return;

            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedTemporarily = false;
            var previousActiveScene = SceneManager.GetActiveScene();

            if (forceOpenScene)
            {
                if ((!scene.IsValid() || !scene.isLoaded) &&
                    !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    issues.Add(new MapValidationIssue(
                        MapValidationSeverity.Error,
                        "Validation cancelled because the target scene could not be opened."));
                    return;
                }

                if (!scene.IsValid() || !scene.isLoaded || !string.Equals(previousActiveScene.path, scenePath, StringComparison.OrdinalIgnoreCase))
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }
            else if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                openedTemporarily = true;
            }

            try
            {
                ValidateCollectibles(scene, issues);
            }
            finally
            {
                if (openedTemporarily && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);

                if (!forceOpenScene && previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private static void ValidateChallengeGroupScripts(MashBoxSDK.ContentTools.Editor.MapContentPackDefinition pack, List<MapValidationIssue> issues, bool forceOpenScene)
        {
            if (pack?.Scene == null)
                return;

            var scenePath = AssetDatabase.GetAssetPath(pack.Scene);
            if (string.IsNullOrWhiteSpace(scenePath))
                return;

            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedTemporarily = false;
            var previousActiveScene = SceneManager.GetActiveScene();

            if (forceOpenScene)
            {
                if ((!scene.IsValid() || !scene.isLoaded) &&
                    !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    issues.Add(new MapValidationIssue(
                        MapValidationSeverity.Error,
                        "Validation cancelled because the target scene could not be opened."));
                    return;
                }

                if (!scene.IsValid() || !scene.isLoaded || !string.Equals(previousActiveScene.path, scenePath, StringComparison.OrdinalIgnoreCase))
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }
            else if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                openedTemporarily = true;
            }

            try
            {
                ValidateChallengeGroupScripts(scene, issues);
            }
            finally
            {
                if (openedTemporarily && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);

                if (!forceOpenScene && previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private static void ValidateRaceGateHeight(MashBoxSDK.ContentTools.Editor.MapContentPackDefinition pack, List<MapValidationIssue> issues, bool forceOpenScene)
        {
            if (pack?.Scene == null)
                return;

            var scenePath = AssetDatabase.GetAssetPath(pack.Scene);
            if (string.IsNullOrWhiteSpace(scenePath))
                return;

            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedTemporarily = false;
            var previousActiveScene = SceneManager.GetActiveScene();

            if (forceOpenScene)
            {
                if ((!scene.IsValid() || !scene.isLoaded) &&
                    !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    issues.Add(new MapValidationIssue(
                        MapValidationSeverity.Error,
                        "Validation cancelled because the target scene could not be opened."));
                    return;
                }

                if (!scene.IsValid() || !scene.isLoaded || !string.Equals(previousActiveScene.path, scenePath, StringComparison.OrdinalIgnoreCase))
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }
            else if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                openedTemporarily = true;
            }

            try
            {
                ValidateRaceGateHeight(scene, issues);
            }
            finally
            {
                if (openedTemporarily && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);

                if (!forceOpenScene && previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private static void ValidateCollectLetterPlacement(MashBoxSDK.ContentTools.Editor.MapContentPackDefinition pack, List<MapValidationIssue> issues, bool forceOpenScene)
        {
            if (pack?.Scene == null)
                return;

            var scenePath = AssetDatabase.GetAssetPath(pack.Scene);
            if (string.IsNullOrWhiteSpace(scenePath))
                return;

            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedTemporarily = false;
            var previousActiveScene = SceneManager.GetActiveScene();

            if (forceOpenScene)
            {
                if ((!scene.IsValid() || !scene.isLoaded) &&
                    !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    issues.Add(new MapValidationIssue(
                        MapValidationSeverity.Error,
                        "Validation cancelled because the target scene could not be opened."));
                    return;
                }

                if (!scene.IsValid() || !scene.isLoaded || !string.Equals(previousActiveScene.path, scenePath, StringComparison.OrdinalIgnoreCase))
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }
            else if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                openedTemporarily = true;
            }

            try
            {
                ValidateCollectLetterPlacement(scene, issues);
            }
            finally
            {
                if (openedTemporarily && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);

                if (!forceOpenScene && previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private static void ValidateRealtimeReflectionProbes(MashBoxSDK.ContentTools.Editor.MapContentPackDefinition pack, List<MapValidationIssue> issues, bool forceOpenScene)
        {
            if (pack?.Scene == null)
                return;

            var scenePath = AssetDatabase.GetAssetPath(pack.Scene);
            if (string.IsNullOrWhiteSpace(scenePath))
                return;

            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedTemporarily = false;
            var previousActiveScene = SceneManager.GetActiveScene();

            if (forceOpenScene)
            {
                if ((!scene.IsValid() || !scene.isLoaded) &&
                    !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    issues.Add(new MapValidationIssue(
                        MapValidationSeverity.Error,
                        "Validation cancelled because the target scene could not be opened."));
                    return;
                }

                if (!scene.IsValid() || !scene.isLoaded || !string.Equals(previousActiveScene.path, scenePath, StringComparison.OrdinalIgnoreCase))
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }
            else if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                openedTemporarily = true;
            }

            try
            {
                ValidateRealtimeReflectionProbes(scene, issues);
            }
            finally
            {
                if (openedTemporarily && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);

                if (!forceOpenScene && previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private static void ValidateRendererMaterials(MashBoxSDK.ContentTools.Editor.MapContentPackDefinition pack, List<MapValidationIssue> issues, bool forceOpenScene)
        {
            if (pack?.Scene == null)
                return;

            var scenePath = AssetDatabase.GetAssetPath(pack.Scene);
            if (string.IsNullOrWhiteSpace(scenePath))
                return;

            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedTemporarily = false;
            var previousActiveScene = SceneManager.GetActiveScene();

            if (forceOpenScene)
            {
                if ((!scene.IsValid() || !scene.isLoaded) &&
                    !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    issues.Add(new MapValidationIssue(
                        MapValidationSeverity.Error,
                        "Validation cancelled because the target scene could not be opened."));
                    return;
                }

                if (!scene.IsValid() || !scene.isLoaded || !string.Equals(previousActiveScene.path, scenePath, StringComparison.OrdinalIgnoreCase))
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }
            else if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                openedTemporarily = true;
            }

            try
            {
                ValidateRendererMaterials(scene, issues);
            }
            finally
            {
                if (openedTemporarily && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);

                if (!forceOpenScene && previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private static void ValidatePrefabReferences(MashBoxSDK.ContentTools.Editor.MapContentPackDefinition pack, List<MapValidationIssue> issues, bool forceOpenScene)
        {
            if (pack?.Scene == null)
                return;

            var scenePath = AssetDatabase.GetAssetPath(pack.Scene);
            if (string.IsNullOrWhiteSpace(scenePath))
                return;

            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedTemporarily = false;
            var previousActiveScene = SceneManager.GetActiveScene();

            if (forceOpenScene)
            {
                if ((!scene.IsValid() || !scene.isLoaded) &&
                    !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    issues.Add(new MapValidationIssue(
                        MapValidationSeverity.Error,
                        "Validation cancelled because the target scene could not be opened."));
                    return;
                }

                if (!scene.IsValid() || !scene.isLoaded || !string.Equals(previousActiveScene.path, scenePath, StringComparison.OrdinalIgnoreCase))
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }
            else if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                openedTemporarily = true;
            }

            try
            {
                ValidatePrefabReferences(scene, issues);
            }
            finally
            {
                if (openedTemporarily && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);

                if (!forceOpenScene && previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private static void ValidateDuplicateExportedLightmaps(MashBoxSDK.ContentTools.Editor.MapContentPackDefinition pack, List<MapValidationIssue> issues)
        {
            if (pack == null || pack.Scene == null)
                return;

            var candidatePaths = CollectMapLightmapCandidatePaths(pack);
            if (candidatePaths.Count == 0)
                return;

            var duplicateGroups = candidatePaths
                .Where(IsLikelyLightmapAsset)
                .GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .Where(group =>
                {
                    var distinctPaths = group
                        .Select(path => path.Replace('\\', '/'))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    return distinctPaths.Count > 1 && distinctPaths.Any(IsBakeryLightmapPath);
                })
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (duplicateGroups.Count == 0)
                return;

            var shownGroups = duplicateGroups
                .Take(6)
                .Select(group =>
                {
                    var paths = group
                        .Select(path => path.Replace('\\', '/'))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

                    return $"- {group.Key}\n  {string.Join("\n  ", paths)}";
                });

            var hiddenCount = duplicateGroups.Count - 6;
            var hiddenMessage = hiddenCount > 0 ? $"\n...and {hiddenCount} more duplicate lightmap name(s)." : string.Empty;

            issues.Add(new MapValidationIssue(
                MapValidationSeverity.Error,
                "Duplicate Bakery lightmap asset filenames were found for this map. This can cause the remote cook/package step to use the wrong baked lighting.\n\n" +
                "Keep only one copy of each generated Bakery lightmap, preferably the active output under Assets/BakeryLightmaps, then validate again.\n\n" +
                $"Duplicates:\n{string.Join("\n", shownGroups)}{hiddenMessage}"));
        }

        private static List<string> CollectMapLightmapCandidatePaths(MashBoxSDK.ContentTools.Editor.MapContentPackDefinition pack)
        {
            var paths = new HashSet<string>(CollectMapExportDependencyPaths(pack), StringComparer.OrdinalIgnoreCase);

            var scenePath = AssetDatabase.GetAssetPath(pack.Scene)?.Replace('\\', '/');
            var sceneFolder = string.IsNullOrWhiteSpace(scenePath) ? null : Path.GetDirectoryName(scenePath)?.Replace('\\', '/');
            AddLikelyLightmapAssetsInFolder(sceneFolder, paths);

            foreach (var bakeryFolder in GetMatchingBakeryLightmapFolders(pack, scenePath, sceneFolder))
                AddLikelyLightmapAssetsInFolder(bakeryFolder, paths);

            return paths.ToList();
        }

        private static List<string> CollectMapExportDependencyPaths(MashBoxSDK.ContentTools.Editor.MapContentPackDefinition pack)
        {
            var roots = new List<string>();
            AddDependencyRoot(roots, AssetDatabase.GetAssetPath(pack));
            AddDependencyRoot(roots, AssetDatabase.GetAssetPath(pack.Scene));
            AddDependencyRoot(roots, AssetDatabase.GetAssetPath(pack.Screenshot));

            if (roots.Count == 0)
                return new List<string>();

            return roots
                .Concat(AssetDatabase.GetDependencies(roots.ToArray(), true))
                .Where(IsExportableUnityPackagePath)
                .Select(path => path.Replace('\\', '/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void AddDependencyRoot(List<string> roots, string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                roots.Add(path);
        }

        private static bool IsExportableUnityPackagePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            path = path.Replace('\\', '/');
            return path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                   && !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                   && path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool IsLikelyLightmapAsset(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            path = path.Replace('\\', '/');
            var extension = Path.GetExtension(path);
            if (!IsLightmapFileExtension(extension))
                return false;

            var fileName = Path.GetFileNameWithoutExtension(path);
            var directory = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty;

            return path.IndexOf("/BakeryLightmaps/", StringComparison.OrdinalIgnoreCase) >= 0
                   || fileName.IndexOf("lightmap", StringComparison.OrdinalIgnoreCase) >= 0
                   || fileName.IndexOf("lmgroup", StringComparison.OrdinalIgnoreCase) >= 0
                   || directory.IndexOf("lightmap", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsBakeryLightmapPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            return path.Replace('\\', '/')
                .IndexOf("/BakeryLightmaps/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddLikelyLightmapAssetsInFolder(string folder, HashSet<string> paths)
        {
            if (string.IsNullOrWhiteSpace(folder) || paths == null || !AssetDatabase.IsValidFolder(folder))
                return;

            foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid)?.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(path) || AssetDatabase.IsValidFolder(path) || !IsLikelyLightmapAsset(path))
                    continue;

                paths.Add(path);
            }
        }

        private const string BakeryLightmapsRoot = "Assets/BakeryLightmaps";

        private static IEnumerable<string> GetMatchingBakeryLightmapFolders(
            MashBoxSDK.ContentTools.Editor.MapContentPackDefinition pack,
            string scenePath,
            string sceneFolder)
        {
            if (!AssetDatabase.IsValidFolder(BakeryLightmapsRoot))
                return Enumerable.Empty<string>();

            var candidateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddCandidateName(candidateNames, pack.name);
            AddCandidateName(candidateNames, pack.PackName);
            AddCandidateName(candidateNames, pack.MapName);
            AddCandidateName(candidateNames, Path.GetFileNameWithoutExtension(scenePath));
            AddCandidateName(candidateNames, Path.GetFileName(sceneFolder ?? string.Empty));

            return AssetDatabase.GetSubFolders(BakeryLightmapsRoot)
                .Where(folder => candidateNames.Any(name => NamesLikelyReferToSameLightmapFolder(Path.GetFileName(folder), name)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void AddCandidateName(HashSet<string> names, string value)
        {
            if (names == null || string.IsNullOrWhiteSpace(value))
                return;

            var trimmed = value.Trim();
            if (trimmed.Length > 0)
                names.Add(trimmed);
        }

        private static bool NamesLikelyReferToSameLightmapFolder(string folderName, string candidateName)
        {
            if (string.IsNullOrWhiteSpace(folderName) || string.IsNullOrWhiteSpace(candidateName))
                return false;

            var normalizedFolder = NormalizeLightmapName(folderName);
            var normalizedCandidate = NormalizeLightmapName(candidateName);

            if (string.IsNullOrEmpty(normalizedFolder) || string.IsNullOrEmpty(normalizedCandidate))
                return false;

            if (normalizedFolder == normalizedCandidate ||
                normalizedFolder.Contains(normalizedCandidate) ||
                normalizedCandidate.Contains(normalizedFolder))
            {
                return true;
            }

            var folderTokens = TokenizeLightmapName(folderName);
            var candidateTokens = TokenizeLightmapName(candidateName);
            return folderTokens.Overlaps(candidateTokens);
        }

        private static string NormalizeLightmapName(string value)
        {
            var chars = value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray();

            return new string(chars);
        }

        private static HashSet<string> TokenizeLightmapName(string value)
        {
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(value))
                return tokens;

            var split = value.Split(new[] { ' ', '_', '-', '.', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawToken in split)
            {
                var token = new string(rawToken.Where(char.IsLetterOrDigit).ToArray());
                if (token.Length >= 4)
                    tokens.Add(token);
            }

            return tokens;
        }

        private static bool IsLightmapFileExtension(string extension)
        {
            return string.Equals(extension, ".exr", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".hdr", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".tga", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".asset", StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateSpawnPoint(Scene scene, List<MapValidationIssue> issues)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            var spawnPoints = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MBSpawnLocation>(true))
                .ToList();

            if (spawnPoints.Count == 0)
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Error,
                    "No spawn point found. Add a MashBox Spawn Point to the active scene."));
                return;
            }

            if (spawnPoints.Any(spawn => !spawn.IsGrounded()))
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Error,
                    "A spawn point is floating. Move it onto the ground so it can snap to a valid surface."));
            }
        }

        private static void ValidateSceneCameras(Scene scene, List<MapValidationIssue> issues)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            EnsureSceneCamerasAreEditorOnly(scene);
        }

        private static void ValidateRealtimeReflectionProbes(Scene scene, List<MapValidationIssue> issues)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            var realtimeProbePaths = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<ReflectionProbe>(true))
                .Where(probe => probe != null &&
                                probe.mode == ReflectionProbeMode.Realtime &&
                                !IsEditorOnly(probe.transform))
                .Select(probe => GetTransformPath(probe.transform))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            if (realtimeProbePaths.Count == 0)
                return;

            var shownPaths = string.Join("\n", realtimeProbePaths.Take(12).Select(path => $"- {path}"));
            var hiddenCount = realtimeProbePaths.Count - 12;
            var hiddenMessage = hiddenCount > 0 ? $"\n...and {hiddenCount} more." : string.Empty;

            issues.Add(new MapValidationIssue(
                MapValidationSeverity.Error,
                "Realtime reflection probes are not allowed in published maps. Change each probe's Type to Baked or Custom, or remove it:\n" +
                $"{shownPaths}{hiddenMessage}"));
        }

        private static bool IsEditorOnly(Transform target)
        {
            var current = target;
            while (current != null)
            {
                if (string.Equals(current.tag, "EditorOnly", StringComparison.Ordinal))
                    return true;

                current = current.parent;
            }

            return false;
        }

        private static void EnsureSceneCamerasAreEditorOnly(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            var camerasToRetag = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                .Where(camera => !string.Equals(camera.gameObject.tag, "EditorOnly", StringComparison.Ordinal))
                .ToList();

            if (camerasToRetag.Count == 0)
                return;

            foreach (var camera in camerasToRetag)
            {
                camera.gameObject.tag = "EditorOnly";
                EditorUtility.SetDirty(camera.gameObject);
            }

            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void ValidateCollectibles(Scene scene, List<MapValidationIssue> issues)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            var blockedCollectibles = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MBCollectible>(true))
                .Where(collectible => collectible.IsPlacementBlocked)
                .Select(collectible => collectible.gameObject.name)
                .ToList();

            if (blockedCollectibles.Count == 0)
                return;

            issues.Add(new MapValidationIssue(
                MapValidationSeverity.Error,
                $"Collectibles cannot be placed inside other geometry. Reposition: {string.Join(", ", blockedCollectibles)}."));
        }

        private static void ValidateCollectLetterPlacement(Scene scene, List<MapValidationIssue> issues)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            var blockedLetters = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MBCollectLetter>(true))
                .Where(letter => letter.IsPlacementBlocked)
                .Select(letter => letter.gameObject.name)
                .ToList();

            if (blockedLetters.Count == 0)
                return;

            issues.Add(new MapValidationIssue(
                MapValidationSeverity.Error,
                $"Collect letters cannot be placed inside other geometry. Reposition: {string.Join(", ", blockedLetters)}."));
        }

        private static void ValidateCollectLetters(Scene scene, List<MapValidationIssue> issues)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            var letterComponents = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MBCollectLetter>(true))
                .ToList();

            if (letterComponents.Count == 0)
                return;

            var letters = letterComponents
                .Select(letter => letter.Letter)
                .Distinct()
                .ToHashSet();

            var missingLetters = Enum.GetValues(typeof(MBCollectLetter.LetterType))
                .Cast<MBCollectLetter.LetterType>()
                .Where(letter => !letters.Contains(letter))
                .Select(letter => letter.ToString())
                .ToList();

            if (missingLetters.Count > 0)
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Error,
                    $"Collect Letters is incomplete. Missing letter(s): {string.Join(", ", missingLetters)}."));
            }
        }

        private static void ValidateChallengeGroupScripts(Scene scene, List<MapValidationIssue> issues)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            ValidateChallengeGroupScript<MBCollectible>("Collectible", scene, issues);
            ValidateChallengeGroupScript<MBPhotoSpot>("Photo Spots", scene, issues);
            ValidateChallengeGroupScript<MBRace>("Races", scene, issues);
            ValidateChallengeGroupScript<MBSecretGap>("Secret Gap", scene, issues);
            ValidateChallengeGroupScript<MBSideHit>("Side Hit", scene, issues);
            ValidateChallengeGroupScript<MBSideHit>("Side Hits", scene, issues);
            ValidateChallengeGroupScript<MBExpertLine>("Expert Line", scene, issues);
            ValidateChallengeGroupScript<MBExpertLine>("Expert Lines", scene, issues);
            ValidateChallengeGroupScript<MBCollectLetter>("Collect Letters", scene, issues);
        }

        private static void ValidateRaceGateHeight(Scene scene, List<MapValidationIssue> issues)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            var invalidRaceGates = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MBRace>(true))
                .SelectMany(race => race.transform.Cast<Transform>()
                    .Where(child => child.name.StartsWith("Gate", StringComparison.Ordinal) || child.GetComponent<MBRaceGate>() != null)
                    .Select(child => new
                    {
                        RaceName = race.RaceName,
                        GateName = child.name,
                        Clearance = GetRaceGateTopClearance(child)
                    }))
                .Where(entry => entry.Clearance.HasValue && entry.Clearance.Value < MinimumRaceGateTopClearanceMeters)
                .Select(entry => $"{entry.RaceName}/{entry.GateName} ({entry.Clearance.Value:0.0}m)")
                .ToList();

            if (invalidRaceGates.Count == 0)
                return;

            issues.Add(new MapValidationIssue(
                MapValidationSeverity.Error,
                $"Race gates must have at least {MinimumRaceGateTopClearanceMeters:0.0}m from the gate top to the ground. Fix: {string.Join(", ", invalidRaceGates)}."));
        }

        private static void ValidateRendererMaterials(Scene scene, List<MapValidationIssue> issues)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            var invalidMaterialUsages = new List<string>();
            var seenUsages = new HashSet<string>(StringComparer.Ordinal);

            var renderers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => renderer is MeshRenderer || renderer is SkinnedMeshRenderer);

            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null)
                        continue;

                    var materialPath = AssetDatabase.GetAssetPath(material).Replace('\\', '/');
                    if (!TryGetInvalidRendererMaterialReason(materialPath, out var reason))
                        continue;

                    var usageKey = $"{GetTransformPath(renderer.transform)}|{i}|{materialPath}|{material.name}";
                    if (!seenUsages.Add(usageKey))
                        continue;

                    var displayPath = string.IsNullOrWhiteSpace(materialPath)
                        ? "<scene-only or unsaved material>"
                        : materialPath;
                    invalidMaterialUsages.Add(
                        $"{GetTransformPath(renderer.transform)} slot {i}: '{material.name}' at {displayPath} - {reason}");
                }
            }

            if (invalidMaterialUsages.Count == 0)
                return;

            var shownUsages = string.Join("\n", invalidMaterialUsages.Take(12).Select(usage => $"- {usage}"));
            var hiddenCount = invalidMaterialUsages.Count - 12;
            var hiddenMessage = hiddenCount > 0 ? $"\n...and {hiddenCount} more." : string.Empty;

            issues.Add(new MapValidationIssue(
                MapValidationSeverity.Error,
                "One or more mesh renderers in this map use materials that are not standalone .mat assets under Assets.\n\n" +
                "FBX embedded materials, package materials, default render pipeline materials, and scene-only materials can fail to cook correctly for published maps.\n\n" +
                "Create or extract .mat assets under Assets, assign them to the MeshRenderer or SkinnedMeshRenderer slots, then publish again.\n\n" +
                $"Invalid material usages:\n{shownUsages}{hiddenMessage}"));
        }

        private static void ValidatePrefabReferences(Scene scene, List<MapValidationIssue> issues)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            var integrityProblems = ContentPackValidator.FindPrefabIntegrityProblems(
                scene.GetRootGameObjects(),
                includePrefabDependencies: true);

            if (integrityProblems.Count == 0)
                return;

            var shownProblems = string.Join("\n", integrityProblems.Take(12).Select(problem => $"- {problem.message}"));
            var hiddenCount = integrityProblems.Count - 12;
            var hiddenMessage = hiddenCount > 0 ? $"\n...and {hiddenCount} more." : string.Empty;

            issues.Add(new MapValidationIssue(
                MapValidationSeverity.Error,
                "This map contains missing scripts or broken prefab references. These assets cannot be recovered by the remote cooker.\n\n" +
                "Fix the following problems, then validate again:\n" +
                $"{shownProblems}{hiddenMessage}"));
        }

        private static bool TryGetInvalidRendererMaterialReason(string assetPath, out string reason)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                reason = "The material is not saved as a standalone project asset.";
                return true;
            }

            if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                reason = "The material is not stored under the project's Assets folder.";
                return true;
            }

            if (!string.Equals(Path.GetExtension(assetPath), ".mat", StringComparison.OrdinalIgnoreCase))
            {
                reason = "The material is not a standalone .mat asset.";
                return true;
            }

            reason = null;
            return false;
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
                return "<missing renderer>";

            var names = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }

        private static float? GetRaceGateTopClearance(Transform gate)
        {
            if (gate == null)
                return null;

            var raceGate = gate.GetComponent<MBRaceGate>();
            if (raceGate == null)
                return null;

            var topPoint = raceGate.GetTopPointWorld();
            if (!Physics.Raycast(topPoint, Vector3.down, out var hit, RaceGateGroundProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return null;

            return topPoint.y - hit.point.y;
        }

        private static void ValidateChallengeGroupScript<TChild>(string rootName, Scene scene, List<MapValidationIssue> issues)
            where TChild : Component
        {
            var childCount = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<TChild>(true))
                .Count();

            if (childCount == 0)
                return;

            var groupRoot = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(transform => string.Equals(transform.name, rootName, StringComparison.Ordinal));

            if (groupRoot == null)
                return;

            if (typeof(TChild) == typeof(MBCollectible) && groupRoot.GetComponent<MBCollectibleGroup>() == null)
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Warning,
                    "Collectible challenge root is missing MBCollectibleGroup. Add it so maps can react when any or all collectibles are collected."));
            }
            else if (typeof(TChild) == typeof(MBPhotoSpot) && groupRoot.GetComponent<MBPhotoSpotGroup>() == null)
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Warning,
                    "Photo Spots root is missing MBPhotoSpotGroup. Add it so maps can react to photo spot progress and completion."));
            }
            else if (typeof(TChild) == typeof(MBRace) && groupRoot.GetComponent<MBRaceGroup>() == null)
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Warning,
                    "Races root is missing MBRaceGroup. Add it so maps can react to race progress and completion."));
            }
            else if (typeof(TChild) == typeof(MBSecretGap) && groupRoot.GetComponent<MBSecretGapGroup>() == null)
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Warning,
                    "Secret Gap root is missing MBSecretGapGroup. Add it so maps can react to gap progress and completion."));
            }
            else if (typeof(TChild) == typeof(MBSideHit) && groupRoot.GetComponent<MBSideHitGroup>() == null)
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Warning,
                    "Side Hit root is missing MBSideHitGroup. Add it so maps can react to side-hit progress and completion."));
            }
            else if (typeof(TChild) == typeof(MBExpertLine) && groupRoot.GetComponent<MBExpertLineGroup>() == null)
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Warning,
                    "Expert Line root is missing MBExpertLineGroup. Add it so maps can react to expert-line progress and completion."));
            }
            else if (typeof(TChild) == typeof(MBCollectLetter) && groupRoot.GetComponent<MBCollectLettersChallenge>() == null)
            {
                issues.Add(new MapValidationIssue(
                    MapValidationSeverity.Warning,
                    "Collect Letters root is missing MBCollectLettersChallenge. Add it so maps can react when letters are collected and when the challenge is complete."));
            }
        }
    }
}
#endif
