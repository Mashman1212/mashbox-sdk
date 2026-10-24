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
            public Color customPrimary = new Color(.18f, .22f, .25f, 1);
            public Color customSecondary = new Color(.85f, .70f, .47f, 1);
            public bool showMeshStatistics = true;
            public bool showWorkspaceFps; // Legacy popup preference; retained for profile compatibility.
            public bool hideViewportFps;
            public bool hideStatusBar;
            public bool hideOrientationAxes;
            public bool hideToolOptions;
            public bool floatingStudioWindow;
            public bool hideObjectWireframe;
            public bool disableViewportAntialiasing;
            public bool useViewportSmaa;
            public bool viewportVSync;
            // Personal preference only. Loading/resetting Studio must never change
            // a project's collector; apply it only after an explicit toggle edit.
            public bool incrementalGarbageCollection;
            public bool disableViewportImageCache;
            public float gridSpacing = .25f, gridOpacity = 1, uvPointSize = 4;
            public float handleSize = 1;
            public float channelDragSensitivity = 1;
            public int toolbarIconSize = 24;
            public int undoLimit = 50;
        }
        internal static Preferences Current { get; private set; }
        static StudioSettings() { Reload(); }
        internal static void Reload()
        {
            try
            {
                Current = new Preferences();
                string json = StudioPreferenceStore.GetString(PreferenceKey, StudioPreferenceStore.GetString(LegacyPreferenceKey, ""));
                if (!string.IsNullOrEmpty(json)) JsonUtility.FromJsonOverwrite(json, Current);
            }
            catch { Current = new Preferences(); }
            Sanitize(Current);
        }
        private static float Clamp(float value, float min, float max, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
        private static void Sanitize(Preferences value)
        {
            if (value.theme != "custom" && Array.FindIndex(StudioTheme.Palettes, p => p.Id == value.theme) < 0) value.theme = "graphite";
            Color CleanColor(Color c, Color fallback) => new Color(Clamp(c.r,0,1,fallback.r),Clamp(c.g,0,1,fallback.g),Clamp(c.b,0,1,fallback.b),1);
            value.customPrimary = CleanColor(value.customPrimary,new Color(.18f,.22f,.25f,1));
            value.customSecondary = CleanColor(value.customSecondary,new Color(.85f,.70f,.47f,1));
            value.gridSpacing = Clamp(value.gridSpacing, .01f, 10, .25f);
            value.gridOpacity = Clamp(value.gridOpacity, 0, 1, 1);
            value.uvPointSize = Clamp(value.uvPointSize, 2, 8, 4);
            value.toolbarIconSize = value.toolbarIconSize == 16 ? 16 : 24;
            value.undoLimit = Math.Max(1, value.undoLimit);
            value.handleSize = Clamp(value.handleSize, .25f, 3, 1);
            value.channelDragSensitivity = StudioDragSensitivity.Normalize(Clamp(value.channelDragSensitivity, .1f, 4, 1));
        }
        internal static void Save()
        {
            Sanitize(Current); StudioPreferenceStore.SetString(PreferenceKey, JsonUtility.ToJson(Current));
            StudioUndo.EnforceLimit();
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
        internal static void SetIncrementalGarbageCollection(bool enabled)
        {
            Current.incrementalGarbageCollection = enabled;
            if (PlayerSettings.gcIncremental != enabled) PlayerSettings.gcIncremental = enabled;
            Save();
        }
        internal static void Reset() { Current = new Preferences(); Save(); }
    }

    internal sealed class StudioSettingsWindow : EditorWindow
    {
        private Vector2 scroll;
        internal static void Open()
        {
            var window = GetWindow<StudioSettingsWindow>(true, "MashBox Studio Themes", true);
            window.minSize = new Vector2(440, 320); StudioWindowOwnership.ShowUtility(window);
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
                GUILayout.Label("Themes", StudioTheme.Styles.boldLabel);
                GUILayout.Label("Choose a palette. Changes apply immediately to MashBox Studio and its tool windows.", StudioTheme.Styles.wordWrappedMiniLabel);
                GUILayout.Space(8);
                for (int i = 0; i < StudioTheme.Palettes.Length; i += 2)
                using (new EditorGUILayout.HorizontalScope())
                    for (int n = i; n < Mathf.Min(i + 2, StudioTheme.Palettes.Length); n++) DrawPalette(StudioTheme.Palettes[n]);
                var settings = StudioSettings.Current;
                GUILayout.Space(12);
                DrawPalette(StudioTheme.Custom);
                EditorGUI.BeginChangeCheck();
                var primary = EditorGUILayout.ColorField(new GUIContent("Primary", "Base color for the custom theme's surfaces."), settings.customPrimary, true, false, false);
                var secondary = EditorGUILayout.ColorField(new GUIContent("Secondary", "Accent color for selections and highlights."), settings.customSecondary, true, false, false);
                if (EditorGUI.EndChangeCheck())
                {
                    settings.customPrimary = primary; settings.customSecondary = secondary; settings.theme = "custom";
                    StudioSettings.Save();
                }
                GUILayout.Label("Choose two colors. Text, surfaces, borders and highlights adjust automatically.", StudioTheme.Styles.wordWrappedMiniLabel);
                GUILayout.Space(12);
                if (GUILayout.Button("Restore Theme Defaults"))
                {
                    var defaults = new StudioSettings.Preferences();
                    settings.theme = defaults.theme;
                    settings.customPrimary = defaults.customPrimary;
                    settings.customSecondary = defaults.customSecondary;
                    StudioSettings.Save();
                }
            }
            EditorGUILayout.EndScrollView();
        }
        internal static void DrawGeneralSettings()
        {
            StudioPreferenceStore.DrawTransferButtons();
            DrawWindowMode();
            DrawToolbarIconSize();
            EditorGUI.BeginChangeCheck();
            int undoLimit = EditorGUILayout.DelayedIntField(new GUIContent("Maximum undo actions", "Maximum Studio modeling actions kept across Undo and Redo. Default 50; minimum 1. Lower values can reduce retained memory. A complete drag or tool operation counts as one action."), StudioSettings.Current.undoLimit);
            if (EditorGUI.EndChangeCheck()) { StudioSettings.Current.undoLimit = Math.Max(1, undoLimit); StudioSettings.Save(); }
            GUILayout.Space(8);
            GUILayout.Label("Viewport", StudioTheme.Styles.boldLabel);
            DrawViewportAntialiasing();
            DrawViewportVSync();
            DrawViewportCache();
            DrawIncrementalGarbageCollection();
            var settings = StudioSettings.Current;
            EditorGUI.BeginChangeCheck();
            settings.hideObjectWireframe = !EditorGUILayout.Toggle("Object wireframe", !settings.hideObjectWireframe);
            settings.handleSize = EditorGUILayout.Slider(new GUIContent("Handle size", "Resize modeling handles with + and − in the viewport."), settings.handleSize, .25f, 3);
            settings.gridSpacing = EditorGUILayout.FloatField(new GUIContent("Grid spacing", "Distance between minor grid lines, in world units."), settings.gridSpacing);
            settings.gridOpacity = EditorGUILayout.Slider("Grid opacity", settings.gridOpacity, 0, 1);
            GUILayout.Space(12);
            GUILayout.Label("UV Editor", StudioTheme.Styles.boldLabel);
            settings.uvPointSize = EditorGUILayout.Slider("UV point size", settings.uvPointSize, 2, 8);
            if (EditorGUI.EndChangeCheck()) StudioSettings.Save();
            GUILayout.Space(12);
            GUILayout.Label("Settings are saved in this project’s MashBoxStudio/Preferences folder. The incremental GC toggle changes this Unity project's Player setting.", StudioTheme.Styles.wordWrappedMiniLabel);
            if (GUILayout.Button("Restore MashBox Studio Defaults")) StudioSettings.Reset();
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
        private static readonly GUIContent ToolbarIconSizeContent = new GUIContent("Toolbar icon size", "Icon size for Studio's native dockable and floating toolbars.");
        private static readonly string[] ToolbarIconSizes = { "16 x 16", "24 x 24" };
        internal static void DrawToolbarIconSize()
        {
            int current = StudioSettings.Current.toolbarIconSize == 16 ? 0 : 1;
            int selected = EditorGUILayout.Popup(ToolbarIconSizeContent, current, ToolbarIconSizes);
            if (selected == current) return;
            StudioSettings.Current.toolbarIconSize = selected == 0 ? 16 : 24;
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
        private static readonly GUIContent IncrementalGCContent = new GUIContent("Incremental GC",
            "Apply this preference to Unity's project-wide garbage collector. Affects future game builds too. Restart Unity after changing the project setting.");
        internal static void DrawIncrementalGarbageCollection()
        {
            bool preferred = StudioSettings.Current.incrementalGarbageCollection;
            EditorGUI.BeginChangeCheck();
            bool selected = EditorGUILayout.Toggle(IncrementalGCContent, preferred);
            if (EditorGUI.EndChangeCheck()) StudioSettings.SetIncrementalGarbageCollection(selected);
            bool configured = PlayerSettings.gcIncremental;
            bool running = UnityEngine.Scripting.GarbageCollector.isIncremental;
            EditorGUILayout.LabelField("Unity project: " + (configured ? "On" : "Off") + " / Running editor: " + (running ? "On" : "Off"), StudioTheme.Styles.miniLabel);
            if (configured != running)
                EditorGUILayout.HelpBox("Restart Unity to apply the project's garbage collection setting. This also affects future game builds.", MessageType.Info);
            else if (StudioSettings.Current.incrementalGarbageCollection != configured)
                EditorGUILayout.HelpBox("The project already uses a different setting. Studio leaves it unchanged until you edit this toggle.", MessageType.None);
            else
                GUILayout.Label("Project-wide setting, including future game builds. Changes require a Unity restart.", StudioTheme.Styles.wordWrappedMiniLabel);
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

