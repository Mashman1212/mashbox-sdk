#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainToolbarIcons
    {
        internal enum Icon { Locked, Unlocked, Select, Frame, Outline, Colours, Names, Grid, Through, Eye, Refresh }

        internal static bool Toggle(bool value, Icon icon, string tooltip)
        {
            Rect rect = GUILayoutUtility.GetRect(28, 20, GUILayout.Width(28), GUILayout.Height(20));
            bool next = GUI.Toggle(rect, value, new GUIContent("", tooltip), EditorStyles.toolbarButton);
            Draw(rect, icon, next);
            return next;
        }

        internal static bool Button(Icon icon, string tooltip)
        {
            Rect rect = GUILayoutUtility.GetRect(28, 20, GUILayout.Width(28), GUILayout.Height(20));
            bool clicked = GUI.Button(rect, new GUIContent("", tooltip), EditorStyles.toolbarButton);
            Draw(rect, icon, false);
            return clicked;
        }

        internal static bool Button(Rect rect, Icon icon, string tooltip, GUIStyle style)
        {
            bool utility = icon == Icon.Eye || icon == Icon.Refresh;
            if (utility)
            {
                s_UtilityButtonStyle ??= new GUIStyle(EditorStyles.miniButton)
                {
                    fixedWidth = 0, fixedHeight = 0, alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(), margin = new RectOffset(), contentOffset = Vector2.zero
                };
                style = s_UtilityButtonStyle;
            }
            bool clicked = GUI.Button(rect, new GUIContent("", tooltip), style);
            if (utility) DrawUtilityIcon(rect, icon);
            else Draw(rect, icon, false);
            return clicked;
        }

        static GUIStyle s_UtilityButtonStyle;
        static Texture2D s_EyeIcon, s_RefreshIcon, s_SettingsIcon;

        static MGTerrainToolbarIcons()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ClearUtilityIcons;
            EditorApplication.quitting += ClearUtilityIcons;
        }

        static void ClearUtilityIcons()
        {
            if (s_EyeIcon != null) Object.DestroyImmediate(s_EyeIcon);
            if (s_RefreshIcon != null) Object.DestroyImmediate(s_RefreshIcon);
            if (s_SettingsIcon != null) Object.DestroyImmediate(s_SettingsIcon);
            s_EyeIcon = s_RefreshIcon = s_SettingsIcon = null;
        }

        internal static void DrawSettingsIcon(Rect button)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (s_SettingsIcon == null) s_SettingsIcon = CreateSettingsIcon();
            const float size = 16;
            Color previous = GUI.color;
            try
            {
                Color ink = button.Contains(Event.current.mousePosition) ? new Color(.35f, .8f, 1f)
                    : EditorGUIUtility.isProSkin ? new Color(.82f, .86f, .9f) : new Color(.22f, .28f, .34f);
                if (!GUI.enabled) ink.a *= .4f;
                GUI.color = previous * ink;
                GUI.DrawTexture(new Rect(button.center.x - size / 2, button.center.y - size / 2, size, size),
                    s_SettingsIcon, ScaleMode.StretchToFill, true);
            }
            finally { GUI.color = previous; }
        }

        static Texture2D CreateSettingsIcon()
        {
            // A solid six-tooth cog, with an open centre and no fine spokes.
            const int resolution = 64, samples = 4;
            var pixels = new Color32[resolution * resolution];
            for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    int coverage = 0;
                    for (int sy = 0; sy < samples; sy++)
                        for (int sx = 0; sx < samples; sx++)
                        {
                            Vector2 point = new Vector2((x + (sx + .5f) / samples) * 16 / resolution - 8,
                                (y + (sy + .5f) / samples) * 16 / resolution - 8);
                            float radius = point.magnitude;
                            bool solid = radius <= 5.25f;
                            for (int tooth = 0; tooth < 6 && !solid; tooth++)
                            {
                                float angle = (30 + tooth * 60) * Mathf.Deg2Rad;
                                float along = point.x * Mathf.Cos(angle) + point.y * Mathf.Sin(angle);
                                float across = -point.x * Mathf.Sin(angle) + point.y * Mathf.Cos(angle);
                                solid = along >= 4 && along <= 7 && Mathf.Abs(across) <= 1.65f;
                            }
                            if (solid && radius >= 2.35f) coverage++;
                        }
                    pixels[y * resolution + x] = new Color32(255, 255, 255, (byte)(coverage * 255 / (samples * samples)));
                }
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            { name = "Terrain Settings", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static void DrawUtilityIcon(Rect button, Icon icon)
        {
            if (Event.current.type != EventType.Repaint) return;
            ref Texture2D cached = ref (icon == Icon.Eye ? ref s_EyeIcon : ref s_RefreshIcon);
            if (cached == null) cached = CreateUtilityIcon(icon);
            const float size = 24;
            Rect image = new Rect(button.center.x - size * .5f, button.center.y - size * .5f, size, size);
            Color previous = GUI.color;
            try
            {
                Color ink = EditorGUIUtility.isProSkin ? new Color(.84f, .88f, .92f) : new Color(.22f, .28f, .34f);
                if (!GUI.enabled) ink.a *= .4f;
                GUI.color = previous * ink;
                // Draw in the same GUI coordinate space as the button, including scroll offsets.
                GUI.DrawTexture(image, cached, ScaleMode.StretchToFill, true);
            }
            finally { GUI.color = previous; }
        }

        static Texture2D CreateUtilityIcon(Icon icon)
        {
            const int resolution = 64;
            var segments = new System.Collections.Generic.List<(Vector2 a, Vector2 b)>();
            void Add(Vector2 a, Vector2 b) => segments.Add((a, b));
            if (icon == Icon.Eye)
            {
                for (int i = 0; i < 32; i++)
                {
                    float x0 = 1 + 14f * i / 32, x1 = 1 + 14f * (i + 1) / 32;
                    float y0 = 4.2f * Mathf.Sin(Mathf.PI * i / 32), y1 = 4.2f * Mathf.Sin(Mathf.PI * (i + 1) / 32);
                    Add(new Vector2(x0, 8 - y0), new Vector2(x1, 8 - y1));
                    Add(new Vector2(x0, 8 + y0), new Vector2(x1, 8 + y1));
                    float a0 = i * Mathf.PI / 16, a1 = (i + 1) * Mathf.PI / 16;
                    Add(new Vector2(8 + 2.6f * Mathf.Cos(a0), 8 + 2.6f * Mathf.Sin(a0)),
                        new Vector2(8 + 2.6f * Mathf.Cos(a1), 8 + 2.6f * Mathf.Sin(a1)));
                }
            }
            else
            {
                for (int i = 0; i < 48; i++)
                {
                    float a0 = (45 + 270f * i / 48) * Mathf.Deg2Rad, a1 = (45 + 270f * (i + 1) / 48) * Mathf.Deg2Rad;
                    Add(new Vector2(8 + 5.5f * Mathf.Cos(a0), 8 + 5.5f * Mathf.Sin(a0)),
                        new Vector2(8 + 5.5f * Mathf.Cos(a1), 8 + 5.5f * Mathf.Sin(a1)));
                }
                Add(new Vector2(9, 4), new Vector2(12, 4));
                Add(new Vector2(12, 4), new Vector2(12, 7));
            }
            var pixels = new Color32[resolution * resolution];
            for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    var point = new Vector2((x + .5f) * 16 / resolution, (resolution - y - .5f) * 16 / resolution);
                    float distance = float.MaxValue;
                    foreach (var segment in segments)
                    {
                        Vector2 edge = segment.b - segment.a;
                        Vector2 closest = segment.a + edge * Mathf.Clamp01(Vector2.Dot(point - segment.a, edge) / edge.sqrMagnitude);
                        distance = Mathf.Min(distance, Vector2.Distance(point, closest));
                    }
                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01((.65f - distance) * resolution / 16) * 255);
                    pixels[y * resolution + x] = new Color32(255, 255, 255, alpha);
                }
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            { name = "Terrain " + icon, hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static void Draw(Rect button, Icon icon, bool active)
        {
            if (Event.current.type != EventType.Repaint) return;
            var rect = new Rect(button.center.x - 8, button.center.y - 8, 16, 16);
            Color previous = Handles.color;
            Handles.BeginGUI();
            try
            {
                Color ink = active ? new Color(.35f, .8f, 1f)
                    : EditorGUIUtility.isProSkin ? new Color(.82f, .86f, .9f) : new Color(.22f, .28f, .34f);
                if (!GUI.enabled) ink.a *= .4f;
                Handles.color = ink;
                Vector2 P(float x, float y) => new Vector2(x, y);
                void Line(params Vector2[] points)
                {
                    var vertices = new Vector3[points.Length];
                    for (int i = 0; i < points.Length; i++) vertices[i] = new Vector3(rect.x + points[i].x, rect.y + points[i].y);
                    Handles.DrawAAPolyLine(1.6f, vertices);
                }
                switch (icon)
                {
                    case Icon.Locked:
                    case Icon.Unlocked:
                        Line(P(3, 7), P(13, 7), P(13, 14), P(3, 14), P(3, 7));
                        if (icon == Icon.Locked) Line(P(5, 7), P(5, 4), P(6, 2), P(10, 2), P(11, 4), P(11, 7));
                        else Line(P(8, 7), P(8, 4), P(9, 2), P(13, 2), P(14, 4));
                        Line(P(8, 10), P(8, 12));
                        break;
                    case Icon.Select:
                        Line(P(3, 1), P(3, 13), P(6, 10), P(9, 15), P(11, 14), P(8, 9), P(13, 9), P(3, 1));
                        break;
                    case Icon.Frame:
                        Line(P(1, 6), P(1, 1), P(6, 1));
                        Line(P(10, 1), P(15, 1), P(15, 6));
                        Line(P(15, 10), P(15, 15), P(10, 15));
                        Line(P(6, 15), P(1, 15), P(1, 10));
                        Line(P(6, 8), P(10, 8));
                        Line(P(8, 6), P(8, 10));
                        break;
                    case Icon.Outline:
                        Line(P(2, 2), P(14, 2), P(14, 14), P(2, 14), P(2, 2));
                        break;
                    case Icon.Colours:
                        Color[] colours = { new Color(.35f, .75f, 1), new Color(.5f, .85f, .45f), new Color(1, .65f, .35f), new Color(.85f, .5f, .9f) };
                        for (int i = 0; i < 4; i++)
                        {
                            Color colour = colours[i];
                            colour.a = GUI.enabled ? (active ? 1 : .5f) : .2f;
                            EditorGUI.DrawRect(new Rect(rect.x + 2 + (i % 2) * 7, rect.y + 2 + (i / 2) * 7, 5, 5), colour);
                        }
                        break;
                    case Icon.Names:
                        Line(P(2, 3), P(11, 3), P(15, 8), P(11, 13), P(2, 13), P(2, 3));
                        Line(P(5, 6), P(9, 6));
                        Line(P(7, 6), P(7, 10));
                        break;
                    case Icon.Grid:
                        for (int i = 0; i < 3; i++)
                        {
                            float n = 2 + i * 6;
                            Line(P(2, n), P(14, n));
                            Line(P(n, 2), P(n, 14));
                        }
                        break;
                    case Icon.Eye:
                        var eyelid = new Vector2[33];
                        for (int i = 0; i <= 16; i++)
                        {
                            float x = 1 + 14f * i / 16;
                            float curve = 4f * Mathf.Sin(Mathf.PI * i / 16);
                            eyelid[i] = P(x, 8 - curve);
                            if (i < 16) eyelid[32 - i] = P(x, 8 + curve);
                        }
                        Line(eyelid);
                        Handles.DrawWireDisc(new Vector3(rect.center.x, rect.center.y), Vector3.forward, 2.5f);
                        break;
                    case Icon.Refresh:
                        var arc = new Vector2[25];
                        for (int i = 0; i < arc.Length; i++)
                        {
                            float angle = (45 + 270f * i / (arc.Length - 1)) * Mathf.Deg2Rad;
                            arc[i] = P(8 + 5.5f * Mathf.Cos(angle), 8 + 5.5f * Mathf.Sin(angle));
                        }
                        Line(arc);
                        Line(P(10, 5), P(13, 5), P(13, 2));
                        break;
                    case Icon.Through:
                        Line(P(1, 8), P(4, 4), P(8, 2), P(12, 4), P(15, 8), P(12, 12), P(8, 14), P(4, 12), P(1, 8));
                        Handles.DrawWireDisc(new Vector3(rect.x + 8, rect.y + 8), Vector3.forward, 3);
                        break;
                }
            }
            finally { Handles.color = previous; Handles.EndGUI(); }
        }
    }
}
#endif
