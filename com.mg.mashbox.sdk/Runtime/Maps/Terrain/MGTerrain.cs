using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.Maps.TerrainSystem
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("MashBox/Maps/MG Terrain")]
    public sealed partial class MGTerrain : MonoBehaviour
    {
        public enum InstanceKind { Detail, Tree }

        [Serializable]
        public sealed class Prototype
        {
            [SerializeField] GameObject m_Prefab;
            [SerializeField] Mesh m_Mesh;
            [SerializeField] Material m_Material;
            [SerializeField] InstanceKind m_Kind;
            [SerializeField, Min(0f)] float m_MaximumDrawDistance = 500f;
            [SerializeField] ShadowCastingMode m_ShadowCasting = ShadowCastingMode.On;
            [SerializeField] bool m_ReceiveShadows = true;

            public GameObject Prefab => m_Prefab;
            public Mesh Mesh => m_Mesh;
            public Material Material => m_Material;
            public InstanceKind Kind => m_Kind;
            public float MaximumDrawDistance => m_MaximumDrawDistance;
            public ShadowCastingMode ShadowCasting => m_ShadowCasting;
            public bool ReceiveShadows => m_ReceiveShadows;

            internal Prototype(GameObject prefab, InstanceKind kind, float maximumDrawDistance)
            {
                m_Prefab = prefab;
                m_Kind = kind;
                m_MaximumDrawDistance = Mathf.Max(0f, maximumDrawDistance);
                m_ShadowCasting = kind == InstanceKind.Tree ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }

            internal Prototype(Mesh mesh, Material material, InstanceKind kind, float maximumDrawDistance)
            {
                m_Mesh = mesh;
                m_Material = material;
                m_Kind = kind;
                m_MaximumDrawDistance = Mathf.Max(0f, maximumDrawDistance);
                m_ShadowCasting = kind == InstanceKind.Tree ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }

            internal void ConfigureAsDenseDetail()
            {
                m_ShadowCasting = ShadowCastingMode.Off;
            }
        }

        [Serializable]
        public struct TerrainInstance
        {
            [SerializeField] int m_PrototypeIndex;
            [SerializeField] Vector3 m_LocalPosition;
            [SerializeField] Quaternion m_LocalRotation;
            [SerializeField] Vector3 m_LocalScale;
            [SerializeField] float m_SurfaceOffset;

            public int PrototypeIndex => m_PrototypeIndex;
            public Vector3 LocalPosition => m_LocalPosition;
            public Quaternion LocalRotation => m_LocalRotation;
            public Vector3 LocalScale => m_LocalScale;
            public float SurfaceOffset => m_SurfaceOffset;

            internal TerrainInstance(int prototypeIndex, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, float surfaceOffset)
            {
                m_PrototypeIndex = prototypeIndex;
                m_LocalPosition = localPosition;
                m_LocalRotation = localRotation;
                m_LocalScale = localScale;
                m_SurfaceOffset = surfaceOffset;
            }

            internal void SetLocalPosition(Vector3 value) => m_LocalPosition = value;
        }

        sealed class DrawBatch
        {
            internal Mesh mesh;
            internal int subMesh;
            internal Material material;
            internal Prototype prototype;
            internal readonly List<Matrix4x4[]> matrixChunks = new List<Matrix4x4[]>();
            internal bool forceNonInstanced;
            internal LightProbeUsage lightProbeUsage = LightProbeUsage.BlendProbes;
            internal ShadowCastingMode? shadowCastingOverride;
        }

        readonly struct RenderPart
        {
            internal readonly Mesh mesh;
            internal readonly int subMesh;
            internal readonly Material material;
            internal readonly Matrix4x4 relativeMatrix;

            internal RenderPart(Mesh mesh, int subMesh, Material material, Matrix4x4 relativeMatrix)
            {
                this.mesh = mesh;
                this.subMesh = subMesh;
                this.material = material;
                this.relativeMatrix = relativeMatrix;
            }
        }

        [SerializeField] MeshFilter m_MeshFilter;
        [SerializeField] MeshRenderer m_MeshRenderer;
        [SerializeField] MeshCollider m_MeshCollider;
        [SerializeField, HideInInspector] Transform m_SurfaceColliderRoot;
        [SerializeField, HideInInspector] MeshCollider[] m_SurfaceColliderChunks = Array.Empty<MeshCollider>();
        public IReadOnlyList<MeshCollider> SurfaceColliderChunks => m_SurfaceColliderChunks;
        public bool HasSurfaceCollider
        {
            get
            {
                if (MeshCollider != null && MeshCollider.enabled && MeshCollider.gameObject.activeInHierarchy) return true;
                foreach (var chunk in m_SurfaceColliderChunks)
                    if (chunk != null && chunk.enabled && chunk.gameObject.activeInHierarchy) return true;
                return false;
            }
        }
        public bool RaycastSurface(Ray ray, out RaycastHit hit, float maximumDistance)
        {
            hit = default;
            bool found = false;
            if (MeshCollider != null && MeshCollider.enabled && MeshCollider.gameObject.activeInHierarchy
                && MeshCollider.Raycast(ray, out hit, maximumDistance))
            { found = true; maximumDistance = hit.distance; }
            foreach (var chunk in m_SurfaceColliderChunks)
                if (chunk != null && chunk.enabled && chunk.gameObject.activeInHierarchy
                    && chunk.Raycast(ray, out RaycastHit candidate, maximumDistance))
                { hit = candidate; found = true; maximumDistance = hit.distance; }
            return found;
        }
        [SerializeField, Tooltip("Constrains Mesh Sculpt strokes to the terrain's local Y axis.")]
        bool m_HeightOnlySculpt = true;
        [SerializeField] bool m_DrawInstances = true;
        [SerializeField] bool m_DrawInstancesInEditMode = true;
        [SerializeField, Min(0f)] float m_DefaultTreeDistance = 1000f;
        [SerializeField, Min(0f)] float m_DefaultDetailDistance = 250f;
        [SerializeField] Texture2D m_ControlMap1;
        [SerializeField] Texture2D m_ControlMap2;
        [SerializeField] Texture2D m_FarGrassBake;
        [SerializeField] bool m_ShowFarGrass = true;
        [SerializeField, Min(0f)] float m_FarGrassBlendStart = 60f;
        [SerializeField, Min(0f)] float m_FarGrassBlendEnd = 150f;
        [SerializeField, Range(0f, 1f)] float m_FarGrassStrength = 1f;
        MaterialPropertyBlock m_FarGrassProperties;

        void ApplyFarGrassProperties(bool active = true)
        {
            // Untouched terrains should not acquire a property block or lose SRP batching.
            if (m_FarGrassBake == null && m_FarGrassProperties == null) return;
            if (MeshRenderer == null || MeshFilter == null || MeshFilter.sharedMesh == null) return;
            m_FarGrassProperties ??= new MaterialPropertyBlock();
            MeshRenderer.GetPropertyBlock(m_FarGrassProperties);
            Bounds bounds = MeshFilter.sharedMesh.bounds;
            m_FarGrassProperties.SetTexture("_MGFarGrassMap", m_FarGrassBake != null ? m_FarGrassBake : Texture2D.blackTexture);
            m_FarGrassProperties.SetMatrix("_MGFarGrassWorldToLocal", MeshFilter.transform.worldToLocalMatrix);
            m_FarGrassProperties.SetVector("_MGFarGrassBounds", new Vector4(bounds.min.x, bounds.min.z, 1f / Mathf.Max(.001f, bounds.size.x), 1f / Mathf.Max(.001f, bounds.size.z)));
            m_FarGrassProperties.SetVector("_MGFarGrassSettings", new Vector4(m_FarGrassBlendStart, Mathf.Max(m_FarGrassBlendStart + .01f, m_FarGrassBlendEnd), m_FarGrassStrength, active && m_ShowFarGrass && m_FarGrassBake != null ? 1f : 0f));
            MeshRenderer.SetPropertyBlock(m_FarGrassProperties);
        }
        [SerializeField] List<Prototype> m_Prototypes = new List<Prototype>();
        [SerializeField] List<TerrainInstance> m_Instances = new List<TerrainInstance>();

        [NonSerialized] readonly List<DrawBatch> m_DrawBatches = new List<DrawBatch>();
        [NonSerialized] readonly Dictionary<Material, Material> m_InstancedMaterials = new Dictionary<Material, Material>();
        [NonSerialized] bool m_RenderCacheDirty = true;
        [NonSerialized] Matrix4x4 m_CachedLocalToWorld;
        [NonSerialized] Bounds m_WorldBounds;
        [NonSerialized] readonly Plane[] m_InstanceFrustumPlanes = new Plane[6];
        static readonly Unity.Profiling.ProfilerMarker s_InstanceUpdateMarker = new Unity.Profiling.ProfilerMarker("MGTerrain.InstanceUpdate");

        public MeshFilter MeshFilter => ResolveComponents().meshFilter;
        public MeshRenderer MeshRenderer => ResolveComponents().meshRenderer;
        public MeshCollider MeshCollider => ResolveComponents().meshCollider;
        public bool HeightOnlySculpt { get => m_HeightOnlySculpt; set => m_HeightOnlySculpt = value; }
        public Texture2D ControlMap1 => m_ControlMap1;
        public Texture2D ControlMap2 => m_ControlMap2;
        public IReadOnlyList<Prototype> Prototypes => m_Prototypes;
        public IReadOnlyList<TerrainInstance> Instances => m_Instances;
        public int InstanceCount => m_Instances.Count;

        void Reset()
        {
            ResolveComponents();
            if (m_MeshCollider != null && m_MeshCollider.sharedMesh == null && m_MeshFilter != null)
                m_MeshCollider.sharedMesh = m_MeshFilter.sharedMesh;
        }

        void OnEnable()
        {
            ResolveComponents();
            InitializeDetailSettingsIfNeeded();
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            InvalidateRenderCache();
            if (Application.isPlaying)
            {
                Debug.Log(
                    $"[MG Terrain Runtime] Enabled '{name}': draw={m_DrawInstances}, "
                    + $"densityLayers={DensityDetailLayerCount}, represented={RepresentedDensityDetailCount:N0}, "
                    + $"GPUResident={m_UseBatchRendererGroup}, mesh='{(m_MeshFilter != null && m_MeshFilter.sharedMesh != null ? m_MeshFilter.sharedMesh.name : "none")}'.",
                    this);
            }
        }

        void OnDisable()
        {
            ApplyFarGrassProperties(false);
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            ReleaseDetailRenderCache();
            ReleaseInstancedMaterials();
        }

        void OnValidate()
        {
            ResolveComponents();
            InitializeDetailSettingsIfNeeded();
            InvalidateRenderCache();
        }

        void OnTransformChildrenChanged() => InvalidateRenderCache();

        void OnRenderObject()
        {
            if (GraphicsSettings.currentRenderPipeline == null)
                RenderInstances(Camera.current);
        }

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            ApplyFarGrassProperties();
            RenderInstances(camera);
        }

        public void Configure(MeshFilter meshFilter, MeshRenderer meshRenderer, MeshCollider meshCollider)
        {
            m_MeshFilter = meshFilter;
            m_MeshRenderer = meshRenderer;
            m_MeshCollider = meshCollider;
            InvalidateRenderCache();
        }

        public void SetControlMaps(Texture2D controlMap1, Texture2D controlMap2)
        {
            m_ControlMap1 = controlMap1;
            m_ControlMap2 = controlMap2;
            ApplyControlMapsToMaterial();
        }

        public void ApplyControlMapsToMaterial()
        {
            Material material = MeshRenderer != null ? MeshRenderer.sharedMaterial : null;
            if (material == null) return;
            if (m_ControlMap1 != null && material.HasProperty("_ControlMap1")) material.SetTexture("_ControlMap1", m_ControlMap1);
            if (m_ControlMap2 != null && material.HasProperty("_ControlMap2")) material.SetTexture("_ControlMap2", m_ControlMap2);
        }

        public int FindOrAddPrototype(GameObject prefab, InstanceKind kind)
        {
            if (prefab == null) return -1;
            for (int index = 0; index < m_Prototypes.Count; index++)
            {
                Prototype prototype = m_Prototypes[index];
                if (prototype != null && prototype.Prefab == prefab && prototype.Kind == kind) return index;
            }
            float distance = kind == InstanceKind.Tree ? m_DefaultTreeDistance : m_DefaultDetailDistance;
            m_Prototypes.Add(new Prototype(prefab, kind, distance));
            InvalidateRenderCache();
            return m_Prototypes.Count - 1;
        }

        public int FindOrAddPrototype(Mesh mesh, Material material, InstanceKind kind)
        {
            if (mesh == null || material == null) return -1;
            for (int index = 0; index < m_Prototypes.Count; index++)
            {
                Prototype prototype = m_Prototypes[index];
                if (prototype != null && prototype.Mesh == mesh && prototype.Material == material && prototype.Kind == kind) return index;
            }
            float distance = kind == InstanceKind.Tree ? m_DefaultTreeDistance : m_DefaultDetailDistance;
            m_Prototypes.Add(new Prototype(mesh, material, kind, distance));
            InvalidateRenderCache();
            return m_Prototypes.Count - 1;
        }

        public bool AddInstance(GameObject prefab, InstanceKind kind, Vector3 worldPosition, Quaternion worldRotation, Vector3 worldScale, float surfaceOffset = 0f)
        {
            int prototypeIndex = FindOrAddPrototype(prefab, kind);
            if (prototypeIndex < 0) return false;
            Quaternion localRotation = Quaternion.Inverse(transform.rotation) * worldRotation;
            Vector3 localScale = DivideScale(worldScale, transform.lossyScale);
            AddInstanceLocal(prototypeIndex, transform.InverseTransformPoint(worldPosition), localRotation, localScale, surfaceOffset);
            return true;
        }

        public void AddInstanceLocal(int prototypeIndex, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, float surfaceOffset = 0f)
        {
            if ((uint)prototypeIndex >= m_Prototypes.Count) return;
            m_Instances.Add(new TerrainInstance(prototypeIndex, localPosition, localRotation, localScale, surfaceOffset));
            InvalidateRenderCache();
        }

        public int RemoveInstances(Vector3 worldCenter, float worldRadius, GameObject prefab = null, InstanceKind? kind = null)
        {
            float radiusSquared = worldRadius * worldRadius;
            int removed = 0;
            for (int index = m_Instances.Count - 1; index >= 0; index--)
            {
                TerrainInstance instance = m_Instances[index];
                if ((uint)instance.PrototypeIndex >= m_Prototypes.Count) continue;
                Prototype prototype = m_Prototypes[instance.PrototypeIndex];
                if (prototype == null || (prefab != null && prototype.Prefab != prefab) || (kind.HasValue && prototype.Kind != kind.Value)) continue;
                Vector3 worldPosition = transform.TransformPoint(instance.LocalPosition);
                if ((worldPosition - worldCenter).sqrMagnitude > radiusSquared) continue;
                m_Instances.RemoveAt(index);
                removed++;
            }
            if (removed > 0) InvalidateRenderCache();
            return removed;
        }

        public void ClearInstances()
        {
            m_Instances.Clear();
            InvalidateRenderCache();
        }

        public void ConformInstancesToSurface()
        {
            ConformInstancesToSurface(Vector3.zero, 0f, false);
        }

        public void ConformInstancesToSurface(Vector3 worldCenter, float worldRadius)
        {
            ConformInstancesToSurface(worldCenter, Mathf.Max(0f, worldRadius), true);
        }

        void ConformInstancesToSurface(Vector3 worldCenter, float worldRadius, bool limitToBrush)
        {
            MeshCollider collider = MeshCollider;
            MeshFilter filter = MeshFilter;
            if (!HasSurfaceCollider || filter == null || filter.sharedMesh == null) return;
            Bounds bounds = filter.sharedMesh.bounds;
            float rayHeight = Mathf.Max(1f, bounds.size.y + 2f);
            Vector3 down = -transform.up;
            Vector3 localCenter = limitToBrush ? transform.InverseTransformPoint(worldCenter) : Vector3.zero;
            float radiusSquared = worldRadius * worldRadius;
            bool changed = false;
            for (int index = 0; index < m_Instances.Count; index++)
            {
                TerrainInstance instance = m_Instances[index];
                if (limitToBrush)
                {
                    Vector3 planarDelta = instance.LocalPosition - localCenter;
                    planarDelta.y = 0f;
                    if (transform.TransformVector(planarDelta).sqrMagnitude > radiusSquared)
                        continue;
                }
                Vector3 localOrigin = new Vector3(instance.LocalPosition.x, bounds.max.y + rayHeight, instance.LocalPosition.z);
                Ray ray = new Ray(transform.TransformPoint(localOrigin), down);
                float maxDistance = Mathf.Max(2f, transform.TransformVector(Vector3.up * rayHeight * 2f).magnitude);
                if (!RaycastSurface(ray, out RaycastHit hit, maxDistance)) continue;
                Vector3 localPosition = transform.InverseTransformPoint(hit.point);
                localPosition.y += instance.SurfaceOffset;
                instance.SetLocalPosition(localPosition);
                m_Instances[index] = instance;
                changed = true;
            }
            if (changed) InvalidateRenderCache();
        }

        public void InvalidateRenderCache()
        {
            m_RenderCacheDirty = true;
            InvalidateDetailRenderCache();
        }

        // A capture is scoped to one temporary editor camera. No serialized quality
        // settings are changed, and its full-density cells never survive the scope.
        [NonSerialized] Camera m_AppearanceCaptureCamera;
        [NonSerialized] long m_AppearanceCapturePopulation;
        public bool AppearanceCaptureNeedsSubdivision { get; private set; }
        public bool AppearanceCaptureTileComplete { get; private set; }
        [NonSerialized] long[] m_AppearanceCaptureLayerSubmissions;
        public IReadOnlyList<long> AppearanceCaptureLayerSubmissions => m_AppearanceCaptureLayerSubmissions;
        public void PrepareAppearanceCaptureTile()
        {
            if (m_AppearanceCaptureCamera == null) throw new InvalidOperationException("No appearance capture is active.");
            AppearanceCaptureNeedsSubdivision = false;
            AppearanceCaptureTileComplete = false;
            m_AppearanceCaptureLayerSubmissions = new long[DensityDetailLayerCount];
            InvalidateRenderCache();
        }
        public void BeginAppearanceCapture(Camera camera)
        {
            if (Application.isPlaying || camera == null || m_AppearanceCaptureCamera != null)
                throw new InvalidOperationException("Appearance capture requires an idle terrain in Edit Mode.");
            m_AppearanceCaptureCamera = camera;
            PrepareAppearanceCaptureTile();
        }

        public void EndAppearanceCapture()
        {
            m_AppearanceCaptureCamera = null;
            ReleaseDetailRenderCache();
            InvalidateRenderCache();
        }

        void RenderInstances(Camera camera)
        {
            using var profile = s_InstanceUpdateMarker.Auto();
            if (camera == null || camera.cameraType == CameraType.Preview) return;
            if (m_AppearanceCaptureCamera != null && camera != m_AppearanceCaptureCamera) return;
            if (m_AppearanceCaptureCamera == null && (!m_DrawInstances || (!Application.isPlaying && !m_DrawInstancesInEditMode)))
            {
                // BRG batches persist until explicitly unregistered. Merely skipping this
                // camera callback otherwise leaves the last submitted grass visible while
                // making the disabled renderer appear much cheaper than it really is.
                ReleaseDensityDetailBrg();
                return;
            }
            if (m_Instances.Count == 0 && DensityDetailLayerCount == 0) return;
            Matrix4x4 localToWorld = transform.localToWorldMatrix;
            if (m_RenderCacheDirty || m_CachedLocalToWorld != localToWorld) RebuildRenderCache(localToWorld);
            GeometryUtility.CalculateFrustumPlanes(camera, m_InstanceFrustumPlanes);
            Plane[] planes = m_InstanceFrustumPlanes;
            if (m_DrawBatches.Count > 0 && GeometryUtility.TestPlanesAABB(planes, m_WorldBounds))
            {
                Vector3 cameraPosition = camera.transform.position;
                for (int batchIndex = 0; batchIndex < m_DrawBatches.Count; batchIndex++)
                {
                    DrawBatch batch = m_DrawBatches[batchIndex];
                    float maximumDistance = batch.prototype.MaximumDrawDistance;
                    if (m_AppearanceCaptureCamera == null && maximumDistance > 0f && Vector3.Distance(cameraPosition, m_WorldBounds.ClosestPoint(cameraPosition)) > maximumDistance) continue;
                    DrawBatchInstances(batch, camera);
                }
            }
            RenderDensityDetails(camera, planes);
        }

        void DrawBatchInstances(DrawBatch batch, Camera camera)
        {
            DrawBatchInstances(batch, camera, int.MaxValue, null);
        }

        int DrawBatchInstances(
            DrawBatch batch,
            Camera camera,
            int maximumInstances,
            ShadowCastingMode? shadowCastingOverride)
        {
            int remaining = Mathf.Max(0, maximumInstances);
            if (remaining == 0)
                return 0;
            ShadowCastingMode shadowCasting = shadowCastingOverride
                ?? batch.shadowCastingOverride
                ?? batch.prototype.ShadowCasting;
            if (!SystemInfo.supportsInstancing || batch.forceNonInstanced)
            {
                return DrawBatchWithoutInstancing(batch, camera, remaining, shadowCasting);
            }
            int drawn = 0;
            try
            {
                for (int chunkIndex = 0; chunkIndex < batch.matrixChunks.Count && remaining > 0; chunkIndex++)
                {
                    Matrix4x4[] matrices = batch.matrixChunks[chunkIndex];
                    int count = Mathf.Min(matrices.Length, remaining);
                    Graphics.DrawMeshInstanced(batch.mesh, batch.subMesh, batch.material, matrices, count, null, shadowCasting, batch.prototype.ReceiveShadows, gameObject.layer, camera, batch.lightProbeUsage);
                    remaining -= count;
                    drawn += count;
                }
            }
            catch (InvalidOperationException)
            {
                batch.forceNonInstanced = true;
                return DrawBatchWithoutInstancing(batch, camera, maximumInstances, shadowCasting);
            }
            return drawn;
        }

        int DrawBatchWithoutInstancing(
            DrawBatch batch,
            Camera camera,
            int maximumInstances,
            ShadowCastingMode? shadowCastingOverride = null)
        {
            int remaining = Mathf.Max(0, maximumInstances);
            int drawn = 0;
            ShadowCastingMode shadowCasting = shadowCastingOverride
                ?? batch.shadowCastingOverride
                ?? batch.prototype.ShadowCasting;
            for (int chunkIndex = 0; chunkIndex < batch.matrixChunks.Count && remaining > 0; chunkIndex++)
            {
                Matrix4x4[] matrices = batch.matrixChunks[chunkIndex];
                int count = Mathf.Min(matrices.Length, remaining);
                for (int matrixIndex = 0; matrixIndex < count; matrixIndex++)
                    Graphics.DrawMesh(batch.mesh, matrices[matrixIndex], batch.material, gameObject.layer, camera, batch.subMesh, null, shadowCasting, batch.prototype.ReceiveShadows, null, batch.lightProbeUsage, null);
                remaining -= count;
                drawn += count;
            }
            return drawn;
        }

        void RebuildRenderCache(Matrix4x4 terrainLocalToWorld)
        {
            m_DrawBatches.Clear();
            ReleaseDetailRenderCache();
            ReleaseInstancedMaterials();
            var batchMatrices = new Dictionary<(int prototype, Mesh mesh, int subMesh, Material material), List<Matrix4x4>>();
            var partsByPrototype = new List<RenderPart>[m_Prototypes.Count];
            bool hasBounds = false;
            Bounds bounds = default;
            for (int prototypeIndex = 0; prototypeIndex < m_Prototypes.Count; prototypeIndex++)
            {
                Prototype prototype = m_Prototypes[prototypeIndex];
                if (prototype == null) continue;
                partsByPrototype[prototypeIndex] = GetRenderParts(prototype);
            }
            for (int instanceIndex = 0; instanceIndex < m_Instances.Count; instanceIndex++)
            {
                TerrainInstance instance = m_Instances[instanceIndex];
                int prototypeIndex = instance.PrototypeIndex;
                if ((uint)prototypeIndex >= partsByPrototype.Length) continue;
                List<RenderPart> parts = partsByPrototype[prototypeIndex];
                if (parts == null || parts.Count == 0) continue;
                Matrix4x4 instanceMatrix = terrainLocalToWorld * Matrix4x4.TRS(instance.LocalPosition, instance.LocalRotation, instance.LocalScale);
                for (int partIndex = 0; partIndex < parts.Count; partIndex++)
                {
                    RenderPart part = parts[partIndex];
                    var key = (prototypeIndex, part.mesh, part.subMesh, part.material);
                    if (!batchMatrices.TryGetValue(key, out List<Matrix4x4> matrices))
                    {
                        matrices = new List<Matrix4x4>();
                        batchMatrices.Add(key, matrices);
                    }
                    Matrix4x4 matrix = instanceMatrix * part.relativeMatrix;
                    matrices.Add(matrix);
                    Bounds partBounds = TransformBounds(part.mesh.bounds, matrix);
                    if (!hasBounds) { bounds = partBounds; hasBounds = true; }
                    else bounds.Encapsulate(partBounds);
                }
            }
            foreach (KeyValuePair<(int prototype, Mesh mesh, int subMesh, Material material), List<Matrix4x4>> pair in batchMatrices)
            {
                Prototype prototype = m_Prototypes[pair.Key.prototype];
                var batch = new DrawBatch { mesh = pair.Key.mesh, subMesh = pair.Key.subMesh, material = GetInstancedMaterial(pair.Key.material), prototype = prototype };
                List<Matrix4x4> matrices = pair.Value;
                for (int start = 0; start < matrices.Count; start += 1023)
                {
                    int count = Mathf.Min(1023, matrices.Count - start);
                    var chunk = new Matrix4x4[count];
                    matrices.CopyTo(start, chunk, 0, count);
                    batch.matrixChunks.Add(chunk);
                }
                m_DrawBatches.Add(batch);
            }
            MeshRenderer renderer = MeshRenderer;
            m_WorldBounds = hasBounds ? bounds : renderer != null ? renderer.bounds : new Bounds(transform.position, Vector3.one);
            m_CachedLocalToWorld = terrainLocalToWorld;
            m_RenderCacheDirty = false;
        }

        public void GetDetailSourceMaterials(int prototypeIndex, List<Material> materials)
        {
            if (materials == null) throw new ArgumentNullException(nameof(materials));
            materials.Clear();
            if ((uint)prototypeIndex >= m_Prototypes.Count || m_Prototypes[prototypeIndex] == null) return;
            // Share the renderer's mesh/submesh and LOD selection so the inspector
            // exposes source assets actually used, rather than runtime material copies.
            foreach (RenderPart part in GetRenderParts(m_Prototypes[prototypeIndex]))
                if (part.material != null && !materials.Contains(part.material)) materials.Add(part.material);
        }

        List<RenderPart> GetRenderParts(Prototype prototype)
        {
            var result = new List<RenderPart>();
            if (prototype.Mesh != null && prototype.Material != null)
            {
                int subMeshCount = Mathf.Max(1, prototype.Mesh.subMeshCount);
                for (int subMesh = 0; subMesh < subMeshCount; subMesh++) result.Add(new RenderPart(prototype.Mesh, subMesh, prototype.Material, Matrix4x4.identity));
                return result;
            }
            if (prototype.Prefab == null) return result;
            HashSet<MeshRenderer> allowedRenderers = GetHighestDetailRenderers(prototype.Prefab);
            MeshRenderer[] renderers = prototype.Prefab.GetComponentsInChildren<MeshRenderer>(true);
            Matrix4x4 rootInverse = prototype.Prefab.transform.worldToLocalMatrix;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                MeshRenderer renderer = renderers[rendererIndex];
                if (!renderer.enabled || !allowedRenderers.Contains(renderer)) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                Material[] materials = renderer.sharedMaterials;
                int subMeshCount = Mathf.Min(filter.sharedMesh.subMeshCount, materials.Length);
                Matrix4x4 relative = rootInverse * renderer.transform.localToWorldMatrix;
                for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                    if (materials[subMesh] != null) result.Add(new RenderPart(filter.sharedMesh, subMesh, materials[subMesh], relative));
            }
            return result;
        }

        static HashSet<MeshRenderer> GetHighestDetailRenderers(GameObject prefab)
        {
            var controlled = new HashSet<MeshRenderer>();
            var highest = new HashSet<MeshRenderer>();
            LODGroup[] lodGroups = prefab.GetComponentsInChildren<LODGroup>(true);
            for (int groupIndex = 0; groupIndex < lodGroups.Length; groupIndex++)
            {
                LOD[] lods = lodGroups[groupIndex].GetLODs();
                for (int lodIndex = 0; lodIndex < lods.Length; lodIndex++)
                {
                    Renderer[] renderers = lods[lodIndex].renderers;
                    for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                    {
                        if (!(renderers[rendererIndex] is MeshRenderer meshRenderer)) continue;
                        controlled.Add(meshRenderer);
                        if (lodIndex == 0) highest.Add(meshRenderer);
                    }
                }
            }
            MeshRenderer[] all = prefab.GetComponentsInChildren<MeshRenderer>(true);
            for (int index = 0; index < all.Length; index++) if (!controlled.Contains(all[index])) highest.Add(all[index]);
            return highest;
        }

        Material GetInstancedMaterial(Material source)
        {
            if (source == null) return null;
            if (source.enableInstancing) return source;
            if (m_InstancedMaterials.TryGetValue(source, out Material material) && material != null) return material;
            material = new Material(source) { name = source.name + " (MG Terrain Instanced)", hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            m_InstancedMaterials[source] = material;
            return material;
        }

        void ReleaseInstancedMaterials()
        {
            foreach (Material material in m_InstancedMaterials.Values)
            {
                if (material == null) continue;
                if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
            }
            m_InstancedMaterials.Clear();
        }

        (MeshFilter meshFilter, MeshRenderer meshRenderer, MeshCollider meshCollider) ResolveComponents()
        {
            m_MeshFilter ??= GetComponent<MeshFilter>();
            m_MeshRenderer ??= GetComponent<MeshRenderer>();
            m_MeshCollider ??= GetComponent<MeshCollider>();
            return (m_MeshFilter, m_MeshRenderer, m_MeshCollider);
        }

        static Vector3 DivideScale(Vector3 value, Vector3 divisor)
        {
            return new Vector3(Mathf.Abs(divisor.x) > 0.00001f ? value.x / divisor.x : value.x, Mathf.Abs(divisor.y) > 0.00001f ? value.y / divisor.y : value.y, Mathf.Abs(divisor.z) > 0.00001f ? value.z / divisor.z : value.z);
        }

        static Bounds TransformBounds(Bounds localBounds, Matrix4x4 matrix)
        {
            Vector3 center = matrix.MultiplyPoint3x4(localBounds.center);
            Vector3 extents = localBounds.extents;
            Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
            Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
            Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));
            extents = new Vector3(Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x), Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y), Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(center, extents * 2f);
        }
    }
}
