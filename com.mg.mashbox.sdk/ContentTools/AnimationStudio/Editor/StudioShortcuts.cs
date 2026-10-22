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
        Orbit, Pan, Dolly, MarkingMenu, FrameAllUV, SelectAll, AxisMenu, XRay
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
            public List<Binding> bindings = new List<Binding>();
        }
        [Serializable] private sealed class Profile { public List<Override> overrides = new List<Override>(); }
        private static Profile profile;
        private static readonly Dictionary<StudioShortcut,Binding[]> bindingCache = new Dictionary<StudioShortcut,Binding[]>();
        private static readonly Dictionary<StudioShortcut,string> labelCache = new Dictionary<StudioShortcut,string>();
        internal static event Action changed;
        private static Binding Key(KeyCode key, StudioModifiers modifiers = StudioModifiers.None, bool shift = false, bool any = false) => new Binding(key, modifiers, shift, any);
        internal static readonly Definition[] Definitions = {
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
            new Definition(StudioShortcut.Delete,"Workspace","Delete objects",Key(KeyCode.Delete,shift:true),Key(KeyCode.Backspace,shift:true)),
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
        static StudioShortcuts() { Reload(); }
        internal static StudioModifiers Modifiers(Event evt) => (evt.shift ? StudioModifiers.Shift : 0) |
            (evt.control || evt.command ? StudioModifiers.Action : 0) | (evt.alt ? StudioModifiers.Alt : 0);
        internal static string KeyName(KeyCode key) => key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9 ? ((int)key-(int)KeyCode.Alpha0).ToString() :
            key == KeyCode.Mouse0 ? "Left mouse" : key == KeyCode.Mouse1 ? "Right mouse" : key == KeyCode.Mouse2 ? "Middle mouse" : key.ToString();
        internal static Definition Find(StudioShortcut action) => Array.Find(Definitions, d => d.id == action);
        internal static Binding[] Bindings(StudioShortcut action)
        {
            if (bindingCache.TryGetValue(action,out var cached)) return cached;
            var entry = profile.overrides.Find(o => o.id == action);
            return bindingCache[action] = entry != null ? entry.bindings.ToArray() : Find(action).defaults;
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
            profile.overrides.Add(new Override { id = action, bindings = bindings.Where(b => b != null && b.key != KeyCode.None).Select(b => b.Copy()).ToList() });
            Save();
        }
        internal static void Reset(StudioShortcut? action = null)
        {
            if (action.HasValue) profile.overrides.RemoveAll(o => o.id == action.Value); else profile.overrides.Clear();
            Save();
        }
        internal static void Reload()
        {
            bindingCache.Clear();labelCache.Clear();
            try { profile = JsonUtility.FromJson<Profile>(EditorPrefs.GetString(PreferenceKey,"")) ?? new Profile(); }
            catch { profile = new Profile(); }
            if (profile.overrides == null) profile.overrides = new List<Override>();
            profile.overrides.RemoveAll(o => o == null || Find(o.id) == null || o.bindings == null);
            foreach (var entry in profile.overrides) entry.bindings.RemoveAll(b => b == null || !Enum.IsDefined(typeof(KeyCode),b.key));
        }
        private static void Save()
        {
            bindingCache.Clear();labelCache.Clear();
            EditorPrefs.SetString(PreferenceKey,JsonUtility.ToJson(profile)); changed?.Invoke();
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
        internal static string Conflict(StudioShortcut action, Binding binding)
        {
            foreach (var definition in Definitions)
            {
                if (definition.id == action || CanShare(action,definition.id)) continue;
                foreach (var other in Bindings(definition.id))
                    if (binding.key == other.key && (binding.allowModifiers || other.allowModifiers || binding.modifiers == other.modifiers ||
                        ((binding.allowShift || other.allowShift) && (binding.modifiers & ~StudioModifiers.Shift) == (other.modifiers & ~StudioModifiers.Shift))))
                        return definition.name;
            }
            return null;
        }
    }
}
