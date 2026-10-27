using UnityEditor;
using UnityEngine;
using System.Reflection;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class MashBoxStudioViewport
    {
        private bool storedConfigurationInitialized;
        internal void InitializeStoredConfiguration()
        {
            if (storedConfigurationInitialized) return;
            storedConfigurationInitialized = true;
            modelGridVisible = StudioPreferenceStore.GetFloat("Studio.GridVisible", modelGridVisible ? 1 : 0) >= .5f;
            rootVisualElement.RegisterCallback<UnityEngine.UIElements.GeometryChangedEvent>(_ => SaveStoredConfiguration());
            rootVisualElement.RegisterCallback<UnityEngine.UIElements.FocusOutEvent>(_ => SaveStoredConfiguration());
        }
        internal void SaveStoredConfiguration()
        {
            if (!storedConfigurationInitialized || (Studio && !ReferenceEquals(Studio.ActiveViewport, this))) return;
            // Docked tab bounds are controlled by Unity's workspace; preserve the last floating bounds.
            if (!docked && !IsStudioMaximized) StudioPreferenceStore.SetBounds("Studio.Bounds", position);
            if (Studio) StudioPreferenceStore.SetString("Studio.Mode", Studio.IsPaintingMode ? "Painting" : Studio.IsModelingMode ? "Modeling" : "Animation");
            StudioPreferenceStore.SetFloat("Studio.GridVisible", modelGridVisible ? 1 : 0);
            Studio?.SaveAnimationConfiguration();
            SaveStoredToolbars();
        }
        internal void RestoreStoredConfiguration()
        {
            Studio?.RestoreStoredMode();
            preferredLeft = ValidWidth(StudioPreferenceStore.GetFloat(LeftWidthKey, 260), 260, MinLeft);
            preferredRight = ValidWidth(StudioPreferenceStore.GetFloat(RightWidthKey, 350), 350, MinRight);
            preferredToolHeight = StudioPreferenceStore.GetFloat(ToolOptionsHeightKey, 220);
            if (!docked && !IsStudioMaximized) position = StudioPreferenceStore.GetBounds("Studio.Bounds", position);
            modelGridVisible = StudioPreferenceStore.GetFloat("Studio.GridVisible", 1) >= .5f;
            ReloadCustomToolbars(); RestoreStoredToolbars(); UpdatePanelLayout();
        }
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
            if (!StudioShortcuts.Pressed(StudioShortcut.Maximize,evt) || EditorGUIUtility.editingTextField || GUIUtility.hotControl != 0) return false;
            evt.Use();
            // Rebuild the dock layout outside the current SceneView IMGUI pass.
            EditorApplication.delayCall += () => { if (this) ToggleMaximized(); };
            return true;
        }
    }
}
