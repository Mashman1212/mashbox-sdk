#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;
using System.IO;

namespace MashBoxSDK.SharedCore.Editor
{
    public class AddressablesCorePatcherWindow : EditorWindow
    {
        private string contentCatalogPath = "";
        private string coreCatalogPath = "";

#if MashBoxDev
        [MenuItem("MashBox/Dev/Core Catalog Patcher")]
#endif
        public static void ShowWindow()
        {
            GetWindow<AddressablesCorePatcherWindow>("Core Catalog Patcher");
        }

        private void OnGUI()
        {
            GUILayout.Label("Addressables Core Catalog Patcher", EditorStyles.boldLabel);

            GUILayout.Space(10);

            DrawPath("Content Catalog", ref contentCatalogPath);
            DrawPath("Core Catalog", ref coreCatalogPath);

            GUILayout.Space(15);

            if (GUILayout.Button("Validate Core Mapping", GUILayout.Height(30)))
                Validate();

            if (GUILayout.Button("Patch Catalog", GUILayout.Height(40)))
                Patch();
        }

        private void DrawPath(string label, ref string path)
        {
            GUILayout.Label(label);

            EditorGUILayout.BeginHorizontal();
            path = EditorGUILayout.TextField(path);

            if (GUILayout.Button("Browse", GUILayout.Width(80)))
                path = EditorUtility.OpenFilePanel("Select Catalog", "", "json");

            EditorGUILayout.EndHorizontal();
        }

        private void Validate()
        {
            if (!File.Exists(contentCatalogPath) || !File.Exists(coreCatalogPath))
            {
                Debug.LogError("❌ Invalid file paths");
                return;
            }

            AddressablesCorePatcher.Validate(contentCatalogPath, coreCatalogPath);
        }

        private void Patch()
        {
            if (!File.Exists(contentCatalogPath) || !File.Exists(coreCatalogPath))
            {
                Debug.LogError("❌ Invalid file paths");
                return;
            }

            AddressablesCorePatcher.PatchCatalog(contentCatalogPath, coreCatalogPath);
        }
    }
}

#endif