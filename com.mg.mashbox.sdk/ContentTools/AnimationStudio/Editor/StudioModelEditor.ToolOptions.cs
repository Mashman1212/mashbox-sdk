using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private StudioModelToolOptions toolOptions;
        internal EditorWindow ToolChannelPaneOwner
        {
            get
            {
                foreach (var viewport in Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>())
                    if (viewport.Studio?.ToolbarModelEditor == this) return viewport;
                return EditorWindow.focusedWindow;
            }
        }
        internal System.Action FocusToolOptions;
        private Vector2 toolOptionsScroll;
        internal int ToolbarTransformState => cutTool != CutTool.None ? 0 : tool == Tool.None ? 1 : tool == Tool.Move ? 2 : tool == Tool.Rotate ? 3 : tool == Tool.Scale ? 4 : 0;
        internal ComponentMode ToolbarComponentMode => mode;
        internal bool CanSetToolbarComponentMode(ComponentMode value) => value == ComponentMode.Object || CanEditComponents();
        internal void SetToolbarComponentMode(ComponentMode value)
        {
            if (!CanSetToolbarComponentMode(value)) return;
            StopCutTool(); SetComponentMode(value); repaint(); toolOptions?.Repaint();
        }
        internal bool ToolbarToolSelected(Tool value) => cutTool == CutTool.None && tool == value;
        internal void ActivateTool(Tool next)
        {
            FinishLiveTool();
            StopCutTool(); tool = next; pivot = false; repaint(); toolOptions?.Repaint();
        }
        internal void OpenToolOptions()
        {
            CloseToolOptions(); FocusToolOptions?.Invoke(); repaint();
        }
        internal void DrawEmbeddedToolOptions()
        {
            using (new EditorGUILayout.VerticalScope(StudioTheme.Styles.modelPanel))
            {
                EditorGUIUtility.labelWidth = 115;
                GUILayout.Label("TOOL OPTIONS", StudioTheme.Styles.panelHeader);
                toolOptionsScroll = EditorGUILayout.BeginScrollView(toolOptionsScroll);
                try { DrawToolOptions(); }
                finally { EditorGUILayout.EndScrollView(); }
            }
        }
        internal void CloseToolOptions() { if (toolOptions) toolOptions.Close(); toolOptions = null; }
        internal void DrawToolOptions()
        {
            if (cutTool != CutTool.None)
            {
                GUILayout.Label(cutTool == CutTool.EdgeLoop ? "Insert Edge Loop" : "Multi-Cut", StudioTheme.Styles.boldLabel);
                EditorGUILayout.HelpBox(cutTool == CutTool.EdgeLoop
                    ? "Click-drag an edge to position the loop; release to apply. Shift snaps in 10% steps. Loops follow quad strips and stop at triangles, boundaries or non-manifold edges."
                    : "Click edges or vertices to build cuts across adjacent faces. "+StudioShortcuts.Label(StudioShortcut.CommitCut)+" or "+StudioShortcuts.Label(StudioShortcut.MarkingMenu)+" applies the path. "+StudioShortcuts.Label(StudioShortcut.RemoveCut)+" removes the last segment. Ctrl/Cmd inserts edge loops; Shift snaps in 10% steps.", MessageType.Info);
                GUILayout.Label(StudioShortcuts.Label(StudioShortcut.Cancel)+" cancels a pending cut; press again to exit, or choose a transform tool.", StudioTheme.Styles.wordWrappedMiniLabel);
                return;
            }
            GUILayout.Label(tool == Tool.None ? "Selection Tool" : tool + " Tool", StudioTheme.Styles.boldLabel);
            EditorGUI.BeginChangeCheck();
            if (tool != Tool.None) DrawOrientationPopup(true);
            xray = EditorGUILayout.Toggle(new GUIContent("Through selection", "Select components behind visible surfaces."), xray);
            if (tool == Tool.Scale && mode != ComponentMode.Object)
                preventNegativeScale = EditorGUILayout.ToggleLeft(new GUIContent("Prevent negative scale", "Stop component scaling at a small positive size so vertices, edges and faces cannot flip across the pivot. Object scaling is unaffected."), preventNegativeScale);
            if (EditorGUI.EndChangeCheck()) { SaveDisplayConfiguration(); repaint(); }
            if (tool == Tool.Move)
            {
                using (new EditorGUI.DisabledScope(!MarkingActionEnabled(MarkingAction.EditPivot)))
                    if (GUILayout.Button(pivot ? "Lock Pivot" : "Edit Pivot")) { pivot = !pivot; repaint(); }
            }
            if (!CanEditComponents()) EditorGUILayout.HelpBox("Component editing requires the skin's bind pose. Object transforms and UVs remain available.", MessageType.Info);
        }
        private void DrawOrientationPopup(bool labelled)
        {
            var content = new GUIContent(local ? "Local" : "World", "Transform handle orientation");
            var style = labelled ? StudioTheme.Styles.popup : StudioTheme.Styles.overlayDropdown;
            Rect rect = labelled ? EditorGUILayout.GetControlRect() : GUILayoutUtility.GetRect(content, style, GUILayout.Width(82));
            if (labelled) rect = EditorGUI.PrefixLabel(rect, new GUIContent("Orientation"));
            if (!EditorGUI.DropdownButton(rect, content, FocusType.Passive, style)) return;
            var menu = new StudioMenu();
            menu.AddItem(new GUIContent("Local"), local, () => { SetToolbarLocalAxes(true); });
            menu.AddItem(new GUIContent("World"), !local, () => { SetToolbarLocalAxes(false); });
            menu.DropDown(rect);
        }
        private static Vector3 DrawChannels(string label, Vector3 value)
        {
            var row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            float titleWidth = Mathf.Clamp(row.width * .25f, 60, 82);
            EditorGUI.LabelField(new Rect(row.x, row.y, titleWidth, row.height), label);
            float axisWidth = (row.width - titleWidth - 8) / 3;
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            int previousIndent = EditorGUI.indentLevel;
            try
            {
                EditorGUIUtility.labelWidth = 13;
                EditorGUI.indentLevel = 0;
                for (int axis = 0; axis < 3; axis++)
                {
                    var field = new Rect(row.x + titleWidth + axis * (axisWidth + 4), row.y, axisWidth, row.height);
                    GUI.SetNextControlName(ChannelNames[label][axis]);
                    // Limit the readout, not the stored transform. Merely painting
                    // or focusing a field must not round the document's values.
                    float displayed = (float)System.Math.Round(value[axis], 4, System.MidpointRounding.AwayFromZero);
                    EditorGUI.BeginChangeCheck();
                    bool scrubbing = Event.current.type == EventType.MouseDrag && GUIUtility.hotControl != 0;
                    float edited = EditorGUI.FloatField(field, ChannelAxes[axis], displayed);
                    if (EditorGUI.EndChangeCheck())
                        value[axis] = scrubbing ? value[axis] + (edited - displayed) * StudioSettings.Current.channelDragSensitivity : edited;
                }
            }
            finally { EditorGUIUtility.labelWidth = previousLabelWidth; EditorGUI.indentLevel = previousIndent; }
            return value;
        }
        internal void SetObjectChannels(Vector3 position, Vector3 rotation, Vector3 scale, bool visible)
        {
            FinishLiveTool();
            if (!Document || Active < 0) return;
            var node = Document.nodes[Active];
            bool axisScrub = !pivot && (pendingChannelScrub == "Position" || pendingChannelScrub == "Rotation");
            if (!axisScrub && position == node.position && (pivot || (rotation == node.RotationChannels && scale == node.scale)) && visible == node.active) return;
            int control = ChannelEditControl(out bool text);
            if (channelUndoGroup >= 0 && (control == 0 || control != channelUndoControl || text != channelUndoText || channelUndoNode != Active)) FinishChannelEdit();
            if (channelUndoGroup < 0)
            {
                Undo.IncrementCurrentGroup(); channelUndoGroup = Undo.GetCurrentGroup();
                channelUndoControl = control; channelUndoText = text; channelUndoNode = Active;
                Undo.SetCurrentGroupName("Edit object channels");
                Undo.RegisterCompleteObjectUndo(Document, "Edit object channels");
            }
            if (axisScrub) ApplyChannelScrub();
            else if (pivot && mode == ComponentMode.Object)
            {
                if (position != node.position)
                {
                    var localPosition = node.positionOffset + position;
                    Document.Repivot(Active, Transforms, Transforms[Active].parent ? Transforms[Active].parent.TransformPoint(localPosition) : localPosition);
                }
            }
            else { node.position = position; node.RotationChannels = rotation; node.scale = scale; Document.ApplyTransform(Active, Transforms[Active]); }
            node.active = visible; Transforms[Active].gameObject.SetActive(visible);
            if (control == 0) FinishChannelEdit();
            Changed(pivot);
        }
    }
    internal sealed class StudioModelToolOptions : EditorWindow
    {
        private StudioModelEditor editor;
        internal void Bind(StudioModelEditor value) { editor = value; titleContent = new GUIContent("Tool Options"); minSize = new Vector2(300, 175); }
        private void OnGUI() { using (StudioTheme.Scope()) { EditorGUI.DrawRect(new Rect(Vector2.zero, position.size), StudioTheme.Current.Panel); DrawThemedGUI(); } }
        private void DrawThemedGUI()
        {
            if (editor == null) { GUILayout.Label("Open tool options from MashBox Studio."); return; }
            EditorGUIUtility.labelWidth = 145;
            using (new EditorGUILayout.VerticalScope(StudioTheme.Styles.inspectorDefaultMargins)) editor.DrawToolOptions();
        }
        private void OnInspectorUpdate() { Repaint(); }
    }
}


