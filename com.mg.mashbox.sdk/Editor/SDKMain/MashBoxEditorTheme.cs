#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.SDKMain
{
    public readonly struct MashBoxTheme
    {
        public readonly string Name;
        public readonly string Description;
        public readonly Color Primary;
        public readonly Color Secondary;
        public readonly Color Accent;
        public readonly Color Panel;

        public MashBoxTheme(string name, string description, Color primary, Color secondary, Color accent, Color panel)
        {
            Name = name;
            Description = description;
            Primary = primary;
            Secondary = secondary;
            Accent = accent;
            Panel = panel;
        }
    }

    public static class MashBoxEditorTheme
    {
        private const string PrefKey = "MashBoxSDK.UiTheme";

        private static readonly MashBoxTheme[] Themes =
        {
            new MashBoxTheme("Classic Blue", "Original cool blue SDK styling.", new Color(0.25f, 0.42f, 0.60f, 1f), new Color(0.18f, 0.28f, 0.40f, 1f), new Color(0.64f, 0.78f, 0.94f, 1f), new Color(0.28f, 0.48f, 0.72f, 0.28f)),
            new MashBoxTheme("Sage", "Soft green-gray panels and calm highlights.", new Color(0.40f, 0.48f, 0.36f, 1f), new Color(0.30f, 0.38f, 0.30f, 1f), new Color(0.82f, 0.88f, 0.78f, 1f), new Color(0.62f, 0.67f, 0.60f, 0.28f)),
            new MashBoxTheme("Graphite", "Neutral graphite with subtle silver accents.", new Color(0.34f, 0.36f, 0.38f, 1f), new Color(0.24f, 0.25f, 0.27f, 1f), new Color(0.76f, 0.78f, 0.80f, 1f), new Color(0.58f, 0.60f, 0.62f, 0.24f)),
            new MashBoxTheme("Plum", "Muted purple without going full nightclub.", new Color(0.42f, 0.32f, 0.52f, 1f), new Color(0.32f, 0.24f, 0.40f, 1f), new Color(0.82f, 0.70f, 0.92f, 1f), new Color(0.56f, 0.46f, 0.64f, 0.27f)),
            new MashBoxTheme("Copper", "Warm amber-copper controls.", new Color(0.56f, 0.38f, 0.24f, 1f), new Color(0.42f, 0.30f, 0.22f, 1f), new Color(0.94f, 0.72f, 0.48f, 1f), new Color(0.64f, 0.52f, 0.40f, 0.26f)),
            new MashBoxTheme("Teal", "Clear teal accents for tool-heavy layouts.", new Color(0.22f, 0.48f, 0.50f, 1f), new Color(0.18f, 0.34f, 0.38f, 1f), new Color(0.58f, 0.88f, 0.88f, 1f), new Color(0.42f, 0.66f, 0.66f, 0.26f)),
            new MashBoxTheme("Rose", "Quiet rose and mauve highlights.", new Color(0.54f, 0.34f, 0.42f, 1f), new Color(0.40f, 0.26f, 0.32f, 1f), new Color(0.94f, 0.70f, 0.78f, 1f), new Color(0.66f, 0.52f, 0.58f, 0.25f)),
            new MashBoxTheme("Indigo", "Deep indigo with crisp cool accents.", new Color(0.30f, 0.34f, 0.58f, 1f), new Color(0.22f, 0.26f, 0.42f, 1f), new Color(0.68f, 0.74f, 0.98f, 1f), new Color(0.46f, 0.50f, 0.72f, 0.27f)),
            new MashBoxTheme("Olive", "Earthy olive with restrained contrast.", new Color(0.44f, 0.44f, 0.28f, 1f), new Color(0.34f, 0.34f, 0.22f, 1f), new Color(0.86f, 0.84f, 0.58f, 1f), new Color(0.62f, 0.62f, 0.46f, 0.25f)),
            new MashBoxTheme("Crimson", "Dark red accents for a bolder SDK look.", new Color(0.52f, 0.24f, 0.26f, 1f), new Color(0.38f, 0.18f, 0.20f, 1f), new Color(0.96f, 0.58f, 0.58f, 1f), new Color(0.66f, 0.42f, 0.42f, 0.24f))
        };

        public static int SelectedIndex
        {
            get => Mathf.Clamp(EditorPrefs.GetInt(PrefKey, 1), 0, Themes.Length - 1);
            set => EditorPrefs.SetInt(PrefKey, Mathf.Clamp(value, 0, Themes.Length - 1));
        }

        public static MashBoxTheme Current => Themes[SelectedIndex];
        public static string[] Names => Array.ConvertAll(Themes, theme => theme.Name);
        public static string CurrentDescription => Current.Description;

        public static Color Primary(bool hovered = false)
        {
            return hovered ? Lighten(Current.Primary, EditorGUIUtility.isProSkin ? 0.10f : 0.06f) : Current.Primary;
        }

        public static Color Secondary(bool hovered = false)
        {
            return hovered ? Lighten(Current.Secondary, EditorGUIUtility.isProSkin ? 0.12f : 0.08f) : Current.Secondary;
        }

        public static Color Border(bool active)
        {
            var baseColor = active ? Current.Accent : Current.Primary;
            var alpha = active ? 0.92f : 0.22f;
            return WithAlpha(baseColor, alpha);
        }

        public static Color Text(bool active, bool hovered)
        {
            if (active)
                return Color.white;

            if (hovered)
                return EditorGUIUtility.isProSkin ? new Color(0.94f, 0.95f, 0.94f, 1f) : new Color(0.08f, 0.08f, 0.08f, 1f);

            return EditorGUIUtility.isProSkin ? new Color(0.78f, 0.80f, 0.80f, 1f) : new Color(0.18f, 0.18f, 0.18f, 1f);
        }

        public static Color SelectedFill()
        {
            return WithAlpha(Current.Secondary, EditorGUIUtility.isProSkin ? 0.78f : 0.56f);
        }

        public static Color SelectedBorder()
        {
            return WithAlpha(Current.Accent, EditorGUIUtility.isProSkin ? 0.55f : 0.42f);
        }

        public static Color SubtleBackground(float alpha)
        {
            return WithAlpha(Current.Secondary, alpha);
        }

        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        public static Color Lighten(Color color, float amount)
        {
            color.r = Mathf.Clamp01(color.r + amount);
            color.g = Mathf.Clamp01(color.g + amount);
            color.b = Mathf.Clamp01(color.b + amount);
            return color;
        }
    }
}
#endif
