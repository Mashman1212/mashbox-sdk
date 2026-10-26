using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;

public static class StudioProfilePersistenceValidation
{
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
    static int checks;
    static string Folder => StudioPreferenceStore.DirectoryPath;
    static string Result(string name) => Path.GetFullPath(Path.Combine(Application.dataPath, "../../" + name));
    static void Check(bool pass, string text) { if (!pass) throw new Exception(text); checks++; }
    static void ReloadStore()
    {
        StudioPreferenceStore.Flush();
        typeof(StudioPreferenceStore).GetField("data", Flags).SetValue(null, null);
        typeof(StudioPreferenceStore).GetField("selected", Flags).SetValue(null, null);
        StudioSettings.Reload(); StudioShortcuts.Reload();
    }
    [Serializable] class Entry { public string key, value; }
    [Serializable] class Config { public string format = "MashBoxStudio.Preferences"; public int version = 1; public Entry[] entries; }
    static void WriteSettings(string path, string theme, int limit)
    {
        File.WriteAllText(path, JsonUtility.ToJson(new Config { entries = new[] { new Entry { key = StudioSettings.PreferenceKey, value = JsonUtility.ToJson(new StudioSettings.Preferences { theme = theme, undoLimit = limit }) }, new Entry { key = "User.Marker", value = "keep me" } } }, true));
    }
    static void Bootstrap() => typeof(StudioPreferenceStore).GetMethod("Bootstrap", Flags).Invoke(null, null);
    static StudioShortcut Register(string group, string label) => StudioModelCommands.Register(group, label).Value;
    public static void Run()
    {
        int exit = 0;
        try
        {
            Bootstrap(); StudioPreferenceStore.Flush();
            string canonicalSettings = Path.Combine(Folder, "Settings.json"), canonicalKeys = Path.Combine(Folder, "Hotkeys.json");
            string initialSettings = File.ReadAllText(canonicalSettings), initialKeys = File.ReadAllText(canonicalKeys);
            Directory.CreateDirectory(Path.Combine(Folder, "Profiles"));
            string settingsA = Path.Combine(Folder, "Profiles", "My settings.json"), keysA = Path.Combine(Folder, "Profiles", "My hotkeys.json");
            string settingsB = Path.Combine(Folder, "Other settings.json"), keysB = Path.Combine(Folder, "Other hotkeys.json");
            WriteSettings(settingsA, "ocean", 73); WriteSettings(settingsB, "porcelain", 21);
            File.WriteAllText(keysA, "{\"format\":\"MashBoxStudio.Hotkeys\",\"version\":1,\"overrides\":[{\"id\":8,\"bindings\":[{\"key\":289}]},{\"id\":-1234567,\"bindings\":[{\"key\":290}]}]}\n");
            File.WriteAllText(keysB, "{\"format\":\"MashBoxStudio.Hotkeys\",\"version\":1,\"overrides\":[{\"id\":8,\"bindings\":[{\"key\":287}]}]}");
            string sourceSettings = File.ReadAllText(settingsA), sourceKeys = File.ReadAllText(keysA);
            StudioPreferenceStore.ImportSettings(settingsA); StudioShortcuts.Import(keysA);
            Check(StudioSettings.Current.theme == "ocean" && StudioSettings.Current.undoLimit == 73, "Loaded settings applied");
            Check(StudioShortcuts.Label(StudioShortcut.Edge) == "F8", "Loaded hotkeys applied");
            Check(StudioPreferenceStore.SettingsPath == settingsA && StudioPreferenceStore.HotkeysPath == keysA, "Independent active file paths selected");
            Check(File.ReadAllText(settingsA) == sourceSettings && File.ReadAllText(keysA) == sourceKeys, "Loading never rewrites input files");
            DateTime st = File.GetLastWriteTimeUtc(settingsA), kt = File.GetLastWriteTimeUtc(keysA);
            Bootstrap(); StudioShortcuts.Reload(); StudioSettings.Reload(); StudioPreferenceStore.Flush();
            Check(File.ReadAllText(settingsA) == sourceSettings && File.GetLastWriteTimeUtc(settingsA) == st, "Startup leaves loaded settings bytes and timestamp unchanged");
            Check(File.ReadAllText(keysA) == sourceKeys && File.GetLastWriteTimeUtc(keysA) == kt, "Reload leaves loaded hotkeys bytes and timestamp unchanged");
            Check(File.ReadAllText(keysA).Contains("-1234567"), "Unregistered legacy command survives reload");
            var bevel = Register("Mesh Tools", "Bevel…"); var shaded = Register("Mesh Display", "Shaded Wireframe");
            Check(StudioShortcuts.Pressed(bevel, new Event { type = EventType.KeyDown, keyCode = KeyCode.B, control = true }), "Ctrl B is standard Bevel default");
            Check(StudioShortcuts.Pressed(shaded, new Event { type = EventType.KeyDown, keyCode = KeyCode.BackQuote }), "Backtick is standard shaded wireframe default");
            Check(StudioShortcuts.Pressed(shaded, new Event { type = EventType.KeyDown, keyCode = KeyCode.BackQuote, shift = true }), "Shift backtick also matches tilde toggle");
            StudioShortcuts.Set(StudioShortcut.SelectAll, new[] { new StudioShortcuts.Binding(KeyCode.BackQuote) });
            Check(StudioShortcuts.Bindings(shaded).Length == 0, "New default yields to existing explicit chord assignment");
            StudioShortcuts.Reload(); Check(StudioShortcuts.Bindings(shaded).Length == 0, "Reload preserves custom chord priority");
            StudioShortcuts.Reset(StudioShortcut.SelectAll);
            Check(StudioShortcuts.Bindings(shaded).Length == 1, "Free chord restores standard default");
            StudioShortcuts.Set(shaded, Array.Empty<StudioShortcuts.Binding>()); StudioShortcuts.Reload();
            Check(StudioShortcuts.Bindings(shaded).Length == 0, "Explicit unassignment stays unassigned");
            StudioShortcuts.Reset(shaded);
            StudioShortcuts.Set(shaded, new[] { new StudioShortcuts.Binding(KeyCode.F10) });
            StudioShortcuts.Set(StudioShortcut.Edge, new[] { new StudioShortcuts.Binding(KeyCode.F9) });
            StudioSettings.Current.undoLimit = 74; StudioSettings.Save(); StudioPreferenceStore.Flush();
            Check(File.ReadAllText(keysA).Contains("-1234567"), "Explicit edit retains unregistered commands");
            Check(File.ReadAllText(settingsA).Contains("User.Marker"), "Explicit settings edit retains other entries");
            ReloadStore();
            Check(StudioSettings.Current.undoLimit == 74 && StudioShortcuts.Label(StudioShortcut.Edge) == "F9", "Edits save to loaded files");
            Check(StudioShortcuts.Label(shaded) == "F10", "User override wins over new standard default");
            StudioPreferenceStore.ImportSettings(settingsB);
            Check(StudioPreferenceStore.HotkeysPath == keysA && StudioShortcuts.Label(StudioShortcut.Edge) == "F9", "Loading settings leaves selected hotkeys alone");
            StudioShortcuts.Import(keysB);
            Check(StudioSettings.Current.undoLimit == 21 && StudioPreferenceStore.SettingsPath == settingsB, "Loading hotkeys leaves selected settings alone");
            StudioPreferenceStore.ImportSettings(settingsA); StudioShortcuts.Import(keysA);
            string activeSettings = File.ReadAllText(settingsA), activeKeys = File.ReadAllText(keysA);
            StudioPreferenceStore.WriteFactoryDefaults();
            Check(!File.ReadAllText(StudioPreferenceStore.DefaultSettingsPath).Contains("User.Marker"), "Factory settings contain no user data");
            Check(!File.ReadAllText(StudioPreferenceStore.DefaultHotkeysPath).Contains("-1234567"), "Factory hotkeys contain no user data");
            string factorySettings = File.ReadAllText(StudioPreferenceStore.DefaultSettingsPath), factoryKeys = File.ReadAllText(StudioPreferenceStore.DefaultHotkeysPath);
            StudioPreferenceStore.ImportSettings(StudioPreferenceStore.DefaultSettingsPath); StudioShortcuts.Import(StudioPreferenceStore.DefaultHotkeysPath);
            Check(StudioShortcuts.Label(shaded).Contains("BackQuote") && StudioSettings.Current.theme == "graphite", "Factory files load independent defaults");
            StudioSettings.Current.undoLimit = 99; StudioSettings.Save(); StudioPreferenceStore.Flush();
            StudioShortcuts.Set(StudioShortcut.Edge, new[] { new StudioShortcuts.Binding(KeyCode.F11) });
            Check(StudioPreferenceStore.SettingsPath != StudioPreferenceStore.DefaultSettingsPath && StudioPreferenceStore.HotkeysPath != StudioPreferenceStore.DefaultHotkeysPath, "Editing factory presets creates personal files");
            Check(File.ReadAllText(StudioPreferenceStore.DefaultSettingsPath) == factorySettings && File.ReadAllText(StudioPreferenceStore.DefaultHotkeysPath) == factoryKeys, "Factory files remain unchanged by edits");
            Check(File.ReadAllText(settingsA) == activeSettings && File.ReadAllText(keysA) == activeKeys, "Factory edits leave existing personal files alone");
            StudioPreferenceStore.ImportSettings(settingsA); StudioShortcuts.Import(keysA);
            string manifest = File.ReadAllText(StudioPreferenceStore.SelectionPath);
            string invalid = Path.Combine(Folder, "Invalid profile.json"); File.WriteAllText(invalid, "{}");
            bool rejected = false; try { StudioPreferenceStore.ImportSettings(invalid); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && File.ReadAllText(StudioPreferenceStore.SelectionPath) == manifest, "Invalid settings cannot replace last-loaded path");
            rejected = false; try { StudioShortcuts.Import(invalid); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && File.ReadAllText(StudioPreferenceStore.SelectionPath) == manifest, "Invalid hotkeys cannot replace last-loaded path");
            File.Move(keysA, keysA + ".missing");
            try { ReloadStore(); Check(StudioPreferenceStore.HotkeysPath == canonicalKeys, "Missing last-loaded hotkeys fall back safely"); Check(File.ReadAllText(StudioPreferenceStore.SelectionPath) == manifest, "Missing file keeps remembered path for recovery"); }
            finally { File.Move(keysA + ".missing", keysA); ReloadStore(); }
            Check(StudioPreferenceStore.HotkeysPath == keysA && StudioShortcuts.Label(StudioShortcut.Edge) == "F9", "Returning profile resumes remembered bindings");
            Check(File.ReadAllText(canonicalSettings) == initialSettings && File.ReadAllText(canonicalKeys) == initialKeys, "Personal fixed files were never overwritten");
            Check(manifest.Contains("Profiles") && manifest.Contains("My hotkeys.json") && manifest.Contains("My settings.json"), "Last loaded files persist as project-relative JSON paths");
            File.WriteAllText(Path.Combine(Folder, "Canonical-settings-before.txt"), initialSettings); File.WriteAllText(Path.Combine(Folder, "Canonical-hotkeys-before.txt"), initialKeys);
            File.WriteAllText(Result("profile-persistence-result.txt"), "PASS " + checks + " persistence assertions");
        }
        catch (Exception e) { exit = 1; File.WriteAllText(Result("profile-persistence-result.txt"), "FAIL after " + checks + "\n" + e); }
        EditorApplication.Exit(exit);
    }
    public static void VerifyFreshLaunch()
    {
        int exit = 0;
        try
        {
            Bootstrap();
            Check(StudioPreferenceStore.SettingsPath.EndsWith(Path.Combine("Profiles", "My settings.json")), "Fresh launch selects remembered settings file");
            Check(StudioPreferenceStore.HotkeysPath.EndsWith(Path.Combine("Profiles", "My hotkeys.json")), "Fresh launch selects remembered hotkeys file");
            Check(StudioSettings.Current.theme == "ocean" && StudioSettings.Current.undoLimit == 74, "Fresh launch restores edited settings");
            Check(StudioShortcuts.Label(StudioShortcut.Edge) == "F9", "Fresh launch restores edited hotkeys");
            var shaded = Register("Mesh Display", "Shaded Wireframe"); Check(StudioShortcuts.Label(shaded) == "F10", "Fresh launch respects custom override of new default");
            Check(File.ReadAllText(StudioPreferenceStore.HotkeysPath).Contains("-1234567"), "Fresh launch preserves unregistered command");
            Check(File.ReadAllText(Path.Combine(Folder, "Settings.json")) == File.ReadAllText(Path.Combine(Folder, "Canonical-settings-before.txt")), "Fresh launch preserves original settings file");
            Check(File.ReadAllText(Path.Combine(Folder, "Hotkeys.json")) == File.ReadAllText(Path.Combine(Folder, "Canonical-hotkeys-before.txt")), "Fresh launch preserves original hotkeys file");
            File.WriteAllText(Result("profile-persistence-fresh-result.txt"), "PASS " + checks + " fresh launch assertions");
        }
        catch (Exception e) { exit = 1; File.WriteAllText(Result("profile-persistence-fresh-result.txt"), e.ToString()); }
        EditorApplication.Exit(exit);
    }
}
