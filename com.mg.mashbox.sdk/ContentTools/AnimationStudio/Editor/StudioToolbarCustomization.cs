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
        [SerializeField] private bool radial;
        private List<StudioCustomToolbarDefinition> ReadDefinitions()=>radial?StudioRadialMenus.Read():StudioCustomToolbars.Read();
        private string Kind=>radial?"Radial Menu":"Toolbar";
        private StudioControlsPreferences embeddedHost;
        private void Refresh(){if(embeddedHost)embeddedHost.Repaint();else base.Repaint();}
        internal static void OpenRadial(MashBoxStudioViewport view)=>StudioControlsPreferences.OpenCustomization(true,view);
        internal static StudioToolbarCustomization CreateEmbedded(StudioControlsPreferences host,MashBoxStudioViewport view,bool radialMode,string presetId=null)
        {
            var editor=CreateInstance<StudioToolbarCustomization>();editor.hideFlags=HideFlags.HideAndDontSave;
            editor.embeddedHost=host;editor.radial=radialMode;editor.SetOwner(view);
            editor.toolbars=editor.ReadDefinitions();editor.available=null;editor.selected=presetId;
            return editor;
        }
        private void SetOwner(MashBoxStudioViewport view)
        {
            if(owner==view)return;
            if(owner)owner.StudioToolbarStateChanged-=InvalidateCommands;
            owner=view;if(owner)owner.StudioToolbarStateChanged+=InvalidateCommands;available=null;
        }
        internal void ReloadEmbedded(){ClearRowDrag();toolbars=ReadDefinitions();available=null;Refresh();}
        internal void CancelInput()=>ClearRowDrag();
        private List<StudioCustomToolbarDefinition> toolbars;
        private Vector2 leftScroll, itemScroll, commandScroll;
        private string search = "";
        private string pressedKey, pressedToolbar;
        private int pressedIndex;
        private bool rowDragActive;
        private void ClearRowDrag()
        {
            rowDragActive=false;pressedKey=null;
            if(DragAndDrop.GetGenericData(DragKey) is DragItem item && item.window==this)DragAndDrop.SetGenericData(DragKey,null);
        }
        private void OnLostFocus() { ClearRowDrag(); Refresh(); }
        private Vector2 pressedPosition;
        private List<StudioModelEditor.ToolbarCommand> available;
        private void OnFocus() { toolbars = ReadDefinitions(); available = null; }
        private void RadialFilesChanged(){if(!radial)return;ClearRowDrag();toolbars=ReadDefinitions();available=null;Refresh();}
        private void InvalidateCommands() { available = null; Refresh(); }
        private void OnDisable() { StudioRadialMenus.Changed-=RadialFilesChanged; ClearRowDrag(); if (owner) owner.StudioToolbarStateChanged -= InvalidateCommands; }
        private void OnEnable() { StudioRadialMenus.Changed-=RadialFilesChanged;StudioRadialMenus.Changed+=RadialFilesChanged; titleContent = new GUIContent(radial?"Customize Radial Menus":"Customize Toolbar Overlays"); minSize = new Vector2(760, 540); toolbars = ReadDefinitions(); wantsMouseMove = true; if (owner) { owner.StudioToolbarStateChanged -= InvalidateCommands; owner.StudioToolbarStateChanged += InvalidateCommands; } }
        internal static void Open(MashBoxStudioViewport view,string toolbarId=null)=>StudioControlsPreferences.OpenCustomization(false,view,toolbarId);
        private void OnDestroy() => StudioWindowOwnership.CloseOwnedWindows(this);
        private void Save() { if(radial)StudioRadialMenus.Save(toolbars);else StudioCustomToolbars.Save(toolbars); available=null;Refresh(); }
        internal void AddToolbar()
        {
            int number = 1; string name;
            do { name = (radial?"Radial Menu ":"Custom Toolbar ") + number++; } while (toolbars.Exists(x => x.name == name));
            var item = radial?StudioRadialMenus.Create(name):StudioCustomToolbars.Create(name); toolbars.Add(item); selected = item.id; Save();
            if (owner && !radial) owner.overlayCanvas.overlaysEnabled = true;
        }
        internal void Insert(string key, int index, string sourceToolbar = null, int sourceIndex = -1)
        {
            var target = toolbars.Find(x => x.id == selected); if (target == null || string.IsNullOrEmpty(key)) return;
            index = Mathf.Clamp(index, 0, target.commands.Count);
            if (sourceToolbar == target.id && sourceIndex >= 0 && sourceIndex < target.commands.Count)
            { target.commands.RemoveAt(sourceIndex); if (sourceIndex < index) index--; }
            target.commands.Insert(Mathf.Clamp(index, 0, target.commands.Count), key); Save();
        }
        private bool redirectQueued;
        private void OnGUI()
        {
            // Previously saved standalone windows migrate to the corresponding tab.
            // Embedded controllers never create or show a native window.
            if(embeddedHost||redirectQueued)return;
            redirectQueued=true;
            EditorApplication.delayCall+=()=>{if(!this)return;StudioControlsPreferences.OpenCustomization(radial,owner,selected);Close();};
        }
        internal void DrawControls()
        {
            if (!owner) SetOwner(StudioWindowOwnership.FindStudioOwner() as MashBoxStudioViewport);
            if (toolbars == null) toolbars = ReadDefinitions();
            if (!toolbars.Exists(x => x.id == selected)) selected = toolbars.FirstOrDefault()?.id;
            var evt = Event.current;
            if(evt.rawType==EventType.MouseUp || evt.type==EventType.DragExited ||
                evt.type==EventType.KeyDown&&evt.keyCode==KeyCode.Escape)ClearRowDrag();
            if (evt.type == EventType.MouseDrag && pressedKey != null && (GUIUtility.GUIToScreenPoint(evt.mousePosition) - pressedPosition).sqrMagnitude > 16)
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(DragKey, new DragItem { window = this, key = pressedKey, toolbar = pressedToolbar, index = pressedIndex });
                DragAndDrop.StartDrag("Toolbar function"); rowDragActive=true; pressedKey = null; evt.Use();
            }
            var commands = available ?? (available = (radial?owner?.Studio?.ToolbarModelEditor?.RadialCommands(owner):owner?.Studio?.ToolbarModelEditor?.ToolbarCommands(owner)) ?? new List<StudioModelEditor.ToolbarCommand>());
            using (new EditorGUILayout.VerticalScope())
            {
                GUILayout.Space(8);
                using (new EditorGUILayout.HorizontalScope())
                {
                    const float listSidePadding=8;
                    // Preferences already contributes six pixels to the leading
                    // gutter. Balance the total visible inset, not just this scope.
                    GUILayout.Space(embeddedHost?listSidePadding-6:listSidePadding);
                    DrawToolbarList();
                    GUILayout.Space(listSidePadding);
                    var divider=GUILayoutUtility.GetRect(1,1,GUILayout.Width(1),GUILayout.ExpandHeight(true));
                    EditorGUI.DrawRect(divider,new Color(.42f,.42f,.42f,1));
                    GUILayout.Space(4);
                    using (new EditorGUILayout.VerticalScope())
                    {
                        var target = toolbars.Find(x => x.id == selected);
                        if (target == null)
                        {
                            EditorGUILayout.HelpBox("Create a "+Kind.ToLowerInvariant()+", then add functions from the list below.", MessageType.Info);
                            if (GUILayout.Button("Create "+Kind, GUILayout.Width(160))) AddToolbar();
                        }
                        else DrawContents(target, commands);
                        GUILayout.Space(10);
                        GUILayout.Label("Available functions", StudioControlsPreferences.SectionTitleStyle);
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

            }
            if (evt.rawType == EventType.MouseUp) pressedKey = null;
        }
        private void DrawToolbarList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(embeddedHost?160:200)))
            {
                using (var scroll = new EditorGUILayout.ScrollViewScope(leftScroll))
                {
                    leftScroll = scroll.scrollPosition;
                    foreach (var toolbar in toolbars.ToArray())
                    {
                        if (GUILayout.Toggle(selected == toolbar.id, toolbar.name, "Button", GUILayout.Height(28))) selected = toolbar.id;
                    }
                }
                if (GUILayout.Button("+ New "+Kind)) AddToolbar();
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(selected)))
                    if (GUILayout.Button("Delete "+Kind))
                    {
                        var target = toolbars.Find(x => x.id == selected);
                        if (target != null && EditorUtility.DisplayDialog("Delete "+Kind,
                            "Delete the "+Kind.ToLowerInvariant()+" \"" + target.name + "\" and its configured buttons?", "Delete", "Cancel"))
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
            string renamed = radial?EditorGUILayout.TextField(Kind+" name", target.name):EditorGUILayout.DelayedTextField(Kind+" name", target.name);
            string nextName=radial?renamed:renamed.Trim();
            if (!string.IsNullOrWhiteSpace(nextName) && nextName != target.name) { target.name = nextName; Save(); }
            if(radial)
            {
                using(new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Shortcut: "+StudioShortcuts.Label(StudioRadialMenus.Shortcut(target.id)),GUILayout.ExpandWidth(true));
                    if(GUILayout.Button("Assign Shortcut…",GUILayout.Width(140)))StudioControlsPreferences.OpenShortcuts(target.name);
                }
                EditorGUILayout.HelpBox("Each preset has its own shortcut and a constant list. Edits save immediately to its JSON file; Defaults contains factory copies. Release runs a button; gaps cancel. Hover enters submenus. All commands appear in one pie; there are no pages.",MessageType.None);
            }
            using(new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(radial?"Clockwise order — first item at the top":"Toolbar contents", StudioControlsPreferences.SectionTitleStyle);
                if(GUILayout.Button(radial?"+ Empty Spacer":"+ Spacer",GUILayout.Width(120)))
                {Insert(radial?StudioRadialMenus.SpacerKey:StudioCustomToolbars.SpacerKey,target.commands.Count);GUIUtility.ExitGUI();}
            }
            itemScroll = EditorGUILayout.BeginScrollView(itemScroll, EditorStyles.helpBox, GUILayout.Height(Mathf.Max(140, (embeddedHost?embeddedHost.position.height-90:position.height) * .32f)));
            int insertion = target.commands.Count;
            for (int i = 0; i < target.commands.Count; i++)
            {
                var row = EditorGUILayout.GetControlRect(false, 26);
                var command = commands.Find(x => x.Key == target.commands[i]);
                GUI.Label(new Rect(row.x + 4, row.y, row.width - 100, row.height), target.commands[i]==StudioCustomToolbars.SpacerKey ? new GUIContent("≡  Spacer") : new GUIContent("≡  " + (command?.Path ?? target.commands[i]), StudioCommandIcons.Get(command == null ? "Unavailable" : target.commands[i]), "Drag to reorder"));
                if (Event.current.mousePosition.y < row.center.y && insertion == target.commands.Count) insertion = i;
                int index = i;
                using (new EditorGUI.DisabledScope(i == 0))
                    if (GUI.Button(new Rect(row.xMax - 90, row.y + 3, 26, 20), "↑")) { Insert(target.commands[i], i - 1, target.id, i); GUIUtility.ExitGUI(); }
                using (new EditorGUI.DisabledScope(i == target.commands.Count - 1))
                    if (GUI.Button(new Rect(row.xMax - 60, row.y + 3, 26, 20), "↓")) { Insert(target.commands[i], i + 2, target.id, i); GUIUtility.ExitGUI(); }
                if (GUI.Button(new Rect(row.xMax - 30, row.y + 3, 26, 20), "×")) { target.commands.RemoveAt(index); Save(); GUIUtility.ExitGUI(); }
                BeginRowDrag(new Rect(row.x, row.y, row.width - 94, row.height), target.commands[i], target.id, i);
                if (rowDragActive && DragAndDrop.GetGenericData(DragKey) is DragItem marker && marker.window==this && insertion == i) EditorGUI.DrawRect(new Rect(row.x, row.y, row.width, 2), StudioTheme.Current.Accent);
            }
            if (target.commands.Count == 0) GUILayout.Label("Drop functions here, or use + below.", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(50));
            EditorGUILayout.EndScrollView();
            var area = GUILayoutUtility.GetLastRect();
            if ((Event.current.type == EventType.DragUpdated || Event.current.type == EventType.DragPerform) && area.Contains(Event.current.mousePosition) && rowDragActive && DragAndDrop.GetGenericData(DragKey) is DragItem drag && drag.window == this)
            {
                DragAndDrop.visualMode = drag.toolbar == target.id ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Copy;
                if (Event.current.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); Insert(drag.key, insertion, drag.toolbar, drag.index); ClearRowDrag(); }
                Event.current.Use(); Refresh();
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
