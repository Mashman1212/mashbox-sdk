#if  UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.SDKMain
{
    [InitializeOnLoad]
    public static class MashBoxProjectSettingsSync
    {
        private const string ProfilePath = "Packages/com.mg.mashbox.sdk/EditorResources/MashBoxProjectSettingsProfile.json";
        private const string PrefAutoApply = "MashBoxSDK.AutoApplyProjectSettings";
        private const string TagManagerProjectPath = "ProjectSettings/TagManager.asset";
        private const string TagManagerTemplatePackagePath = "Packages/com.mg.mashbox.sdk/EditorResources/MashBox_TagManager.asset";
        private const string DynamicsManagerProjectPath = "ProjectSettings/DynamicsManager.asset";
        private const string DynamicsManagerTemplatePackagePath = "Packages/com.mg.mashbox.sdk/EditorResources/MashBox_DynamicsManager.asset";
        private const string TimeManagerProjectPath = "ProjectSettings/TimeManager.asset";
        private const string TimeManagerTemplatePackagePath = "Packages/com.mg.mashbox.sdk/EditorResources/MashBox_TimeManager.asset";

        [Serializable]
        public class ProjectSettingsProfile
        {
            public string[] tags = Array.Empty<string>();
            public LayerEntry[] layers = Array.Empty<LayerEntry>();
            public string layerCollisionMatrix = string.Empty;
            public bool enableAllLayerCollisions = false;
            public CollisionRule[] collisions = Array.Empty<CollisionRule>();
            public float fixedTimestep = -1f;
            public float maximumAllowedTimestep = -1f;
            public float timeScale = -1f;
            public float maximumParticleTimestep = -1f;
        }

        [Serializable]
        public class LayerEntry
        {
            public int index;
            public string name;
        }

        [Serializable]
        public class CollisionRule
        {
            public int a;
            public int b;
            public bool collide;
        }

        public class SyncReport
        {
            public bool profileFound;
            public bool profileConfigured;
            public bool changed;
            public readonly List<string> missingTags = new();
            public readonly List<string> missingLayers = new();
            public readonly List<string> conflictingLayers = new();
            public readonly List<string> collisionMismatches = new();
            public readonly List<string> timeMismatches = new();

            public bool IsInSync =>
                profileFound &&
                profileConfigured &&
                missingTags.Count == 0 &&
                missingLayers.Count == 0 &&
                conflictingLayers.Count == 0 &&
                collisionMismatches.Count == 0 &&
                timeMismatches.Count == 0;
        }

        static MashBoxProjectSettingsSync()
        {
            EditorApplication.delayCall += AutoApplyIfEnabled;
        }

        public static bool AutoApplyEnabled
        {
            get => true;
            set => EditorPrefs.SetBool(PrefAutoApply, true);
        }

        public static ProjectSettingsProfile LoadProfile()
        {
            if (!File.Exists(ProfilePath))
                return null;

            var json = File.ReadAllText(ProfilePath);
            if (string.IsNullOrWhiteSpace(json))
                return new ProjectSettingsProfile();

            return JsonUtility.FromJson<ProjectSettingsProfile>(json) ?? new ProjectSettingsProfile();
        }

        public static void SaveCurrentProjectAsProfile()
        {
            var tagManager = LoadTagManager();
            var tagsProp = tagManager.FindProperty("tags");
            var layersProp = tagManager.FindProperty("layers");

            var profile = new ProjectSettingsProfile
            {
                tags = ReadTags(tagsProp).ToArray(),
                layers = ReadCustomLayers(layersProp)
                    .Select(pair => new LayerEntry { index = pair.Key, name = pair.Value })
                    .ToArray(),
                layerCollisionMatrix = ReadLayerCollisionMatrixFromFile(),
                enableAllLayerCollisions = AreAllLayerCollisionsEnabled(),
                collisions = ReadCollisionRules()
                    .Select(rule => new CollisionRule { a = rule.a, b = rule.b, collide = rule.collide })
                    .ToArray(),
                fixedTimestep = ReadTimeSettingFromFile("Fixed Timestep"),
                maximumAllowedTimestep = ReadTimeSettingFromFile("Maximum Allowed Timestep"),
                timeScale = ReadTimeSettingFromFile("m_TimeScale"),
                maximumParticleTimestep = ReadTimeSettingFromFile("Maximum Particle Timestep")
            };

            var dir = Path.GetDirectoryName(ProfilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(ProfilePath, JsonUtility.ToJson(profile, true));
            CopyCurrentTagManagerToTemplate();
            CopyCurrentDynamicsManagerToTemplate();
            CopyCurrentTimeManagerToTemplate();
            AssetDatabase.Refresh();
        }

        public static SyncReport GetSyncReport()
        {
            return BuildReport(LoadProfile());
        }

        public static SyncReport ApplyProfile(bool overwriteConflictingLayers)
        {
            var profile = LoadProfile();
            var report = BuildReport(profile);
            if (!report.profileFound || !report.profileConfigured)
                return report;

            if (overwriteConflictingLayers && TryApplyTagManagerTemplate())
            {
                report.changed = true;
            }
            else if (profile != null)
            {
                var tagManager = LoadTagManager();
                var tagsProp = tagManager.FindProperty("tags");
                var layersProp = tagManager.FindProperty("layers");
                var desiredTags = profile.tags.Where(IsMeaningful).Distinct(StringComparer.Ordinal).ToArray();

                if (!SerializedStringArrayEquals(tagsProp, desiredTags))
                {
                    ReplaceStringArray(tagsProp, desiredTags);
                    report.changed = true;
                }

                foreach (var layer in profile.layers.Where(IsValidLayerEntry))
                {
                    var existing = layersProp.GetArrayElementAtIndex(layer.index).stringValue;
                    if (string.Equals(existing, layer.name, StringComparison.Ordinal))
                        continue;

                    if (string.IsNullOrEmpty(existing) || overwriteConflictingLayers)
                    {
                        layersProp.GetArrayElementAtIndex(layer.index).stringValue = layer.name;
                        report.changed = true;
                    }
                }

                if (report.changed)
                    tagManager.ApplyModifiedPropertiesWithoutUndo();
            }

            if (TryApplyDynamicsManagerTemplate())
            {
                report.changed = true;
            }
            else if (profile != null && IsMeaningful(profile.layerCollisionMatrix))
            {
                if (TryWriteLayerCollisionMatrixToFile(profile.layerCollisionMatrix))
                    report.changed = true;
            }
            else if (profile != null && profile.enableAllLayerCollisions)
            {
                for (var a = 0; a < 32; a++)
                {
                    for (var b = a; b < 32; b++)
                    {
                        if (Physics.GetIgnoreLayerCollision(a, b))
                        {
                            Physics.IgnoreLayerCollision(a, b, false);
                            report.changed = true;
                        }
                    }
                }
            }

            foreach (var collision in (profile?.collisions ?? Array.Empty<CollisionRule>()).Where(IsValidCollisionRule))
            {
                var collidesNow = !Physics.GetIgnoreLayerCollision(collision.a, collision.b);
                if (collidesNow == collision.collide)
                    continue;

                Physics.IgnoreLayerCollision(collision.a, collision.b, !collision.collide);
                report.changed = true;
            }

            if (profile != null)
            {
                report.changed |= TryApplyTimeSettings(profile);
            }

            if (report.changed)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return BuildReport(profile);
        }

        //[MenuItem("MashBox/Setup/Apply SDK Tags And Layers")]
        private static void ApplySdkTagsAndLayersMenu()
        {
            var report = ApplyProfile(false);
            ShowResultDialog("Apply SDK Tags And Layers", report, false);
        }

        //[MenuItem("MashBox/Setup/Force SDK Tags And Layers")]
        private static void ForceSdkTagsAndLayersMenu()
        {
            var report = ApplyProfile(true);
            ShowResultDialog("Force SDK Tags And Layers", report, true);
        }

        //[MenuItem("MashBox/Dev/Capture Current Project Tags And Layers As SDK Defaults")]
        private static void CaptureCurrentProjectSettingsMenu()
        {
            SaveCurrentProjectAsProfile();
            EditorUtility.DisplayDialog(
                "SDK Project Settings Captured",
                $"Saved the current project's tags and custom layers to:\n{ProfilePath}",
                "OK");
        }

        private static void AutoApplyIfEnabled()
        {
            var report = GetSyncReport();
            if (!report.profileFound || !report.profileConfigured)
                return;

            if (report.IsInSync)
                return;

            ApplyProfile(true);
        }

        private static SyncReport BuildReport(ProjectSettingsProfile profile)
        {
            var safeProfile = profile ?? new ProjectSettingsProfile();
            var report = new SyncReport
            {
                profileFound = profile != null || HasTagManagerTemplate() || HasDynamicsManagerTemplate() || HasTimeManagerTemplate()
            };

            if (profile == null && !HasTagManagerTemplate() && !HasDynamicsManagerTemplate() && !HasTimeManagerTemplate())
                return report;

            var requiredTags = safeProfile.tags?.Where(IsMeaningful).Distinct(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
            var requiredLayers = safeProfile.layers?.Where(IsValidLayerEntry)
                .GroupBy(layer => layer.index)
                .Select(group => group.Last())
                .ToArray() ?? Array.Empty<LayerEntry>();
            var requiredLayerCollisionMatrix = safeProfile.layerCollisionMatrix ?? string.Empty;
            var requiredCollisions = safeProfile.collisions?.Where(IsValidCollisionRule)
                .GroupBy(rule => GetCollisionKey(rule.a, rule.b))
                .Select(group => group.Last())
                .ToArray() ?? Array.Empty<CollisionRule>();

            report.profileConfigured = requiredTags.Length > 0 ||
                                       requiredLayers.Length > 0 ||
                                       IsMeaningful(requiredLayerCollisionMatrix) ||
                                       safeProfile.enableAllLayerCollisions ||
                                       requiredCollisions.Length > 0 ||
                                       HasTagManagerTemplate() ||
                                       HasDynamicsManagerTemplate() ||
                                       HasTimeManagerTemplate() ||
                                       IsValidTimeSetting(safeProfile.fixedTimestep) ||
                                       IsValidTimeSetting(safeProfile.maximumAllowedTimestep) ||
                                       IsValidTimeSetting(safeProfile.timeScale) ||
                                       IsValidTimeSetting(safeProfile.maximumParticleTimestep);
            if (!report.profileConfigured)
                return report;

            var tagManager = LoadTagManager();
            var tagsProp = tagManager.FindProperty("tags");
            var layersProp = tagManager.FindProperty("layers");

            if (HasTagManagerTemplate())
            {
                if (DoesTagManagerDifferFromTemplate())
                    report.conflictingLayers.Add("TagManager.asset differs from the SDK template.");
            }
            else
            {
                foreach (var tag in requiredTags)
                {
                    if (!ContainsString(tagsProp, tag))
                        report.missingTags.Add(tag);
                }

                var currentTags = ReadTags(tagsProp).ToArray();
                var extraTags = currentTags.Where(tag => !requiredTags.Contains(tag, StringComparer.Ordinal)).ToArray();
                if (extraTags.Length > 0)
                    report.conflictingLayers.Add($"Extra tags in project: {string.Join(", ", extraTags)}");

                foreach (var layer in requiredLayers)
                {
                    var existing = layersProp.GetArrayElementAtIndex(layer.index).stringValue;
                    if (string.IsNullOrEmpty(existing))
                    {
                        report.missingLayers.Add($"Layer {layer.index}: {layer.name}");
                        continue;
                    }

                    if (!string.Equals(existing, layer.name, StringComparison.Ordinal))
                        report.conflictingLayers.Add($"Layer {layer.index}: project has '{existing}', SDK requires '{layer.name}'");
                }
            }

            if (DoesDynamicsManagerDifferFromTemplate())
            {
                report.collisionMismatches.Add("Physics settings file differs from the SDK template.");
            }
            else if (IsMeaningful(requiredLayerCollisionMatrix))
            {
                var currentLayerCollisionMatrix = ReadLayerCollisionMatrixFromFile();
                if (!string.Equals(currentLayerCollisionMatrix, requiredLayerCollisionMatrix, StringComparison.Ordinal))
                    report.collisionMismatches.Add("Physics layer collision matrix differs from the SDK profile.");
            }
            else if (safeProfile.enableAllLayerCollisions)
            {
                for (var a = 0; a < 32; a++)
                {
                    for (var b = a; b < 32; b++)
                    {
                        if (Physics.GetIgnoreLayerCollision(a, b))
                        {
                            report.collisionMismatches.Add(
                                $"{GetLayerDisplayName(a, layersProp)} <-> {GetLayerDisplayName(b, layersProp)}: project=ignore, SDK=collide");
                        }
                    }
                }
            }

            foreach (var collision in requiredCollisions)
            {
                var collidesNow = !Physics.GetIgnoreLayerCollision(collision.a, collision.b);
                if (collidesNow != collision.collide)
                {
                    var expected = collision.collide ? "collide" : "ignore";
                    var actual = collidesNow ? "collide" : "ignore";
                    report.collisionMismatches.Add(
                        $"{GetLayerDisplayName(collision.a, layersProp)} <-> {GetLayerDisplayName(collision.b, layersProp)}: project={actual}, SDK={expected}");
                }
            }

            AddTimeMismatch(report.timeMismatches, "Fixed Timestep", safeProfile.fixedTimestep);
            AddTimeMismatch(report.timeMismatches, "Maximum Allowed Timestep", safeProfile.maximumAllowedTimestep);
            AddTimeMismatch(report.timeMismatches, "m_TimeScale", safeProfile.timeScale, "Time Scale");
            AddTimeMismatch(report.timeMismatches, "Maximum Particle Timestep", safeProfile.maximumParticleTimestep);

            return report;
        }

        private static SerializedObject LoadTagManager()
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath(TagManagerProjectPath).FirstOrDefault();
            if (asset == null)
                throw new InvalidOperationException($"Could not load {TagManagerProjectPath}");

            return new SerializedObject(asset);
        }

        private static SerializedObject LoadTimeManager()
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath(TimeManagerProjectPath).FirstOrDefault();
            if (asset == null)
                throw new InvalidOperationException($"Could not load {TimeManagerProjectPath}");

            return new SerializedObject(asset);
        }

        private static string GetTagManagerAbsolutePath()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, TagManagerProjectPath);
        }

        private static string GetTagManagerTemplateAbsolutePath()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, TagManagerTemplatePackagePath);
        }

        private static string GetDynamicsManagerAbsolutePath()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, DynamicsManagerProjectPath);
        }

        private static string GetDynamicsManagerTemplateAbsolutePath()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, DynamicsManagerTemplatePackagePath);
        }

        private static string GetTimeManagerAbsolutePath()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, TimeManagerProjectPath);
        }

        private static string GetTimeManagerTemplateAbsolutePath()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, TimeManagerTemplatePackagePath);
        }

        private static string ReadLayerCollisionMatrixFromFile()
        {
            var path = GetDynamicsManagerAbsolutePath();
            if (!File.Exists(path))
                return string.Empty;

            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith("m_LayerCollisionMatrix:", StringComparison.Ordinal))
                    continue;

                var value = trimmed.Substring("m_LayerCollisionMatrix:".Length).Trim();
                return value;
            }

            return string.Empty;
        }

        private static bool TryWriteLayerCollisionMatrixToFile(string desiredValue)
        {
            if (!IsMeaningful(desiredValue))
                return false;

            var path = GetDynamicsManagerAbsolutePath();
            if (!File.Exists(path))
                return false;

            var original = File.ReadAllText(path);
            var updated = Regex.Replace(
                original,
                @"(^\s*m_LayerCollisionMatrix:\s*)(\S+)",
                $"$1{desiredValue}",
                RegexOptions.Multiline);

            if (string.Equals(original, updated, StringComparison.Ordinal))
                return false;

            File.WriteAllText(path, updated);
            AssetDatabase.Refresh();
            return true;
        }

        private static bool TryApplyDynamicsManagerTemplate()
        {
            var templatePath = GetDynamicsManagerTemplateAbsolutePath();
            var targetPath = GetDynamicsManagerAbsolutePath();
            if (!File.Exists(templatePath) || !File.Exists(targetPath))
                return false;

            var templateText = File.ReadAllText(templatePath);
            var targetText = File.ReadAllText(targetPath);
            if (string.Equals(templateText, targetText, StringComparison.Ordinal))
                return false;

            File.WriteAllText(targetPath, templateText);
            AssetDatabase.Refresh();
            return true;
        }

        private static bool DoesDynamicsManagerDifferFromTemplate()
        {
            var templatePath = GetDynamicsManagerTemplateAbsolutePath();
            var targetPath = GetDynamicsManagerAbsolutePath();
            if (!File.Exists(templatePath) || !File.Exists(targetPath))
                return false;

            return !string.Equals(File.ReadAllText(templatePath), File.ReadAllText(targetPath), StringComparison.Ordinal);
        }

        private static void CopyCurrentDynamicsManagerToTemplate()
        {
            var sourcePath = GetDynamicsManagerAbsolutePath();
            var templatePath = GetDynamicsManagerTemplateAbsolutePath();
            if (!File.Exists(sourcePath))
                return;

            var dir = Path.GetDirectoryName(templatePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(templatePath, File.ReadAllText(sourcePath));
        }

        private static float ReadTimeSettingFromFile(string settingName)
        {
            if (TryReadTimeSettingFallback(settingName, out var fileValue))
                return fileValue;

            try
            {
                var timeManager = LoadTimeManager();
                var property = timeManager.FindProperty(settingName);
                if (property != null && property.propertyType == SerializedPropertyType.Float)
                    return property.floatValue;
            }
            catch (Exception)
            {
            }

            return -1f;
        }

        private static bool TryReadTimeSettingFallback(string settingName, out float value)
        {
            value = -1f;

            var path = GetTimeManagerAbsolutePath();
            if (!File.Exists(path))
                return false;

            foreach (var line in File.ReadLines(path))
            {
                var match = Regex.Match(line, $@"^\s*{Regex.Escape(settingName)}:\s*([-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)\s*$");
                if (!match.Success)
                    continue;

                if (float.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                    return true;
            }

            return false;
        }

        private static bool TryApplyTimeSettings(ProjectSettingsProfile profile)
        {
            if (profile == null)
                return false;

            SerializedObject timeManager;
            try
            {
                timeManager = LoadTimeManager();
            }
            catch (Exception)
            {
                return false;
            }

            var changed = false;
            changed |= TrySetTimeProperty(timeManager, "Fixed Timestep", profile.fixedTimestep);
            changed |= TrySetTimeProperty(timeManager, "Maximum Allowed Timestep", profile.maximumAllowedTimestep);
            changed |= TrySetTimeProperty(timeManager, "m_TimeScale", profile.timeScale);
            changed |= TrySetTimeProperty(timeManager, "Maximum Particle Timestep", profile.maximumParticleTimestep);

            if (!changed)
                return false;

            timeManager.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return true;
        }

        private static bool TrySetTimeProperty(SerializedObject timeManager, string propertyName, float desiredValue)
        {
            if (timeManager == null || !IsValidTimeSetting(desiredValue))
                return false;

            var property = timeManager.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Float)
                return TryWriteTimeSettingFallback(propertyName, desiredValue);

            if (Mathf.Approximately(property.floatValue, desiredValue))
                return false;

            property.floatValue = desiredValue;
            return true;
        }

        private static bool TryWriteTimeSettingFallback(string settingName, float desiredValue)
        {
            var path = GetTimeManagerAbsolutePath();
            if (!File.Exists(path))
                return false;

            var original = File.ReadAllText(path);
            var replacementValue = desiredValue.ToString("0.0#######", System.Globalization.CultureInfo.InvariantCulture);
            var updated = Regex.Replace(
                original,
                $@"(^\s*{Regex.Escape(settingName)}:\s*)([-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)",
                $"$1{replacementValue}",
                RegexOptions.Multiline);

            if (string.Equals(original, updated, StringComparison.Ordinal))
                return false;

            File.WriteAllText(path, updated);
            AssetDatabase.Refresh();
            return true;
        }

        private static bool HasTagManagerTemplate()
        {
            return File.Exists(GetTagManagerTemplateAbsolutePath());
        }

        private static bool HasDynamicsManagerTemplate()
        {
            return File.Exists(GetDynamicsManagerTemplateAbsolutePath());
        }

        private static bool HasTimeManagerTemplate()
        {
            return File.Exists(GetTimeManagerTemplateAbsolutePath());
        }

        private static void CopyCurrentTimeManagerToTemplate()
        {
            var sourcePath = GetTimeManagerAbsolutePath();
            var templatePath = GetTimeManagerTemplateAbsolutePath();
            if (!File.Exists(sourcePath))
                return;

            var dir = Path.GetDirectoryName(templatePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(templatePath, File.ReadAllText(sourcePath));
        }

        private static bool TryApplyTagManagerTemplate()
        {
            var templatePath = GetTagManagerTemplateAbsolutePath();
            var targetPath = GetTagManagerAbsolutePath();
            if (!File.Exists(templatePath) || !File.Exists(targetPath))
                return false;

            var templateText = File.ReadAllText(templatePath);
            var targetText = File.ReadAllText(targetPath);
            if (string.Equals(templateText, targetText, StringComparison.Ordinal))
                return false;

            File.WriteAllText(targetPath, templateText);
            AssetDatabase.Refresh();
            return true;
        }

        private static bool DoesTagManagerDifferFromTemplate()
        {
            var templatePath = GetTagManagerTemplateAbsolutePath();
            var targetPath = GetTagManagerAbsolutePath();
            if (!File.Exists(templatePath) || !File.Exists(targetPath))
                return false;

            return !string.Equals(File.ReadAllText(templatePath), File.ReadAllText(targetPath), StringComparison.Ordinal);
        }

        private static void CopyCurrentTagManagerToTemplate()
        {
            var sourcePath = GetTagManagerAbsolutePath();
            var templatePath = GetTagManagerTemplateAbsolutePath();
            if (!File.Exists(sourcePath))
                return;

            var dir = Path.GetDirectoryName(templatePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(templatePath, File.ReadAllText(sourcePath));
        }

        private static IEnumerable<string> ReadTags(SerializedProperty tagsProp)
        {
            for (var i = 0; i < tagsProp.arraySize; i++)
            {
                var value = tagsProp.GetArrayElementAtIndex(i).stringValue;
                if (IsMeaningful(value))
                    yield return value;
            }
        }

        private static IEnumerable<KeyValuePair<int, string>> ReadCustomLayers(SerializedProperty layersProp)
        {
            for (var i = 0; i < Math.Min(32, layersProp.arraySize); i++)
            {
                if (!IsEditableLayerIndex(i))
                    continue;

                var value = layersProp.GetArrayElementAtIndex(i).stringValue;
                if (IsMeaningful(value))
                    yield return new KeyValuePair<int, string>(i, value);
            }
        }

        private static IEnumerable<(int a, int b, bool collide)> ReadCollisionRules()
        {
            for (var a = 0; a < 32; a++)
            {
                for (var b = a; b < 32; b++)
                {
                    yield return (a, b, !Physics.GetIgnoreLayerCollision(a, b));
                }
            }
        }

        private static bool AreAllLayerCollisionsEnabled()
        {
            for (var a = 0; a < 32; a++)
            {
                for (var b = a; b < 32; b++)
                {
                    if (Physics.GetIgnoreLayerCollision(a, b))
                        return false;
                }
            }

            return true;
        }

        private static bool ContainsString(SerializedProperty arrayProp, string value)
        {
            for (var i = 0; i < arrayProp.arraySize; i++)
            {
                if (string.Equals(arrayProp.GetArrayElementAtIndex(i).stringValue, value, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static void AppendString(SerializedProperty arrayProp, string value)
        {
            arrayProp.arraySize++;
            arrayProp.GetArrayElementAtIndex(arrayProp.arraySize - 1).stringValue = value;
        }

        private static void ReplaceStringArray(SerializedProperty arrayProp, IReadOnlyList<string> values)
        {
            arrayProp.arraySize = values.Count;
            for (var i = 0; i < values.Count; i++)
                arrayProp.GetArrayElementAtIndex(i).stringValue = values[i];
        }

        private static bool SerializedStringArrayEquals(SerializedProperty arrayProp, IReadOnlyList<string> values)
        {
            if (arrayProp.arraySize != values.Count)
                return false;

            for (var i = 0; i < values.Count; i++)
            {
                if (!string.Equals(arrayProp.GetArrayElementAtIndex(i).stringValue, values[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private static bool IsMeaningful(string value)
        {
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool IsValidLayerEntry(LayerEntry entry)
        {
            return entry != null &&
                   IsEditableLayerIndex(entry.index) &&
                   IsMeaningful(entry.name);
        }

        private static bool IsValidCollisionRule(CollisionRule rule)
        {
            return rule != null &&
                   rule.a >= 0 && rule.a < 32 &&
                   rule.b >= 0 && rule.b < 32;
        }

        private static bool IsValidTimeSetting(float value)
        {
            return value >= 0f;
        }

        private static void AddTimeMismatch(List<string> mismatches, string settingKey, float requiredValue, string displayName = null)
        {
            if (!IsValidTimeSetting(requiredValue))
                return;

            var currentValue = ReadTimeSettingFromFile(settingKey);
            if (!IsValidTimeSetting(currentValue))
            {
                mismatches.Add($"{displayName ?? settingKey}: project setting not found.");
                return;
            }

            if (Mathf.Approximately(currentValue, requiredValue))
                return;

            mismatches.Add($"{displayName ?? settingKey}: project={currentValue:0.0#######}, SDK={requiredValue:0.0#######}");
        }

        private static bool IsEditableLayerIndex(int index)
        {
            return index == 3 || (index >= 6 && index < 32);
        }

        private static string GetCollisionKey(int a, int b)
        {
            return a <= b ? $"{a}:{b}" : $"{b}:{a}";
        }

        private static string GetLayerDisplayName(int index, SerializedProperty layersProp)
        {
            if (index < 0 || index >= layersProp.arraySize)
                return $"Layer {index}";

            var name = layersProp.GetArrayElementAtIndex(index).stringValue;
            return string.IsNullOrWhiteSpace(name) ? $"Layer {index}" : name;
        }

        private static void ShowResultDialog(string title, SyncReport report, bool overwriteConflictingLayers)
        {
            if (!report.profileFound)
            {
                EditorUtility.DisplayDialog(title, $"No SDK project settings profile was found at:\n{ProfilePath}", "OK");
                return;
            }

            if (!report.profileConfigured)
            {
                EditorUtility.DisplayDialog(
                    title,
                    "The SDK project settings profile is empty.\n\nCapture the canonical project's tags and layers first, then apply them to SDK users' projects.",
                    "OK");
                return;
            }

            if (report.IsInSync)
            {
                var modeText = overwriteConflictingLayers ? "forced" : "applied";
                EditorUtility.DisplayDialog(title, $"SDK tags and layers are now in sync.\n\nProject settings {modeText} successfully.", "OK");
                return;
            }

            var message = "Some SDK project settings still need attention.\n\n";
            if (report.missingTags.Count > 0)
                message += "Missing tags:\n" + string.Join("\n", report.missingTags.Select(tag => $"• {tag}")) + "\n\n";
            if (report.missingLayers.Count > 0)
                message += "Missing layers:\n" + string.Join("\n", report.missingLayers.Select(layer => $"• {layer}")) + "\n\n";
            if (report.conflictingLayers.Count > 0)
                message += "Conflicting layers:\n" + string.Join("\n", report.conflictingLayers.Select(layer => $"• {layer}"));

            EditorUtility.DisplayDialog(title, message.Trim(), "OK");
        }
    }
}


#endif
