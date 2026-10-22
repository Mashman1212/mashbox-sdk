using System;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioDragSensitivity
    {
        private static readonly float[] Ratios = { 1f, 2f, 3f, 4f };
        private static readonly GUIContent[] Icons = new GUIContent[4];
        private static readonly GUIContent Hit = new GUIContent();
        static StudioDragSensitivity()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ClearIcons;
            EditorApplication.quitting += ClearIcons;
        }
        private static void ClearIcons()
        {
            for (int i = 0; i < Icons.Length; i++)
            { if (Icons[i]?.image) UnityEngine.Object.DestroyImmediate(Icons[i].image); Icons[i] = null; }
        }
        internal static int State(float multiplier)
        {
            int state = 0;
            for (int i = 1; i < Ratios.Length; i++)
                if (Mathf.Abs(Ratios[i]-multiplier) < Mathf.Abs(Ratios[state]-multiplier)) state = i;
            return state;
        }
        internal static float Normalize(float multiplier) => Ratios[State(multiplier)];
        internal static bool DrawButton(Rect rect, float multiplier, out float next)
        {
            int state = State(multiplier); var icon = Icon(state);
            GUI.DrawTexture(rect, icon.image, ScaleMode.ScaleToFit, true); Hit.tooltip = icon.tooltip;
            bool clicked = GUI.Button(rect, Hit, GUIStyle.none);
            next = clicked ? Ratios[(state+1)%Ratios.Length] : multiplier;
            return clicked;
        }
        // Apply only around tool content. Native numeric fields read mouse delta
        // directly, so future Float/Double/Int fields inherit the ratio automatically.
        internal readonly struct DragScope : IDisposable
        {
            private readonly Event evt;
            private readonly Vector2 original;
            internal DragScope(float multiplier)
            {
                evt = Event.current; original = evt != null ? evt.delta : Vector2.zero;
                if (evt != null && evt.type == EventType.MouseDrag && GUIUtility.hotControl != 0)
                    evt.delta = original * Normalize(multiplier);
            }
            public void Dispose() { if (evt != null) evt.delta = original; }
        }
        internal static GUIContent Icon(int state)
        {
            if (Icons[state] != null) return Icons[state];
            // Render once at 2x resolution with coverage sampling for a crisp,
            // smooth circular gauge. Fill starts at six o'clock: bottom left, top left, top right, bottom right.
            const int size = 32, samples = 4;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
                name = "Studio drag sensitivity " + state, hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[size * size];
            float sweep = (state + 1) * Mathf.PI * .5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int coverage = 0;
                    for (int sy = 0; sy < samples; sy++)
                        for (int sx = 0; sx < samples; sx++)
                        {
                            float dx = x + (sx + .5f) / samples - size * .5f;
                            float dy = y + (sy + .5f) / samples - size * .5f;
                            float radius = Mathf.Sqrt(dx * dx + dy * dy);
                            float angle = Mathf.Repeat(Mathf.Atan2(dx, dy) - Mathf.PI, Mathf.PI * 2);
                            bool rim = radius >= 9 && radius <= 13;
                            bool fill = radius < 9 && angle <= sweep;
                            if (rim || fill) coverage++;
                        }
                    pixels[y * size + x] = new Color(.8f, .8f, .8f, coverage / (float)(samples * samples));
                }
            texture.SetPixels(pixels); texture.Apply(false, true);
            return Icons[state] = new GUIContent(texture, "Drag sensitivity: " + Ratios[state] + "x (click to cycle)");
        }
    }
}
