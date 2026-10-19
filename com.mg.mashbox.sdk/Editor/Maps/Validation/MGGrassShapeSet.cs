using UnityEngine;

namespace MashBoxSDK.MapTools
{
    // Authoring only. Runtime prefabs reference the mesh and texture assets directly.
    public sealed class MGGrassShapeSet : ScriptableObject
    {
        public GameObject sourceFbx;
        public string[] shapePaths = new string[8];
        public bool bakeRotationAndScale = true;
        [Min(0)] public float boundsPadding = 0.5f;
        public Mesh bakedMesh;
        public Texture2DArray positions;
        public Texture2DArray uvs;
        public bool bakeVertexAO;
        public Texture2DArray vertexAO;
    }
}
