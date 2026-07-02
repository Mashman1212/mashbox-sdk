#if UNITY_EDITOR

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ContentTools.PhotoBooth.Editor
{
    public class PhotoBoothWindow : EditorWindow
    {
        private const string PhotoBoothSceneName = "PhotoBooth Scene";
        private const string PhotoBoothPrefabName = "Photo Booth";
        private const string PhotoBoothRootName = "PhotoBoothRoot";
        private const string CaptureLocationName = "CaptureLocation";
        private const string StageRootName = "StagedContentRoot";
        private const string BackdropSphereName = "BackDropSphere";
        private const string PhotoBoothCameraName = "Camera";
        private const string CaptureOutputFolder = "Assets/Photo Booth Captures";
        private const float DefaultCameraDistance = 4.5f;
        private const float DefaultCameraPitch = 12f;
        private const float DefaultCameraYaw = 180f;
        private const float DefaultCameraFov = 35f;

        private const string CameraDistancePrefKey = "MashBoxSDK.PhotoBooth.CameraDistance";
        private const string CameraPitchPrefKey = "MashBoxSDK.PhotoBooth.CameraPitch";
        private const string CameraYawPrefKey = "MashBoxSDK.PhotoBooth.CameraYaw";
        private const string CameraFovPrefKey = "MashBoxSDK.PhotoBooth.CameraFov";
        private const string StageOffsetXPrefKey = "MashBoxSDK.PhotoBooth.StageOffsetX";
        private const string StageOffsetYPrefKey = "MashBoxSDK.PhotoBooth.StageOffsetY";
        private const string StageOffsetZPrefKey = "MashBoxSDK.PhotoBooth.StageOffsetZ";
        private const string StageRotationXPrefKey = "MashBoxSDK.PhotoBooth.StageRotationX";
        private const string StageRotationYPrefKey = "MashBoxSDK.PhotoBooth.StageRotationY";
        private const string StageRotationZPrefKey = "MashBoxSDK.PhotoBooth.StageRotationZ";

        private string _fileName = "NewCapture";
        private GameObject _stagedPrefabAsset;
        private float _cameraDistance = DefaultCameraDistance;
        private float _cameraPitch = DefaultCameraPitch;
        private float _cameraYaw = DefaultCameraYaw;
        private float _cameraFov = DefaultCameraFov;
        private Vector3 _stageOffset = Vector3.zero;
        private Vector3 _stageRotation = Vector3.zero;
        private string _photoBoothRigStatus = "";
        private Vector2 _scrollPosition;

        private sealed class BoothRig
        {
            public GameObject Root;
            public Transform CaptureLocation;
            public Transform StageRoot;
            public Camera Camera;
        }

        public static void Open()
        {
            GetWindow<PhotoBoothWindow>("Photo Booth");
        }

        private void OnEnable()
        {
            LoadPrefs();
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        private void OnDisable()
        {
            SavePrefs();
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        }

        private void OnGUI()
        {
            Draw();
        }

        public void Draw()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            if (!IsPhotoBoothSceneOpen())
            {
                EditorGUILayout.HelpBox(
                    "Photo Booth Scene is not open. If you do not have it yet, download/import the Photo Booth sample from the SDK Samples first.",
                    MessageType.Info
                );

                if (GUILayout.Button("Open Photo Booth Scene", GUILayout.Height(40)))
                    OpenPhotoBoothScene();

                EditorGUILayout.EndScrollView();
                return;
            }

            var rig = EnsurePhotoBoothRig();
            if (rig == null)
                return;

            GUILayout.Space(10);

            EditorGUILayout.LabelField("Photo Booth Rig", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The booth root, capture location, camera, stage root, and backdrop sphere are managed by the SDK. If users delete them, the window will regenerate them.",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "Frame your item in the Game view, not the Scene view. The final capture uses the Photo Booth camera output, so the Game view is the accurate preview of what will be saved.",
                MessageType.Warning);
            if (!string.IsNullOrEmpty(_photoBoothRigStatus))
                EditorGUILayout.HelpBox(_photoBoothRigStatus, MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Repair Booth Rig", GUILayout.Height(24)))
                {
                    rig = EnsurePhotoBoothRig(true);
                }

                if (GUILayout.Button("Select Capture Location", GUILayout.Height(24)))
                {
                    Selection.activeObject = rig.CaptureLocation.gameObject;
                    EditorGUIUtility.PingObject(rig.CaptureLocation.gameObject);
                }
            }

            GUILayout.Space(8);

            EditorGUILayout.LabelField("Stage Item", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Drag a prefab here and stage it. The SDK will place it under the capture location automatically so users do not need to parent it manually.",
                MessageType.None);

            EditorGUI.BeginChangeCheck();
            _stagedPrefabAsset = (GameObject)EditorGUILayout.ObjectField("Prefab", _stagedPrefabAsset, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck())
                Repaint();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_stagedPrefabAsset == null))
                {
                    if (GUILayout.Button("Stage Prefab", GUILayout.Height(28)))
                        StagePrefab(_stagedPrefabAsset, rig);
                }

                using (new EditorGUI.DisabledScope(!HasStagedObject(rig)))
                {
                    if (GUILayout.Button("Clear Staged Item", GUILayout.Height(28)))
                        ClearStagedObjects(rig);
                }
            }

            var stagedCount = GetStagedObjectCount(rig);
            if (stagedCount <= 0)
            {
                EditorGUILayout.HelpBox(
                    "No object staged. Use the prefab field above and click Stage Prefab.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"Staged objects: {stagedCount}. Content is kept under {CaptureLocationName}/{StageRootName}.",
                    MessageType.None);
            }

            using (new EditorGUI.DisabledScope(!HasStagedObject(rig)))
            {
                GUILayout.Space(8);
                EditorGUILayout.LabelField("Staged Content Transform", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "These sliders offset and rotate the staged content inside the booth without changing the source prefab asset.",
                    MessageType.None);

                EditorGUI.BeginChangeCheck();
                _stageOffset.x = EditorGUILayout.Slider("Offset X", _stageOffset.x, -5f, 5f);
                _stageOffset.y = EditorGUILayout.Slider("Offset Y", _stageOffset.y, -5f, 5f);
                _stageOffset.z = EditorGUILayout.Slider("Offset Z", _stageOffset.z, -5f, 5f);
                _stageRotation.x = EditorGUILayout.Slider("Rotation X", _stageRotation.x, -180f, 180f);
                _stageRotation.y = EditorGUILayout.Slider("Rotation Y", _stageRotation.y, -180f, 180f);
                _stageRotation.z = EditorGUILayout.Slider("Rotation Z", _stageRotation.z, -180f, 180f);
                if (EditorGUI.EndChangeCheck())
                {
                    ApplyStageTransform(rig, false);
                    SavePrefs();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Reset Stage Transform", GUILayout.Height(24)))
                    {
                        _stageOffset = Vector3.zero;
                        _stageRotation = Vector3.zero;
                        ApplyStageTransform(rig, false);
                        SavePrefs();
                    }

                    if (GUILayout.Button("Select Staged Content", GUILayout.Height(24)))
                    {
                        Selection.activeObject = rig.StageRoot.gameObject;
                        EditorGUIUtility.PingObject(rig.StageRoot.gameObject);
                    }
                }
            }

            GUILayout.Space(10);

            EditorGUILayout.LabelField("Camera Orbit", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _cameraDistance = EditorGUILayout.Slider("Distance", _cameraDistance, 0.1f, 5f);
            _cameraPitch = EditorGUILayout.Slider("Pitch", _cameraPitch, -45f, 45f);
            _cameraYaw = EditorGUILayout.Slider("Yaw", _cameraYaw, -180f, 180f);
            _cameraFov = EditorGUILayout.Slider("FOV", _cameraFov, 10f, 90f);
            if (EditorGUI.EndChangeCheck())
            {
                ApplyCameraOrbit(rig, false);
                SavePrefs();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset Camera", GUILayout.Height(24)))
                {
                    _cameraDistance = DefaultCameraDistance;
                    _cameraPitch = DefaultCameraPitch;
                    _cameraYaw = DefaultCameraYaw;
                    _cameraFov = DefaultCameraFov;
                    ApplyCameraOrbit(rig, false);
                    SavePrefs();
                }

                if (GUILayout.Button("Select Camera", GUILayout.Height(24)))
                {
                    Selection.activeObject = rig.Camera.gameObject;
                    EditorGUIUtility.PingObject(rig.Camera.gameObject);
                }
            }

            GUILayout.Space(10);

            EditorGUILayout.LabelField("Capture Settings", EditorStyles.boldLabel);
            _fileName = EditorGUILayout.TextField("File Name", _fileName);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Ping Output Folder", GUILayout.Height(24)))
                    PingOutputFolder();

                if (GUILayout.Button("Select Output Folder", GUILayout.Height(24)))
                    SelectOutputFolder();
            }

            using (new EditorGUI.DisabledScope(!HasStagedObject(rig)))
            {
                if (GUILayout.Button("Capture", GUILayout.Height(30)))
                    Capture(rig.Camera);
            }

            EditorGUILayout.EndScrollView();
        }

        private void OnHierarchyChanged()
        {
            if (!IsPhotoBoothSceneOpen())
                return;

            EnsurePhotoBoothRig();
            Repaint();
        }

        private bool IsPhotoBoothSceneOpen()
        {
            var scene = SceneManager.GetSceneByName(PhotoBoothSceneName);
            return scene.IsValid() && scene.isLoaded;
        }

        private void OpenPhotoBoothScene()
        {
            var guids = AssetDatabase.FindAssets($"t:Scene {PhotoBoothSceneName}");
            if (guids.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "Missing Scene",
                    $"{PhotoBoothSceneName} not found.\n\nPlease download/import the Photo Booth sample from the SDK Samples, then open the scene again.",
                    "OK");
                return;
            }

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            EditorSceneManager.OpenScene(path);
        }

        private BoothRig EnsurePhotoBoothRig(bool forceSceneDirty = false)
        {
            var scene = SceneManager.GetSceneByName(PhotoBoothSceneName);
            if (!scene.IsValid() || !scene.isLoaded)
                return null;

            TryRestoreBoothPrefab(scene);

            var changed = false;
            var rig = new BoothRig();

            rig.Root = FindNamedObjectInScene(scene, PhotoBoothRootName);
            if (rig.Root == null)
            {
                rig.Root = new GameObject(PhotoBoothRootName);
                SceneManager.MoveGameObjectToScene(rig.Root, scene);
                changed = true;
            }

            EnsureEditable(rig.Root);

            rig.CaptureLocation = FindNamedTransformUnder(rig.Root.transform, CaptureLocationName);
            if (rig.CaptureLocation == null)
            {
                var captureLocation = new GameObject(CaptureLocationName);
                captureLocation.transform.SetParent(rig.Root.transform, false);
                rig.CaptureLocation = captureLocation.transform;
                changed = true;
            }

            if (NormalizeManagedTransform(rig.CaptureLocation, Vector3.zero, Quaternion.identity, Vector3.one))
                changed = true;
            EnsureEditable(rig.CaptureLocation.gameObject);

            rig.StageRoot = FindNamedTransformUnder(rig.CaptureLocation, StageRootName);
            if (rig.StageRoot == null)
            {
                var stageRoot = new GameObject(StageRootName);
                stageRoot.transform.SetParent(rig.CaptureLocation, false);
                rig.StageRoot = stageRoot.transform;
                changed = true;
            }

            if (NormalizeManagedTransform(rig.StageRoot, Vector3.zero, Quaternion.identity, Vector3.one))
                changed = true;
            EnsureEditable(rig.StageRoot.gameObject);

            if (MoveLooseStageChildrenIntoStageRoot(rig.CaptureLocation, rig.StageRoot))
                changed = true;

            ApplyStageTransform(rig, false);

            rig.Camera = FindBoothCamera(scene);
            if (rig.Camera == null)
            {
                var cameraGo = new GameObject(PhotoBoothCameraName);
                SceneManager.MoveGameObjectToScene(cameraGo, scene);
                rig.Camera = cameraGo.AddComponent<Camera>();
                changed = true;
            }

            rig.Camera.name = PhotoBoothCameraName;
            EnsureEditable(rig.Camera.gameObject);
            ApplyCameraOrbit(rig, false);

            if (changed || forceSceneDirty)
                EditorSceneManager.MarkSceneDirty(scene);

            return rig;
        }

        private void TryRestoreBoothPrefab(Scene scene)
        {
            var hasPhotoBoothRoot = FindNamedObjectInScene(scene, PhotoBoothRootName) != null;
            var hasCaptureLocation = FindNamedObjectInScene(scene, CaptureLocationName) != null;
            var hasCamera = FindCameraInScene(scene) != null;

            if (hasPhotoBoothRoot && hasCaptureLocation && hasCamera)
                return;

            var existingPrefabRoot = FindSceneRootByName(scene, PhotoBoothPrefabName);
            if (existingPrefabRoot != null)
            {
                _photoBoothRigStatus = string.Empty;
                return;
            }

            var photoBoothPrefab = FindPhotoBoothPrefabAsset();
            if (photoBoothPrefab == null)
            {
                _photoBoothRigStatus = $"Could not find the '{PhotoBoothPrefabName}' prefab. Please update or reimport the Photo Booth sample from the SDK Samples.";
                return;
            }

            PrefabUtility.InstantiatePrefab(photoBoothPrefab, scene);
            _photoBoothRigStatus = string.Empty;
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static GameObject FindNamedObjectInScene(Scene scene, string name)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;

            foreach (var rootObject in scene.GetRootGameObjects())
            {
                if (rootObject.name == name)
                    return rootObject;

                var descendants = rootObject.GetComponentsInChildren<Transform>(true);
                foreach (var descendant in descendants)
                {
                    if (descendant.name == name)
                        return descendant.gameObject;
                }
            }

            return null;
        }

        private static GameObject FindSceneRootByName(Scene scene, string name)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;

            foreach (var rootObject in scene.GetRootGameObjects())
            {
                if (rootObject.name == name)
                    return rootObject;
            }

            return null;
        }

        private static Camera FindCameraInScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;

            foreach (var rootObject in scene.GetRootGameObjects())
            {
                var camera = rootObject.GetComponentInChildren<Camera>(true);
                if (camera != null)
                    return camera;
            }

            return null;
        }

        private static GameObject FindPhotoBoothPrefabAsset()
        {
            var guids = AssetDatabase.FindAssets($"t:Prefab {PhotoBoothPrefabName}");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != PhotoBoothPrefabName)
                    continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                    return prefab;
            }

            return null;
        }

        private static Transform FindNamedTransformUnder(Transform parent, string name)
        {
            if (parent == null)
                return null;

            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child;
            }

            return null;
        }

        private static void EnsureEditable(GameObject gameObject)
        {
            if (gameObject == null)
                return;

            if ((gameObject.hideFlags & HideFlags.NotEditable) == 0)
                return;

            gameObject.hideFlags &= ~HideFlags.NotEditable;
            EditorUtility.SetDirty(gameObject);
        }

        private static bool NormalizeManagedTransform(Transform target, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            if (target == null)
                return false;

            var changed = false;

            if (target.localPosition != localPosition)
            {
                target.localPosition = localPosition;
                changed = true;
            }

            if (target.localRotation != localRotation)
            {
                target.localRotation = localRotation;
                changed = true;
            }

            if (target.localScale != localScale)
            {
                target.localScale = localScale;
                changed = true;
            }

            return changed;
        }

        private static bool MoveLooseStageChildrenIntoStageRoot(Transform captureLocation, Transform stageRoot)
        {
            if (captureLocation == null || stageRoot == null)
                return false;

            var movedAny = false;

            for (var index = captureLocation.childCount - 1; index >= 0; index--)
            {
                var child = captureLocation.GetChild(index);
                if (child == stageRoot || child.name == BackdropSphereName)
                    continue;

                child.SetParent(stageRoot, true);
                movedAny = true;
            }

            return movedAny;
        }

        private Camera FindBoothCamera(Scene scene)
        {
            var exactName = FindNamedObjectInScene(scene, PhotoBoothCameraName);
            if (exactName != null && exactName.TryGetComponent<Camera>(out var namedCamera))
                return namedCamera;

            foreach (var rootObject in scene.GetRootGameObjects())
            {
                var camera = rootObject.GetComponentInChildren<Camera>(true);
                if (camera != null)
                    return camera;
            }

            return null;
        }

        private void ApplyCameraOrbit(BoothRig rig, bool recordUndo)
        {
            if (rig?.Camera == null || rig.CaptureLocation == null)
                return;

            if (recordUndo)
                Undo.RecordObject(rig.Camera.transform, "Adjust Photo Booth Camera");

            var pivot = rig.CaptureLocation.position;
            var orbitRotation = Quaternion.Euler(_cameraPitch, _cameraYaw, 0f);
            var cameraPosition = pivot + (orbitRotation * (Vector3.back * _cameraDistance));

            rig.Camera.transform.position = cameraPosition;
            rig.Camera.transform.rotation = Quaternion.LookRotation((pivot - cameraPosition).normalized, Vector3.up);
            rig.Camera.fieldOfView = _cameraFov;

            EditorUtility.SetDirty(rig.Camera.transform);
            EditorUtility.SetDirty(rig.Camera);
            EditorSceneManager.MarkSceneDirty(rig.Camera.gameObject.scene);
            SceneView.RepaintAll();
        }

        private void ApplyStageTransform(BoothRig rig, bool recordUndo)
        {
            if (rig?.StageRoot == null)
                return;

            if (recordUndo)
                Undo.RecordObject(rig.StageRoot, "Adjust Photo Booth Staged Content");

            rig.StageRoot.localPosition = _stageOffset;
            rig.StageRoot.localRotation = Quaternion.Euler(_stageRotation);
            rig.StageRoot.localScale = Vector3.one;

            EditorUtility.SetDirty(rig.StageRoot);
            EditorSceneManager.MarkSceneDirty(rig.StageRoot.gameObject.scene);
            SceneView.RepaintAll();
        }

        private void StagePrefab(GameObject prefabAsset, BoothRig rig)
        {
            if (prefabAsset == null || rig?.StageRoot == null)
                return;

            if (!PrefabUtility.IsPartOfPrefabAsset(prefabAsset))
            {
                EditorUtility.DisplayDialog("Invalid Prefab", "Please drag a prefab asset into the Prefab field.", "OK");
                return;
            }

            ClearStagedObjects(rig);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, rig.StageRoot.gameObject.scene);
            if (instance == null)
                return;

            Undo.RegisterCreatedObjectUndo(instance, "Stage Photo Booth Prefab");
            instance.transform.SetParent(rig.StageRoot, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            instance.name = prefabAsset.name;

            ApplyStageTransform(rig, false);

            Selection.activeObject = instance;
            EditorGUIUtility.PingObject(instance);
            EditorSceneManager.MarkSceneDirty(rig.StageRoot.gameObject.scene);
        }

        private void ClearStagedObjects(BoothRig rig)
        {
            if (rig?.StageRoot == null)
                return;

            for (var index = rig.StageRoot.childCount - 1; index >= 0; index--)
            {
                Undo.DestroyObjectImmediate(rig.StageRoot.GetChild(index).gameObject);
            }

            EditorSceneManager.MarkSceneDirty(rig.StageRoot.gameObject.scene);
        }

        private static int GetStagedObjectCount(BoothRig rig)
        {
            return rig?.StageRoot != null ? rig.StageRoot.childCount : 0;
        }

        private static bool HasStagedObject(BoothRig rig)
        {
            return GetStagedObjectCount(rig) > 0;
        }

        private void Capture(Camera cam)
        {
            if (!cam)
            {
                Debug.LogError("No camera in scene");
                return;
            }

            if (string.IsNullOrWhiteSpace(_fileName))
            {
                Debug.LogError("Invalid file name");
                return;
            }

            const string directory = CaptureOutputFolder;
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var fullPath = $"{directory}/{_fileName}";

            PhotoBoothUtility.Capture(fullPath, cam);
            AssetDatabase.Refresh();

            Debug.Log($"[PhotoBooth] Captured: {fullPath}");
        }

        private void LoadPrefs()
        {
            _cameraDistance = EditorPrefs.GetFloat(CameraDistancePrefKey, DefaultCameraDistance);
            _cameraPitch = EditorPrefs.GetFloat(CameraPitchPrefKey, DefaultCameraPitch);
            _cameraYaw = EditorPrefs.GetFloat(CameraYawPrefKey, DefaultCameraYaw);
            _cameraFov = EditorPrefs.GetFloat(CameraFovPrefKey, DefaultCameraFov);
            _stageOffset = new Vector3(
                EditorPrefs.GetFloat(StageOffsetXPrefKey, 0f),
                EditorPrefs.GetFloat(StageOffsetYPrefKey, 0f),
                EditorPrefs.GetFloat(StageOffsetZPrefKey, 0f));
            _stageRotation = new Vector3(
                EditorPrefs.GetFloat(StageRotationXPrefKey, 0f),
                EditorPrefs.GetFloat(StageRotationYPrefKey, 0f),
                EditorPrefs.GetFloat(StageRotationZPrefKey, 0f));
        }

        private void SavePrefs()
        {
            EditorPrefs.SetFloat(CameraDistancePrefKey, _cameraDistance);
            EditorPrefs.SetFloat(CameraPitchPrefKey, _cameraPitch);
            EditorPrefs.SetFloat(CameraYawPrefKey, _cameraYaw);
            EditorPrefs.SetFloat(CameraFovPrefKey, _cameraFov);
            EditorPrefs.SetFloat(StageOffsetXPrefKey, _stageOffset.x);
            EditorPrefs.SetFloat(StageOffsetYPrefKey, _stageOffset.y);
            EditorPrefs.SetFloat(StageOffsetZPrefKey, _stageOffset.z);
            EditorPrefs.SetFloat(StageRotationXPrefKey, _stageRotation.x);
            EditorPrefs.SetFloat(StageRotationYPrefKey, _stageRotation.y);
            EditorPrefs.SetFloat(StageRotationZPrefKey, _stageRotation.z);
        }

        private static void PingOutputFolder()
        {
            var folder = EnsureOutputFolderAsset();
            if (folder == null)
                return;

            EditorGUIUtility.PingObject(folder);
        }

        private static void SelectOutputFolder()
        {
            var folder = EnsureOutputFolderAsset();
            if (folder == null)
                return;

            Selection.activeObject = folder;
            EditorGUIUtility.PingObject(folder);
        }

        private static DefaultAsset EnsureOutputFolderAsset()
        {
            EnsureFolderExists(CaptureOutputFolder);
            AssetDatabase.Refresh();
            return AssetDatabase.LoadAssetAtPath<DefaultAsset>(CaptureOutputFolder);
        }

        private static void EnsureFolderExists(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
                return;

            var normalizedPath = assetPath.Replace("\\", "/");
            var segments = normalizedPath.Split('/');
            if (segments.Length < 2 || segments[0] != "Assets")
                return;

            var currentPath = segments[0];
            for (var index = 1; index < segments.Length; index++)
            {
                var nextPath = $"{currentPath}/{segments[index]}";
                if (!AssetDatabase.IsValidFolder(nextPath))
                    AssetDatabase.CreateFolder(currentPath, segments[index]);

                currentPath = nextPath;
            }
        }
    }
}

#endif
