#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainEditor
    {
        GUIStyle m_BrushHeadingStyle, m_BrushModeStyle, m_BrushModeLabelStyle;
        static readonly string[] BrushModeLabels = { "Density", "Size", "Health" };
        static readonly string[] BrushModeTips = {
            "Density: paint the checked detail types towards the target density. Shift thins or erases them. Click again to disable; combine with Size or Health.",
            "Size: paint a local size multiplier on checked detail types. Shift restores size to 1. Click again to disable; combine with Density or Health.",
            "Health: paint the health of checked detail types. Shift restores full health. Click again to disable; combine with Density or Size." };
        static readonly Color[] BrushModeColours = {
            new Color(.4f, .85f, .45f), new Color(.3f, .8f, 1f), new Color(1f, .7f, .3f), new Color(.8f, .55f, 1f) };

        void DrawDetailBrushHeader() => DrawDetailSectionHeader("Brush", true);
        void DrawDetailCardsHeader() => DrawDetailSectionHeader("Details", false);

        void DrawDetailSectionHeader(string title, bool brush)
        {
            m_BrushHeadingStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            GUILayout.Space(10);
            Rect row = EditorGUILayout.GetControlRect(false, 32);
            Color rule = EditorGUIUtility.isProSkin ? new Color(.42f, .46f, .5f, .65f) : new Color(.4f, .43f, .47f, .5f);
            EditorGUI.DrawRect(new Rect(row.x, row.yMax - 2, row.width, 1), rule);
            GUI.Label(new Rect(row.x + 28, row.y, row.width - 28, 26), title, m_BrushHeadingStyle);
            if (Event.current.type == EventType.Repaint)
            {
                Color previous = Handles.color;
                Handles.BeginGUI();
                Handles.color = new Color(.35f, .75f, 1f);
                var r = new Rect(row.x + 3, row.y + 4, 20, 20);
                void Line(params Vector3[] points) => Handles.DrawAAPolyLine(1.8f, points);
                Vector3 P(float x, float y) => new Vector3(r.x + x, r.y + y);
                if (brush)
                {
                    Line(P(7,12),P(15,2),P(18,5),P(10,15),P(7,12));
                    Line(P(7,12),P(3,13),P(2,18),P(7,18),P(10,15));
                }
                else
                {
                    Line(P(2,6),P(10,2),P(18,6),P(10,10),P(2,6));
                    Line(P(2,10),P(10,14),P(18,10));
                    Line(P(2,14),P(10,18),P(18,14));
                }
                Handles.color = previous;
                Handles.EndGUI();
            }
            GUILayout.Space(5);
        }

        bool m_BrushChannelsInitialized;

        void EnsureDetailBrushChannels()
        {
            if (m_BrushChannelsInitialized) return;
            m_BrushChannelsInitialized = true;
            if (m_PaintChannel < 3)
            {
                m_MultiDensity = m_PaintChannel == 0;
                m_MultiSize = m_PaintChannel == 1;
                m_MultiHealth = m_PaintChannel == 2;
            }
        }

        bool HasDetailBrushChannels => PaintsChannel(0) || PaintsChannel(1) || PaintsChannel(2);

        void SetDetailBrushChannel(int channel, bool enabled)
        {
            EnsureDetailBrushChannels();
            bool previous = channel == 0 ? m_MultiDensity : channel == 1 ? m_MultiSize : m_MultiHealth;
            if (previous == enabled) return;
            FinishDetailStroke();
            if (channel == 0) m_MultiDensity = enabled;
            else if (channel == 1) m_MultiSize = enabled;
            else m_MultiHealth = enabled;
            int count = (m_MultiDensity ? 1 : 0) + (m_MultiSize ? 1 : 0) + (m_MultiHealth ? 1 : 0);
            m_PaintChannel = count == 1 ? (m_MultiDensity ? 0 : m_MultiSize ? 1 : 2) : 3;
            if (m_WorldAutoDetailBrush) UpdateWorldDetailBrush((MashBoxSDK.Maps.TerrainSystem.MGTerrain)target);
            else if (count == 0) SetDetailPainting(false);
            Repaint();
            SceneView.RepaintAll();
        }

        void DrawDetailBrushModes()
        {
            EnsureDetailBrushChannels();
            m_BrushModeStyle ??= new GUIStyle(EditorStyles.miniButton) { fixedHeight = 0 };
            m_BrushModeLabelStyle ??= new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft };
            using (new EditorGUILayout.HorizontalScope())
                for (int mode = 0; mode < BrushModeLabels.Length; mode++)
                {
                    Rect rect = GUILayoutUtility.GetRect(0, 38, GUILayout.Height(38), GUILayout.ExpandWidth(true));
                    bool selected = mode == 0 ? m_MultiDensity : mode == 1 ? m_MultiSize : m_MultiHealth;
                    bool next = GUI.Toggle(rect, selected, new GUIContent("", BrushModeTips[mode]), m_BrushModeStyle);
                    if (next != selected) { SetDetailBrushChannel(mode, next); selected = next; }
                    float textWidth = m_BrushModeLabelStyle.CalcSize(new GUIContent(BrushModeLabels[mode])).x;
                    float left = rect.center.x - (textWidth + 26) * .5f;
                    GUI.Label(new Rect(left + 26, rect.y, textWidth + 2, rect.height - 2), new GUIContent(BrushModeLabels[mode], BrushModeTips[mode]), m_BrushModeLabelStyle);
                    if (Event.current.type != EventType.Repaint) continue;
                    Color colour = selected ? BrushModeColours[mode]
                        : EditorGUIUtility.isProSkin ? new Color(.8f, .83f, .86f) : new Color(.25f, .3f, .35f);
                    DrawBrushModeIcon(new Rect(left, rect.center.y - 9, 18, 18), mode, colour);
                    if (selected) EditorGUI.DrawRect(new Rect(rect.x + 1, rect.yMax - 3, rect.width - 2, 2), BrushModeColours[mode]);
                }
            GUILayout.Space(6);
        }


        static void DrawBrushModeIcon(Rect rect, int mode, Color colour)
        {
            Color previous = Handles.color;
            Handles.BeginGUI();
            try
            {
                Handles.color = colour;
                Vector3 P(float x, float y) => new Vector3(rect.x + x, rect.y + y);
                void Line(params Vector3[] points) => Handles.DrawAAPolyLine(1.7f, points);
                if (mode == 0)
                    for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
                        Handles.DrawSolidDisc(P(3 + x * 6, 3 + y * 6), Vector3.forward, 1.3f);
                else if (mode == 1)
                {
                    Line(P(1,9),P(17,9)); Line(P(4,6),P(1,9),P(4,12)); Line(P(14,6),P(17,9),P(14,12));
                    Line(P(9,1),P(9,17)); Line(P(6,4),P(9,1),P(12,4)); Line(P(6,14),P(9,17),P(12,14));
                }
                else if (mode == 2)
                    Line(P(9,16),P(2,9),P(1,5),P(3,2),P(6,2),P(9,5),P(12,2),P(15,2),P(17,5),P(16,9),P(9,16));
                else
                {
                    Line(P(1,5),P(9,1),P(17,5),P(9,9),P(1,5));
                    Line(P(1,9),P(9,13),P(17,9));
                    Line(P(1,13),P(9,17),P(17,13));
                }
            }
            finally { Handles.color = previous; Handles.EndGUI(); }
        }
    }
}
#endif
