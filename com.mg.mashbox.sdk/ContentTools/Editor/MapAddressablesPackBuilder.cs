#if UNITY_EDITOR_WIN
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace MashBoxSDK.ContentTools.Editor
{
    public static class MapAddressablesPackBuilder
    {
        private class GroupState
        {
            public UnityEditor.AddressableAssets.Settings.AddressableAssetGroup Group;
            public BundledAssetGroupSchema Schema;
            public string OriginalBuildVarId;
            public string OriginalLoadVarId;
            public bool IncludeInBuild;
        }

        private class ProfileOverrideBackup
        {
            public string VariableName;
            public string PreviousValue;
        }

        public struct BuildOptions
        {
            public string ProfileId;
            public bool EnableRemoteCatalog;
            public bool DisableOtherGroups;
            public bool SetPlayerVersionOverride;
            public string SessionRemoteBuildRootOverride;
        }

        public static void BuildPack(MapContentPackDefinition pack, BuildOptions options)
        {
            if (pack == null)
            {
                Debug.LogError("Map pack is null.");
                return;
            }

            if (pack.Scene == null)
            {
                Debug.LogError($"Map pack '{pack.name}' does not have a scene assigned.");
                return;
            }

            pack.SyncToAddressables();

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("Addressables settings not found.");
                return;
            }

            if (string.IsNullOrWhiteSpace(options.SessionRemoteBuildRootOverride))
            {
                Debug.LogError("Build output path is not set.");
                return;
            }

            if (string.IsNullOrEmpty(options.ProfileId))
                options.ProfileId = settings.activeProfileId;

            var profile = settings.profileSettings;
            var originalScenes = EditorBuildSettings.scenes;
            var buildRoot = options.SessionRemoteBuildRootOverride.Replace("\\", "/");
            var packBuildFolder = Path.Combine(buildRoot, pack.PackName).Replace("\\", "/");
            var loadPathValue = pack.BuildToCustomFolder
                ? "{Application.streamingAssetsPath}/Addressables/Maps/" + pack.PackName
                : "{Application.streamingAssetsPath}/Addressables/Maps/Local Custom/" + pack.PackName;

            var tracked = settings.groups
                .Where(group => group != null && group.Name == pack.PackName)
                .Select(group =>
                {
                    var schema = group.GetSchema<BundledAssetGroupSchema>();
                    if (schema == null)
                        return null;

                    return new GroupState
                    {
                        Group = group,
                        Schema = schema,
                        OriginalBuildVarId = schema.BuildPath?.Id,
                        OriginalLoadVarId = schema.LoadPath?.Id,
                        IncludeInBuild = schema.IncludeInBuild
                    };
                })
                .Where(state => state != null)
                .ToList();

            if (tracked.Count == 0)
            {
                Debug.LogError($"No Addressables group found for map pack '{pack.PackName}'.");
                return;
            }

            if (options.DisableOtherGroups)
            {
                foreach (var group in settings.groups.Where(g => g != null))
                {
                    var schema = group.GetSchema<BundledAssetGroupSchema>();
                    if (schema == null)
                        continue;

                    schema.IncludeInBuild = group.Name == pack.PackName;
                    EditorUtility.SetDirty(group);
                }
            }

            if (!Directory.Exists(buildRoot))
                Directory.CreateDirectory(buildRoot);

            if (Directory.Exists(packBuildFolder))
                Directory.Delete(packBuildFolder, true);

            Directory.CreateDirectory(packBuildFolder);

            var buildVarName = EnsureProfileVar(profile, $"MapPack_{pack.PackName}_BuildPath", packBuildFolder);
            var loadVarName = EnsureProfileVar(profile, $"MapPack_{pack.PackName}_LoadPath", loadPathValue);

            var backups = new List<ProfileOverrideBackup>
            {
                new ProfileOverrideBackup
                {
                    VariableName = buildVarName,
                    PreviousValue = profile.GetValueByName(options.ProfileId, buildVarName)
                },
                new ProfileOverrideBackup
                {
                    VariableName = loadVarName,
                    PreviousValue = profile.GetValueByName(options.ProfileId, loadVarName)
                }
            };

            profile.SetValue(options.ProfileId, buildVarName, packBuildFolder);
            profile.SetValue(options.ProfileId, loadVarName, loadPathValue);

            var previousRemoteCatalogBuildVarId = settings.RemoteCatalogBuildPath?.Id;
            var previousRemoteCatalogLoadVarId = settings.RemoteCatalogLoadPath?.Id;

            if (options.EnableRemoteCatalog)
                settings.BuildRemoteCatalog = true;

            if (options.SetPlayerVersionOverride)
                settings.OverridePlayerVersion = pack.PackName;

            settings.RemoteCatalogBuildPath?.SetVariableByName(settings, buildVarName);
            settings.RemoteCatalogLoadPath?.SetVariableByName(settings, loadVarName);

            foreach (var state in tracked)
            {
                state.Schema.BuildPath.SetVariableByName(settings, buildVarName);
                state.Schema.LoadPath.SetVariableByName(settings, loadVarName);
                state.Schema.IncludeInBuild = true;
                EditorUtility.SetDirty(state.Group);
            }

            SimplifyAddressesForSelectedGroups(tracked.Select(t => t.Group));

            try
            {
                EnsureBuildTargetIsValid();
                EditorBuildSettings.scenes = new EditorBuildSettingsScene[0];
                AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult _);
                RewriteCatalogForStreamingAssets(packBuildFolder, pack.PackName, pack.BuildToCustomFolder);
            }
            finally
            {
                foreach (var state in tracked)
                {
                    if (!string.IsNullOrEmpty(state.OriginalBuildVarId))
                        state.Schema.BuildPath.SetVariableById(settings, state.OriginalBuildVarId);
                    if (!string.IsNullOrEmpty(state.OriginalLoadVarId))
                        state.Schema.LoadPath.SetVariableById(settings, state.OriginalLoadVarId);

                    state.Schema.IncludeInBuild = state.IncludeInBuild;
                    EditorUtility.SetDirty(state.Group);
                }

                foreach (var backup in backups)
                    profile.SetValue(options.ProfileId, backup.VariableName, backup.PreviousValue);

                if (!string.IsNullOrEmpty(previousRemoteCatalogBuildVarId))
                    settings.RemoteCatalogBuildPath?.SetVariableById(settings, previousRemoteCatalogBuildVarId);
                if (!string.IsNullOrEmpty(previousRemoteCatalogLoadVarId))
                    settings.RemoteCatalogLoadPath?.SetVariableById(settings, previousRemoteCatalogLoadVarId);

                EditorBuildSettings.scenes = originalScenes;
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private static void RewriteCatalogForStreamingAssets(string packBuildFolder, string packName, bool buildToCustomFolder)
        {
            var catalogs = Directory.GetFiles(packBuildFolder, "catalog_*.json", SearchOption.AllDirectories);
            if (catalogs.Length == 0)
                return;

            var catalogPath = catalogs[0];
            var json = File.ReadAllText(catalogPath, Encoding.UTF8);
            var prefixForward = (packBuildFolder + "/").Replace("\\", "/");
            var prefixBack = (packBuildFolder + "\\").Replace("/", "\\");
            var baseFolder = buildToCustomFolder
                ? "Addressables\\\\Maps\\\\"
                : "Addressables\\\\Maps\\\\Local Custom\\\\";
            var dynamicPath = "{Application.streamingAssetsPath}\\\\" + baseFolder + packName + "\\\\";

            json = json.Replace(prefixForward, dynamicPath);
            json = json.Replace(prefixBack, dynamicPath);
            File.WriteAllText(catalogPath, json, Encoding.UTF8);

            var hashPath = Path.Combine(Path.GetDirectoryName(catalogPath) ?? packBuildFolder,
                Path.GetFileNameWithoutExtension(catalogPath) + ".hash");
            File.WriteAllText(hashPath, ComputeMd5(json), Encoding.UTF8);
        }

        private static void SimplifyAddressesForSelectedGroups(IEnumerable<UnityEditor.AddressableAssets.Settings.AddressableAssetGroup> groups)
        {
            var used = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                foreach (var entry in group.entries.ToList())
                {
                    if (entry?.TargetAsset == null)
                        continue;

                    var filename = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(entry.TargetAsset));
                    var candidate = filename;
                    var suffix = 1;
                    while (used.Contains(candidate))
                        candidate = filename + "_" + suffix++;

                    entry.SetAddress(candidate);
                    used.Add(candidate);
                }

                EditorUtility.SetDirty(group);
            }
        }

        private static string EnsureProfileVar(AddressableAssetProfileSettings profile, string variableName, string defaultValue)
        {
            if (!profile.GetVariableNames().Contains(variableName))
                profile.CreateValue(variableName, defaultValue);

            return variableName;
        }

        private static void EnsureBuildTargetIsValid()
        {
            var activeTarget = EditorUserBuildSettings.activeBuildTarget;
            var activeGroup = BuildPipeline.GetBuildTargetGroup(activeTarget);

            if (activeGroup != BuildTargetGroup.Unknown)
                return;

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
        }

        private static string ComputeMd5(string text)
        {
            using var md5 = MD5.Create();
            var bytes = Encoding.UTF8.GetBytes(text);
            var hash = md5.ComputeHash(bytes);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var value in hash)
                builder.Append(value.ToString("x2"));
            return builder.ToString();
        }
    }
}
#endif
