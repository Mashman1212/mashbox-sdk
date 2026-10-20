using System;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    [InitializeOnLoad]
    internal static class StudioSettings
    {
        internal const string PreferenceKey = "MashBox.Studio.Settings.v1";
        // Read the previous key until preferences are explicitly saved under the new name.
        private const string LegacyPreferenceKey = "MashBox.MGStudio.Settings.v1";
        [Serializable] internal sealed class Preferences
        {
            public string theme = "graphite";
            public bool neutralMarkingMenus = true;
            public bool showMeshStatistics = true;
            public bool showWorkspaceFps;
            public bool hideStatusBar;
            public bool floatingStudioWindow;
            public bool hideObjectWireframe;
            public bool disableViewportAntialiasing;
            public bool useViewportSmaa;
            public bool viewportVSync;
            public bool disableViewportImageCache;
            public float gridSpacing = .25f, gridOpacity = 1, uvPointSize = 4;
            public float handleSize = 1;
        }
        internal static Preferences Current { get; private set; }
        static StudioSettings() { Reload(); }
        internal static void Reload()
        {
            try { Current = JsonUtility.FromJson<Preferences>(EditorPrefs.GetString(PreferenceKey, EditorPrefs.GetString(LegacyPreferenceKey, ""))) ?? new Preferences(); }
            catch { Current = new Preferences(); }
            Sanitize(Current);
        }
        private static float Clamp(float value, float min, float max, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
        private static void Sanitize(Preferences value)
        {
            if (Array.FindIndex(StudioTheme.Palettes, p => p.Id == value.theme) < 0) value.theme = "graphite";
            value.gridSpacing = Clamp(value.gridSpacing, .01f, 10, .25f);
            value.gridOpacity = Clamp(value.gridOpacity, 0, 1, 1);
            value.uvPointSize = Clamp(value.uvPointSize, 2, 8, 4);
            value.handleSize = Clamp(value.handleSize, .25f, 3, 1);
        }
        internal static void Save()
        {
            Sanitize(Current); EditorPrefs.SetString(PreferenceKey, JsonUtility.ToJson(Current));
            // Host replacement must happen after the preferences IMGUI event finishes.
            EditorApplication.delayCall -= ApplyWindowMode;
            EditorApplication.delayCall += ApplyWindowMode;
            foreach (var view in Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>()) { view.ApplyTheme(); view.Repaint(); }
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (window.GetType().Namespace == typeof(StudioSettings).Namespace) window.Repaint();
        }
        private static void ApplyWindowMode()
        {
            foreach (var studio in Resources.FindObjectsOfTypeAll<AnimationStudioWindow>())
                studio.ApplyWindowModePreference();
        }
        internal static void Reset() { Current = new Preferences(); Save(); }
    }

    internal sealed class StudioSettingsWindow : EditorWindow
    {
        private Vector2 scroll;
        internal static void Open()
        {
            var window = GetWindow<StudioSettingsWindow>(true, "MashBox Studio Settings", true);
            window.minSize = new Vector2(440, 540); window.ShowUtility(); window.Focus();
        }
        private void OnGUI()
        {
            using (StudioTheme.Scope()) DrawSettings();
        }
        private void DrawSettings()
        {
            EditorGUI.DrawRect(new Rect(Vector2.zero, position.size), StudioTheme.Current.Panel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            using (new EditorGUILayout.VerticalScope(StudioTheme.Styles.inspectorDefaultMargins))
            {
                if (GUILayout.Button("Controls and Shortcuts…")) StudioControlsPreferences.Open();
                GUILayout.Label("Appearance", StudioTheme.Styles.boldLabel);
                GUILayout.Label("Choose a palette. Changes apply immediately to MashBox Studio and its tool windows.", StudioTheme.Styles.wordWrappedMiniLabel);
                GUILayout.Space(8);
                for (int i = 0; i < StudioTheme.Palettes.Length; i += 2)
                using (new EditorGUILayout.HorizontalScope())
                    for (int n = i; n < Mathf.Min(i + 2, StudioTheme.Palettes.Length); n++) DrawPalette(StudioTheme.Palettes[n]);
                var settings = StudioSettings.Current;
                EditorGUI.BeginChangeCheck();
                GUILayout.Space(12);
                settings.neutralMarkingMenus = EditorGUILayout.ToggleLeft(new GUIContent("Neutral marking menus", "Keep right-click marking menus gray, regardless of the selected palette."), settings.neutralMarkingMenus);
                GUILayout.Space(12);
                DrawWindowMode();
                GUILayout.Label("Viewport", StudioTheme.Styles.boldLabel);
                DrawViewportAntialiasing();
                DrawViewportVSync();
                DrawViewportCache();
                settings.showMeshStatistics = EditorGUILayout.Toggle("Mesh statistics", settings.showMeshStatistics);
                settings.hideObjectWireframe = !EditorGUILayout.Toggle("Object wireframe", !settings.hideObjectWireframe);
                settings.handleSize = EditorGUILayout.Slider(new GUIContent("Handle size", "Resize modeling handles with + and − in the viewport."), settings.handleSize, .25f, 3);
                settings.gridSpacing = EditorGUILayout.FloatField(new GUIContent("Grid spacing", "Distance between minor grid lines, in world units."), settings.gridSpacing);
                settings.gridOpacity = EditorGUILayout.Slider("Grid opacity", settings.gridOpacity, 0, 1);
                GUILayout.Space(12);
                GUILayout.Label("UV Editor", StudioTheme.Styles.boldLabel);
                settings.uvPointSize = EditorGUILayout.Slider("UV point size", settings.uvPointSize, 2, 8);
                if (EditorGUI.EndChangeCheck()) StudioSettings.Save();
                GUILayout.Space(12);
                GUILayout.Label("Saved for this user on this computer. Unity's own theme and other windows are unchanged.", StudioTheme.Styles.wordWrappedMiniLabel);
                GUILayout.Space(8);
                if (GUILayout.Button("Restore MashBox Studio Defaults")) StudioSettings.Reset();
            }
            EditorGUILayout.EndScrollView();
        }
        private void DrawPalette(StudioTheme.Palette palette)
        {
            bool active = StudioSettings.Current.theme == palette.Id;
            var rect = GUILayoutUtility.GetRect(170, 39, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, active ? StudioTheme.Current.Selection : rect.Contains(Event.current.mousePosition) ? StudioTheme.Current.Hover : StudioTheme.Current.Button);
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            { StudioSettings.Current.theme = palette.Id; StudioSettings.Save(); }
            var chip = new Rect(rect.x + 7, rect.y + 7, 26, 25);
            EditorGUI.DrawRect(chip, palette.Canvas);
            EditorGUI.DrawRect(new Rect(chip.x, chip.yMax - 6, chip.width, 6), palette.Accent);
            GUI.Label(new Rect(rect.x + 40, rect.y, rect.width - 44, rect.height), (active ? "✓ " : "") + palette.Name, StudioTheme.Styles.label);
        }
        internal static void DrawWindowMode()
        {
            var settings = StudioSettings.Current;
            bool dockable = !settings.floatingStudioWindow;
            bool selected = EditorGUILayout.Toggle(new GUIContent("Dockable window", "Allow docking into Unity and show its tab strip. Disable for a floating utility window without the tab strip. The open workspace and camera are retained."), dockable);
            if (selected == dockable) return;
            settings.floatingStudioWindow = !selected;
            StudioSettings.Save();
        }
        internal static void DrawViewportAntialiasing()
        {
            var settings = StudioSettings.Current;
            int current = settings.disableViewportAntialiasing ? 0 : settings.useViewportSmaa ? 2 : 1;
            int selected = EditorGUILayout.Popup(new GUIContent("Viewport anti-aliasing", "Built-in HDRP spatial AA. FXAA smooths color boundaries throughout the camera image. Studio motion blur is always disabled."), current, new[] { "Off", "FXAA", "SMAA (High)" });
            if (selected == current) return;
            settings.disableViewportAntialiasing = selected == 0;
            settings.useViewportSmaa = selected == 2;
            StudioSettings.Save();
        }
        internal static void DrawViewportCache()
        {
            var settings = StudioSettings.Current;
            EditorGUI.BeginChangeCheck();
            settings.disableViewportImageCache = !EditorGUILayout.Toggle(new GUIContent("Cache unchanged viewport", "Reuse the last modelling image until camera, geometry, selection or display state changes. Input and panels remain live."), !settings.disableViewportImageCache);
            if (EditorGUI.EndChangeCheck()) StudioSettings.Save();
        }
        internal static void DrawViewportVSync()
        {
            var settings = StudioSettings.Current;
            EditorGUI.BeginChangeCheck();
            settings.viewportVSync = EditorGUILayout.Toggle(new GUIContent("VSync", "Synchronize Studio's window presentation with the display. Other Unity windows and project quality settings are unchanged."), settings.viewportVSync);
            if (EditorGUI.EndChangeCheck()) StudioSettings.Save();
        }
    }
}

