using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.Map.Rendering.Instancer
{
    public class InstancingManager : MonoBehaviour
    {
        static InstancingManager instance;
        public static InstancingManager ExistingInstance => instance;
        public static InstancingManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<InstancingManager>();
                    if (instance == null)
                        instance = new GameObject("InstancingManager").AddComponent<InstancingManager>();
                }
                return instance;
            }
        }

        [SerializeField, Min(1f)] float cellSize = 100f;
        [SerializeField] bool useCameraCulling = true;
        [SerializeField, Min(0f), Tooltip("Extra world-space bounds for shader deformation. Impostor meshes must also have bounds covering their rotated geometry.")]
        float boundsPadding = 2f;
        [SerializeField, Min(0f), Tooltip("Optional distance limit for non-shadow-casting batches. Zero keeps the full camera range.")]
        float maximumDistance;
        readonly Plane[] frustumPlanes = new Plane[6];
        readonly List<DrawCell> cells = new();
        public int LastSubmittedBatches { get; private set; }
        public int LastCulledBatches { get; private set; }
        public int LastSubmittedInstances { get; private set; }
        public int TotalBatches => batches.Count;
        public int TotalCells => cells.Count;
        public Camera LastCamera { get; private set; }
        static readonly Unity.Profiling.ProfilerMarker SubmitMarker = new("InstanceGroups.CullAndSubmit");
        static readonly Unity.Profiling.ProfilerMarker RebuildMarker = new("InstanceGroups.Rebuild");
        readonly List<InstanceGroup> groups = new();
        readonly List<MeshRenderer> suppressed = new();
        readonly List<DrawBatch> batches = new();
        readonly Dictionary<Material, Material> materials = new();
        readonly Dictionary<InstanceGroup, Matrix4x4> groupMatrices = new();
        bool dirty = true;

        sealed class DrawCell
        {
            public Bounds bounds;
            public readonly List<DrawBatch> batches = new();
        }

        sealed class DrawBatch
        {
            public Bounds bounds;
            public Mesh mesh;
            public Material material;
            public int submesh, layer;
            public ShadowCastingMode shadows;
            public bool receiveShadows;
            public LightProbeUsage probes;
            public Matrix4x4[] matrices;
            public MaterialPropertyBlock properties;
        }

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(this); return; }
            instance = this;
            if (transform.parent == null) DontDestroyOnLoad(gameObject);
        }

        void OnEnable()
        {
            dirty = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            Camera.onPreCull += OnCameraPreCull;
        }
        void OnValidate() => dirty = true;
        public void MarkDirty() => dirty = true;

        public void RegisterGroup(InstanceGroup group)
        {
            if (!groups.Contains(group)) groups.Add(group);
            dirty = true;
        }

        public void UnregisterGroup(InstanceGroup group)
        {
            groups.Remove(group);
            // Stop drawing the old cache immediately; restore source renderers until rebuilt.
            ReleaseCache();
            dirty = true;
        }

        void LateUpdate()
        {
            EnsureCache();
            // Retained for direct A/B profiling against the previous submission path.
            if (!useCameraCulling)
                foreach (var batch in batches)
                    Graphics.DrawMeshInstanced(batch.mesh, batch.submesh, batch.material,
                        batch.matrices, batch.matrices.Length, batch.properties, batch.shadows,
                        batch.receiveShadows, batch.layer, null, batch.probes);
        }

        void EnsureCache()
        {
            foreach (var group in groups)
                if (group == null || !groupMatrices.TryGetValue(group, out var matrix) ||
                    matrix != group.transform.localToWorldMatrix) { dirty = true; break; }
            if (!dirty) return;
            using (RebuildMarker.Auto()) Rebuild();
            dirty = false;
        }

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera) => RenderCamera(camera);
        void OnCameraPreCull(Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline == null) RenderCamera(camera);
        }

        void RenderCamera(Camera camera)
        {
            if (!useCameraCulling || !isActiveAndEnabled || camera == null) return;
            EnsureCache();
            using (SubmitMarker.Auto())
            {
                LastCamera = camera;
                LastSubmittedBatches = LastCulledBatches = LastSubmittedInstances = 0;
                GeometryUtility.CalculateFrustumPlanes(camera.cullingMatrix, frustumPlanes);
                Vector3 position = camera.transform.position;
                float distanceSquared = maximumDistance * maximumDistance;
                foreach (var cell in cells)
                {
                    bool cellVisible = GeometryUtility.TestPlanesAABB(frustumPlanes, cell.bounds);
                    foreach (var batch in cell.batches)
                    {
                        if ((camera.cullingMask & (1 << batch.layer)) == 0)
                        { LastCulledBatches++; continue; }
                        bool visible = cellVisible && GeometryUtility.TestPlanesAABB(frustumPlanes, batch.bounds);
                        if (maximumDistance > 0f && batch.bounds.SqrDistance(position) > distanceSquared) visible = false;
                        // Off-screen geometry may still cast a visible shadow. Background
                        // materials have shadows off and skip submission entirely here.
                        if (!visible && batch.shadows == ShadowCastingMode.Off)
                        { LastCulledBatches++; continue; }
                        Graphics.DrawMeshInstanced(batch.mesh, batch.submesh, batch.material,
                            batch.matrices, batch.matrices.Length, batch.properties,
                            visible ? batch.shadows : ShadowCastingMode.ShadowsOnly,
                            batch.receiveShadows, batch.layer, camera, batch.probes);
                        LastSubmittedBatches++;
                        LastSubmittedInstances += batch.matrices.Length;
                    }
                }
            }
        }

        static Bounds TransformBounds(Bounds local, Matrix4x4 matrix)
        {
            Vector3 x = matrix.MultiplyVector(new Vector3(local.extents.x, 0, 0));
            Vector3 y = matrix.MultiplyVector(new Vector3(0, local.extents.y, 0));
            Vector3 z = matrix.MultiplyVector(new Vector3(0, 0, local.extents.z));
            var extents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(matrix.MultiplyPoint3x4(local.center), extents * 2f);
        }

        void ReleaseCache()
        {
            foreach (var renderer in suppressed)
                if (renderer != null) renderer.forceRenderingOff = false;
            suppressed.Clear();
            batches.Clear();
            cells.Clear();
            groupMatrices.Clear();
            foreach (var material in materials.Values)
                if (material != null)
                {
                    if (Application.isPlaying) Destroy(material);
                    else DestroyImmediate(material);
                }
            materials.Clear();
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            Camera.onPreCull -= OnCameraPreCull;
            ReleaseCache(); dirty = true;
        }
        void OnDestroy() { ReleaseCache(); if (instance == this) instance = null; }

        void Rebuild()
        {
            ReleaseCache();
            groups.RemoveAll(g => g == null);
            if (!SystemInfo.supportsInstancing) return;
            var collected = new Dictionary<(Vector3Int cell, Mesh mesh, Material material,
                int submesh, int layer, ShadowCastingMode shadows, bool receive, LightProbeUsage probes),
                List<(Matrix4x4 matrix, Vector3 probePosition)>>();
            var visited = new HashSet<MeshRenderer>();
            foreach (var group in groups)
            {
                groupMatrices[group] = group.transform.localToWorldMatrix;
                if (!group.isActiveAndEnabled) continue;
                foreach (var cached in group.CachedRenderers)
                {
                    var world = group.transform.localToWorldMatrix;
                    var cell = Vector3Int.FloorToInt(world.MultiplyPoint3x4(cached.localCenter) / Mathf.Max(1f, cellSize));
                    for (int submesh = 0; submesh < cached.materials.Length; submesh++)
                    {
                        var key = (cell, cached.mesh, cached.materials[submesh], submesh, cached.layer,
                            cached.shadows, cached.receiveShadows, cached.probes);
                        if (!collected.TryGetValue(key, out var entries))
                            collected.Add(key, entries = new List<(Matrix4x4, Vector3)>());
                        entries.Add((world * cached.localMatrix, world.MultiplyPoint3x4(cached.localProbe)));
                    }
                }
                foreach (var renderer in group.GetRenderers())
                {
                    if (renderer == null || !visited.Add(renderer) || !renderer.enabled || renderer.forceRenderingOff) continue;
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    var mesh = filter.sharedMesh;
                    var sourceMaterials = renderer.sharedMaterials;
                    // Preserve unsupported rendering features through the original renderer.
                    var lod = renderer.GetComponentInParent<LODGroup>();
                    if ((lod != null && lod.lodCount > 1) || renderer.HasPropertyBlock() ||
                        renderer.lightmapIndex >= 0 || renderer.realtimeLightmapIndex >= 0 ||
                        renderer.renderingLayerMask != 1 ||
                        (renderer.lightProbeUsage != LightProbeUsage.Off &&
                         renderer.lightProbeUsage != LightProbeUsage.BlendProbes) ||
                        sourceMaterials.Length != mesh.subMeshCount) continue;
                    bool valid = true;
                    foreach (var material in sourceMaterials)
                        if (material == null || material.shader == null || !material.shader.isSupported)
                            { valid = false; break; }
                    if (!valid) continue;
                    var position = renderer.bounds.center;
                    var cell = Vector3Int.FloorToInt(position / Mathf.Max(1f, cellSize));
                    var probePosition = renderer.probeAnchor != null ? renderer.probeAnchor.position : position;
                    for (int submesh = 0; submesh < sourceMaterials.Length; submesh++)
                    {
                        var key = (cell, mesh, sourceMaterials[submesh], submesh, renderer.gameObject.layer,
                            renderer.shadowCastingMode, renderer.receiveShadows, renderer.lightProbeUsage);
                        if (!collected.TryGetValue(key, out var entries))
                            collected.Add(key, entries = new List<(Matrix4x4, Vector3)>());
                        entries.Add((renderer.localToWorldMatrix, probePosition));
                    }
                    suppressed.Add(renderer);
                }
            }

            var cellLookup = new Dictionary<Vector3Int, DrawCell>();
            foreach (var entry in collected)
            {
                var key = entry.Key;
                if (!materials.TryGetValue(key.material, out var material))
                {
                    material = new Material(key.material) { enableInstancing = true,
                        name = key.material.name + " (Instance Group)", hideFlags = HideFlags.HideAndDontSave };
                    materials.Add(key.material, material);
                }
                const int batchLimit = 1023;
                for (int start = 0; start < entry.Value.Count; start += batchLimit)
                {
                    int count = Mathf.Min(batchLimit, entry.Value.Count - start);
                    var matrices = new Matrix4x4[count];
                    var positions = new Vector3[count];
                    for (int i = 0; i < count; i++)
                    {
                        matrices[i] = entry.Value[start + i].matrix;
                        positions[i] = entry.Value[start + i].probePosition;
                    }
                    MaterialPropertyBlock properties = null;
                    var probes = key.probes;
                    if (probes == LightProbeUsage.BlendProbes)
                    {
                        var sh = new SphericalHarmonicsL2[count];
                        var occlusion = new Vector4[count];
                        LightProbes.CalculateInterpolatedLightAndOcclusionProbes(positions, sh, occlusion);
                        properties = new MaterialPropertyBlock();
                        properties.CopySHCoefficientArraysFrom(sh);
                        properties.CopyProbeOcclusionArrayFrom(occlusion);
                        probes = LightProbeUsage.CustomProvided;
                    }
                    Bounds bounds = TransformBounds(key.mesh.bounds, matrices[0]);
                    for (int i = 1; i < count; i++) bounds.Encapsulate(TransformBounds(key.mesh.bounds, matrices[i]));
                    bounds.Expand(Mathf.Max(0f, boundsPadding) * 2f);
                    var batch = new DrawBatch { bounds = bounds, mesh = key.mesh, material = material, submesh = key.submesh,
                        layer = key.layer, shadows = key.shadows, receiveShadows = key.receive,
                        probes = probes, matrices = matrices, properties = properties };
                    batches.Add(batch);
                    if (!cellLookup.TryGetValue(key.cell, out var cell))
                    {
                        cell = new DrawCell { bounds = bounds };
                        cellLookup.Add(key.cell, cell); cells.Add(cell);
                    }
                    else cell.bounds.Encapsulate(bounds);
                    cell.batches.Add(batch);
                }
            }
            foreach (var renderer in suppressed) renderer.forceRenderingOff = true;
            // Commit only after all draw batches have been built successfully.
            foreach (var group in groups)
                if (group.isActiveAndEnabled) group.CommitRuntimeChildren(suppressed);
        }
    }
}
