#if UNITY_EDITOR
using MashBoxSDK.ContentTools;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.ContentTools.Editor
{
    [CustomPropertyDrawer(typeof(ContentPackDefinition.GameModMapping))]
    internal sealed class GameModMappingDrawer : PropertyDrawer
    {
        private const float LineGap = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 3f + LineGap * 2f;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var gameName = property.FindPropertyRelative(nameof(ContentPackDefinition.GameModMapping.GameName));
            var modId = property.FindPropertyRelative(nameof(ContentPackDefinition.GameModMapping.ModId));
            var isPublishTarget = property.FindPropertyRelative(nameof(ContentPackDefinition.GameModMapping.IsPublishTarget));

            var lineHeight = EditorGUIUtility.singleLineHeight;
            var gameRect = new Rect(position.x, position.y, position.width, lineHeight);
            var modRect = new Rect(position.x, gameRect.yMax + LineGap, position.width, lineHeight);
            var targetRect = new Rect(position.x, modRect.yMax + LineGap, position.width, lineHeight);

            EditorGUI.PropertyField(gameRect, gameName, new GUIContent("Game Name"));
            EditorGUI.PropertyField(modRect, modId, new GUIContent("Mod ID"));

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.ToggleLeft(
                    targetRect,
                    new GUIContent(
                        "Publish Target",
                        "Set automatically to the active Setup game when publishing."),
                    isPublishTarget.boolValue);
            }

            EditorGUI.EndProperty();
        }
    }
}
#endif
