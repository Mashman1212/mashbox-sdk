#if UNITY_EDITOR
#if MashBoxDev

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.Dev
{
    public class BundleViewerWindow : EditorWindow
    {
        private string _bundlePath = "";
        private AssetBundle _loadedBundle;

        private List<GameObject> _prefabs = new List<GameObject>();
        private Vector2 _scroll;

        [MenuItem("MashBox/Dev/Bundle Viewer")]
        public static void ShowWindow()
        {
            GetWindow<BundleViewerWindow>("Bundle Viewer");
        }

        private void OnGUI()
        {
            GUILayout.Label("AssetBundle Loader", EditorStyles.boldLabel);

            GUILayout.Space(5);

            // =========================
            //  PATH SELECT
            // =========================
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.TextField("Bundle Path", _bundlePath);

                if (GUILayout.Button("Browse", GUILayout.Width(80)))
                {
                    string path = EditorUtility.OpenFilePanel("Select AssetBundle", "", "");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _bundlePath = path;
                    }
                }
            }

            GUILayout.Space(5);

            // =========================
            // LOAD BUTTON
            // =========================
            if (GUILayout.Button("Load Bundle", GUILayout.Height(30)))
            {
                LoadBundle();
            }

            if (_loadedBundle != null)
            {
                GUILayout.Space(10);

                // =========================
                // ACTIONS
                // =========================
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Instantiate All", GUILayout.Height(25)))
                    {
                        foreach (var prefab in _prefabs)
                        {
                            InstantiatePrefab(prefab);
                        }
                    }

                    if (GUILayout.Button("Unload Bundle", GUILayout.Height(25)))
                    {
                        _loadedBundle.Unload(false);
                        _loadedBundle = null;
                        _prefabs.Clear();
                    }
                }

                GUILayout.Space(10);

                GUILayout.Label($"Loaded Prefabs: {_prefabs.Count}", EditorStyles.boldLabel);

                // =========================
                // LIST
                // =========================
                _scroll = EditorGUILayout.BeginScrollView(_scroll);

                foreach (var prefab in _prefabs)
                {
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                    {
                        GUILayout.Label(prefab.name);

                        GUILayout.FlexibleSpace();

                        if (GUILayout.Button("Instantiate", GUILayout.Width(100)))
                        {
                            InstantiatePrefab(prefab);
                        }
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

// =========================
// LOAD LOGIC
// =========================
        private void LoadBundle()
        {
            if (string.IsNullOrEmpty(_bundlePath) || !File.Exists(_bundlePath))
            {
                Debug.LogError("Invalid bundle path");
                return;
            }

            if (_loadedBundle != null)
            {
                _loadedBundle.Unload(false);
                _prefabs.Clear();
            }

            _loadedBundle = AssetBundle.LoadFromFile(_bundlePath);

            if (_loadedBundle == null)
            {
                Debug.LogError("Failed to load bundle");
                return;
            }

            Debug.Log("Bundle loaded");
            
            var objs = _loadedBundle.LoadAllAssets<GameObject>();

            _prefabs = new List<GameObject>(objs);

            Debug.Log($"Found {_prefabs.Count} GameObjects");
        }

// =========================
// INSTANTIATE
// =========================
        private void InstantiatePrefab(GameObject prefab)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            if (instance == null)
            {
                // fallback if not a prefab
                instance = Instantiate(prefab);
            }

            instance.transform.position = Vector3.zero;
            Selection.activeGameObject = instance;
        }
    }
}

#endif
#endif
