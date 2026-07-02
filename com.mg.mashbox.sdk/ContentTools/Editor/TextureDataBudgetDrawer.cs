#if UNITY_EDITOR
using System;
using MashBoxSDK.ContentTools;
using UnityEditor;
using UnityEngine;

namespace ContentTools.Editor
{
    [CustomPropertyDrawer(typeof(ContentValidationRules.TextureDataBudget))]
    public class TextureDataBudgetDrawer : PropertyDrawer
    {
        static readonly float[] values =
        {
            0f,  
            0.25f,
            0.5f,
            1f,
            2f,
            4f,
            5f,
            6f,
            8f,
            16f,
            32f
        };

        static readonly string[] labels =
        {
            "0 MB",
            "0.25 MB",
            "0.5 MB",
            "1 MB",
            "2 MB",
            "4 MB",
            "5 MB",
            "6 MB",
            "8 MB",
            "16 MB",
            "32 MB"
        };

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var mbProp = property.FindPropertyRelative("MB");

            float current = mbProp.floatValue;

            // Find closest index
            int index = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (Mathf.Approximately(values[i], current))
                {
                    index = i;
                    break;
                }
            }

            EditorGUI.BeginChangeCheck();

            int newIndex = EditorGUI.Popup(position, label.text, index, labels);

            if (EditorGUI.EndChangeCheck())
            {
                mbProp.floatValue = values[newIndex];
            }
        }
    }
}
#endif