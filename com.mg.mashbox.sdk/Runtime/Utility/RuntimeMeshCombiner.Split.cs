using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public partial class RuntimeMeshCombiner
{
    private sealed class SourceSection
    {
        public Vector3Int Cell;
        public int Submesh;
        public Bounds WorldBounds;
        public Mesh Mesh;
        public readonly Dictionary<int, int> Remap = new Dictionary<int, int>();
        public readonly List<int> Vertices = new List<int>();
        public readonly List<int> Indices = new List<int>();
    }

    private static Vector3Int SpatialCell(Vector3 point, float size) => new Vector3Int(
        Mathf.FloorToInt(point.x / size), Mathf.FloorToInt(point.y / size), Mathf.FloorToInt(point.z / size));

    private static bool FitsSpatialBounds(Bounds bounds, float limit) =>
        bounds.size.x <= limit && bounds.size.y <= limit && bounds.size.z <= limit;

    // Reindex whole triangles, preserving authored shading, UV seams and colors.
    // A triangle wider than a cell cannot be made smaller by reindexing. Retain
    // that source instead of silently rebuilding/interpolating its geometry.
    private List<SourceSection> SplitSpatialSource(Mesh source, Matrix4x4 matrix,
        float cellSize, int vertexLimit, List<Mesh> temporaryMeshes)
    {
        Vector3[] positions = source.vertices;
        var world = new Vector3[positions.Length];
        for (int i = 0; i < world.Length; i++) world[i] = matrix.MultiplyPoint3x4(positions[i]);
        var sections = new List<SourceSection>();
        var current = new Dictionary<(Vector3Int, int), SourceSection>();
        for (int submesh = 0; submesh < source.subMeshCount; submesh++)
        {
            int[] indices = source.GetTriangles(submesh);
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                Bounds triangle = new Bounds(world[a], Vector3.zero);
                triangle.Encapsulate(world[b]); triangle.Encapsulate(world[c]);
                if (!FitsSpatialBounds(triangle, cellSize)) return null;
                Vector3Int cell = SpatialCell((world[a] + world[b] + world[c]) / 3f, cellSize);
                var key = (cell, submesh);
                current.TryGetValue(key, out SourceSection section);
                Bounds joined = section != null ? section.WorldBounds : triangle;
                joined.Encapsulate(triangle);
                int additional = section == null ? 3 :
                    (section.Remap.ContainsKey(a) ? 0 : 1) +
                    (section.Remap.ContainsKey(b) ? 0 : 1) +
                    (section.Remap.ContainsKey(c) ? 0 : 1);
                if (section == null || section.Vertices.Count + additional > vertexLimit ||
                    !FitsSpatialBounds(joined, cellSize * 2f))
                {
                    section = new SourceSection { Cell = cell, Submesh = submesh, WorldBounds = triangle };
                    current[key] = section;
                    sections.Add(section);
                }
                section.WorldBounds.Encapsulate(triangle);
                for (int j = 0; j < 3; j++)
                {
                    int original = indices[i + j];
                    if (!section.Remap.TryGetValue(original, out int mapped))
                    {
                        mapped = section.Vertices.Count;
                        section.Remap.Add(original, mapped);
                        section.Vertices.Add(original);
                    }
                    section.Indices.Add(mapped);
                }
            }
        }
        Vector3[] normals = source.normals;
        Vector4[] tangents = source.tangents;
        Color[] colors = source.colors;
        var uv = new List<Vector4>[8];
        for (int channel = 0; channel < 8; channel++)
        {
            uv[channel] = new List<Vector4>();
            source.GetUVs(channel, uv[channel]);
        }
        foreach (SourceSection section in sections)
        {
            var mesh = new Mesh { name = source.name + " spatial section", indexFormat = IndexFormat.UInt16 };
            temporaryMeshes.Add(mesh);
            section.Mesh = mesh;
            mesh.vertices = CopySpatialValues(positions, section.Vertices);
            if (normals.Length == positions.Length) mesh.normals = CopySpatialValues(normals, section.Vertices);
            if (tangents.Length == positions.Length) mesh.tangents = CopySpatialValues(tangents, section.Vertices);
            if (colors.Length == positions.Length) mesh.colors = CopySpatialValues(colors, section.Vertices);
            for (int channel = 0; channel < 8; channel++)
            {
                if (uv[channel].Count != positions.Length) continue;
                var values = CopySpatialValues(uv[channel], section.Vertices);
                int dimension = source.GetVertexAttributeDimension((VertexAttribute)((int)VertexAttribute.TexCoord0 + channel));
                if (dimension == 2)
                {
                    var values2 = new Vector2[values.Length];
                    for (int i = 0; i < values.Length; i++) values2[i] = values[i];
                    mesh.SetUVs(channel, values2);
                }
                else if (dimension == 3)
                {
                    var values3 = new Vector3[values.Length];
                    for (int i = 0; i < values.Length; i++) values3[i] = values[i];
                    mesh.SetUVs(channel, values3);
                }
                else mesh.SetUVs(channel, values);
            }
            mesh.SetTriangles(section.Indices, 0, true);
            section.Remap.Clear(); section.Vertices.Clear(); section.Indices.Clear();
        }
        return sections;
    }

    private static T[] CopySpatialValues<T>(IReadOnlyList<T> values, List<int> indices)
    {
        var copy = new T[indices.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = values[indices[i]];
        return copy;
    }
}
