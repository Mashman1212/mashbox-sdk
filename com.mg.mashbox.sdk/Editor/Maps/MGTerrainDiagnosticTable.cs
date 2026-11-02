#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    /// <summary>Full-width diagnostic cells, independent of the Inspector label-width setting.</summary>
    internal static class MGTerrainDiagnosticTable
    {
        static readonly float[] MetricColumns = { .66f, .34f };
        static GUIStyle s_Label, s_Value, s_Heading, s_BoldValue;

        internal static void Header(string label, string value) => Row(0, MetricColumns, new[] { label, value }, heading: true);
        internal static void Metric(int row, string label, string value, bool total = false) =>
            Row(row, MetricColumns, new[] { label, value }, total: total);

        internal static Rect[] Row(int row, float[] columns, string[] values, bool heading = false, bool total = false)
        {
            s_Label ??= new GUIStyle(EditorStyles.label) { wordWrap = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset() };
            s_Value ??= new GUIStyle(s_Label) { alignment = TextAnchor.MiddleRight };
            s_Heading ??= new GUIStyle(s_Label) { fontStyle = FontStyle.Bold };
            s_BoldValue ??= new GUIStyle(s_Value) { fontStyle = FontStyle.Bold };
            float available = Mathf.Max(320, EditorGUIUtility.currentViewWidth - 38);
            float height = 28;
            for (int i = 0; i < values.Length; i++)
                height = Mathf.Max(height, (heading || total ? s_Heading : i == 0 ? s_Label : s_Value)
                    .CalcHeight(new GUIContent(values[i]), Mathf.Max(20, available * columns[i] - 16)) + 10);
            Rect rect = GUILayoutUtility.GetRect(0, height, GUILayout.Height(height), GUILayout.ExpandWidth(true));
            bool dark = EditorGUIUtility.isProSkin;
            Color background = heading ? (dark ? new Color(.19f, .23f, .28f) : new Color(.77f, .83f, .9f))
                : total ? (dark ? new Color(.17f, .27f, .34f) : new Color(.77f, .87f, .95f))
                : dark ? (row % 2 == 0 ? new Color(.21f, .21f, .21f) : new Color(.245f, .245f, .245f))
                : row % 2 == 0 ? new Color(.87f, .87f, .87f) : new Color(.93f, .93f, .93f);
            Color line = dark ? new Color(.12f, .12f, .12f) : new Color(.72f, .72f, .72f);
            EditorGUI.DrawRect(rect, background);
            var cells = new Rect[columns.Length];
            float x = rect.x;
            for (int i = 0; i < columns.Length; i++)
            {
                float width = i == columns.Length - 1 ? rect.xMax - x : rect.width * columns[i];
                var cell = new Rect(x, rect.y, width, rect.height);
                cells[i] = new Rect(cell.x + 8, cell.y + 4, Mathf.Max(0, cell.width - 16), cell.height - 8);
                if (!string.IsNullOrEmpty(values[i]))
                    GUI.Label(cells[i], new GUIContent(values[i], values[i]), heading || total ? (i == 0 ? s_Heading : s_BoldValue) : i == 0 ? s_Label : s_Value);
                EditorGUI.DrawRect(new Rect(cell.x, cell.y, 1, cell.height), line);
                x += width;
            }
            EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), line);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), line);
            return cells;
        }
    }
}
#endif
