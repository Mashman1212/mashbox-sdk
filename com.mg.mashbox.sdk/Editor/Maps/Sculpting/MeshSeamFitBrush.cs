using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.Sculpting;
using MashBoxSDK.Maps.Spline;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    // One cache per drag. Read render meshes directly: loft colliders may use
    // different tessellation, and a target does not need a collider to be fitted.
    [InitializeOnLoad]
    internal sealed class MeshSeamFitBrush
    {
        static readonly HashSet<MeshSculptModifier> pendingUndoModifiers = new HashSet<MeshSculptModifier>();
        static MeshSeamFitBrush()
        {
            MeshSculptModifier.RestoredByUndo += OnModifierRestored;
            Undo.undoRedoPerformed += ReplayUndo;
        }

        // Kept as an explicit initialization hook for stroke recording.
        internal static void TrackUndo(MeshSculptModifier modifier) { }
        internal static bool HandlesUndo(MeshSculptModifier modifier) => modifier != null;
        internal static event Action<MeshSculptModifier> UndoMeshRebuilt;

        static void OnModifierRestored(MeshSculptModifier modifier)
        {
            if (modifier != null) pendingUndoModifiers.Add(modifier);
        }

        static void ReplayUndo()
        {
            if (pendingUndoModifiers.Count == 0) return;
            var restored = new List<MeshSculptModifier>(pendingUndoModifiers);
            pendingUndoModifiers.Clear();
            foreach (var modifier in restored)
                if (modifier != null && modifier.gameObject.scene.IsValid() && modifier.gameObject.scene.isLoaded)
                {
                    modifier.Rebuild();
                    UndoMeshRebuilt?.Invoke(modifier);
                }
            SceneView.RepaintAll();
        }

        readonly List<MeshFilter> targets = new List<MeshFilter>();
        readonly Dictionary<MeshFilter, Surface> surfaces = new Dictionary<MeshFilter, Surface>();
        readonly bool verticesOnly;

        internal MeshSeamFitBrush(IEnumerable<MeshFilter> candidates, MeshFilter terrain, bool verticesOnly, bool allowTerrainTargets = false)
        {
            this.verticesOnly = verticesOnly;
            var seen = new HashSet<MeshFilter>();
            var sourceLoft = terrain.GetComponentInParent<MultiSplineLoft>();
            foreach (MeshFilter candidate in candidates)
            {
                if (candidate == null || candidate == terrain || !seen.Add(candidate)
                    || candidate.gameObject.scene != terrain.gameObject.scene
                    || !candidate.gameObject.activeInHierarchy
                    || (!allowTerrainTargets && candidate.GetComponentInParent<MGTerrain>() != null)
                    || (sourceLoft != null && candidate.GetComponentInParent<MultiSplineLoft>() == sourceLoft)
                    || candidate.sharedMesh == null || !candidate.sharedMesh.isReadable
                    || (candidate.gameObject.hideFlags & HideFlags.DontSave) != 0) continue;
                var renderer = candidate.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled) continue;
                targets.Add(candidate);
            }
        }

        internal MeshSculptModifier.SeamVertex[] Sample(MeshFilter terrain, Vector3 center,
            float radius, float threshold, float strength, float falloff, float normalBlend, bool heightOnly, float surfaceOffset = 0f, bool aboveOnly = false, float minimumRise = 0.02f, Mesh sourceMesh = null)
        {
            var samples = new List<MeshSculptModifier.SeamVertex>();
            if (radius <= 0f || threshold <= 0f) return samples.ToArray();
            var nearby = new List<Surface>();
            float reachSquared = (radius + threshold) * (radius + threshold);
            foreach (MeshFilter target in targets)
            {
                if (target == null || target.sharedMesh == null) continue;
                MeshRenderer renderer = target.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled || !target.gameObject.activeInHierarchy
                    || renderer.bounds.SqrDistance(center) > reachSquared) continue;
                if (!surfaces.TryGetValue(target, out Surface surface))
                {
                    surface = new Surface(target, verticesOnly);
                    surfaces.Add(target, surface);
                }
                nearby.Add(surface);
            }
            if (nearby.Count == 0) return samples.ToArray();

            Transform transform = terrain.transform;
            Vector3[] vertices = (sourceMesh != null ? sourceMesh : terrain.sharedMesh).vertices;
            float thresholdSquared = threshold * threshold;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = transform.TransformPoint(vertices[i]);
                float distance = Vector3.Distance(world, center);
                if (distance >= radius) continue;
                float weight = Mathf.Clamp01(Mathf.Abs(strength) * Mathf.Pow(1f - distance / radius, falloff));
                if (weight <= 0f) continue;
                float best = thresholdSquared;
                Vector3 point = default, normal = default;
                bool found = false;
                // Clear the offset left beneath a previously fitted shoulder
                // vertex, otherwise that same vertex wins every subsequent dab.
                float rise = aboveOnly ? Mathf.Max(minimumRise, Mathf.Abs(surfaceOffset) + 0.0001f) : 0f;
                foreach (Surface surface in nearby)
                    found |= surface.Nearest(world, ref best, ref point, ref normal, aboveOnly, rise);
                // At the final reachable step, finish fitting its remaining gap
                // instead of stopping minimumRise short of the shoulder top.
                if (!found && aboveOnly)
                    foreach (Surface surface in nearby)
                        found |= surface.Nearest(world, ref best, ref point, ref normal, true, 0.00001f);
                if (!found) continue;
                Vector3 desired = transform.InverseTransformPoint(point + normal * surfaceOffset);
                if (heightOnly) { desired.x = vertices[i].x; desired.z = vertices[i].z; }
                Vector3 delta = (desired - vertices[i]) * weight;
                // A negative surface offset or a tilted height-only axis must
                // not turn an upward vertex fit into a downward movement.
                if (aboveOnly && transform.TransformVector(delta).y < 0f) delta = Vector3.zero;
                float normalWeight = weight * Mathf.Clamp01(normalBlend);
                if (delta.sqrMagnitude < 1e-14f && normalWeight <= 0f) continue;
                samples.Add(new MeshSculptModifier.SeamVertex
                {
                    index = i, delta = delta,
                    normal = transform.localToWorldMatrix.transpose.MultiplyVector(normal).normalized,
                    normalWeight = normalWeight
                });
            }
            return samples.ToArray();
        }

        internal static MeshSculptModifier.SeamVertex[] SampleLower(MeshFilter terrain,
            Vector3 center, float radius, float strength, float falloff, float depth, Mesh sourceMesh = null)
        {
            var samples = new List<MeshSculptModifier.SeamVertex>();
            if (radius <= 0f || depth <= 0f) return samples.ToArray();
            Transform transform = terrain.transform;
            Vector3[] vertices = (sourceMesh != null ? sourceMesh : terrain.sharedMesh).vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                float distance = Vector3.Distance(transform.TransformPoint(vertices[i]), center);
                if (distance >= radius) continue;
                float weight = Mathf.Clamp01(Mathf.Abs(strength) * Mathf.Pow(1f - distance / radius, falloff));
                if (weight <= 0f) continue;
                samples.Add(new MeshSculptModifier.SeamVertex
                {
                    index = i,
                    delta = transform.InverseTransformVector(Vector3.down * (depth * weight))
                });
            }
            return samples.ToArray();
        }

        sealed class Surface
        {
            struct Primitive
            {
                internal int a, b, c;
                internal Bounds bounds;
            }
            sealed class Node
            {
                internal Bounds bounds;
                internal int start, count;
                internal Node left, right;
            }
            readonly Vector3[] vertices, normals;
            readonly Primitive[] primitives;
            readonly Node root;
            readonly bool verticesOnly;

            internal Surface(MeshFilter filter, bool verticesOnly)
            {
                this.verticesOnly = verticesOnly;
                Mesh mesh = filter.sharedMesh;
                vertices = mesh.vertices;
                normals = mesh.normals;
                Matrix4x4 matrix = filter.transform.localToWorldMatrix;
                Matrix4x4 normalMatrix = filter.transform.worldToLocalMatrix.transpose;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                bool hasNormals = normals.Length == vertices.Length;
                if (!hasNormals) normals = new Vector3[vertices.Length];
                else for (int i = 0; i < normals.Length; i++) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;

                var list = new List<Primitive>();
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    int[] indices = mesh.GetTriangles(sub);
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                    {
                        int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                        Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                        if (face.sqrMagnitude < 1e-16f) continue;
                        if (!hasNormals) { normals[a] += face; normals[b] += face; normals[c] += face; }
                        if (verticesOnly) continue;
                        var bounds = new Bounds(vertices[a], Vector3.zero);
                        bounds.Encapsulate(vertices[b]); bounds.Encapsulate(vertices[c]);
                        list.Add(new Primitive { a = a, b = b, c = c, bounds = bounds });
                    }
                }
                if (!hasNormals) for (int i = 0; i < normals.Length; i++) normals[i].Normalize();
                if (verticesOnly)
                    for (int i = 0; i < vertices.Length; i++)
                        list.Add(new Primitive { a = i, b = i, c = i, bounds = new Bounds(vertices[i], Vector3.zero) });
                primitives = list.ToArray();
                if (primitives.Length > 0) root = Build(0, primitives.Length);
            }

            Node Build(int start, int count)
            {
                var node = new Node { start = start, count = count, bounds = primitives[start].bounds };
                for (int i = start + 1; i < start + count; i++) node.bounds.Encapsulate(primitives[i].bounds);
                if (count <= 12) return node;
                Vector3 size = node.bounds.size;
                int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                Array.Sort(primitives, start, count, Comparer<Primitive>.Create(
                    (a, b) => a.bounds.center[axis].CompareTo(b.bounds.center[axis])));
                int half = count / 2;
                node.left = Build(start, half);
                node.right = Build(start + half, count - half);
                return node;
            }

            internal bool Nearest(Vector3 position, ref float best, ref Vector3 point, ref Vector3 normal, bool aboveOnly, float minimumRise)
                => Search(root, position, ref best, ref point, ref normal, aboveOnly, minimumRise);

            bool Search(Node node, Vector3 position, ref float best, ref Vector3 point, ref Vector3 normal, bool aboveOnly, float minimumRise)
            {
                if (node == null || node.bounds.SqrDistance(position) > best) return false;
                if (aboveOnly && node.bounds.max.y <= position.y + minimumRise) return false;
                bool found = false;
                if (node.left != null)
                {
                    Node first = node.left, second = node.right;
                    if (first.bounds.SqrDistance(position) > second.bounds.SqrDistance(position))
                    { first = node.right; second = node.left; }
                    found |= Search(first, position, ref best, ref point, ref normal, aboveOnly, minimumRise);
                    found |= Search(second, position, ref best, ref point, ref normal, aboveOnly, minimumRise);
                    return found;
                }
                for (int i = node.start; i < node.start + node.count; i++)
                {
                    Primitive tri = primitives[i];
                    if (tri.bounds.SqrDistance(position) > best) continue;
                    Vector3 bary = verticesOnly ? new Vector3(1f, 0f, 0f)
                        : ClosestBarycentric(position, vertices[tri.a], vertices[tri.b], vertices[tri.c]);
                    Vector3 candidate = vertices[tri.a] * bary.x + vertices[tri.b] * bary.y + vertices[tri.c] * bary.z;
                    if (aboveOnly && candidate.y <= position.y + minimumRise) continue;
                    float distance = (candidate - position).sqrMagnitude;
                    if (distance > best) continue;
                    best = distance; point = candidate;
                    normal = (normals[tri.a] * bary.x + normals[tri.b] * bary.y + normals[tri.c] * bary.z).normalized;
                    if (normal.sqrMagnitude < 0.000001f)
                        normal = Vector3.Cross(vertices[tri.b] - vertices[tri.a], vertices[tri.c] - vertices[tri.a]).normalized;
                    found = true;
                }
                return found;
            }
        }

        internal static Vector3 ClosestBarycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return new Vector3(1f, 0f, 0f);
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return new Vector3(0f, 1f, 0f);
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            { float v = d1 / (d1 - d3); return new Vector3(1f - v, v, 0f); }
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return new Vector3(0f, 0f, 1f);
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            { float w = d2 / (d2 - d6); return new Vector3(1f - w, 0f, w); }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            { float w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); return new Vector3(0f, 1f - w, w); }
            float denominator = 1f / (va + vb + vc);
            float y = vb * denominator, z = vc * denominator;
            return new Vector3(1f - y - z, y, z);
        }
    }
}
