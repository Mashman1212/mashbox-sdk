#if UNITY_EDITOR

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.Exporting
{
    public class AssetBundleExporter : EditorWindow
    {
        private string outputPath = "AssetBundles/";
        private const string OutputPathKey = "AssetBundleExporter_OutputPath";
        private const string CompressionModeKey = "AssetBundleExporter_CompressionMode";
        private const string MapFolderSuffix = "_map";

        private BuildTarget buildTarget = BuildTarget.StandaloneWindows64;

        private enum CompressionMode
        {
            LZMACompression,
            ChunkBasedCompression,
            None
        }

        private CompressionMode compressionMode = CompressionMode.LZMACompression;
        
        private void OnGUI()
        {
            GUILayout.Label("AssetBundle Export Settings", EditorStyles.boldLabel);

            GUILayout.Label("Output Path:");
            outputPath = EditorGUILayout.TextField(outputPath);

            if (GUILayout.Button("Choose Folder"))
            {
                string selectedPath = EditorUtility.OpenFolderPanel("Select Output Folder", outputPath, "");
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    outputPath = selectedPath;
                }
            }

            GUILayout.Label("Compression Mode:");
            compressionMode = (CompressionMode)EditorGUILayout.EnumPopup(compressionMode);

            GUILayout.Space(10);

            if (GUILayout.Button("Build AssetBundles"))
            {
                BuildBundles();
            }
            if (GUILayout.Button("Generate Manifests"))
            {
                GenerateManifest();
            }

            if (GUILayout.Button("Print All Asset Bundle Names"))
            {
                string[] bundleNames = AssetDatabase.GetAllAssetBundleNames();
                foreach (var name in bundleNames)
                {
                    Debug.Log("[AssetBundle] " + name);
                }

                if (bundleNames.Length == 0)
                {
                    Debug.Log("[AssetBundle] No asset bundles found.");
                }
            }
        }
        private BuildTarget GetEffectiveBuildTarget()
        {
            BuildTarget active = EditorUserBuildSettings.activeBuildTarget;

            // Force all Windows builds to 64-bit
            if (active == BuildTarget.StandaloneWindows || 
                active == BuildTarget.StandaloneWindows64)
            {
                return BuildTarget.StandaloneWindows64;
            }

            // Any non-windows platform uses the active target
            return active;
        }
        private void BuildBundles()
        {
            buildTarget = GetEffectiveBuildTarget();
            string buildTargetName = buildTarget.ToString();
            string platformSpecificOutputPath = Path.Combine(outputPath, buildTargetName);

            if (!Directory.Exists(platformSpecificOutputPath))
            {
                Directory.CreateDirectory(platformSpecificOutputPath);
            }
            else
            {
                // Clean only root-level loose files (avoid deleting subfolders like applewood forest_map)
                var files = Directory.GetFiles(platformSpecificOutputPath);
                foreach (var file in files)
                {
                    if (file.EndsWith(".bundle") || file.EndsWith(".manifest"))
                    {
                        File.Delete(file);
                    }
                }
            }

            BuildAssetBundleOptions options = BuildAssetBundleOptions.None;

            switch (compressionMode)
            {
                case CompressionMode.LZMACompression:
                    options = BuildAssetBundleOptions.None;
                    break;
                case CompressionMode.ChunkBasedCompression:
                    options = BuildAssetBundleOptions.ChunkBasedCompression;
                    break;
                case CompressionMode.None:
                    options = BuildAssetBundleOptions.UncompressedAssetBundle;
                    break;
            }

            Debug.Log($"[AssetBundleExporter] Building to {platformSpecificOutputPath} for {buildTarget} using {options}");

            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(platformSpecificOutputPath, options, buildTarget);

            
            // Rename bundle files to have .bundle extension
            foreach (string bundleName in manifest.GetAllAssetBundles())
            {
                string oldPath = Path.Combine(platformSpecificOutputPath, bundleName);
                string newPath = Path.Combine(platformSpecificOutputPath, bundleName + ".bundle");

                if (File.Exists(oldPath))
                {
                    if (File.Exists(newPath)) File.Delete(newPath);
                    File.Move(oldPath, newPath);
                    Debug.Log($"Renamed {bundleName} â†’ {bundleName}.bundle");
                }
            }

            GenerateManifest(); // expects .bundle files to exist now

            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("AssetBundles Built", "AssetBundles and manifest created at:\n" + platformSpecificOutputPath, "OK");
        }

        private void GenerateManifest()
        {
            var db = MapBundleDatabase.GetOrCreate();
            var includedBundles = new HashSet<string>();

            foreach (var entry in db.entries)
            {
                if (entry.includeInBuild && !string.IsNullOrEmpty(entry.bundleName))
                {
                    includedBundles.Add(entry.bundleName);
                }
            }
            
            string buildTargetName = buildTarget.ToString();
            string rootPath = Path.Combine(outputPath, buildTargetName);

            string versionLogPath = Path.Combine(rootPath, "version_log.json");

            VersionLog versionLog = new VersionLog();
            if (File.Exists(versionLogPath))
            {
                try
                {
                    string json = File.ReadAllText(versionLogPath);
                    versionLog = JsonUtility.FromJson<VersionLog>(json);
                    if (versionLog.versions == null)
                        versionLog.versions = new Dictionary<string, int>();
                }
                catch
                {
                    versionLog.versions = new Dictionary<string, int>();
                }
            }
            else
            {
                versionLog.versions = new Dictionary<string, int>();
            }

            var manifest = new MapManifestWrapper { maps = new Dictionary<string, MapInfo>() };

            string[] bundleNames = AssetDatabase.GetAllAssetBundleNames();
            foreach (var bundleName in bundleNames)
            {
                Debug.Log("Generating for bundle: " + bundleName);

                string bundleFileName = bundleName + ".bundle";
                string bundlePath = Path.Combine(rootPath, bundleFileName);

                if (File.Exists(bundlePath))
                {
                    if (!versionLog.versions.ContainsKey(bundleName))
                        versionLog.versions[bundleName] = 1;

                    int version = versionLog.versions[bundleName];
                    long size = new FileInfo(bundlePath).Length;

                    manifest.maps[bundleName] = new MapInfo
                    {
                        version = version,
                        filename = bundleFileName,
                        size = size
                    };
                }

                // Use unique folder to avoid collision with bundle files
                string bundleFolderName = bundleName + MapFolderSuffix;
                string bundleFolder = Path.Combine(rootPath, bundleFolderName);
                if (!Directory.Exists(bundleFolder))
                    Directory.CreateDirectory(bundleFolder);

                // Move bundle into subfolder
                if (File.Exists(bundlePath))
                {
                    string newBundlePath = Path.Combine(bundleFolder, bundleFileName);
                    SafeMove(bundlePath, newBundlePath);
                }

                // Move .manifest file into subfolder
                string manifestPath = Path.Combine(rootPath, bundleName + ".manifest");
                if (File.Exists(manifestPath))
                {
                    string newManifestPath = Path.Combine(bundleFolder, bundleName + ".manifest");
                    SafeMove(manifestPath, newManifestPath);
                }

                //if (includedBundles.Contains(bundleName))
                {
                    
                    CopyScreenshotForMap(bundleName, bundleFolder);

                    var challengeData = ExtractChallengeDataFromScenes(new List<string> { bundleName });
                    if (challengeData.Count > 0)
                    {
                        string challengeJson = JsonUtility.ToJson(challengeData[0], true);
                        File.WriteAllText(
                            Path.Combine(bundleFolder, $"{bundleName}_Challenges.json"),
                            challengeJson
                        );
                    }
                }
                //else
                //{
                //    Debug.Log($"[AssetBundleExporter] Skipping extras for excluded map: {bundleName}");
                //}

            }

            File.WriteAllText(Path.Combine(rootPath, "manifest.json"), JsonUtility.ToJson(manifest, true));
            File.WriteAllText(versionLogPath, JsonUtility.ToJson(versionLog, true));

            Debug.Log("[AssetBundleExporter] Bundles organized into subfolders with screenshots and challenge manifests.");
        }

        private void CopyScreenshotForMap(string mapName, string destinationDirectory)
        {
            string[] sceneGuids = AssetDatabase.FindAssets($"{mapName} t:Scene");
            foreach (string guid in sceneGuids)
            {
                string scenePath = AssetDatabase.GUIDToAssetPath(guid);
                string sceneDir = Path.GetDirectoryName(scenePath);
                string screenshotPath = Path.Combine(sceneDir, mapName + ".png");

                if (File.Exists(screenshotPath))
                {
                    string destPath = Path.Combine(destinationDirectory, mapName + ".png");
                    File.Copy(screenshotPath, destPath, true);
                    Debug.Log($"[AssetBundleExporter] Copied screenshot for '{mapName}' to build output.");
                }
                else
                {
                    Debug.LogWarning($"[AssetBundleExporter] Screenshot not found for '{mapName}' at expected path: {screenshotPath}");
                }

                return;
            }

            Debug.LogWarning($"[AssetBundleExporter] Could not find scene asset for '{mapName}' to locate screenshot.");
        }

        private List<ChallengeMapData> ExtractChallengeDataFromScenes(List<string> mapNames)
        {
            var result = new List<ChallengeMapData>();

            foreach (var mapName in mapNames)
            {
                string[] sceneGuids = AssetDatabase.FindAssets($"{mapName} t:Scene");
                if (sceneGuids.Length == 0)
                {
                    Debug.LogWarning($"[AssetBundleExporter] No scene found for map: {mapName}");
                    continue;
                }

                string scenePath = AssetDatabase.GUIDToAssetPath(sceneGuids[0]);
                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                GameObject challengesRoot = GameObject.Find("Challenges");
                if (challengesRoot == null)
                {
                    Debug.LogWarning($"[AssetBundleExporter] 'Challenges' GameObject not found in scene {mapName}");
                    continue;
                }

                var mapData = new ChallengeMapData
                {
                    mapName = mapName,
                    categories = new List<ChallengeCategory>(),
                    tasks = ExtractMapTaskData()
                };

                foreach (Transform category in challengesRoot.transform)
                {
                    var challengeNames = new List<string>();
                    foreach (Transform challenge in category)
                    {
                        challengeNames.Add(challenge.name);
                    }

                    mapData.categories.Add(new ChallengeCategory
                    {
                        categoryName = category.name,
                        items = challengeNames
                    });
                }

                result.Add(mapData);
            }

            return result;
        }

        private static List<MapTaskData> ExtractMapTaskData()
        {
            var result = new List<MapTaskData>();
            Component taskList = FindMapTaskListInActiveScene();
            if (taskList == null)
                return result;

            SerializedObject serializedTaskList = new SerializedObject(taskList);
            SerializedProperty tasksProperty = serializedTaskList.FindProperty("tasks");
            if (tasksProperty == null || !tasksProperty.isArray)
                return result;

            for (int i = 0; i < tasksProperty.arraySize; i++)
            {
                SerializedProperty taskProperty = tasksProperty.GetArrayElementAtIndex(i);
                SerializedProperty enabledProperty = taskProperty.FindPropertyRelative("enabled");
                if (enabledProperty != null && !enabledProperty.boolValue)
                    continue;

                string taskType = GetEnumPropertyName(taskProperty.FindPropertyRelative("taskType"));
                string displayName = GetStringProperty(taskProperty, "displayName");
                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = ObjectNames.NicifyVariableName(taskType);

                result.Add(new MapTaskData
                {
                    taskType = taskType,
                    displayName = displayName,
                    verb = GetStringProperty(taskProperty, "verb"),
                    preposition = GetStringProperty(taskProperty, "preposition"),
                    adjective = GetStringProperty(taskProperty, "adjective"),
                    targetValue = GetFloatProperty(taskProperty, "targetValue"),
                    targetCount = Mathf.Max(1, GetIntProperty(taskProperty, "targetCount", 1))
                });
            }

            return result;
        }

        private static Component FindMapTaskListInActiveScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
                return null;

            GameObject[] roots = activeScene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                MonoBehaviour[] behaviours = roots[i].GetComponentsInChildren<MonoBehaviour>(true);
                for (int j = 0; j < behaviours.Length; j++)
                {
                    MonoBehaviour behaviour = behaviours[j];
                    if (behaviour != null && behaviour.GetType().FullName == "MashBoxSDK.Maps.MBMapTaskList")
                        return behaviour;
                }
            }

            return null;
        }

        private static string GetEnumPropertyName(SerializedProperty property)
        {
            if (property == null)
                return string.Empty;

            string[] names = property.enumNames;
            int index = property.enumValueIndex;
            if (names != null && index >= 0 && index < names.Length)
                return names[index];

            return property.enumDisplayNames != null && index >= 0 && index < property.enumDisplayNames.Length
                ? property.enumDisplayNames[index]
                : string.Empty;
        }

        private static string GetStringProperty(SerializedProperty property, string relativeName)
        {
            SerializedProperty relativeProperty = property.FindPropertyRelative(relativeName);
            return relativeProperty == null ? string.Empty : relativeProperty.stringValue;
        }

        private static float GetFloatProperty(SerializedProperty property, string relativeName)
        {
            SerializedProperty relativeProperty = property.FindPropertyRelative(relativeName);
            return relativeProperty == null ? 0.0f : relativeProperty.floatValue;
        }

        private static int GetIntProperty(SerializedProperty property, string relativeName, int fallback)
        {
            SerializedProperty relativeProperty = property.FindPropertyRelative(relativeName);
            return relativeProperty == null ? fallback : relativeProperty.intValue;
        }

        private void SafeMove(string source, string destination)
        {
            if (!File.Exists(source))
            {
                Debug.LogWarning($"[AssetBundleExporter] Source file does not exist: {source}");
                return;
            }

            try
            {
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                File.Move(source, destination);
                Debug.Log($"[AssetBundleExporter] Moved: {source} â†’ {destination}");
            }
            catch (IOException ex)
            {
                Debug.LogError($"[AssetBundleExporter] Failed to move file from {source} to {destination}: {ex.Message}");
            }
        }

        [System.Serializable]
        public class MapInfo
        {
            public int version;
            public string filename;
            public long size;
        }

        [System.Serializable]
        public class MapManifestWrapper
        {
            public Dictionary<string, MapInfo> maps;
        }

        [System.Serializable]
        public class VersionLog
        {
            public Dictionary<string, int> versions;
        }

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
        
        public static void RunBuildFromExternalTool(string outputPath)
        {
            AssetBundleExporter window = CreateInstance<AssetBundleExporter>();

            // Load prefs like OnEnable normally does
            window.outputPath = outputPath;//EditorPrefs.GetString(OutputPathKey, "AssetBundles/");
            window.compressionMode = (CompressionMode)EditorPrefs.GetInt(CompressionModeKey, (int)CompressionMode.ChunkBasedCompression);
            window.buildTarget = EditorUserBuildSettings.activeBuildTarget;

            window.BuildBundles();
        }
    }
}

#endif

