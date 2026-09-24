#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    // A seam is a shared piecewise-linear curve. Only knots present on both
    // sides can bend it; additional vertices must lie on the same segments.
    internal static class MGTerrainSeams
    {
        const float Epsilon = .001f;
        sealed class Buffer
        {
            internal MGTerrain tile;
            internal readonly List<Vector3> vertices = new List<Vector3>();
            internal readonly List<Vector3> normals = new List<Vector3>();
            internal readonly List<Sample> border = new List<Sample>();
            internal Matrix4x4 toWorld, toLocal;
            internal bool prepared, changed, moved;
        }
        sealed class Sample
        {
            internal Buffer buffer;
            internal int index;
            internal Vector3 world, normal;
            internal Node node;
        }
        sealed class Node
        {
            internal Vector2Int key;
            internal readonly List<Sample> samples = new List<Sample>();
            internal readonly List<Node> dependents = new List<Node>();
            internal Node first, last;
            internal float blend, height;
            internal Vector3 normal;
            internal bool active, shared;
            internal int state;
        }
        sealed class Edge
        {
            internal Buffer buffer;
            internal bool positive;
            internal readonly List<Node> points = new List<Node>();
        }
        sealed class Line
        {
            internal bool alongZ;
            internal readonly List<Edge> edges = new List<Edge>();
            internal readonly HashSet<Node> interpolated = new HashSet<Node>();
            internal int Along(Node node) => alongZ ? node.key.y : node.key.x;
        }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MGTerrain, Buffer> Buffers =
            new System.Runtime.CompilerServices.ConditionalWeakTable<MGTerrain, Buffer>();

        internal static List<MGTerrain> Neighbours(IReadOnlyList<MGTerrain> world, List<MGTerrain> touched)
        {
            var bounds = touched.Select(MGTerrainTileAuthoring.BoundsOf).ToArray();
            return world.Where(tile => tile != null && tile.MeshFilter != null && tile.MeshFilter.sharedMesh != null
                && tile.MeshFilter.sharedMesh.isReadable && bounds.Any(b => Touch(b, MGTerrainTileAuthoring.BoundsOf(tile)))).ToList();
        }
        static bool Touch(Bounds a, Bounds b) => a.min.x <= b.max.x + Epsilon && a.max.x >= b.min.x - Epsilon
            && a.min.z <= b.max.z + Epsilon && a.max.z >= b.min.z - Epsilon;

        internal static void Join(List<MGTerrain> tiles, Vector3 center, float radius, bool preparedMeshes,
            Action<MGTerrain> changed = null, HashSet<MGTerrain> touched = null, IReadOnlyList<MGTerrain> world = null)
        {
            if (tiles.Count < 2) return;
            var nodes = new Dictionary<Vector2Int, Node>();
            var lines = new Dictionary<(bool, int), Line>();
            var buffers = new List<Buffer>();
            foreach (var tile in tiles.Distinct())
            {
                MGTerrainTileAuthoring.Validate(tile);
                var mesh = tile.MeshFilter.sharedMesh;
                var buffer = Buffers.GetOrCreateValue(tile);
                buffer.tile = tile; buffer.prepared = preparedMeshes; buffer.changed = buffer.moved = false;
                buffer.border.Clear(); mesh.GetVertices(buffer.vertices); mesh.GetNormals(buffer.normals);
                buffer.toWorld = tile.MeshFilter.transform.localToWorldMatrix;
                buffer.toLocal = tile.MeshFilter.transform.worldToLocalMatrix;
                buffers.Add(buffer);
                var bounds = mesh.bounds;
                var sides = new Edge[4];
                var worldBounds = MGTerrainTileAuthoring.BoundsOf(tile);
                for (int side = 0; side < 4; side++)
                {
                    bool alongZ = side < 2;
                    float coordinate = side == 0 ? worldBounds.min.x : side == 1 ? worldBounds.max.x
                        : side == 2 ? worldBounds.min.z : worldBounds.max.z;
                    var key = (alongZ, Mathf.RoundToInt(coordinate * 1000));
                    if (!lines.TryGetValue(key, out var line)) lines.Add(key, line = new Line { alongZ = alongZ });
                    sides[side] = new Edge { buffer = buffer, positive = (side & 1) != 0 };
                    line.edges.Add(sides[side]);
                }
                void Add(int index)
                {
                    var local = buffer.vertices[index];
                    var world = buffer.toWorld.MultiplyPoint3x4(local);
                    var key = new Vector2Int(Mathf.RoundToInt(world.x * 1000), Mathf.RoundToInt(world.z * 1000));
                    int membership = 0;
                    if (Mathf.Abs(local.x - bounds.min.x) < Epsilon) membership |= 1;
                    if (Mathf.Abs(local.x - bounds.max.x) < Epsilon) membership |= 2;
                    if (Mathf.Abs(local.z - bounds.min.z) < Epsilon) membership |= 4;
                    if (Mathf.Abs(local.z - bounds.max.z) < Epsilon) membership |= 8;
                    if (membership == 0) return;
                    if (!nodes.TryGetValue(key, out var node)) nodes.Add(key, node = new Node { key = key });
                    var normal = buffer.normals.Count == buffer.vertices.Count ? buffer.normals[index] : Vector3.up;
                    var sample = new Sample { buffer = buffer, index = index, world = world, normal = normal, node = node };
                    node.samples.Add(sample); buffer.border.Add(sample);
                    for (int side = 0; side < 4; side++) if ((membership & (1 << side)) != 0) sides[side].points.Add(node);
                }
                int width = tile.SurfaceGridWidth, height = tile.SurfaceGridHeight;
                if (width >= 2 && height >= 2 && (long)width * height == mesh.vertexCount)
                {
                    for (int x = 0; x < width; x++) { Add(x); Add((height - 1) * width + x); }
                    for (int z = 1; z < height - 1; z++) { Add(z * width); Add(z * width + width - 1); }
                }
                else for (int i = 0; i < mesh.vertexCount; i++) Add(i); // Includes stitched rim vertices.
            }

            foreach (var line in lines.Values)
            {
                foreach (var edge in line.edges)
                {
                    var sorted = edge.points.Distinct().OrderBy(line.Along).ToArray();
                    edge.points.Clear(); edge.points.AddRange(sorted);
                }
                foreach (var edge in line.edges)
                    foreach (var other in line.edges)
                    {
                        if (edge.positive == other.positive || edge.buffer == other.buffer || other.points.Count < 2) continue;
                        int segment = 0;
                        foreach (var node in edge.points)
                        {
                            int along = line.Along(node);
                            while (segment + 1 < other.points.Count && line.Along(other.points[segment + 1]) < along) segment++;
                            if (segment + 1 < other.points.Count && along > line.Along(other.points[segment])
                                && along < line.Along(other.points[segment + 1])) line.interpolated.Add(node);
                        }
                    }
                var points = line.edges.SelectMany(edge => edge.points).Distinct().OrderBy(line.Along).ToArray();
                // Removing every unmatched knot also supports non-integer resolution ratios.
                int left = 0;
                while (left < points.Length - 1)
                {
                    int right = left + 1;
                    while (right < points.Length && line.interpolated.Contains(points[right])) right++;
                    if (right >= points.Length) throw new InvalidOperationException("Terrain seam has no enclosing edge anchor.");
                    for (int i = left + 1; i < right; i++)
                    {
                        var node = points[i]; node.first = points[left]; node.last = points[right];
                        node.blend = (line.Along(node) - line.Along(node.first)) / (float)(line.Along(node.last) - line.Along(node.first));
                        node.first.dependents.Add(node); node.last.dependents.Add(node);
                    }
                    left = right;
                }
            }
            var queue = new Queue<Node>();
            void Activate(Node node) { if (!node.active) { node.active = true; queue.Enqueue(node); } }
            foreach (var node in nodes.Values)
            {
                node.shared = node.samples.Any(s => s.buffer != node.samples[0].buffer);
                float dx = node.samples[0].world.x - center.x, dz = node.samples[0].world.z - center.z;
                if (dx * dx + dz * dz <= radius * radius
                    && (touched == null || node.samples.Any(sample => touched.Contains(sample.buffer.tile)))) Activate(node);
            }
            // Include complete coarse segments and all T-junction copies, even outside the dab.
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (node.first != null) { Activate(node.first); Activate(node.last); }
                foreach (var dependent in node.dependents) Activate(dependent);
            }
            if (world != null)
            {
                // A coarse segment can extend beyond the immediate brush neighbours.
                // Discover all owners of its active knots before changing any mesh.
                var additional = new List<MGTerrain>();
                foreach (var tile in world)
                {
                    if (tile == null || tiles.Contains(tile) || tile.MeshFilter == null
                        || tile.MeshFilter.sharedMesh == null || !tile.MeshFilter.sharedMesh.isReadable) continue;
                    var bounds = MGTerrainTileAuthoring.BoundsOf(tile);
                    if (nodes.Values.Any(node => node.active && OnBorder(node.samples[0].world, bounds))) additional.Add(tile);
                }
                if (additional.Count > 0)
                {
                    var expanded = new List<MGTerrain>(tiles); expanded.AddRange(additional);
                    Join(expanded, center, radius, preparedMeshes, changed, touched, world);
                    return;
                }
            }
            foreach (var node in nodes.Values) Resolve(node);
            void Prepare(Buffer buffer)
            {
                if (buffer.prepared) return;
                MGTerrainDirectSculpt.Prepare(MGTerrainTileAuthoring.Modifier(buffer.tile), false);
                buffer.prepared = true;
            }
            foreach (var node in nodes.Values)
            {
                if (!node.active || (!node.shared && node.first == null)) continue;
                foreach (var sample in node.samples)
                {
                    if (Mathf.Abs(sample.world.y - node.height) <= .000001f) continue;
                    var buffer = sample.buffer; Prepare(buffer);
                    var position = sample.world; position.y = node.height;
                    // Height-only constraints preserve the tile footprint exactly.
                    var local = buffer.vertices[sample.index]; local.y = buffer.toLocal.MultiplyPoint3x4(position).y;
                    buffer.vertices[sample.index] = local; buffer.moved = buffer.changed = true;
                }
            }
            foreach (var buffer in buffers)
            {
                if (!buffer.moved) continue;
                var mesh = buffer.tile.MeshFilter.sharedMesh;
                mesh.SetVertices(buffer.vertices); mesh.RecalculateBounds(); mesh.RecalculateNormals(); mesh.GetNormals(buffer.normals);
                // Recalculation must not erase previously welded edges outside this pass.
                foreach (var sample in buffer.border)
                    if (!sample.node.active || (!sample.node.shared && sample.node.first == null))
                        buffer.normals[sample.index] = sample.normal;
            }
            foreach (var node in nodes.Values) node.state = 0;
            foreach (var node in nodes.Values) Resolve(node);
            foreach (var node in nodes.Values)
            {
                if (!node.active || (!node.shared && node.first == null)) continue;
                foreach (var sample in node.samples)
                {
                    var buffer = sample.buffer;
                    var normal = buffer.toWorld.transpose.MultiplyVector(node.normal).normalized;
                    if (buffer.normals.Count == buffer.vertices.Count && (buffer.normals[sample.index] - normal).sqrMagnitude <= 1e-10f) continue;
                    Prepare(buffer);
                    while (buffer.normals.Count < buffer.vertices.Count) buffer.normals.Add(Vector3.up);
                    buffer.normals[sample.index] = normal; buffer.changed = true;
                }
            }
            foreach (var buffer in buffers)
            {
                if (!buffer.changed) continue;
                var mesh = buffer.tile.MeshFilter.sharedMesh;
                mesh.SetNormals(buffer.normals);
                if (mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent)) mesh.RecalculateTangents();
                mesh.UploadMeshData(false); buffer.tile.NotifySurfaceMeshChanged(); EditorUtility.SetDirty(mesh);
                changed?.Invoke(buffer.tile);
            }
            foreach (var buffer in buffers) buffer.border.Clear();
        }

        static bool OnBorder(Vector3 p, Bounds b) => p.x >= b.min.x - Epsilon && p.x <= b.max.x + Epsilon
            && p.z >= b.min.z - Epsilon && p.z <= b.max.z + Epsilon
            && (Mathf.Abs(p.x - b.min.x) < Epsilon || Mathf.Abs(p.x - b.max.x) < Epsilon
                || Mathf.Abs(p.z - b.min.z) < Epsilon || Mathf.Abs(p.z - b.max.z) < Epsilon);

        static void Resolve(Node node)
        {
            if (node.state == 2) return;
            if (node.state == 1) throw new InvalidOperationException("Overlapping terrain seam constraints. Check tile footprints.");
            node.state = 1;
            if (node.first != null)
            {
                Resolve(node.first); Resolve(node.last);
                node.height = Mathf.Lerp(node.first.height, node.last.height, node.blend);
                node.normal = Vector3.Lerp(node.first.normal, node.last.normal, node.blend).normalized;
            }
            else
            {
                node.height = 0; node.normal = Vector3.zero;
                foreach (var sample in node.samples)
                {
                    node.height += sample.buffer.toWorld.MultiplyPoint3x4(sample.buffer.vertices[sample.index]).y;
                    var normals = sample.buffer.normals;
                    node.normal += normals.Count == sample.buffer.vertices.Count
                        ? sample.buffer.toLocal.transpose.MultiplyVector(normals[sample.index]).normalized : Vector3.up;
                }
                node.height /= node.samples.Count; node.normal.Normalize();
            }
            node.state = 2;
        }
    }
}
#endif
