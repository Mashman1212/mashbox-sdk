#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.EditorResources
{
    public static class MashBoxInspectorHeaderUtility
    {
        private const float VerticalSpacing = 8f;
        private static Texture2D scriptHeader;

        public static void DrawScriptHeader()
        {
            var header = GetScriptHeader();
            if (header == null)
                return;

            GUILayout.Space(VerticalSpacing);

            var availableWidth = Mathf.Max(64f, EditorGUIUtility.currentViewWidth - 40f);
            var aspect = (float)header.height / header.width;
            var rect = GUILayoutUtility.GetRect(availableWidth, availableWidth * aspect, GUILayout.ExpandWidth(true));

            GUI.DrawTexture(rect, header, ScaleMode.ScaleToFit, true);

            GUILayout.Space(VerticalSpacing);
        }

        private static Texture2D GetScriptHeader()
        {
            if (scriptHeader != null)
                return scriptHeader;

            scriptHeader = AssetDatabase.LoadAssetAtPath<Texture2D>(MashBoxEditorResources.SCRIPT_HEADER);
            return scriptHeader;
        }
    }
}
#endif
