using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace MashBoxSDK.Maps.Roads.Editor
{
    internal static class MGRoadFalloffSmoothing
    {
        sealed class EdgeComparer : IEqualityComparer<ulong>
        {
            public bool Equals(ulong a, ulong b) => a == b;
            // ulong's default XOR hash collapses regular-grid edges into a few buckets.
            public int GetHashCode(ulong value)
            {
                unchecked { value ^= value >> 33; value *= 0xff51afd7ed558ccdUL; value ^= value >> 33; value *= 0xc4ceb9fe1a85ec53UL; return (int)(value ^ (value >> 33)); }
            }
        }
        sealed class Graph
        {
            internal int topology;
            internal int[][] neighbours;
            internal bool[] boundary;
            internal float[] heights, snapshot;
        }
        static ConditionalWeakTable<Mesh, Graph> graphs = new ConditionalWeakTable<Mesh, Graph>();
        internal static int GraphBuildCount { get; private set; }
        internal static void ClearCache() => graphs = new ConditionalWeakTable<Mesh, Graph>();
        internal static float Distance(RoadTerrainSettings settings, float cellSpan)
            => Mathf.Max(Mathf.Max(0, settings.falloffDistance), settings.smoothFalloff ? Mathf.Max(0, settings.minimumFalloffCells) * cellSpan : 0);

        static Graph Build(Mesh mesh, int topology)
        {
            GraphBuildCount++;
            int count = mesh.vertexCount;
            var neighbours = new HashSet<int>[count];
            var edges = new Dictionary<ulong, int>(new EdgeComparer());
            void Edge(int a, int b)
            {
                if (a == b) return;
                (neighbours[a] ??= new HashSet<int>()).Add(b);
                (neighbours[b] ??= new HashSet<int>()).Add(a);
                ulong key = ((ulong)(uint)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
                edges.TryGetValue(key, out int uses); edges[key] = uses + 1;
            }
            var triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            { Edge(triangles[i], triangles[i + 1]); Edge(triangles[i + 1], triangles[i + 2]); Edge(triangles[i + 2], triangles[i]); }
            var graph = new Graph { topology = topology, neighbours = new int[count][], boundary = new bool[count], heights = new float[count], snapshot = new float[count] };
            for (int i = 0; i < count; i++)
            {
                if (neighbours[i] == null) { graph.neighbours[i] = Array.Empty<int>(); continue; }
                graph.neighbours[i] = new int[neighbours[i].Count]; neighbours[i].CopyTo(graph.neighbours[i]);
            }
            foreach (var edge in edges) if (edge.Value == 1) { graph.boundary[(int)(edge.Key >> 32)] = true; graph.boundary[(int)(edge.Key & uint.MaxValue)] = true; }
            return graph;
        }
        // The layer service validates the topology hash before evaluation. Height edits
        // preserve this graph; Undo or a changed topology hash invalidates it.
        internal static void Apply(Mesh mesh, Transform transform, Vector3[] before, Vector3[] vertices, MGRoadTerrainLayers.Layer layer, int topology = 0)
        {
            if (layer.smoothingPasses <= 0) return;
            if (!graphs.TryGetValue(mesh, out var graph) || graph.topology != topology || graph.heights.Length != vertices.Length)
            {
                graphs.Remove(mesh); graph = Build(mesh, topology); graphs.Add(mesh, graph);
            }
            var heights = graph.heights; var snapshot = graph.snapshot;
            var toWorld = transform.localToWorldMatrix; var toLocal = transform.worldToLocalMatrix;
            for (int i = 0; i < vertices.Length; i++) heights[i] = toWorld.MultiplyPoint3x4(vertices[i]).y;
            for (int pass = 0; pass < layer.smoothingPasses; pass++)
            {
                Array.Copy(heights, snapshot, heights.Length);
                foreach (var sample in layer.samples)
                {
                    int i = sample.index;
                    if (sample.smoothing <= 0 || graph.boundary[i]) continue;
                    var neighbours = graph.neighbours[i];
                    float sum = snapshot[i];
                    for (int n = 0; n < neighbours.Length; n++) sum += snapshot[neighbours[n]];
                    float height = Mathf.Lerp(snapshot[i], sum / (neighbours.Length + 1), sample.smoothing);
                    if (layer.heightMode != RoadHeightMode.RaiseAndLower)
                    {
                        float baseHeight = toWorld.MultiplyPoint3x4(before[i]).y;
                        height = layer.heightMode == RoadHeightMode.RaiseOnly ? Mathf.Max(baseHeight, height) : Mathf.Min(baseHeight, height);
                    }
                    heights[i] = height;
                }
            }
            foreach (var sample in layer.samples)
            {
                int i = sample.index;
                if (sample.smoothing <= 0 || graph.boundary[i]) continue;
                var p = toWorld.MultiplyPoint3x4(vertices[i]); p.y = heights[i]; vertices[i] = toLocal.MultiplyPoint3x4(p);
            }
        }
    }
}
