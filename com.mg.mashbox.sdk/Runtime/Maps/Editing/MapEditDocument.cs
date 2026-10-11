using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using MashBoxSDK.Maps.Spline;

namespace MashBoxSDK.Maps.Editing
{
    // Deliberately typed: no Unity instance IDs, arbitrary component deserialization, or asset paths to execute.
    [Serializable] public sealed class MapEditDocument
    {
        public int version = 1;
        public string sessionId, scenePath, sceneName, exportedUtc;
        public List<MapEditChange> changes = new List<MapEditChange>();
        public const long MaximumBytes = 128L * 1024 * 1024;
        public string ToJson() => JsonUtility.ToJson(this, true);
        public static MapEditDocument Read(string path)
        {
            if (new FileInfo(path).Length > MaximumBytes) throw new InvalidDataException("Map edit file exceeds 128 MB.");
            var result = JsonUtility.FromJson<MapEditDocument>(File.ReadAllText(path));
            result?.Validate();
            return result ?? throw new InvalidDataException("Empty map edit file.");
        }
        public void Validate()
        {
            if (version != 1 || string.IsNullOrEmpty(sessionId) || changes == null || changes.Count > 50000)
                throw new InvalidDataException("Unsupported map edit document.");
            var ids = new HashSet<string>();
            foreach (var change in changes)
            {
                if (change == null || string.IsNullOrEmpty(change.id) || !ids.Add(change.id) || change.after == null)
                    throw new InvalidDataException("Missing or duplicate object identity.");
                if (change.kind != "existing" && change.kind != "duplicate" && change.kind != "child" &&
                    change.kind != "empty" && change.kind != "spline" && change.kind != "loft")
                    throw new InvalidDataException("Unsupported object creation type.");
                if ((change.kind == "existing" || change.kind == "duplicate" || change.kind == "child") &&
                    (string.IsNullOrEmpty(change.sourcePath) || change.before == null))
                    throw new InvalidDataException("Missing original object state.");
                ValidateState(change.after);
                if (change.kind == "existing" || change.kind == "duplicate" || change.kind == "child") ValidateState(change.before);
            }
            var byId = changes.ToDictionary(c => c.id);
            foreach (var change in changes)
            {
                var visited = new HashSet<string> { change.id };
                string parent = change.after.parentId;
                while (!string.IsNullOrEmpty(parent) && byId.TryGetValue(parent, out var ancestor))
                {
                    if (!visited.Add(parent)) throw new InvalidDataException("Cyclic map hierarchy.");
                    parent = ancestor.after.parentId;
                }
            }
        }
        static void ValidateState(MapEditState state)
        {
            if (state.name == null || state.name.Length > 1024 || !Finite(state.position) || !Finite(state.scale) ||
                !Finite(state.rotation) || state.scale.sqrMagnitude > 1e12f)
                throw new InvalidDataException("Invalid transform values.");
            if (state.hasSplines)
            {
                if (state.splines == null || state.links == null) throw new InvalidDataException("Missing spline data.");
                foreach (var spline in state.splines)
                {
                    if (spline == null || spline.knots == null || spline.knots.Count > 100000) throw new InvalidDataException("Invalid spline.");
                    foreach (var knot in spline.knots)
                        if (knot == null || !Finite(knot.position) || !Finite(knot.tangentIn) || !Finite(knot.tangentOut) ||
                            !Finite(knot.rotation) || !float.IsFinite(knot.tension) || !Enum.IsDefined(typeof(TangentMode), knot.mode))
                            throw new InvalidDataException("Invalid spline knot.");
                }
                foreach (var link in state.links)
                    if (link.aSpline < 0 || link.bSpline < 0 || link.aSpline >= state.splines.Count || link.bSpline >= state.splines.Count ||
                        link.aKnot < 0 || link.bKnot < 0 || link.aKnot >= state.splines[link.aSpline].knots.Count || link.bKnot >= state.splines[link.bSpline].knots.Count)
                        throw new InvalidDataException("Invalid linked knot.");
            }
            if (state.hasMesh && (state.vertices == null || state.vertices.Length > 8000000 || state.vertices.Any(v => !Finite(v))))
                throw new InvalidDataException("Invalid terrain vertices.");
            if (state.hasLoft && (state.loft == null || state.loft.sources == null || state.loft.sources.Count > 128 ||
                state.loft.samples < 2 || state.loft.samples > 10000 || state.loft.across < 1 || state.loft.across > 256 ||
                !float.IsFinite(state.loft.segmentLength) || state.loft.segmentLength < .01f ||
                !float.IsFinite(state.loft.colliderLength) || state.loft.colliderLength < 1))
                throw new InvalidDataException("Invalid loft settings.");
            if (state.hasLoft)
            {
                if (!Enum.IsDefined(typeof(MultiSplineLoft.AcrossInterpolation), state.loft.interpolation) ||
                    !Enum.IsDefined(typeof(MultiSplineLoft.AlongResolutionMode), state.loft.resolution) ||
                    !Enum.IsDefined(typeof(MultiSplineLoft.NormalMode), state.loft.normals) ||
                    state.loft.sources.Any(s => s == null || string.IsNullOrEmpty(s.id) || s.spline < 0))
                    throw new InvalidDataException("Invalid loft source or mode.");
            }
        }
        static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        static bool Finite(Quaternion v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z) && float.IsFinite(v.w) &&
            v.x * v.x + v.y * v.y + v.z * v.z + v.w * v.w > .000001f;
        public static void WriteAtomic(string path, string json)
        {
            path = Path.GetFullPath(path);
            if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("Map edit file exceeds 128 MB.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, json);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    [Serializable] public sealed class MapEditChange
    {
        public bool baselineFromUnity;
        public string id, sourceId, sourcePath, kind = "existing", cloneRootId, clonePath;
        public MapEditState before, after;
    }
    [Serializable] public sealed class MapEditState
    {
        public string name, parentId, materialSourceId;
        public bool active, deleted, hasSplines, hasLoft, hasMesh;
        public Vector3 position, scale = Vector3.one;
        public Quaternion rotation = Quaternion.identity;
        public List<MapEditSpline> splines = new List<MapEditSpline>();
        public List<MapEditKnotLink> links = new List<MapEditKnotLink>();
        public MapEditLoft loft = new MapEditLoft();
        public string meshTopology;
        public Vector3[] vertices = Array.Empty<Vector3>();
    }
    [Serializable] public sealed class MapEditSpline { public bool closed; public List<MapEditKnot> knots = new List<MapEditKnot>(); }
    [Serializable] public sealed class MapEditKnotLink { public int aSpline, aKnot, bSpline, bKnot; }
    [Serializable] public sealed class MapEditKnot
    {
        public Vector3 position, tangentIn, tangentOut;
        public Quaternion rotation;
        public int mode;
        public float tension;
    }
    [Serializable] public sealed class MapEditLoft
    {
        public List<MapEditSource> sources = new List<MapEditSource>();
        public int samples, across, interpolation, resolution, normals;
        public float segmentLength, colliderLength;
        public bool closeAcross, closeAlong, capStart, capEnd, doubleSided, flipNormals;
    }
    [Serializable] public sealed class MapEditSource { public string id; public int spline; public bool reverse; }

    public static class MapEditCodec
    {
        public static Dictionary<string, Transform> Lookup(Scene scene)
        {
            var result = new Dictionary<string, Transform>();
            var roots = scene.GetRootGameObjects();
            void Index(Transform target, string prefix, Dictionary<string, int> occurrences)
            {
                string name = target.name;
                occurrences.TryGetValue(name, out int occurrence);
                occurrences[name] = occurrence + 1;
                string path = prefix + Uri.EscapeDataString(name) + "#" + occurrence;
                result[path] = target;
                if (target.childCount == 0) return;
                var children = new Dictionary<string, int>();
                for (int i = 0; i < target.childCount; i++) Index(target.GetChild(i), path + "/", children);
            }
            var rootOccurrences = new Dictionary<string, int>();
            foreach (var root in roots) Index(root.transform, "", rootOccurrences);
            foreach (var root in roots)
            {
                foreach (var identity in root.GetComponentsInChildren<MapEditSceneIdentity>(true))
                    foreach (var item in identity.objects)
                        if (item.target && !string.IsNullOrEmpty(item.id)) result[item.id] = item.target;
            }
            return result;
        }
        public static Dictionary<Transform, string> StableIds(Scene scene)
        {
            var result = new Dictionary<Transform, string>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var identity in root.GetComponentsInChildren<MapEditSceneIdentity>(true))
                    foreach (var item in identity.objects)
                        if (item.target && !string.IsNullOrEmpty(item.id)) result[item.target] = item.id;
            return result;
        }
        // Name + occurrence amongst equally named siblings. Unrelated insertions do not shift identity.
        public static string PathOf(Transform target, Transform relativeTo = null)
        {
            var segments = new List<string>();
            for (var item = target; item && item != relativeTo; item = item.parent)
            {
                var siblings = item.parent ? Enumerable.Range(0, item.parent.childCount).Select(i => item.parent.GetChild(i)) :
                    item.gameObject.scene.GetRootGameObjects().Select(g => g.transform);
                int occurrence = siblings.TakeWhile(t => t != item).Count(t => t.name == item.name);
                segments.Add(Uri.EscapeDataString(item.name) + "#" + occurrence);
            }
            segments.Reverse(); return string.Join("/", segments);
        }
        public static Transform ResolvePath(Scene scene, string path, Transform relativeTo = null)
        {
            Transform result = relativeTo;
            if (string.IsNullOrEmpty(path)) return result;
            foreach (string segment in path.Split('/'))
            {
                int split = segment.LastIndexOf('#');
                if (split < 0 || !int.TryParse(segment.Substring(split + 1), out int occurrence) || occurrence < 0) return null;
                string name = Uri.UnescapeDataString(segment.Substring(0, split));
                var children = result ? Enumerable.Range(0, result.childCount).Select(i => result.GetChild(i)) :
                    scene.GetRootGameObjects().Select(g => g.transform);
                result = children.Where(t => t.name == name).Skip(occurrence).FirstOrDefault();
                if (!result) return null;
            }
            return result;
        }
        public static MapEditState Capture(Transform target, Func<Transform, string> id, bool mesh = false)
        {
            var state = new MapEditState { name = target.name, parentId = target.parent ? id(target.parent) : "",
                active = target.gameObject.activeSelf, position = target.localPosition, rotation = target.localRotation, scale = target.localScale };
            var container = target.GetComponent<SplineContainer>();
            if (container)
            {
                state.hasSplines = true;
                state.splines = new List<MapEditSpline>();
                foreach (var spline in container.Splines)
                {
                    var data = new MapEditSpline { closed = spline.Closed };
                    for (int i = 0; i < spline.Count; i++)
                    {
                        var knot = spline[i];
                        data.knots.Add(new MapEditKnot { position = knot.Position, tangentIn = knot.TangentIn, tangentOut = knot.TangentOut,
                            rotation = knot.Rotation, mode = (int)spline.GetTangentMode(i), tension = spline.GetAutoSmoothTension(i) });
                    }
                    state.splines.Add(data);
                }
                for (int s = 0; s < container.Splines.Count; s++)
                    for (int k = 0; k < container.Splines[s].Count; k++)
                        foreach (var link in container.KnotLinkCollection.GetKnotLinks(new SplineKnotIndex(s, k)))
                            if (link.Spline > s || link.Spline == s && link.Knot > k)
                                state.links.Add(new MapEditKnotLink { aSpline = s, aKnot = k, bSpline = link.Spline, bKnot = link.Knot });
            }
            var loft = target.GetComponent<MultiSplineLoft>();
            if (loft)
            {
                state.hasLoft = true;
                state.loft = new MapEditLoft { samples = loft.SamplesAlong, across = loft.SegmentsAcross, interpolation = (int)loft.AcrossMode,
                    resolution = (int)loft.AlongResolution, normals = (int)loft.SurfaceNormalMode, segmentLength = loft.TargetSegmentLength,
                    colliderLength = loft.ColliderChunkLength, closeAcross = loft.CloseAcrossSplines, closeAlong = loft.CloseAlongClosedSplines,
                    capStart = loft.CapStart, capEnd = loft.CapEnd, doubleSided = loft.DoubleSided, flipNormals = loft.FlipNormals };
                foreach (var source in loft.Sources)
                    if (source?.container) state.loft.sources.Add(new MapEditSource { id = id(source.container.transform), spline = source.splineIndex, reverse = source.reverse });
            }
            if (mesh)
            {
                state.hasMesh = true;
                var source = target.GetComponent<MeshFilter>().sharedMesh;
                state.meshTopology = Topology(source); state.vertices = source.vertices;
            }
            return state;
        }
        public static void Apply(Transform target, MapEditState state, Func<string, Transform> resolve)
        {
            target.name = state.name;
            target.localPosition = state.position; target.localRotation = state.rotation; target.localScale = state.scale;
            target.gameObject.SetActive(state.active && !state.deleted);
            if (!string.IsNullOrEmpty(state.materialSourceId))
            {
                var source = resolve(state.materialSourceId);
                if (!source || !source.GetComponent<MeshRenderer>()) throw new InvalidDataException("Missing material source.");
                var renderer = target.GetComponent<MeshRenderer>();
                if (renderer) renderer.sharedMaterials = source.GetComponent<MeshRenderer>().sharedMaterials;
            }
            if (state.hasSplines)
            {
                var container = target.GetComponent<SplineContainer>();
                if (!container) container = target.gameObject.AddComponent<SplineContainer>();
                container.KnotLinkCollection.Clear();
                while (container.Splines.Count > state.splines.Count) container.RemoveSplineAt(container.Splines.Count - 1);
                while (container.Splines.Count < state.splines.Count) container.AddSpline(new UnityEngine.Splines.Spline());
                for (int s = 0; s < state.splines.Count; s++)
                {
                    var spline = container.Splines[s]; var data = state.splines[s];
                    spline.Clear();
                    foreach (var knot in data.knots) spline.Add(new BezierKnot(knot.position, knot.tangentIn, knot.tangentOut, knot.rotation), TangentMode.Broken);
                    spline.Closed = data.closed;
                    for (int k = 0; k < data.knots.Count; k++)
                    { spline.SetAutoSmoothTension(k, data.knots[k].tension); spline.SetTangentMode(k, (TangentMode)data.knots[k].mode); }
                }
                foreach (var link in state.links)
                    container.KnotLinkCollection.Link(new SplineKnotIndex(link.aSpline, link.aKnot), new SplineKnotIndex(link.bSpline, link.bKnot));
            }
            if (state.hasLoft)
            {
                var loft = target.GetComponent<MultiSplineLoft>();
                if (!loft) loft = target.gameObject.AddComponent<MultiSplineLoft>();
                var data = state.loft;
                loft.SamplesAlong = data.samples; loft.SegmentsAcross = data.across;
                loft.AcrossMode = (MultiSplineLoft.AcrossInterpolation)data.interpolation;
                loft.AlongResolution = (MultiSplineLoft.AlongResolutionMode)data.resolution;
                loft.SurfaceNormalMode = (MultiSplineLoft.NormalMode)data.normals;
                loft.TargetSegmentLength = data.segmentLength; loft.ColliderChunkLength = data.colliderLength;
                loft.CloseAcrossSplines = data.closeAcross; loft.CloseAlongClosedSplines = data.closeAlong;
                loft.CapStart = data.capStart; loft.CapEnd = data.capEnd; loft.DoubleSided = data.doubleSided; loft.FlipNormals = data.flipNormals;
                loft.Sources.Clear();
                foreach (var source in data.sources)
                {
                    var transform = resolve(source.id);
                    var container = transform ? transform.GetComponent<SplineContainer>() : null;
                    if (!container) throw new InvalidDataException("Missing loft source " + source.id);
                    loft.Sources.Add(new MultiSplineLoft.SplineSource { container = container, splineIndex = source.spline, reverse = source.reverse });
                }
            }
        }
        public static string Topology(Mesh mesh)
        {
            // Include connectivity, not just vertex count: a reimport with reordered vertices must conflict.
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                foreach (int index in mesh.triangles) { hash ^= (uint)index; hash *= 1099511628211UL; }
                return mesh.vertexCount + ":" + mesh.subMeshCount + ":" + hash.ToString("x16");
            }
        }
        public static bool Same(MapEditState a, MapEditState b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return SameText(a.name, b.name) && SameText(a.parentId, b.parentId) && SameText(a.materialSourceId, b.materialSourceId) &&
                a.active == b.active && a.deleted == b.deleted && a.hasSplines == b.hasSplines && a.hasLoft == b.hasLoft && a.hasMesh == b.hasMesh &&
                a.position.Equals(b.position) && a.rotation.Equals(b.rotation) && a.scale.Equals(b.scale) && SameText(a.meshTopology, b.meshTopology) &&
                EqualItems(a.vertices, b.vertices, (x, y) => x.Equals(y)) &&
                EqualItems(a.links, b.links, (x, y) => x.aSpline == y.aSpline && x.aKnot == y.aKnot && x.bSpline == y.bSpline && x.bKnot == y.bKnot) &&
                EqualItems(a.splines, b.splines, (x, y) => x.closed == y.closed && EqualItems(x.knots, y.knots, (k, l) =>
                    k.position.Equals(l.position) && k.tangentIn.Equals(l.tangentIn) && k.tangentOut.Equals(l.tangentOut) &&
                    k.rotation.Equals(l.rotation) && k.mode == l.mode && k.tension.Equals(l.tension))) && SameLoft(a.loft, b.loft);
        }
        static bool SameText(string a, string b) => (a ?? "") == (b ?? "");
        static bool EqualItems<T>(IList<T> a, IList<T> b, Func<T, T, bool> same)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!same(a[i], b[i])) return false;
            return true;
        }
        static bool SameLoft(MapEditLoft a, MapEditLoft b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.samples == b.samples && a.across == b.across && a.interpolation == b.interpolation && a.resolution == b.resolution && a.normals == b.normals &&
                a.segmentLength.Equals(b.segmentLength) && a.colliderLength.Equals(b.colliderLength) && a.closeAcross == b.closeAcross && a.closeAlong == b.closeAlong &&
                a.capStart == b.capStart && a.capEnd == b.capEnd && a.doubleSided == b.doubleSided && a.flipNormals == b.flipNormals &&
                EqualItems(a.sources, b.sources, (x, y) => SameText(x.id, y.id) && x.spline == y.spline && x.reverse == y.reverse);
        }
    }
}
