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
        private readonly GUIContent toolOptionsHeader = new GUIContent();
        private ToolChannelPane ActiveChannelTool => bisectPane ? bisectPane : bevelToolPane ? bevelToolPane : extrudeToolPane ? extrudeToolPane
            : bridgeToolPane ? bridgeToolPane : softenToolPane ? softenToolPane : edgeFlowToolPane ? edgeFlowToolPane : selectionActionPane ? selectionActionPane : combineToolPane;
        internal void PrepareEmbeddedChannelTool()
        {
            FinishSelectionPaint(); StopTargetWeld(); StopCutTool();
            CameraOptionsVisible = LightingOptionsVisible = false; pivot = false;
        }
        internal string ToolOptionsTitle => ActiveChannelTool ? ActiveChannelTool.titleContent.text : CameraOptionsVisible ? "Camera Settings"
            : LightingOptionsVisible ? "Viewport Lighting"
            : targetWeldActive ? "Target Weld"
            : cutTool == CutTool.EdgeLoop ? "Insert Edge Loop"
            : cutTool != CutTool.None ? "Multi-Cut"
            : tool == Tool.None ? "Selection Tool" : tool + " Tool";
        internal int ToolbarTransformState => targetWeldActive || cutTool != CutTool.None ? 0 : tool == Tool.None ? 1 : tool == Tool.Move ? 2 : tool == Tool.Rotate ? 3 : tool == Tool.Scale ? 4 : 0;
        internal ComponentMode ToolbarComponentMode => mode;
        internal bool CanSetToolbarComponentMode(ComponentMode value) => value >= ComponentMode.Object && value <= ComponentMode.Face;
        internal void SetToolbarComponentMode(ComponentMode value)
        {
            if (!CanSetToolbarComponentMode(value)) return;
            StopCutTool(); SetComponentMode(value); repaint(); toolOptions?.Repaint();
        }
        internal bool ToolbarToolSelected(Tool value) => !targetWeldActive && cutTool == CutTool.None && tool == value;
        internal void ActivateTool(Tool next)
        {
            FinishSelectionPaint();
            FinishLiveTool();
            CloseLiveToolPanes(); CloseCombineToolWindow();
            StopTargetWeld(); StopCutTool(); CameraOptionsVisible = false; LightingOptionsVisible = false; tool = next; pivot = false; repaint(); toolOptions?.Repaint();
        }
        internal void OpenToolOptions()
        {
            if (toolOptions) toolOptions.Close(); toolOptions = null;
            if (CameraOptionsVisible || LightingOptionsVisible) { CloseLiveToolPanes(); CloseCombineToolWindow(); }
            FocusToolOptions?.Invoke(); repaint();
        }
        internal void DrawEmbeddedToolOptions()
        {
            using (new EditorGUILayout.VerticalScope(StudioTheme.Styles.modelPanel))
            {
                EditorGUIUtility.labelWidth = 115;
                var channel = ActiveChannelTool;
                if (channel && channel.IsEmbedded) channel.DrawEmbeddedHeader();
                else { toolOptionsHeader.text = ToolOptionsTitle.ToUpperInvariant(); StudioDragSensitivity.DrawPanelHeader(toolOptionsHeader, false); }
                GUILayout.Space(6);
                toolOptionsScroll = EditorGUILayout.BeginScrollView(toolOptionsScroll);
                try { if (channel && channel.IsEmbedded) channel.DrawEmbeddedContent(); else if (!channel) DrawToolOptions(); }
                finally { EditorGUILayout.EndScrollView(); }
                if (channel && channel.IsEmbedded) channel.DrawEmbeddedCommands();
            }
        }
        internal void CloseToolOptions() { if (toolOptions) toolOptions.Close(); toolOptions = null; CloseLiveToolPanes(); CloseCombineToolWindow(); }
        internal void DrawToolOptions()
        {
            if (CameraOptionsVisible) { DrawViewportCameraOptions(); return; }
            if (LightingOptionsVisible) { DrawViewportLightingOptions(); return; }
            if (targetWeldActive) { EditorGUILayout.HelpBox("Drag a vertex onto another vertex on the same mesh. The destination stays fixed. Release to weld; Escape cancels the drag, then exits the tool.", MessageType.Info); return; }
            if (cutTool != CutTool.None)
            {
                if (cutTool == CutTool.EdgeLoop) DrawLoopCountOptions();
                EditorGUILayout.HelpBox(cutTool == CutTool.EdgeLoop
                    ? "Drag to position the loops; release to apply. Ctrl/Cmd + wheel changes the loop count. Middle-click places evenly spaced centered loops. Shift snaps in 10% steps. Blue lines are an unapplied preview. Loops follow quad strips and stop at triangles, boundaries or non-manifold edges."
                    : "Start on an edge or vertex, then place straight segments on faces. Snap to pending vertices or lines, or finish on a perimeter. Return to the first vertex after at least three distinct vertices to close the loop. W applies and returns to Move. Drag from outside the mesh bounds to outside the bounds to slice through only this mesh. "+StudioShortcuts.Label(StudioShortcut.CommitCut)+" or "+StudioShortcuts.Label(StudioShortcut.MarkingMenu)+" applies the path. "+StudioShortcuts.Label(StudioShortcut.RemoveCut)+" removes the last segment. Ctrl/Cmd temporarily inserts edge loops.", MessageType.Info);
                GUILayout.Label(StudioShortcuts.Label(StudioShortcut.Cancel)+" cancels a pending cut; press again to exit, or choose a transform tool.", StudioTheme.Styles.wordWrappedMiniLabel);
                return;
            }
            EditorGUI.BeginChangeCheck();
            if (tool != Tool.None) DrawOrientationPopup(true);
            xray = EditorGUILayout.Toggle(new GUIContent("Through selection", "Select components behind visible surfaces."), xray);
            if (tool == Tool.Scale && mode != ComponentMode.Object)
                preventNegativeScale = EditorGUILayout.ToggleLeft(new GUIContent("Prevent negative scale", "Stop component scaling at a small positive size so vertices, edges and faces cannot flip across the pivot. Object scaling is unaffected."), preventNegativeScale);
            if (EditorGUI.EndChangeCheck()) { SaveDisplayConfiguration(); repaint(); }
            if (tool == Tool.Move || tool == Tool.Rotate || tool == Tool.Scale)
            {
                EditorGUI.BeginChangeCheck();
                bool locked = EditorGUILayout.Toggle(new GUIContent("Lock pivot", "Keep the pivot fixed in world space through Undo, selection changes and modeling actions. Edit Pivot can still reposition it."), PivotLocked);
                if (EditorGUI.EndChangeCheck()) SetPivotLocked(locked);
            }
            if (tool == Tool.Move)
            {
                using (new EditorGUI.DisabledScope(!MarkingActionEnabled(MarkingAction.EditPivot)))
                    if (GUILayout.Button(pivot ? "Finish Pivot Edit" : "Edit Pivot")) { pivot = !pivot; repaint(); }
            }
            if (!CanEditComponents()) EditorGUILayout.HelpBox("Component editing requires the skin's bind pose. Object transforms and UVs remain available.", MessageType.Info);
        }
        private void DrawOrientationPopup(bool labelled)
        {
            var content = new GUIContent(ToolbarOrientationLabel, "Transform handle orientation");
            var style = labelled ? StudioTheme.Styles.popup : StudioTheme.Styles.overlayDropdown;
            Rect rect = labelled ? EditorGUILayout.GetControlRect() : GUILayoutUtility.GetRect(content, style, GUILayout.Width(82));
            if (labelled) rect = EditorGUI.PrefixLabel(rect, new GUIContent("Orientation"));
            if (!EditorGUI.DropdownButton(rect, content, FocusType.Passive, style)) return;
            var menu = new StudioMenu();
            menu.AddItem(new GUIContent("Object"), local && !componentAxes, () => { SetToolbarLocalAxes(true); });
            menu.AddItem(new GUIContent("Component"), ToolbarComponentAxes, () => { SetToolbarComponentAxes(); });
            menu.AddItem(new GUIContent("World"), !local, () => { SetToolbarLocalAxes(false); });
            menu.DropDown(rect);
        }
        private static Vector3 DrawChannels(string label, Vector3 value)
        {
            var row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            float titleWidth = Mathf.Clamp(row.width * .4f, 100, 150);
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
            CloseLiveToolPanes(); CloseCombineToolWindow();
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
                if (!(pivot && mode == ComponentMode.Object && position != node.position))
                    Undo.RegisterCompleteObjectUndo(Document, "Edit object channels");
            }
            if (axisScrub) ApplyChannelScrub();
            else if (pivot && mode == ComponentMode.Object)
            {
                if (position != node.position)
                {
                    var localPosition = node.positionOffset + position;
                    MoveObjectPivots(Transforms[Active].parent ? Transforms[Active].parent.TransformPoint(localPosition) : localPosition);
                }
            }
            else { node.position = position; node.RotationChannels = rotation; node.scale = scale; Document.ApplyTransform(Active, Transforms[Active]); }
            if (pivot && mode == ComponentMode.Object) RememberLockedPivot(Transforms[Active].position, PivotOrientation);
            node.active = visible; Transforms[Active].gameObject.SetActive(visible);
            if (control == 0) FinishChannelEdit();
            Changed(pivot);
        }
    }
    internal sealed class StudioModelToolOptions : EditorWindow
    {
        private StudioModelEditor editor;
        internal void Bind(StudioModelEditor value) { editor = value; titleContent = new GUIContent(value.ToolOptionsTitle); minSize = new Vector2(300, 175); }
        private void OnGUI() { using (StudioTheme.Scope()) { EditorGUI.DrawRect(new Rect(Vector2.zero, position.size), StudioTheme.Current.Panel); DrawThemedGUI(); } }
        private void DrawThemedGUI()
        {
            if (editor == null) { GUILayout.Label("Open tool options from MashBox Studio."); return; }
            if (titleContent.text != editor.ToolOptionsTitle) titleContent = new GUIContent(editor.ToolOptionsTitle);
            EditorGUIUtility.labelWidth = 145;
            using (new EditorGUILayout.VerticalScope(StudioTheme.Styles.inspectorDefaultMargins)) editor.DrawToolOptions();
        }
        private void OnInspectorUpdate() { Repaint(); }
    }
}


