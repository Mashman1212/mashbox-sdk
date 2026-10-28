using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // One icon source for the workspace selector and its menu rows.
    [InitializeOnLoad]
    internal static class StudioModeIcons
    {
        private static Texture2D painting;

        static StudioModeIcons()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Release;
            EditorApplication.quitting += Release;
        }

        internal static GUIContent Content(string mode, bool selector = false)
        {
            Texture icon;
            switch (mode)
            {
                case "Cloth": icon = EditorGUIUtility.IconContent("Cloth Icon").image; break;
                case "Painting": icon = Painting; break;
                case "Animation": icon = EditorGUIUtility.IconContent("AnimationClip Icon").image; break;
                case "Skinning": icon = EditorGUIUtility.IconContent("SkinnedMeshRenderer Icon").image; break;
                default: icon = EditorGUIUtility.IconContent("Mesh Icon").image; break;
            }
            return new GUIContent((selector ? " " : "") + mode, icon, "Switch studio mode");
        }

        private static Texture2D Painting
        {
            get
            {
                if (painting) return painting;
                // A diagonal brush: slim cyan handle, pale metal collar, broad loaded tip.
                // Supersample the vector silhouettes for clean 16-point and HiDPI display.
                var handle = new[] { new Vector2(7.6f, 7.2f), new Vector2(12.5f, 1.7f), new Vector2(13.9f, 1.2f), new Vector2(14.7f, 2), new Vector2(14.2f, 3.5f), new Vector2(9, 8.8f) };
                var collar = new[] { new Vector2(6, 8.5f), new Vector2(7.6f, 6.9f), new Vector2(10, 9.1f), new Vector2(8.4f, 10.8f) };
                var tip = new[] { new Vector2(5.8f, 8.9f), new Vector2(8, 11), new Vector2(7.5f, 12.3f), new Vector2(6.3f, 13.2f), new Vector2(1.5f, 14.4f), new Vector2(3.1f, 12.4f), new Vector2(3.5f, 10.8f), new Vector2(4.5f, 9.6f) };
                const int size = 32, samples = 4;
                var pixels = new Color[size * size];
                Color cyan = new Color(.35f, .8f, .96f), metal = new Color(.89f, .96f, 1);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Color sum = Color.clear;
                    for (int sy = 0; sy < samples; sy++)
                    for (int sx = 0; sx < samples; sx++)
                    {
                        var point = new Vector2((x + (sx + .5f) / samples) * 16 / size, (y + (sy + .5f) / samples) * 16 / size);
                        if (Inside(point, collar)) sum += metal;
                        else if (Inside(point, handle) || Inside(point, tip)) sum += cyan;
                    }
                    float alpha = sum.a / (samples * samples);
                    if (sum.a > 0) sum /= sum.a;
                    sum.a = alpha;
                    pixels[(size - 1 - y) * size + x] = sum;
                }
                painting = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { name = "Studio Painting Brush", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                painting.SetPixels(pixels); painting.Apply(false, true);
                return painting;
            }
        }

        private static bool Inside(Vector2 p, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                var a = polygon[i]; var b = polygon[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        private static void Release()
        {
            if (painting) Object.DestroyImmediate(painting);
            painting = null;
        }
    }
}
