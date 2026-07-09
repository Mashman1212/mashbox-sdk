#if UNITY_EDITOR
using MashBoxSDK.ContentTools;
using UnityEditor;
using UnityEngine;

namespace ContentTools
{
    [CustomPropertyDrawer(typeof(ContentValidationRules.ItemRule))]
    public class ItemRuleDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var superType = property.FindPropertyRelative("AppliesToSuperType");
            var type = property.FindPropertyRelative("AppliesToType");

            var ignoreRequireChildren = property.FindPropertyRelative("IgnoreRequiredChildren");
            var requiredChildren = property.FindPropertyRelative("RequiredChildren");
            var preferredOrientations = property.FindPropertyRelative("PreferredOrientations");
            var shader = property.FindPropertyRelative("ShaderType");
            var additionalAllowedShaders = property.FindPropertyRelative("AdditionalAllowedShaderTypes");
            var maxTextureData = property.FindPropertyRelative("MaxTextureDataMB");
            var slots = property.FindPropertyRelative("Slots");
            var maxVertexCount = property.FindPropertyRelative("MaxVertexCount");
            var maxRenderers = property.FindPropertyRelative("MaxRenderers");
            var maxDistinctMaterials = property.FindPropertyRelative("MaxDistinctMaterials");

            string superTypeName = superType.enumDisplayNames[superType.enumValueIndex];
            string typeName = type.enumDisplayNames[type.enumValueIndex];

            string title = $"{superTypeName} / {typeName}";


            // Foldout
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

            // --- Basic Info ---
            DrawProperty(ref y, position, superType);
            DrawProperty(ref y, position, type);

            y += EditorGUIUtility.standardVerticalSpacing * 2;

            // --- Anchors ---
            DrawProperty(ref y, position, ignoreRequireChildren);
            DrawProperty(ref y, position, requiredChildren);
            DrawProperty(ref y, position, preferredOrientations);

            y += EditorGUIUtility.standardVerticalSpacing * 2;

            // --- Mesh ---
            DrawProperty(ref y, position, maxVertexCount);
            DrawProperty(ref y, position, maxRenderers);
            DrawProperty(ref y, position, maxDistinctMaterials);

            y += EditorGUIUtility.standardVerticalSpacing * 2;

            // --- Rendering ---
            DrawProperty(ref y, position, shader);
            DrawProperty(ref y, position, additionalAllowedShaders);
            DrawProperty(ref y, position, maxTextureData);
            DrawProperty(ref y, position, slots);

            EditorGUI.indentLevel--;
        }

        void DrawProperty(ref float y, Rect position, SerializedProperty prop)
        {
            float height = EditorGUI.GetPropertyHeight(prop, true);

            EditorGUI.PropertyField(
                new Rect(position.x, y, position.width, height),
                prop,
                true
            );

            y += height + 2;
        }

        float DrawAndMeasure(SerializedProperty prop)
        {
            return EditorGUI.GetPropertyHeight(prop, true) + 2;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
                return EditorGUIUtility.singleLineHeight;

            float height = EditorGUIUtility.singleLineHeight;

            // --- Basic Info ---
            height += DrawAndMeasure(property.FindPropertyRelative("AppliesToSuperType"));
            height += DrawAndMeasure(property.FindPropertyRelative("AppliesToType"));

            height += EditorGUIUtility.standardVerticalSpacing * 2;

            // --- Anchors ---
            height += DrawAndMeasure(property.FindPropertyRelative("IgnoreRequiredChildren"));
            height += DrawAndMeasure(property.FindPropertyRelative("RequiredChildren"));
            height += DrawAndMeasure(property.FindPropertyRelative("PreferredOrientations"));

            height += EditorGUIUtility.standardVerticalSpacing * 2;

            // --- Mesh ---
            height += DrawAndMeasure(property.FindPropertyRelative("MaxVertexCount"));
            height += DrawAndMeasure(property.FindPropertyRelative("MaxRenderers"));
            height += DrawAndMeasure(property.FindPropertyRelative("MaxDistinctMaterials"));

            height += EditorGUIUtility.standardVerticalSpacing * 2;

            // --- Rendering ---
            height += DrawAndMeasure(property.FindPropertyRelative("ShaderType"));
            height += DrawAndMeasure(property.FindPropertyRelative("AdditionalAllowedShaderTypes"));
            height += DrawAndMeasure(property.FindPropertyRelative("MaxTextureDataMB"));
            height += DrawAndMeasure(property.FindPropertyRelative("Slots"));

            return height + 4;
        }
    }
}
#endif
