using System.Collections.Generic;
using UnityEngine;
#if UNITY_6000_0_OR_NEWER
using BorderPhysicsMaterial = UnityEngine.PhysicsMaterial;
#else
using BorderPhysicsMaterial = UnityEngine.PhysicMaterial;
#endif

namespace MashBoxSDK.MapTools.WorldBorders
{
    // Editor-only asset. Generated scene objects have no dependency on this type.
    public sealed class MGWorldBorderProfile : ScriptableObject
    {
        public enum Outline { Rectangle, Polygon }
        public Outline outline;
        public Vector3 origin;
        public Vector2 size = new Vector2(1024, 1024);
        public float rotation;
        public List<Vector2> points = new List<Vector2> { new Vector2(-512,-512), new Vector2(512,-512), new Vector2(512,512), new Vector2(-512,512) };
        public float height = 128;
        public float tileSize = 128;
        public float visibilityDistance = 20;
        public float fadeWidth = 10;
        public Color color = Color.white;
        public float opacity = .25f;
        public float patternSize = 5.12f;
        public Texture2D pattern;
        public Material materialTemplate;
        public bool collision = true;
        public float collisionThickness = .5f;
        public BorderPhysicsMaterial physicsMaterial;
        public int layer;
        public string collisionTag = "Untagged";
        public float maxVerticalFov = 100;
        public float minLodBias = 1f;
        public float cullPadding = 5;
        [HideInInspector] public string rootId;
        [HideInInspector] public string sceneGuid;
        [HideInInspector] public string rootName;
        [HideInInspector] public string generatedFolder;
        [HideInInspector] public string builtSettings;
    }
}
