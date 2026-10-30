using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        internal sealed class MergeResult { internal int Removed; internal int[] Selection; }
        internal MergeResult MergeVertices(StudioModelDocument.Node node, IEnumerable<int> selection, bool mergeAll,
            float distance, Matrix4x4 world, Action beforeWrite = null, int target = -1)
        {
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0)
                throw new InvalidOperationException("Merge distance must be finite and nonnegative.");
            var mesh = node.mesh;
            if (node.skinned || mesh.blendShapeCount != 0 || mesh.bindposes.Length != 0)
                throw new InvalidOperationException("Bake skinning and blend shapes before merging vertices.");
            var selected = new Dictionary<int, int>();var selectedPoints=new List<Vector3>();
            foreach (int i in selection)
            {
                if (i < 0 || i >= Positions.Length) throw new InvalidOperationException("The vertex selection changed; select the vertices again.");
                if (!selected.ContainsKey(welded[i])) {selected.Add(welded[i], selected.Count);selectedPoints.Add(Positions[i]);}
            }
            if (selected.Count < 2) return null;
            var points = selectedPoints.ToArray();
            if (target >= 0 && (!mergeAll || target >= Positions.Length || !selected.ContainsKey(welded[target])))
                throw new InvalidOperationException("The weld destination must belong to the selected vertices.");
            var parents = Enumerable.Range(0, points.Length).ToArray();
            int Root(int i) { while (parents[i] != i) { parents[i] = parents[parents[i]]; i = parents[i]; } return i; }
            void Join(int a, int b) { a = Root(a); b = Root(b); if (a != b) parents[Math.Max(a, b)] = Math.Min(a, b); }
            if (mergeAll) for (int i = 1; i < points.Length; i++) Join(0, i);
            else if (distance > 0)
            {
                // Spatial bins keep sparse, large selections out of an all-pairs scan.
                var bins = new Dictionary<(long x, long y, long z), List<int>>();
                var wp = points.Select(world.MultiplyPoint3x4).ToArray();
                double squared = (double)distance * distance;
                long Cell(float value)
                {
                    double cell = Math.Floor((double)value / distance);
                    if (double.IsNaN(cell) || cell <= long.MinValue + 1d || cell >= long.MaxValue - 1d)
                        throw new InvalidOperationException("Merge distance is too small for this mesh's world coordinates.");
                    return (long)cell;
                }
                for (int i = 0; i < wp.Length; i++)
                {
                    var p = wp[i]; var key = (x: Cell(p.x), y: Cell(p.y), z: Cell(p.z));
                    for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                    {
                        if (!bins.TryGetValue((key.x+x, key.y+y, key.z+z), out var candidates)) continue;
                        foreach (int j in candidates)
                        {
                            double dx = (double)p.x-wp[j].x, dy = (double)p.y-wp[j].y, dz = (double)p.z-wp[j].z;
                            if (dx*dx+dy*dy+dz*dz <= squared) Join(i, j);
                        }
                    }
                    if (!bins.TryGetValue(key, out var bucket)) bins[key] = bucket = new List<int>();
                    bucket.Add(i);
                }
            }
            if(!mergeAll && distance==0){var coincident=new Dictionary<Vector3,int>();for(int i=0;i<points.Length;i++){if(coincident.TryGetValue(points[i],out int other))Join(i,other);else coincident.Add(points[i],i);}}
            var centers = new Vector3[points.Length]; var counts = new int[points.Length];
            var sumX = new double[points.Length]; var sumY = new double[points.Length]; var sumZ = new double[points.Length];
            for (int i = 0; i < points.Length; i++) { int root = Root(i); sumX[root] += points[i].x; sumY[root] += points[i].y; sumZ[root] += points[i].z; counts[root]++; }
            int removed = points.Length - counts.Count(c => c > 0);
            if (removed == 0) return null;
            for (int i = 0; i < centers.Length; i++) if (counts[i] > 0) centers[i] = new Vector3((float)(sumX[i] / counts[i]), (float)(sumY[i] / counts[i]), (float)(sumZ[i] / counts[i]));
            if (target >= 0) centers[Root(selected[welded[target]])] = Positions[target];
            var positions = (Vector3[])Positions.Clone(); var geometric = (int[])welded.Clone();
            var cluster = Enumerable.Repeat(-1, Positions.Length).ToArray();
            for (int i = 0; i < positions.Length; i++)
                if (selected.TryGetValue(welded[i], out int point))
                {
                    int root = Root(point);
                    if (counts[root] > 1) { positions[i] = centers[root]; geometric[i] = Positions.Length + root; cluster[i] = root; }
                }
            var normals = mesh.normals; var tangents = mesh.tangents; var colors = mesh.colors;
            var uvs = new List<Vector4>[8]; var dimensions = new int[8];
            for (int channel = 0; channel < 8; channel++)
            {
                uvs[channel] = new List<Vector4>(); mesh.GetUVs(channel, uvs[channel]);
                if (uvs[channel].Count > 0) dimensions[channel] = mesh.GetVertexAttributeDimension((VertexAttribute)((int)VertexAttribute.TexCoord0+channel));
            }
            // Collapse compatible render vertices, retaining separate corners where
            // UVs, colors or hard normals differ. They still share geometric topology.
            int Hash(int i)
            {
                unchecked
                {
                    int h = 17;
                    if (normals.Length > 0) h = h*31+normals[i].GetHashCode();
                    if (tangents.Length > 0) h = h*31+tangents[i].GetHashCode();
                    if (colors.Length > 0) h = h*31+colors[i].GetHashCode();
                    for (int c = 0; c < 8; c++) if (uvs[c].Count > 0) h = h*31+uvs[c][i].GetHashCode();
                    return h;
                }
            }
            bool Same(int a, int b)
            {
                if (normals.Length > 0 && !normals[a].Equals(normals[b])) return false;
                if (tangents.Length > 0 && !tangents[a].Equals(tangents[b])) return false;
                if (colors.Length > 0 && !colors[a].Equals(colors[b])) return false;
                for (int c = 0; c < 8; c++) if (uvs[c].Count > 0 && !uvs[c][a].Equals(uvs[c][b])) return false;
                return true;
            }
            var render = Enumerable.Range(0, Positions.Length).ToArray();
            var variants = new Dictionary<(int cluster, int hash), List<int>>();
            for (int i = 0; i < render.Length; i++)
            {
                if (cluster[i] < 0) continue;
                var key = (cluster[i], Hash(i));
                if (!variants.TryGetValue(key, out var candidates)) variants[key] = candidates = new List<int>();
                int match = candidates.FindIndex(v => Same(i, v));
                if (match >= 0) render[i] = candidates[match]; else candidates.Add(i);
            }
            var faces = new List<Face>();
            foreach (var face in Faces)
            {
                var polygon = new List<int>();
                foreach (int i in face.Vertices)
                    if (polygon.Count == 0 || geometric[polygon[polygon.Count-1]] != geometric[i]) polygon.Add(render[i]);
                if (polygon.Count > 1 && geometric[polygon[0]] == geometric[polygon[polygon.Count-1]]) polygon.RemoveAt(polygon.Count-1);
                if (polygon.Count < 3 || polygon.Select(v => geometric[v]).Distinct().Count() != polygon.Count) continue;
                Vector3 area = Vector3.zero;
                for (int i = 0; i < polygon.Count; i++) area += Vector3.Cross(positions[polygon[i]], positions[polygon[(i+1)%polygon.Count]]);
                if (area.sqrMagnitude < 1e-20f) continue;
                faces.Add(new Face { Submesh = face.Submesh, Vertices = polygon });
            }
            var retained = new HashSet<int>(faces.SelectMany(f => f.Vertices));
            // A fully collapsed mesh still leaves its welded point. Preserve
            // unrelated loose vertices without inventing render triangles.
            var originalFaceVertices = new HashSet<int>(Faces.SelectMany(f => f.Vertices));
            for (int i = 0; i < positions.Length; i++) if (!originalFaceVertices.Contains(i) && cluster[i] < 0) retained.Add(i);
            var retainedClusters = new HashSet<int>(retained.Where(i => cluster[i] >= 0).Select(i => cluster[i]));
            for (int i = 0; i < positions.Length; i++)
                if (cluster[i] >= 0 && retainedClusters.Add(cluster[i]))
                    retained.Add(target >= 0 && cluster[target] == cluster[i] ? render[target] : render[i]);
            var used = retained.OrderBy(i => i).ToArray();
            var remap = new int[Positions.Length]; for (int i = 0; i < used.Length; i++) remap[used[i]] = i;
            var compact = used.Select(i => positions[i]).ToList();
            foreach (var face in faces) face.Vertices = face.Vertices.Select(i => remap[i]).ToList();
            var triangles = Enumerable.Range(0, mesh.subMeshCount).Select(_ => new List<int>()).ToArray();
            foreach (var face in faces) Triangulate(face.Vertices, compact, triangles[face.Submesh]);
            beforeWrite?.Invoke();
            mesh.Clear(); mesh.indexFormat = used.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16; mesh.SetVertices(compact);
            if (colors.Length > 0) mesh.SetColors(used.Select(i => colors[i]).ToList());
            for (int c = 0; c < 8; c++) if (uvs[c].Count > 0)
            {
                if (dimensions[c] == 1) StudioSubdivisionData.SetUV1(mesh,c,used.Select(i=>uvs[c][i]));
                else if (dimensions[c] == 2) mesh.SetUVs(c, used.Select(i => new Vector2(uvs[c][i].x, uvs[c][i].y)).ToList());
                else if (dimensions[c] == 3) mesh.SetUVs(c, used.Select(i => new Vector3(uvs[c][i].x, uvs[c][i].y, uvs[c][i].z)).ToList());
                else mesh.SetUVs(c, used.Select(i => uvs[c][i]).ToList());
            }
            mesh.subMeshCount = triangles.Length;
            for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateNormals(); if (uvs[0].Count > 0) mesh.RecalculateTangents(); mesh.RecalculateBounds(); mesh.UploadMeshData(false);
            node.cutPolygons = faces.SelectMany(f => new[] { f.Submesh, f.Vertices.Count }.Concat(f.Vertices)).ToArray(); node.cutTriangles = mesh.triangles; node.normalGroups = null;node.geometryVertices=GeometricIds(used.Select(i=>geometric[i]));
            return new MergeResult { Removed = removed, Selection = used.Select((v, i) => (v, i)).Where(p => selected.ContainsKey(welded[p.v])).Select(p => p.i).ToArray() };
        }
    }
}
