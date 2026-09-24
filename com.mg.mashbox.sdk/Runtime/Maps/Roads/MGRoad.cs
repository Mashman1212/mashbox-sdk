using UnityEngine.Scripting.APIUpdating;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;
using UnitySpline = UnityEngine.Splines.Spline;

namespace MashBoxSDK.Maps.Roads
{
    [ExecuteAlways, DisallowMultipleComponent]
    [RequireComponent(typeof(SplineContainer), typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("MashBox/Maps/Road")]
    [MovedFrom(true, "MappyX.Roads", "Assembly-CSharp", null)]
    public sealed class MGRoad : MonoBehaviour
    {
        [Min(.1f)] public float width = 6;
        [Min(0)] public float shoulderWidth = 1;
        public float shoulderDrop = .1f;
        public float crown = .08f;
        [Range(-45, 45)] public float bankAngle;
        [Min(.1f)] public float sampleSpacing = 1;
        public Material roadMaterial, shoulderMaterial;
        [Min(.01f)] public float uvMetresAlong = 5, uvMetresAcross = 6;
        public Vector2 uvOffset;
        public bool swapUV;
        public bool generateCollider = true;
        public bool overrideTerrain;
        [HideInInspector] public bool terrainLayerEnabled = true;
        public RoadTerrainSettings terrain = new RoadTerrainSettings();
        [NonSerialized] Mesh generatedMesh;
        [NonSerialized] Mesh ownedMesh;
        [NonSerialized] bool dirty = true;
        Matrix4x4 lastMatrix;
        static Material defaultMaterial;
        public MGRoadNetwork Network => GetComponentInParent<MGRoadNetwork>();
        public SplineContainer Container => GetComponent<SplineContainer>();
        public RoadTerrainSettings TerrainSettings => !overrideTerrain && Network != null ? Network.terrain : terrain;
        // The editor supplies exact MG mesh sampling, without requiring terrain colliders.
        public static Func<MGRoad, Func<Vector3, Vector3>> CreateTerrainProjector;
        public string LastBuildMessage { get; private set; }
        public Mesh GeneratedMesh => generatedMesh;
        public void RequestRebuild() { dirty = true; }
        void OnEnable() { UnitySpline.Changed += Changed; dirty = true; }
        void OnDisable() { UnitySpline.Changed -= Changed; }
        void OnValidate() { dirty = true; }
        void Changed(UnitySpline spline, int index, SplineModification modification)
        { if (Container != null && Container.Spline == spline) dirty = true; }
        void Update()
        {
            if (Application.isPlaying) return;
            if (dirty || lastMatrix != transform.localToWorldMatrix) Rebuild();
        }
        void OnDestroy()
        {
            if (ownedMesh != null) { if (Application.isPlaying) Destroy(ownedMesh); else DestroyImmediate(ownedMesh); }
        }

        public void Rebuild()
        {
            dirty = false; lastMatrix = transform.localToWorldMatrix;
            var spline = Container.Spline;
            LastBuildMessage = null;
            if (ownedMesh == null)
            {
                ownedMesh = new Mesh { name = name + " Road Surface", indexFormat = IndexFormat.UInt32 };
                generatedMesh = ownedMesh;
            }
            ownedMesh.Clear();
            GetComponent<MeshFilter>().sharedMesh = ownedMesh;
            var collider = GetComponent<MeshCollider>();
            if (collider != null) collider.sharedMesh = null;
            if (spline == null || spline.Count < 2) return;

            // Arc-length lookup prevents uneven UVs and tessellation on unequal spline segments.
            int lookupCount = Mathf.Clamp(spline.Count * 64, 128, 65536);
            var lengths = new float[lookupCount + 1];
            Vector3 previous = Container.EvaluatePosition(0);
            for (int i = 1; i <= lookupCount; i++)
            {
                Vector3 p = Container.EvaluatePosition(i / (float)lookupCount);
                lengths[i] = lengths[i - 1] + Vector3.Distance(previous, p); previous = p;
            }
            float length = lengths[lookupCount];
            if (length < .001f) return;
            int segments = Mathf.Clamp(Mathf.CeilToInt(length / Mathf.Max(.1f, sampleSpacing)), spline.Closed ? 3 : 1, 50000);
            Func<Vector3, Vector3> project = null;
            if (TerrainSettings.mode == RoadTerrainMode.RoadFollowsTerrain)
            {
                try { project = CreateTerrainProjector?.Invoke(this); }
                catch (Exception error) { LastBuildMessage = error.Message; }
                if (project == null) LastBuildMessage = "Assign a Terrain World with readable MG terrain meshes to conform this road.";
            }
            var vertices = new Vector3[(segments + 1) * 5];
            var uv = new Vector2[vertices.Length];
            var asphalt = new List<int>(segments * 12);
            var shoulders = new List<int>(segments * 12);
            float half = Mathf.Max(.1f, width) * .5f;
            float edge = half + Mathf.Max(0, shoulderWidth);
            float[] offsets = { -edge, -half, 0, half, edge };
            var ring = new Vector3[5];
            int lookup = 1;
            for (int row = 0; row <= segments; row++)
            {
                float distance = length * row / segments;
                while (lookup < lookupCount && lengths[lookup] < distance) lookup++;
                float fraction = Mathf.InverseLerp(lengths[lookup - 1], lengths[lookup], distance);
                float t = (lookup - 1 + fraction) / lookupCount;
                // Exactly duplicate the first ring at a closed seam (except its longitudinal UV).
                if (spline.Closed && row == segments) t = 0;
                int curve = SplineUtility.SplineToCurveT(spline, t, out float curveT);
                MGRoadKnotShape.Ring(this, spline, curve, curveT, project, ring);
                float widthScale = MGRoadKnotShape.ScaleAt(spline, curve, curveT).x;
                for (int col = 0; col < 5; col++)
                {
                    int index = row * 5 + col;
                    vertices[index] = transform.InverseTransformPoint(ring[col]);
                    Vector2 tex = new Vector2((offsets[col] + half) * widthScale / Mathf.Max(.01f, uvMetresAcross), distance / Mathf.Max(.01f, uvMetresAlong));
                    uv[index] = (swapUV ? new Vector2(tex.y, tex.x) : tex) + uvOffset;
                }
                if (row == 0) continue;
                for (int col = 0; col < 4; col++)
                {
                    if ((col == 0 || col == 3) && shoulderWidth <= 0) continue;
                    var triangles = col == 0 || col == 3 ? shoulders : asphalt;
                    int a = (row - 1) * 5 + col, b = a + 5;
                    triangles.Add(a); triangles.Add(b); triangles.Add(a + 1);
                    triangles.Add(a + 1); triangles.Add(b); triangles.Add(b + 1);
                }
            }
            ownedMesh.vertices = vertices; ownedMesh.uv = uv; ownedMesh.subMeshCount = 2;
            ownedMesh.SetTriangles(asphalt, 0); ownedMesh.SetTriangles(shoulders, 1);
            ownedMesh.RecalculateNormals();
            if (spline.Closed)
            {
                var normals = ownedMesh.normals;
                for (int i = 0; i < 5; i++) { var n = (normals[i] + normals[segments * 5 + i]).normalized; normals[i] = normals[segments * 5 + i] = n; }
                ownedMesh.normals = normals;
            }
            ownedMesh.RecalculateTangents(); ownedMesh.RecalculateBounds();
            var network = Network;
            var surface = roadMaterial != null ? roadMaterial : network != null ? network.defaultRoadMaterial : null;
            if (surface == null)
            {
                if (defaultMaterial == null) defaultMaterial = Resources.Load<Material>("DefaultRoad_Mat");
                surface = defaultMaterial;
            }
            var shoulder = shoulderMaterial != null ? shoulderMaterial : network != null ? network.defaultShoulderMaterial : null;
            GetComponent<MeshRenderer>().sharedMaterials = new[] { surface, shoulder != null ? shoulder : surface };
            if (generateCollider && collider == null) collider = gameObject.AddComponent<MeshCollider>();
            if (collider != null) { collider.enabled = generateCollider; collider.sharedMesh = generateCollider ? ownedMesh : null; }
        }
    }
}
