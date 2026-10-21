using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private static readonly GUIContent ChannelHeaderContent = new GUIContent("CHANNELS");
        private static readonly GUILayoutOption[] ChannelHeaderLayout = { GUILayout.ExpandWidth(true) };
        private static readonly GUIContent ChannelSensitivityHit = new GUIContent();
        private static readonly float[] ChannelSensitivities = { 1f, 2f, 3f, 4f };
        private static readonly GUIContent[] ChannelSensitivityIcons = new GUIContent[4];
        private static GUIContent ChannelSensitivityIcon(int state)
        {
            if (ChannelSensitivityIcons[state] != null) return ChannelSensitivityIcons[state];
            // Render once at 2x resolution with coverage sampling for a crisp,
            // smooth circular gauge. Fill starts at six o'clock: bottom left, top left, top right, bottom right.
            const int size = 32, samples = 4;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
                name = "Studio channel sensitivity " + state, hideFlags = HideFlags.HideAndDontSave,
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
            return ChannelSensitivityIcons[state] = new GUIContent(texture, "Channel drag sensitivity: " + ChannelSensitivities[state] + "x (click to cycle)");
        }
        private void DrawChannelHeader()
        {
            var style = StudioTheme.Styles.panelHeader;
            var rect = GUILayoutUtility.GetRect(0, 18, ChannelHeaderLayout);
            var button = new Rect(rect.xMax - 16, rect.center.y - 8, 16, 16);
            GUI.Label(new Rect(rect.x, rect.y, Mathf.Max(0, rect.width - 20), rect.height), ChannelHeaderContent, style);
            int state = 0;
            for (int i = 1; i < ChannelSensitivities.Length; i++)
                if (Mathf.Abs(ChannelSensitivities[i] - StudioSettings.Current.channelDragSensitivity) < Mathf.Abs(ChannelSensitivities[state] - StudioSettings.Current.channelDragSensitivity)) state = i;
            var icon = ChannelSensitivityIcon(state);
            GUI.DrawTexture(button, icon.image, ScaleMode.ScaleToFit, true);
            ChannelSensitivityHit.tooltip = icon.tooltip;
            if (GUI.Button(button, ChannelSensitivityHit, GUIStyle.none))
            {
                StudioSettings.Current.channelDragSensitivity = ChannelSensitivities[(state + 1) % ChannelSensitivities.Length];
                StudioSettings.Save();
            }
        }
        private int channelUndoGroup = -1, channelUndoControl, channelUndoNode;
        private bool channelUndoText, resetChannelFocus;
        private int channelScrubAxis;
        private string pendingChannelScrub;
        private float channelScrubDelta;
        private Vector3 DrawObjectChannels(string label, Vector3 value)
        {
            bool dragging = Event.current.type == EventType.MouseDrag;
            var before = value;
            EditorGUI.BeginChangeCheck();
            value = DrawChannels(label, value);
            if (EditorGUI.EndChangeCheck() && dragging && GUIUtility.hotControl != 0)
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    if (value[axis] == before[axis]) continue;
                    // FloatField already supplies a relative increment, including when
                    // Unity wraps the pointer. Compare with this event's Euler readout;
                    // other Euler components may legitimately change after a local turn.
                    channelScrubDelta = value[axis] - before[axis];
                    channelScrubAxis = axis; pendingChannelScrub = label;
                    break;
                }
            }
            return value;
        }
        private void ApplyChannelScrub()
        {
            var target = Transforms[Active];
            var axis = channelScrubAxis == 0 ? Vector3.right : channelScrubAxis == 1 ? Vector3.up : Vector3.forward;
            if (pendingChannelScrub == "Rotation")
            {
                var delta = Quaternion.AngleAxis(channelScrubDelta, axis);
                target.rotation = local ? target.rotation * delta : delta * target.rotation;
            }
            else target.position += (local ? target.rotation * axis : axis) * channelScrubDelta;
            Document.ReadTransform(Active, target);
        }
        private static readonly GUIContent[] ChannelAxes = { new GUIContent("X"), new GUIContent("Y"), new GUIContent("Z") };
        private static readonly Dictionary<string, string[]> ChannelNames = new Dictionary<string, string[]> {
            { "Position", new[] { "StudioChannel:Position:0", "StudioChannel:Position:1", "StudioChannel:Position:2" } },
            { "World Position", new[] { "StudioChannel:World Position:0", "StudioChannel:World Position:1", "StudioChannel:World Position:2" } },
            { "Rotation", new[] { "StudioChannel:Rotation:0", "StudioChannel:Rotation:1", "StudioChannel:Rotation:2" } },
            { "Scale", new[] { "StudioChannel:Scale:0", "StudioChannel:Scale:1", "StudioChannel:Scale:2" } }
        };
        private static bool FocusedTransformChannel => GUI.GetNameOfFocusedControl().StartsWith("StudioChannel:", StringComparison.Ordinal);
        private static int ChannelEditControl(out bool text)
        {
            text = false;
            if (Event.current == null) return 0;
            if (GUIUtility.hotControl != 0) return GUIUtility.hotControl;
            if (EditorGUIUtility.editingTextField && FocusedTransformChannel) { text = true; return GUIUtility.keyboardControl; }
            return 0;
        }
        private void FinishChannelEdit()
        {
            pendingChannelScrub = null;
            if (channelUndoGroup < 0) return;
            int group = channelUndoGroup; channelUndoGroup = -1;
            Undo.CollapseUndoOperations(group);
        }
        private void FinishInactiveChannelEdit()
        {
            if (channelUndoGroup < 0) return;
            int control = ChannelEditControl(out bool text);
            if (control != channelUndoControl || text != channelUndoText || Active != channelUndoNode) FinishChannelEdit();
        }
        internal void PerformWorkspaceHistory(bool redo)
        {
            FinishChannelEdit(); resetChannelFocus = true;
            if (redo) Undo.PerformRedo(); else Undo.PerformUndo();
        }
        private void HandleChannelHistory()
        {
            var evt = Event.current;
            bool focused = FocusedTransformChannel;
            if (resetChannelFocus)
            {
                if (focused) { GUI.FocusControl(null); EditorGUIUtility.editingTextField = false; }
                resetChannelFocus = false;
            }
            if (evt == null || !focused || GUIUtility.hotControl != 0) return;
            bool undo = StudioShortcuts.Pressed(StudioShortcut.Undo, evt);
            bool redo = StudioShortcuts.Pressed(StudioShortcut.Redo, evt);
            bool command = (evt.type == EventType.ValidateCommand || evt.type == EventType.ExecuteCommand) && evt.commandName == "UndoRedoPerformed";
            if (undo || redo)
            {
                GUI.FocusControl(null); EditorGUIUtility.editingTextField = false;
                PerformWorkspaceHistory(redo); evt.Use();
            }
            else if (command) { GUI.FocusControl(null); EditorGUIUtility.editingTextField = false; }
        }
    }
}
