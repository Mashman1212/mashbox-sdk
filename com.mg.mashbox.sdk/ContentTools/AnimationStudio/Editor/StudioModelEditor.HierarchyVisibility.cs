using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private System.Collections.Generic.Dictionary<StudioModelDocument.Node, bool> hierarchyIsolationVisibility;
        private StudioModelDocument.Node hierarchyIsolatedNode;
        private void ClearHierarchyIsolation() { hierarchyIsolationVisibility = null; hierarchyIsolatedNode = null; }
        internal void ToggleHierarchyIsolation(int index)
        {
            if (!Document || index < 0 || index >= Document.nodes.Count) return;
            if (hierarchyIsolatedNode != null && !Document.nodes.Contains(hierarchyIsolatedNode)) ClearHierarchyIsolation();
            var clicked = Document.nodes[index];
            if (hierarchyIsolatedNode == clicked && hierarchyIsolationVisibility != null)
            {
                var previous = hierarchyIsolationVisibility;
                ClearHierarchyIsolation();
                ChangeHierarchyVisibility("Restore visibility", i => previous.TryGetValue(Document.nodes[i], out bool visible) ? visible : Document.nodes[i].active);
                return;
            }
            if (hierarchyIsolationVisibility == null)
                hierarchyIsolationVisibility = Document.nodes.ToDictionary(node => node, node => node.active);
            hierarchyIsolatedNode = clicked;
            var roots = objects.Contains(index) && objects.Count > 1 ? objects.ToArray() : new[] { index };
            ChangeHierarchyVisibility("Isolate visibility", i => roots.Any(root => IsHierarchyDescendant(i, root) || IsHierarchyDescendant(root, i)));
        }

        internal bool HandleHierarchyContext(Rect row, int index)
        {
            var evt = Event.current;
            if (evt == null || evt.alt || GUIUtility.hotControl != 0 || !row.Contains(evt.mousePosition) ||
                !(evt.type == EventType.ContextClick || (evt.type == EventType.MouseDown && evt.button == 1))) return false;
            EnsureHierarchyObjectMode();
            if (!objects.Contains(index)) SelectObject(index);
            HierarchyVisibilityMenu(index).DropDown(new Rect(evt.mousePosition, Vector2.zero));
            evt.Use(); return true;
        }

        internal StudioMenu HierarchyVisibilityMenu(int index)
        {
            var menu = new StudioMenu();
            AddVehiclePartContext(menu, index);
            AddUpdateSourceMenu(menu, index);
            bool visible = Document.nodes[index].active;
            menu.AddItem(new GUIContent(visible ? "Hide" : "Show"), false, () => Run(() => SetHierarchyVisibility(index, !visible)));
            menu.AddItem(new GUIContent("Isolate Visibility"), false, () => Run(() => IsolateHierarchyObject(index)));
            if (objects.Count > 0) menu.AddItem(new GUIContent("Isolate Selected"), false, () => Run(IsolateHierarchySelection));
            else menu.AddDisabledItem(new GUIContent("Isolate Selected"));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Show All Objects"), false, () => Run(ShowAllHierarchyObjects));
            menu.AddItem(new GUIContent("Hide All Objects"), false, () => Run(() => ChangeHierarchyVisibility("Hide all objects", i => false)));
            return menu;
        }

        private bool IsHierarchyDescendant(int index, int ancestor)
        {
            for (int node = index; node >= 0; node = Document.nodes[node].parent)
                if (node == ancestor) return true;
            return false;
        }

        internal bool HandleHierarchyVisibilityShortcut()
        {
            var evt = Event.current;
            if (evt == null || evt.type != EventType.KeyDown || GUIUtility.hotControl != 0 ||
                EditorGUIUtility.editingTextField || hierarchyRenameNode != null ||
                !StudioShortcuts.Pressed(StudioModelCommands.StableId("Select/Hide Selection"), evt)) return false;
            evt.Use(); Run(ToggleHierarchySelectionVisibility, false); return true;
        }
        private float hierarchyViewHeight = 300;
        internal bool HandleHierarchyFrameShortcut(Event evt)
        {
            if (evt == null || evt.type != EventType.KeyDown || EditorGUIUtility.editingTextField ||
                hierarchyRenameNode != null || GUIUtility.hotControl != 0 || !Document ||
                !(StudioShortcuts.Pressed(StudioShortcut.Frame, evt) ||
                  (evt.keyCode == KeyCode.F && !evt.alt && !evt.control && !evt.command && !evt.shift))) return false;
            var selected = objects.Where(i => i >= 0 && i < Document.nodes.Count).ToArray();
            if (selected.Length == 0 && Active >= 0 && Active < Document.nodes.Count) selected = new[] { Active };
            if (selected.Length > 0)
            {
                search = "";
                foreach (int index in selected)
                    for (int p = Document.nodes[index].parent; p >= 0; p = Document.nodes[p].parent)
                        collapsedHierarchy.Remove(Document.nodes[p]);
                hierarchyRows = null;
                var rows = HierarchyRows();
                var style = StudioTheme.Styles.hierarchyRow;
                float height = Mathf.Max(EditorGUIUtility.singleLineHeight, style.fixedHeight) + style.margin.vertical;
                int first = selected.Min(i => rows.IndexOf(i)), last = selected.Max(i => rows.IndexOf(i));
                float span = (last - first + 1) * height;
                int focus = selected.Contains(Active) ? rows.IndexOf(Active) : first;
                hierarchyScroll.y = Mathf.Max(0, span <= hierarchyViewHeight
                    ? first * height - (hierarchyViewHeight - span) * .5f
                    : focus * height - (hierarchyViewHeight - height) * .5f);
                repaint();
            }
            evt.Use(); return true;
        }
        internal void ToggleHierarchySelectionVisibility()
        {
            if (!Document) return;
            var selected = objects.Where(i => i >= 0 && i < Document.nodes.Count).ToArray();
            if (selected.Length == 0) return;
            FinishLiveTool();
            selected = objects.Where(i => i >= 0 && i < Document.nodes.Count).ToArray();
            if (selected.Length == 0) return;
            bool show = !selected.Any(IsVisibleHideTarget);
            ClearHierarchyIsolation();
            ChangeHierarchyVisibility(show ? "Show hierarchy selection" : "Hide hierarchy selection", i =>
                selected.Contains(i) ? show : show && selected.Any(child => IsHierarchyDescendant(child, i)) ? true : Document.nodes[i].active);
            // Keep row/component selection so another press toggles the same set.
            // Like the eye controls, this is document visibility with StudioUndo;
            // viewport component masks and last-hidden history are independent.
        }

        internal void SetHierarchyVisibility(int index, bool visible)
        {
            if (!Document || index < 0 || index >= Document.nodes.Count) return;
            ClearHierarchyIsolation();
            // Showing a child also enables its parent chain so it can actually appear.
            ChangeHierarchyVisibility(visible ? "Show object" : "Hide object", i =>
                i == index ? visible : visible && IsHierarchyDescendant(index, i) ? true : Document.nodes[i].active);
        }

        internal void IsolateHierarchyObject(int index)
        {
            if (!Document || index < 0 || index >= Document.nodes.Count) return;
            ClearHierarchyIsolation();
            // Keep the selected branch and the ancestors required to display it.
            ChangeHierarchyVisibility("Isolate visibility", i => IsHierarchyDescendant(i, index) || IsHierarchyDescendant(index, i));
        }

        internal void ShowAllHierarchyObjects() { ClearHierarchyIsolation(); ChangeHierarchyVisibility("Show all objects", i => true); }

        internal void IsolateHierarchySelection()
        {
            if (!Document) return;
            ClearHierarchyIsolation();
            var selection = objects.Where(i => i >= 0 && i < Document.nodes.Count).ToArray();
            if (selection.Length == 0) return;
            ChangeHierarchyVisibility("Isolate selected", i => selection.Any(selected =>
                IsHierarchyDescendant(i, selected) || IsHierarchyDescendant(selected, i)));
        }

        private void ChangeHierarchyVisibility(string label, Func<int, bool> visibility)
        {
            FinishLiveTool();
            if (!Document) return;
            var values = new bool[Document.nodes.Count]; bool changed = false;
            for (int i = 0; i < values.Length; i++) { values[i] = visibility(i); changed |= values[i] != Document.nodes[i].active; }
            if (!changed) return;
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(label); Undo.RegisterCompleteObjectUndo(Document, label);
            for (int i = 0; i < values.Length; i++)
            {
                Document.nodes[i].active = values[i];
                Transforms[i].gameObject.SetActive(values[i]);
            }
            Undo.CollapseUndoOperations(group); Changed();
        }
    }
}
