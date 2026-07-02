#if UNITY_EDITOR
using MashBoxSDK.ContentTools;
using UnityEditor;
using UnityEngine;

namespace ContentTools.Editor
{
    public class TextureRuleDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var superType = property.FindPropertyRelative("AppliesToSuperType");
            var type = property.FindPropertyRelative("AppliesToType");

            string superTypeName = superType.enumDisplayNames.Length > 0
                ? superType.enumDisplayNames[superType.enumValueIndex]
                : "None";

            string typeName = type.enumDisplayNames.Length > 0
                ? type.enumDisplayNames[type.enumValueIndex]
                : "None";

            string title = $"{superTypeName} / {typeName}";

            if (superType.enumValueIndex == 0 && type.enumValueIndex == 0)
                title = "Texture Rule";

            property.isExpanded = EditorGUI.Foldout(
                new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight),
                property.isExpanded,
                title,
                true
            );

            if (!property.isExpanded)
                return;

            EditorGUI.indentLevel++;

            float y = position.y + EditorGUIUtility.singleLineHeight + 2;

            DrawProperty(ref y, position, property, "AppliesToSuperType");
            DrawProperty(ref y, position, property, "AppliesToType");
            DrawProperty(ref y, position, property, "ShaderType");
            DrawProperty(ref y, position, property, "MaxTextureDataMB");
            DrawProperty(ref y, position, property, "Slots");

            EditorGUI.indentLevel--;
        }

        void DrawProperty(ref float y, Rect position, SerializedProperty parent, string name)
        {
            var prop = parent.FindPropertyRelative(name);
            float height = EditorGUI.GetPropertyHeight(prop, true);

            EditorGUI.PropertyField(
                new Rect(position.x, y, position.width, height),
                prop,
                true
            );

            y += height + 2;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
                return EditorGUIUtility.singleLineHeight;

            float height = EditorGUIUtility.singleLineHeight;

            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("AppliesToSuperType"), true) + 2;
            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("AppliesToType"), true) + 2;
            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("ShaderType"), true) + 2;
            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("MaxTextureDataMB"), true) + 2;
            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("Slots"), true) + 2;

            return height + 4; // small padding at bottom
        }
    }
}
#endif