using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Editor-only, deterministic geometry sampling. No cameras, colliders or pipeline changes.
public static class VDPrecisionBaker
{
    // Reuses the production sampler without adding scene handlers or touching the source.
    public static float[] SampleIsolated(Mesh mesh, float distance, float bias, int samples, float falloff, bool twoSided, Func<float,bool> cancel = null)
    {
        if(!mesh || !mesh.isReadable || distance<=0 || !float.IsFinite(distance) || !float.IsFinite(bias))
            throw new InvalidOperationException("Use a readable mesh and a positive finite AO distance.");
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        try
        {
            var go=new GameObject("Vertex AO sample");SceneManager.MoveGameObjectToScene(go,scene);
            var filter=go.AddComponent<MeshFilter>();filter.sharedMesh=mesh;go.AddComponent<MeshRenderer>();
            var settings=new Settings {distance=distance,bias=Mathf.Clamp(bias,0,distance*.1f),samples=samples,falloff=falloff,twoSided=twoSided,sceneOccluders=false,output=Output.Red};
            using(var job=new Job(new[]{filter},settings))
            {
                bool done=false;
                while(!done){done=job.Step();if(cancel!=null&&cancel(job.completed/(float)Math.Max(1,job.total)))throw new OperationCanceledException();}
                return (float[])job.results[0].visibility.Clone();
            }
        }
        finally {UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
    }

    // Shared acceleration for editor picking. The immutable local-space tree
    // reuses the AO BVH; callers own mesh-version and transform invalidation.
    public sealed class RayIndex
    {
        private readonly Bvh tree;
        public RayIndex(Vector3[] positions, int[] indices)
        {
            if (indices.Length % 3 != 0) throw new ArgumentException("Triangle indices must be complete.");
            var triangles = new Triangle[indices.Length / 3];
            for (int i = 0; i < triangles.Length; i++)
                triangles[i] = new Triangle(positions[indices[i * 3]], positions[indices[i * 3 + 1]], positions[indices[i * 3 + 2]]);
            tree = new Bvh(triangles);
        }
        public bool Occluded(Vector3 origin, Vector3 direction, float limit)
            => limit > 0 && tree.AnyHit(origin, direction, limit);
    }

    internal enum Output { RGB, Red, Green, Blue, Alpha, RGBA }
    [Serializable] internal class Settings
    {
        public float distance = .01f, bias = .00002f, angle = 180, falloff = 1;
        public int samples = 128;
        public bool sceneOccluders, smoothNormals = true, twoSided = true;
        public int layerMask = ~0;
        public float smoothTolerance = .000001f, smoothAngle = 120;
        public float strength = 1, gamma = 1, blackPoint, whitePoint = 1;
        public Color shadow = Color.black, light = Color.white;
        public Output output = Output.RGB;
        public bool multiply;
    }

    [Serializable] internal class Result
    {
        public MeshFilter filter;
        public Mesh source;
        public Mesh geometry;
        public Color32[] original;
        public float[] visibility;
        public Color32[] colors;
    }

    internal static Mesh Source(MeshFilter filter)
    {
        var handler = filter.GetComponent<VDColorHandlerBase>();
        return handler && handler.originalMesh ? handler.originalMesh : filter.sharedMesh;
    }

    internal static MeshFilter[] Targets(GameObject target, bool children)
    {
        if (!target || EditorUtility.IsPersistent(target)) return Array.Empty<MeshFilter>();
        return (children ? target.GetComponentsInChildren<MeshFilter>() : target.GetComponents<MeshFilter>())
            .Where(f => f.sharedMesh && f.GetComponent<MeshRenderer>()).ToArray();
    }

    internal static Bounds BoundsOf(IEnumerable<MeshFilter> filters)
    {
        bool first = true;
        var bounds = new Bounds();
        foreach (var f in filters)
        {
            var b = f.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var p = f.transform.TransformPoint(b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p);
            }
        }
        return bounds;
    }

    internal static void Apply(List<Result> results, Settings settings)
    {
        foreach (var r in results)
            if (!r.filter || !r.source || !r.filter.sharedMesh || !r.geometry || Source(r.filter) != r.source
                || r.filter.sharedMesh.vertexCount != r.visibility.Length)
                throw new InvalidOperationException("The baked target changed. Bake again before applying colors.");
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Apply vertex occlusion");
        foreach (var r in results)
        {
            r.colors = Remap(r, settings);
            var handler = r.filter.GetComponent<VDColorHandler>();
            if (!handler) handler = Undo.AddComponent<VDColorHandler>(r.filter.gameObject);
            Undo.RecordObjects(new Object[] { handler, r.filter }, "Apply vertex occlusion");
            handler.originalMesh = r.source;
            if (!handler.coloredMesh || EditorUtility.IsPersistent(handler.coloredMesh))
            {
                handler.coloredMesh = Object.Instantiate(r.geometry);
                handler.coloredMesh.name = r.source.name;
                Undo.RegisterCreatedObjectUndo(handler.coloredMesh, "Create vertex color preview");
            }
            else Undo.RecordObject(handler.coloredMesh, "Apply vertex occlusion");
            handler.colors = r.colors;
            handler.tempColors = r.colors;
            handler.coloredMesh.colors32 = r.colors;
            r.filter.sharedMesh = handler.coloredMesh;
            EditorUtility.SetDirty(handler);
            EditorUtility.SetDirty(r.filter);
            PrefabUtility.RecordPrefabInstancePropertyModifications(handler);
            PrefabUtility.RecordPrefabInstancePropertyModifications(r.filter);
        }
        Undo.CollapseUndoOperations(group);
        SceneView.RepaintAll();
    }

    internal static Color32[] Remap(Result r, Settings s)
    {
        var colors = new Color32[r.visibility.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            float value = Mathf.Clamp01((r.visibility[i] - s.blackPoint) / Mathf.Max(.0001f, s.whitePoint - s.blackPoint));
            value = Mathf.Clamp01(1 - (1 - Mathf.Pow(value, 1 / Mathf.Max(.05f, s.gamma))) * s.strength);
            Color tint = Color.Lerp(s.shadow, s.light, value);
            Color old = r.original.Length == colors.Length ? (Color)r.original[i] : Color.white;
            Color result = old;
            if (s.output == Output.RGB || s.output == Output.RGBA)
            {
                result.r = tint.r * (s.multiply ? old.r : 1);
                result.g = tint.g * (s.multiply ? old.g : 1);
                result.b = tint.b * (s.multiply ? old.b : 1);
            }
            if (s.output == Output.Red) result.r = value * (s.multiply ? old.r : 1);
            if (s.output == Output.Green) result.g = value * (s.multiply ? old.g : 1);
            if (s.output == Output.Blue) result.b = value * (s.multiply ? old.b : 1);
            if (s.output == Output.Alpha || s.output == Output.RGBA) result.a = value * (s.multiply ? old.a : 1);
            colors[i] = result;
        }
        return colors;
    }

    internal sealed class Job : IDisposable
    {
        internal readonly List<Result> results = new List<Result>();
        internal int completed, total, triangleCount;
        internal string currentName;
        internal double elapsed => timer.Elapsed.TotalSeconds;
        readonly Stopwatch timer = Stopwatch.StartNew();
        readonly Settings settings;
        readonly Bvh bvh;
        readonly List<Vector3[]> positions = new List<Vector3[]>(), normals = new List<Vector3[]>();
        readonly Vector3[] directions;
        int meshIndex, vertexIndex;
        bool transferred;

        internal Job(MeshFilter[] targets, Settings sourceSettings)
        {
            if (targets.Length == 0) throw new InvalidOperationException("Select a scene object with a Mesh Filter.");
            settings = JsonUtility.FromJson<Settings>(JsonUtility.ToJson(sourceSettings));
            if (settings.distance <= 0 || settings.bias < 0 || settings.bias >= settings.distance)
                throw new InvalidOperationException("Distance must be positive and larger than the surface bias.");
            directions = new Vector3[Mathf.Clamp(settings.samples, 8, 4096)];
            float radius = Mathf.Sin(Mathf.Clamp(settings.angle, 5, 180) * Mathf.Deg2Rad * .5f);
            for (int i = 0; i < directions.Length; i++)
            {
                float r = Mathf.Sqrt((i + .5f) / directions.Length) * radius;
                float a = i * 2.39996323f;
                directions[i] = new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), Mathf.Sqrt(1 - r * r));
            }
            try
            {
                foreach (var f in targets)
                {
                    Mesh mesh = Object.Instantiate(f.sharedMesh);
                    mesh.hideFlags = HideFlags.HideAndDontSave;
                    var result = new Result { filter = f, source = Source(f), geometry = mesh,
                        original = Source(f).colors32, visibility = new float[mesh.vertexCount] };
                    results.Add(result);
                    if (mesh.normals.Length != mesh.vertexCount) mesh.RecalculateNormals();
                    Vector3[] p = mesh.vertices, n = mesh.normals;
                    Matrix4x4 normalMatrix = f.transform.localToWorldMatrix.inverse.transpose;
                    for (int i = 0; i < p.Length; i++)
                    {
                        p[i] = f.transform.TransformPoint(p[i]);
                        n[i] = normalMatrix.MultiplyVector(n[i]).normalized;
                    }
                    if (settings.smoothNormals) Smooth(p, n, settings.smoothTolerance, settings.smoothAngle);
                    positions.Add(p); normals.Add(n); total += p.Length;
                }
                var triangles = new List<Triangle>();
                IEnumerable<MeshFilter> occluders = targets;
                if (settings.sceneOccluders)
                {
                    var scenes = new HashSet<Scene>(targets.Select(t => t.gameObject.scene));
                    occluders = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)
                        .Where(f => scenes.Contains(f.gameObject.scene) && f.gameObject.activeInHierarchy
                            && f.TryGetComponent<MeshRenderer>(out var renderer) && renderer.enabled);
                }
                foreach (var f in occluders)
                {
                    if (!f.sharedMesh || (settings.layerMask & (1 << f.gameObject.layer)) == 0) continue;
                    var points = f.sharedMesh.vertices;
                    for (int i = 0; i < points.Length; i++) points[i] = f.transform.TransformPoint(points[i]);
                    for (int sub = 0; sub < f.sharedMesh.subMeshCount; sub++)
                    {
                        if (f.sharedMesh.GetTopology(sub) != MeshTopology.Triangles)
                            throw new InvalidOperationException("Triangulate the mesh before baking: " + f.name);
                        var indices = f.sharedMesh.GetTriangles(sub);
                        for (int i = 0; i < indices.Length; i += 3)
                            triangles.Add(new Triangle(points[indices[i]], points[indices[i + 1]], points[indices[i + 2]]));
                    }
                }
                triangleCount = triangles.Count;
                bvh = new Bvh(triangles.ToArray());
            }
            catch { Dispose(); throw; }
        }

        // Work in short slices so the editor remains responsive and Cancel works within a mesh.
        internal bool Step(double milliseconds = 12)
        {
            var slice = Stopwatch.StartNew();
            while (meshIndex < results.Count && slice.Elapsed.TotalMilliseconds < milliseconds)
            {
                var r = results[meshIndex];
                currentName = r.filter.name;
                if (vertexIndex >= r.visibility.Length) { meshIndex++; vertexIndex = 0; continue; }
                Vector3 n = normals[meshIndex][vertexIndex];
                if (n.sqrMagnitude < .5f) n = Vector3.up;
                Vector3 tangent = Vector3.Cross(n, Mathf.Abs(n.y) < .9f ? Vector3.up : Vector3.right).normalized;
                Vector3 bitangent = Vector3.Cross(n, tangent);
                Vector3 origin = positions[meshIndex][vertexIndex] + n * settings.bias;
                float occlusion = 0;
                foreach (var d in directions)
                {
                    float hit = bvh.Hit(origin, tangent * d.x + bitangent * d.y + n * d.z,
                        settings.distance, settings.twoSided);
                    if (hit < settings.distance)
                        occlusion += settings.falloff <= 0 ? 1 : Mathf.Pow(1 - hit / settings.distance, settings.falloff);
                }
                r.visibility[vertexIndex++] = 1 - occlusion / directions.Length;
                completed++;
            }
            if (meshIndex < results.Count) return false;
            timer.Stop();
            return true;
        }

        internal List<Result> TakeResults() { transferred = true; return results; }
        public void Dispose()
        {
            timer.Stop();
            if (!transferred) foreach (var r in results) if (r.geometry) Object.DestroyImmediate(r.geometry);
        }
    }

    internal static void Smooth(Vector3[] positions, Vector3[] normals, float tolerance, float angle)
    {
        float cell = Mathf.Max(tolerance, 1e-9f);
        var buckets = new Dictionary<Vector3Int, List<int>>();
        Func<Vector3, Vector3Int> key = p => new Vector3Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));
        // Use a local origin to retain precision far from the world origin.
        var origin = positions.Length > 0 ? positions[0] : Vector3.zero;
        for (int i = 0; i < positions.Length; i++)
        {
            var k = key(positions[i] - origin);
            if (!buckets.TryGetValue(k, out var bucket)) buckets[k] = bucket = new List<int>();
            bucket.Add(i);
        }
        var source = (Vector3[])normals.Clone();
        float cosine = Mathf.Cos(angle * Mathf.Deg2Rad);
        for (int i = 0; i < positions.Length; i++)
        {
            var k = key(positions[i] - origin);
            Vector3 sum = Vector3.zero;
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                if (buckets.TryGetValue(k + new Vector3Int(x, y, z), out var bucket))
                    foreach (int j in bucket)
                        if ((positions[i] - positions[j]).sqrMagnitude <= cell * cell && Vector3.Dot(source[i], source[j]) >= cosine)
                            sum += source[j];
            normals[i] = sum.sqrMagnitude > 1e-10f ? sum.normalized : source[i];
        }
    }

    internal struct Triangle
    {
        internal Vector3 a, e1, e2, center;
        internal Bounds bounds;
        internal Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            this.a = a; e1 = b - a; e2 = c - a; center = (a + b + c) / 3;
            bounds = new Bounds(a, Vector3.zero); bounds.Encapsulate(b); bounds.Encapsulate(c);
        }
        internal float Hit(Vector3 o, Vector3 d, float limit, bool twoSided, float fixedTolerance = float.NaN)
        {
            Vector3 p = Vector3.Cross(d, e2);
            float determinant = Vector3.Dot(e1, p);
            bool relative = float.IsNaN(fixedTolerance);
            float epsilon = relative ? 1e-7f * Mathf.Sqrt(e1.sqrMagnitude * e2.sqrMagnitude) : fixedTolerance;
            if (twoSided ? relative ? Mathf.Abs(determinant) <= epsilon : Mathf.Abs(determinant) < epsilon : determinant <= epsilon) return limit;
            float inv = 1 / determinant;
            Vector3 t = o - a;
            float u = Vector3.Dot(t, p) * inv;
            if (u < 0 || u > 1) return limit;
            Vector3 q = Vector3.Cross(t, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0 || u + v > 1) return limit;
            float distance = Vector3.Dot(e2, q) * inv;
            return distance > 0 && distance < limit ? distance : limit;
        }
    }

    internal sealed class Bvh
    {
        struct Node { internal Bounds bounds; internal int start, count, left, right; }
        readonly Triangle[] triangles;
        readonly List<Node> nodes = new List<Node>();
        internal Bvh(Triangle[] triangles) { this.triangles = triangles; if (triangles.Length > 0) Build(0, triangles.Length); }
        int Build(int start, int count)
        {
            var bounds = triangles[start].bounds;
            for (int i = start + 1; i < start + count; i++) bounds.Encapsulate(triangles[i].bounds);
            int index = nodes.Count;
            var node = new Node { bounds = bounds, start = start, count = count };
            nodes.Add(node);
            if (count > 8)
            {
                Vector3 size = bounds.size;
                int axis = size.x > size.y ? (size.x > size.z ? 0 : 2) : (size.y > size.z ? 1 : 2);
                Partition(start, start + count - 1, start + count / 2, axis);
                node.left = Build(start, count / 2); node.right = Build(start + count / 2, count - count / 2); node.count = 0;
                nodes[index] = node;
            }
            return index;
        }
        private void Partition(int left, int right, int median, int axis)
        {
            while (left < right)
            {
                float a = triangles[left].center[axis], b = triangles[(left + right) / 2].center[axis], c = triangles[right].center[axis];
                float pivot = Mathf.Max(Mathf.Min(a, b), Mathf.Min(Mathf.Max(a, b), c));
                int i = left, j = right;
                while (i <= j)
                {
                    while (triangles[i].center[axis] < pivot) i++;
                    while (triangles[j].center[axis] > pivot) j--;
                    if (i > j) break;
                    var swap = triangles[i]; triangles[i] = triangles[j]; triangles[j] = swap; i++; j--;
                }
                if (median <= j) right = j; else if (median >= i) left = i; else return;
            }
        }
        internal float Hit(Vector3 origin, Vector3 direction, float limit, bool twoSided)
        { return nodes.Count == 0 ? limit : HitNode(0, origin, direction, limit, twoSided); }
        internal bool AnyHit(Vector3 origin, Vector3 direction, float limit)
            => nodes.Count > 0 && AnyHitNode(0, origin, direction, limit);
        private bool AnyHitNode(int index, Vector3 origin, Vector3 direction, float limit)
        {
            var node = nodes[index];
            if (!Intersects(node.bounds, origin, direction, limit)) return false;
            if (node.count == 0)
                return AnyHitNode(node.left, origin, direction, limit) || AnyHitNode(node.right, origin, direction, limit);
            for (int i = node.start; i < node.start + node.count; i++)
                if (triangles[i].Hit(origin, direction, limit, true, 1e-8f) < limit) return true;
            return false;
        }
        private static bool Intersects(Bounds bounds, Vector3 origin, Vector3 direction, float limit)
        {
            float near = 0, far = limit;
            var min = bounds.min; var max = bounds.max;
            for (int a = 0; a < 3; a++)
            {
                if (Mathf.Abs(direction[a]) < 1e-12f) { if (origin[a] < min[a] || origin[a] > max[a]) return false; }
                else
                {
                    float t0 = (min[a] - origin[a]) / direction[a], t1 = (max[a] - origin[a]) / direction[a];
                    near = Mathf.Max(near, Mathf.Min(t0, t1)); far = Mathf.Min(far, Mathf.Max(t0, t1));
                    if (near > far) return false;
                }
            }
            return true;
        }
        float HitNode(int index, Vector3 origin, Vector3 direction, float limit, bool twoSided)
        {
            var node = nodes[index];
            float near = 0, far = limit;
            var min = node.bounds.min; var max = node.bounds.max;
            for (int a = 0; a < 3; a++)
            {
                if (Mathf.Abs(direction[a]) < 1e-12f) { if (origin[a] < min[a] || origin[a] > max[a]) return limit; }
                else
                {
                    float t0 = (min[a] - origin[a]) / direction[a], t1 = (max[a] - origin[a]) / direction[a];
                    near = Mathf.Max(near, Mathf.Min(t0, t1)); far = Mathf.Min(far, Mathf.Max(t0, t1));
                    if (near > far) return limit;
                }
            }
            if (node.count == 0) return HitNode(node.right, origin, direction, HitNode(node.left, origin, direction, limit, twoSided), twoSided);
            for (int i = node.start; i < node.start + node.count; i++) limit = triangles[i].Hit(origin, direction, limit, twoSided);
            return limit;
        }
    }
}
