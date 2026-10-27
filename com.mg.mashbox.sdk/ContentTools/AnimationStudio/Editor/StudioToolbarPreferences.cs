using System;
using System.IO;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // Toolbar layout and custom contents share one project-level file, independent
    // of the currently selected general-settings and hotkey profiles.
    internal static class StudioToolbarPreferences
    {
        [Serializable] private sealed class Configuration
        {
            public string format = "MashBoxStudio.ToolbarOverlays";
            public int version = 1;
            public string layout = "", customToolbars = "";
        }
        private static Configuration current;
        private static bool dirty, unreadable;
        internal static string PathName => Path.Combine(StudioPreferenceStore.DirectoryPath, "ToolbarOverlay_Settings.json");
        internal static bool Owns(string key) => key == "Studio.Toolbars" || key == "Studio.CustomToolbars";
        internal static bool Dirty => dirty;
        private static void Load()
        {
            if (current != null) return;
            if (File.Exists(PathName))
            {
                try
                {
                    var parsed = JsonUtility.FromJson<Configuration>(File.ReadAllText(PathName));
                    if (parsed == null || parsed.format != "MashBoxStudio.ToolbarOverlays" || parsed.version != 1 || parsed.layout == null || parsed.customToolbars == null)
                        throw new InvalidDataException("Unsupported toolbar settings file.");
                    current = parsed; return;
                }
                catch (Exception ex) { unreadable = true; Debug.LogError("MashBox Studio could not load toolbar settings: " + ex.Message); }
            }
            current = new Configuration {
                layout = StudioPreferenceStore.LegacyToolbarString("Studio.Toolbars"),
                customToolbars = StudioPreferenceStore.LegacyToolbarString("Studio.CustomToolbars") };
        }
        internal static string Get(string key, string fallback) { Load(); string value = key == "Studio.Toolbars" ? current.layout : current.customToolbars; return string.IsNullOrEmpty(value) ? fallback : value; }
        internal static bool Has(string key) { Load(); return !string.IsNullOrEmpty(key == "Studio.Toolbars" ? current.layout : current.customToolbars); }
        internal static void Set(string key, string value)
        {
            Load();
            if (unreadable) return; // Preserve the unreadable source file for recovery.
            string previous = key == "Studio.Toolbars" ? current.layout : current.customToolbars;
            if (previous == value && File.Exists(PathName)) return;
            if (key == "Studio.Toolbars") current.layout = value; else current.customToolbars = value;
            dirty = true; StudioPreferenceStore.ScheduleFlush();
        }
        internal static void Flush()
        {
            if (!dirty || unreadable) return;
            try { StudioPreferenceStore.WriteAtomic(PathName, JsonUtility.ToJson(current, true)); dirty = false; }
            catch (Exception ex) { Debug.LogError("MashBox Studio could not save toolbar settings: " + ex.Message); }
        }
    }
}
