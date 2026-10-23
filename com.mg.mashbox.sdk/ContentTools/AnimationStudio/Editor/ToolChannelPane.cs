using System;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    // Borderless, persistent floating host. Tools supply content; the host owns
    // compact chrome, dragging, resizing, padding, scrolling and window lifetime.
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "MashBoxSDK.AnimationStudio", sourceAssembly: "MashBoxSDK.AnimationStudio.Editor", sourceClassName: "StudioToolWindow")]
    internal sealed class ToolChannelPane : EditorWindow
    {
        private static readonly System.Collections.Generic.Dictionary<string, float> FloatDefaults = new System.Collections.Generic.Dictionary<string, float>();
        private static readonly System.Collections.Generic.HashSet<string> DirtyDefaults = new System.Collections.Generic.HashSet<string>();
        private static readonly System.Collections.Generic.Dictionary<(string, string), string> DefaultKeys = new System.Collections.Generic.Dictionary<(string, string), string>();
        private static readonly GUIContent CloseContent = new GUIContent("\u00d7", "Close tool window");
        internal static string DefaultKey(string tool, string parameter)
        {
            var pair = (tool, parameter);
            if (!DefaultKeys.TryGetValue(pair, out string key)) DefaultKeys[pair] = key = "MashBox.Studio.ToolDefaults." + tool + "." + parameter;
            return key;
        }
        internal static float GetFloatDefault(string tool, string parameter, float fallback)
        {
            string key = DefaultKey(tool, parameter);
            if (!FloatDefaults.TryGetValue(key, out float value))
            {
                value = StudioPreferenceStore.GetFloat(key, fallback);
                if (float.IsNaN(value) || float.IsInfinity(value)) value = fallback;
                FloatDefaults[key] = value;
            }
            return value;
        }
        internal static void SetFloatDefault(string tool, string parameter, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            string key = DefaultKey(tool, parameter);
            if (FloatDefaults.TryGetValue(key, out float old) && old == value) return;
            FloatDefaults[key] = value; DirtyDefaults.Add(key);
        }
        internal static float FloatParameter(string tool, string parameter, GUIContent label, float fallback,
            float minimum = float.NegativeInfinity, float maximum = float.PositiveInfinity)
        {
            float value = GetFloatDefault(tool, parameter, fallback);
            EditorGUI.BeginChangeCheck(); float edited = EditorGUILayout.FloatField(label, value);
            if (EditorGUI.EndChangeCheck() && !float.IsNaN(edited) && !float.IsInfinity(edited))
            { value = Mathf.Clamp(edited, minimum, maximum); SetFloatDefault(tool, parameter, value); }
            return value;
        }
        private static ToolChannelPane drawingPane;
        private int integerControl, integerStart;
        private double integerRemainder;
        private void EndIntegerScrub()
        {
            if (integerControl == 0) return;
            if (GUIUtility.hotControl == integerControl) GUIUtility.hotControl = 0;
            integerControl = 0; integerRemainder = 0; EditorGUIUtility.SetWantsMouseJumping(0);
        }
        private int IntegerField(Rect rect, GUIContent label, int value, int minimum, int maximum)
        {
            int id = GUIUtility.GetControlID("StudioIntegerScrub".GetHashCode(), FocusType.Passive, rect);
            var evt = Event.current;
            var drag = new Rect(rect.x, rect.y, Mathf.Min(EditorGUIUtility.labelWidth, rect.width), rect.height);
            EditorGUIUtility.AddCursorRect(drag, MouseCursor.SlideArrow);
            if (integerControl != 0 && GUIUtility.hotControl != integerControl) EndIntegerScrub();
            if (GUI.enabled && evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt && drag.Contains(evt.mousePosition) && GUIUtility.hotControl == 0)
            {
                integerControl = id; integerStart = value; integerRemainder = 0;
                GUIUtility.hotControl = id; GUIUtility.keyboardControl = 0; EditorGUIUtility.editingTextField = false;
                EditorGUIUtility.SetWantsMouseJumping(1); evt.Use();
            }
            if (integerControl == id && GUIUtility.hotControl == id)
            {
                if (evt.type == EventType.MouseDrag)
                {
                    int edited = StudioDragSensitivity.StepInteger(ref integerRemainder, value, StudioDragSensitivity.UnscaledDragDelta.x, dragSensitivity, minimum, maximum);
                    if (edited != value) { value = edited; GUI.changed = true; }
                    evt.Use();
                }
                else if (evt.rawType == EventType.MouseUp && evt.button == 0) { EndIntegerScrub(); evt.Use(); }
                else if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
                { if (value != integerStart) { value = integerStart; GUI.changed = true; } EndIntegerScrub(); evt.Use(); }
            }
            return EditorGUI.IntField(rect, label, value);
        }
        internal static int IntParameter(string tool, string parameter, GUIContent label, int fallback, int minimum, int maximum)
        {
            int value = Mathf.Clamp(Mathf.RoundToInt(GetFloatDefault(tool, parameter, fallback)), minimum, maximum);
            var rect = EditorGUILayout.GetControlRect();
            EditorGUI.BeginChangeCheck();
            int edited = drawingPane ? drawingPane.IntegerField(rect, label, value, minimum, maximum) : EditorGUI.IntField(rect, label, value);
            if (EditorGUI.EndChangeCheck()) { value = Mathf.Clamp(edited, minimum, maximum); SetFloatDefault(tool, parameter, value); }
            return value;
        }
        internal static T EnumParameter<T>(string tool, string parameter, GUIContent label, T fallback) where T : Enum
        {
            int raw = Mathf.RoundToInt(GetFloatDefault(tool, parameter, Convert.ToInt32(fallback)));
            var value = (T)Enum.ToObject(typeof(T), raw);
            if (!Enum.IsDefined(typeof(T), value)) value = fallback;
            EditorGUI.BeginChangeCheck(); var edited = (T)EditorGUILayout.EnumPopup(label, value);
            if (EditorGUI.EndChangeCheck()) { value = edited; SetFloatDefault(tool, parameter, Convert.ToInt32(value)); }
            return value;
        }
        internal static void FlushDefaults()
        {
            foreach (string key in DirtyDefaults) StudioPreferenceStore.SetFloat(key, FloatDefaults[key]);
            DirtyDefaults.Clear();
        }
        internal static void ReloadPreferences()
        {
            foreach (var pane in Resources.FindObjectsOfTypeAll<ToolChannelPane>()) pane.Close();
            FloatDefaults.Clear(); DirtyDefaults.Clear();
        }
        private readonly StudioShortcutFocusGuard shortcutFocus = new StudioShortcutFocusGuard();
        private void OnEnable()
        {
            rootVisualElement.RegisterCallback<UnityEngine.UIElements.KeyDownEvent>(ShieldShortcut, UnityEngine.UIElements.TrickleDown.TrickleDown);
        }
        private void ShieldShortcut(UnityEngine.UIElements.KeyDownEvent evt)
        {
            if (!evt.ctrlKey && !evt.commandKey && !evt.altKey) return;
            // Let IMGUI fields handle ordinary text editing. Other modified
            // keys are pane commands, never Unity main-menu accelerators.
            bool textEdit = !evt.altKey && (evt.keyCode == KeyCode.A || evt.keyCode == KeyCode.C || evt.keyCode == KeyCode.V || evt.keyCode == KeyCode.X);
            if (textEdit) return;
            var key = new Event { type = EventType.KeyDown, keyCode = evt.keyCode, modifiers = evt.modifiers };
            HandlePaneHistory(key);
            evt.imguiEvent?.Use();
            evt.StopImmediatePropagation();
        }
        private void OnFocus() => shortcutFocus.Acquire();
        private void OnLostFocus() { EndIntegerScrub(); shortcutFocus.Release(); FlushDefaults(); SaveBounds(); }
        private Action<bool> history;
        private bool historyQueued, redoQueued;
        private void ApplyQueuedHistory()
        {
            historyQueued = false;
            if (!this || history == null) return;
            rootVisualElement.focusController?.focusedElement?.Blur();
            EditorGUIUtility.editingTextField = false;
            history(redoQueued); Repaint();
        }
        private bool HandlePaneHistory(Event evt)
        {
            bool undo = StudioShortcuts.Pressed(StudioShortcut.Undo, evt), redo = StudioShortcuts.Pressed(StudioShortcut.Redo, evt);
            if (!undo && !redo) return false;
            EndIntegerScrub(); evt.Use();
            // Undo affects live geometry and selection. Never mutate them inside
            // an open IMGUI area/scroll group or Unity text-field callback.
            if (!historyQueued && history != null)
            {
                historyQueued = true; redoQueued = redo; EditorApplication.delayCall += ApplyQueuedHistory;
            }
            return true;
        }
        private void SaveBounds() => StudioPreferenceStore.SetBounds("ToolChannelPane.Bounds", position);
        private const float HeaderHeight = 20, Padding = 10, GripSize = 10;
        private Action draw, closed, execute, preview, cancelPreview;
        private Func<bool> canExecute;
        private Action<Exception> failed;
        private Vector2 scroll, startPointer;
        private Rect startBounds;
        private int chromeControl;
        private bool resizing;
        private float dragSensitivity = 1;
        private string sensitivityKey;
        internal static ToolChannelPane Open(string title, Action draw, Action closed = null,
            Vector2? initialSize = null, Vector2? minimumSize = null,
            Action execute = null, Func<bool> canExecute = null, Action<Exception> failed = null, EditorWindow owner = null, Action preview = null, Action cancelPreview = null, Action<bool> history = null)
        {
            foreach (var pane in Resources.FindObjectsOfTypeAll<ToolChannelPane>()) pane.Close();
            var window = CreateInstance<ToolChannelPane>();
            window.titleContent = new GUIContent(title);
            window.sensitivityKey = "MashBox.Studio.ToolDragSensitivity." + title;
            window.dragSensitivity = StudioDragSensitivity.Normalize(StudioPreferenceStore.GetFloat(window.sensitivityKey, 1));
            window.minSize = minimumSize ?? new Vector2(200, 116);
            var size = Vector2.Max(initialSize ?? new Vector2(240, 124), window.minSize);
            window.draw = draw; window.closed = closed; window.preview = preview; window.cancelPreview = cancelPreview;
            window.history = history;
            window.execute = execute; window.canExecute = canExecute; window.failed = failed;
            // Native ownership keeps the compact borderless pane above its Studio
            // host without focus-stealing callbacks or a repaint/polling loop.
            StudioWindowOwnership.ShowUtility(window, owner, borderless: Application.platform == RuntimePlatform.WindowsEditor);
            // The native utility host can adjust its initial bounds when first shown.
            var bounds = StudioPreferenceStore.GetBounds("ToolChannelPane.Bounds", new Rect(new Vector2(200, 200), size));
            bounds.size = Vector2.Max(bounds.size, window.minSize);
            window.position = bounds;
            return window;
        }
        private void OnGUI()
        {
            if (draw == null) { Close(); return; }
            if (HandlePaneHistory(Event.current)) return;
            bool finishEdit = Event.current.type == EventType.MouseUp || Event.current.type == EventType.KeyUp;
            using (StudioTheme.Scope())
            {
                EditorGUI.DrawRect(new Rect(Vector2.zero, position.size), StudioTheme.Current.Panel);
                DrawChrome();
                if (!this || draw == null) return;
                var content = new Rect(Padding, HeaderHeight + Padding, Mathf.Max(1, position.width-Padding*2), Mathf.Max(1, position.height-HeaderHeight-Padding*2-(execute != null ? 30 : 0)));
                GUILayout.BeginArea(content);
                try
                {
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    try
                    {
                        EditorGUI.BeginChangeCheck();
                        var previousPane = drawingPane; drawingPane = this;
                        try { using (new StudioDragSensitivity.DragScope(dragSensitivity)) draw(); }
                        finally { drawingPane = previousPane; }
                        if (EditorGUI.EndChangeCheck()) preview?.Invoke();
                    }
                    finally { EditorGUILayout.EndScrollView(); }
                }
                finally { GUILayout.EndArea(); }
                if (execute != null) DrawCommands();
                StudioTheme.DrawPopupBorder(new Rect(Vector2.zero, position.size));
                if (finishEdit) FlushDefaults();
                // Fields/buttons get first refusal (typing, copy/paste, tab and
                // numeric edits). Consume anything left before global shortcuts.
                var evt = Event.current;
                if (evt.isKey || evt.type == EventType.ValidateCommand || evt.type == EventType.ExecuteCommand) evt.Use();
            }
        }
        private void DrawCommands()
        {
            // Commands are outside the scroll area with the same padding as fields.
            float width = (position.width - Padding * 2 - 4) / 2;
            float y = position.height - Padding - 22;
            bool enabled = canExecute == null || canExecute();
            using (new EditorGUI.DisabledScope(!enabled))
            {
                if (GUI.Button(new Rect(Padding, y, width, 22), "Execute")) ExecuteCommand(true);
                if (this && GUI.Button(new Rect(Padding + width + 4, y, width, 22), "Apply")) ExecuteCommand(false);
            }
        }
        internal bool ExecuteCommand(bool closeAfter)
        {
            if (execute == null || (canExecute != null && !canExecute())) return false;
            FlushDefaults();
            try { execute(); }
            catch (ExitGUIException) { throw; }
            catch (Exception exception)
            {
                if (failed == null) throw;
                failed(exception); Repaint(); return false;
            }
            if (closeAfter) Close(); else Repaint();
            return true;
        }
        private void DrawChrome()
        {
            var evt = Event.current;
            var header = new Rect(1, 1, position.width-2, HeaderHeight-1);
            var close = new Rect(position.width-HeaderHeight, 1, HeaderHeight-1, HeaderHeight-1);
            var sensitivity = new Rect(close.x-20, 2, 16, 16);
            var grip = new Rect(position.width-GripSize-1, position.height-GripSize-1, GripSize, GripSize);
            EditorGUI.DrawRect(header, StudioTheme.Current.Header);
            for (int i = 0; i < 3; i++) EditorGUI.DrawRect(new Rect(5, 6+i*3, 7, 1), StudioTheme.Current.Muted);
            GUI.Label(new Rect(17, 1, Mathf.Max(1, sensitivity.x-19), HeaderHeight-1), titleContent, StudioTheme.Styles.miniLabel);
            if (StudioDragSensitivity.DrawButton(sensitivity, dragSensitivity, out float next))
            {
                dragSensitivity = next; StudioPreferenceStore.SetFloat(sensitivityKey, next); Repaint();
            }
            if (GUI.Button(close, CloseContent, StudioTheme.Styles.overlayButton)) { Close(); return; }
            for (int i = 0; i < 3; i++)
            {
                EditorGUI.DrawRect(new Rect(position.width-4-i*3, position.height-4, 1, 1+i*3), StudioTheme.Current.Muted);
                EditorGUI.DrawRect(new Rect(position.width-4-i*3, position.height-4-i*3, 1+i*3, 1), StudioTheme.Current.Muted);
            }
            var drag = new Rect(1, 1, Mathf.Max(1, sensitivity.x-1), HeaderHeight-1);
            EditorGUIUtility.AddCursorRect(drag, MouseCursor.Pan);
            EditorGUIUtility.AddCursorRect(grip, MouseCursor.ResizeUpLeft);
            int id = GUIUtility.GetControlID("MashBoxToolWindowChrome".GetHashCode(), FocusType.Passive);
            if (evt.type == EventType.MouseDown && evt.button == 0 && (drag.Contains(evt.mousePosition) || grip.Contains(evt.mousePosition)))
            {
                chromeControl = id; GUIUtility.hotControl = id; resizing = grip.Contains(evt.mousePosition);
                startPointer = position.position + evt.mousePosition; startBounds = position; evt.Use();
            }
            else if (chromeControl == id && GUIUtility.hotControl == id)
            {
                if (evt.type == EventType.MouseDrag)
                {
                    var delta = position.position + evt.mousePosition-startPointer;
                    position = resizing ? new Rect(startBounds.position, Vector2.Min(maxSize, Vector2.Max(minSize, startBounds.size+delta)))
                        : new Rect(startBounds.position+delta, startBounds.size);
                    evt.Use(); Repaint();
                }
                else if (evt.type == EventType.MouseUp && evt.button == 0)
                { GUIUtility.hotControl = 0; chromeControl = 0; SaveBounds(); evt.Use(); }
            }
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape && GUIUtility.hotControl == 0)
            { evt.Use(); Close(); }
        }
        private void OnDisable()
        {
            EndIntegerScrub(); shortcutFocus.Release();
            EditorApplication.delayCall -= ApplyQueuedHistory; historyQueued = false; history = null;
            SaveBounds(); FlushDefaults(); StudioPreferenceStore.Flush();
            if (chromeControl != 0 && GUIUtility.hotControl == chromeControl) GUIUtility.hotControl = 0;
            chromeControl = 0;
            cancelPreview?.Invoke(); cancelPreview = preview = null;
            var callback = closed; draw = closed = execute = null; canExecute = null; failed = null; callback?.Invoke();
        }
    }
}
