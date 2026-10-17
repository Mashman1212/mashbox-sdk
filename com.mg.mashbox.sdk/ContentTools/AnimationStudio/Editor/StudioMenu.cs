using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // Keep menu rendering inside Unity instead of handing it to the OS menu theme.
    internal sealed class StudioMenu
    {
        internal sealed class Item
        {
            internal string Text;
            internal bool Checked, Enabled = true, Separator;
            internal Action Action;
            internal readonly List<Item> Children = new List<Item>();
        }
        internal readonly List<Item> Items = new List<Item>();
        internal void AddItem(GUIContent label, bool selected, Action action) => Add(label.text, selected, true, action);
        internal void AddDisabledItem(GUIContent label) => Add(label.text, false, false, null);
        private void Add(string path, bool selected, bool enabled, Action action)
        {
            var parts = path.Split('/'); var level = Items;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var branch = level.Find(item => item.Text == parts[i] && !item.Separator);
                if (branch == null) { branch = new Item { Text = parts[i] }; level.Add(branch); }
                level = branch.Children;
            }
            level.Add(new Item { Text = parts[parts.Length - 1], Checked = selected, Enabled = enabled, Action = action });
        }
        internal void AddSeparator(string path)
        {
            var level = Items;
            foreach (var part in path.Split('/'))
            {
                if (string.IsNullOrEmpty(part)) continue;
                var branch = level.Find(item => item.Text == part);
                if (branch == null) return;
                level = branch.Children;
            }
            level.Add(new Item { Separator = true, Enabled = false });
        }
        internal void DropDown(Rect rect)
        {
            if (Items.Count == 0) return;
            var screen = new Rect(GUIUtility.GUIToScreenPoint(rect.position), rect.size);
            StudioMenuPopup.Open(this, screen);
        }
    }

    internal sealed class StudioMenuPopup : EditorWindow
    {
        internal const float ColumnWidth = 224, RowHeight = 25, Padding = 4;
        private readonly List<List<StudioMenu.Item>> columns = new List<List<StudioMenu.Item>>();
        private readonly List<int> selected = new List<int>();
        private Rect anchor;
        private int activeColumn;
        private GUIStyle textStyle;
        private bool positioning;
        internal static StudioMenuPopup Open(StudioMenu menu, Rect screenAnchor)
        {
            foreach (var previous in Resources.FindObjectsOfTypeAll<StudioMenuPopup>()) previous.Close();
            var window = CreateInstance<StudioMenuPopup>();
            window.anchor = screenAnchor; window.columns.Add(menu.Items); window.selected.Add(-1);
            window.wantsMouseMove = true; window.PositionPopup();
            return window;
        }
        internal int ColumnCount => columns.Count;
        private Vector2 MenuSize()
        {
            float height = 0;
            foreach (var column in columns)
            {
                float current = Padding * 2;
                foreach (var item in column) current += item.Separator ? 7 : RowHeight;
                height = Mathf.Max(height, current);
            }
            return new Vector2(ColumnWidth * columns.Count, height);
        }
        private void SelectRow(int column, int row, bool expand)
        {
            activeColumn = column;
            if (selected[column] == row && (!expand || columns[column][row].Children.Count == 0 || columns.Count > column + 1)) return;
            selected[column] = row;
            while (columns.Count > column + 1) { columns.RemoveAt(columns.Count - 1); selected.RemoveAt(selected.Count - 1); }
            var item = columns[column][row];
            if (expand && item.Enabled && item.Children.Count > 0) { columns.Add(item.Children); selected.Add(-1); }
            var size = MenuSize();
            if ((position.size - size).sqrMagnitude > 1) PositionPopup();
            Repaint();
        }
        private void InvokeSelected()
        {
            int row = selected[activeColumn]; if (row < 0) return;
            var item = columns[activeColumn][row];
            if (!item.Enabled || item.Separator) return;
            if (item.Children.Count > 0)
            {
                SelectRow(activeColumn, row, true); activeColumn = columns.Count - 1; Step(1); return;
            }
            var action = item.Action; Close();
            if (action != null) EditorApplication.delayCall += () => action();
        }
        private void Step(int direction)
        {
            var items = columns[activeColumn]; int row = selected[activeColumn];
            if (row < 0 && direction < 0) row = 0;
            for (int i = 0; i < items.Count; i++)
            {
                row = (row + direction + items.Count) % items.Count;
                if (!items[row].Separator && items[row].Enabled) { SelectRow(activeColumn, row, false); break; }
            }
        }
        private void PositionPopup()
        {
            positioning = true;
            try
            {
                var size = MenuSize();
                var bounds = UnityEditorInternal.InternalEditorUtility.GetBoundsOfDesktopAtPoint(anchor.center);
                var origin = new Vector2(Mathf.Clamp(anchor.x, bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - size.x)),
                    anchor.yMax + size.y <= bounds.yMax ? anchor.yMax : Mathf.Max(bounds.yMin, anchor.y - size.y));
                position = new Rect(origin, size);
                ShowPopup(); Focus();
            }
            finally { positioning = false; }
        }
        private void OnLostFocus() { if (!positioning) Close(); }
        private void OnGUI() { using (StudioTheme.Scope()) DrawThemedGUI(); }
        private void DrawThemedGUI()
        {
            if (columns.Count == 0) { Close(); return; }
            if (textStyle == null) textStyle = new GUIStyle(StudioTheme.Styles.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            var evt = Event.current;
            if (evt.type == EventType.KeyDown)
            {
                switch (evt.keyCode)
                {
                    case KeyCode.Escape: Close(); break;
                    case KeyCode.DownArrow: Step(1); break;
                    case KeyCode.UpArrow: Step(-1); break;
                    case KeyCode.RightArrow:
                        if (selected[activeColumn] >= 0 && columns[activeColumn][selected[activeColumn]].Children.Count > 0) InvokeSelected();
                        break;
                    case KeyCode.LeftArrow:
                        if (activeColumn > 0) { activeColumn--; int row = selected[activeColumn]; selected[activeColumn] = -1; SelectRow(activeColumn, row, false); }
                        break;
                    case KeyCode.Return: case KeyCode.KeypadEnter: case KeyCode.Space: InvokeSelected(); break;
                    default: return;
                }
                evt.Use(); return;
            }
            EditorGUI.DrawRect(new Rect(Vector2.zero, position.size), StudioTheme.Current.Panel);
            int hitColumn = -1, hitRow = -1;
            for (int c = 0; c < columns.Count; c++)
            {
                float x = c * ColumnWidth, y = Padding;
                if (c > 0) EditorGUI.DrawRect(new Rect(x, 0, 1, position.height), StudioTheme.Current.Border);
                for (int r = 0; r < columns[c].Count; r++)
                {
                    var item = columns[c][r];
                    if (item.Separator) { EditorGUI.DrawRect(new Rect(x + 10, y + 3, ColumnWidth - 20, 1), StudioTheme.Current.Border); y += 7; continue; }
                    var rect = new Rect(x + Padding, y, ColumnWidth - Padding * 2, RowHeight); y += RowHeight;
                    if (rect.Contains(evt.mousePosition)) { hitColumn = c; hitRow = r; }
                    if (selected[c] == r && item.Enabled) EditorGUI.DrawRect(rect, StudioTheme.Current.Selection);
                    textStyle.normal.textColor = item.Enabled ? StudioTheme.Current.Text : StudioTheme.Current.Muted;
                    if (item.Checked) GUI.Label(new Rect(rect.x + 3, rect.y, 18, rect.height), "✓", textStyle);
                    GUI.Label(new Rect(rect.x + 24, rect.y, rect.width - 46, rect.height), item.Text, textStyle);
                    if (item.Children.Count > 0) GUI.Label(new Rect(rect.xMax - 18, rect.y, 16, rect.height), "›", textStyle);
                }
            }
            if (hitRow >= 0)
            {
                if (evt.type == EventType.MouseMove || evt.type == EventType.MouseDrag)
                { SelectRow(hitColumn, hitRow, true); evt.Use(); }
                else if (evt.type == EventType.MouseUp && evt.button == 0)
                { SelectRow(hitColumn, hitRow, true); InvokeSelected(); evt.Use(); }
            }
        }
    }
}
