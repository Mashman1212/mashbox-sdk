using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK
{
    public class MaterialPropertyMigrator : EditorWindow
    {
        [System.Serializable]
        public class PropertyMapping
        {
            public string oldProperty;
            public string newProperty;
        }

        List<PropertyMapping> mappings = new List<PropertyMapping>();

        Vector2 scroll;

#if MashBoxDev
        [MenuItem("MashBox/Dev/Rendering/Material Property Migrator")]
        static void Open()
        {
            GetWindow<MaterialPropertyMigrator>("Material Migrator");
        }
#endif

        void OnGUI()
        {
            GUILayout.Label("Material Property Migration", EditorStyles.boldLabel);

            GUILayout.Space(5);

            if (GUILayout.Button("Add Mapping"))
            {
                mappings.Add(new PropertyMapping());
            }

            GUILayout.Space(10);

            scroll = EditorGUILayout.BeginScrollView(scroll);

            for (int i = 0; i < mappings.Count; i++)
            {
                EditorGUILayout.BeginVertical("box");

                EditorGUILayout.BeginHorizontal();

                mappings[i].oldProperty = EditorGUILayout.TextField("Old", mappings[i].oldProperty);
                mappings[i].newProperty = EditorGUILayout.TextField("New", mappings[i].newProperty);

                if (GUILayout.Button("X", GUILayout.Width(20)))
                {
                    mappings.RemoveAt(i);
                    break;
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();

            GUILayout.Space(10);

            if (GUILayout.Button("Run Migration On Selected Materials"))
            {
                RunMigration();
            }

            GUILayout.Space(10);

            EditorGUILayout.HelpBox(
                "Select materials in the Project window.\n" +
                "Each row copies values from Old → New property.",
                MessageType.Info);
        }

        void RunMigration()
        {
            var selected = Selection.objects;

            int changed = 0;

            foreach (var obj in selected)
            {
                if (obj is Material mat)
                {
                    Undo.RecordObject(mat, "Material Property Migration");

                    foreach (var map in mappings)
                    {
                        CopyValue(mat, map.oldProperty, map.newProperty);
                    }

                    EditorUtility.SetDirty(mat);
                    changed++;
                }
            }

            AssetDatabase.SaveAssets();

            Debug.Log($"Migrated properties on {changed} materials.");
        }

        void CopyValue(Material mat, string oldProp, string newProp)
        {
            if (!mat.HasProperty(oldProp) || !mat.HasProperty(newProp))
                return;

            Texture tex = mat.GetTexture(oldProp);
            if (tex != null)
            {
                mat.SetTexture(newProp, tex);
                mat.SetTextureOffset(newProp, mat.GetTextureOffset(oldProp));
                mat.SetTextureScale(newProp, mat.GetTextureScale(oldProp));
                return;
            }

            mat.SetFloat(newProp, mat.GetFloat(oldProp));
            mat.SetColor(newProp, mat.GetColor(oldProp));
            mat.SetVector(newProp, mat.GetVector(oldProp));
        }
    }
}