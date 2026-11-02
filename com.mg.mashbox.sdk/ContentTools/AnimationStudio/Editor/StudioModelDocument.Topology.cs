using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioModelDocument
    {
        // Preserve native face types before producing the render buffer. Fresh
        // triangle-only imports may explicitly auto-quad suitable pairs; existing
        // workspaces and mixed native topology keep their saved face boundaries.
        internal static void CaptureMeshTopology(Node node, bool autoQuad = false)
        {
            var mesh = node.mesh;
            if (!mesh) return;
            bool hasQuads = false;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var topology = mesh.GetTopology(s);
                if (topology != MeshTopology.Triangles && topology != MeshTopology.Quads) return;
                hasQuads |= topology == MeshTopology.Quads;
            }
            var positions=autoQuad&&!hasQuads?mesh.vertices:null;
            var polygons = new List<int>();
            var render = new int[mesh.subMeshCount][];
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                bool quad = mesh.GetTopology(s) == MeshTopology.Quads;
                int size = quad ? 4 : 3;
                var indices = mesh.GetIndices(s, true);
                if (indices.Length % size != 0) throw new InvalidOperationException("The imported polygon index buffer is incomplete.");
                var pairs=autoQuad&&!hasQuads?ImportQuadPairs(indices,positions):null;
                var triangles = new List<int>(quad ? indices.Length / 4 * 6 : indices.Length);
                for (int i = 0; i < indices.Length; i += size)
                {
                    if(pairs!=null&&pairs.TryGetValue(i/3,out var corners)){if(corners.Length>0){polygons.Add(s);polygons.Add(4);polygons.AddRange(corners);}}
                    else{polygons.Add(s);polygons.Add(size);for(int j=0;j<size;j++)polygons.Add(indices[i+j]);}
                    triangles.Add(indices[i]); triangles.Add(indices[i + 1]); triangles.Add(indices[i + 2]);
                    if (quad) { triangles.Add(indices[i]); triangles.Add(indices[i + 2]); triangles.Add(indices[i + 3]); }
                }
                render[s] = triangles.ToArray();
            }
            // Only an owned Studio clone is converted; the source asset and its
            // importer settings are never modified. Vertex attributes stay intact.
            if (hasQuads)
            {
                // Imported 16-bit submeshes can use base vertices above 65535.
                // The rebuilt buffer uses absolute indices, so promote if needed.
                bool wide = false;
                foreach (var indices in render) foreach (int index in indices) wide |= index > ushort.MaxValue;
                if (wide && mesh.indexFormat != IndexFormat.UInt32) mesh.indexFormat = IndexFormat.UInt32;
                mesh.subMeshCount = render.Length;
                for (int s = 0; s < render.Length; s++) mesh.SetIndices(render[s], MeshTopology.Triangles, s, false);
            }
            node.cutPolygons = polygons.ToArray(); node.cutTriangles = mesh.triangles;
            node.geometryVertices = StudioSubdivisionData.Identity(mesh.vertexCount);
        }
    }
}
