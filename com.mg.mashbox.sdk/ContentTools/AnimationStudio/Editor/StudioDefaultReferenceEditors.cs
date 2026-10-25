using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioDefaultReferenceFields
    {
        internal static void Draw(Rect rect, SerializedProperty model, SerializedProperty reference, GUIContent label)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(rect, model, label);
            if (!EditorGUI.EndChangeCheck()) return;
            // Explicitly choosing None must clear recovery too. Import-time nulls
            // never pass through this UI path, so they retain the original identity.
            var path = model.objectReferenceValue ? AssetDatabase.GetAssetPath(model.objectReferenceValue) : "";
            reference.FindPropertyRelative("assetPath").stringValue = path;
            reference.FindPropertyRelative("guid").stringValue = string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path);
        }
        internal static void Draw(SerializedObject owner, string name)
        {
            var field = owner.FindProperty(name);
            Draw(EditorGUILayout.GetControlRect(), field, owner.FindProperty(name + "Reference"), new GUIContent(field.displayName, field.tooltip));
        }
        internal static void Script(SerializedObject owner)
        {
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(owner.FindProperty("m_Script"));
        }
    }

    [CustomEditor(typeof(StudioDefaultCharacter)), CanEditMultipleObjects]
    internal sealed class StudioDefaultCharacterEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            StudioDefaultReferenceFields.Script(serializedObject);
            StudioDefaultReferenceFields.Draw(serializedObject, "character");
            StudioDefaultReferenceFields.Draw(serializedObject, "body");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("outfit"), true);
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(StudioSkinningDefaults)), CanEditMultipleObjects]
    internal sealed class StudioSkinningDefaultsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            StudioDefaultReferenceFields.Script(serializedObject);
            foreach (string name in new[] { "body", "bust", "pants", "shirt" }) StudioDefaultReferenceFields.Draw(serializedObject, name);
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomPropertyDrawer(typeof(StudioDefaultCharacter.OutfitPiece))]
    internal sealed class StudioDefaultOutfitPieceDrawer : PropertyDrawer
    {
        private static float Line => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            property.isExpanded ? 3 * Line + EditorGUI.GetPropertyHeight(property.FindPropertyRelative("options"), true) : EditorGUIUtility.singleLineHeight;
        public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(rect, label, property);
            var row = new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, label, true);
            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                row.y += Line; EditorGUI.PropertyField(row, property.FindPropertyRelative("enabled"));
                row.y += Line; StudioDefaultReferenceFields.Draw(row, property.FindPropertyRelative("prefab"), property.FindPropertyRelative("modelReference"), new GUIContent("Prefab"));
                row.y += Line; row.height = EditorGUI.GetPropertyHeight(property.FindPropertyRelative("options"), true);
                EditorGUI.PropertyField(row, property.FindPropertyRelative("options"), true);
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }
    }
}
