using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEngine;

namespace MashBoxSDK.Maps.Editing
{
    public sealed partial class MapEditSession
    {
        readonly Dictionary<string, MapEditState> unityPullAnchors = new Dictionary<string, MapEditState>();
        static MapEditState CopyState(MapEditState state) => JsonUtility.FromJson<MapEditState>(JsonUtility.ToJson(state));

        public MapEditDocument CapturePullRequest()
        {
            var request = new MapEditDocument { sessionId = SessionId, sceneName = Scene.name, scenePath = Scene.path, exportedUtc = DateTime.UtcNow.ToString("O") };
            foreach (var entry in entries.Values)
            {
                // Only identities retained in the prepared source scene are eligible.
                if (!entry.target || !(entry.id.StartsWith("map:", StringComparison.Ordinal) || entry.id.StartsWith("new:", StringComparison.Ordinal))) continue;
                bool terrain = entry.target.GetComponent<MGTerrain>();
                var current = MapEditCodec.Capture(entry.target, Id, terrain);
                current.deleted = entry.deleted;
                if (entry.deleted) current.active = entry.before?.active ?? true;
                var original = CopyState(entry.before ?? current);
                if (terrain && !original.hasMesh)
                { original.hasMesh = true; original.meshTopology = current.meshTopology; original.vertices = current.vertices; }
                request.changes.Add(new MapEditChange { id = entry.id, sourceId = entry.id, sourcePath = entry.path ?? MapEditCodec.PathOf(entry.target),
                    before = unityPullAnchors.TryGetValue(entry.id, out var anchor) ? CopyState(anchor) : original, after = current });
            }
            if (request.changes.Count == 0) throw new InvalidOperationException("Prepare and re-export this map before pulling: no shared scene identities were found.");
            request.Validate();
            return request;
        }

        public int ApplyUnityPull(MapEditDocument delta)
        {
            delta.Validate();
            if (delta.sessionId != SessionId) throw new InvalidOperationException("Pull belongs to a different map editing session.");
            var changed = new List<MapEditChange>();
            foreach (var change in delta.changes)
            {
                if (change.kind != "existing" || !entries.TryGetValue(change.id, out var entry) || !entry.target)
                    throw new InvalidOperationException("Pull includes an unknown runtime object: " + change.id);
                var current = MapEditCodec.Capture(entry.target, Id, change.before.hasMesh);
                current.deleted = entry.deleted;
                if (entry.deleted) current.active = entry.before?.active ?? true;
                if (!MapEditCodec.Same(current, change.before)) throw new InvalidOperationException("Edited while pull was pending: " + current.name + ". Pull again.");
                if (current.hasSplines != change.after.hasSplines || current.hasLoft != change.after.hasLoft || current.hasMesh != change.after.hasMesh ||
                    current.hasMesh && current.meshTopology != change.after.meshTopology)
                    throw new InvalidOperationException("Component or terrain topology changed: " + current.name + ". Rebuild the map.");
                foreach (string reference in new[] { change.after.parentId, change.after.materialSourceId }.Concat(change.after.hasLoft ? change.after.loft.sources.Select(s => s.id) : Enumerable.Empty<string>()))
                    if (!string.IsNullOrEmpty(reference) && !Resolve(reference)) throw new InvalidOperationException("Pull references an object absent from this map. Rebuild the map: " + reference);
                if (!MapEditCodec.Same(current, change.after)) { ValidateEdit(entry.target); changed.Add(change); }
            }
            var parents = entries.Values.Where(e => e.target).ToDictionary(e => e.id, e => Id(e.target.parent));
            foreach (var change in changed) parents[change.id] = change.after.parentId;
            foreach (var change in changed)
            {
                var visited = new HashSet<string> { change.id };
                string parent = change.after.parentId;
                while (!string.IsNullOrEmpty(parent) && parents.TryGetValue(parent, out var next))
                { if (!visited.Add(parent)) throw new InvalidOperationException("Pull would create a hierarchy cycle."); parent = next; }
            }
            string rollback = Snapshot();
            var merged = Capture(false);
            try
            {
                foreach (var change in changed)
                {
                    var entry = entries[change.id];
                    if (change.after.hasMesh) TouchTerrain(entry.target.GetComponent<MGTerrain>()); else Touch(entry.target);
                    merged.changes.RemoveAll(c => c.id == change.id); merged.changes.Add(change);
                }
                if (changed.Count > 0) Restore(merged.ToJson());
                foreach (var change in delta.changes) unityPullAnchors[change.id] = CopyState(change.after);
            }
            catch { Restore(rollback); throw; }
            return changed.Count;
        }
    }
}
