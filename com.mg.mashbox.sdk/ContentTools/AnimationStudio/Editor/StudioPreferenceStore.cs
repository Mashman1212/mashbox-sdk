using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // Project-local configuration only. Native Undo records and running jobs stay session-scoped.
    [InitializeOnLoad]
    internal static class StudioPreferenceStore
    {
        [Serializable] private sealed class Entry { public string key, value; }
        [Serializable] private sealed class Configuration
        {
            public string format;
            public int version;
            public List<Entry> entries = new List<Entry>();
        }
        private static Configuration data;
        private static bool dirty, migrateLegacy;
        private static double flushAfter;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        internal static string DirectoryPath => Path.Combine(Path.GetDirectoryName(Application.dataPath), "MashBoxStudio", "Preferences");
        internal static string SettingsPath { get { EnsureSelection(); return activeSettings; } }
        internal static string HotkeysPath { get { EnsureSelection(); return activeHotkeys; } }
        [Serializable] private sealed class Selection
        {
            public string format = "MashBoxStudio.LastLoaded";
            public int version = 1;
            public string settingsFile = "Settings.json", hotkeysFile = "Hotkeys.json";
        }
        private static Selection selected;
        private static string activeSettings, activeHotkeys;
        private static bool applyingSettings;
        internal static string SelectionPath => Path.Combine(DirectoryPath, "LastLoaded.json");
        internal static string DefaultSettingsPath => Path.Combine(DirectoryPath, "default_settings.json");
        internal static string DefaultHotkeysPath => Path.Combine(DirectoryPath, "default_hotkeys.json");
        private static string ResolvePath(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(DirectoryPath, path));
        private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        private static string StoredPath(string path)
        {
            string prefix = Path.GetFullPath(DirectoryPath) + Path.DirectorySeparatorChar;
            path = Path.GetFullPath(path);
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path.Substring(prefix.Length) : path;
        }
        private static void EnsureSelection()
        {
            if (selected != null) return;
            selected = new Selection();
            if (File.Exists(SelectionPath))
                try
                {
                    var saved = JsonUtility.FromJson<Selection>(File.ReadAllText(SelectionPath));
                    if (saved == null || saved.format != "MashBoxStudio.LastLoaded" || saved.version != 1 || string.IsNullOrWhiteSpace(saved.settingsFile) || string.IsNullOrWhiteSpace(saved.hotkeysFile))
                        throw new InvalidDataException("Unsupported last-loaded configuration.");
                    ResolvePath(saved.settingsFile); ResolvePath(saved.hotkeysFile); selected = saved;
                }
                catch (Exception ex) { Debug.LogWarning("MashBox Studio could not read LastLoaded.json: " + ex.Message); }
            activeSettings = ExistingOrFallback(selected.settingsFile, "Settings.json");
            activeHotkeys = ExistingOrFallback(selected.hotkeysFile, "Hotkeys.json");
        }
        private static string ExistingOrFallback(string saved, string fallback)
        {
            string path = ResolvePath(saved);
            if (File.Exists(path) || SamePath(path, ResolvePath(fallback))) return path;
            Debug.LogWarning("MashBox Studio's last loaded file is unavailable: " + path + ". Using " + fallback + "; the missing file has not been changed.");
            return ResolvePath(fallback);
        }
        private static void SelectFile(bool hotkeys, string path)
        {
            EnsureSelection(); path = Path.GetFullPath(path);
            var next = new Selection { settingsFile = selected.settingsFile, hotkeysFile = selected.hotkeysFile };
            if (hotkeys) next.hotkeysFile = StoredPath(path); else next.settingsFile = StoredPath(path);
            WriteIfChanged(SelectionPath, JsonUtility.ToJson(next, true));
            selected = next;
            if (hotkeys) activeHotkeys = path; else activeSettings = path;
        }
        internal static void SelectHotkeys(string path) => SelectFile(true, path);
        private static void EnsureWritable(bool hotkeys)
        {
            string path = hotkeys ? HotkeysPath : SettingsPath;
            if (!SamePath(path, DefaultSettingsPath) && !SamePath(path, DefaultHotkeysPath)) return;
            // Loading a factory preset is read-only. Its first edit creates a new
            // personal file rather than changing the preset or another user file.
            string stem = hotkeys ? "Custom_Hotkeys" : "Custom_Settings";
            string copy = Path.Combine(DirectoryPath, stem + ".json"); int suffix = 2;
            while (File.Exists(copy)) copy = Path.Combine(DirectoryPath, stem + "_" + suffix++ + ".json");
            WriteAtomic(copy, File.ReadAllText(path)); SelectFile(hotkeys, copy);
        }
        private static void WriteIfChanged(string path, string json)
        {
            if (!File.Exists(path) || File.ReadAllText(path) != json) WriteAtomic(path, json);
        }
        private static Configuration FactorySettings()
        {
            var defaults = new Configuration { format = "MashBoxStudio.Preferences", version = 1 };
            void Add(string key, string value) => defaults.entries.Add(new Entry { key = key, value = value });
            void Number(string key, float value) => Add(key, value.ToString("R", Invariant));
            Add("Studio.Files.WorkspaceFolder", "");
            Add("Studio.Files.AssetFolder", "");
            Add(StudioSettings.PreferenceKey, JsonUtility.ToJson(new StudioSettings.Preferences()));
            foreach (var item in new[] { ("Bevel Edges", "Width", .1f), ("Bevel Edges", "Subdivisions", 1f), ("Bevel Edges", "Tension", 1f), ("Bevel Edges", "Junction", 0f), ("Soften Edges", "Angle", 45f), ("Extrude", "Distance", .01f), ("Extrude", "Inset", 0f), ("Extrude", "Together", 1f) })
                Number(ToolChannelPane.DefaultKey(item.Item1, item.Item2), item.Item3);
            foreach (string title in new[] { "Bevel", "Soften Edges", "Extrude", "Edit Edge Flow", "Bridge Edges", "Bisect", "Tool Options" }) Number("MashBox.Studio.ToolDragSensitivity." + title, 1);
            Number(MashBoxStudioViewport.LeftWidthKey, 260); Number(MashBoxStudioViewport.RightWidthKey, 350); Number(MashBoxStudioViewport.ToolOptionsHeightKey, 220);
            Number(MashBoxStudioViewport.NavigationSensitivityKey, 1);
            Number("Model.BackfaceCulling", 1);
            Number("Model.CheckerMap", 0);
            Number("Model.EdgeLoopCount", 1);
            Number(ToolChannelPane.DefaultKey("Bisect","Bisect Distance"),.01f);
            Number(ToolChannelPane.DefaultKey("Bisect","Fill"),1);
            Number(StudioModelEditor.PivotLockKey, 0);
            Number(ToolChannelPane.DefaultKey("Combine Meshes","CombineUVs"),1);
            Number(ToolChannelPane.DefaultKey("Combine Meshes","Pivot"),0);
            Number(ToolChannelPane.DefaultKey("Combine Meshes","KeepFirstName"),0);
            Number("MashBox.Studio.ToolDragSensitivity.Combine Meshes",1);
            Number(ToolChannelPane.DefaultKey("Bridge Edges","Divisions"),0);
            Number(ToolChannelPane.DefaultKey("Bridge Edges","Curvature"),0);
            Number(ToolChannelPane.DefaultKey("Edit Edge Flow","Influence"),.5f);
            Number(ToolChannelPane.DefaultKey("Edit Edge Flow","Iterations"),4);
            Number(ToolChannelPane.DefaultKey("Bevel Edges","EdgeFlow"),0);
            Number(ToolChannelPane.DefaultKey("Bevel Edges","FlowInfluence"),.5f);
            Number(ToolChannelPane.DefaultKey("Bevel Edges","FlowIterations"),4);
            Number(StudioMaterialLibraryIndex.SourcesKey, 3);
            Number("Studio.MaterialLibrary.Thumbnail", 76);
            Number("Studio.MaterialLibrary.FolderWidth", 145);
            Number("Studio.MaterialLibrary.HideMaterialSuffix", 0);
            Add("Studio.Mode", "Modeling");
            return defaults;
        }
        internal static void WriteFactoryDefaults()
        {
            WriteIfChanged(DefaultSettingsPath, JsonUtility.ToJson(FactorySettings(), true));
            WriteIfChanged(DefaultHotkeysPath, StudioShortcuts.FactoryJson());
        }

        static StudioPreferenceStore()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Flush;
            EditorApplication.quitting += Flush;
            EditorApplication.delayCall += Bootstrap;
        }
        private static void Bootstrap()
        {
            // Only factory files may be regenerated. Existing user configuration
            // is read as-is; reading defaults never writes an active profile.
            WriteFactoryDefaults(); EnsureLoaded();
            if (!File.Exists(SettingsPath) && SamePath(SettingsPath, Path.Combine(DirectoryPath, "Settings.json")))
            {
                StudioSettings.Save();
                foreach (var entry in FactorySettings().entries)
                    if (!data.entries.Exists(e => e.key == entry.key)) data.entries.Add(entry);
                dirty = true; Flush();
            }
            if (!File.Exists(HotkeysPath) && SamePath(HotkeysPath, Path.Combine(DirectoryPath, "Hotkeys.json")))
            {
                string legacy = EditorPrefs.GetString(StudioShortcuts.PreferenceKey, "");
                WriteAtomic(HotkeysPath, string.IsNullOrEmpty(legacy) ? "{\"format\":\"MashBoxStudio.Hotkeys\",\"version\":1,\"overrides\":[]}" : legacy);
            }
            if (!File.Exists(SelectionPath)) WriteAtomic(SelectionPath, JsonUtility.ToJson(selected, true));
            StudioShortcuts.Reload();
        }
        private static Configuration Parse(string json)
        {
            var parsed = JsonUtility.FromJson<Configuration>(json);
            if (parsed == null || parsed.format != "MashBoxStudio.Preferences" || parsed.version != 1 || parsed.entries == null)
                throw new InvalidDataException("This is not a supported MashBox Studio settings file.");
            var keys = new HashSet<string>();
            foreach (var entry in parsed.entries)
                if (entry == null || string.IsNullOrEmpty(entry.key) || entry.value == null || !keys.Add(entry.key))
                    throw new InvalidDataException("The settings file contains invalid or duplicate entries.");
            return parsed;
        }
        private static void EnsureLoaded()
        {
            if (data != null) return;
            migrateLegacy = !File.Exists(SettingsPath);
            data = new Configuration { format = "MashBoxStudio.Preferences", version = 1 };
            if (File.Exists(SettingsPath))
                try { data = Parse(File.ReadAllText(SettingsPath)); }
                catch (Exception ex)
                {
                    // Preserve malformed input for recovery instead of silently overwriting the only copy.
                    Debug.LogWarning("MashBox Studio could not load " + SettingsPath + ": " + ex.Message);
                    File.Copy(SettingsPath, SettingsPath + ".corrupt-" + DateTime.UtcNow.Ticks, false);
                    if (File.Exists(SettingsPath + ".bak"))
                        try { data = Parse(File.ReadAllText(SettingsPath + ".bak")); } catch (Exception) { }
                }
        }
        internal static void WriteAtomic(string path, string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, json);
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        private static string CanonicalKey(string key)
        {
            if (key.StartsWith("MashBox.LastTake.", StringComparison.Ordinal)) return "MashBox.LastTake";
            if (key.StartsWith("MashBox.LastModel.", StringComparison.Ordinal)) return "MashBox.LastModel";
            if (key.StartsWith("MashBox.Studio.RecentFiles.", StringComparison.Ordinal)) return "MashBox.Studio.RecentFiles";
            return key;
        }
        internal static string LegacyToolbarString(string key)
        {
            EnsureLoaded();
            var entry = data.entries.Find(e => e.key == key);
            return entry != null ? entry.value : migrateLegacy ? EditorPrefs.GetString(key, "") : "";
        }
        internal static string GetString(string key, string fallback = "")
        {
            if (StudioToolbarPreferences.Owns(key)) return StudioToolbarPreferences.Get(key, fallback);
            EnsureLoaded();
            if (key == StudioShortcuts.PreferenceKey)
            {
                if (File.Exists(HotkeysPath)) return File.ReadAllText(HotkeysPath);
                return SamePath(HotkeysPath, Path.Combine(DirectoryPath, "Hotkeys.json")) ? EditorPrefs.GetString(key, fallback) : fallback;
            }
            var entry = data.entries.Find(e => e.key == CanonicalKey(key));
            if (entry != null) return entry.value;
            if (migrateLegacy && EditorPrefs.HasKey(key))
            { string value = EditorPrefs.GetString(key, fallback); SetString(key, value); return value; }
            return fallback;
        }
        internal static bool HasKey(string key)
        {
            if (StudioToolbarPreferences.Owns(key)) return StudioToolbarPreferences.Has(key);
            EnsureLoaded();
            if (key == StudioShortcuts.PreferenceKey) return File.Exists(HotkeysPath);
            return data.entries.Exists(e => e.key == CanonicalKey(key)) || (migrateLegacy && EditorPrefs.HasKey(key));
        }
        internal static float GetFloat(string key, float fallback = 0)
        {
            EnsureLoaded();
            var entry = data.entries.Find(e => e.key == CanonicalKey(key));
            if (entry == null)
            {
                if (migrateLegacy && EditorPrefs.HasKey(key))
                { float migrated = EditorPrefs.GetFloat(key, fallback); SetFloat(key, migrated); return migrated; }
                return fallback;
            }
            return float.TryParse(entry.value, NumberStyles.Float, Invariant, out float value) && !float.IsNaN(value) && !float.IsInfinity(value) ? value : fallback;
        }
        internal static void SetFloat(string key, float value) => SetString(key, value.ToString("R", Invariant));
        internal static void SetString(string key, string value)
        {
            EnsureLoaded();
            if (key == StudioShortcuts.PreferenceKey) { EnsureWritable(true); WriteAtomic(HotkeysPath, value); return; }
            if (applyingSettings) return;
            if (StudioToolbarPreferences.Owns(key)) { StudioToolbarPreferences.Set(key, value); return; }
            var entry = data.entries.Find(e => e.key == CanonicalKey(key));
            if (entry != null && entry.value == value) return;
            if (entry == null) data.entries.Add(new Entry { key = CanonicalKey(key), value = value }); else entry.value = value;
            dirty = true;
            ScheduleFlush();
        }
        internal static void ScheduleFlush()
        {
            flushAfter = EditorApplication.timeSinceStartup + .4;
            EditorApplication.update -= FlushWhenQuiet;
            EditorApplication.update += FlushWhenQuiet;
        }
        internal static void DeleteKey(string key)
        {
            // A tombstone prevents deleted legacy preferences from being resurrected.
            SetString(key, "");
        }
        private static void FlushWhenQuiet()
        {
            if (EditorApplication.timeSinceStartup >= flushAfter) Flush();
        }
        internal static void Flush()
        {
            EditorApplication.update -= FlushWhenQuiet;
            StudioToolbarPreferences.Flush();
            if (!dirty) return;
            try { EnsureWritable(false); WriteAtomic(SettingsPath, JsonUtility.ToJson(data, true)); dirty = false; }
            catch (Exception ex) { Debug.LogError("MashBox Studio could not save preferences: " + ex.Message); }
        }
        [Serializable] private sealed class BoundsValue
        {
            public float x, y, width, height;
        }
        internal static Rect GetBounds(string key, Rect fallback)
        {
            string raw = GetString(key);
            if (string.IsNullOrEmpty(raw)) return fallback;
            try
            {
                var value = JsonUtility.FromJson<BoundsValue>(raw);
                if (value == null) return fallback;
                Rect rect = new Rect(value.x, value.y, value.width, value.height);
                if (!float.IsNaN(rect.x + rect.y + rect.width + rect.height) && !float.IsInfinity(rect.x + rect.y + rect.width + rect.height) && rect.width >= 100 && rect.height >= 80)
                    return rect;
            }
            catch (ArgumentException) { }
            return fallback;
        }
        internal static void SetBounds(string key, Rect value) => SetString(key, JsonUtility.ToJson(new BoundsValue { x = value.x, y = value.y, width = value.width, height = value.height }));
        internal static void ImportSettings(string path)
        {
            var imported = Parse(File.ReadAllText(path)); // Validate before replacing the active file.
            var general = imported.entries.Find(e => e.key == StudioSettings.PreferenceKey);
            if (general != null && JsonUtility.FromJson<StudioSettings.Preferences>(general.value) == null)
                throw new InvalidDataException("The general preferences are invalid.");
            ToolChannelPane.ReloadPreferences(); Flush();
            if (dirty || StudioToolbarPreferences.Dirty) throw new IOException("The current settings could not be saved. The selected file has not changed.");
            SelectFile(false, path);
            data = imported; dirty = false; migrateLegacy = false;
            applyingSettings = true;
            try
            {
                StudioSettings.Reload();
                foreach (var studio in Resources.FindObjectsOfTypeAll<AnimationStudioWindow>()) { studio.ReloadToolDefaults(); studio.LoadAnimationConfiguration(); }
                foreach (var view in Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>()) view.RestoreStoredConfiguration();
            }
            finally { applyingSettings = false; }
        }
        internal static void ExportSettings(string path)
        {
            ToolChannelPane.FlushDefaults();
            StudioSettings.Save();
            foreach (var view in Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>()) view.SaveStoredConfiguration();
            EnsureLoaded(); Flush();
            var exported = JsonUtility.FromJson<Configuration>(JsonUtility.ToJson(data));
            exported.entries.RemoveAll(entry => StudioToolbarPreferences.Owns(entry.key));
            WriteAtomic(path, JsonUtility.ToJson(exported, true));
        }
        internal static void DrawTransferButtons()
        {
            GUILayout.Label("Configuration files", StudioTheme.Styles.boldLabel);
            EditorGUILayout.SelectableLabel("Settings: " + SettingsPath + "\nHotkeys: " + HotkeysPath + "\nToolbars: " + StudioToolbarPreferences.PathName, StudioTheme.Styles.wordWrappedMiniLabel, GUILayout.Height(66));
            GUILayout.Label("The last loaded files reopen automatically. Changes save to those files; factory presets create a personal copy on first edit.", StudioTheme.Styles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Import Settings")) Transfer(false, false);
                if (GUILayout.Button("Export Settings")) Transfer(false, true);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Import Hotkeys")) Transfer(true, false);
                if (GUILayout.Button("Export Hotkeys")) Transfer(true, true);
            }
            GUILayout.Space(8);
        }
        private static bool transferPending;
        private static void Transfer(bool hotkeys, bool export)
        {
            // Native file dialogs can re-enter/reset IMGUI layout. Open them only
            // after the scroll view and button-row scopes have finished drawing.
            if (transferPending) return;
            transferPending = true;
            EditorApplication.delayCall += () =>
            {
                try { ExecuteTransfer(hotkeys, export); }
                finally { transferPending = false; }
            };
        }
        private static void ExecuteTransfer(bool hotkeys, bool export)
        {
            string name = hotkeys ? "Hotkeys" : "Settings";
            string path = export ? EditorUtility.SaveFilePanel("Export Studio " + name, DirectoryPath, name, "json") : EditorUtility.OpenFilePanel("Import Studio " + name, DirectoryPath, "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (hotkeys) { if (export) StudioShortcuts.Export(path); else StudioShortcuts.Import(path); }
                else { if (export) ExportSettings(path); else ImportSettings(path); }
            }
            catch (Exception ex) { EditorUtility.DisplayDialog("Studio " + name, ex.Message, "OK"); }
        }
    }
}
