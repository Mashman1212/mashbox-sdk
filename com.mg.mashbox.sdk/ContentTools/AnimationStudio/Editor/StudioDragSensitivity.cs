using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioDragSensitivity
    {
        private static readonly float[] Ratios = { .1f, 1f, 2f, 4f };
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
        // Numeric fields round each drag increment using their drag sensitivity.
        // Scaling only Event.delta leaves that rounding coarse and erases fine
        // movements. Scale the native sensitivity (and therefore its precision)
        // temporarily, restoring it before another window/control can use it.
        private static readonly FieldInfo NumericDragSensitivity = typeof(EditorGUI).GetField(
            "s_DragSensitivity", BindingFlags.Static | BindingFlags.NonPublic);
        internal static int StepInteger(ref double remainder, int value, float delta, float multiplier, int minimum, int maximum)
        {
            // Twelve logical pixels per step at 1x, independent of value size,
            // pointer acceleration and native integer-field rounding.
            remainder += delta * (double)Normalize(multiplier) / 12d;
            if ((value==minimum && remainder<0) || (value==maximum && remainder>0))
            { remainder=0; return value; }
            // Event deltas are floats: an exact reverse can otherwise stop a
            // few millionths of a pixel short of an integer threshold.
            double whole = Math.Truncate(remainder + Math.Sign(remainder)*1e-7);
            if (whole == 0) return value;
            double next = Math.Max(minimum, Math.Min(maximum, value + whole));
            remainder = next != value + whole ? 0 : remainder - whole;
            if (Math.Abs(remainder)<1e-7) remainder=0;
            return (int)next;
        }
        internal static Vector2 UnscaledDragDelta { get; private set; }
        internal readonly struct DragScope : IDisposable
        {
            private readonly Event evt;
            private readonly Vector2 original, previousDelta;
            private readonly double nativeSensitivity;
            private readonly bool nativeScaled;
            internal DragScope(float multiplier)
            {
                evt = Event.current; original = evt != null ? evt.delta : Vector2.zero;
                previousDelta = UnscaledDragDelta; UnscaledDragDelta = original;
                nativeSensitivity = 0; nativeScaled = false;
                if (evt == null || evt.type != EventType.MouseDrag || GUIUtility.hotControl == 0) return;
                float ratio = Normalize(multiplier);
                if (ratio == 1) return;
                if (NumericDragSensitivity != null && NumericDragSensitivity.FieldType == typeof(double))
                {
                    nativeSensitivity = (double)NumericDragSensitivity.GetValue(null);
                    NumericDragSensitivity.SetValue(null, nativeSensitivity * ratio);
                    nativeScaled = true;
                }
                else evt.delta = original * ratio; // Compatibility with older editor implementations.
            }
            public void Dispose()
            {
                if (nativeScaled) NumericDragSensitivity.SetValue(null, nativeSensitivity);
                if (evt != null) evt.delta = original;
                UnscaledDragDelta = previousDelta;
            }
        }
        internal static GUIContent Icon(int state)
        {
            if (Icons[state] != null) return Icons[state];
            // Render once at 2x resolution with coverage sampling for a crisp,
            // smooth circular gauge. Fill grows clockwise from the top-left quarter through the top half to a full circle; fuller means faster.
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
                            float angle = Mathf.Repeat(Mathf.Atan2(dy, -dx), Mathf.PI * 2);
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
