using System;
using UnityEngine;

namespace MashBoxSDK.Culling
{
    public enum CullingRegion { Body, Bust }

    // A self-contained, bind-space reference. No prefab, scene, or third-party culler dependency.
    public sealed class ClothingCullingSurface : ScriptableObject
    {
        public CullingRegion region;
        public Vector3[] vertices = Array.Empty<Vector3>();
        public int[] triangles = Array.Empty<int>();
        public string[] boneNames = Array.Empty<string>();
        public Matrix4x4[] bindposes = Array.Empty<Matrix4x4>();
        public string sourceGuid;
        public int TriangleCount => triangles.Length / 3;
    }
}
