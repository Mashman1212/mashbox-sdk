using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    // Native ownership provides z-order, explicit visibility events cover hidden
    // (rather than minimized) owners, and managed links cascade Close().
    internal static class StudioWindowOwnership
    {
        private static readonly System.Reflection.FieldInfo WindowHost = typeof(EditorWindow).GetField("m_Parent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        private const string SessionKey = "MashBox.Studio.OwnedWindows";
        [Serializable] private sealed class SavedLinks { public int[] children, owners; public bool[] hidden; }
        private static readonly Dictionary<EditorWindow, EditorWindow> links = new Dictionary<EditorWindow, EditorWindow>();
        private static readonly HashSet<EditorWindow> restoreHiddenChildren = new HashSet<EditorWindow>();
        static StudioWindowOwnership()
        {
            RestoreLinks();
            AssemblyReloadEvents.beforeAssemblyReload += SaveLinks;
            AssemblyReloadEvents.beforeAssemblyReload += StopVisibilityEvents;
            EditorApplication.quitting += StopVisibilityEvents;
            if (links.Count > 0) EditorApplication.delayCall += RestoreNativeVisibilityLinks;
        }
        private static void RestoreLinks()
        {
            string json = SessionState.GetString(SessionKey, "");
            if (string.IsNullOrEmpty(json)) return;
            var saved = JsonUtility.FromJson<SavedLinks>(json);
            if (saved?.children == null || saved.owners == null) return;
            for (int i = 0; i < Math.Min(saved.children.Length, saved.owners.Length); i++)
            {
                var child = EditorUtility.InstanceIDToObject(saved.children[i]) as EditorWindow;
                var owner = EditorUtility.InstanceIDToObject(saved.owners[i]) as EditorWindow;
                if (child && owner && child != owner)
                {
                    links[child] = owner;
                    if (saved.hidden != null && i < saved.hidden.Length && saved.hidden[i]) restoreHiddenChildren.Add(child);
                }
            }
        }
        private static void SaveLinks()
        {
            foreach (var child in links.Where(p => !p.Key || !p.Value).Select(p => p.Key).ToArray()) links.Remove(child);
            SessionState.SetString(SessionKey, JsonUtility.ToJson(new SavedLinks {
                children = links.Keys.Select(w => w.GetInstanceID()).ToArray(),
                owners = links.Values.Select(w => w.GetInstanceID()).ToArray(),
                hidden = links.Keys.Select(w => restoreHiddenChildren.Contains(w) || WasHiddenForOwner(w)).ToArray() }));
            restoreHiddenChildren.RemoveWhere(w => !w || !links.ContainsKey(w));
        }
        internal static EditorWindow FindStudioOwner()
        {
            var current = EditorWindow.focusedWindow;
            var visited = new HashSet<EditorWindow>();
            while (current && visited.Add(current))
            {
                if (current is MashBoxStudioViewport) return current;
                if (!links.TryGetValue(current, out current)) break;
            }
            return SceneView.lastActiveSceneView as MashBoxStudioViewport ??
                Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().FirstOrDefault();
        }
        internal static void Register(EditorWindow window, EditorWindow owner)
        {
            if (!window || !owner || window == owner) return;
            // Refuse ownership cycles, including indirect cycles through a recorder.
            for (var parent = owner; parent;)
            {
                if (parent == window) return;
                if (!links.TryGetValue(parent, out parent)) break;
            }
            links[window] = owner; SaveLinks();
        }
        internal static void CloseOwnedWindows(EditorWindow owner)
        {
            if (ReferenceEquals(owner, null)) return;
            foreach (var child in links.Where(p => ReferenceEquals(p.Value, owner)).Select(p => p.Key).ToArray())
            {
                links.Remove(child);
                if (!child) continue;
                CloseOwnedWindows(child);
                // Native owner destruction can remove a host before the managed
                // window receives OnDestroy. Close() requires a live HostView.
                if (WindowHost?.GetValue(child) == null) UnityEngine.Object.DestroyImmediate(child);
                else child.Close();
            }
            links.Remove(owner); SaveLinks();
            if (links.Count == 0) StopVisibilityEvents();
        }
        internal static void ReparentOwnedWindows(EditorWindow previous, EditorWindow replacement)
        {
            foreach (var child in links.Where(p => p.Value == previous).Select(p => p.Key).ToArray())
            {
                if (!child) continue;
                Register(child, replacement);
                if (Application.platform == RuntimePlatform.WindowsEditor && !child.docked)
                {
                    var nativeOwner = CaptureOwner(replacement);
                    if (nativeOwner != IntPtr.Zero) { child.Focus(); AttachActiveWindow(nativeOwner); }
                }
            }
        }
        internal static IntPtr CaptureOwner(EditorWindow owner)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor || !owner || WindowHost?.GetValue(owner) == null) return IntPtr.Zero;
            owner.Focus(); return GetActiveWindow();
        }
        internal static void AttachActiveWindow(IntPtr ownerHandle)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor || ownerHandle == IntPtr.Zero) return;
            var windowHandle = GetActiveWindow();
            if (windowHandle != IntPtr.Zero && windowHandle != ownerHandle)
            {
                SetOwner(windowHandle, ownerHandle);
                TrackVisibility(windowHandle, ownerHandle);
            }
        }
        internal static void ShowUtility(EditorWindow window, EditorWindow owner = null, bool borderless = false)
        {
            owner = owner ? owner : FindStudioOwner();
            Register(window, owner);
            var ownerHandle = CaptureOwner(owner);
            window.ShowUtility(); window.Focus(); AttachActiveWindow(ownerHandle);
            if (borderless && Application.platform == RuntimePlatform.WindowsEditor)
            {
                var handle = GetActiveWindow();
                if (handle != IntPtr.Zero && handle != ownerHandle)
                {
                    // Keep the utility lifecycle; only remove native frame chrome.
                    // ShowPopup uses menu semantics and can survive app minimize.
                    long style = IntPtr.Size == 8 ? GetWindowLongPtr(handle, -16).ToInt64() : GetWindowLong(handle, -16);
                    long frameless = style & ~(0x00C00000L | 0x00040000L);
                    if (IntPtr.Size == 8) SetWindowLongPtr(handle, -16, new IntPtr(frameless));
                    else SetWindowLong(handle, -16, (int)frameless);
                    SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
                }
            }
        }
        internal static void Show(EditorWindow window, EditorWindow owner = null)
        {
            owner = owner ? owner : FindStudioOwner();
            Register(window, owner);
            var ownerHandle = CaptureOwner(owner);
            window.Show();
            if (!window.docked) { window.Focus(); AttachActiveWindow(ownerHandle); }
        }
        private static readonly Dictionary<IntPtr, IntPtr> nativeLinks = new Dictionary<IntPtr, IntPtr>();
        private static readonly HashSet<IntPtr> hiddenByOwner = new HashSet<IntPtr>();
        private delegate void VisibilityEvent(IntPtr hook, uint type, IntPtr window, int objectId, int childId, uint thread, uint time);
        private static readonly VisibilityEvent visibilityEvent = OnVisibilityEvent;
        private static System.Runtime.InteropServices.GCHandle visibilityRoot;
        private static IntPtr objectVisibilityHook, minimizeHook;
        private static bool syncingVisibility;
        private static void TrackVisibility(IntPtr window, IntPtr owner)
        {
            if (window == IntPtr.Zero || owner == IntPtr.Zero || window == owner) return;
            nativeLinks[window] = owner;
            if (objectVisibilityHook == IntPtr.Zero && minimizeHook == IntPtr.Zero)
            {
                if (!visibilityRoot.IsAllocated) visibilityRoot = System.Runtime.InteropServices.GCHandle.Alloc(visibilityEvent);
                // Out-of-context callbacks run on this Unity UI thread. Listen only
                // to this process's window show/hide/destroy and minimize events.
                uint process = GetCurrentProcessId();
                objectVisibilityHook = SetWinEventHook(0x8001, 0x8003, IntPtr.Zero, visibilityEvent, process, 0, 0);
                minimizeHook = SetWinEventHook(0x0016, 0x0017, IntPtr.Zero, visibilityEvent, process, 0, 0);
            }
            SyncOwnedVisibility();
        }
        private static bool OwnerVisible(IntPtr owner)
        {
            for (int depth = 0; owner != IntPtr.Zero && depth < 32; depth++, owner = GetWindow(owner, 4))
                if (!IsWindow(owner) || !IsWindowVisible(owner) || IsIconic(owner)) return false;
            return true;
        }
        private static void OnVisibilityEvent(IntPtr hook, uint type, IntPtr window, int objectId, int childId, uint thread, uint time)
        {
            if (syncingVisibility || window == IntPtr.Zero || (type >= 0x8000 && (objectId != 0 || childId != 0))) return;
            foreach (var pair in nativeLinks.ToArray())
            {
                bool related = pair.Key == window;
                var owner = pair.Value;
                for (int depth = 0; !related && owner != IntPtr.Zero && depth < 32; depth++, owner = GetWindow(owner, 4)) related = owner == window;
                if (related) { SyncOwnedVisibility(); return; }
            }
        }
        private static void SyncOwnedVisibility()
        {
            if (syncingVisibility) return;
            syncingVisibility = true;
            try
            {
                foreach (var pair in nativeLinks.ToArray())
                {
                    var child = pair.Key;
                    if (!IsWindow(child) || !IsWindow(pair.Value) || GetWindow(child, 4) != pair.Value)
                    { nativeLinks.Remove(child); hiddenByOwner.Remove(child); continue; }
                    if (!OwnerVisible(pair.Value))
                    {
                        if (IsWindowVisible(child)) { hiddenByOwner.Add(child); ShowWindow(child, 0); }
                    }
                    else if (hiddenByOwner.Remove(child)) ShowWindow(child, 4); // SW_SHOWNOACTIVATE
                }
            }
            finally { syncingVisibility = false; }
        }
        private static void StopVisibilityEvents()
        {
            EditorApplication.delayCall -= RestoreNativeVisibilityLinks;
            if (objectVisibilityHook != IntPtr.Zero) UnhookWinEvent(objectVisibilityHook);
            if (minimizeHook != IntPtr.Zero) UnhookWinEvent(minimizeHook);
            objectVisibilityHook = minimizeHook = IntPtr.Zero;
            if (visibilityRoot.IsAllocated) visibilityRoot.Free();
            nativeLinks.Clear(); hiddenByOwner.Clear();
        }
        private delegate bool EnumerateWindow(IntPtr window, IntPtr extra);
        private static string NativeTitle(IntPtr window)
        {
            var text = new System.Text.StringBuilder(256); GetWindowText(window, text, text.Capacity);
            return text.ToString().TrimEnd(' ', '*');
        }
        private static bool WasHiddenForOwner(EditorWindow window)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor || nativeLinks.Count == 0) return false;
            string title = window.titleContent.text.TrimEnd(' ', '*');
            return hiddenByOwner.Any(w => IsWindow(w) && NativeTitle(w) == title);
        }
        private static void RestoreNativeVisibilityLinks()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor || Application.isBatchMode || links.Count == 0) return;
            var windows = new List<IntPtr>(); uint process = GetCurrentProcessId();
            EnumWindows((window, extra) => { GetWindowThreadProcessId(window, out uint id); if (id == process) windows.Add(window); return true; }, IntPtr.Zero);
            foreach (var child in links.Keys.ToArray())
            {
                if (!child || child.docked) continue;
                string title = child.titleContent.text.TrimEnd(' ', '*');
                var matches = windows.Where(w => NativeTitle(w) == title).ToArray();
                // Rebind existing native ownership once after reload without
                // focusing windows or persisting native handles. Ambiguity is skipped.
                if (matches.Length == 1 && GetWindow(matches[0], 4) != IntPtr.Zero)
                {
                    if (restoreHiddenChildren.Remove(child)) hiddenByOwner.Add(matches[0]);
                    TrackVisibility(matches[0], GetWindow(matches[0], 4));
                }
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, VisibilityEvent callback, uint process, uint thread, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumerateWindow callback, IntPtr extra);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int maximum);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong(IntPtr window, int index, int value);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        private static void SetOwner(IntPtr window, IntPtr owner)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr(window, -8, owner);
            else SetWindowLong(window, -8, owner.ToInt32());
        }
    }
}
