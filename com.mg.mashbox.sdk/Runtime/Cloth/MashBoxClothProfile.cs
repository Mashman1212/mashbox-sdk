using System;
using UnityEngine;

namespace MashBoxSDK.Cloth
{
    [CreateAssetMenu(menuName = "MashBox/Cloth Profile", fileName = "Cloth Profile")]
    public sealed class MashBoxClothProfile : ScriptableObject
    {
        public Mesh sourceMesh;
        [Tooltip("Per render vertex movement allowance in metres. Zero pins to the animated pose.")]
        public float[] motion = Array.Empty<float>();
        public ClothBoneCapsule[] bodyCapsules = Array.Empty<ClothBoneCapsule>();
        [Range(32, 8192)] public int particleBudget = 1500;
        [Range(15, 120)] public int frequency = 60;
        [Range(1, 8)] public int substeps = 2;
        [Range(1, 16)] public int iterations = 4;
        [Min(0)] public float stretchCompliance = 0.000001f;
        [Min(0)] public float bendCompliance = 0.001f;
        [Range(0, 20)] public float damping = 3;
        public Vector3 gravity = new Vector3(0, -9.81f, 0);
        public Vector3 wind = new Vector3(0, 0, 4);
        [Range(0, 1)] public float gust = .25f;
        [Range(0.001f, .05f)] public float thickness = .008f;
        [Min(.1f)] public float teleportDistance = 2;
        [Range(0, 60)] public int normalsEverySteps = 2;

        public bool Matches(Mesh mesh) => mesh && sourceMesh == mesh && motion != null && motion.Length == mesh.vertexCount;

        public void Initialize(Mesh mesh)
        {
            if (!mesh || !mesh.isReadable) throw new ArgumentException("Cloth requires a readable mesh. Enable Read/Write on its importer.");
            sourceMesh = mesh;
            var vertices = mesh.vertices;
            motion = new float[vertices.Length];
            // A usable starting point; users paint garment-specific attachment regions.
            float pinHeight = mesh.bounds.max.y - mesh.bounds.size.y * .15f;
            for (int i = 0; i < motion.Length; i++) motion[i] = vertices[i].y >= pinHeight ? 0 : .15f;
        }
    }

    [Serializable]
    public struct ClothBoneCapsule
    {
        public string startBone, endBone;
        [Min(.001f)] public float radius;
    }

    [Serializable]
    public struct ClothCapsule
    {
        public Vector3 a, b;
        public float radius;
        public ClothCapsule(Vector3 a, Vector3 b, float radius) { this.a = a; this.b = b; this.radius = radius; }
    }
}
