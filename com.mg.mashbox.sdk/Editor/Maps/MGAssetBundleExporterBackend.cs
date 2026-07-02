#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using MashBoxSDK.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    public static class MGAssetBundleExporterBackend
    {
        private const string MapFolderSuffix = "_map";

        // ===========================
        //   PUBLIC API CALLED BY UI
        // ===========================
        public static void BuildSelectedBundles(string outputPath, BuildTarget buildTarget, BuildAssetBundleOptions options)
        {
            
            
            // Ensure root folder exists
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            // Normalize path for Unity
            outputPath = outputPath.Replace("\\", "/");

            string platform = buildTarget.ToString();
            string platformOutput = Path.Combine(outputPath, platform);
            platformOutput = platformOutput.Replace("\\", "/");

            if (!Directory.Exists(platformOutput))
                Directory.CreateDirectory(platformOutput);

            Debug.Log($"[MGMapTools] Building bundles to: {platformOutput}");

            AssetBundleManifest manifest =
                BuildPipeline.BuildAssetBundles(platformOutput, options, buildTarget);

            if (manifest == null)
            {
                Debug.LogError("[MGMapTools] ERROR: BuildPipeline.BuildAssetBundles returned NULL.");
                Debug.LogError("[MGMapTools] UNITY FAILED TO BUILD BUNDLES. Dumping debug info...");

                Debug.LogError($"outputPath = {outputPath}");
                Debug.LogError($"platformOutput = {platformOutput}");
                Debug.LogError($"buildTarget = {buildTarget}");
                Debug.LogError($"options = {options}");

                Debug.LogError("Checking for assigned bundle names...");
                foreach (var name in AssetDatabase.GetAllAssetBundleNames())
                    Debug.LogError(" - bundleName: " + name);

                Debug.LogError("If list above is EMPTY => you have no assigned bundle names.");
                Debug.LogError("If path is outside project and Unity cannot write to it => fail.");
                Debug.LogError("If any scene is missing => fail.");
                Debug.LogError("If output folder does not exist or is locked => fail.");

                return; // prevent crash
            }
            
            foreach (string bundleName in manifest.GetAllAssetBundles())
            {
                string original = Path.Combine(platformOutput, bundleName);
                string renamed = Path.Combine(platformOutput, bundleName + ".bundle");

                if (File.Exists(original))
                {
                    if (File.Exists(renamed))
                        File.Delete(renamed);

                    File.Move(original, renamed);
                    Debug.Log($"Renamed {bundleName} → {bundleName}.bundle");
                }
            }

            GenerateManifests(outputPath, buildTarget);
            AssetDatabase.Refresh();
        }


        // ===========================
        //   MANIFEST GENERATION
        // ===========================

        public static void GenerateManifests(string root, BuildTarget target)
        {
            string platform = target.ToString();
            string rootPath = Path.Combine(root, platform);

            string versionLogPath = Path.Combine(rootPath, "version_log.json");

            VersionLog versionLog = LoadVersionLog(versionLogPath);

            var manifest = new MapManifestWrapper { maps = new Dictionary<string, MapInfo>() };

            string[] bundleNames = AssetDatabase.GetAllAssetBundleNames();

            foreach (var bundleName in bundleNames)
            {
                string bundleFile = $"{bundleName}.bundle";
                string bundlePath = Path.Combine(rootPath, bundleFile);

                if (File.Exists(bundlePath))
                {
                    if (!versionLog.versions.ContainsKey(bundleName))
                        versionLog.versions[bundleName] = 1;

                    long size = new FileInfo(bundlePath).Length;

                    manifest.maps[bundleName] = new MapInfo
                    {
                        version = versionLog.versions[bundleName],
                        filename = bundleFile,
                        size = size
                    };
                }

                string folder = Path.Combine(rootPath, bundleName + MapFolderSuffix);
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                SafeMove(bundlePath, Path.Combine(folder, bundleFile));
                SafeMove(Path.Combine(rootPath, $"{bundleName}.manifest"),
                         Path.Combine(folder, $"{bundleName}.manifest"));

                CopyScreenshot(bundleName, folder);
                WriteChallengeData(bundleName, folder);
            }

            File.WriteAllText(Path.Combine(rootPath, "manifest.json"),
                JsonUtility.ToJson(manifest, true));

            File.WriteAllText(versionLogPath, JsonUtility.ToJson(versionLog, true));

            Debug.Log("[MGMapTools] Manifest generated.");
        }


        private static VersionLog LoadVersionLog(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    var data = JsonUtility.FromJson<VersionLog>(File.ReadAllText(path));
                    if (data.versions == null)
                        data.versions = new Dictionary<string, int>();
                    return data;
                }
                catch
                {
                    return new VersionLog { versions = new Dictionary<string, int>() };
                }
            }

            return new VersionLog { versions = new Dictionary<string, int>() };
        }


        // =======================================================
        //   SCREENSHOT / CHALLENGE GENERATION (unchanged logic)
        // =======================================================

        private static void CopyScreenshot(string mapName, string dest)
        {
            string[] guids = AssetDatabase.FindAssets($"{mapName} t:Scene");
            if (guids.Length == 0) return;

            string scenePath = AssetDatabase.GUIDToAssetPath(guids[0]);
            string dir = Path.GetDirectoryName(scenePath);

            string screenshot = Path.Combine(dir, mapName + ".png");
            if (!File.Exists(screenshot)) return;

            File.Copy(screenshot, Path.Combine(dest, mapName + ".png"), true);
        }

        private static void WriteChallengeData(string mapName, string dest)
        {
            string[] guids = AssetDatabase.FindAssets($"{mapName} t:Scene");
            if (guids.Length == 0) return;

            string scenePath = AssetDatabase.GUIDToAssetPath(guids[0]);
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            GameObject root = GameObject.Find("Challenges");
            if (root == null) return;

            var mapData = new ChallengeMapData
            {
                mapName = mapName,
                categories = new List<ChallengeCategory>(),
                tasks = ExtractMapTaskData()
            };

            foreach (Transform cat in root.transform)
            {
                var list = new List<string>();
                foreach (Transform c in cat)
                    list.Add(c.name);

                mapData.categories.Add(new ChallengeCategory
                {
                    categoryName = cat.name,
                    items = list
                });
            }

            File.WriteAllText(Path.Combine(dest, $"{mapName}_Challenges.json"),
                JsonUtility.ToJson(mapData, true));
        }

        private static List<MapTaskData> ExtractMapTaskData()
        {
            var result = new List<MapTaskData>();
            MBMapTaskList taskList = MBMapTaskList.FindInScene(SceneManager.GetActiveScene());
            if (taskList == null || taskList.Tasks == null)
                return result;

            for (int i = 0; i < taskList.Tasks.Count; i++)
            {
                MBMapTaskDefinition task = taskList.Tasks[i];
                if (task == null || !task.enabled)
                    continue;

                result.Add(new MapTaskData
                {
                    taskType = task.taskType.ToString(),
                    displayName = task.DisplayNameOrFallback,
                    verb = task.verb,
                    preposition = task.preposition,
                    adjective = task.adjective,
                    targetValue = task.targetValue,
                    targetCount = Mathf.Max(1, task.targetCount)
                });
            }

            return result;
        }


        public static void SafeMove(string source, string dest)
        {
            if (!File.Exists(source)) return;

            if (File.Exists(dest))
                File.Delete(dest);

            File.Move(source, dest);
        }


        // ===========================
        //   DATA STRUCTURES
        // ===========================

        [System.Serializable]
        public class MapInfo { public int version; public string filename; public long size; }

        [System.Serializable]
        public class MapManifestWrapper { [System.NonSerialized] public Dictionary<string, MapInfo> maps; }

        [System.Serializable]
        public class VersionLog { [System.NonSerialized] public Dictionary<string, int> versions; }

        [System.Serializable]
        public class ChallengeMapData
        {
            public string mapName;
            public List<ChallengeCategory> categories;
            public List<MapTaskData> tasks;
        }

        [System.Serializable]
        public class ChallengeCategory
        {
            public string categoryName;
            public List<string> items;
        }

        [System.Serializable]
        public class MapTaskData
        {
            public string taskType;
            public string displayName;
            public string verb;
            public string preposition;
            public string adjective;
            public float targetValue;
            public int targetCount;
        }
    }
}


#endif
