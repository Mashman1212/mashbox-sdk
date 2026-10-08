using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_6000_0_OR_NEWER
using BorderPhysicsMaterial = UnityEngine.PhysicsMaterial;
#else
using BorderPhysicsMaterial = UnityEngine.PhysicMaterial;
#endif

namespace MashBoxSDK.Maps
{
    /// <summary>A unit-box authoring proxy. Transform scale is width, height and thickness.</summary>
    [AddComponentMenu("MashBox/Maps/World Border Wall")]
    [DisallowMultipleComponent]
    public sealed class MBWorldBorderWall : MonoBehaviour
    {
        [Tooltip("Assigned by the SDK tool and included as a map dependency.")]
        public Material materialTemplate;
        public Texture2D albedoOverride;
        public Color tint = Color.white;
        [Range(0, 1)] public float opacity = .25f;
        [Min(.01f)] public float patternRepeat = 5.12f;
        [Min(.01f)] public float visibilityDistance = 20;
        [Min(.01f)] public float fadeWidth = 10;
        [Min(1)] public float tileSize = 128;
        public bool solidCollision = true;
        public BorderPhysicsMaterial physicsMaterial;
        [Range(1, 179)] public float maxVerticalFov = 100;
        [Min(.01f)] public float minLodBias = 1;
        [Min(0)] public float cullPadding = 5;

        public const int MaxTiles = 8192;
        [NonSerialized] GameObject generated;
        [NonSerialized] Material runtimeMaterial;
        readonly List<Mesh> meshes = new List<Mesh>();
        public GameObject RuntimeInstance => generated;
        public Vector3 WorldSize => new Vector3(transform.TransformVector(Vector3.right).magnitude,
            transform.TransformVector(Vector3.up).magnitude, transform.TransformVector(Vector3.forward).magnitude);

        public int TileCount
        {
            get
            {
                var size = WorldSize;
                if (!Finite(tileSize) || tileSize < 1 || !Finite(size.x) || !Finite(size.y)) return 0;
                double count = (double)AxisTiles(size.x) * AxisTiles(size.y);
                return (int)Math.Min(MaxTiles + 1, count);
            }
        }

        void OnEnable() { if (Application.isPlaying) GenerateRuntime(); }
        void OnDisable() => ReleaseRuntime();
        void OnDestroy() => ReleaseRuntime();
#if UNITY_EDITOR
        void Reset()
        {
            materialTemplate = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Packages/com.mg.mashbox.sdk/Shaders/WorldBorders/World Border Default.mat");
        }
#endif

        // Also covers Enter Play Mode with scene reload disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InitializeLoadedWalls()
        {
            foreach (var wall in FindObjectsOfType<MBWorldBorderWall>())
                if (wall.isActiveAndEnabled) wall.GenerateRuntime();
        }

        public string Problem()
        {
            var size = WorldSize;
            if (!Finite(size.x) || !Finite(size.y) || !Finite(size.z) || Mathf.Min(size.x, size.y, size.z) < .001f)
                return "Scale the proxy to a non-zero width, height and thickness.";
            var matrix = transform.localToWorldMatrix;
            Vector3 x = matrix.MultiplyVector(Vector3.right) / size.x;
            Vector3 y = matrix.MultiplyVector(Vector3.up) / size.y;
            Vector3 z = matrix.MultiplyVector(Vector3.forward) / size.z;
            if (Vector3.Dot(Vector3.Cross(x, y), z) < .999f ||
                Mathf.Abs(Vector3.Dot(x, y)) > .001f || Mathf.Abs(Vector3.Dot(x, z)) > .001f || Mathf.Abs(Vector3.Dot(y, z)) > .001f)
                return "Use positive scale and avoid sheared parents. Put rotated walls under an unscaled group.";
            if (!Finite(tileSize) || tileSize < 1 || TileCount < 1 || TileCount > MaxTiles)
                return "Increase tile size: a wall supports 1 to 8,192 runtime tiles.";
            if (!Finite(visibilityDistance) || !Finite(fadeWidth) || visibilityDistance <= 0 || fadeWidth <= 0 || fadeWidth > visibilityDistance ||
                !Finite(patternRepeat) || patternRepeat <= 0 || !Finite(opacity) || opacity < 0 || opacity > 1 ||
                !Finite(tint.r) || !Finite(tint.g) || !Finite(tint.b) || !Finite(tint.a))
                return "Use finite appearance settings and a fade width between zero and the visibility distance.";
            if (!Finite(maxVerticalFov) || maxVerticalFov < 1 || maxVerticalFov >= 179 ||
                !Finite(minLodBias) || minLodBias <= 0 || !Finite(cullPadding) || cullPadding < 0)
                return "Check the camera FOV, LOD bias and culling margin.";
            if (!materialTemplate || !materialTemplate.shader || !materialTemplate.HasProperty("_VISIBILITY_DISTANCE") ||
                !materialTemplate.HasProperty("_FADE_WIDTH") || !materialTemplate.HasProperty("_ALBEDO") ||
                !materialTemplate.HasProperty("_BORDER_OPACITY"))
                return "Assign a compatible World Border material from the SDK.";
            if (!albedoOverride && !materialTemplate.GetTexture("_ALBEDO")) return "The border material needs an albedo texture.";
            return null;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        // Rotated transforms can report 1024.0001 m for an exact 1024 m wall.
        // Keep that rounding noise from creating an entire extra row/column.
        int AxisTiles(float length) => (int)Math.Min(MaxTiles + 1, Math.Max(1, Math.Ceiling((double)length / tileSize - .00001)));

        /// <summary>Idempotent startup, also callable by a map loader. No per-frame polling.</summary>
        public void GenerateRuntime()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || generated) return;
            string issue = Problem();
            if (issue != null) { Debug.LogError("World border '" + name + "': " + issue, this); return; }
            BuildGeometry();
        }

        /// <summary>Explicit rebuild for runtime editors after changing a proxy transform or settings.</summary>
        public void RebuildRuntime() { ReleaseRuntime(); GenerateRuntime(); }

        // Shared with the isolated SDK validation. Never invoked automatically in edit mode.
        public void BuildGeometry()
        {
            string issue = Problem();
            if (issue != null) throw new InvalidOperationException(issue);
            ReleaseRuntime();
            try
            {
                var size = WorldSize;
                generated = new GameObject("Runtime World Border") { hideFlags = HideFlags.DontSave };
                generated.SetActive(false);
                generated.transform.SetParent(transform, false);
                // Cancel authoring scale so tile sizes and LOD distances are measured in metres.
                generated.transform.localScale = new Vector3(1 / size.x, 1 / size.y, 1 / size.z);
                generated.layer = gameObject.layer;
                generated.tag = gameObject.tag;
                if (solidCollision)
                {
                    var collider = generated.AddComponent<BoxCollider>();
                    collider.size = size;
                    collider.sharedMaterial = physicsMaterial;
                }

                runtimeMaterial = new Material(materialTemplate) { name = name + " Runtime Border", hideFlags = HideFlags.DontSave };
                runtimeMaterial.SetFloat("_VISIBILITY_DISTANCE", visibilityDistance);
                runtimeMaterial.SetFloat("_FADE_WIDTH", fadeWidth);
                runtimeMaterial.SetFloat("_BORDER_OPACITY", opacity);
                runtimeMaterial.SetColor("_COLOUR_OVERLAY", tint);
                runtimeMaterial.SetVector("_TILING", Vector4.one);
                runtimeMaterial.SetVector("_OFFSET", Vector4.zero);
                runtimeMaterial.SetFloat("_DoubleSidedEnable", 1);
                runtimeMaterial.SetFloat("_TransparentCullMode", 0);
                runtimeMaterial.SetFloat("_CullMode", 0);
                runtimeMaterial.SetFloat("_CullModeForward", 0);
                runtimeMaterial.EnableKeyword("_DOUBLESIDED_ON");
                runtimeMaterial.SetVector("_DoubleSidedConstants", new Vector4(1, 1, -1, 0));
                if (albedoOverride) runtimeMaterial.SetTexture("_ALBEDO", albedoOverride);

                int columns = AxisTiles(size.x), rows = AxisTiles(size.y);
                float width = size.x / columns, height = size.y / rows;
                for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    var tile = new GameObject("Tile " + (row * columns + column + 1)) { layer = gameObject.layer };
                    tile.transform.SetParent(generated.transform, false);
                    tile.transform.localPosition = new Vector3(-size.x * .5f + (column + .5f) * width,
                        -size.y * .5f + (row + .5f) * height, 0);
                    var mesh = new Mesh { name = "World Border Tile", hideFlags = HideFlags.DontSave };
                    meshes.Add(mesh);
                    mesh.vertices = new[] { new Vector3(-width/2,-height/2,0), new Vector3(width/2,-height/2,0),
                        new Vector3(width/2,height/2,0), new Vector3(-width/2,height/2,0) };
                    mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                    float u = column * width / patternRepeat, v = row * height / patternRepeat;
                    mesh.uv = new[] { new Vector2(u,v), new Vector2(u+width/patternRepeat,v),
                        new Vector2(u+width/patternRepeat,v+height/patternRepeat), new Vector2(u,v+height/patternRepeat) };
                    mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                    tile.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = tile.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = runtimeMaterial;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    renderer.rayTracingMode = UnityEngine.Experimental.Rendering.RayTracingMode.Off;
                    var lod = tile.AddComponent<LODGroup>();
                    lod.fadeMode = LODFadeMode.None;
                    lod.localReferencePoint = Vector3.zero;
                    lod.size = Mathf.Max(width, height);
                    float radius = Mathf.Sqrt(width * width + height * height) * .5f;
                    float threshold = lod.size * minLodBias / (2 * (visibilityDistance + radius + cullPadding) * Mathf.Tan(maxVerticalFov * Mathf.Deg2Rad * .5f));
                    lod.SetLODs(new[] { new LOD(Mathf.Min(threshold, .99f), new Renderer[] { renderer }) });
                }
                generated.SetActive(isActiveAndEnabled);
            }
            catch { ReleaseRuntime(); throw; }
        }

        public void ReleaseRuntime()
        {
            if (generated) { generated.SetActive(false); Dispose(generated); generated = null; }
            foreach (var mesh in meshes) if (mesh) Dispose(mesh);
            meshes.Clear();
            if (runtimeMaterial) { Dispose(runtimeMaterial); runtimeMaterial = null; }
        }

        static void Dispose(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
