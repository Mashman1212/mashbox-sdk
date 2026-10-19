using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed class StudioControlsPreferences : EditorWindow
    {
        private string search = "";
        private Vector2 scroll;
        private string feedback;
        internal static void Open()
        {
            var window = GetWindow<StudioControlsPreferences>(true,"MashBox Studio Preferences",true);
            window.minSize = new Vector2(600,540); window.ShowUtility(); window.Focus();
        }
        private void OnGUI()
        {
            using (StudioTheme.Scope())
            {
                EditorGUI.DrawRect(new Rect(Vector2.zero,position.size),StudioTheme.Current.Panel);
                DrawControls();
            }
        }
        internal void DrawControls()
        {
            EditorGUILayout.LabelField("MashBox Studio — Modelling Controls", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Bindings apply to Studio modelling and its UV editor. Unity's shortcut profile is unchanged. Click a binding to record a replacement, + to add an alternative, or × to remove it. Changes save immediately for this user.", MessageType.None);
            search = EditorGUILayout.TextField("Find a control",search);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Restore all defaults",GUILayout.Width(160))) { StudioShortcuts.Reset(); feedback = null; }
                GUILayout.FlexibleSpace();
            }
            if (!string.IsNullOrEmpty(feedback)) EditorGUILayout.HelpBox(feedback, MessageType.Warning);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            string group = null;
            foreach (var definition in StudioShortcuts.Definitions)
            {
                string filter = search;
                if (!string.IsNullOrEmpty(filter) && (definition.group+" "+definition.name+" "+StudioShortcuts.Label(definition.id)).IndexOf(filter,StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (group != definition.group) { group = definition.group; GUILayout.Space(10); EditorGUILayout.LabelField(group,EditorStyles.boldLabel); }
                var bindings = StudioShortcuts.Bindings(definition.id);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(definition.name,GUILayout.Width(205));
                    using (new EditorGUILayout.VerticalScope())
                    {
                        if (bindings.Length == 0) GUILayout.Label("Unassigned",EditorStyles.miniLabel);
                        for (int i = 0; i < bindings.Length; i++)
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            int index = i;
                            var binding = bindings[i];
                            if (GUILayout.Button(new GUIContent(binding.ToString(),binding.allowShift ? "Also accepts Shift. Click to remap." : "Click to remap."),GUILayout.MinWidth(130)))
                                Record(definition,index);
                            if (GUILayout.Button("×",GUILayout.Width(23)))
                            { StudioShortcuts.Set(definition.id,bindings.Where((b,n) => n != index)); GUIUtility.ExitGUI(); }
                            string conflict = StudioShortcuts.Conflict(definition.id,binding);
                            if (conflict != null) GUILayout.Label(new GUIContent("⚠", "Also assigned to "+conflict),GUILayout.Width(20));
                        }
                    }
                    if (GUILayout.Button("+",GUILayout.Width(23))) Record(definition,-1);
                    if (GUILayout.Button("Reset",GUILayout.Width(48))) { StudioShortcuts.Reset(definition.id); feedback = null; GUIUtility.ExitGUI(); }
                }
            }
            GUILayout.Space(12);
            EditorGUILayout.HelpBox("Hold bindings release even if you release Ctrl/Shift/Alt first. Text fields keep normal typing and text-editing shortcuts. Selection modifiers (Shift adds, Ctrl/Cmd subtracts), cut precision modifiers and the mouse wheel retain their existing behavior.",MessageType.None);
            EditorGUILayout.EndScrollView();
        }
        private void Record(StudioShortcuts.Definition definition, int index)
        {
            StudioShortcutRecorder.Open(definition,index,binding => {
                var bindings = StudioShortcuts.Bindings(definition.id).Select(b => b.Copy()).ToList();
                if (index < 0) bindings.Add(binding); else if (index < bindings.Count) bindings[index] = binding;
                // Reject conflicts instead of silently disabling another working command.
                feedback = StudioShortcuts.Conflict(definition.id,binding);
                if (feedback != null) { feedback = binding+" is already assigned to "+feedback+". Clear or remap that control first."; return; }
                StudioShortcuts.Set(definition.id,bindings); feedback = null;
            });
        }
    }
    internal sealed class StudioShortcutRecorder : EditorWindow
    {
        private Action<StudioShortcuts.Binding> apply;
        private StudioShortcuts.Definition definition;
        private bool allowShift;
        private string message;
        private Rect cancelRect;
        internal static void Open(StudioShortcuts.Definition definition, int index, Action<StudioShortcuts.Binding> apply)
        {
            var window = CreateInstance<StudioShortcutRecorder>();
            window.definition = definition; window.apply = apply;
            var current = StudioShortcuts.Bindings(definition.id);
            window.allowShift = index >= 0 && index < current.Length && current[index].allowShift;
            window.titleContent = new GUIContent("Remap "+definition.name);
            window.minSize = window.maxSize = new Vector2(440,180); window.ShowUtility(); window.Focus();
        }
        private void OnGUI()
        {
            bool mouse = definition.id == StudioShortcut.Orbit || definition.id == StudioShortcut.Pan || definition.id == StudioShortcut.Dolly || definition.id == StudioShortcut.MarkingMenu || definition.id == StudioShortcut.AxisMenu;
            GUILayout.Label(definition.name,EditorStyles.boldLabel);
            GUILayout.Label(mouse ? "Click a mouse button here with your chosen modifiers." : "Press a key with your chosen Ctrl/Cmd, Shift or Alt modifiers.",EditorStyles.wordWrappedLabel);
            GUILayout.Label("Escape can be assigned. Use Cancel below to close without changes.",EditorStyles.wordWrappedMiniLabel);
            if (!mouse) allowShift = EditorGUILayout.ToggleLeft("Also accept Shift with this binding",allowShift);
            if (message != null) EditorGUILayout.HelpBox(message,MessageType.Warning);
            GUILayout.FlexibleSpace();
            cancelRect = GUILayoutUtility.GetRect(new GUIContent("Cancel"),GUI.skin.button,GUILayout.Height(24));
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && cancelRect.Contains(evt.mousePosition)) { evt.Use(); Close(); return; }
            GUI.Button(cancelRect,"Cancel");
            if (evt.type == EventType.KeyDown && !mouse && !IsModifier(evt.keyCode) && evt.keyCode != KeyCode.None)
                Capture(new StudioShortcuts.Binding(evt.keyCode,StudioShortcuts.Modifiers(evt),allowShift && !evt.shift));
            else if (mouse && evt.type == EventType.MouseDown)
                Capture(new StudioShortcuts.Binding((KeyCode)((int)KeyCode.Mouse0+evt.button),StudioShortcuts.Modifiers(evt)));
        }
        private void Capture(StudioShortcuts.Binding binding)
        {
            Event.current.Use();
            string conflict = StudioShortcuts.Conflict(definition.id,binding);
            if (conflict != null) { message = binding+" is already assigned to "+conflict+". Clear or remap it first."; Repaint(); return; }
            apply(binding); Close();
        }
        private static bool IsModifier(KeyCode code) => code == KeyCode.LeftShift || code == KeyCode.RightShift ||
            code == KeyCode.LeftControl || code == KeyCode.RightControl || code == KeyCode.LeftCommand || code == KeyCode.RightCommand || code == KeyCode.LeftAlt || code == KeyCode.RightAlt;
    }
}
