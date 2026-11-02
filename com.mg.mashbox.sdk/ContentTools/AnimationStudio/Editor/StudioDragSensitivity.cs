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
        private static readonly GUILayoutOption[] HeaderLayout = { GUILayout.ExpandWidth(true) };
        internal static Rect DrawPanelHeader(GUIContent title, bool sensitivity = true)
        {
            var rect = GUILayoutUtility.GetRect(0, 18, HeaderLayout);
            GUI.Label(new Rect(rect.x, rect.y, Mathf.Max(0, rect.width - (sensitivity ? 20 : 0)), rect.height), title, StudioTheme.Styles.panelHeader);
            return new Rect(rect.xMax - 16, rect.center.y - 8, 16, 16);
        }
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
        internal sealed class IntegerScrubber
        {
            private int integerControl, integerStart;
            private double integerRemainder;
            internal void End()
            {
                if (integerControl == 0) return;
                if (GUIUtility.hotControl == integerControl) GUIUtility.hotControl = 0;
                integerControl = 0; integerRemainder = 0; EditorGUIUtility.SetWantsMouseJumping(0);
            }
            internal int Draw(Rect rect, GUIContent label, int value, int minimum, int maximum, float dragSensitivity)
            {
                int id = GUIUtility.GetControlID("StudioIntegerScrub".GetHashCode(), FocusType.Passive, rect);
                var evt = Event.current;
                var drag = new Rect(rect.x, rect.y, Mathf.Min(EditorGUIUtility.labelWidth, rect.width), rect.height);
                EditorGUIUtility.AddCursorRect(drag, MouseCursor.SlideArrow);
                if (integerControl != 0 && GUIUtility.hotControl != integerControl) End();
                if (GUI.enabled && evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt && drag.Contains(evt.mousePosition) && GUIUtility.hotControl == 0)
                {
                    integerControl = id; integerStart = value; integerRemainder = 0;
                    GUIUtility.hotControl = id; GUIUtility.keyboardControl = 0; EditorGUIUtility.editingTextField = false;
                    EditorGUIUtility.SetWantsMouseJumping(1); evt.Use();
                }
                if (integerControl == id && GUIUtility.hotControl == id)
                {
                    if (evt.type == EventType.MouseDrag)
                    {
                        int edited = StudioDragSensitivity.StepInteger(ref integerRemainder, value, StudioDragSensitivity.UnscaledDragDelta.x, dragSensitivity, minimum, maximum);
                        if (edited != value) { value = edited; GUI.changed = true; }
                        evt.Use();
                    }
                    else if (evt.rawType == EventType.MouseUp && evt.button == 0) { End(); evt.Use(); }
                    else if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
                    { if (value != integerStart) { value = integerStart; GUI.changed = true; } End(); evt.Use(); }
                }
                return EditorGUI.IntField(rect, label, value);
            }
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
