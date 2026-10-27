using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed class StudioToolbarCustomization : EditorWindow
    {
        private const string DragKey = "MashBox.CustomToolbar.Function";
        private sealed class DragItem { internal StudioToolbarCustomization window; internal string key, toolbar; internal int index; }
        [SerializeField] private MashBoxStudioViewport owner;
        [SerializeField] private string selected;
        private List<StudioCustomToolbarDefinition> toolbars;
        private Vector2 leftScroll, itemScroll, commandScroll;
        private string search = "";
        private string pressedKey, pressedToolbar;
        private int pressedIndex;
        private Vector2 pressedPosition;
        private List<StudioModelEditor.ToolbarCommand> available;
        private void OnFocus() { toolbars = StudioCustomToolbars.Read(); available = null; }
        private void InvalidateCommands() { available = null; Repaint(); }
        private void OnDisable() { if (owner) owner.StudioToolbarStateChanged -= InvalidateCommands; }
        private void OnEnable() { titleContent = new GUIContent("Customize Toolbar Overlays"); minSize = new Vector2(760, 540); toolbars = StudioCustomToolbars.Read(); wantsMouseMove = true; if (owner) { owner.StudioToolbarStateChanged -= InvalidateCommands; owner.StudioToolbarStateChanged += InvalidateCommands; } }
        internal static void Open(MashBoxStudioViewport view, string toolbarId = null)
        {
            var window = GetWindow<StudioToolbarCustomization>(true, "Customize Toolbar Overlays", true);
            if (window.owner) window.owner.StudioToolbarStateChanged -= window.InvalidateCommands;
            window.owner = view ? view : StudioWindowOwnership.FindStudioOwner() as MashBoxStudioViewport;
            if (window.owner) { window.owner.StudioToolbarStateChanged -= window.InvalidateCommands; window.owner.StudioToolbarStateChanged += window.InvalidateCommands; }
            window.available = null;
            window.toolbars = StudioCustomToolbars.Read();
            if (toolbarId != null) window.selected = toolbarId;
            if (window.position.width < 760 || window.position.height < 540) window.position = new Rect(window.position.position, new Vector2(900, 680));
            StudioWindowOwnership.ShowUtility(window, window.owner);
        }
        private void OnDestroy() => StudioWindowOwnership.CloseOwnedWindows(this);
        private void Save() { StudioCustomToolbars.Save(toolbars); Repaint(); }
        internal void AddToolbar()
        {
            int number = 1; string name;
            do { name = "Custom Toolbar " + number++; } while (toolbars.Exists(x => x.name == name));
            var item = StudioCustomToolbars.Create(name); toolbars.Add(item); selected = item.id; Save();
            if (owner) owner.overlayCanvas.overlaysEnabled = true;
        }
        internal void Insert(string key, int index, string sourceToolbar = null, int sourceIndex = -1)
        {
            var target = toolbars.Find(x => x.id == selected); if (target == null || string.IsNullOrEmpty(key)) return;
            index = Mathf.Clamp(index, 0, target.commands.Count);
            if (sourceToolbar == target.id && sourceIndex >= 0 && sourceIndex < target.commands.Count)
            { target.commands.RemoveAt(sourceIndex); if (sourceIndex < index) index--; }
            target.commands.Insert(Mathf.Clamp(index, 0, target.commands.Count), key); Save();
        }
        private void OnGUI()
        {
            using (StudioTheme.Scope())
            {
                EditorGUI.DrawRect(new Rect(Vector2.zero, position.size), StudioTheme.Current.Panel);
                DrawControls();
            }
        }
        internal void DrawControls()
        {
            if (!owner) owner = StudioWindowOwnership.FindStudioOwner() as MashBoxStudioViewport;
            if (toolbars == null) toolbars = StudioCustomToolbars.Read();
            if (!toolbars.Exists(x => x.id == selected)) selected = toolbars.FirstOrDefault()?.id;
            var evt = Event.current;
            if (evt.type == EventType.MouseDrag && pressedKey != null && (GUIUtility.GUIToScreenPoint(evt.mousePosition) - pressedPosition).sqrMagnitude > 16)
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(DragKey, new DragItem { window = this, key = pressedKey, toolbar = pressedToolbar, index = pressedIndex });
                DragAndDrop.StartDrag("Toolbar function"); pressedKey = null; evt.Use();
            }
            var commands = available ?? (available = owner?.Studio?.ToolbarModelEditor?.ToolbarCommands(owner) ?? new List<StudioModelEditor.ToolbarCommand>());
            using (new EditorGUILayout.VerticalScope())
            {
                GUILayout.Space(8);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(8);
                    DrawToolbarList(); GUILayout.Space(10);
                    using (new EditorGUILayout.VerticalScope())
                    {
                        var target = toolbars.Find(x => x.id == selected);
                        if (target == null)
                        {
                            EditorGUILayout.HelpBox("Create a custom toolbar, then add functions from the list below.", MessageType.Info);
                            if (GUILayout.Button("Create Toolbar", GUILayout.Width(160))) AddToolbar();
                        }
                        else DrawContents(target, commands);
                        GUILayout.Space(10);
                        GUILayout.Label("Available functions", EditorStyles.boldLabel);
                        search = EditorGUILayout.TextField(search, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.textField);
                        using (var scroll = new EditorGUILayout.ScrollViewScope(commandScroll))
                        {
                            commandScroll = scroll.scrollPosition;
                            foreach (var command in commands)
                            {
                                if (!string.IsNullOrWhiteSpace(search) && command.Path.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                                var row = EditorGUILayout.GetControlRect(false, 24);
                                GUI.Label(new Rect(row.x + 4, row.y, row.width - 46, row.height), new GUIContent(command.Path, StudioCommandIcons.Get(command.Key), command.Path));
                                using (new EditorGUI.DisabledScope(target == null))
                                    if (GUI.Button(new Rect(row.xMax - 40, row.y + 2, 36, 20), new GUIContent("+", "Add to selected toolbar"))) Insert(command.Key, target.commands.Count);
                                BeginRowDrag(new Rect(row.x, row.y, row.width - 44, row.height), command.Key, null, -1);
                            }
                        }
                    }
                    GUILayout.Space(8);
                }
                GUILayout.Space(6);
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                { GUILayout.Label("Drag functions into Toolbar contents. Changes save automatically.", EditorStyles.miniLabel); GUILayout.FlexibleSpace(); if (GUILayout.Button("Close", EditorStyles.toolbarButton, GUILayout.Width(60))) Close(); }
            }
            if (evt.rawType == EventType.MouseUp) pressedKey = null;
        }
        private void DrawToolbarList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(200)))
            {
                GUILayout.Label("Custom toolbars", EditorStyles.boldLabel);
                using (var scroll = new EditorGUILayout.ScrollViewScope(leftScroll))
                {
                    leftScroll = scroll.scrollPosition;
                    foreach (var toolbar in toolbars.ToArray())
                    {
                        if (GUILayout.Toggle(selected == toolbar.id, toolbar.name, "Button", GUILayout.Height(28))) selected = toolbar.id;
                    }
                }
                if (GUILayout.Button("+ New Toolbar")) AddToolbar();
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(selected)))
                    if (GUILayout.Button("Delete Toolbar"))
                    {
                        var target = toolbars.Find(x => x.id == selected);
                        if (target != null && EditorUtility.DisplayDialog("Delete Custom Toolbar",
                            "Delete the toolbar \"" + target.name + "\" and its configured buttons?", "Delete", "Cancel"))
                        {
                            toolbars.RemoveAll(x => x.id == target.id);
                            selected = null;
                            Save();
                        }
                    }
            }
        }
        private void DrawContents(StudioCustomToolbarDefinition target, List<StudioModelEditor.ToolbarCommand> commands)
        {
            string renamed = EditorGUILayout.DelayedTextField("Toolbar name", target.name);
            if (!string.IsNullOrWhiteSpace(renamed) && renamed.Trim() != target.name) { target.name = renamed.Trim(); Save(); }
            GUILayout.Label("Toolbar contents", EditorStyles.boldLabel);
            itemScroll = EditorGUILayout.BeginScrollView(itemScroll, EditorStyles.helpBox, GUILayout.Height(Mathf.Max(140, position.height * .32f)));
            int insertion = target.commands.Count;
            for (int i = 0; i < target.commands.Count; i++)
            {
                var row = EditorGUILayout.GetControlRect(false, 26);
                var command = commands.Find(x => x.Key == target.commands[i]);
                GUI.Label(new Rect(row.x + 4, row.y, row.width - 100, row.height), new GUIContent("≡  " + (command?.Path ?? target.commands[i]), StudioCommandIcons.Get(command == null ? "Unavailable" : target.commands[i]), "Drag to reorder"));
                if (Event.current.mousePosition.y < row.center.y && insertion == target.commands.Count) insertion = i;
                int index = i;
                using (new EditorGUI.DisabledScope(i == 0))
                    if (GUI.Button(new Rect(row.xMax - 90, row.y + 3, 26, 20), "↑")) { Insert(target.commands[i], i - 1, target.id, i); GUIUtility.ExitGUI(); }
                using (new EditorGUI.DisabledScope(i == target.commands.Count - 1))
                    if (GUI.Button(new Rect(row.xMax - 60, row.y + 3, 26, 20), "↓")) { Insert(target.commands[i], i + 2, target.id, i); GUIUtility.ExitGUI(); }
                if (GUI.Button(new Rect(row.xMax - 30, row.y + 3, 26, 20), "×")) { target.commands.RemoveAt(index); Save(); GUIUtility.ExitGUI(); }
                BeginRowDrag(new Rect(row.x, row.y, row.width - 94, row.height), target.commands[i], target.id, i);
                if (DragAndDrop.GetGenericData(DragKey) is DragItem && insertion == i) EditorGUI.DrawRect(new Rect(row.x, row.y, row.width, 2), StudioTheme.Current.Accent);
            }
            if (target.commands.Count == 0) GUILayout.Label("Drop functions here, or use + below.", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(50));
            EditorGUILayout.EndScrollView();
            var area = GUILayoutUtility.GetLastRect();
            if ((Event.current.type == EventType.DragUpdated || Event.current.type == EventType.DragPerform) && area.Contains(Event.current.mousePosition) && DragAndDrop.GetGenericData(DragKey) is DragItem drag && drag.window == this)
            {
                DragAndDrop.visualMode = drag.toolbar == target.id ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Copy;
                if (Event.current.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); Insert(drag.key, insertion, drag.toolbar, drag.index); DragAndDrop.SetGenericData(DragKey, null); }
                Event.current.Use(); Repaint();
            }
        }
        private void BeginRowDrag(Rect row, string key, string toolbar, int index)
        {
            if (Event.current.type != EventType.MouseDown || Event.current.button != 0 || !row.Contains(Event.current.mousePosition)) return;
            pressedKey = key; pressedToolbar = toolbar; pressedIndex = index; pressedPosition = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            Event.current.Use();
        }
    }
}
