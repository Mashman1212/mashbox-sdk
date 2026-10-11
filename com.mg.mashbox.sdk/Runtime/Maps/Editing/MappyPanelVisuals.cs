using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps.Editing
{
    // Shared by the Scene View overlay and the in-game Mappy panel.
    public static class MappyPanelVisuals
    {
        public const float PanelWidth = 240f, HeadingWidth = 72f, IconSize = 28f, RowHeight = 22f;
        public static readonly Color ToolSelection = new Color(.68f, .38f, .08f, .95f);
        public static readonly Color ActionSelection = new Color(.42f, .27f, .68f, .95f);
        public static readonly Color EditingSelection = new Color(.12f, .48f, .2f, .95f);
        public static readonly Color CategorySelection = new Color(.23f, .35f, .46f, 1f);
        public static readonly Color ShortcutColor = new Color(.35f, .67f, .96f, 1f);
        public static readonly Color CurrentToolColor = new Color(.9f, .58f, .24f, 1f);
        static readonly Dictionary<string, Texture2D> Icons = new Dictionary<string, Texture2D>();

        public static Texture2D Icon(string name)
        {
            if (!Icons.TryGetValue(name, out var icon) || !icon)
                Icons[name] = icon = CreateMashBoxIcon(name);
            return icon;
        }

        static Texture2D CreateMashBoxIcon(string iconName)
        {
            const int size = 32;
            var icon = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = iconName + " Icon",
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[size * size];
            var bright = new Color32(240, 244, 250, 255);
            var mid = new Color32(195, 203, 215, 255);

            switch (iconName)
            {
                case "MashBox.ActiveEditing":
                    DrawIconLine(pixels, size, new Vector2(6f, 6f), new Vector2(20f, 20f), 4.5f, bright);
                    DrawIconLine(pixels, size, new Vector2(5f, 9f), new Vector2(9f, 5f), 2.2f, mid);
                    DrawIconLine(pixels, size, new Vector2(24f, 20f), new Vector2(24f, 30f), 2.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(19f, 25f), new Vector2(29f, 25f), 2.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(20.5f, 21.5f), new Vector2(27.5f, 28.5f), 1.6f, mid);
                    DrawIconLine(pixels, size, new Vector2(20.5f, 28.5f), new Vector2(27.5f, 21.5f), 1.6f, mid);
                    DrawIconDot(pixels, size, new Vector2(24f, 25f), 2f, bright);
                    break;
                case "MashBox.GameplayGizmos":
                    DrawIconLine(pixels, size, new Vector2(3f, 16f), new Vector2(10f, 22f), 2f, mid);
                    DrawIconLine(pixels, size, new Vector2(10f, 22f), new Vector2(22f, 22f), 2f, mid);
                    DrawIconLine(pixels, size, new Vector2(22f, 22f), new Vector2(29f, 16f), 2f, mid);
                    DrawIconLine(pixels, size, new Vector2(3f, 16f), new Vector2(10f, 10f), 2f, mid);
                    DrawIconLine(pixels, size, new Vector2(10f, 10f), new Vector2(22f, 10f), 2f, mid);
                    DrawIconLine(pixels, size, new Vector2(22f, 10f), new Vector2(29f, 16f), 2f, mid);
                    DrawIconDot(pixels, size, new Vector2(16f, 16f), 5f, bright);
                    DrawIconDot(pixels, size, new Vector2(16f, 16f), 2f, new Color32(120, 160, 210, 255));
                    break;
                case "MashBox.Brush":
                    DrawIconLine(pixels, size, new Vector2(25f, 26f), new Vector2(14f, 15f), 4.5f, mid);
                    DrawIconLine(pixels, size, new Vector2(16f, 17f), new Vector2(11f, 12f), 6f, bright);
                    DrawIconLine(pixels, size, new Vector2(10f, 12f), new Vector2(5f, 6f), 2.4f, bright);
                    DrawIconLine(pixels, size, new Vector2(12f, 10f), new Vector2(7f, 5f), 2f, bright);
                    break;
                case "MashBox.Road":
                    DrawIconLine(pixels, size, new Vector2(5f, 4f), new Vector2(11f, 28f), 2.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(27f, 4f), new Vector2(21f, 28f), 2.2f, bright);
                    for (int y = 5; y < 28; y += 8)
                        DrawIconLine(pixels, size, new Vector2(16f, y), new Vector2(16f, y + 4f), 2f, mid);
                    break;
                case "MashBox.Spline":
                    DrawIconLine(pixels, size, new Vector2(4f, 22f), new Vector2(10f, 12f), 2.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(10f, 12f), new Vector2(18f, 20f), 2.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(18f, 20f), new Vector2(28f, 8f), 2.2f, bright);
                    DrawIconDot(pixels, size, new Vector2(4f, 22f), 2.3f, mid);
                    DrawIconDot(pixels, size, new Vector2(18f, 20f), 2.3f, mid);
                    DrawIconDot(pixels, size, new Vector2(28f, 8f), 2.3f, mid);
                    break;
                case "MashBox.SplineEdit":
                    DrawIconLine(pixels, size, new Vector2(4f, 22f), new Vector2(11f, 12f), 2.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(11f, 12f), new Vector2(20f, 19f), 2.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(20f, 19f), new Vector2(28f, 9f), 2.2f, bright);
                    DrawIconDot(pixels, size, new Vector2(11f, 12f), 3.2f, mid);
                    DrawIconLine(pixels, size, new Vector2(11f, 6f), new Vector2(11f, 18f), 1.8f, bright);
                    DrawIconLine(pixels, size, new Vector2(5f, 12f), new Vector2(17f, 12f), 1.8f, bright);
                    break;
                case "MashBox.NewSpline":
                    DrawIconLine(pixels, size, new Vector2(4f, 22f), new Vector2(10f, 13f), 2.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(10f, 13f), new Vector2(18f, 19f), 2.2f, bright);
                    DrawIconDot(pixels, size, new Vector2(4f, 22f), 2.2f, mid);
                    DrawIconDot(pixels, size, new Vector2(10f, 13f), 2.2f, mid);
                    DrawIconDot(pixels, size, new Vector2(18f, 19f), 2.2f, mid);
                    DrawIconLine(pixels, size, new Vector2(25f, 8f), new Vector2(25f, 20f), 2.4f, bright);
                    DrawIconLine(pixels, size, new Vector2(19f, 14f), new Vector2(31f, 14f), 2.4f, bright);
                    break;
                case "MashBox.Terrain":
                    DrawIconLine(pixels, size, new Vector2(3f, 7f), new Vector2(11f, 21f), 2.4f, bright);
                    DrawIconLine(pixels, size, new Vector2(11f, 21f), new Vector2(16f, 14f), 2.4f, bright);
                    DrawIconLine(pixels, size, new Vector2(16f, 14f), new Vector2(22f, 25f), 2.4f, bright);
                    DrawIconLine(pixels, size, new Vector2(22f, 25f), new Vector2(29f, 7f), 2.4f, bright);
                    DrawIconLine(pixels, size, new Vector2(3f, 7f), new Vector2(29f, 7f), 2f, mid);
                    break;
                case "MashBox.Decor":
                    DrawIconLine(pixels, size, new Vector2(16f, 5f), new Vector2(16f, 24f), 2.4f, bright);
                    DrawIconLine(pixels, size, new Vector2(16f, 17f), new Vector2(8f, 22f), 3f, mid);
                    DrawIconLine(pixels, size, new Vector2(16f, 14f), new Vector2(24f, 19f), 3f, mid);
                    DrawIconLine(pixels, size, new Vector2(7f, 5f), new Vector2(25f, 5f), 2f, bright);
                    break;
                case "MashBox.VertexPaint":
                    DrawIconLine(pixels, size, new Vector2(25f, 26f), new Vector2(14f, 15f), 4f, mid);
                    DrawIconLine(pixels, size, new Vector2(14f, 15f), new Vector2(8f, 9f), 4.8f, bright);
                    DrawIconDot(pixels, size, new Vector2(7f, 24f), 2f, bright);
                    DrawIconDot(pixels, size, new Vector2(14f, 26f), 2f, bright);
                    DrawIconDot(pixels, size, new Vector2(6f, 16f), 2f, bright);
                    break;
                case "MashBox.Splat":
                    DrawIconLine(pixels, size, new Vector2(16f, 27f), new Vector2(8f, 13f), 2.5f, bright);
                    DrawIconLine(pixels, size, new Vector2(16f, 27f), new Vector2(24f, 13f), 2.5f, bright);
                    DrawIconDot(pixels, size, new Vector2(16f, 12f), 8f, mid);
                    DrawIconDot(pixels, size, new Vector2(16f, 13f), 5.5f, bright);
                    break;
                case "MashBox.Loft":
                    DrawIconLine(pixels, size, new Vector2(4f, 24f), new Vector2(12f, 15f), 2f, bright);
                    DrawIconLine(pixels, size, new Vector2(12f, 15f), new Vector2(28f, 20f), 2f, bright);
                    DrawIconLine(pixels, size, new Vector2(4f, 17f), new Vector2(12f, 8f), 2f, mid);
                    DrawIconLine(pixels, size, new Vector2(12f, 8f), new Vector2(28f, 13f), 2f, mid);
                    break;
                case "MashBox.UV":
                    DrawIconLine(pixels, size, new Vector2(5f, 5f), new Vector2(27f, 5f), 2f, mid);
                    DrawIconLine(pixels, size, new Vector2(27f, 5f), new Vector2(27f, 27f), 2f, mid);
                    DrawIconLine(pixels, size, new Vector2(27f, 27f), new Vector2(5f, 27f), 2f, mid);
                    DrawIconLine(pixels, size, new Vector2(5f, 27f), new Vector2(5f, 5f), 2f, mid);
                    DrawIconLine(pixels, size, new Vector2(5f, 20f), new Vector2(14f, 11f), 2.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(14f, 11f), new Vector2(27f, 18f), 2.2f, bright);
                    break;
                case "MashBox.Sculpt":
                    DrawIconLine(pixels, size, new Vector2(24f, 25f), new Vector2(18f, 19f), 6f, mid);
                    DrawIconLine(pixels, size, new Vector2(18f, 19f), new Vector2(8f, 9f), 3.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(5f, 10f), new Vector2(10f, 5f), 2.2f, bright);
                    DrawIconLine(pixels, size, new Vector2(20f, 28f), new Vector2(28f, 20f), 2f, mid);
                    break;
                case "MashBox.Displace":
                    DrawIconLine(pixels, size, new Vector2(16f, 5f), new Vector2(16f, 27f), 2.5f, bright);
                    DrawIconLine(pixels, size, new Vector2(16f, 27f), new Vector2(11f, 21f), 2.5f, bright);
                    DrawIconLine(pixels, size, new Vector2(16f, 27f), new Vector2(21f, 21f), 2.5f, bright);
                    DrawIconLine(pixels, size, new Vector2(5f, 8f), new Vector2(27f, 8f), 2.5f, mid);
                    break;
                case "MashBox.Smooth":
                    DrawIconLine(pixels, size, new Vector2(3f, 12f), new Vector2(9f, 20f), 2.5f, bright);
                    DrawIconLine(pixels, size, new Vector2(9f, 20f), new Vector2(16f, 12f), 2.5f, bright);
                    DrawIconLine(pixels, size, new Vector2(16f, 12f), new Vector2(23f, 20f), 2.5f, bright);
                    DrawIconLine(pixels, size, new Vector2(23f, 20f), new Vector2(29f, 12f), 2.5f, bright);
                    break;
                case "MashBox.Flatten":
                    DrawIconLine(pixels, size, new Vector2(4f, 9f), new Vector2(28f, 9f), 3f, bright);
                    DrawIconLine(pixels, size, new Vector2(10f, 27f), new Vector2(10f, 15f), 2.3f, mid);
                    DrawIconLine(pixels, size, new Vector2(22f, 27f), new Vector2(22f, 15f), 2.3f, mid);
                    DrawIconLine(pixels, size, new Vector2(10f, 15f), new Vector2(7f, 19f), 2.3f, mid);
                    DrawIconLine(pixels, size, new Vector2(22f, 15f), new Vector2(25f, 19f), 2.3f, mid);
                    break;
                case "MashBox.SeamFit":
                    // Two surface profiles meeting at a shared vertex, with a
                    // lift arrow from the terrain toward the loft shoulder.
                    DrawIconLine(pixels, size, new Vector2(3f, 24f), new Vector2(12f, 24f), 2.4f, bright);
                    DrawIconLine(pixels, size, new Vector2(12f, 24f), new Vector2(20f, 16f), 2.4f, bright);
                    DrawIconLine(pixels, size, new Vector2(20f, 16f), new Vector2(29f, 16f), 2.4f, bright);
                    DrawIconLine(pixels, size, new Vector2(3f, 6f), new Vector2(11f, 9f), 2.2f, mid);
                    DrawIconLine(pixels, size, new Vector2(11f, 9f), new Vector2(20f, 16f), 2.2f, mid);
                    DrawIconDot(pixels, size, new Vector2(20f, 16f), 2.8f, bright);
                    DrawIconLine(pixels, size, new Vector2(7f, 12f), new Vector2(7f, 20f), 2f, bright);
                    DrawIconLine(pixels, size, new Vector2(7f, 20f), new Vector2(4f, 17f), 2f, bright);
                    DrawIconLine(pixels, size, new Vector2(7f, 20f), new Vector2(10f, 17f), 2f, bright);
                    break;
            }

            icon.SetPixels32(pixels);
            icon.Apply(false, true);
            return icon;
        }

        static void DrawIconLine(
            Color32[] pixels,
            int textureSize,
            Vector2 start,
            Vector2 end,
            float width,
            Color32 color)
        {
            Vector2 segment = end - start;
            float segmentLengthSquared = segment.sqrMagnitude;
            float radius = width * 0.5f;
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                    float amount = segmentLengthSquared > 0f
                        ? Mathf.Clamp01(Vector2.Dot(point - start, segment) / segmentLengthSquared)
                        : 0f;
                    if (Vector2.Distance(point, start + segment * amount) <= radius)
                        pixels[y * textureSize + x] = color;
                }
            }
        }

        static void DrawIconDot(
            Color32[] pixels,
            int textureSize,
            Vector2 center,
            float radius,
            Color32 color)
        {
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                    if (Vector2.Distance(point, center) <= radius)
                        pixels[y * textureSize + x] = color;
                }
            }
        }

    }
}
