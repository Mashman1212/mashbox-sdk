using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Undo = MashBoxSDK.AnimationStudio.StudioUndo;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioModelDocument
    {
        internal int[] CombineInputs(IEnumerable<int> selection)
        {
            // Selected groups include their mesh descendants, once each.
            var result = new List<int>();
            var order = OrderedHierarchy(); var seen = new HashSet<int>();
            foreach (int root in selection)
            {
                var branch = WithDescendants(new[] { root });
                foreach (int i in order) if (branch.Contains(i) && nodes[i].mesh && seen.Add(i)) result.Add(i);
            }
            return result.ToArray();
        }

        internal bool CanCombineMeshes(int[] inputs, out string reason)
        {
            reason = null;
            if (inputs.Length < 2) { reason = "Select at least two mesh objects, or groups containing them."; return false; }
            foreach (int i in inputs)
            {
                var n = nodes[i];
                if (n.skinned || IsBone(i) || n.blendShape != null || n.mesh.blendShapeCount > 0)
                { reason = "Combine requires static meshes without skinning or blend shapes. Bake Current Pose first where available."; return false; }
                if (n.previewTextureSlice != nodes[inputs[0]].previewTextureSlice)
                { reason = "Meshes with different preview texture slices cannot share one object."; return false; }
                for (int s = 0; s < n.mesh.subMeshCount; s++)
                    if (n.mesh.GetTopology(s) != MeshTopology.Triangles && n.mesh.GetTopology(s) != MeshTopology.Quads)
                    { reason = "Combine requires polygon surfaces."; return false; }
            }
            return true;
        }

        internal int CombineMeshes(int[] inputs, Transform[] transforms, bool combineUVs, bool centerPivot, bool keepFirstName = false)
        {
            if (!CanCombineMeshes(inputs, out var reason)) throw new InvalidOperationException(reason);
            if (transforms.Length != nodes.Count) throw new InvalidOperationException("The hierarchy changed. Select the meshes again.");
            // Build the complete replacement before registering or changing document data.
            var position = transforms[inputs[0]].position;
            if (centerPivot)
            {
                // Equal weight per object, independent of tessellation density.
                position = Vector3.zero;
                foreach (int i in inputs) position += transforms[i].localToWorldMatrix.MultiplyPoint3x4(nodes[i].mesh.bounds.center) / inputs.Length;
            }
            var rotation = transforms[inputs[0]].rotation;
            var destination = Matrix4x4.TRS(position, rotation, Vector3.one).inverse;
            var result = new Node { name = nodes[inputs[0]].name + (keepFirstName ? "" : " Combined"), position = position, rotation = rotation,
                pivotRotation = nodes[inputs[0]].pivotRotation, previewTextureSlice = nodes[inputs[0]].previewTextureSlice,
                vertexAOBaked = inputs.All(i => nodes[i].vertexAOBaked) };
            Mesh mesh = null;
            try
            {
                mesh = BuildCombinedMesh(inputs, transforms, destination, combineUVs, result);
                result.mesh = mesh;
                var remove = new HashSet<int>(inputs);
                var ancestors = new HashSet<int>();
                foreach (int i in inputs) for (int p = nodes[i].parent; p >= 0; p = nodes[p].parent) ancestors.Add(p);
                bool changed;
                do
                {
                    changed = false;
                    foreach (int i in ancestors)
                        if (!remove.Contains(i) && !nodes[i].mesh && string.IsNullOrEmpty(nodes[i].anchorType) && !IsBone(i) &&
                            !nodes.Where((n, j) => n.parent == i && !remove.Contains(j)).Any())
                        { remove.Add(i); changed = true; }
                } while (changed);
                var repairs = new List<ParentChange>();
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (remove.Contains(i) || !remove.Contains(nodes[i].parent)) continue;
                    int parent = nodes[i].parent;
                    while (parent >= 0 && remove.Contains(parent)) parent = nodes[parent].parent;
                    if (!PlanReparent(new[] { i }, parent, transforms, out var plan, out reason)) throw new InvalidOperationException(reason);
                    repairs.AddRange(plan);
                }
                var remap = new int[nodes.Count]; int next = 0;
                for (int i = 0; i < nodes.Count; i++) remap[i] = remove.Contains(i) ? -1 : next++;
                AttachOwnedMesh(mesh);
                Undo.RegisterCompleteObjectUndo(this, "Combine meshes");
                foreach (var repair in repairs)
                {
                    var n = nodes[repair.index]; int parent = n.parent;
                    while (parent >= 0 && remove.Contains(parent)) parent = nodes[parent].parent;
                    n.parent = parent; n.position = repair.position; n.positionOffset = repair.offset;
                    n.rotation = repair.rotation; n.scale = repair.scale; n.pivotRotation = repair.pivotRotation;
                }
                var remaining = new List<Node>();
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (remove.Contains(i)) continue;
                    var n = nodes[i]; n.parent = n.parent < 0 ? -1 : remap[n.parent];
                    n.rootBone = n.rootBone < 0 ? -1 : remap[n.rootBone];
                    n.bones = n.bones.Select(b => b < 0 ? -1 : remap[b]).ToArray(); remaining.Add(n);
                }
                result.name = ObjectNames.GetUniqueName(remaining.Where(n => n.parent < 0).Select(n => n.name).ToArray(), result.name);
                mesh.name = result.name;
                remaining.Add(result); nodes = remaining; EditorUtility.SetDirty(this);
                // Source and replacement meshes remain owned for native Undo/Redo.
                return nodes.Count - 1;
            }
            catch { if (mesh) DestroyImmediate(mesh, true); throw; }
        }

        private Mesh BuildCombinedMesh(int[] inputs, Transform[] transforms, Matrix4x4 destination, bool combineUVs, Node result)
        {
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var tangents = new List<Vector4>(); var colors = new List<Color>();
            var groups = new List<int>();var geometry=new List<int>();int nextGeometryId=0; var polygons = new List<int>(); var materials = new List<Material>(); var triangles = new List<List<int>>();
            var uv = Enumerable.Range(0, 8).Select(_ => new List<Vector4>()).ToArray(); var dimensions = new int[8];
            bool hasNormals = inputs.Any(i => nodes[i].mesh.normals.Length > 0), hasTangents = inputs.Any(i => nodes[i].mesh.tangents.Length > 0), hasColors = inputs.Any(i => nodes[i].mesh.colors.Length > 0);
            int nextUV = 0, nextGroup = 0;
            foreach (int i in inputs)
            {
                var node = nodes[i]; var source = node.mesh; int offset = vertices.Count, count = source.vertexCount;
                var inputGeometry=StudioSubdivisionData.Geometry(node);var geometryMap=new Dictionary<int,int>();foreach(int id in inputGeometry){if(!geometryMap.TryGetValue(id,out int mapped)){mapped=nextGeometryId++;geometryMap.Add(id,mapped);}geometry.Add(mapped);}
                var matrix = destination * transforms[i].localToWorldMatrix;
                if (Enumerable.Range(0,4).Any(c => !FiniteHierarchyVector(matrix.GetColumn(c))) || Mathf.Abs(matrix.determinant) < 1e-12f)
                    throw new InvalidOperationException("Cannot combine a mesh with zero or invalid scale.");
                bool mirrored = matrix.determinant < 0; var normalMatrix = matrix.inverse.transpose;
                var positions = source.vertices; var ns = source.normals; var ts = source.tangents; var cs = source.colors;
                if (hasNormals && ns.Length != count)
                {
                    var normalCopy = Instantiate(source);
                    try { normalCopy.RecalculateNormals(); ns = normalCopy.normals; }
                    finally { DestroyImmediate(normalCopy); }
                }
                var localGroups = new Dictionary<int, int>();
                for (int v = 0; v < count; v++)
                {
                    vertices.Add(matrix.MultiplyPoint3x4(positions[v]));
                    var normal = v < ns.Length ? normalMatrix.MultiplyVector(ns[v]).normalized : Vector3.zero;
                    if (hasNormals) normals.Add(normal);
                    if (hasTangents)
                    {
                        Vector4 t = v < ts.Length ? ts[v] : new Vector4(1, 0, 0, 1);
                        var direction = matrix.MultiplyVector(new Vector3(t.x, t.y, t.z));
                        direction = (direction - normal * Vector3.Dot(normal, direction)).normalized;
                        tangents.Add(new Vector4(direction.x, direction.y, direction.z, t.w * (mirrored ? -1 : 1)));
                    }
                    if (hasColors) colors.Add(v < cs.Length ? cs[v] : Color.white);
                    // Keep each source's smoothing identities in its own namespace.
                    int key = node.normalGroups != null && node.normalGroups.Length == count ? node.normalGroups[v] : v;
                    if (!localGroups.TryGetValue(key, out int group)) localGroups[key] = group = nextGroup++;
                    groups.Add(group);
                }
                var targets = new int[8]; for (int c = 0; c < 8; c++) targets[c] = -1;
                for (int c = 0; c < 8; c++)
                {
                    if (!source.HasVertexAttribute(VertexAttribute.TexCoord0 + c)) continue;
                    int target = combineUVs ? c : nextUV++;
                    if (target >= 8) throw new InvalidOperationException("Separate UV channels would exceed Unity's eight slots. Enable Combine UV channels or reduce the input UV sets.");
                    targets[c] = target; dimensions[target] = Math.Max(dimensions[target], source.GetVertexAttributeDimension(VertexAttribute.TexCoord0 + c));
                }
                for (int c = 0; c < 8; c++)
                {
                    int from = Array.IndexOf(targets, c); var values = new List<Vector4>();
                    if (from >= 0) source.GetUVs(from, values);
                    for (int v = 0; v < count; v++) uv[c].Add(v < values.Count ? values[v] : Vector4.zero);
                }
                var submeshes = new int[source.subMeshCount];
                for (int s = 0; s < submeshes.Length; s++)
                {
                    Material material = node.materials != null && s < node.materials.Length ? node.materials[s] : null;
                    int slot = materials.IndexOf(material);
                    if (slot < 0) { slot = materials.Count; materials.Add(material); triangles.Add(new List<int>()); }
                    submeshes[s] = slot; var indices = source.GetIndices(s, true); bool quad = source.GetTopology(s) == MeshTopology.Quads;
                    for (int t = 0; t < indices.Length; t += quad ? 4 : 3)
                    {
                        void Triangle(int a, int b, int c) { triangles[slot].Add(offset + a); triangles[slot].Add(offset + (mirrored ? c : b)); triangles[slot].Add(offset + (mirrored ? b : c)); }
                        Triangle(indices[t], indices[t+1], indices[t+2]);
                        if (quad) Triangle(indices[t], indices[t+2], indices[t+3]);
                    }
                }
                if (node.cutPolygons != null && node.cutTriangles != null &&
                    Enumerable.Range(0,source.subMeshCount).All(s => source.GetTopology(s) == MeshTopology.Triangles) && node.cutTriangles.SequenceEqual(source.triangles))
                {
                    for (int p = 0; p < node.cutPolygons.Length;)
                    {
                        int sub = node.cutPolygons[p++], length = node.cutPolygons[p++]; polygons.Add(submeshes[sub]); polygons.Add(length);
                        var face = node.cutPolygons.Skip(p).Take(length).Select(v => offset + v).ToArray();
                        if (mirrored) Array.Reverse(face); polygons.AddRange(face); p += length;
                    }
                }
                else for (int s = 0; s < source.subMeshCount; s++)
                {
                    var indices = source.GetIndices(s, true); int length = source.GetTopology(s) == MeshTopology.Quads ? 4 : 3;
                    for (int p = 0; p < indices.Length; p += length)
                    { polygons.Add(submeshes[s]); polygons.Add(length); var face = indices.Skip(p).Take(length).Select(v => offset+v).ToArray(); if (mirrored) Array.Reverse(face); polygons.AddRange(face); }
                }
            }
            var output = new Mesh { name = result.name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            try
            {
                output.SetVertices(vertices); if (hasNormals) output.SetNormals(normals); if (hasTangents) output.SetTangents(tangents); if (hasColors) output.SetColors(colors);
                for (int c = 0; c < 8; c++)
                {
                    if (dimensions[c] == 1) StudioSubdivisionData.SetUV1(output,c,uv[c]);
                    else if (dimensions[c] == 2) output.SetUVs(c, uv[c].Select(v => new Vector2(v.x,v.y)).ToList());
                    else if (dimensions[c] == 3) output.SetUVs(c, uv[c].Select(v => new Vector3(v.x,v.y,v.z)).ToList());
                    else if (dimensions[c] > 0) output.SetUVs(c, uv[c]);
                }
                output.subMeshCount = materials.Count;
                for (int s = 0; s < triangles.Count; s++) output.SetTriangles(triangles[s], s, false);
                if (!hasNormals) output.RecalculateNormals(); output.RecalculateBounds();
                result.materials = materials.ToArray(); result.normalGroups = groups.ToArray();result.geometryVertices=geometry.ToArray(); result.cutPolygons = polygons.ToArray(); result.cutTriangles = output.triangles;
                return output;
            }
            catch { DestroyImmediate(output); throw; }
        }
    }
}
