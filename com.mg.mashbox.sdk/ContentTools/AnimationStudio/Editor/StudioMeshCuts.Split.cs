using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        // Partition by authored faces while reusing their exact render diagonals.
        // Each side owns its boundary render vertices; UV/normal seams stay split.
        internal void ValidateSplitSource(StudioModelDocument.Node node)
        {
            if (!node.mesh || !node.mesh.isReadable || node.skinned || node.blendShape != null || node.mesh.blendShapeCount > 0)
                throw new InvalidOperationException("Split Selection requires a readable static mesh without skinning or blend shapes.");
            for (int s = 0; s < node.mesh.subMeshCount; s++)
                if (node.mesh.GetTopology(s) != MeshTopology.Triangles)
                    throw new InvalidOperationException("Split Selection requires Studio polygon surfaces with a triangle render buffer.");
            var triangles = BevelSourceTriangles(node.mesh);
            for (int f = 0; f < Faces.Count; f++)
                if (triangles[f].Count != (Faces[f].Vertices.Count - 2) * 3)
                    throw new InvalidOperationException("A face has no matching render triangulation; splitting was not applied.");
            if (triangles.Sum(t => t.Count) != node.mesh.triangles.Length)
                throw new InvalidOperationException("The face boundaries do not cover the render mesh; splitting was not applied.");
        }

        internal StudioModelDocument.Node ExtractSplitFaces(StudioModelDocument.Node node, HashSet<int> included, bool keepLooseVertices)
        {
            var source = BevelSource(node.mesh); var originalTriangles = BevelSourceTriangles(node.mesh);
            var faces = Enumerable.Range(0, Faces.Count).Where(included.Contains).ToArray();
            var usedSet = new HashSet<int>(faces.SelectMany(f => Faces[f].Vertices));
            if (keepLooseVertices)
            {
                var surface = new HashSet<int>(Faces.SelectMany(f => f.Vertices));
                for (int v = 0; v < Positions.Length; v++) if (!surface.Contains(v)) usedSet.Add(v);
            }
            var used = usedSet.OrderBy(v => v).ToArray();
            var remap = new int[Positions.Length]; for (int v = 0; v < used.Length; v++) remap[used[v]] = v;
            var triangles = Enumerable.Range(0, node.mesh.subMeshCount).Select(_ => new List<int>()).ToArray();
            foreach (int f in faces) triangles[Faces[f].Submesh].AddRange(originalTriangles[f].Select(v => remap[v]));
            var mesh = new Mesh { name = node.mesh.name, indexFormat = used.Length > 65535 || node.mesh.indexFormat == IndexFormat.UInt32 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            try
            {
                mesh.SetVertices(used.Select(v => Positions[v]).ToList());
                if (source.Normals.Length == Positions.Length) mesh.SetNormals(used.Select(v => source.Normals[v]).ToList());
                if (source.Tangents.Length == Positions.Length) mesh.SetTangents(used.Select(v => source.Tangents[v]).ToList());
                if (source.Colors.Length == Positions.Length) mesh.SetColors(used.Select(v => source.Colors[v]).ToList());
                for (int c = 0; c < 8; c++) if (source.UV[c] != null)
                {
                    var values = used.Select(v => source.UV[c][v]);
                    if (source.Dimensions[c] == 2) mesh.SetUVs(c, values.Select(v => new Vector2(v.x,v.y)).ToList());
                    else if (source.Dimensions[c] == 3) mesh.SetUVs(c, values.Select(v => new Vector3(v.x,v.y,v.z)).ToList());
                    else mesh.SetUVs(c, values.ToList());
                }
                mesh.subMeshCount = triangles.Length;
                for (int s = 0; s < triangles.Length; s++) mesh.SetTriangles(triangles[s],s,false);
                mesh.RecalculateBounds();
                var part = new StudioModelDocument.Node { name = node.name, parent = node.parent, siblingOrder = node.siblingOrder,
                    position = node.position, positionOffset = node.positionOffset, rotation = node.rotation, scale = node.scale,
                    pivotRotation = node.pivotRotation, active = node.active, sourcePath = node.sourcePath, anchorType = node.anchorType,
                    materials = node.materials?.ToArray() ?? Array.Empty<Material>(), previewTextureSlice = node.previewTextureSlice, vertexAOBaked = node.vertexAOBaked,
                    mesh = mesh, cutPolygons = faces.SelectMany(f => new[] { Faces[f].Submesh, Faces[f].Vertices.Count }.Concat(Faces[f].Vertices.Select(v => remap[v]))).ToArray(),
                    cutTriangles = mesh.triangles, normalGroups = node.normalGroups != null && node.normalGroups.Length == Positions.Length ? used.Select(v => node.normalGroups[v]).ToArray() : null };
                part.RotationChannels = node.RotationChannels; part.rotation = node.rotation;
                return part;
            }
            catch { UnityEngine.Object.DestroyImmediate(mesh); throw; }
        }
    }
}
