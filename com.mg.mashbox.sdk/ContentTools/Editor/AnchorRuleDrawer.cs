#if UNITY_EDITOR
using MashBoxSDK.ContentTools;
using UnityEditor;
using UnityEngine;

namespace ContentTools.Editor
{

    public class AnchorRuleDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var superType = property.FindPropertyRelative("AppliesToSuperType");
            var type = property.FindPropertyRelative("AppliesToType");
            var brand = property.FindPropertyRelative("AppliesToBrand");

            string superTypeName = superType.enumDisplayNames[superType.enumValueIndex];
            string typeName = type.enumDisplayNames[type.enumValueIndex];

            string title = $"{superTypeName} / {typeName}";

            if (!string.IsNullOrEmpty(brand.stringValue))
                title += $" ({brand.stringValue})";

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
            DrawProperty(ref y, position, property, "AppliesToBrand");
            DrawProperty(ref y, position, property, "RequiredChildren");
            DrawProperty(ref y, position, property, "PreferredOrientations");

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

            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("AppliesToSuperType"), true);
            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("AppliesToType"), true);
            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("AppliesToBrand"), true);
            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("RequiredChildren"), true);
            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("PreferredOrientations"), true);

            return height + 8;
        }
    }
}
#endif