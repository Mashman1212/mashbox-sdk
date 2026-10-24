using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        internal int[] DeleteComponents(StudioModelDocument.Node node, HashSet<int> faces,
            HashSet<int> vertices, IEnumerable<(int a, int b)> edges, bool weldEdges, Action beforeWrite)
        {
            var mesh = node.mesh;
            if (!mesh.isReadable) throw new InvalidOperationException("Component deletion requires a readable Studio editable mesh.");
            if (node.skinned || mesh.blendShapeCount > 0)
                throw new InvalidOperationException("Component deletion is unavailable on skinned meshes or meshes with blend shapes.");
            (int, int) EdgeKey(int a, int b) => weldEdges ? Key(a, b) : (Math.Min(a, b), Math.Max(a, b));
            var edgeKeys = new HashSet<(int, int)>(edges.Select(e => EdgeKey(e.a, e.b)));
            var kept = new List<int>();
            var retainedPolygons = new Dictionary<int, List<int>>();
            for (int f = 0; f < Faces.Count; f++)
            {
                var polygon = Faces[f].Vertices;
                bool remove = faces.Contains(f);
                for (int e = 0; !remove && e < polygon.Count; e++)
                    remove = edgeKeys.Contains(EdgeKey(polygon[e], polygon[(e + 1) % polygon.Count]));
                if (remove) continue;
                // Vertex deletion clips the selected corners from each polygon boundary.
                // A quad with one removed corner becomes the remaining triangle.
                var remaining = polygon.Where(v => !vertices.Contains(v)).ToList();
                if (remaining.Count < 3) continue;
                kept.Add(f); retainedPolygons.Add(f, remaining);
            }
            bool removedVertex = vertices.Any(v => v >= 0 && v < Positions.Length);
            if (kept.Count == Faces.Count && !removedVertex) return null;
            var used = kept.SelectMany(f => retainedPolygons[f]).Distinct().OrderBy(v => v).ToArray();
            var remap = new int[Positions.Length];
            for (int i = 0; i < used.Length; i++) remap[used[i]] = i;
            var originalTriangles = BevelSourceTriangles(mesh);
            var triangles = Enumerable.Range(0, mesh.subMeshCount).Select(_ => new List<int>()).ToArray();
            List<Vector3> triangulationPositions = null;
            foreach (int f in kept)
            {
                var remaining = retainedPolygons[f];
                var faceTriangles = originalTriangles[f];
                if (remaining.Count != Faces[f].Vertices.Count)
                {
                    if (triangulationPositions == null) triangulationPositions = Positions.ToList();
                    faceTriangles = new List<int>();
                    Triangulate(remaining, triangulationPositions, faceTriangles);
                }
                else if (faceTriangles.Count != (remaining.Count - 2) * 3)
                    throw new InvalidOperationException("A remaining face has no matching render triangulation; deletion was not applied.");
                triangles[Faces[f].Submesh].AddRange(faceTriangles.Select(v => remap[v]));
            }
            var source = BevelSource(mesh);
            var polygons = kept.SelectMany(f => new[] { Faces[f].Submesh, retainedPolygons[f].Count }
                .Concat(retainedPolygons[f].Select(v => remap[v]))).ToArray();
            beforeWrite?.Invoke();
            mesh.Clear(); mesh.indexFormat = used.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(used.Select(v => Positions[v]).ToList());
            if (source.Normals.Length == Positions.Length) mesh.SetNormals(used.Select(v => source.Normals[v]).ToList());
            if (source.Tangents.Length == Positions.Length) mesh.SetTangents(used.Select(v => source.Tangents[v]).ToList());
            if (source.Colors.Length == Positions.Length) mesh.SetColors(used.Select(v => source.Colors[v]).ToList());
            for (int c = 0; c < 8; c++) if (source.UV[c] != null)
            {
                var values = used.Select(v => source.UV[c][v]);
                if (source.Dimensions[c] == 2) mesh.SetUVs(c, values.Select(v => new Vector2(v.x, v.y)).ToList());
                else if (source.Dimensions[c] == 3) mesh.SetUVs(c, values.Select(v => new Vector3(v.x, v.y, v.z)).ToList());
                else mesh.SetUVs(c, values.ToList());
            }
            mesh.subMeshCount = triangles.Length;
            for (int s = 0; s < triangles.Length; s++) mesh.SetTriangles(triangles[s], s);
            mesh.RecalculateBounds(); mesh.UploadMeshData(false);
            node.cutPolygons = polygons; node.cutTriangles = mesh.triangles;
            return used;
        }
    }
}
