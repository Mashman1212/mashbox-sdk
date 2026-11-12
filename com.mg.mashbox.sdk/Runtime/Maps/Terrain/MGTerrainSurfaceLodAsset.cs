using System;
using UnityEngine;

namespace MashBoxSDK.Maps.TerrainSystem
{
    // Persistent render-only HLOD. Never owns/replaces the editable or collision mesh.
    public sealed class MGTerrainSurfaceLodAsset : ScriptableObject
    {
        [Serializable]
        public sealed class Level
        {
            public Mesh Mesh;
            public float Error;
            public int TriangleCount;
        }
        public Mesh Source;
        public string SourceDependencyHash;
        public int SourceVertexCount;
        public Level[] Levels = Array.Empty<Level>();
    }
}
