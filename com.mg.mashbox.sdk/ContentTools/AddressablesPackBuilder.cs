#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Compilation;
using UnityEngine;

namespace MashBoxSDK.ContentTools
{
    /// <summary>
    /// Builds a single content pack to the chosen Build Location:
    /// - ALWAYS writes bundles and catalog to a physical folder: <BuildLocation>/<pack>
    /// - Before build, simplifies each selected group's entry addresses to the bare file name (unique across all selected groups)
    /// - Uses non-dynamic Build/Load during the build to avoid SBP mismatches
    /// - AFTER the build, if BuildLocation is under StreamingAssets, rewrites catalog*.json
    ///   so internal paths become JSON-escaped:
    ///   {Application.streamingAssetsPath}\\<pack>\\..
    ///   and recomputes catalog.hash accordingly (or deletes it on failure).
    /// - Restores Addressables settings afterward
    /// Works by variable NAME (no GUID APIs) for broad Addressables compatibility.
    /// </summary>
    public static class AddressablesPackBuilder
    {
        private const string JsonCatalogScriptingDefine = "ENABLE_JSON_CATALOG";
        private const string PendingBuildQueueKey = "MashBoxSDK.PendingJsonCatalogPackBuilds";

        [InitializeOnLoadMethod]
        private static void ConfigureJsonCatalogOnEditorLoad()
        {
            // Addressables 2.x selects its serializer at compile time. Run after every
            // domain reload so importing the SDK or switching build targets configures
            // the project and lets Unity recompile before a creator can start a build.
            EditorApplication.delayCall += () =>
            {
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings != null)
                    EnsureJsonCatalogEnabled(settings);
                else if (typeof(AddressableAssetSettings).GetProperty("EnableJsonCatalog") != null)
                    EnsureJsonCatalogScriptingDefine();

                ResumePendingBuildsIfReady(settings);
            };
        }

        [Serializable]
        private class PendingPackBuildRequest
        {
            public string definitionGuid;
            public BuildOptions options;
        }

        [Serializable]
        private class PendingPackBuildQueue
        {
            public List<PendingPackBuildRequest> requests = new List<PendingPackBuildRequest>();
        }

        [System.Serializable]
        private class PackBuildManifest
        {
            public string packName;
            public string version;
            public string profileName;
            public string playerVersionOverride;
            public string catalogRemoteUrl;
            public string catalogLocalPath;
            public string bundlesRemoteRoot;
            public string bundlesLocalPath;
        }

        private class GroupState
        {
            public AddressableAssetGroup group;
            public BundledAssetGroupSchema schema;
            public string origBuildVarId;   // original variable (ID) reference from the group
            public string origLoadVarId;    // original variable (ID) reference from the group
            public bool includeInBuild;
        }

        private class ProfileOverrideBackup
        {
            // We store the variable NAME here; SetValue accepts names.
            public string varId;            // (name)
            public string previousValue;
        }

        private class ProxyMaterialState
        {
            public GameObject prefabRoot;
            public Renderer renderer;
            public Material[] originalMaterials;
        }

        [Serializable]
        public struct BuildOptions
        {
            public string profileId;                       // Addressables profile to build with
            public bool rebuildPlayerContent;              // reserved
            public bool enableRemoteCatalog;               // force remote catalog on
            public bool disableOtherGroups;                // include only selected pack's groups
            public bool writeManifestJson;                 // write manifest JSON
            public string manifestFileName;                // default: {pack}.manifest.json
            public bool setPlayerVersionOverride;          // sets OverridePlayerVersion = {pack}_{version}

            // From the window:
            public string sessionRemoteBuildRootOverride;  // Build Location (filesystem folder) - authoritative
            public string sessionRemoteLoadRootOverride;   // ignored here during build (we unify to non-dynamic)

            // Keep groups on Local-style schemas before rewire (so report looks sane)
            public bool forceLocalPaths;
        }

        private static void EnsureBuildTargetIsValid()
        {
            var activeTarget = EditorUserBuildSettings.activeBuildTarget;
            var activeGroup  = BuildPipeline.GetBuildTargetGroup(activeTarget);

            if (activeGroup == BuildTargetGroup.Unknown)
            {
                // Default to a sane platform (e.g., Windows)
                activeTarget = BuildTarget.StandaloneWindows64;
                activeGroup  = BuildTargetGroup.Standalone;

                EditorUserBuildSettings.SwitchActiveBuildTarget(activeGroup, activeTarget);
                Debug.Log($"[AddressablesPackBuilder] Switched active build target to {activeTarget} ({activeGroup})");
            }
        }
        
        

        public static void BuildPack(ContentPackDefinition def, BuildOptions opts)
        {
            if (def == null) { Debug.LogError("ContentPackDefinition is null"); return; }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) { Debug.LogError("Addressables settings not found."); return; }

            // Ensure the icon Addressables group exists and is current before selection
            try { def.SyncToAddressables(); } catch {}

            if (string.IsNullOrEmpty(opts.profileId))
                opts.profileId = settings.activeProfileId;

            var originalScenes = EditorBuildSettings.scenes;
            
            var prof = settings.profileSettings;
            var profileName = prof.GetProfileName(opts.profileId);

            // Determine remote/local roots from the session override in the window
            // (We unify both Build and Load to the SAME per-pack, non-dynamic locations during the build)
            string buildRoot = opts.sessionRemoteBuildRootOverride?.Replace("\\", "/") ?? "";
            if (string.IsNullOrEmpty(buildRoot))
            {
                Debug.LogError("Build Location (Remote Build Path) is not set. Open the builder window and choose a folder.");
                return;
            }

            // Per-pack subfolder
            string sub = def.PackName;

            // Build only the selected groups: the pack group and the icon group
            bool isolate = opts.disableOtherGroups;

            // MashBox's content loader consumes and patches JSON catalogs. Addressables 2.x
            // defaults new projects to binary catalogs, so enforce the required format at
            // the SDK build boundary instead of relying on each creator's project setting.
            EnsureJsonCatalogEnabled(settings);
            if (!IsJsonCatalogSerializerActive(settings))
            {
                QueueBuildAfterJsonCatalogRecompile(def, opts);
                return;
            }

            if (opts.enableRemoteCatalog) settings.BuildRemoteCatalog = true;
            if (opts.setPlayerVersionOverride) settings.OverridePlayerVersion = $"{def.PackName}";

            // Track groups + originals
            var groupSet = new HashSet<string>();
            groupSet.Add(def.PackName);

            if (!def.IsCorePack)
            {
                groupSet.Add("MashBoxCustomizationCore");
                groupSet.Add(def.PackName + "_Icons");
            }

            var tracked = new List<GroupState>();

            foreach (var g in settings.groups.Where(x => x != null && groupSet.Contains(x.Name)))
            {
                var s = g.GetSchema<BundledAssetGroupSchema>();
                if (s == null) continue;

                string ob = s.BuildPath != null ? s.BuildPath.Id : null;
                string ol = s.LoadPath != null ? s.LoadPath.Id : null;

                tracked.Add(new GroupState
                {
                    group = g,
                    schema = s,
                    origBuildVarId = ob,
                    origLoadVarId = ol,
                    includeInBuild = s.IncludeInBuild
                });
            }

            // Optionally remove all other groups from the build (only build the selected pack + icons)
            if (isolate)
            {
                foreach (var g in settings.groups.Where(x => x != null))
                {
                    var s = g.GetSchema<BundledAssetGroupSchema>();
                    if (s == null) continue;

                    bool selected = groupSet.Contains(g.Name);
                    
                    
                    if(!def.IsCorePack && g.Name == "MashBoxCustomizationCore")
                    {
                        s.IncludeInBuild = false;
                    }
                    else
                    {
                        s.IncludeInBuild = selected;
                    }
                    
                    EditorUtility.SetDirty(g);
                }
            }

            if (opts.forceLocalPaths)
            {
                // No-op placeholder to preserve behavior (schema enum tweaking if needed)
            }

            string serverData = buildRoot; // where bundles & catalog are written
            if (!Directory.Exists(serverData))
            {
                try { Directory.CreateDirectory(serverData); }
                catch { Debug.LogError($"Could not create build root folder: {serverData}"); return; }
            }

            string packBuildFolder = Path.Combine(buildRoot, sub).Replace("\\", "/");

// 🔥 CLEAN OLD BUILD
            if (Directory.Exists(packBuildFolder))
            {
                try
                {
                    Directory.Delete(packBuildFolder, true);
                    Debug.Log($"[Addressables] Cleaned pack folder: {packBuildFolder}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to clean pack folder: {packBuildFolder}\n{e}");
                }
            }

// recreate fresh folder
            try { Directory.CreateDirectory(packBuildFolder); } catch {}

            // During build: make BOTH Build & Load non-dynamic and pointing to the same folder
            string buildPathValue = packBuildFolder;            // filesystem path
            string loadPathValue = "{Application.streamingAssetsPath}/Addressables/Customization/Local Custom/" + sub;

            if (def.BuildToCustomFolder)
            {
                loadPathValue = "{Application.streamingAssetsPath}/Addressables/Customization/" + sub;//for  built-in
            }
            
            // Create pack-scoped profile vars (by NAME) and set values
            string packBuildVarName = EnsureProfileVar(prof, $"Pack_{def.PackName}_BuildPath", buildPathValue);
            string packLoadVarName  = EnsureProfileVar(prof, $"Pack_{def.PackName}_LoadPath",  loadPathValue);

            var backups = new List<ProfileOverrideBackup>();
            foreach (var (name, val) in new[] { (packBuildVarName, buildPathValue), (packLoadVarName, loadPathValue) })
            {
                string prev = prof.GetValueByName(opts.profileId, name);
                backups.Add(new ProfileOverrideBackup { varId = name, previousValue = prev });
                prof.SetValue(opts.profileId, name, val);
            }

            // Point global Remote Catalog paths to our per-pack vars for the build output location
            if (settings.RemoteCatalogBuildPath != null)
                settings.RemoteCatalogBuildPath.SetVariableByName(settings, packBuildVarName);
            if (settings.RemoteCatalogLoadPath != null)
                settings.RemoteCatalogLoadPath.SetVariableByName(settings,  packLoadVarName);

            // Remember previous Remote Catalog variable IDs for restore
            string prevRemoteCatalogBuildVarId = settings.RemoteCatalogBuildPath != null ? settings.RemoteCatalogBuildPath.Id : null;
            string prevRemoteCatalogLoadVarId  = settings.RemoteCatalogLoadPath  != null ? settings.RemoteCatalogLoadPath.Id  : null;

            // Rewire ONLY selected groups to pack vars (by NAME)
            foreach (var t in tracked)
            {
                if (t.schema.BuildPath != null) t.schema.BuildPath.SetVariableByName(settings, packBuildVarName);
                if (t.schema.LoadPath  != null) t.schema.LoadPath .SetVariableByName(settings,  packLoadVarName);
                EditorUtility.SetDirty(t.group);
            }

            // Simplify addresses (filename-only) within the selected groups to ensure uniqueness across them
            SimplifyAddressesForSelectedGroups(settings, tracked.Select(t => t.group));
            

            var strippedProxyMaterials = new List<ProxyMaterialState>();
            
            try
            {
                // Build Addressables
                EnsureBuildTargetIsValid(); // ✅ make sure Unity has a valid target
                
                EditorBuildSettings.scenes = new EditorBuildSettingsScene[0];
                strippedProxyMaterials = StripScooterDeckProxyMaterials(def);
                
                AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult buildResult);
                if (!string.IsNullOrEmpty(buildResult.Error))
                    throw new InvalidOperationException($"Addressables build failed for pack '{def.PackName}': {buildResult.Error}");

                if (!def.IsCorePack)
                {
                    RemoveCoreBundlesFromOutput(packBuildFolder, "MashBoxCustomizationCore");
                }
                
                
                // After build: find the produced catalog and adjust it if under StreamingAssets
                bool underSA = TryGetRelativeUnderStreamingAssets(serverData, out var relAfterSA);
                string[] catalogs = Directory.GetFiles(packBuildFolder, "catalog_*.json", SearchOption.AllDirectories);

                if (catalogs.Length == 0)
                {
                    string binaryCatalog = Directory
                        .GetFiles(packBuildFolder, "catalog_*.bin", SearchOption.AllDirectories)
                        .FirstOrDefault();
                    string detail = string.IsNullOrEmpty(binaryCatalog)
                        ? "No catalog file was produced."
                        : $"A binary catalog was produced instead: {binaryCatalog}";

                    throw new InvalidOperationException(
                        $"Pack '{def.PackName}' requires a JSON Addressables catalog, but catalog_*.json was not found. {detail}");
                }

                if (catalogs.Length > 0)
                {
                    string catalogLocal = catalogs[0];
                    string catalogRemoteUrl = GuessCatalogRemoteUrl(loadPathValue, catalogLocal);

                    // If under StreamingAssets, rewrite catalog to JSON-escaped dynamic tokenized paths AND recompute/replace .hash
                    if (!string.IsNullOrEmpty(catalogLocal))
                    {
                        string physicalPrefixFwd = (serverData + "/").Replace("\\", "/");
                        string physicalPrefixBwd = (serverData + "\\").Replace("/", "\\");

                        string dynamicPerPackRaw = loadPathValue.Replace("/", "\\").TrimEnd('\\') + "\\";

                        string dynamicPerPackJson = dynamicPerPackRaw.Replace("\\", "\\\\");

                        string json = File.ReadAllText(catalogLocal, Encoding.UTF8);

                        // Addressables can emit its generated MonoScripts bundle into
                        // Library/com.unity.addressables even though all selected content
                        // groups and the catalog build into the pack folder. The catalog
                        // still records that Library path as a required prefab dependency.
                        // Ship the dependency with the pack and make its ID portable before
                        // the creator project's Library cache disappears.
                        json = CopyMonoScriptDependenciesIntoPackAndRebase(
                            json,
                            packBuildFolder,
                            dynamicPerPackJson);

                        json = json.Replace(physicalPrefixFwd, dynamicPerPackJson);
                        json = json.Replace(physicalPrefixBwd, dynamicPerPackJson);

                        File.WriteAllText(catalogLocal, json, Encoding.UTF8);

                        // recompute hash
                        string hashPath = Path.Combine(
                            Path.GetDirectoryName(catalogLocal),
                            Path.GetFileNameWithoutExtension(catalogLocal) + ".hash"
                        );

                        try
                        {
                            string hash = ComputeMD5(json);
                            File.WriteAllText(hashPath, hash, Encoding.UTF8);
                        }
                        catch
                        {
                            if (File.Exists(hashPath)) File.Delete(hashPath);
                        }

                        Debug.Log($"Pack '{def.PackName}' built.\nCatalog: {catalogLocal}");
                    }

                }
            }
            finally
            {
                RestoreScooterDeckProxyMaterials(strippedProxyMaterials);

                // Restore groups & IncludeInBuild
                foreach (var t in tracked)
                {
                    if (!string.IsNullOrEmpty(t.origBuildVarId))
                        t.schema.BuildPath.SetVariableById(settings, t.origBuildVarId);
                    if (!string.IsNullOrEmpty(t.origLoadVarId))
                        t.schema.LoadPath.SetVariableById(settings, t.origLoadVarId);
                    t.schema.IncludeInBuild = t.includeInBuild;
                    EditorUtility.SetDirty(t.group);
                }

                // Restore per-pack profile values (by NAME)
                foreach (var b in backups)
                    prof.SetValue(opts.profileId, b.varId, b.previousValue);

                // Restore Remote Catalog variable bindings
                if (settings.RemoteCatalogBuildPath != null && !string.IsNullOrEmpty(prevRemoteCatalogBuildVarId))
                    settings.RemoteCatalogBuildPath.SetVariableById(settings, prevRemoteCatalogBuildVarId);
                if (settings.RemoteCatalogLoadPath != null && !string.IsNullOrEmpty(prevRemoteCatalogLoadVarId))
                    settings.RemoteCatalogLoadPath.SetVariableById(settings, prevRemoteCatalogLoadVarId);

                EditorBuildSettings.scenes = originalScenes;
                
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }
        

        // =========================
        // Helpers
        // =========================

        private static void EnsureJsonCatalogEnabled(AddressableAssetSettings settings)
        {
            // EnableJsonCatalog was added in Addressables 2.x. Addressables 1.x already
            // emits JSON and remains the minimum package version declared by this SDK.
            var property = settings.GetType().GetProperty("EnableJsonCatalog");
            if (property == null || property.PropertyType != typeof(bool) || !property.CanWrite)
                return;

            bool changedSetting = !(bool)property.GetValue(settings);
            if (changedSetting)
            {
                property.SetValue(settings, true);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            bool changedDefine = EnsureJsonCatalogScriptingDefine();

            if (changedSetting || changedDefine)
            {
                Debug.Log(
                    "[AddressablesPackBuilder] Enabled JSON catalog output for MashBox content packs." +
                    (changedDefine ? " Unity will recompile scripts to activate the JSON catalog serializer." : ""));
            }
        }

        private static bool EnsureJsonCatalogScriptingDefine()
        {
            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            if (buildTargetGroup == BuildTargetGroup.Unknown)
                return false;

#pragma warning disable 618
            string symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup);
            var definedSymbols = symbols
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(symbol => symbol.Trim())
                .Where(symbol => !string.IsNullOrEmpty(symbol))
                .ToList();

            if (definedSymbols.Contains(JsonCatalogScriptingDefine, StringComparer.Ordinal))
                return false;

            definedSymbols.Add(JsonCatalogScriptingDefine);
            PlayerSettings.SetScriptingDefineSymbolsForGroup(
                buildTargetGroup,
                string.Join(";", definedSymbols));
#pragma warning restore 618

            return true;
        }

        private static bool IsJsonCatalogSerializerActive(AddressableAssetSettings settings)
        {
            if (settings == null)
                return false;

            // This value is compiled inside the Addressables editor assembly and therefore
            // tells us what serializer the next build will actually use.
            var input = new AddressablesDataBuilderInput(settings);
            return string.Equals(
                Path.GetExtension(input.RuntimeCatalogFilename),
                ".json",
                StringComparison.OrdinalIgnoreCase);
        }

        private static void QueueBuildAfterJsonCatalogRecompile(ContentPackDefinition def, BuildOptions opts)
        {
            string assetPath = AssetDatabase.GetAssetPath(def);
            string definitionGuid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(definitionGuid))
            {
                throw new InvalidOperationException(
                    $"Pack '{def.PackName}' cannot be resumed after the JSON catalog recompile because its definition is not a saved asset.");
            }

            var queue = LoadPendingBuildQueue();
            queue.requests.RemoveAll(request => request == null || request.definitionGuid == definitionGuid);
            queue.requests.Add(new PendingPackBuildRequest
            {
                definitionGuid = definitionGuid,
                options = opts
            });

            SessionState.SetString(PendingBuildQueueKey, JsonUtility.ToJson(queue));
            CompilationPipeline.RequestScriptCompilation();
            Debug.Log(
                $"[AddressablesPackBuilder] Pack '{def.PackName}' is queued. Unity is recompiling the JSON catalog serializer; the build will resume automatically.");
        }

        private static PendingPackBuildQueue LoadPendingBuildQueue()
        {
            string json = SessionState.GetString(PendingBuildQueueKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return new PendingPackBuildQueue();

            var queue = JsonUtility.FromJson<PendingPackBuildQueue>(json);
            return queue ?? new PendingPackBuildQueue();
        }

        private static void ResumePendingBuildsIfReady(AddressableAssetSettings settings)
        {
            string json = SessionState.GetString(PendingBuildQueueKey, string.Empty);
            if (string.IsNullOrEmpty(json) || settings == null || EditorApplication.isCompiling)
                return;

            if (!IsJsonCatalogSerializerActive(settings))
                return;

            var queue = LoadPendingBuildQueue();
            SessionState.EraseString(PendingBuildQueueKey);

            foreach (var request in queue.requests)
            {
                if (request == null || string.IsNullOrEmpty(request.definitionGuid))
                    continue;

                string assetPath = AssetDatabase.GUIDToAssetPath(request.definitionGuid);
                var definition = AssetDatabase.LoadAssetAtPath<ContentPackDefinition>(assetPath);
                if (definition == null)
                {
                    Debug.LogError(
                        $"[AddressablesPackBuilder] Could not resume queued build; content pack GUID '{request.definitionGuid}' no longer exists.");
                    continue;
                }

                Debug.Log($"[AddressablesPackBuilder] JSON catalog serializer is active. Resuming '{definition.PackName}'.");
                BuildPack(definition, request.options);
            }
        }

        private static void RemoveCoreBundlesFromOutput(string folder, string coreGroupName)
        {
            if (!Directory.Exists(folder)) return;

            var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                var lower = file.ToLower();

                // Match core bundle files
                if (lower.Contains(coreGroupName.ToLower()) && 
                    (lower.EndsWith(".bundle") || lower.EndsWith(".manifest")))
                {
                    try
                    {
                        File.Delete(file);
                        Debug.Log($"[Addressables] Removed core bundle from pack: {file}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Failed to delete core bundle: {file}\n{e}");
                    }
                }
            }
        }

        private static List<ProxyMaterialState> StripScooterDeckProxyMaterials(ContentPackDefinition def)
        {
            var states = new List<ProxyMaterialState>();
            if (def == null || def.IsCorePack || def._items == null)
                return states;

            var rootsToSave = new HashSet<GameObject>();

            foreach (var item in def._items)
            {
                if (item == null)
                    continue;

                foreach (var renderer in item.GetComponentsInChildren<Renderer>(true))
                {
                    if (!ContentPackValidator.IsScooterDeckGriptapeProxy(item, renderer))
                        continue;

                    var originalMaterials = renderer.sharedMaterials;
                    if (originalMaterials == null || originalMaterials.All(material => material == null))
                        continue;

                    states.Add(new ProxyMaterialState
                    {
                        prefabRoot = item,
                        renderer = renderer,
                        originalMaterials = originalMaterials
                    });

                    renderer.sharedMaterials = new Material[originalMaterials.Length];
                    EditorUtility.SetDirty(renderer);
                    EditorUtility.SetDirty(item);
                    rootsToSave.Add(item);
                }
            }

            SavePrefabRoots(rootsToSave);

            if (states.Count > 0)
                Debug.Log($"[Addressables] Stripped {states.Count} scooter deck griptape proxy material reference(s) before build.");

            return states;
        }

        private static void RestoreScooterDeckProxyMaterials(List<ProxyMaterialState> states)
        {
            if (states == null || states.Count == 0)
                return;

            var rootsToSave = new HashSet<GameObject>();

            foreach (var state in states)
            {
                if (state == null || state.renderer == null)
                    continue;

                state.renderer.sharedMaterials = state.originalMaterials;
                EditorUtility.SetDirty(state.renderer);

                if (state.prefabRoot != null)
                {
                    EditorUtility.SetDirty(state.prefabRoot);
                    rootsToSave.Add(state.prefabRoot);
                }
            }

            SavePrefabRoots(rootsToSave);
            Debug.Log($"[Addressables] Restored {states.Count} scooter deck griptape proxy material reference(s) after build.");
        }

        private static void SavePrefabRoots(IEnumerable<GameObject> prefabRoots)
        {
            var savedAny = false;

            foreach (var prefabRoot in prefabRoots.Where(root => root != null))
            {
                PrefabUtility.SavePrefabAsset(prefabRoot);
                savedAny = true;
            }

            if (!savedAny)
                return;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        
        private static string EnsureProfileVar(AddressableAssetProfileSettings prof, string varName, string defaultValue)
        {
            // Work purely by NAME (portable across Unity versions)
            if (prof == null) return varName;

            // If the variable doesn't exist, create it with a default value.
            // GetVariableNames() is public; no internal API needed.
            bool exists = prof.GetVariableNames().Contains(varName);
            if (!exists)
            {
                prof.CreateValue(varName, defaultValue);
            }

            return varName; // callers use the NAME everywhere
        }


        private static string ToFileURL(string absoluteFolder)
        {
            absoluteFolder = absoluteFolder.Replace("\\", "/");
            if (!absoluteFolder.StartsWith("file://"))
                return "file:///" + absoluteFolder.TrimStart('/');
            return absoluteFolder;
        }

        private static void SimplifyAddressesForSelectedGroups(AddressableAssetSettings settings, IEnumerable<AddressableAssetGroup> groups)
        {
            var used = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var g in groups)
            {
                bool anyChanged = false;
                foreach (var e in g.entries.ToList())
                {
                    if (e == null || e.TargetAsset == null) continue;

                    var path = AssetDatabase.GetAssetPath(e.TargetAsset);
                    var filename = Path.GetFileNameWithoutExtension(path);
                    var candidate = filename;
                    int i = 1;
                    while (used.Contains(candidate))
                        candidate = filename + "_" + i++;

                    if (e.address != candidate)
                    {
                        e.SetAddress(candidate);
                        anyChanged = true;
                    }
                    used.Add(candidate);
                }

                if (anyChanged)
                    EditorUtility.SetDirty(g);
            }
        }

        private static bool TryGetRelativeUnderStreamingAssets(string absolute, out string relAfterSA)
        {
            relAfterSA = null;
            if (string.IsNullOrEmpty(absolute))
                return false;

            string sa = Application.streamingAssetsPath.Replace("\\", "/");
            string abs = absolute.Replace("\\", "/");

            if (abs.StartsWith(sa))
            {
                relAfterSA = abs.Substring(sa.Length).TrimStart('/');
                return true;
            }
            return false;
        }

        private static string GetStreamingAssetsTokenJson()
        {
            // Raw token string; we JSON-escape backslashes later when needed.
            return "{Application.streamingAssetsPath}";
        }

        private static string GuessCatalogRemoteUrl(string loadPathValue, string catalogJsonPath)
        {
            if (string.IsNullOrEmpty(loadPathValue) || string.IsNullOrEmpty(catalogJsonPath))
                return null;

            var folder = Path.GetDirectoryName(catalogJsonPath)?.Replace("\\", "/") ?? "";
            return ToFileURL(folder);
        }

        private static string ComputeMD5(string text)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                var hashBytes = md5.ComputeHash(bytes);
                var sb = new StringBuilder(hashBytes.Length * 2);
                foreach (var b in hashBytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static string CopyMonoScriptDependenciesIntoPackAndRebase(
            string catalogJson,
            string packBuildFolder,
            string dynamicPerPackJsonPrefix)
        {
            if (string.IsNullOrEmpty(catalogJson) ||
                catalogJson.IndexOf("_monoscripts_", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return catalogJson;
            }

            const string bundleStringPattern = "\"((?:\\\\.|[^\"])*?_monoscripts_(?:\\\\.|[^\"])*?\\.bundle)\"";
            var copiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return Regex.Replace(
                catalogJson,
                bundleStringPattern,
                match =>
                {
                    string escapedId = match.Groups[1].Value;
                    string internalId = escapedId
                        .Replace("\\\\", "\\")
                        .Replace("\\/", "/");
                    string fileName = Path.GetFileName(internalId.Replace('\\', '/'));
                    if (string.IsNullOrWhiteSpace(fileName))
                        return match.Value;

                    string destinationPath = Path.Combine(packBuildFolder, fileName);
                    if (!File.Exists(destinationPath))
                    {
                        string sourcePath = ResolveGeneratedBundleSourcePath(internalId, fileName, packBuildFolder);
                        if (string.IsNullOrEmpty(sourcePath))
                        {
                            throw new FileNotFoundException(
                                $"Addressables catalog references generated MonoScripts bundle '{fileName}', but the SDK " +
                                "could not locate it in the pack output or the project's Addressables build outputs. " +
                                "The content pack would load icons but fail when its prefab is previewed.",
                                internalId);
                        }

                        Directory.CreateDirectory(packBuildFolder);
                        File.Copy(sourcePath, destinationPath, true);
                    }

                    if (copiedFiles.Add(fileName))
                        Debug.Log($"[Addressables] Included MonoScripts dependency: {destinationPath}");

                    return $"\"{dynamicPerPackJsonPrefix}{fileName}\"";
                },
                RegexOptions.IgnoreCase);
        }

        private static string ResolveGeneratedBundleSourcePath(
            string internalId,
            string fileName,
            string packBuildFolder)
        {
            string directPath = internalId;
            if (Uri.TryCreate(internalId, UriKind.Absolute, out Uri uri) && uri.IsFile)
                directPath = uri.LocalPath;

            // Runtime tokens such as {Application.streamingAssetsPath} are load IDs, not
            // physical build paths. Only test the catalog ID directly when it is rooted.
            if (Path.IsPathRooted(directPath) && File.Exists(directPath))
                return directPath;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var searchRoots = new[]
            {
                packBuildFolder,
                Path.Combine(projectRoot, "Content"),
                Path.Combine(projectRoot, "ServerData"),
                Path.Combine(projectRoot, "Library", "com.unity.addressables")
            };

            var candidates = new List<FileInfo>();
            foreach (string root in searchRoots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                    continue;

                try
                {
                    candidates.AddRange(
                        Directory.GetFiles(root, fileName, SearchOption.AllDirectories)
                            .Select(path => new FileInfo(path)));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Addressables] Could not search generated bundle folder '{root}': {exception.Message}");
                }
            }

            return candidates
                .Where(candidate => candidate.Exists)
                .OrderByDescending(candidate => candidate.LastWriteTimeUtc)
                .Select(candidate => candidate.FullName)
                .FirstOrDefault();
        }
    }
}

#endif
