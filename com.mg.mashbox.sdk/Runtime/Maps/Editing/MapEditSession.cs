using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using MashBoxSDK.Maps.Spline;
using MashBoxSDK.Maps.TerrainSystem;
using Object = UnityEngine.Object;

namespace MashBoxSDK.Maps.Editing
{
    /// <summary>Owns only map authoring state. The host owns gameplay input, cameras, and the rider.</summary>
    public sealed partial class MapEditSession
    {
        public sealed class Entry
        {
            public Transform target;
            public string id, sourceId, path, kind = "existing", cloneRootId, clonePath, materialSourceId;
            public MapEditState before;
            public bool deleted, touched;
        }
        public Scene Scene { get; }
        public string SessionId { get; } = Guid.NewGuid().ToString("N");
        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        readonly Dictionary<Transform, Entry> byTransform = new Dictionary<Transform, Entry>();
        readonly Func<Transform, bool> allowed;
        readonly Dictionary<Transform, string> stableIds;
        readonly HashSet<MultiSplineLoft> pendingLofts = new HashSet<MultiSplineLoft>();
        readonly HashSet<MultiSplineLoft> previewedLofts = new HashSet<MultiSplineLoft>();
        static readonly Unity.Profiling.ProfilerMarker FlushMarker = new Unity.Profiling.ProfilerMarker("MapEditor.Geometry.Flush");
        readonly List<MultiSplineLoft> lofts = new List<MultiSplineLoft>();
        readonly List<MGTerrain> terrains = new List<MGTerrain>();
        readonly List<RuntimeMeshCombiner> combiners = new List<RuntimeMeshCombiner>();
        readonly HashSet<Transform> checkedCombinerTargets = new HashSet<Transform>();
        readonly Dictionary<Transform, bool> staticBatches = new Dictionary<Transform, bool>();
        readonly Dictionary<Transform, string> editBlockReasons = new Dictionary<Transform, string>();
        bool transformsDirty;
        public int HierarchyVersion { get; private set; }
        public IReadOnlyList<MGTerrain> Terrains => terrains;
        public IEnumerable<Entry> Entries => entries.Values.Where(e => e.target && !e.deleted);
        public MapEditSession(Scene scene, Func<Transform, bool> allowed)
        {
            Scene = scene; this.allowed = allowed; stableIds = MapEditCodec.StableIds(scene);
            var names = new Dictionary<string, int>();
            foreach (var root in scene.GetRootGameObjects()) RegisterTree(root.transform, Segment(root.transform, names));
            foreach (var entry in entries.Values) entry.before = MapEditCodec.Capture(entry.target, Id);
            // Opening/browsing the editor must preserve the map's combined rendering.
            // Only a batch containing edited geometry needs to expose its source renderers.
            foreach (var root in scene.GetRootGameObjects())
                combiners.AddRange(root.GetComponentsInChildren<RuntimeMeshCombiner>(true));
        }
        static string Segment(Transform target, Dictionary<string, int> names)
        {
            string name = target.name;
            names.TryGetValue(name, out int occurrence); names[name] = occurrence + 1;
            return Uri.EscapeDataString(name) + "#" + occurrence;
        }
        void RegisterTree(Transform target, string path)
        {
            if (!allowed(target) || IsGenerated(target)) return;
            var entry = new Entry { target = target, id = stableIds.TryGetValue(target, out var stable) ? stable : path, path = path };
            entries.Add(entry.id, entry); byTransform.Add(target, entry);
            IndexComponents(target);
            var names = new Dictionary<string, int>();
            foreach (Transform child in target) RegisterTree(child, path + "/" + Segment(child, names));
        }
        void IndexComponents(Transform target)
        {
            if (target.TryGetComponent(out MultiSplineLoft loft)) lofts.Add(loft);
            if (target.TryGetComponent(out MGTerrain terrain)) terrains.Add(terrain);
        }
        public void InvalidateHierarchy()
        {
            HierarchyVersion++;
            staticBatches.Clear();
            editBlockReasons.Clear();
            checkedCombinerTargets.Clear();
        }
        public bool IsStaticallyBatched(Transform target)
        {
            if (!target) return false;
            if (!staticBatches.TryGetValue(target, out bool result))
            {
                foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
                    if (renderer.isPartOfStaticBatch)
                    {
                        // Loft preflight checks its retained local source. Its old
                        // root and baked visual chunks are replaced on regeneration.
                        if (renderer.GetComponent<MultiSplineLoft>() || renderer.GetComponentInParent<LoftVisualChunks>()) continue;
                        result = true; break;
                    }
                staticBatches[target] = result;
            }
            return result;
        }
        void ExposeEditedGeometry(Transform target)
        {
            if (!checkedCombinerTargets.Add(target)) return;
            foreach (var combiner in combiners)
                if (combiner && combiner.enabled && (target.IsChildOf(combiner.transform) || combiner.transform.IsChildOf(target)) && combiner.ContainsCombinedSource(target))
                    combiner.enabled = false;
        }
        public static bool IsGenerated(Transform target)
        {
            if (target.GetComponent<MapEditSceneIdentity>()) return true;
            if (((target.hideFlags | target.gameObject.hideFlags) & HideFlags.DontSave) != 0 || target.GetComponentInParent<LoftVisualChunks>()) return true;
            if (target.parent && target.parent.GetComponent<MultiSplineLoft>() &&
                (target.name == "Collider Chunks" || target.name == "Visual Chunks")) return true;
            return target.name == "Combined Mesh";
        }
        public string Id(Transform target) => target && byTransform.TryGetValue(target, out var entry) ? entry.id : target ? MapEditCodec.PathOf(target) : "";
        public Transform Resolve(string id) => !string.IsNullOrEmpty(id) && entries.TryGetValue(id, out var entry) ? entry.target : null;
        public Entry Get(Transform target) => target && byTransform.TryGetValue(target, out var entry) ? entry : null;
        public bool Contains(Transform target) => Get(target) != null;
        public bool Visible(Transform target)
        {
            for (var t = target; t; t = t.parent) if (Get(t)?.deleted == true) return false;
            return Contains(target);
        }
        public void Touch(Transform target)
        {
            var entry = Get(target) ?? throw new InvalidOperationException("Select an authored map object.");
            ValidateEdit(target);
            ExposeEditedGeometry(target);
            foreach (var loft in lofts)
                if (loft && Visible(loft.transform) && DependsOn(loft, target))
                {
                    ExposeEditedGeometry(loft.transform);
                    loft.PrepareRuntimeEditing();
                }
            transformsDirty = true;
            entry.touched = true;
        }
        public string EditBlockReason(Transform target)
        {
            if (!target) return "Select an authored map object.";
            if (!editBlockReasons.TryGetValue(target, out string reason))
            {
                try { ValidateEdit(target); }
                catch (InvalidOperationException error) { reason = error.Message; }
                editBlockReasons[target] = reason;
            }
            return reason;
        }
        void ValidateEdit(Transform target)
        {
            if (!Contains(target)) throw new InvalidOperationException("Select an authored map object.");
            // Validate all affected lofts before exposing renderers or touching authoring data.
            foreach (var loft in lofts)
                if (loft && Visible(loft.transform) && DependsOn(loft, target)) loft.ValidateRuntimeEditing();
            if (IsStaticallyBatched(target)) throw new InvalidOperationException("This object is statically batched. Prepare the source map for runtime editing and export it again.");
        }
        static bool DependsOn(MultiSplineLoft loft, Transform target) =>
            loft.transform == target || loft.transform.IsChildOf(target) ||
            loft.Sources.Any(s => s?.container && (s.container.transform == target || s.container.transform.IsChildOf(target)));
        public void TouchTerrain(MGTerrain terrain)
        {
            Touch(terrain.transform);
            var entry = Get(terrain.transform);
            if (!entry.before.hasMesh)
            {
                var source = terrain.MeshFilter ? terrain.MeshFilter.sharedMesh : null;
                if (!source || !source.isReadable) throw new InvalidOperationException("The terrain source mesh is not readable.");
                entry.before.hasMesh = true; entry.before.meshTopology = MapEditCodec.Topology(source); entry.before.vertices = source.vertices;
            }
            terrain.PrepareRuntimeEditing();
        }
        public string Snapshot() { var document = Capture(false); document.exportedUtc = null; return JsonUtility.ToJson(document); }
        public MapEditDocument Capture(bool changedOnly = true, bool includeUnchanged = false)
        {
            var document = new MapEditDocument { sessionId = SessionId, scenePath = Scene.path, sceneName = Scene.name,
                exportedUtc = DateTime.UtcNow.ToString("O") };
            foreach (var entry in entries.Values)
            {
                if (!entry.target || (!entry.touched && entry.kind == "existing")) continue;
                var state = MapEditCodec.Capture(entry.target, Id, entry.before != null && entry.before.hasMesh);
                state.materialSourceId = entry.materialSourceId;
                state.deleted = entry.deleted;
                if (entry.deleted) state.active = entry.before?.active ?? true;
                var baseline = changedOnly && unityPullAnchors.TryGetValue(entry.id, out var anchor) ? anchor : entry.before;
                bool acknowledged = changedOnly && unityPullAnchors.ContainsKey(entry.id);
                if (changedOnly && !includeUnchanged && (entry.kind == "existing" ? MapEditCodec.Same(baseline, state) : entry.deleted)) continue;
                if (changedOnly && entry.kind != "existing" && entry.deleted && !acknowledged) continue;
                document.changes.Add(new MapEditChange { id = entry.id, sourcePath = entry.path ?? MapEditCodec.PathOf(entry.target), kind = acknowledged ? "existing" : entry.kind,
                    baselineFromUnity = acknowledged, sourceId = acknowledged ? entry.id : entry.sourceId ?? entry.id,
                    cloneRootId = entry.cloneRootId, clonePath = entry.clonePath, before = baseline, after = state });
            }
            // A created descendant of a deleted created parent is not an independent addition.
            document.changes.RemoveAll(c => c.kind != "existing" && Get(Resolve(c.id)) is Entry e && !Visible(e.target));
            return document;
        }
        public void Restore(string json)
        {
            InvalidateHierarchy();
            transformsDirty = true;
            var snapshot = JsonUtility.FromJson<MapEditDocument>(json);
            var states = snapshot.changes.ToDictionary(c => c.id, c => c.after);
            // Retain inactive runtime clones for redo; never rebuild them by invoking arbitrary Awake methods.
            foreach (var entry in entries.Values)
            {
                if (!entry.target || (!entry.touched && entry.kind == "existing")) continue;
                if (!states.TryGetValue(entry.id, out var state))
                {
                    if (entry.kind != "existing") { entry.deleted = true; entry.target.gameObject.SetActive(false); continue; }
                    state = entry.before;
                }
                // Pull merges only a few changed objects into the session snapshot. Avoid
                // rewriting/rebuilding every previously edited loft and terrain tile.
                var current = MapEditCodec.Capture(entry.target, Id, entry.before != null && entry.before.hasMesh);
                current.materialSourceId = entry.materialSourceId;
                current.deleted = entry.deleted;
                if (entry.deleted) current.active = entry.before?.active ?? true;
                if (MapEditCodec.Same(current, state)) continue;
                Transform parent = Resolve(state.parentId);
                if (parent != entry.target.parent) entry.target.SetParent(parent, false);
                MapEditCodec.Apply(entry.target, state, Resolve);
                entry.deleted = state.deleted;
                if (entry.before != null && entry.before.hasMesh)
                {
                    var terrain = entry.target.GetComponent<MGTerrain>();
                    terrain.PrepareRuntimeEditing();
                    terrain.MeshFilter.sharedMesh.vertices = state.hasMesh ? state.vertices : entry.before.vertices;
                    RefreshTerrain(terrain, true);
                }
                QueueDependents(entry.target);
            }
            Flush();
        }
        public void Delete(Transform target)
        {
            Touch(target); Get(target).deleted = true; target.gameObject.SetActive(false);
            InvalidateHierarchy();
            foreach (var loft in lofts)
            {
                if (!loft || !Visible(loft.transform) || loft.transform == target || loft.transform.IsChildOf(target)) continue;
                if (!loft.Sources.Any(s => s?.container && (s.container.transform == target || s.container.transform.IsChildOf(target)))) continue;
                Touch(loft.transform);
                loft.Sources.RemoveAll(s => s?.container && (s.container.transform == target || s.container.transform.IsChildOf(target)));
                pendingLofts.Add(loft);
            }
            QueueDependents(target);
        }
        public Transform Duplicate(Transform target)
        {
            Touch(target);
            if (string.IsNullOrEmpty(Get(target).path))
            {
                if (Entries.Any(e => e.target != target && e.target.IsChildOf(target)))
                    throw new InvalidOperationException("Duplicate newly created objects individually before grouping them.");
                var original = Get(target);
                var result = Create(original.kind, target.position, target.parent);
                var state = MapEditCodec.Capture(target, Id); state.name += " Copy";
                MapEditCodec.Apply(result, state, Resolve); Get(result).materialSourceId = original.materialSourceId;
                return result;
            }
            var staging = new GameObject("Runtime clone staging"); staging.SetActive(false);
            GameObject clone;
            try { clone = Object.Instantiate(target.gameObject, staging.transform); clone.SetActive(false); clone.transform.SetParent(target.parent, false); }
            finally { if (Application.isPlaying) Object.Destroy(staging); else Object.DestroyImmediate(staging); }
            if (!clone.transform.parent) SceneManager.MoveGameObjectToScene(clone, Scene);
            clone.name = target.name + " Copy";
            var pairs = new Dictionary<Transform, Transform>();
            Pair(target, clone.transform, pairs);
            string rootId = "new:" + Guid.NewGuid().ToString("N");
            foreach (var pair in pairs)
            {
                var original = Get(pair.Key); if (original == null) continue;
                var entry = new Entry { target = pair.Value, id = pair.Key == target ? rootId : "new:" + Guid.NewGuid().ToString("N"),
                    path = original.path ?? MapEditCodec.PathOf(pair.Key), before = MapEditCodec.Capture(pair.Key, Id, original.before != null && original.before.hasMesh), kind = pair.Key == target ? "duplicate" : "child",
                    sourceId = original.id,
                    cloneRootId = rootId, clonePath = MapEditCodec.PathOf(pair.Value, clone.transform), touched = true };
                entries.Add(entry.id, entry); byTransform.Add(entry.target, entry);
                IndexComponents(entry.target);
            }
            clone.transform.localPosition = target.localPosition;
            clone.transform.localRotation = target.localRotation; clone.transform.localScale = target.localScale;
            clone.SetActive(target.gameObject.activeSelf);
            foreach (var loft in clone.GetComponentsInChildren<MultiSplineLoft>(true))
                if (Contains(loft.transform)) { loft.PrepareRuntimeEditing(); pendingLofts.Add(loft); }
            combiners.AddRange(clone.GetComponentsInChildren<RuntimeMeshCombiner>(true));
            transformsDirty = true;
            InvalidateHierarchy();
            return clone.transform;
        }
        static void Pair(Transform source, Transform clone, Dictionary<Transform, Transform> pairs)
        {
            pairs.Add(source, clone);
            for (int i = 0; i < source.childCount && i < clone.childCount; i++) Pair(source.GetChild(i), clone.GetChild(i), pairs);
        }
        public Transform Create(string kind, Vector3 point, Transform parent = null)
        {
            if (kind != "empty" && kind != "spline" && kind != "loft") throw new ArgumentException("Unsupported creation type.");
            var go = new GameObject(kind == "spline" ? "Spline" : kind == "loft" ? "Loft" : "GameObject");
            SceneManager.MoveGameObjectToScene(go, Scene);
            go.transform.SetParent(parent, true); go.transform.position = point;
            if (kind == "spline")
            {
                var spline = go.AddComponent<SplineContainer>().Spline;
                spline.Add(new BezierKnot(Vector3.zero), TangentMode.AutoSmooth);
                spline.Add(new BezierKnot(Vector3.forward * 5), TangentMode.AutoSmooth);
            }
            string materialSource = null;
            if (kind == "loft")
            {
                var loft = go.AddComponent<MultiSplineLoft>(); loft.AutoRegenerate = false; loft.PrepareRuntimeEditing();
                // Reuse an existing map material; no runtime Shader.Find dependency.
                var material = Entries.Select(e => e.target.GetComponent<MeshRenderer>()).FirstOrDefault(r => r && r.sharedMaterial);
                if (material) { go.GetComponent<MeshRenderer>().sharedMaterial = material.sharedMaterial; materialSource = Id(material.transform); }
            }
            var entry = new Entry { target = go.transform, id = "new:" + Guid.NewGuid().ToString("N"), kind = kind, touched = true, materialSourceId = materialSource };
            entries.Add(entry.id, entry); byTransform.Add(entry.target, entry);
            IndexComponents(entry.target);
            transformsDirty = true;
            InvalidateHierarchy();
            return go.transform;
        }
        public void QueueDependents(Transform target)
        {
            if (!target) return;
            transformsDirty = true;
            // Component lookup across the entire scene was previously repeated for every drag step.
            foreach (var loft in lofts)
            {
                if (!loft || !Visible(loft.transform)) continue;
                if (DependsOn(loft, target))
                {
                    pendingLofts.Add(loft);
                }
            }
        }
        public void Flush(bool preview = false)
        {
            using var flushScope = FlushMarker.Auto();
            if (!preview && previewedLofts.Count > 0) pendingLofts.UnionWith(previewedLofts);
            if (pendingLofts.Count == 0 && !transformsDirty) return;
            if (pendingLofts.Count > 0)
            {
                var pending = pendingLofts.ToArray();
                foreach (var loft in pending) if (loft && loft.gameObject.activeInHierarchy) loft.ValidateRuntimeEditing();
                foreach (var loft in pending)
                {
                    if (loft && loft.gameObject.activeInHierarchy)
                    { ExposeEditedGeometry(loft.transform); loft.RegenerateRuntimeEditing(preview); }
                    if (preview) previewedLofts.Add(loft); else previewedLofts.Remove(loft);
                    // A failed rebuild remains pending, so Save cannot claim success.
                    pendingLofts.Remove(loft);
                }
            }
            if (transformsDirty && !preview) { Physics.SyncTransforms(); transformsDirty = false; }
        }
        public static void RefreshTerrain(MGTerrain terrain, bool collision)
        {
            var mesh = terrain.MeshFilter.sharedMesh;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            if (mesh.uv.Length == mesh.vertexCount) mesh.RecalculateTangents();
            terrain.NotifySurfaceMeshChanged(false, true);
            terrain.InvalidateRenderCache();
            if (collision) { terrain.RefreshSurfaceCollidersFromMesh(); terrain.ConformInstancesToSurface(); }
        }
        public void Sculpt(MGTerrain terrain, Vector3 center, float radius, float amount, bool flatten, float height)
        {
            TouchTerrain(terrain);
            var filter = terrain.MeshFilter; var mesh = filter.sharedMesh; var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = filter.transform.TransformPoint(vertices[i]);
                float distance = Vector2.Distance(new Vector2(world.x, world.z), new Vector2(center.x, center.z));
                if (distance >= radius) continue;
                float weight = 1 - distance / radius; weight = weight * weight * (3 - 2 * weight);
                world.y = flatten ? Mathf.Lerp(world.y, height, Mathf.Clamp01(Mathf.Abs(amount) * weight)) : world.y + amount * weight;
                vertices[i] = filter.transform.InverseTransformPoint(world);
            }
            mesh.vertices = vertices;
            RefreshTerrain(terrain, false);
        }
    }
}
