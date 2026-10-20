using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal bool HandleHierarchyContext(Rect row, int index)
        {
            var evt = Event.current;
            if (evt == null || evt.alt || GUIUtility.hotControl != 0 || !row.Contains(evt.mousePosition) ||
                !(evt.type == EventType.ContextClick || (evt.type == EventType.MouseDown && evt.button == 1))) return false;
            mode = ComponentMode.Object;
            if (!objects.Contains(index)) SelectObject(index);
            HierarchyVisibilityMenu(index).DropDown(new Rect(evt.mousePosition, Vector2.zero));
            evt.Use(); return true;
        }

        internal StudioMenu HierarchyVisibilityMenu(int index)
        {
            var menu = new StudioMenu();
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

        internal void SetHierarchyVisibility(int index, bool visible)
        {
            if (!Document || index < 0 || index >= Document.nodes.Count) return;
            // Showing a child also enables its parent chain so it can actually appear.
            ChangeHierarchyVisibility(visible ? "Show object" : "Hide object", i =>
                i == index ? visible : visible && IsHierarchyDescendant(index, i) ? true : Document.nodes[i].active);
        }

        internal void IsolateHierarchyObject(int index)
        {
            if (!Document || index < 0 || index >= Document.nodes.Count) return;
            // Keep the selected branch and the ancestors required to display it.
            ChangeHierarchyVisibility("Isolate visibility", i => IsHierarchyDescendant(i, index) || IsHierarchyDescendant(index, i));
        }

        internal void ShowAllHierarchyObjects() => ChangeHierarchyVisibility("Show all objects", i => true);

        internal void IsolateHierarchySelection()
        {
            if (!Document) return;
            var selection = objects.Where(i => i >= 0 && i < Document.nodes.Count).ToArray();
            if (selection.Length == 0) return;
            ChangeHierarchyVisibility("Isolate selected", i => selection.Any(selected =>
                IsHierarchyDescendant(i, selected) || IsHierarchyDescendant(selected, i)));
        }

        private void ChangeHierarchyVisibility(string label, Func<int, bool> visibility)
        {
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
