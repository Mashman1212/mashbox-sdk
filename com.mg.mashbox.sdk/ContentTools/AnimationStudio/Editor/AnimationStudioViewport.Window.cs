using UnityEditor;
using UnityEngine;
using System.Reflection;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioViewport
    {
        private const BindingFlags WindowMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo HostField = typeof(EditorWindow).GetField("m_Parent", WindowMembers);
        private object FloatingContainer
        {
            get
            {
                var host = HostField?.GetValue(this);
                return host?.GetType().GetProperty("window", WindowMembers)?.GetValue(host);
            }
        }
        internal bool IsStudioMaximized
        {
            get
            {
                if (maximized) return true;
                if (docked) return false;
                var container = FloatingContainer;
                return container?.GetType().GetProperty("maximized", WindowMembers)?.GetValue(container) is bool value && value;
            }
        }

        internal void ToggleMaximized()
        {
            if (docked || maximized) maximized = !maximized;
            else
            {
                // EditorWindow.maximized only expands tabs in Unity's main layout.
                // Floating windows use the native container's maximize/restore operation.
                var container = FloatingContainer;
                container?.GetType().GetMethod("ToggleMaximize", WindowMembers)?.Invoke(container, null);
            }
            Focus(); Repaint();
        }

        internal bool HandleWindowShortcut()
        {
            var evt = Event.current;
            if (evt.type != EventType.KeyDown || evt.keyCode != KeyCode.F11 ||
                evt.alt || evt.control || evt.command || evt.shift || GUIUtility.hotControl != 0) return false;
            evt.Use();
            // Rebuild the dock layout outside the current SceneView IMGUI pass.
            EditorApplication.delayCall += () => { if (this) ToggleMaximized(); };
            return true;
        }
    }
}
