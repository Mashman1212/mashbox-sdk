using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private static readonly GUIContent ChannelHeaderContent = new GUIContent("CHANNELS");
        private static readonly GUIContent ChannelVisibilityContent = new GUIContent("", "Show or hide this object");
        private void DrawChannelHeader()
        {
            var button = StudioDragSensitivity.DrawPanelHeader(ChannelHeaderContent);
            if (StudioDragSensitivity.DrawButton(button, StudioSettings.Current.channelDragSensitivity, out float next))
            {
                StudioSettings.Current.channelDragSensitivity = next;
                StudioSettings.Save();
            }
        }
        private bool transformChannelsExpanded = true;
        private static readonly GUIContent TransformChannelHeader = new GUIContent("Transform", EditorGUIUtility.IconContent("Transform Icon").image);
        private bool DrawTransformChannelHeader()
        {
            GUILayout.Space(4);
            var rect=EditorGUILayout.GetControlRect(false,20);
            var background = new Rect(rect.x-4,rect.y,rect.width+8,rect.height);
            EditorGUI.DrawRect(background,Color.Lerp(StudioTheme.Current.Panel,StudioTheme.Current.Button,.5f));
            bool expanded=EditorGUI.Foldout(rect,transformChannelsExpanded,TransformChannelHeader,true,StudioTheme.Styles.transformHeader);
            if (expanded != transformChannelsExpanded)
            {
                transformChannelsExpanded=expanded;
                StudioPreferenceStore.SetFloat("Model.TransformChannelsExpanded",expanded ? 1 : 0);
                repaint();
            }
            return transformChannelsExpanded;
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
            if (bisectPreview != null || bisectDragging || bisectQueued) { CancelBisectPreview(); return; }
            if (livePreview != null || previewQueued) { CancelLiveTool(); return; }
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
