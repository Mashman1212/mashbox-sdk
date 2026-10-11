using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps.Editing
{
    /// <summary>Cached, flattened authoring hierarchy. An idle IMGUI pass never walks the scene.</summary>
    public sealed class MapEditHierarchy
    {
        public readonly struct Row
        {
            public readonly Transform target;
            public readonly int depth;
            public readonly bool hasChildren;
            public readonly string label;
            public Row(Transform target, int depth, bool hasChildren)
            { this.target = target; this.depth = depth; this.hasChildren = hasChildren; label = target.name; }
        }
        readonly MapEditSession session;
        readonly Dictionary<Transform, List<Transform>> children = new Dictionary<Transform, List<Transform>>();
        readonly List<Transform> roots = new List<Transform>();
        readonly HashSet<Transform> expanded = new HashSet<Transform>();
        readonly HashSet<Transform> matches = new HashSet<Transform>();
        readonly List<Row> rows = new List<Row>();
        int version = -1;
        bool dirty = true;
        string query;
        public int IndexBuilds { get; private set; }
        public int RowBuilds { get; private set; }
        public MapEditHierarchy(MapEditSession session) { this.session = session; }
        public bool IsExpanded(Transform target) => expanded.Contains(target);
        public void Toggle(Transform target) { if (!expanded.Remove(target)) expanded.Add(target); dirty = true; }
        public void CollapseAll() { expanded.Clear(); dirty = true; }
        public void Reveal(Transform target)
        {
            for (var parent = target ? target.parent : null; parent; parent = parent.parent)
                if (expanded.Add(parent)) dirty = true;
        }
        public IReadOnlyList<Row> GetRows(string search)
        {
            if (version != session.HierarchyVersion)
            {
                children.Clear(); roots.Clear();
                foreach (var entry in session.Entries)
                    if (session.Visible(entry.target)) children.Add(entry.target, new List<Transform>());
                foreach (var pair in children)
                {
                    var parent = pair.Key.parent;
                    if (parent && children.TryGetValue(parent, out var siblings)) siblings.Add(pair.Key);
                    else roots.Add(pair.Key);
                }
                roots.Sort(SiblingOrder);
                foreach (var nested in children.Values) nested.Sort(SiblingOrder);
                version = session.HierarchyVersion; dirty = true; IndexBuilds++;
            }
            search = string.IsNullOrWhiteSpace(search) ? "" : search;
            if (!dirty && query == search) return rows;
            query = search; dirty = false; rows.Clear(); matches.Clear(); RowBuilds++;
            bool filtering = search.Length != 0;
            if (filtering)
                foreach (var target in children.Keys)
                    if (target.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                        for (var parent = target; parent && children.ContainsKey(parent); parent = parent.parent)
                            if (!matches.Add(parent)) break;
            foreach (var root in roots) Append(root, 0, filtering);
            return rows;
        }
        static int SiblingOrder(Transform a, Transform b) => a.GetSiblingIndex().CompareTo(b.GetSiblingIndex());
        void Append(Transform target, int depth, bool filtering)
        {
            if (filtering && !matches.Contains(target)) return;
            var nested = children[target];
            rows.Add(new Row(target, depth, nested.Count != 0));
            if (filtering || expanded.Contains(target))
                foreach (var child in nested) Append(child, depth + 1, filtering);
        }
    }
}
