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
        private static readonly GUIContent CloseContent = new GUIContent("×", "Close tool window");
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
                value = EditorPrefs.GetFloat(key, fallback);
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
        internal static int IntParameter(string tool, string parameter, GUIContent label, int fallback, int minimum, int maximum)
        {
            int value = Mathf.Clamp(Mathf.RoundToInt(GetFloatDefault(tool, parameter, fallback)), minimum, maximum);
            EditorGUI.BeginChangeCheck(); int edited = EditorGUILayout.IntField(label, value);
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
            foreach (string key in DirtyDefaults) EditorPrefs.SetFloat(key, FloatDefaults[key]);
            DirtyDefaults.Clear();
        }
        private void OnLostFocus() => FlushDefaults();
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
            Action execute = null, Func<bool> canExecute = null, Action<Exception> failed = null, EditorWindow owner = null, Action preview = null, Action cancelPreview = null)
        {
            var window = CreateInstance<ToolChannelPane>();
            window.titleContent = new GUIContent(title);
            window.sensitivityKey = "MashBox.Studio.ToolDragSensitivity." + title;
            window.dragSensitivity = StudioDragSensitivity.Normalize(EditorPrefs.GetFloat(window.sensitivityKey, 1));
            window.minSize = minimumSize ?? new Vector2(200, 116);
            var size = Vector2.Max(initialSize ?? new Vector2(240, 124), window.minSize);
            window.draw = draw; window.closed = closed; window.preview = preview; window.cancelPreview = cancelPreview;
            window.execute = execute; window.canExecute = canExecute; window.failed = failed;
            // Native ownership keeps the compact borderless pane above its Studio
            // host without focus-stealing callbacks or a repaint/polling loop.
            var ownerHandle = IntPtr.Zero;
            if (Application.platform == RuntimePlatform.WindowsEditor && owner)
            { owner.Focus(); ownerHandle = NativePaneOwnership.GetActiveWindow(); }
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                window.ShowPopup();
                var paneHandle = NativePaneOwnership.GetActiveWindow();
                if (ownerHandle != IntPtr.Zero && paneHandle != IntPtr.Zero && paneHandle != ownerHandle)
                    NativePaneOwnership.SetOwner(paneHandle, ownerHandle);
            }
            else window.ShowUtility();
            // The native popup can replace its initial bounds when first shown.
            window.position = new Rect(new Vector2(200, 200), size);
            return window;
        }
        private static class NativePaneOwnership
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            internal static extern IntPtr GetActiveWindow();
            [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
            private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
            [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
            private static extern int SetWindowLong(IntPtr window, int index, int value);
            internal static void SetOwner(IntPtr window, IntPtr owner)
            {
                const int OwnerIndex = -8; // GWLP_HWNDPARENT for a top-level window.
                if (IntPtr.Size == 8) SetWindowLongPtr(window, OwnerIndex, owner);
                else SetWindowLong(window, OwnerIndex, owner.ToInt32());
            }
        }
        private void OnGUI()
        {
            if (draw == null) { Close(); return; }
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
                        using (new StudioDragSensitivity.DragScope(dragSensitivity)) draw();
                        if (EditorGUI.EndChangeCheck()) preview?.Invoke();
                    }
                    finally { EditorGUILayout.EndScrollView(); }
                }
                finally { GUILayout.EndArea(); }
                if (execute != null) DrawCommands();
                StudioTheme.DrawPopupBorder(new Rect(Vector2.zero, position.size));
                if (finishEdit) FlushDefaults();
            }
        }
        private void DrawCommands()
        {
            // Commands are outside the scroll area with the same padding as fields.
            float width = (position.width - Padding * 2 - 8) / 3;
            float y = position.height - Padding - 22;
            bool enabled = canExecute == null || canExecute();
            using (new EditorGUI.DisabledScope(!enabled))
            {
                if (GUI.Button(new Rect(Padding, y, width, 22), "Execute")) ExecuteCommand(true);
                if (this && GUI.Button(new Rect(Padding + width + 4, y, width, 22), "Apply")) ExecuteCommand(false);
            }
            if (this && GUI.Button(new Rect(Padding + (width + 4) * 2, y, width, 22), "Close")) Close();
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
                dragSensitivity = next; EditorPrefs.SetFloat(sensitivityKey, next); Repaint();
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
                { GUIUtility.hotControl = 0; chromeControl = 0; evt.Use(); }
            }
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape && GUIUtility.hotControl == 0)
            { evt.Use(); Close(); }
        }
        private void OnDisable()
        {
            FlushDefaults();
            if (chromeControl != 0 && GUIUtility.hotControl == chromeControl) GUIUtility.hotControl = 0;
            chromeControl = 0;
            cancelPreview?.Invoke(); cancelPreview = preview = null;
            var callback = closed; draw = closed = execute = null; canExecute = null; failed = null; callback?.Invoke();
        }
    }
}
