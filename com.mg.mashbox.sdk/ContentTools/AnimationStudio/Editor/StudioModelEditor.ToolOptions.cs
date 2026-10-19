using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private StudioModelToolOptions toolOptions;
        private GUIStyle toolIconStyle;
        internal void DrawViewportOptions(MashBoxStudioViewport view)
        {
            DrawOrientationPopup(false);
            EditorGUI.BeginChangeCheck();
            GUILayout.Space(8);
            var gridRect = GUILayoutUtility.GetRect(24, 24, GUILayout.Width(24), GUILayout.Height(24));
            bool grid = GUI.Toggle(gridRect, view.modelGridVisible, new GUIContent("", "Show the modeling ground grid"), StudioTheme.Styles.overlayButton);
            if (Event.current.type == EventType.Repaint)
            {
                var icon = new Rect(gridRect.center.x - 6, gridRect.center.y - 6, 13, 13);
                for (int line = 0; line < 4; line++)
                {
                    EditorGUI.DrawRect(new Rect(icon.x + line * 4, icon.y, 1, icon.height), StudioTheme.Current.Text);
                    EditorGUI.DrawRect(new Rect(icon.x, icon.y + line * 4, icon.width, 1), StudioTheme.Current.Text);
                }
            }
            if (EditorGUI.EndChangeCheck()) { view.modelGridVisible = grid; repaint(); toolOptions?.Repaint(); }
            DrawDisplayToggles();

        }
        internal void DrawToolStrip()
        {
            ToolButton(Tool.None, new GUIContent("↖", "Select (Q) · Double-click for options"));
            var separator = GUILayoutUtility.GetRect(32, 9, GUILayout.Width(32), GUILayout.Height(9));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(separator.x + 4, separator.y + 4, 24, 1), StudioTheme.Current.Border);
            ToolButton(Tool.Move, new GUIContent(EditorGUIUtility.IconContent("MoveTool").image, "Move (W) · Double-click for options"));
            ToolButton(Tool.Rotate, new GUIContent(EditorGUIUtility.IconContent("RotateTool").image, "Rotate (E) · Double-click for options"));
            ToolButton(Tool.Scale, new GUIContent(EditorGUIUtility.IconContent("ScaleTool").image, "Scale (R) · Double-click for options"));
        }
        private void ToolButton(Tool next, GUIContent content)
        {
            var rect = GUILayoutUtility.GetRect(32, 32, GUILayout.Width(32), GUILayout.Height(32));
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && evt.clickCount == 2 && rect.Contains(evt.mousePosition))
            { ActivateTool(next); OpenToolOptions(); evt.Use(); }
            if (toolIconStyle == null)
                toolIconStyle = new GUIStyle(GUIStyle.none) { alignment = TextAnchor.MiddleCenter, fontSize = 20 };
            var palette = StudioTheme.Current;
            toolIconStyle.normal.textColor = toolIconStyle.hover.textColor = toolIconStyle.active.textColor = palette.Text;
            toolIconStyle.onNormal.textColor = toolIconStyle.onHover.textColor = toolIconStyle.onActive.textColor = palette.Text;
            bool selected = cutTool == CutTool.None && tool == next;
            if (evt.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, selected ? palette.ToolSelection : rect.Contains(evt.mousePosition) ? palette.Hover : palette.ToolRail);
                if (selected)
                {
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2, rect.height), palette.Accent);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), palette.Border);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), palette.Border);
                }
            }
            if (GUI.Toggle(rect, selected, content, toolIconStyle) && !selected) ActivateTool(next);
            GUILayout.Space(3);
        }
        internal void ActivateTool(Tool next)
        {
            StopCutTool(); tool = next; pivot = false; repaint(); toolOptions?.Repaint();
        }
        internal void OpenToolOptions()
        {
            if (!toolOptions) toolOptions = ScriptableObject.CreateInstance<StudioModelToolOptions>();
            toolOptions.Bind(this); toolOptions.ShowUtility(); toolOptions.Focus();
        }
        internal void CloseToolOptions() { if (toolOptions) toolOptions.Close(); toolOptions = null; }
        internal void DrawToolOptions()
        {
            if (cutTool != CutTool.None)
            {
                GUILayout.Label(cutTool == CutTool.EdgeLoop ? "Insert Edge Loop" : "Multi-Cut", StudioTheme.Styles.boldLabel);
                EditorGUILayout.HelpBox(cutTool == CutTool.EdgeLoop
                    ? "Click-drag an edge to position the loop; release to apply. Shift snaps in 10% steps. Loops follow quad strips and stop at triangles, boundaries or non-manifold edges."
                    : "Click edges or vertices to build cuts across adjacent faces. Enter or right-click applies the path. Backspace removes the last segment. Ctrl temporarily inserts edge loops; Shift snaps edge points in 10% steps.", MessageType.Info);
                GUILayout.Label("Escape cancels a pending cut; Escape again or Q/W/E/R exits the tool.", StudioTheme.Styles.wordWrappedMiniLabel);
                return;
            }
            GUILayout.Label(tool == Tool.None ? "Selection Tool" : tool + " Tool", StudioTheme.Styles.boldLabel);
            EditorGUI.BeginChangeCheck();
            if (tool != Tool.None) DrawOrientationPopup(true);
            xray = EditorGUILayout.Toggle(new GUIContent("Through selection", "Select components behind visible surfaces."), xray);
            if (tool != Tool.None) weld = EditorGUILayout.Toggle(new GUIContent("Keep seams together", "Move coincident vertices across UV and normal splits together."), weld);
            if (EditorGUI.EndChangeCheck()) repaint();
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
            menu.AddItem(new GUIContent("Local"), local, () => { local = true; repaint(); toolOptions?.Repaint(); });
            menu.AddItem(new GUIContent("World"), !local, () => { local = false; repaint(); toolOptions?.Repaint(); });
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
                    value[axis] = EditorGUI.DelayedFloatField(field, new GUIContent(axis == 0 ? "X" : axis == 1 ? "Y" : "Z"), value[axis]);
                }
            }
            finally { EditorGUIUtility.labelWidth = previousLabelWidth; EditorGUI.indentLevel = previousIndent; }
            return value;
        }
        internal void SetObjectChannels(Vector3 position, Vector3 rotation, Vector3 scale, bool visible)
        {
            if (!Document || Active < 0) return;
            var node = Document.nodes[Active];
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Edit object channels");
            Undo.RegisterCompleteObjectUndo(Document, "Edit object channels");
            if (pivot && mode == ComponentMode.Object)
            {
                if (position != node.position)
                {
                    var localPosition = node.positionOffset + position;
                    Document.Repivot(Active, Transforms, Transforms[Active].parent ? Transforms[Active].parent.TransformPoint(localPosition) : localPosition);
                }
            }
            else { node.position = position; node.rotation = Quaternion.Euler(rotation); node.scale = scale; Document.ApplyTransform(Active, Transforms[Active]); }
            node.active = visible; Transforms[Active].gameObject.SetActive(visible);
            Undo.CollapseUndoOperations(group); Changed();
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
