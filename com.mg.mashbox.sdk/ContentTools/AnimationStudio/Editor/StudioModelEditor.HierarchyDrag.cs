using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private const string HierarchyDragKey = "MashBoxStudio.HierarchyDrag";
        private StudioModelDocument.Node hierarchyRenameNode;
        private string hierarchyRenameText;
        private bool focusHierarchyRename;
        private Rect hierarchyRenameScreenRect;

        private void CommitHierarchyRenameOnClick()
        {
            if (hierarchyRenameNode == null || Event.current.type != EventType.MouseDown ||
                hierarchyRenameScreenRect.Contains(GUIUtility.GUIToScreenPoint(Event.current.mousePosition))) return;
            int index = Document ? Document.nodes.IndexOf(hierarchyRenameNode) : -1;
            string value = hierarchyRenameText;
            hierarchyRenameNode = null;
            GUI.FocusControl(null); EditorGUIUtility.editingTextField = false;
            if (index >= 0) Run(() => RenameObject(index, value));
        }

        internal void RenameObject(int index, string value)
        {
            var node = Document.nodes[index];
            if (!string.IsNullOrEmpty(node.anchorType)) return;
            value = (value ?? "").Trim();
            if (value == node.name) return;
            if (value.Length == 0 || value.IndexOfAny(new[] { '/', '\\', '\r', '\n', '\t' }) >= 0)
                throw new InvalidOperationException("Enter a nonempty object name without slashes or control characters.");
            if (Document.nodes.Any(other => other != node && other.parent == node.parent && other.name == value))
                throw new InvalidOperationException("An object with that name already exists under this parent.");
            Undo.RegisterCompleteObjectUndo(Document, "Rename object");
            node.name = value;
            Transforms[index].name = value;
            Changed(false);
        }

        private bool DrawHierarchyRename(Rect row, int index)
        {
            if (hierarchyRenameNode != Document.nodes[index]) return false;
            hierarchyRenameScreenRect = new Rect(GUIUtility.GUIToScreenPoint(row.position), row.size);
            var evt = Event.current;
            bool cancel = evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape;
            bool enter = evt.type == EventType.KeyDown && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter);
            bool outside = evt.type == EventType.MouseDown && !row.Contains(evt.mousePosition);
            if (cancel || enter || outside)
            {
                string value = hierarchyRenameText;
                hierarchyRenameNode = null;
                GUI.FocusControl(null); EditorGUIUtility.editingTextField = false;
                if (!cancel) Run(() => RenameObject(index, value));
                if (!outside) evt.Use();
                repaint();
                return false;
            }
            GUI.SetNextControlName("StudioHierarchyRename");
            hierarchyRenameText = EditorGUI.TextField(row, hierarchyRenameText);
            if (focusHierarchyRename)
            {
                EditorGUI.FocusTextInControl("StudioHierarchyRename");
                focusHierarchyRename = false;
            }
            return true;
        }
        private sealed class HierarchyDragPayload
        {
            internal StudioModelEditor owner;
            internal StudioModelDocument document;
            internal int[] selection;
            internal StudioModelDocument.Node[] nodes;
        }
        private int hierarchyDragControl, hierarchyDragButton;
        private Vector2 hierarchyDragStart;
        // Keep the OS drag loop separate from IMGUI event handling for editor tests.
        private Action<string> beginHierarchyDrag = DragAndDrop.StartDrag;
        private void CancelHierarchyDrag()
        {
            if (hierarchyDragControl != 0 && GUIUtility.hotControl == hierarchyDragControl) GUIUtility.hotControl = 0;
            hierarchyDragControl = 0;
            if (DragAndDrop.GetGenericData(HierarchyDragKey) is HierarchyDragPayload payload && payload.owner == this)
                DragAndDrop.SetGenericData(HierarchyDragKey, null);
        }
        private void HandleHierarchyDragCancellation()
        {
            var evt = Event.current;
            // DragExited can be sent when the pointer leaves this window during a
            // live drag. Keep the payload so it can be brought back and dropped.
            if (evt.type == EventType.MouseDown && DragAndDrop.GetGenericData(HierarchyDragKey) is HierarchyDragPayload)
                CancelHierarchyDrag();
            if (StudioShortcuts.Pressed(StudioShortcut.Cancel,evt) &&
                (hierarchyDragControl != 0 || DragAndDrop.GetGenericData(HierarchyDragKey) is HierarchyDragPayload))
            { CancelHierarchyDrag(); evt.Use(); repaint(); }
        }
        private void DrawHierarchyDragRow(Rect row, GUIContent content, GUIStyle style, int index)
        {
            if (DrawHierarchyRename(row, index)) return;
            int id = GUIUtility.GetControlID("StudioHierarchyRow".GetHashCode(), FocusType.Passive, row);
            var evt = Event.current;
            if (evt.type == EventType.Repaint) style.Draw(row, content, row.Contains(evt.mousePosition), GUIUtility.hotControl == id, false, false);
            HandleHierarchyDrop(row, index);
            if (evt.type == EventType.MouseDown && row.Contains(evt.mousePosition) && (evt.button == 0 || evt.button == 2))
            {
                if (evt.button == 0 && evt.clickCount == 2 && string.IsNullOrEmpty(Document.nodes[index].anchorType))
                {
                    CancelHierarchyDrag(); SelectObject(index);
                    hierarchyRenameNode = Document.nodes[index]; hierarchyRenameText = hierarchyRenameNode.name;
                    focusHierarchyRename = true;
                    evt.Use(); repaint(); return;
                }
                mode = ComponentMode.Object;
                if (!objects.Contains(index)) SelectObject(index, evt.shift, evt.control || evt.command);
                hierarchyDragControl = id; hierarchyDragButton = evt.button;
                hierarchyDragStart = GUIUtility.GUIToScreenPoint(evt.mousePosition);
                GUIUtility.hotControl = id; evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == id && hierarchyDragControl == id)
            {
                if ((GUIUtility.GUIToScreenPoint(evt.mousePosition) - hierarchyDragStart).sqrMagnitude >= 25)
                {
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>(); DragAndDrop.paths = Array.Empty<string>();
                    var selection = objects.ToArray();
                    DragAndDrop.SetGenericData(HierarchyDragKey, new HierarchyDragPayload { owner = this, document = Document,
                        selection = selection, nodes = selection.Select(i => Document.nodes[i]).ToArray() });
                    GUIUtility.hotControl = 0; hierarchyDragControl = 0;
                    beginHierarchyDrag(objects.Count == 1 ? Document.nodes[index].name : "MashBox Studio objects");
                }
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && evt.button == hierarchyDragButton && GUIUtility.hotControl == id)
            {
                if (row.Contains(evt.mousePosition) && hierarchyDragButton == 0)
                    SelectObject(index, evt.shift, evt.control || evt.command);
                GUIUtility.hotControl = 0; hierarchyDragControl = 0; evt.Use();
            }
        }
        private void HandleHierarchyDrop(Rect rect, int parent)
        {
            if (!(DragAndDrop.GetGenericData(HierarchyDragKey) is HierarchyDragPayload payload)) return;
            var evt = Event.current;
            if (!rect.Contains(evt.mousePosition)) return;
            string reason = "Drop into the source workspace.";
            bool valid = payload.owner == this && payload.document == Document && Document &&
                payload.selection.Select((i, n) => i >= 0 && i < Document.nodes.Count && ReferenceEquals(Document.nodes[i], payload.nodes[n])).All(same => same) &&
                Document.CanReparent(payload.selection, parent, Transforms, out reason);
            if (evt.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, valid ? new Color(.25f,.7f,1,.22f) : new Color(1,.25f,.2f,.15f));
                if (parent < 0) GUI.Label(rect, valid ? "Drop to move to root" : reason, StudioTheme.Styles.miniLabel);
            }
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;
            DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Rejected;
            if (evt.type == EventType.DragPerform)
            {
                if (valid)
                {
                    DragAndDrop.AcceptDrag();
                    Run(() => ReparentHierarchy(payload.selection, parent));
                }
                else { error = reason; repaint(); }
                CancelHierarchyDrag();
            }
            evt.Use();
        }
        internal void ReparentHierarchy(int[] selection, int parent)
        {
            Document.ReparentObjects(selection, parent, Transforms);
            CancelMarkingMenu(); EndPivotSnap(); StopCutTool(); pivot = false; mode = ComponentMode.Object; Components.Clear();
            if (parent >= 0)
                for (int p = parent; p >= 0; p = Document.nodes[p].parent) collapsedHierarchy.Remove(Document.nodes[p]);
            Rebuild(); Changed();
        }
    }
}
