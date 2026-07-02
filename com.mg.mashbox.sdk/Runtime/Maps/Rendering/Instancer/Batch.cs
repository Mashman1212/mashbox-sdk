using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Map.Rendering.Instancer
{
    class Batch
    {
        public Mesh mesh;
        public Material material;

        public List<Matrix4x4[]> matrixChunks = new();
        public List<MaterialPropertyBlock> mpbChunks = new();
    }
}