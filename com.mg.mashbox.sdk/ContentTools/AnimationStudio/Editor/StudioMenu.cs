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
            internal string Shortcut;
            internal string ToolbarKey;
            internal StudioShortcut? Command;
            internal bool ShortcutEligible;
            internal bool Checked, Enabled = true, Separator;
            internal Action Action;
            internal readonly List<Item> Children = new List<Item>();
        }
        internal readonly List<Item> Items = new List<Item>();
        private readonly string commandCategory;
        internal readonly bool CatalogOnly;
        internal StudioMenu(string commandCategory = null, bool catalogOnly = false)
        { this.commandCategory = commandCategory; CatalogOnly = catalogOnly; }
        internal void AddItem(GUIContent label, bool selected, Action action, string commandKey = null, bool shortcutEligible = true) => Add(label.text, selected, true, action, commandKey, shortcutEligible);
        internal void AddDisabledItem(GUIContent label, string commandKey = null, bool shortcutEligible = true) => Add(label.text, false, false, null, commandKey, shortcutEligible);
        private void Add(string path, bool selected, bool enabled, Action action, string commandKey, bool shortcutEligible)
        {
            // Shortcut alternatives may contain '/'; only the label defines the menu path.
            int shortcutStart = path.IndexOf('\t');
            string shortcut = shortcutStart >= 0 ? path.Substring(shortcutStart + 1) : "";
            string labelPath = shortcutStart >= 0 ? path.Substring(0, shortcutStart) : path;
            shortcutEligible &= commandCategory != "Display";
            StudioShortcut? command = shortcutEligible && commandCategory != null ? StudioModelCommands.Register(commandCategory, labelPath, commandKey) : null;
            if (command.HasValue) shortcut = StudioShortcuts.Label(command.Value);
            var parts = labelPath.Split('/'); var level = Items;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var branch = level.Find(item => item.Text == parts[i] && !item.Separator);
                if (branch == null) { branch = new Item { Text = parts[i] }; level.Add(branch); }
                level = branch.Children;
            }
            level.Add(new Item { Text = parts[parts.Length - 1], Shortcut = shortcut, Command = command, ShortcutEligible = shortcutEligible, ToolbarKey = commandCategory == null ? null : StudioModelCommands.ToolbarKey(commandCategory, labelPath, commandKey), Checked = selected, Enabled = enabled, Action = action });
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
        internal static bool MenuBarButton(EditorWindow owner, Rect rect, GUIContent content)
        {
            var evt = Event.current;
            bool open = StudioMenuPopup.IsMenuBarItemOpen(owner,content.text);
            bool switchMenu = !open && rect.Contains(evt.mousePosition) &&
                (evt.type == EventType.MouseMove || evt.type == EventType.MouseDrag) && StudioMenuPopup.HasMenuBar(owner);
            bool clicked = EditorGUI.DropdownButton(rect,content,FocusType.Passive,StudioTheme.Styles.menuBarButton);
            if (evt.type == EventType.Repaint && open)
                StudioTheme.Styles.menuBarButton.Draw(rect,content,rect.Contains(evt.mousePosition),true,true,false);
            if (!switchMenu && !clicked) return false;
            if (clicked && open) { StudioMenuPopup.CloseMenuBar(owner); return false; }
            if (switchMenu) evt.Use();
            return true;
        }
        internal void DropDown(Rect rect, EditorWindow menuBarOwner = null, string menuBarItem = null)
        {
            if (Items.Count == 0) return;
            var screen = new Rect(GUIUtility.GUIToScreenPoint(rect.position), rect.size);
            StudioMenuPopup.Open(this, screen,menuBarOwner,menuBarItem);
        }
    }

    internal sealed class StudioMenuPopup : EditorWindow
    {
        internal const float RowHeight = 25, Padding = 4;
        private readonly List<List<StudioMenu.Item>> columns = new List<List<StudioMenu.Item>>();
        private readonly List<int> selected = new List<int>();
        private Rect anchor;
        private int activeColumn;
        private GUIStyle textStyle, shortcutStyle;
        private readonly List<StudioMenuColumnPopup> submenus = new List<StudioMenuColumnPopup>();
        private bool positioning;
        private bool shown;
        private static StudioMenuPopup current;
        private EditorWindow menuBarOwner;
        private MashBoxStudioViewport inputOwner;
        private string menuBarItem;
        internal static bool HasMenuBar(EditorWindow owner) => owner && current && current.menuBarOwner == owner;
        internal static bool BlocksInput(MashBoxStudioViewport owner) => owner && current && current.inputOwner == owner;
        internal static void CloseInputMenu(MashBoxStudioViewport owner) { if (BlocksInput(owner)) current.Close(); }
        internal static bool IsMenuBarItemOpen(EditorWindow owner,string item) => HasMenuBar(owner) && current.menuBarItem == item;
        internal static void CloseMenuBar(EditorWindow owner) { if (HasMenuBar(owner)) current.Close(); }
        internal static StudioMenuPopup Open(StudioMenu menu, Rect screenAnchor, EditorWindow menuBarOwner = null, string menuBarItem = null)
        {
            if (menuBarOwner && HasMenuBar(menuBarOwner))
            {
                current.ReplaceMenu(menu,screenAnchor,menuBarItem);
                return current;
            }
            foreach (var previous in Resources.FindObjectsOfTypeAll<StudioMenuPopup>()) previous.Close();
            var window = CreateInstance<StudioMenuPopup>();
            current = window; window.menuBarOwner = menuBarOwner;
            window.inputOwner = menuBarOwner as MashBoxStudioViewport ?? focusedWindow as MashBoxStudioViewport;
            window.inputOwner?.SetMenuInputBlocked(true);
            window.wantsMouseMove = true; window.ReplaceMenu(menu,screenAnchor,menuBarItem);
            return window;
        }
        private void ReplaceMenu(StudioMenu menu, Rect screenAnchor, string item)
        {
            anchor=screenAnchor;menuBarItem=item;activeColumn=0;
            columns.Clear();selected.Clear();columns.Add(menu.Items);selected.Add(-1);
            PositionPopup();Repaint();RepaintMenuOwner();
        }
        private void OnDisable()
        {
            foreach (var submenu in submenus) if (submenu) submenu.Close();
            submenus.Clear();
            if (current == this) current=null;
            if (inputOwner) inputOwner.SetMenuInputBlocked(false);
            RepaintMenuOwner();
        }
        private void RepaintMenuOwner()
        {
            if(menuBarOwner is MashBoxStudioViewport studio)studio.RepaintUI();
            else if(menuBarOwner)menuBarOwner.Repaint();
        }
        internal int ColumnCount => columns.Count;
        private Vector2 ColumnSize(int index)
        {
            float width = 80, height = Padding * 2;
            var measure = new GUIStyle(EditorStyles.label) { fontSize = 12 };
            foreach (var item in columns[index])
            {
                height += item.Separator ? 7 : RowHeight;
                if (item.Separator) continue;
                float shortcut = string.IsNullOrEmpty(item.Shortcut) ? 0 : measure.CalcSize(new GUIContent(item.Shortcut)).x + 16;
                width = Mathf.Max(width, measure.CalcSize(new GUIContent(item.Text)).x + shortcut + 46 + Padding * 2);
            }
            return new Vector2(Mathf.Ceil(width), height);
        }
        private void SelectRow(int column, int row, bool expand)
        {
            activeColumn = column;
            if (selected[column] == row && (!expand || columns[column][row].Children.Count == 0 || columns.Count > column + 1)) return;
            selected[column] = row;
            while (columns.Count > column + 1) { columns.RemoveAt(columns.Count - 1); selected.RemoveAt(selected.Count - 1); }
            var item = columns[column][row];
            if (expand && item.Enabled && item.Children.Count > 0) { columns.Add(item.Children); selected.Add(-1); }
            PositionPopup();
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
                var size = ColumnSize(0);
                var bounds = UnityEditorInternal.InternalEditorUtility.GetBoundsOfDesktopAtPoint(anchor.center);
                var origin = new Vector2(Mathf.Clamp(anchor.x, bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - size.x)),
                    anchor.yMax + size.y <= bounds.yMax ? anchor.yMax : Mathf.Max(bounds.yMin, anchor.y - size.y));
                // ShowPopup creates the native window and may replace its initial
                // bounds. Apply the measured rectangle after creation, only once.
                if (!shown) { shown = true; ShowPopup(); }
                minSize = maxSize = size;
                var rootRect = new Rect(origin, size);
                position = rootRect;
                while (submenus.Count >= columns.Count)
                {
                    var last = submenus[submenus.Count - 1]; submenus.RemoveAt(submenus.Count - 1);
                    if (last) last.Close();
                }
                Rect parent = rootRect;
                for (int c = 1; c < columns.Count; c++)
                {
                    bool create = submenus.Count < c;
                    if (create)
                    {
                        var child = CreateInstance<StudioMenuColumnPopup>(); child.Owner = this; child.Column = c;
                        child.wantsMouseMove = true; submenus.Add(child);
                    }
                    float rowY = Padding;
                    for (int r = 0; r < selected[c - 1]; r++) rowY += columns[c - 1][r].Separator ? 7 : RowHeight;
                    var childSize = ColumnSize(c);
                    float x = parent.xMax;
                    if (x + childSize.x > bounds.xMax) x = parent.xMin - childSize.x;
                    var childPosition = new Vector2(Mathf.Clamp(x,bounds.xMin,Mathf.Max(bounds.xMin,bounds.xMax-childSize.x)),
                        Mathf.Clamp(parent.y + rowY - Padding,bounds.yMin,Mathf.Max(bounds.yMin,bounds.yMax-childSize.y)));
                    var submenu = submenus[c - 1];
                    if (create) submenu.ShowPopup();
                    submenu.minSize = submenu.maxSize = childSize;
                    var childRect = new Rect(childPosition,childSize);
                    submenu.position = childRect;
                    submenu.Repaint(); parent = childRect;
                }
                if (activeColumn == 0) Focus();
                else if (activeColumn <= submenus.Count) submenus[activeColumn - 1].Focus();
            }
            finally { positioning = false; }
        }
        private void OnLostFocus() => CheckFocusLater();
        internal void CheckFocusLater()
        {
            EditorApplication.delayCall += () => {
                if (!this || positioning || focusedWindow == this) return;
                foreach (var submenu in submenus) if (submenu && focusedWindow == submenu) return;
                Close();
            };
        }
        private void OnGUI() => DrawColumn(0);
        internal void DrawColumn(int column)
        { using (StudioTheme.Scope()) DrawThemedGUI(column); }
        private void DrawThemedGUI(int column)
        {
            if (columns.Count == 0) { Close(); return; }
            if (column >= columns.Count) return;
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
            var size = ColumnSize(column);
            EditorGUI.DrawRect(new Rect(Vector2.zero, size), StudioTheme.Current.Panel);
            StudioTheme.DrawPopupBorder(new Rect(Vector2.zero, size));
            int hitColumn = -1, hitRow = -1;
            {
                int c = column;
                float x = 0, y = Padding, columnWidth = size.x;
                for (int r = 0; r < columns[c].Count; r++)
                {
                    var item = columns[c][r];
                    if (item.Separator) { EditorGUI.DrawRect(new Rect(x + 10, y + 3, columnWidth - 20, 1), StudioTheme.Current.Border); y += 7; continue; }
                    var rect = new Rect(x + Padding, y, columnWidth - Padding * 2, RowHeight); y += RowHeight;
                    if (rect.Contains(evt.mousePosition)) { hitColumn = c; hitRow = r; }
                    if (selected[c] == r && item.Enabled) EditorGUI.DrawRect(rect, StudioTheme.Current.Selection);
                    textStyle.normal.textColor = item.Enabled ? StudioTheme.Current.Text : StudioTheme.Current.Muted;
                    if (item.Checked) GUI.Label(new Rect(rect.x + 3, rect.y, 18, rect.height), "✓", textStyle);
                    float shortcutWidth = string.IsNullOrEmpty(item.Shortcut) ? 0 : textStyle.CalcSize(new GUIContent(item.Shortcut)).x + 16;
                    GUI.Label(new Rect(rect.x + 24, rect.y, rect.width - 46 - shortcutWidth, rect.height), item.Text, textStyle);
                    if (shortcutWidth > 0)
                    {
                        if (shortcutStyle == null) shortcutStyle = new GUIStyle(textStyle) { alignment = TextAnchor.MiddleRight };
                        shortcutStyle.normal.textColor = StudioTheme.Current.Muted;
                        GUI.Label(new Rect(rect.xMax - 18 - shortcutWidth, rect.y, shortcutWidth, rect.height), item.Shortcut, shortcutStyle);
                    }
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
            if (evt.isMouse || evt.type == EventType.ScrollWheel || evt.type == EventType.ContextClick)
                evt.Use();
        }
    }

    internal sealed class StudioMenuColumnPopup : EditorWindow
    {
        internal StudioMenuPopup Owner;
        internal int Column;
        private void OnGUI() { if (Owner) Owner.DrawColumn(Column); else Close(); }
        private void OnLostFocus() { if (Owner) Owner.CheckFocusLater(); }
    }
}
