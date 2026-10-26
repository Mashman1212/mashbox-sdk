using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal enum StudioShortcut
    {
        Select, Move, Rotate, Scale, Pivot, Frame, Object, Vertex, Edge, Face,
        Save, SaveAs, Duplicate, Delete, Extrude, Undo, Redo, Cancel, CommitCut, RemoveCut,
        VertexSnap, GridSnap, LargerHandles, SmallerHandles, Maximize, Preferences,
        Orbit, Pan, Dolly, MarkingMenu, FrameAllUV, SelectAll, AxisMenu, XRay, PaintSelection, UnpaintSelection
    }
    [Flags] internal enum StudioModifiers { None = 0, Shift = 1, Action = 2, Alt = 4 }

    // These bindings belong to Studio, independently of Unity's shortcut profiles.
    [InitializeOnLoad]
    internal static class StudioShortcuts
    {
        internal const string PreferenceKey = "MashBox.Studio.Controls.v1";
        [Serializable] internal sealed class Binding
        {
            public KeyCode key;
            public StudioModifiers modifiers;
            public bool allowShift;
            public bool allowModifiers;
            internal Binding(KeyCode key, StudioModifiers modifiers = StudioModifiers.None, bool allowShift = false, bool allowModifiers = false)
            { this.key = key; this.modifiers = modifiers; this.allowShift = allowShift; this.allowModifiers = allowModifiers; }
            internal Binding Copy() => new Binding(key, modifiers, allowShift, allowModifiers);
            internal bool Matches(Event evt, bool release = false)
            {
                if (evt == null || key == KeyCode.None) return false;
                var code = evt.isMouse ? (KeyCode)((int)KeyCode.Mouse0 + evt.button) : evt.keyCode;
                if (code == KeyCode.None && evt.character == '+') code = KeyCode.Plus;
                if (code == KeyCode.None && evt.character == '-') code = KeyCode.Minus;
                if (code != key) return false;
                if (release) return true; // Releasing modifiers first must still end a held shortcut.
                if (allowModifiers) return true;
                var actual = Modifiers(evt);
                if (allowShift) actual &= ~StudioModifiers.Shift;
                var expected = allowShift ? modifiers & ~StudioModifiers.Shift : modifiers;
                return actual == expected;
            }
            public override string ToString() =>
                ((modifiers & StudioModifiers.Action) != 0 ? (Application.platform == RuntimePlatform.OSXEditor ? "Cmd+" : "Ctrl+") : "") +
                ((modifiers & StudioModifiers.Alt) != 0 ? "Alt+" : "") +
                ((modifiers & StudioModifiers.Shift) != 0 ? "Shift+" : "") + KeyName(key);
        }
        internal sealed class Definition
        {
            internal readonly StudioShortcut id;
            internal readonly string group, name;
            internal readonly Binding[] defaults;
            internal Definition(StudioShortcut id, string group, string name, params Binding[] defaults)
            { this.id = id; this.group = group; this.name = name; this.defaults = defaults; }
        }
        [Serializable] internal sealed class Override
        {
            public StudioShortcut id;
            public string command;
            public List<Binding> bindings = new List<Binding>();
        }
        [Serializable] private sealed class Profile
        {
            public string format;
            public int version;
            public List<Override> overrides = new List<Override>();
        }
        internal static void Export(string path)
        {
            var exported = new Profile { format = "MashBoxStudio.Hotkeys", version = 1 };
            foreach (var definition in Definitions)
                exported.overrides.Add(new Override { id = definition.id, command = StudioModelCommands.Key(definition.id), bindings = Bindings(definition.id).Select(b => b.Copy()).ToList() });
            StudioPreferenceStore.WriteAtomic(path, JsonUtility.ToJson(exported, true));
        }
        internal static void Import(string path)
        {
            var imported = JsonUtility.FromJson<Profile>(System.IO.File.ReadAllText(path));
            if (imported == null || imported.format != "MashBoxStudio.Hotkeys" || imported.version != 1 || imported.overrides == null)
                throw new System.IO.InvalidDataException("This is not a supported MashBox Studio hotkeys file.");
            var ids = new HashSet<StudioShortcut>();
            foreach (var entry in imported.overrides)
            {
                if (entry == null || (Find(entry.id) == null && entry.id != StudioShortcut.Preferences && ((int)entry.id >= 0 || (!string.IsNullOrEmpty(entry.command) && !StudioModelCommands.ValidKey(entry.id, entry.command)))) || !ids.Add(entry.id) || entry.bindings == null || entry.bindings.Any(b => b == null || b.key == KeyCode.None || !Enum.IsDefined(typeof(KeyCode), b.key) || ((int)b.modifiers & ~7) != 0))
                    throw new System.IO.InvalidDataException("The hotkeys file contains invalid or duplicate bindings.");
            }
            imported.overrides.RemoveAll(entry => entry.id == StudioShortcut.Preferences); // Accept older exports without restoring the removed UI shortcut.
            // Commit to disk before changing live bindings; a failed import leaves them intact.
            StudioPreferenceStore.SelectHotkeys(path);
            profile = imported; NotifyChanged();
        }
        private static Profile profile;
        private static readonly Dictionary<StudioShortcut,Binding[]> bindingCache = new Dictionary<StudioShortcut,Binding[]>();
        private static readonly Dictionary<StudioShortcut,string> labelCache = new Dictionary<StudioShortcut,string>();
        internal static event Action changed;
        private static Binding Key(KeyCode key, StudioModifiers modifiers = StudioModifiers.None, bool shift = false, bool any = false) => new Binding(key, modifiers, shift, any);
        private static readonly Definition[] BuiltinDefinitions = {
            new Definition(StudioShortcut.Select,"Tools","Select",Key(KeyCode.Q,shift:true)),
            new Definition(StudioShortcut.Move,"Tools","Move",Key(KeyCode.W,shift:true)),
            new Definition(StudioShortcut.Rotate,"Tools","Rotate",Key(KeyCode.E,shift:true)),
            new Definition(StudioShortcut.Scale,"Tools","Scale",Key(KeyCode.R,shift:true)),
            new Definition(StudioShortcut.Pivot,"Tools","Edit pivot",Key(KeyCode.D,shift:true)),
            new Definition(StudioShortcut.Object,"Selection","Object mode",Key(KeyCode.Alpha1,shift:true)),
            new Definition(StudioShortcut.Vertex,"Selection","Vertex mode",Key(KeyCode.Alpha2,shift:true)),
            new Definition(StudioShortcut.Edge,"Selection","Edge mode",Key(KeyCode.Alpha3,shift:true)),
            new Definition(StudioShortcut.Face,"Selection","Face mode",Key(KeyCode.Alpha4,shift:true)),
            new Definition(StudioShortcut.SelectAll,"Selection","Select all",Key(KeyCode.A,StudioModifiers.Action)),
            new Definition(StudioShortcut.PaintSelection,"Select","Paint selection (hold + left drag)",Key(KeyCode.Tab,any:true)),
            new Definition(StudioShortcut.UnpaintSelection,"Select","Unpaint selection (hold + left drag)",Key(KeyCode.Tab,StudioModifiers.Action)),
            new Definition(StudioShortcut.Frame,"View","Frame selection",Key(KeyCode.F,shift:true)),
            new Definition(StudioShortcut.XRay,"View","X-ray mode",Key(KeyCode.X,StudioModifiers.Alt)),
            new Definition(StudioShortcut.Maximize,"View","Maximize / restore window",Key(KeyCode.F11),Key(KeyCode.Space,StudioModifiers.Shift)),
            new Definition(StudioShortcut.LargerHandles,"View","Increase handle size",Key(KeyCode.Equals,shift:true),Key(KeyCode.Plus,shift:true),Key(KeyCode.KeypadPlus,shift:true)),
            new Definition(StudioShortcut.SmallerHandles,"View","Decrease handle size",Key(KeyCode.Minus,shift:true),Key(KeyCode.KeypadMinus,shift:true)),
            new Definition(StudioShortcut.Save,"Workspace","Save workspace",Key(KeyCode.S,StudioModifiers.Action)),
            new Definition(StudioShortcut.SaveAs,"Workspace","Save workspace as",Key(KeyCode.S,StudioModifiers.Action|StudioModifiers.Shift)),
            new Definition(StudioShortcut.Undo,"Workspace","Undo",Key(KeyCode.Z,StudioModifiers.Action)),
            new Definition(StudioShortcut.Redo,"Workspace","Redo",Key(KeyCode.Y,StudioModifiers.Action),Key(KeyCode.Z,StudioModifiers.Action|StudioModifiers.Shift)),
            new Definition(StudioShortcut.Duplicate,"Workspace","Duplicate objects",Key(KeyCode.D,StudioModifiers.Action)),
            new Definition(StudioShortcut.Delete,"Workspace","Delete selection",Key(KeyCode.Delete,shift:true),Key(KeyCode.Backspace,shift:true)),
            new Definition(StudioShortcut.Extrude,"Mesh","Extrude",Key(KeyCode.E,StudioModifiers.Action)),
            new Definition(StudioShortcut.Cancel,"Mesh","Cancel / exit tool",Key(KeyCode.Escape,any:true)),
            new Definition(StudioShortcut.CommitCut,"Cut tool","Apply cut path",Key(KeyCode.Return,any:true),Key(KeyCode.KeypadEnter,any:true)),
            new Definition(StudioShortcut.RemoveCut,"Cut tool","Remove last cut segment",Key(KeyCode.Backspace,any:true)),
            new Definition(StudioShortcut.VertexSnap,"Snapping (hold)","Vertex snap",Key(KeyCode.V,shift:true)),
            new Definition(StudioShortcut.GridSnap,"Snapping (hold)","Grid snap",Key(KeyCode.X,shift:true)),
            new Definition(StudioShortcut.Orbit,"Mouse navigation","Orbit",Key(KeyCode.Mouse0,StudioModifiers.Alt,shift:true)),
            new Definition(StudioShortcut.Pan,"Mouse navigation","Pan",Key(KeyCode.Mouse2,StudioModifiers.Alt,shift:true)),
            new Definition(StudioShortcut.Dolly,"Mouse navigation","Zoom / dolly",Key(KeyCode.Mouse1,StudioModifiers.Alt,shift:true)),
            new Definition(StudioShortcut.MarkingMenu,"Mouse navigation","Marking menu / apply cuts",Key(KeyCode.Mouse1,shift:true),Key(KeyCode.Mouse1,StudioModifiers.Action)),
            new Definition(StudioShortcut.AxisMenu,"Mouse navigation","Object / World axes menu",Key(KeyCode.Mouse1,StudioModifiers.Action|StudioModifiers.Shift)),
            new Definition(StudioShortcut.FrameAllUV,"UV editor","Frame all UVs",Key(KeyCode.A,shift:true)),
            new Definition(StudioShortcut.Preferences,"Workspace","Open controls preferences")
        };
        internal static Definition[] Definitions => StudioModelCommands.Definitions(BuiltinDefinitions);
        internal static int Revision { get; private set; }
        private static readonly Dictionary<KeyCode, StudioShortcut[]> commandKeys = new Dictionary<KeyCode, StudioShortcut[]>();
        private static int commandKeyRevision = -1;
        private static bool defaultsQueued;
        internal static void CatalogChanged()
        {
            Revision++; labelCache.Clear(); bindingCache.Clear();
            if (defaultsQueued) return;
            defaultsQueued = true; EditorApplication.delayCall += RefreshFactoryDefaults;
        }
        private static void RefreshFactoryDefaults()
        { defaultsQueued = false; StudioPreferenceStore.WriteFactoryDefaults(); }
        internal static string FactoryJson()
        {
            var defaults = new Profile { format = "MashBoxStudio.Hotkeys", version = 1 };
            foreach (var definition in Definitions)
                defaults.overrides.Add(new Override { id = definition.id, command = StudioModelCommands.Key(definition.id), bindings = definition.defaults.Select(b => b.Copy()).ToList() });
            foreach (string key in StudioModelCommands.StandardKeys)
            {
                var id = StudioModelCommands.StableId(key);
                if (!defaults.overrides.Exists(o => o.id == id)) defaults.overrides.Add(new Override { id = id, command = key, bindings = StudioModelCommands.StandardBindings(key).ToList() });
            }
            return JsonUtility.ToJson(defaults, true);
        }
        internal static bool MatchModelCommand(Event evt, out StudioShortcut command)
        {
            command = default;
            if (evt == null || evt.type != EventType.KeyDown) return false;
            if (commandKeyRevision != Revision)
            {
                commandKeys.Clear();
                foreach (var group in Definitions.Where(d => (int)d.id < 0).SelectMany(d => Bindings(d.id).Select(b => (d.id, binding: b))).GroupBy(p => p.binding.key))
                    commandKeys[group.Key] = group.Select(p => p.id).Distinct().ToArray();
                commandKeyRevision = Revision;
            }
            if (!commandKeys.TryGetValue(evt.keyCode, out var candidates)) return false;
            foreach (var candidate in candidates) if (Pressed(candidate, evt)) { command = candidate; return true; }
            return false;
        }
        static StudioShortcuts() { Reload(); }
        internal static StudioModifiers Modifiers(Event evt) => (evt.shift ? StudioModifiers.Shift : 0) |
            (evt.control || evt.command ? StudioModifiers.Action : 0) | (evt.alt ? StudioModifiers.Alt : 0);
        internal static string KeyName(KeyCode key) => key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9 ? ((int)key-(int)KeyCode.Alpha0).ToString() :
            key == KeyCode.Mouse0 ? "Left mouse" : key == KeyCode.Mouse1 ? "Right mouse" : key == KeyCode.Mouse2 ? "Middle mouse" : key.ToString();
        internal static Definition Find(StudioShortcut action) => action == StudioShortcut.Preferences ? null : StudioModelCommands.Find(action) ?? Array.Find(BuiltinDefinitions, d => d.id == action);
        internal static Binding[] Bindings(StudioShortcut action)
        {
            if (bindingCache.TryGetValue(action,out var cached)) return cached;
            var entry = profile.overrides.Find(o => o.id == action);
            if (entry != null) return bindingCache[action] = entry.bindings.ToArray();
            var defaults = Find(action)?.defaults ?? Array.Empty<Binding>();
            // New standard chords must not steal an older explicit assignment.
            if (StudioModelCommands.StandardKeys.Any(key => StudioModelCommands.StableId(key) == action))
                defaults = defaults.Where(binding => !profile.overrides.Any(other => other.id != action && other.id != StudioShortcut.Preferences && !CanShare(action, other.id) && other.bindings.Any(b => Overlaps(binding, b)))).ToArray();
            return bindingCache[action] = defaults;
        }
        internal static bool Matches(StudioShortcut action, Event evt, bool release = false)
        {
            var bindings=Bindings(action);
            for(int i=0;i<bindings.Length;i++)if(bindings[i].Matches(evt,release))return true;
            return false;
        }
        internal static bool Pressed(StudioShortcut action, Event evt) => evt != null && evt.type == EventType.KeyDown && Matches(action,evt);
        internal static string Label(StudioShortcut action)
        {
            if(labelCache.TryGetValue(action,out var label))return label;
            return labelCache[action]=string.Join(" / ",Bindings(action).Select(b => b.ToString()));
        }
        internal static string MenuLabel(string label, StudioShortcut action) => Label(action) is string keys && keys.Length > 0 ? label+"\t"+keys : label;
        internal static void Set(StudioShortcut action, IEnumerable<Binding> bindings)
        {
            profile.overrides.RemoveAll(o => o.id == action);
            profile.overrides.Add(new Override { id = action, command = StudioModelCommands.Key(action), bindings = bindings.Where(b => b != null && b.key != KeyCode.None).Select(b => b.Copy()).ToList() });
            Save();
        }
        internal static void Reset(StudioShortcut? action = null)
        {
            if (action.HasValue) profile.overrides.RemoveAll(o => o.id == action.Value); else profile.overrides.Clear();
            Save();
        }
        internal static void Reload()
        {
            Revision++; bindingCache.Clear();labelCache.Clear();
            try { profile = JsonUtility.FromJson<Profile>(StudioPreferenceStore.GetString(PreferenceKey,"")) ?? new Profile(); }
            catch (Exception ex) { Debug.LogWarning("MashBox Studio could not load " + StudioPreferenceStore.HotkeysPath + ": " + ex.Message); profile = new Profile(); }
            if (profile.overrides == null) profile.overrides = new List<Override>();
            profile.overrides.RemoveAll(o => o == null || o.bindings == null); // Retain commands not registered yet (and legacy IDs without command paths).
            foreach (var entry in profile.overrides) entry.bindings.RemoveAll(b => b == null || !Enum.IsDefined(typeof(KeyCode),b.key));
            profile.format = "MashBoxStudio.Hotkeys"; profile.version = 1;
            // Reload is read-only: never replace a user's file with a filtered or
            // temporarily incomplete startup catalog.
        }
        private static void Save()
        {
            bindingCache.Clear();labelCache.Clear();
            profile.format = "MashBoxStudio.Hotkeys"; profile.version = 1;
            StudioPreferenceStore.SetString(PreferenceKey,JsonUtility.ToJson(profile, true)); NotifyChanged();
        }
        private static void NotifyChanged()
        {
            Revision++; bindingCache.Clear(); labelCache.Clear(); changed?.Invoke();
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (window.GetType().Namespace == typeof(StudioShortcuts).Namespace) window.Repaint();
        }
        private static bool UsedInUV(StudioShortcut action) => action == StudioShortcut.Select || action == StudioShortcut.Move ||
            action == StudioShortcut.Rotate || action == StudioShortcut.Scale || action == StudioShortcut.Frame || action == StudioShortcut.Cancel ||
            action == StudioShortcut.Undo || action == StudioShortcut.Redo || action == StudioShortcut.Save || action == StudioShortcut.SaveAs ||
            action == StudioShortcut.Preferences || action == StudioShortcut.SelectAll;
        internal static bool CanShare(StudioShortcut a, StudioShortcut b) =>
            (a == StudioShortcut.RemoveCut && b == StudioShortcut.Delete) || (b == StudioShortcut.RemoveCut && a == StudioShortcut.Delete) ||
            (a == StudioShortcut.FrameAllUV && !UsedInUV(b)) || (b == StudioShortcut.FrameAllUV && !UsedInUV(a));
        private static bool Overlaps(Binding binding, Binding other) => binding.key == other.key &&
            (binding.allowModifiers || other.allowModifiers || binding.modifiers == other.modifiers ||
             ((binding.allowShift || other.allowShift) && (binding.modifiers & ~StudioModifiers.Shift) == (other.modifiers & ~StudioModifiers.Shift)));
        internal static string Conflict(StudioShortcut action, Binding binding)
        {
            foreach (var definition in Definitions)
            {
                if (definition.id == action || CanShare(action,definition.id)) continue;
                foreach (var other in Bindings(definition.id))
                    if (Overlaps(binding, other))
                        return definition.name;
            }
            return null;
        }
    }
    // Shared by focused Studio utility windows. Multiple focus callbacks may
    // overlap while switching windows; restore Unity only after the last owner.
    internal sealed class StudioShortcutFocusGuard
    {
        internal static readonly System.Reflection.PropertyInfo Dispatch =
            typeof(UnityEditor.ShortcutManagement.ShortcutManager).Assembly.GetType("UnityEditor.ShortcutManagement.ShortcutIntegration")?
                .GetProperty("enabled", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        private static readonly HashSet<StudioShortcutFocusGuard> owners = new HashSet<StudioShortcutFocusGuard>();
        private static bool held, previous;
        internal bool IsAcquired => owners.Contains(this);
        static StudioShortcutFocusGuard() { AssemblyReloadEvents.beforeAssemblyReload += Reset; EditorApplication.quitting += Reset; }
        internal bool Acquire()
        {
            if (Dispatch == null || !Dispatch.CanRead || !Dispatch.CanWrite) return false;
            EditorApplication.delayCall -= RestoreIfUnused;
            if (!held) { previous = (bool)Dispatch.GetValue(null); Dispatch.SetValue(null, false); held = true; }
            owners.Add(this); return true;
        }
        internal void Release()
        {
            if (!owners.Remove(this) || owners.Count != 0) return;
            var evt = Event.current;
            if (evt != null && (evt.rawType == EventType.KeyDown || evt.rawType == EventType.KeyUp || evt.rawType == EventType.ExecuteCommand))
                EditorApplication.delayCall += RestoreIfUnused;
            else RestoreIfUnused();
        }
        private static void RestoreIfUnused()
        {
            EditorApplication.delayCall -= RestoreIfUnused;
            if (held && owners.Count == 0) { Dispatch.SetValue(null, previous); held = false; }
        }
        private static void Reset() { owners.Clear(); RestoreIfUnused(); }
    }

}
