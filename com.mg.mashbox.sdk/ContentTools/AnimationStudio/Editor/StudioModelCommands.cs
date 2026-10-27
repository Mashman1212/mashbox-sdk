using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    // Metadata only: never retain document/view/action closures in the global registry.
    // A categorized StudioMenu is the single declaration of availability and execution.
    internal static class StudioModelCommands
    {
        private static readonly Dictionary<string, StudioShortcut> keys = new Dictionary<string, StudioShortcut>(StringComparer.Ordinal);
        private static readonly Dictionary<StudioShortcut, string> paths = new Dictionary<StudioShortcut, string>();
        private static readonly Dictionary<StudioShortcut, StudioShortcuts.Definition> definitions = new Dictionary<StudioShortcut, StudioShortcuts.Definition>();
        private static StudioShortcuts.Definition[] ordered;
        private static WeakReference<StudioModelEditor> active;
        private static readonly Dictionary<string, StudioShortcut> aliases = new Dictionary<string, StudioShortcut>(StringComparer.Ordinal)
        {
            {"File/Save Workspace", StudioShortcut.Save}, {"File/Save Workspace As…", StudioShortcut.SaveAs},
            {"Edit/Undo", StudioShortcut.Undo}, {"Edit/Redo", StudioShortcut.Redo}, {"Edit/Duplicate", StudioShortcut.Duplicate},
            {"Edit/Delete", StudioShortcut.Delete}, {"Edit/Preferences…", StudioShortcut.Preferences},
            {"Select/All", StudioShortcut.SelectAll}, {"Select/Object Mode", StudioShortcut.Object},
            {"Select/Vertex", StudioShortcut.Vertex}, {"Select/Edge", StudioShortcut.Edge}, {"Select/Face", StudioShortcut.Face},
            {"Select/Frame Selection", StudioShortcut.Frame}, {"Modify/Edit Pivot", StudioShortcut.Pivot},
            {"Modify/Select", StudioShortcut.Select}, {"Modify/Move", StudioShortcut.Move},
            {"Modify/Rotate", StudioShortcut.Rotate}, {"Modify/Scale", StudioShortcut.Scale},
            {"Mesh Tools/Extrude", StudioShortcut.Extrude}, {"Mesh Display/X-ray Mode", StudioShortcut.XRay}
        };
        private static string Normalize(string text) => text.Replace("...", "…").Trim();
        private static string Canonical(string category, string path)
        {
            string key = Normalize(category + "/" + path);
            // Preserve the AO shortcut identity when Lighting is renamed Render.
            if (key.StartsWith("Render/", StringComparison.Ordinal)) return "Lighting/" + key.Substring("Render/".Length);
            // Keep stored shortcut identities while Deform moves into Mesh Tools.
            if (key.StartsWith("Mesh Tools/Deform/", StringComparison.Ordinal)) return key.Substring("Mesh Tools/".Length);
            if (key == "Modify/Lock Pivot") return "Modify/Edit Pivot";
            if (key == "Mesh/UV Editor") return "UV/UV Editor";
            return key;
        }
        internal static string ToolbarKey(string category, string label, string explicitKey) => Canonical(category, explicitKey ?? label);
        private static StudioShortcut Hash(string key)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char ch in key) { hash ^= ch; hash *= 16777619; }
                return (StudioShortcut)(int)(hash | 0x80000000u);
            }
        }
        internal static readonly string[] StandardKeys = { "Mesh Tools/Bevel…", "Mesh Display/Shaded Wireframe", "Mesh Tools/Merge Vertices", "Mesh Tools/Target Weld", "Mesh Tools/Insert Edge Loop" };
        internal static StudioShortcut StableId(string key) => Hash(key);
        internal static StudioShortcuts.Binding[] StandardBindings(string key)
        {
            if (key == StandardKeys[0]) return new[] { new StudioShortcuts.Binding(KeyCode.B, StudioModifiers.Action) };
            if (key == StandardKeys[1]) return new[] { new StudioShortcuts.Binding(KeyCode.BackQuote, allowShift: true) };
            if (key == "Mesh Tools/Merge Vertices") return new[] { new StudioShortcuts.Binding(KeyCode.M) };
            if (key == "Mesh Tools/Target Weld") return new[] { new StudioShortcuts.Binding(KeyCode.T) };
            if (key == "Mesh Tools/Insert Edge Loop") return new[] { new StudioShortcuts.Binding(KeyCode.X, StudioModifiers.Action | StudioModifiers.Shift) };
            return Array.Empty<StudioShortcuts.Binding>();
        }
        internal static bool ValidKey(StudioShortcut id, string key) => (int)id < 0 && !string.IsNullOrEmpty(key) && key.Length <= 512 && key.IndexOf('/') > 0 && Hash(key) == id;
        internal static string Key(StudioShortcut id) => (int)id < 0 && paths.TryGetValue(id, out var path) ? path : null;
        internal static StudioShortcuts.Definition Find(StudioShortcut id) => definitions.TryGetValue(id, out var value) ? value : null;
        internal static string Category(StudioShortcut id) => definitions.TryGetValue(id, out var definition) ? definition.group : null;
        internal static StudioShortcut? Register(string category, string label, string explicitKey = null)
        {
            if (category == "Display") return null; // Display controls live in their menu/toolbars.
            if (label.Contains("No recent files") || label.Contains("SDK rules unavailable")) return null;
            string key = Canonical(category, explicitKey ?? label);
            if (keys.TryGetValue(key, out var existing)) return existing;
            StudioShortcut id = aliases.TryGetValue(key, out var alias) ? alias : Hash(key);
            if (paths.TryGetValue(id, out var collision) && collision != key)
                throw new InvalidOperationException("Studio command identifier collision: " + collision + " and " + key + ". Supply a distinct commandKey.");
            var defaults = StudioShortcuts.Find(id)?.defaults ?? StandardBindings(key);
            int slash = key.IndexOf('/');
            bool deform = key.StartsWith("Deform/", StringComparison.Ordinal);
            definitions[id] = new StudioShortcuts.Definition(id, deform ? "Mesh Tools" : key.StartsWith("Lighting/", StringComparison.Ordinal) ? "Render" : key.Substring(0, slash), deform ? key : key == "Mesh Tools/Merge Vertices" ? "Merge to Center" : key.Substring(slash + 1), defaults);
            paths[id] = key; keys[key] = id; ordered = null;
            StudioShortcuts.CatalogChanged();
            return id;
        }
        private static string BuiltinGroup(StudioShortcut id, string fallback)
        {
            switch (id)
            {
                case StudioShortcut.Save: case StudioShortcut.SaveAs: return "File";
                case StudioShortcut.Undo: case StudioShortcut.Redo: case StudioShortcut.Duplicate: case StudioShortcut.Delete:
                case StudioShortcut.Preferences: case StudioShortcut.Cancel: return "Edit";
                case StudioShortcut.Maximize: return "Window";
                case StudioShortcut.LargerHandles: case StudioShortcut.SmallerHandles: return "View";
                case StudioShortcut.Frame: case StudioShortcut.SelectAll: case StudioShortcut.Object: case StudioShortcut.Vertex:
                case StudioShortcut.Edge: case StudioShortcut.Face: case StudioShortcut.PaintSelection: case StudioShortcut.UnpaintSelection: return "Select";
                case StudioShortcut.Pivot: case StudioShortcut.VertexSnap: case StudioShortcut.GridSnap: case StudioShortcut.AxisMenu: return "Modify";
                case StudioShortcut.Select: case StudioShortcut.Move: case StudioShortcut.Rotate: case StudioShortcut.Scale: return "Modify";
                case StudioShortcut.Extrude: case StudioShortcut.CommitCut: case StudioShortcut.RemoveCut: return "Mesh Tools";
                case StudioShortcut.XRay: return "Mesh Display";
                case StudioShortcut.FrameAllUV: return "UV";
                case StudioShortcut.Orbit: case StudioShortcut.Pan: case StudioShortcut.Dolly: case StudioShortcut.MarkingMenu: return "Navigation";
                default: return fallback;
            }
        }
        internal static StudioShortcuts.Definition[] Definitions(StudioShortcuts.Definition[] builtins)
        {
            if (ordered != null) return ordered;
            var combined = builtins.Where(d => d.id != StudioShortcut.Preferences).Select(d => Find(d.id) ?? new StudioShortcuts.Definition(d.id, BuiltinGroup(d.id, d.group), d.name, d.defaults))
                .Concat(definitions.Values.Where(d => (int)d.id < 0));
            int Order(string group) { int i = Array.IndexOf(StudioModelEditor.ModelMenuTitles, group); return i < 0 ? int.MaxValue : i; }
            return ordered = combined.OrderBy(d => Order(d.group)).ThenBy(d => d.group, StringComparer.Ordinal).ThenBy(d => d.name, StringComparer.Ordinal).ToArray();
        }
        internal static void SetActive(StudioModelEditor editor) => active = new WeakReference<StudioModelEditor>(editor);
        internal static void RefreshActiveCatalog() { if (active != null && active.TryGetTarget(out var editor)) editor.RefreshModelCommandCatalog(); }
        internal static void Release(StudioModelEditor editor)
        { if (active != null && active.TryGetTarget(out var target) && ReferenceEquals(editor, target)) active = null; }
    }
}
