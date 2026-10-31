using System;
using System.Collections.Generic;
using UnityEngine;
namespace MG.GodSurface
{
    public sealed class MGSurfaceGeometry : ScriptableObject
    {
        public const int CornersPerChunk = 768; // 256 triangles; every geometry piece uses this one carrier.
        [Serializable] public sealed class Part { public int[] chunks; }
        [Serializable] public sealed class Shape { public Mesh source; public Part[] parts; }
        public Mesh carrier;
        public Texture2D vertices, indices, chunks;
        public Shape[] shapes = Array.Empty<Shape>();
        public int vertexCount, indexCount, chunkCount;
        public bool IsValid => carrier && vertices && indices && chunks;
        public long TexturePayloadBytes => (vertices ? (long)vertices.width * vertices.height * 16 : 0)
            + (indices ? (long)indices.width * indices.height * 4 : 0)
            + (chunks ? (long)chunks.width * chunks.height * 16 : 0);
        Dictionary<Mesh, Shape> lookup;
        void OnEnable() => lookup = null;
        public bool TryGet(Mesh mesh, out Shape shape)
        {
            if (lookup == null) { lookup = new Dictionary<Mesh, Shape>(); foreach (var s in shapes) if (s.source) lookup[s.source] = s; }
            return lookup.TryGetValue(mesh, out shape);
        }
        public void Bind(Material material)
        {
            material.SetTexture("_MGGeometryVertices", vertices);
            material.SetTexture("_MGGeometryIndices", indices);
            material.SetTexture("_MGGeometryChunks", chunks);
        }
    }
}
