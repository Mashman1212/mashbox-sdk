using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        public enum DetailQualityPreset
        {
            Low,
            Medium,
            High,
            Ultra
        }

        const double MaximumDetailInstancesPerSquareMetre = 32768.0 / (50.0 * 50.0);
        const int DetailDensityReferenceChunkCells = 32;
        const int CombinedVertexStrideBytes = 52;
        static readonly Unity.Profiling.ProfilerMarker s_DetailSelectionMarker = new Unity.Profiling.ProfilerMarker("MGTerrain.DetailSelection");

        public struct MemoryUsageSnapshot
        {
            public long SurfaceMeshBytes { get; internal set; }
            public long ControlMapBytes { get; internal set; }
            public long DensityMapBytes { get; internal set; }
            public long SerializedInstanceBytes { get; internal set; }
            public long StreamedCombinedMeshBytes { get; internal set; }
            public long MatrixBufferBytes { get; internal set; }
            public long ProceduralCellDataBytes { get; internal set; }
            public long PendingBuildBytes { get; internal set; }
            public long CpuSourceCacheBytes { get; internal set; }
            public long RuntimeMaterialBytes { get; internal set; }
            public int CachedDetailCellCount { get; internal set; }
            public int CombinedMeshSliceCount { get; internal set; }
            public int PendingBuildCount { get; internal set; }

            public long TotalBytes => SurfaceMeshBytes
                + ControlMapBytes
                + DensityMapBytes
                + SerializedInstanceBytes
                + StreamedCombinedMeshBytes
                + MatrixBufferBytes
                + ProceduralCellDataBytes
                + PendingBuildBytes
                + CpuSourceCacheBytes
                + RuntimeMaterialBytes;
        }

        readonly struct DensityDetailSpawn
        {
            internal readonly int x;
            internal readonly int z;
            internal readonly int count;
            internal readonly Vector4 sizes;

            internal DensityDetailSpawn(int x, int z, int count, Vector4 sizes)
            {
                this.x = x;
                this.z = z;
                this.count = count;
                this.sizes = sizes;
            }
        }

        [Serializable]
        public sealed class DensityDetailLayer
        {
            [SerializeField] int m_PrototypeIndex;
            [SerializeField] Texture2D m_DensityMap;
            [SerializeField, Tooltip("Spatial size multipliers painted by the detail brush. No map means 1. Readable RHalf, values 0.05 to 4.")]
            Texture2D m_SizeMap;
            [SerializeField, Min(0), Tooltip("Texture2DArray slice used by the MG Detail Data shader function.")]
            int m_TextureSlice;
            [SerializeField, Tooltip("Albedo approximation for the far grass bake; not a capture of the detail shader.")]
            Color m_FarBakeColor = new Color(.32f, .4f, .12f, 1f);
            public Color FarBakeColor => m_FarBakeColor;
            [SerializeField] Color m_ShaderTint = Color.white;
            [SerializeField, Min(0)] float m_WindMultiplier = 1f;
            public Vector4 ShaderDefinition => new Vector4(Mathf.Max(0, m_TextureSlice), Mathf.Max(0, m_WindMultiplier), 0, 0);
            public Color ShaderTint => m_ShaderTint;
            [SerializeField] float m_MinWidth = 1f;
            [SerializeField] float m_MaxWidth = 1f;
            [SerializeField] float m_MinHeight = 1f;
            [SerializeField] float m_MaxHeight = 1f;
            [SerializeField, Min(0.01f), Tooltip("Scales both width and height ranges together. 1 keeps the original size; 2 doubles it. Does not change density or Y Offset.")]
            float m_SizeMultiplier = 1f;
            [SerializeField, Min(0.01f), Tooltip("Scales the width range in addition to Size Multiplier. Does not change height.")]
            float m_WidthMultiplier = 1f;
            [SerializeField, Min(0.01f), Tooltip("Scales the height range in addition to Size Multiplier. Does not change width or Y Offset.")]
            float m_HeightMultiplier = 1f;
            [SerializeField, Tooltip("Moves every generated instance in this density layer along the MG Terrain's local Y axis. Negative values sink details into the surface.")]
            float m_YOffset;
            [SerializeField] int m_Seed;
            [SerializeField] long m_RepresentedInstanceCount;
            [SerializeField, HideInInspector] bool m_PaletteSourceOnly;
            [SerializeField, HideInInspector] MGDetailFoliagePalette m_GeneratedByPalette;
            [SerializeField, HideInInspector] Texture2D m_PaletteSourceMap;
            [SerializeField, HideInInspector] int m_PaletteEntryIndex = -1;

            public int PrototypeIndex => m_PrototypeIndex;
            public Texture2D DensityMap => m_DensityMap;
            public Texture2D SizeMap => m_SizeMap;
            public float MaximumPaintedHeight => MaxHeight * (m_SizeMap != null ? 4f : 1f);
            internal Vector4 GetPaintedSizes(int x, int z)
            {
                if (m_SizeMap == null || !m_SizeMap.isReadable) return Vector4.one;
                float u = x / (float)m_DensityMap.width, v = z / (float)m_DensityMap.height;
                float du = 1f / m_DensityMap.width, dv = 1f / m_DensityMap.height;
                return new Vector4(
                    Mathf.Clamp(m_SizeMap.GetPixelBilinear(u, v).r, .05f, 4f),
                    Mathf.Clamp(m_SizeMap.GetPixelBilinear(u + du, v).r, .05f, 4f),
                    Mathf.Clamp(m_SizeMap.GetPixelBilinear(u, v + dv).r, .05f, 4f),
                    Mathf.Clamp(m_SizeMap.GetPixelBilinear(u + du, v + dv).r, .05f, 4f));
            }
            public float SizeMultiplier => Mathf.Max(0.01f, m_SizeMultiplier);
            public float WidthMultiplier => Mathf.Max(0.01f, m_WidthMultiplier);
            public float HeightMultiplier => Mathf.Max(0.01f, m_HeightMultiplier);
            public float MinWidth => m_MinWidth * SizeMultiplier * WidthMultiplier;
            public float MaxWidth => m_MaxWidth * SizeMultiplier * WidthMultiplier;
            public float MinHeight => m_MinHeight * SizeMultiplier * HeightMultiplier;
            public float MaxHeight => m_MaxHeight * SizeMultiplier * HeightMultiplier;
            public float YOffset => m_YOffset;
            public int Seed => m_Seed;
            public long RepresentedInstanceCount => m_RepresentedInstanceCount;
            public bool PaletteSourceOnly => m_PaletteSourceOnly;
            public MGDetailFoliagePalette GeneratedByPalette => m_GeneratedByPalette;
            public Texture2D PaletteSourceMap => m_PaletteSourceMap;
            public int PaletteEntryIndex => m_PaletteEntryIndex;

            internal void AddToRepresentedInstanceCount(long delta)
            {
                m_RepresentedInstanceCount = Math.Max(0L, m_RepresentedInstanceCount + delta);
            }

            internal DensityDetailLayer(int prototypeIndex, Texture2D densityMap, float minWidth, float maxWidth, float minHeight, float maxHeight, int seed, long representedInstanceCount, float yOffset = 0f)
            {
                m_PrototypeIndex = prototypeIndex;
                m_DensityMap = densityMap;
                m_MinWidth = minWidth;
                m_MaxWidth = maxWidth;
                m_MinHeight = minHeight;
                m_MaxHeight = maxHeight;
                m_YOffset = yOffset;
                m_Seed = seed;
                m_RepresentedInstanceCount = representedInstanceCount;
            }

            internal void SetPaletteSourceOnly(bool value)
            {
                m_PaletteSourceOnly = value;
            }

            internal void SetPaletteBakeIdentity(MGDetailFoliagePalette palette, Texture2D sourceMap, int entryIndex)
            {
                m_GeneratedByPalette = palette;
                m_PaletteSourceMap = sourceMap;
                m_PaletteEntryIndex = entryIndex;
            }
        }

        [Serializable]
        public sealed class DetailFoliagePaletteBinding
        {
            [SerializeField] bool m_Enabled = true;
            [SerializeField] MGDetailFoliagePalette m_Palette;
            [SerializeField, Tooltip("Paint this compact R16 density map. Baking distributes it into the palette's generated detail layers.")]
            Texture2D m_SourceDensityMap;
            [SerializeField] int m_SeedOffset;
            [SerializeField, HideInInspector] bool m_NeedsBake = true;

            public bool Enabled => m_Enabled;
            public MGDetailFoliagePalette Palette => m_Palette;
            public Texture2D SourceDensityMap => m_SourceDensityMap;
            public int SeedOffset => m_SeedOffset;
            public bool NeedsBake => m_NeedsBake;

            internal DetailFoliagePaletteBinding(MGDetailFoliagePalette palette, Texture2D sourceDensityMap = null)
            {
                m_Palette = palette;
                m_SourceDensityMap = sourceDensityMap;
                m_NeedsBake = true;
            }

            internal void MarkDirty() => m_NeedsBake = true;
            internal void MarkBaked() => m_NeedsBake = false;
            internal void RandomizeSeed() => m_SeedOffset = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            internal void SetSourceDensityMap(Texture2D sourceDensityMap)
            {
                if (m_SourceDensityMap == sourceDensityMap)
                    return;
                m_SourceDensityMap = sourceDensityMap;
                m_NeedsBake = true;
            }
        }

        readonly struct DetailChunkKey : IEquatable<DetailChunkKey>
        {
            readonly int m_Layer;
            readonly int m_X;
            readonly int m_Z;
            readonly ushort m_CellSize;
            readonly byte m_DensityLod;

            internal DetailChunkKey(int layer, int x, int z, int cellSize, int densityLod)
            {
                m_Layer = layer;
                m_X = x;
                m_Z = z;
                m_CellSize = (ushort)Mathf.Clamp(cellSize, 1, ushort.MaxValue);
                m_DensityLod = (byte)densityLod;
            }

            public bool Equals(DetailChunkKey other) =>
                m_Layer == other.m_Layer
                && m_X == other.m_X
                && m_Z == other.m_Z
                && m_CellSize == other.m_CellSize
                && m_DensityLod == other.m_DensityLod;
            internal bool OverlapsPaint(int layer, Rect rect, int width, int height) =>
                m_Layer == layer && new Rect(m_X / (float)width, m_Z / (float)height,
                    m_CellSize / (float)width, m_CellSize / (float)height).Overlaps(rect);
            public override bool Equals(object obj) => obj is DetailChunkKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return ((((m_Layer * 397) ^ m_X) * 397 ^ m_Z) * 397 ^ m_CellSize) * 397 ^ m_DensityLod; }
            }
        }

        readonly struct DetailLodStateKey : IEquatable<DetailLodStateKey>
        {
            readonly int m_Camera;
            readonly int m_Layer;
            readonly int m_X;
            readonly int m_Z;
            readonly ushort m_CellSize;

            internal DetailLodStateKey(int camera, int layer, int x, int z, int cellSize)
            {
                m_Camera = camera;
                m_Layer = layer;
                m_X = x;
                m_Z = z;
                m_CellSize = (ushort)Mathf.Clamp(cellSize, 1, ushort.MaxValue);
            }

            public bool Equals(DetailLodStateKey other) =>
                m_Camera == other.m_Camera
                && m_Layer == other.m_Layer
                && m_X == other.m_X
                && m_Z == other.m_Z
                && m_CellSize == other.m_CellSize;
            public override bool Equals(object obj) => obj is DetailLodStateKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return (((((m_Camera * 397) ^ m_Layer) * 397 ^ m_X) * 397 ^ m_Z) * 397) ^ m_CellSize; }
            }
        }

        sealed class DetailLodState
        {
            internal bool split;
            internal int densityLod;
            internal int lastUsedTick;
        }

        readonly struct DetailCandidateChunk
        {
            internal readonly int firstX;
            internal readonly int firstZ;
            internal readonly int cellSize;
            internal readonly int densityLod;
            internal readonly Bounds bounds;
            internal readonly float distance;
            internal readonly bool visible;

            internal DetailCandidateChunk(int firstX, int firstZ, int cellSize, int densityLod, Bounds bounds, float distance, bool visible = true)
            {
                this.firstX = firstX;
                this.firstZ = firstZ;
                this.cellSize = cellSize;
                this.densityLod = densityLod;
                this.bounds = bounds;
                this.distance = distance;
                this.visible = visible;
            }
        }

        sealed class DensityDetailChunk
        {
            internal Bounds worldBounds;
            internal int instanceCount;
            internal int densityLod;
            internal int layerIndex;
            internal bool gpuProcedural;
            internal readonly List<DensityDetailSpawn> proceduralSpawns = new List<DensityDetailSpawn>();
            internal readonly List<DrawBatch> batches = new List<DrawBatch>();
            internal readonly List<CombinedDetailDraw> combinedDraws = new List<CombinedDetailDraw>();
            internal readonly List<PendingCombinedDetailDraw> pendingCombinedDraws = new List<PendingCombinedDetailDraw>();
            internal int lastUsedTick;
        }

        readonly struct VisibleDensityDetail
        {
            internal readonly DensityDetailChunk chunk;
            internal readonly Prototype prototype;
            internal readonly float distance;
            internal readonly int densityLod;

            internal VisibleDensityDetail(DensityDetailChunk chunk, Prototype prototype, float distance, int densityLod)
            {
                this.chunk = chunk;
                this.prototype = prototype;
                this.distance = distance;
                this.densityLod = densityLod;
            }
        }

        sealed class CombinedDetailDraw
        {
            internal Mesh mesh;
            internal Material material;
            internal Prototype prototype;
            internal int instanceStart;
            internal int instanceCount;
            internal long estimatedBytes;
        }

        sealed class PendingCombinedDetailDraw
        {
            internal Task<CombinedMeshData> task;
            internal Material material;
            internal Prototype prototype;
            internal int instanceStart;
            internal int instanceCount;
            internal long estimatedBytes;
            internal bool abandoned;
        }

        readonly struct CombinedDetailSourceKey : IEquatable<CombinedDetailSourceKey>
        {
            readonly Mesh m_Mesh;
            readonly int m_SubMesh;

            internal CombinedDetailSourceKey(Mesh mesh, int subMesh)
            {
                m_Mesh = mesh;
                m_SubMesh = subMesh;
            }

            public bool Equals(CombinedDetailSourceKey other) => m_Mesh == other.m_Mesh && m_SubMesh == other.m_SubMesh;
            public override bool Equals(object obj) => obj is CombinedDetailSourceKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return ((m_Mesh != null ? m_Mesh.GetHashCode() : 0) * 397) ^ m_SubMesh; }
            }
        }

        sealed class CombinedMeshSource
        {
            internal Vector3[] vertices;
            internal Vector3[] normals;
            internal Vector4[] tangents;
            internal Vector2[] uv;
            internal Color32[] colors;
            internal int[] indices;
            internal MeshTopology topology;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct CombinedVertex
        {
            internal Vector3 position;
            internal Vector3 normal;
            internal Vector4 tangent;
            internal Color32 color;
            internal Vector2 uv;
        }

        sealed class CombinedMeshData
        {
            internal CombinedVertex[] vertices;
            internal int[] indices;
            internal MeshTopology topology;
            internal Bounds bounds;
        }

        sealed class DenseDetailPrototypeParts
        {
            internal readonly List<RenderPart> parts = new List<RenderPart>();
            internal readonly List<Mesh> generatedMeshes = new List<Mesh>();
            internal int sourcePartCount;
        }

        readonly struct DenseDetailBatchKey : IEquatable<DenseDetailBatchKey>
        {
            readonly Mesh m_Mesh;
            readonly int m_SubMesh;
            readonly Material m_Material;
            readonly Prototype m_Prototype;
            readonly ShadowCastingMode m_ShadowCasting;
            readonly bool m_ReceiveShadows;
            readonly Matrix4x4 m_PartTransform;

            internal DenseDetailBatchKey(DrawBatch batch, ShadowCastingMode shadowCasting, bool sharePrototypes = false, Matrix4x4 partTransform = default)
            {
                m_Mesh = batch.mesh;
                m_SubMesh = batch.subMesh;
                m_Material = batch.material;
                m_Prototype = sharePrototypes ? null : batch.prototype;
                m_ReceiveShadows = batch.prototype.ReceiveShadows;
                m_PartTransform = partTransform;
                m_ShadowCasting = shadowCasting;
            }

            public bool Equals(DenseDetailBatchKey other) =>
                m_Mesh == other.m_Mesh
                && m_SubMesh == other.m_SubMesh
                && m_Material == other.m_Material
                && ReferenceEquals(m_Prototype, other.m_Prototype)
                && m_ReceiveShadows == other.m_ReceiveShadows
                && m_PartTransform.Equals(other.m_PartTransform)
                && m_ShadowCasting == other.m_ShadowCasting;

            public override bool Equals(object obj) =>
                obj is DenseDetailBatchKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = m_Mesh != null ? m_Mesh.GetHashCode() : 0;
                    hash = (hash * 397) ^ m_SubMesh;
                    hash = (hash * 397) ^ (m_Material != null ? m_Material.GetHashCode() : 0);
                    hash = (hash * 397) ^ (m_Prototype != null ? m_Prototype.GetHashCode() : 0);
                    hash = (hash * 397) ^ m_ReceiveShadows.GetHashCode();
                    hash = (hash * 397) ^ m_PartTransform.GetHashCode();
                    return (hash * 397) ^ (int)m_ShadowCasting;
                }
            }
        }

        sealed class DenseDetailBatchAccumulator
        {
            internal DrawBatch batch;
            internal ShadowCastingMode shadowCasting;
            internal readonly Matrix4x4[] matrices = new Matrix4x4[1023];
            internal int count;
            internal bool active;
        }

        [SerializeField] List<DensityDetailLayer> m_DensityDetailLayers = new List<DensityDetailLayer>();
        [SerializeField] List<DetailFoliagePaletteBinding> m_DetailFoliagePalettes = new List<DetailFoliagePaletteBinding>();
        [SerializeField, Range(8, 64)] int m_DetailChunkCells = 64;
        [SerializeField, Range(0f, 1f)] float m_OverallDetailDensity = 1f;
        [SerializeField, Range(32, 2048)] int m_MaxCachedDetailChunks = 128;
        [SerializeField, Range(1, 64)] int m_MaxDetailChunksBuiltPerLayerPerFrame = 2;
        [SerializeField, Tooltip("Reduce the number of generated detail instances in cells farther from the camera.")]
        bool m_UseDetailDensityLod = true;
        [SerializeField, Min(0f), Tooltip("Cells at or inside this distance keep 100% of their generated detail density.")]
        float m_FullDetailDensityDistance = 35f;
        [SerializeField, Min(0f), Tooltip("Cells between the full-density distance and this distance use the mid-density percentage. Cells beyond it use the far-density percentage.")]
        float m_MidDetailDensityDistance = 100f;
        [SerializeField, Range(0.01f, 1f)] float m_MidDetailDensity = 0.5f;
        [SerializeField, Range(0.01f, 1f)] float m_FarDetailDensity = 0.15f;
        [SerializeField, Range(0f, 50f), Tooltip("Extra distance a detail cell must travel past a density boundary before changing LOD. Prevents cells flickering when the camera hovers near a boundary.")]
        float m_DetailDensityLodHysteresis = 8f;
        [SerializeField, Tooltip("Draw active density-detail HLOD cells and their distance/hysteresis boundaries while this terrain is selected.")]
        bool m_DebugDrawDensityDetailCells;
        [SerializeField, Min(0f), Tooltip("Global draw-distance ceiling for density-map details. Individual prototype distances can still be shorter. Set to 0 to use only each prototype's distance.")]
        float m_MaxDensityDetailDistance = 250f;
        [SerializeField, Min(0), Tooltip("Maximum density-map detail instances submitted by this terrain to one camera. Nearest chunks win. Set to 0 for unlimited.")]
        int m_MaxVisibleDenseDetailInstances = 50000;
        [SerializeField, Range(0f, 0.75f), Tooltip("Fraction of the visible instance budget protected for middle/far HLOD cells. Prevents a dense foreground from starving all distance grass. Set to 0 for strict near-first allocation.")]
        float m_DistantDetailBudgetReserve = 0.25f;
        [SerializeField, Tooltip("Use Unity's GPU-resident BatchRendererGroup backend for dense details in Play Mode. This removes the 1,023-instance submission limit while preserving Shader Graph object transforms. Falls back automatically if unavailable.")]
        bool m_UseBatchRendererGroup = true;
        [SerializeField, Tooltip("Generate visible detail transforms directly on the GPU from compact per-cell density spans. No per-blade CPU matrices or combined cell meshes are created.")]
        bool m_UseGpuProceduralDetailGeneration = true;
        [SerializeField, Tooltip("Build every fixed detail cell inside the first camera's detail radius on its first render. This moves generation cost to map startup so camera movement only enables and disables cached cells.")]
        bool m_PrewarmFixedDetailCells = true;
        [SerializeField, Tooltip("Keep fixed GPU-resident detail cells after they are built. Revisiting an area will not regenerate grass, at the cost of memory growing as the camera explores the terrain.")]
        bool m_RetainFixedDetailCells = true;
        [SerializeField, Min(0.1f), Tooltip("Distance the main camera must move before MG Terrain refreshes its settled GPU-resident visible-cell set.")]
        float m_DetailStreamingRefreshDistance = 2f;
        [SerializeField, Range(0.25f, 45f), Tooltip("Angle the main camera must rotate before MG Terrain refreshes its settled GPU-resident visible-cell set.")]
        float m_DetailStreamingRefreshAngle = 3f;
        [SerializeField] bool m_CombineDenseDetailMeshes = true;
        [SerializeField] bool m_DenseDetailShadows;
        [SerializeField, Min(65535)] int m_MaxCombinedDetailVerticesPerChunk = 600000;
        [SerializeField, Range(32768, 262144)] int m_MaxDetailVerticesPerUpload = 131072;
        [SerializeField, Range(1, 8)] int m_MaxDetailMeshUploadsPerFrame = 1;
        [SerializeField, Range(1, 16)] int m_MaxPendingDetailBuilds = 4;
        [SerializeField, Min(2)] int m_SurfaceGridWidth = 2;
        [SerializeField, Min(2)] int m_SurfaceGridHeight = 2;
        [SerializeField, HideInInspector] int m_DetailSettingsVersion;

        [NonSerialized] readonly Dictionary<DetailChunkKey, DensityDetailChunk> m_DensityDetailCache = new Dictionary<DetailChunkKey, DensityDetailChunk>();
        [NonSerialized] bool m_DetailRenderCacheDirty = true;
        [NonSerialized] int m_DetailRenderTick;
        [NonSerialized] int m_DetailUploadBudgetFrame = -1;
        [NonSerialized] int m_RemainingDetailMeshUploads;
        [NonSerialized] Mesh m_CachedDetailSurfaceMesh;
        [NonSerialized] Vector3[] m_CachedDetailSurfaceVertices;
        [NonSerialized] readonly Dictionary<CombinedDetailSourceKey, CombinedMeshSource> m_CombinedDetailSources = new Dictionary<CombinedDetailSourceKey, CombinedMeshSource>();
        [NonSerialized] readonly Dictionary<Prototype, DenseDetailPrototypeParts> m_DenseDetailPrototypeParts = new Dictionary<Prototype, DenseDetailPrototypeParts>();
        [NonSerialized] readonly Dictionary<DenseDetailBatchKey, DenseDetailBatchAccumulator> m_DenseDetailBatchAccumulators = new Dictionary<DenseDetailBatchKey, DenseDetailBatchAccumulator>();
        [NonSerialized] readonly List<DenseDetailBatchAccumulator> m_ActiveDenseDetailBatchAccumulators = new List<DenseDetailBatchAccumulator>();
        [NonSerialized] readonly List<DetailCandidateChunk> m_DetailCandidateChunks = new List<DetailCandidateChunk>();
        [NonSerialized] readonly Dictionary<DetailLodStateKey, DetailLodState> m_DetailLodStates = new Dictionary<DetailLodStateKey, DetailLodState>();
        [NonSerialized] readonly List<VisibleDensityDetail> m_VisibleDensityDetails = new List<VisibleDensityDetail>();
        [NonSerialized] readonly HashSet<DensityDetailChunk> m_VisibleDensityDetailSet = new HashSet<DensityDetailChunk>();
        [NonSerialized] Vector3 m_LastDensityDetailCameraWorld;
        [NonSerialized] bool m_HasDensityDetailDebugCamera;
        [NonSerialized] long m_LastVisibleDensityDetailInstances;
        [NonSerialized] int m_LastSubmittedDensityDetailInstances;
        [NonSerialized] int m_LastDensityDetailDrawCalls;
        [NonSerialized] int m_LastDensityDetailSourceParts;
        [NonSerialized] int m_LastDensityDetailBatchedParts;
        [NonSerialized] bool m_DetailNeedsInitialPrewarm = true;
        [NonSerialized] bool m_DetailStreamingSettled;
        [NonSerialized] bool m_HasDetailStreamingCamera;
        [NonSerialized] int m_DetailStreamingCameraId;
        [NonSerialized] Camera m_DetailStreamingCamera;
        [NonSerialized] Vector3 m_DetailStreamingCameraPosition;
        [NonSerialized] Quaternion m_DetailStreamingCameraRotation;
        [NonSerialized] bool m_RuntimeDetailCameraLogged;

        public IReadOnlyList<DensityDetailLayer> DensityDetailLayers => m_DensityDetailLayers;
        public IReadOnlyList<DetailFoliagePaletteBinding> DetailFoliagePalettes => m_DetailFoliagePalettes;
        public int DensityDetailLayerCount => m_DensityDetailLayers.Count;
        public long LastVisibleDensityDetailInstances => m_LastVisibleDensityDetailInstances;
        public int LastSubmittedDensityDetailInstances => m_LastSubmittedDensityDetailInstances;
        public int LastDensityDetailDrawCalls => m_LastDensityDetailDrawCalls;
        public int LastDensityDetailSourceParts => m_LastDensityDetailSourceParts;
        public int LastDensityDetailBatchedParts => m_LastDensityDetailBatchedParts;

        public MemoryUsageSnapshot CaptureMemoryUsageSnapshot()
        {
            var usage = new MemoryUsageSnapshot
            {
                SurfaceMeshBytes = GetRuntimeMemorySize(MeshFilter != null ? MeshFilter.sharedMesh : null),
                ControlMapBytes = GetRuntimeMemorySize(m_ControlMap1)
                    + (m_ControlMap2 != m_ControlMap1 ? GetRuntimeMemorySize(m_ControlMap2) : 0L),
                SerializedInstanceBytes = m_Instances.Count * 48L,
                CachedDetailCellCount = m_DensityDetailCache.Count
            };

            for (int layerIndex = 0; layerIndex < m_DensityDetailLayers.Count; layerIndex++)
            {
                DensityDetailLayer layer = m_DensityDetailLayers[layerIndex];
                if (layer != null)
                    usage.DensityMapBytes += GetRuntimeMemorySize(layer.DensityMap) + GetRuntimeMemorySize(layer.SizeMap);
            }

            for (int batchIndex = 0; batchIndex < m_DrawBatches.Count; batchIndex++)
                usage.MatrixBufferBytes += EstimateMatrixBatchBytes(m_DrawBatches[batchIndex]);

            foreach (DensityDetailChunk chunk in m_DensityDetailCache.Values)
            {
                usage.ProceduralCellDataBytes += chunk.proceduralSpawns.Count * 28L;
                for (int drawIndex = 0; drawIndex < chunk.combinedDraws.Count; drawIndex++)
                {
                    CombinedDetailDraw draw = chunk.combinedDraws[drawIndex];
                    usage.StreamedCombinedMeshBytes += draw.estimatedBytes;
                    usage.CombinedMeshSliceCount++;
                }
                for (int batchIndex = 0; batchIndex < chunk.batches.Count; batchIndex++)
                    usage.MatrixBufferBytes += EstimateMatrixBatchBytes(chunk.batches[batchIndex]);
                for (int pendingIndex = 0; pendingIndex < chunk.pendingCombinedDraws.Count; pendingIndex++)
                {
                    usage.PendingBuildBytes += chunk.pendingCombinedDraws[pendingIndex].estimatedBytes;
                    usage.PendingBuildCount++;
                }
            }

            if (m_CachedDetailSurfaceVertices != null)
                usage.CpuSourceCacheBytes += m_CachedDetailSurfaceVertices.LongLength * 12L;
            foreach (CombinedMeshSource source in m_CombinedDetailSources.Values)
            {
                usage.CpuSourceCacheBytes += EstimateArrayBytes(source.vertices, 12);
                usage.CpuSourceCacheBytes += EstimateArrayBytes(source.normals, 12);
                usage.CpuSourceCacheBytes += EstimateArrayBytes(source.tangents, 16);
                usage.CpuSourceCacheBytes += EstimateArrayBytes(source.uv, 8);
                usage.CpuSourceCacheBytes += EstimateArrayBytes(source.colors, 4);
                usage.CpuSourceCacheBytes += EstimateArrayBytes(source.indices, 4);
            }

            foreach (DenseDetailPrototypeParts prototypeParts in m_DenseDetailPrototypeParts.Values)
                for (int meshIndex = 0; meshIndex < prototypeParts.generatedMeshes.Count; meshIndex++)
                    usage.StreamedCombinedMeshBytes += GetRuntimeMemorySize(prototypeParts.generatedMeshes[meshIndex]);

            foreach (Material material in m_InstancedMaterials.Values)
                usage.RuntimeMaterialBytes += GetRuntimeMemorySize(material);
            usage.MatrixBufferBytes += GetDensityDetailBrgMemoryBytes();
            return usage;
        }

        static long EstimateMatrixBatchBytes(DrawBatch batch)
        {
            if (batch == null)
                return 0L;
            long bytes = 0L;
            for (int chunkIndex = 0; chunkIndex < batch.matrixChunks.Count; chunkIndex++)
            {
                Matrix4x4[] matrices = batch.matrixChunks[chunkIndex];
                if (matrices != null)
                    bytes += matrices.LongLength * 64L;
            }
            return bytes;
        }

        static long EstimateArrayBytes(Array array, int elementBytes) =>
            array != null ? array.LongLength * elementBytes : 0L;

        static long GetRuntimeMemorySize(UnityEngine.Object value) =>
            value != null ? Math.Max(0L, Profiler.GetRuntimeMemorySizeLong(value)) : 0L;

        public long RepresentedDensityDetailCount
        {
            get
            {
                long count = 0;
                for (int index = 0; index < m_DensityDetailLayers.Count; index++)
                    if (m_DensityDetailLayers[index] != null && !m_DensityDetailLayers[index].PaletteSourceOnly)
                        count += m_DensityDetailLayers[index].RepresentedInstanceCount;
                return count;
            }
        }

        /// <summary>
        /// Applies one of the supported runtime density-detail quality presets.
        /// </summary>
        public void ApplyDetailQualityPreset(DetailQualityPreset preset)
        {
            m_UseDetailDensityLod = true;
            m_UseBatchRendererGroup = true;
            m_DebugDrawDensityDetailCells = false;

            switch (preset)
            {
                case DetailQualityPreset.Low:
                case DetailQualityPreset.Medium:
                case DetailQualityPreset.High:
                default:
                    SetDetailQualityValues(29, 65536, 249, 2, 30f, 90f, 0.5f, 0.25f, 3.8f, 300000, 0.223f, true, 1, 4);
                    m_OverallDetailDensity = preset == DetailQualityPreset.Low ? 0.15f
                        : preset == DetailQualityPreset.Medium ? 0.25f
                        : preset == DetailQualityPreset.High ? 0.5f : 1f;
                    m_UseGpuProceduralDetailGeneration = true;
                    m_PrewarmFixedDetailCells = true;
                    m_RetainFixedDetailCells = true;
                    m_DetailStreamingRefreshDistance = 2f;
                    m_DetailStreamingRefreshAngle = 3f;
                    m_CombineDenseDetailMeshes = true;
                    m_MaxCombinedDetailVerticesPerChunk = 1000000;
                    m_MaxDetailVerticesPerUpload = 131072;
                    break;
            }

            InvalidateRenderCache();
        }

        void SetDetailQualityValues(
            int nearCellSize,
            int maxInstancesPerCell,
            int cachedCells,
            int buildsPerFrame,
            float fullDensityDistance,
            float midDensityEnd,
            float midDensity,
            float farDensity,
            float hysteresis,
            int visibleBudget,
            float distantBudgetReserve,
            bool detailShadows,
            int uploadsPerFrame,
            int pendingBuilds)
        {
            // Render distance is user-controlled, independent of quality presets.
            m_DetailChunkCells = nearCellSize;
            m_MaxCachedDetailChunks = cachedCells;
            m_MaxDetailChunksBuiltPerLayerPerFrame = buildsPerFrame;
            m_FullDetailDensityDistance = fullDensityDistance;
            m_MidDetailDensityDistance = midDensityEnd;
            m_MidDetailDensity = midDensity;
            m_FarDetailDensity = farDensity;
            m_DetailDensityLodHysteresis = hysteresis;
            m_MaxVisibleDenseDetailInstances = visibleBudget;
            m_DistantDetailBudgetReserve = distantBudgetReserve;
            m_DenseDetailShadows = detailShadows;
            m_MaxDetailMeshUploadsPerFrame = uploadsPerFrame;
            m_MaxPendingDetailBuilds = pendingBuilds;
        }

        void InitializeDetailSettingsIfNeeded()
        {
            if (m_DetailSettingsVersion < 1)
            {
                m_DetailChunkCells = 64;
                m_MaxCachedDetailChunks = 128;
                m_MaxDetailChunksBuiltPerLayerPerFrame = 2;
                m_CombineDenseDetailMeshes = true;
                m_MaxCombinedDetailVerticesPerChunk = 600000;
                m_MaxDetailMeshUploadsPerFrame = 1;
                m_MaxPendingDetailBuilds = 4;
                for (int layerIndex = 0; layerIndex < m_DensityDetailLayers.Count; layerIndex++)
                {
                    DensityDetailLayer layer = m_DensityDetailLayers[layerIndex];
                    if (layer != null && (uint)layer.PrototypeIndex < m_Prototypes.Count && m_Prototypes[layer.PrototypeIndex] != null)
                        m_Prototypes[layer.PrototypeIndex].ConfigureAsDenseDetail();
                }
                m_DetailSettingsVersion = 1;
            }

            if (m_DetailSettingsVersion < 2)
            {
                // Large one-shot mesh uploads show up as long CopyChannels stalls.
                // Keep the combined renderer, but stream its packed vertex data in
                // bounded slices so camera movement cannot enqueue a 600k-vertex copy.
                m_MaxDetailVerticesPerUpload = 131072;
                m_DetailSettingsVersion = 2;
            }

            if (m_DetailSettingsVersion < 3)
            {
                m_UseDetailDensityLod = true;
                m_FullDetailDensityDistance = 35f;
                m_MidDetailDensityDistance = 100f;
                m_MidDetailDensity = 0.5f;
                m_FarDetailDensity = 0.15f;
                m_DetailSettingsVersion = 3;
            }

            if (m_DetailSettingsVersion < 4)
            {
                // A hard near-first submission ceiling prevents a very large
                // authored density map from turning a wide camera view into
                // millions of alpha-tested grass draws on the GPU.
                m_MaxVisibleDenseDetailInstances = 200000;
                m_DetailSettingsVersion = 4;
            }

            if (m_DetailSettingsVersion < 5)
            {
                // The original 200k ceiling still permits roughly 11.4 million
                // source vertices for the common 57-vertex grass clump before
                // HDRP's alpha/depth passes are counted. Fifty thousand keeps
                // the nearest four 64-cell chunks at full density and spends
                // only the remainder on the middle/far bands.
                m_MaxVisibleDenseDetailInstances = 50000;
                m_DetailSettingsVersion = 5;
            }

            if (m_DetailSettingsVersion < 6)
            {
                m_DetailDensityLodHysteresis = 8f;
                m_DetailSettingsVersion = 6;
            }

            if (m_DetailSettingsVersion < 7)
            {
                // Density is now normalized independently from streaming-cell
                // size. Use fine leaves for near-field culling; the existing
                // distance-density bands provide the hierarchical LOD.
                m_DetailChunkCells = DetailDensityReferenceChunkCells;
                m_DetailSettingsVersion = 7;
            }

            if (m_DetailSettingsVersion < 8)
            {
                m_MaxDensityDetailDistance = Mathf.Max(0f, m_DefaultDetailDistance);
                m_DetailSettingsVersion = 8;
            }

            if (m_DetailSettingsVersion < 9)
            {
                // Preserve distance coverage when the near field alone exceeds
                // the visible budget. Without a reserve, middle/far cells are
                // assigned a zero scale and appear to have failed to stream.
                m_DistantDetailBudgetReserve = 0.25f;
                m_DetailSettingsVersion = 9;
            }

            if (m_DetailSettingsVersion < 10)
            {
                // GPU-resident details now use one reusable full-density leaf
                // cell per location. LOD only changes the submitted prefix, so
                // crossing a distance boundary never regenerates matrices.
                m_PrewarmFixedDetailCells = true;
                m_RetainFixedDetailCells = true;
                m_DetailStreamingRefreshDistance = 2f;
                m_DetailStreamingRefreshAngle = 3f;
                m_DetailSettingsVersion = 10;
            }

            if (m_DetailSettingsVersion < 11)
            {
                m_UseGpuProceduralDetailGeneration = true;
                m_DetailSettingsVersion = 11;
            }
        }

        public void ConfigureSurfaceGrid(int width, int height)
        {
            m_SurfaceGridWidth = Mathf.Max(2, width);
            m_SurfaceGridHeight = Mathf.Max(2, height);
            InvalidateRenderCache();
        }

        public void AddDensityDetailLayer(int prototypeIndex, Texture2D densityMap, float minWidth, float maxWidth, float minHeight, float maxHeight, int seed, long representedInstanceCount, float yOffset = 0f)
        {
            if ((uint)prototypeIndex >= m_Prototypes.Count || densityMap == null)
                return;
            m_DensityDetailLayers.Add(new DensityDetailLayer(
                prototypeIndex,
                densityMap,
                Mathf.Max(0.001f, minWidth),
                Mathf.Max(0.001f, maxWidth),
                Mathf.Max(0.001f, minHeight),
                Mathf.Max(0.001f, maxHeight),
                seed,
                Math.Max(0L, representedInstanceCount),
                yOffset));
            InvalidateDetailRenderCache();
        }

        public void AddGeneratedDensityDetailLayer(
            int prototypeIndex,
            Texture2D densityMap,
            float minWidth,
            float maxWidth,
            float minHeight,
            float maxHeight,
            int seed,
            long representedInstanceCount,
            float yOffset,
            MGDetailFoliagePalette palette,
            Texture2D sourceMap,
            int paletteEntryIndex)
        {
            if ((uint)prototypeIndex >= m_Prototypes.Count || densityMap == null || palette == null || sourceMap == null)
                return;
            var layer = new DensityDetailLayer(
                prototypeIndex,
                densityMap,
                Mathf.Max(0.001f, minWidth),
                Mathf.Max(0.001f, maxWidth),
                Mathf.Max(0.001f, minHeight),
                Mathf.Max(0.001f, maxHeight),
                seed,
                Math.Max(0L, representedInstanceCount),
                yOffset);
            layer.SetPaletteBakeIdentity(palette, sourceMap, paletteEntryIndex);
            m_DensityDetailLayers.Add(layer);
            InvalidateDetailRenderCache();
        }

        public void AddDetailFoliagePalette(MGDetailFoliagePalette palette, Texture2D sourceDensityMap = null)
        {
            if (palette == null)
                return;
            if (sourceDensityMap == null)
                TryGetAutomaticDetailFoliageSource(out sourceDensityMap, out _);
            m_DetailFoliagePalettes.Add(new DetailFoliagePaletteBinding(palette, sourceDensityMap));
        }

        /// <summary>
        /// Finds an unambiguous painted master density map. Generated palette outputs are never considered.
        /// Multiple layers that reference the same texture still count as one source choice.
        /// </summary>
        public bool TryGetAutomaticDetailFoliageSource(out Texture2D sourceDensityMap, out int uniqueSourceCount)
        {
            sourceDensityMap = null;
            uniqueSourceCount = 0;
            for (int index = 0; index < m_DensityDetailLayers.Count; index++)
            {
                DensityDetailLayer layer = m_DensityDetailLayers[index];
                Texture2D candidate = layer != null && layer.GeneratedByPalette == null ? layer.DensityMap : null;
                if (candidate == null)
                    continue;

                bool alreadyCounted = false;
                for (int previous = 0; previous < index; previous++)
                {
                    DensityDetailLayer previousLayer = m_DensityDetailLayers[previous];
                    if (previousLayer != null && previousLayer.GeneratedByPalette == null && previousLayer.DensityMap == candidate)
                    {
                        alreadyCounted = true;
                        break;
                    }
                }
                if (alreadyCounted)
                    continue;

                uniqueSourceCount++;
                if (uniqueSourceCount == 1)
                    sourceDensityMap = candidate;
                else
                    sourceDensityMap = null;
            }
            return uniqueSourceCount == 1 && sourceDensityMap != null;
        }

        public bool TryAssignAutomaticDetailFoliageSource(DetailFoliagePaletteBinding binding, out int uniqueSourceCount)
        {
            uniqueSourceCount = 0;
            if (binding == null)
                return false;
            if (binding.SourceDensityMap != null)
                return true;
            if (!TryGetAutomaticDetailFoliageSource(out Texture2D sourceDensityMap, out uniqueSourceCount))
                return false;
            binding.SetSourceDensityMap(sourceDensityMap);
            return true;
        }

        public bool TryGetDensityDetailPrototype(Texture2D densityMap, out Prototype prototype)
        {
            prototype = null;
            if (densityMap == null)
                return false;
            for (int index = 0; index < m_DensityDetailLayers.Count; index++)
            {
                DensityDetailLayer layer = m_DensityDetailLayers[index];
                if (layer == null || layer.GeneratedByPalette != null || layer.DensityMap != densityMap)
                    continue;
                if ((uint)layer.PrototypeIndex >= m_Prototypes.Count)
                    continue;
                prototype = m_Prototypes[layer.PrototypeIndex];
                if (prototype != null && prototype.Kind == InstanceKind.Detail)
                    return true;
            }
            prototype = null;
            return false;
        }

        public int CountGeneratedDensityDetailLayers(MGDetailFoliagePalette palette, Texture2D sourceMap)
        {
            int count = 0;
            for (int index = 0; index < m_DensityDetailLayers.Count; index++)
            {
                DensityDetailLayer layer = m_DensityDetailLayers[index];
                if (layer != null && layer.GeneratedByPalette == palette && layer.PaletteSourceMap == sourceMap)
                    count++;
            }
            return count;
        }

        public int RemoveGeneratedDensityDetailLayers(MGDetailFoliagePalette palette, Texture2D sourceMap)
        {
            int removed = 0;
            for (int index = m_DensityDetailLayers.Count - 1; index >= 0; index--)
            {
                DensityDetailLayer layer = m_DensityDetailLayers[index];
                if (layer == null || layer.GeneratedByPalette != palette || layer.PaletteSourceMap != sourceMap)
                    continue;
                m_DensityDetailLayers.RemoveAt(index);
                removed++;
            }
            if (removed > 0)
                InvalidateDetailRenderCache();
            return removed;
        }

        public bool SetDensityMapPaletteSourceOnly(Texture2D sourceMap, bool sourceOnly)
        {
            bool changed = false;
            for (int index = 0; index < m_DensityDetailLayers.Count; index++)
            {
                DensityDetailLayer layer = m_DensityDetailLayers[index];
                if (layer == null || layer.DensityMap != sourceMap || layer.GeneratedByPalette != null)
                    continue;
                if (layer.PaletteSourceOnly == sourceOnly)
                    continue;
                layer.SetPaletteSourceOnly(sourceOnly);
                changed = true;
            }
            if (changed)
                InvalidateDetailRenderCache();
            return changed;
        }

        public void MarkDetailFoliagePaletteBaked(DetailFoliagePaletteBinding binding)
        {
            binding?.MarkBaked();
        }

        public void RandomizeDetailFoliagePaletteSeed(DetailFoliagePaletteBinding binding)
        {
            if (binding == null)
                return;
            binding.RandomizeSeed();
            binding.MarkDirty();
        }

        void MarkDetailFoliagePaletteSourcesDirty(Texture2D sourceMap)
        {
            for (int index = 0; index < m_DetailFoliagePalettes.Count; index++)
            {
                DetailFoliagePaletteBinding binding = m_DetailFoliagePalettes[index];
                if (binding != null && binding.SourceDensityMap == sourceMap)
                    binding.MarkDirty();
            }
        }

        public void ClearDensityDetailLayers()
        {
            m_DensityDetailLayers.Clear();
            InvalidateDetailRenderCache();
        }

        public int PaintDensityDetailLayer(int layerIndex, Vector3 worldCenter, float worldRadius, int densityDelta, float falloffPower = 1f)
        {
            if ((uint)layerIndex >= m_DensityDetailLayers.Count || worldRadius <= 0f || densityDelta == 0)
                return 0;
            DensityDetailLayer layer = m_DensityDetailLayers[layerIndex];
            Texture2D densityMap = layer != null ? layer.DensityMap : null;
            Mesh mesh = MeshFilter != null ? MeshFilter.sharedMesh : null;
            if (densityMap == null || mesh == null || densityMap.format != TextureFormat.R16)
                return 0;

            Bounds bounds = mesh.bounds;
            Vector3 localCenter = transform.InverseTransformPoint(worldCenter);
            float localRadiusX = worldRadius / Mathf.Max(0.0001f, transform.TransformVector(Vector3.right).magnitude);
            float localRadiusZ = worldRadius / Mathf.Max(0.0001f, transform.TransformVector(Vector3.forward).magnitude);
            int minimumX = Mathf.Clamp(Mathf.FloorToInt(Mathf.InverseLerp(bounds.min.x, bounds.max.x, localCenter.x - localRadiusX) * densityMap.width), 0, densityMap.width - 1);
            int maximumX = Mathf.Clamp(Mathf.CeilToInt(Mathf.InverseLerp(bounds.min.x, bounds.max.x, localCenter.x + localRadiusX) * densityMap.width), 0, densityMap.width - 1);
            int minimumZ = Mathf.Clamp(Mathf.FloorToInt(Mathf.InverseLerp(bounds.min.z, bounds.max.z, localCenter.z - localRadiusZ) * densityMap.height), 0, densityMap.height - 1);
            int maximumZ = Mathf.Clamp(Mathf.CeilToInt(Mathf.InverseLerp(bounds.min.z, bounds.max.z, localCenter.z + localRadiusZ) * densityMap.height), 0, densityMap.height - 1);

            long totalDelta = 0;
            int changedCells = 0;
            try
            {
                var values = densityMap.GetRawTextureData<ushort>();
                for (int z = minimumZ; z <= maximumZ; z++)
                {
                    float normalizedZ = (z + 0.5f) / densityMap.height;
                    float localZ = Mathf.Lerp(bounds.min.z, bounds.max.z, normalizedZ);
                    for (int x = minimumX; x <= maximumX; x++)
                    {
                        float normalizedX = (x + 0.5f) / densityMap.width;
                        float localX = Mathf.Lerp(bounds.min.x, bounds.max.x, normalizedX);
                        Vector3 planarDelta = new Vector3(localX - localCenter.x, 0f, localZ - localCenter.z);
                        float distance = transform.TransformVector(planarDelta).magnitude;
                        if (distance >= worldRadius)
                            continue;
                        float influence = Mathf.Pow(1f - distance / worldRadius, Mathf.Max(0.01f, falloffPower));
                        int appliedDelta = Mathf.RoundToInt(densityDelta * influence);
                        if (appliedDelta == 0)
                            continue;
                        int valueIndex = z * densityMap.width + x;
                        int previous = values[valueIndex];
                        int next = Mathf.Clamp(previous + appliedDelta, 0, ushort.MaxValue);
                        if (next == previous)
                            continue;
                        values[valueIndex] = (ushort)next;
                        totalDelta += next - previous;
                        changedCells++;
                    }
                }
                if (changedCells > 0)
                {
                    densityMap.Apply(false, false);
                    layer.AddToRepresentedInstanceCount(totalDelta);
                    MarkDetailFoliagePaletteSourcesDirty(densityMap);
                    InvalidateDetailRenderCache();
                }
            }
            catch (UnityException)
            {
                return 0;
            }
            return changedCells;
        }

        [NonSerialized] readonly Dictionary<int, Rect> m_DetailPaintRegions = new Dictionary<int, Rect>();

        bool IsDetailPaintRegion(int layer, int x, int z, int cells, int width, int height) =>
            !Application.isPlaying && m_DetailPaintRegions.TryGetValue(layer, out Rect rect)
            && new Rect(x / (float)width, z / (float)height, cells / (float)width, cells / (float)height).Overlaps(rect);

        // Authoring preview: keep unaffected cells and avoid combined mesh upload queues.
        public void RefreshDetailPaintRegion(int layerIndex, Rect normalizedRegion, bool recountDensity = false)
        {
            if ((uint)layerIndex >= m_DensityDetailLayers.Count) return;
            var layer = m_DensityDetailLayers[layerIndex];
            if (layer == null || layer.DensityMap == null) return;
            int width = layer.DensityMap.width, height = layer.DensityMap.height;
            if (recountDensity)
            {
                long total = 0;
                foreach (ushort value in layer.DensityMap.GetPixelData<ushort>(0)) total += value;
                layer.AddToRepresentedInstanceCount(total - layer.RepresentedInstanceCount);
                MarkDetailFoliagePaletteSourcesDirty(layer.DensityMap);
            }
            if (normalizedRegion.width <= 0 || normalizedRegion.height <= 0) return;
            // Include neighboring samples used by bilinear size interpolation.
            normalizedRegion.xMin -= 1f / width; normalizedRegion.xMax += 1f / width;
            normalizedRegion.yMin -= 1f / height; normalizedRegion.yMax += 1f / height;
            if (m_DetailPaintRegions.TryGetValue(layerIndex, out Rect previous))
                m_DetailPaintRegions[layerIndex] = Rect.MinMaxRect(Mathf.Min(previous.xMin, normalizedRegion.xMin), Mathf.Min(previous.yMin, normalizedRegion.yMin), Mathf.Max(previous.xMax, normalizedRegion.xMax), Mathf.Max(previous.yMax, normalizedRegion.yMax));
            else m_DetailPaintRegions[layerIndex] = normalizedRegion;
            var remove = new List<DetailChunkKey>();
            foreach (var pair in m_DensityDetailCache)
                if (pair.Key.OverlapsPaint(layerIndex, normalizedRegion, width, height)) remove.Add(pair.Key);
            foreach (var key in remove)
            {
                DestroyDensityDetailChunk(m_DensityDetailCache[key]);
                m_DensityDetailCache.Remove(key);
            }
            m_DetailStreamingSettled = false;
            m_HasDetailStreamingCamera = false;
        }

        void InvalidateDetailRenderCache()
        {
            m_DetailRenderCacheDirty = true;
            m_DetailStreamingSettled = false;
            m_HasDetailStreamingCamera = false;
            m_DetailStreamingCamera = null;
            m_DetailNeedsInitialPrewarm = true;
        }

        void ReleaseDetailRenderCache()
        {
            ReleaseDensityDetailBrg();
            foreach (DensityDetailChunk chunk in m_DensityDetailCache.Values)
                DestroyDensityDetailChunk(chunk);
            m_DensityDetailCache.Clear();
            m_CachedDetailSurfaceMesh = null;
            m_CachedDetailSurfaceVertices = null;
            m_CombinedDetailSources.Clear();
            foreach (DenseDetailPrototypeParts prototypeParts in m_DenseDetailPrototypeParts.Values)
            {
                for (int meshIndex = 0; meshIndex < prototypeParts.generatedMeshes.Count; meshIndex++)
                {
                    Mesh mesh = prototypeParts.generatedMeshes[meshIndex];
                    if (mesh == null)
                        continue;
                    if (Application.isPlaying)
                        Destroy(mesh);
                    else
                        DestroyImmediate(mesh);
                }
            }
            m_DenseDetailPrototypeParts.Clear();
            m_DenseDetailBatchAccumulators.Clear();
            m_ActiveDenseDetailBatchAccumulators.Clear();
            m_DetailLodStates.Clear();
            m_DetailStreamingSettled = false;
            m_HasDetailStreamingCamera = false;
            m_DetailStreamingCamera = null;
            m_DetailNeedsInitialPrewarm = true;
            m_DetailRenderCacheDirty = false;
        }

        void RenderDensityDetails(Camera camera, Plane[] planes)
        {
            using var profile = s_DetailSelectionMarker.Auto();
            if (m_AppearanceCaptureCamera != null)
            {
                AppearanceCaptureTileComplete = false;
                Array.Clear(m_AppearanceCaptureLayerSubmissions, 0, m_AppearanceCaptureLayerSubmissions.Length);
            }
            if (m_DensityDetailLayers.Count == 0 || MeshFilter == null || MeshFilter.sharedMesh == null)
            {
                if (m_AppearanceCaptureCamera != null) AppearanceCaptureTileComplete = true;
                return;
            }
            bool useFixedResidentCells = Application.isPlaying && m_UseBatchRendererGroup;
            if (useFixedResidentCells && !IsDensityDetailStreamingCamera(camera))
                return;
            if (useFixedResidentCells && !m_RuntimeDetailCameraLogged)
            {
                Camera mainCamera = Camera.main;
                string targetName = camera.targetTexture != null ? camera.targetTexture.name : "backbuffer";
                Debug.Log(
                    $"[MG Terrain Runtime] '{name}' selected camera '{camera.name}' "
                    + $"(main='{(mainCamera != null ? mainCamera.name : "none")}', target='{targetName}', "
                    + $"display={camera.targetDisplay}, layer={gameObject.layer}) for GPU-resident details. "
                    + $"Density layers={m_DensityDetailLayers.Count}, represented={RepresentedDensityDetailCount:N0}.",
                    this);
                m_RuntimeDetailCameraLogged = true;
            }
            if (useFixedResidentCells
                && !m_DetailRenderCacheDirty
                && CanReuseDensityDetailStreamingSet(camera))
            {
                return;
            }
            if (m_DetailRenderCacheDirty)
                ReleaseDetailRenderCache();

            m_DetailRenderTick++;
            Bounds surfaceBounds = MeshFilter.sharedMesh.bounds;
            if (surfaceBounds.size.x <= Mathf.Epsilon || surfaceBounds.size.z <= Mathf.Epsilon)
                return;
            Vector3 cameraWorld = camera.transform.position;
            if (m_DebugDrawDensityDetailCells)
            {
                m_LastDensityDetailCameraWorld = cameraWorld;
                m_HasDensityDetailDebugCamera = true;
            }
            float worldPerLocalX = Mathf.Max(0.0001f, transform.TransformVector(Vector3.right).magnitude);
            float worldPerLocalZ = Mathf.Max(0.0001f, transform.TransformVector(Vector3.forward).magnitude);
            int renderedFrame = Time.renderedFrameCount;
            if (m_DetailUploadBudgetFrame != renderedFrame)
            {
                m_DetailUploadBudgetFrame = renderedFrame;
                m_RemainingDetailMeshUploads = Mathf.Max(1, m_MaxDetailMeshUploadsPerFrame);
            }
            int pendingBuilds = CountPendingDetailBuilds();
            m_AppearanceCapturePopulation = 0;
            bool prewarmThisPass = m_AppearanceCaptureCamera != null || useFixedResidentCells
                && m_PrewarmFixedDetailCells
                && m_DetailNeedsInitialPrewarm;
            bool allCandidateCellsReady = true;
            m_VisibleDensityDetails.Clear();
            m_VisibleDensityDetailSet.Clear();
            m_LastVisibleDensityDetailInstances = 0;
            m_LastSubmittedDensityDetailInstances = 0;
            m_LastDensityDetailDrawCalls = 0;
            m_LastDensityDetailSourceParts = 0;
            m_LastDensityDetailBatchedParts = 0;
            ResetDenseDetailBatchAccumulators();

            for (int layerIndex = 0; layerIndex < m_DensityDetailLayers.Count; layerIndex++)
            {
                DensityDetailLayer layer = m_DensityDetailLayers[layerIndex];
                if (layer == null || layer.PaletteSourceOnly || layer.DensityMap == null || (uint)layer.PrototypeIndex >= m_Prototypes.Count)
                    continue;
                Prototype prototype = m_Prototypes[layer.PrototypeIndex];
                if (prototype == null)
                    continue;

                int width = layer.DensityMap.width;
                int height = layer.DensityMap.height;
                int leafCellSize = Mathf.Clamp(m_DetailChunkCells, 8, 64);
                if (m_AppearanceCaptureCamera != null)
                {
                    float metresPerTexel = Mathf.Max(surfaceBounds.size.x * worldPerLocalX / width, surfaceBounds.size.z * worldPerLocalZ / height);
                    leafCellSize = Mathf.Clamp(Mathf.FloorToInt(32f / Mathf.Max(.0001f, metresPerTexel)), 1, leafCellSize);
                }
                float fallbackDistance = Mathf.Max(
                    surfaceBounds.size.x * worldPerLocalX,
                    surfaceBounds.size.z * worldPerLocalZ);
                float maximumDistance = prototype.MaximumDrawDistance > 0f
                    ? prototype.MaximumDrawDistance
                    : fallbackDistance;
                if (m_MaxDensityDetailDistance > 0f)
                    maximumDistance = Mathf.Min(maximumDistance, m_MaxDensityDetailDistance);
                if (m_AppearanceCaptureCamera != null) maximumDistance = float.MaxValue;
                List<DetailCandidateChunk> candidateChunks = m_DetailCandidateChunks;
                if (useFixedResidentCells)
                {
                    BuildFixedDetailCellCandidates(
                        camera,
                        planes,
                        cameraWorld,
                        layerIndex,
                        layer,
                        surfaceBounds,
                        width,
                        height,
                        leafCellSize,
                        maximumDistance,
                        candidateChunks);
                }
                else
                {
                    BuildDetailHierarchyCandidates(
                        camera,
                        planes,
                        cameraWorld,
                        layerIndex,
                        layer,
                        surfaceBounds,
                        width,
                        height,
                        leafCellSize,
                        maximumDistance,
                        candidateChunks);
                }
                candidateChunks.Sort((left, right) =>
                {
                    bool lp = IsDetailPaintRegion(layerIndex, left.firstX, left.firstZ, left.cellSize, width, height);
                    bool rp = IsDetailPaintRegion(layerIndex, right.firstX, right.firstZ, right.cellSize, width, height);
                    return lp != rp ? (lp ? -1 : 1) : left.distance.CompareTo(right.distance);
                });

                int builtThisFrame = 0;
                int buildLimit = prewarmThisPass
                    ? int.MaxValue
                    : Mathf.Max(1, m_MaxDetailChunksBuiltPerLayerPerFrame);
                for (int candidateIndex = 0; candidateIndex < candidateChunks.Count; candidateIndex++)
                {
                        DetailCandidateChunk candidate = candidateChunks[candidateIndex];
                        int cachedDensityLod = useFixedResidentCells ? 0 : candidate.densityLod;
                        var key = new DetailChunkKey(
                            layerIndex,
                            candidate.firstX,
                            candidate.firstZ,
                            candidate.cellSize,
                            cachedDensityLod);
                        m_DensityDetailCache.TryGetValue(key, out DensityDetailChunk chunk);
                        if (chunk == null
                            && builtThisFrame < buildLimit
                            && (prewarmThisPass
                                || IsDetailPaintRegion(layerIndex, candidate.firstX, candidate.firstZ, candidate.cellSize, width, height)
                                || pendingBuilds < Mathf.Max(1, m_MaxPendingDetailBuilds)))
                        {
                            chunk = BuildDensityDetailChunk(
                                layerIndex,
                                layer,
                                prototype,
                                surfaceBounds,
                                candidate.cellSize,
                                candidate.firstX,
                                candidate.firstZ,
                                cachedDensityLod,
                                useFixedResidentCells ? 1f : GetDetailDensityScale(candidate.densityLod));
                            m_DensityDetailCache.Add(key, chunk);
                            pendingBuilds += chunk.pendingCombinedDraws.Count;
                            builtThisFrame++;
                        }

                        if (chunk == null || !IsDetailChunkFullyReady(chunk))
                            allCandidateCellsReady = false;

                        if (chunk != null)
                        {
                            if (useFixedResidentCells)
                                chunk.densityLod = candidate.densityLod;
                            chunk.lastUsedTick = m_DetailRenderTick;
                            FinalizeCompletedDetailMeshes(chunk, ref m_RemainingDetailMeshUploads);
                        }

                        // Fixed resident cells are prebuilt in a camera-centered
                        // radius, including cells outside the current frustum.
                        // Only the visible subset is submitted to BRG.
                        if (useFixedResidentCells && !candidate.visible)
                            continue;

                        // Do not switch away from the old density until every
                        // slice of the requested cell has reached the GPU.
                        DensityDetailChunk drawChunk;
                        if (useFixedResidentCells)
                        {
                            drawChunk = IsDetailChunkFullyReady(chunk) ? chunk : null;
                        }
                        else
                        {
                            drawChunk = IsDetailChunkFullyReady(chunk)
                                ? chunk
                                : FindDetailHierarchyParentFallback(
                                    layerIndex,
                                    candidate.firstX,
                                    candidate.firstZ,
                                    candidate.cellSize,
                                    candidate.densityLod,
                                    leafCellSize);
                        }
                        if (drawChunk == null && HasRenderableDetailData(chunk))
                            drawChunk = chunk;
                        if (drawChunk == null)
                            continue;

                        drawChunk.lastUsedTick = m_DetailRenderTick;
                        if (m_VisibleDensityDetailSet.Add(drawChunk))
                        {
                            m_VisibleDensityDetails.Add(new VisibleDensityDetail(
                                drawChunk,
                                prototype,
                                candidate.distance,
                                useFixedResidentCells ? candidate.densityLod : drawChunk.densityLod));
                        }
                }
            }

            if (useFixedResidentCells)
            {
                m_DetailNeedsInitialPrewarm = false;
                m_DetailStreamingSettled = allCandidateCellsReady && CountPendingDetailBuilds() == 0;
                RememberDensityDetailStreamingCamera(camera);
            }
            DrawVisibleDensityDetails(camera);
            if (m_AppearanceCaptureCamera != null)
                AppearanceCaptureTileComplete = !AppearanceCaptureNeedsSubdivision && allCandidateCellsReady
                    && m_LastSubmittedDensityDetailInstances == m_LastVisibleDensityDetailInstances;
            PruneDetailChunkCache();
        }

        void DrawVisibleDensityDetails(Camera camera)
        {
            if (m_VisibleDensityDetails.Count == 0)
            {
                ClearDensityDetailBrgVisibility();
                return;
            }

            long nearInstances = 0;
            long distantInstances = 0;
            for (int index = 0; index < m_VisibleDensityDetails.Count; index++)
            {
                VisibleDensityDetail visible = m_VisibleDensityDetails[index];
                int lodInstanceCount = GetVisibleDensityDetailInstanceCount(visible);
                if (visible.densityLod == 0)
                    nearInstances += lodInstanceCount;
                else
                    distantInstances += lodInstanceCount;
            }

            long totalInstances = nearInstances + distantInstances;
            m_LastVisibleDensityDetailInstances = totalInstances;
            m_LastSubmittedDensityDetailInstances = 0;
            int budget = m_AppearanceCaptureCamera == null && m_MaxVisibleDenseDetailInstances > 0
                ? m_MaxVisibleDenseDetailInstances
                : int.MaxValue;
            float nearScale = 1f;
            float distantScale = 1f;
            if (totalInstances > budget)
            {
                long distantAllocation = distantInstances > 0
                    ? Math.Min(
                        distantInstances,
                        Mathf.RoundToInt(budget * Mathf.Clamp01(m_DistantDetailBudgetReserve)))
                    : 0L;
                long nearAllocation = Math.Min(nearInstances, Math.Max(0L, (long)budget - distantAllocation));
                long unallocated = Math.Max(0L, (long)budget - nearAllocation - distantAllocation);

                // If one band cannot consume its share, return the unused
                // instances to the other band instead of wasting budget.
                long extraNear = Math.Min(Math.Max(0L, nearInstances - nearAllocation), unallocated);
                nearAllocation += extraNear;
                unallocated -= extraNear;
                long extraDistant = Math.Min(Math.Max(0L, distantInstances - distantAllocation), unallocated);
                distantAllocation += extraDistant;

                nearScale = nearAllocation / (float)Math.Max(1L, nearInstances);
                distantScale = distantAllocation / (float)Math.Max(1L, distantInstances);
            }

            // Draw by stable LOD/distance priority, but share the remaining
            // budget proportionally between all middle/far cells. The old
            // first-come cutoff made entire cells alternately disappear when
            // two candidates exchanged sort order during camera movement.
            m_VisibleDensityDetails.Sort((left, right) =>
            {
                int lodComparison = left.densityLod.CompareTo(right.densityLod);
                if (lodComparison != 0)
                    return lodComparison;
                int xComparison = left.chunk.worldBounds.center.x.CompareTo(right.chunk.worldBounds.center.x);
                if (xComparison != 0)
                    return xComparison;
                return left.chunk.worldBounds.center.z.CompareTo(right.chunk.worldBounds.center.z);
            });

            if (TryPrepareDensityDetailBrg(camera, budget, nearScale, distantScale))
                return;

            int remaining = budget;
            for (int index = 0; index < m_VisibleDensityDetails.Count && remaining > 0; index++)
            {
                VisibleDensityDetail visible = m_VisibleDensityDetails[index];
                float scale = visible.densityLod == 0 ? nearScale : distantScale;
                int allowedInstances = Mathf.Min(
                    remaining,
                    Mathf.FloorToInt(GetVisibleDensityDetailInstanceCount(visible) * scale));
                if (allowedInstances <= 0)
                    continue;

                ShadowCastingMode shadowCasting = m_AppearanceCaptureCamera != null ? ShadowCastingMode.On : m_DenseDetailShadows
                    ? visible.prototype.ShadowCasting
                    : ShadowCastingMode.Off;
                int submittedInstances = 0;
                for (int drawIndex = 0; drawIndex < visible.chunk.combinedDraws.Count; drawIndex++)
                {
                    CombinedDetailDraw draw = visible.chunk.combinedDraws[drawIndex];
                    if (draw.instanceStart >= allowedInstances)
                        continue;
                    Graphics.DrawMesh(
                        draw.mesh,
                        transform.localToWorldMatrix,
                        draw.material,
                        gameObject.layer,
                        camera,
                        0,
                        null,
                        shadowCasting,
                        draw.prototype.ReceiveShadows,
                        null,
                        UnityEngine.Rendering.LightProbeUsage.Off,
                        null);
                    m_LastDensityDetailDrawCalls++;
                    submittedInstances = Mathf.Max(
                        submittedInstances,
                        draw.instanceStart + draw.instanceCount);
                }
                for (int batchIndex = 0; batchIndex < visible.chunk.batches.Count; batchIndex++)
                {
                    submittedInstances = Mathf.Max(
                        submittedInstances,
                        QueueDenseDetailBatch(
                            visible.chunk.batches[batchIndex],
                            camera,
                            allowedInstances,
                            shadowCasting));
                }
                submittedInstances = Mathf.Min(visible.chunk.instanceCount, submittedInstances);
                if (submittedInstances <= 0)
                    continue;
                remaining -= submittedInstances;
                m_LastSubmittedDensityDetailInstances += submittedInstances;
                if (m_AppearanceCaptureCamera != null)
                    m_AppearanceCaptureLayerSubmissions[visible.chunk.layerIndex] += submittedInstances;
            }
            FlushDenseDetailBatchAccumulators(camera);
        }

        int GetVisibleDensityDetailInstanceCount(VisibleDensityDetail visible)
        {
            if (!Application.isPlaying || !m_UseBatchRendererGroup)
                return visible.chunk.instanceCount;
            return Mathf.Clamp(
                Mathf.FloorToInt(visible.chunk.instanceCount * GetDetailDensityScale(visible.densityLod)),
                0,
                visible.chunk.instanceCount);
        }

        bool ShouldBuildGpuProceduralDetailCells()
        {
#if UNITY_6000_0_OR_NEWER
            return Application.isPlaying
                && m_UseBatchRendererGroup
                && m_UseGpuProceduralDetailGeneration
                && CanUseGpuGeneratedDensityDetailBrg();
#else
            return false;
#endif
        }

        void ResetDenseDetailBatchAccumulators()
        {
            for (int index = 0; index < m_ActiveDenseDetailBatchAccumulators.Count; index++)
            {
                DenseDetailBatchAccumulator accumulator = m_ActiveDenseDetailBatchAccumulators[index];
                accumulator.count = 0;
                accumulator.active = false;
            }
            m_ActiveDenseDetailBatchAccumulators.Clear();
        }

        int QueueDenseDetailBatch(
            DrawBatch batch,
            Camera camera,
            int maximumInstances,
            ShadowCastingMode shadowCasting)
        {
            if (batch == null || maximumInstances <= 0)
                return 0;

            if (!SystemInfo.supportsInstancing || batch.forceNonInstanced)
            {
                int drawn = DrawBatchInstances(batch, camera, maximumInstances, shadowCasting);
                m_LastDensityDetailDrawCalls += drawn;
                return drawn;
            }

            var key = new DenseDetailBatchKey(batch, shadowCasting);
            if (!m_DenseDetailBatchAccumulators.TryGetValue(key, out DenseDetailBatchAccumulator accumulator))
            {
                accumulator = new DenseDetailBatchAccumulator
                {
                    batch = batch,
                    shadowCasting = shadowCasting
                };
                m_DenseDetailBatchAccumulators.Add(key, accumulator);
            }
            if (!accumulator.active)
            {
                accumulator.active = true;
                m_ActiveDenseDetailBatchAccumulators.Add(accumulator);
            }

            int remaining = maximumInstances;
            int queued = 0;
            for (int chunkIndex = 0; chunkIndex < batch.matrixChunks.Count && remaining > 0; chunkIndex++)
            {
                Matrix4x4[] matrices = batch.matrixChunks[chunkIndex];
                if (matrices == null)
                    continue;
                int count = Mathf.Min(matrices.Length, remaining);
                int sourceIndex = 0;
                while (sourceIndex < count)
                {
                    int copyCount = Mathf.Min(1023 - accumulator.count, count - sourceIndex);
                    Array.Copy(matrices, sourceIndex, accumulator.matrices, accumulator.count, copyCount);
                    accumulator.count += copyCount;
                    sourceIndex += copyCount;
                    queued += copyCount;
                    if (accumulator.count == 1023)
                        SubmitDenseDetailBatch(accumulator, camera);
                }
                remaining -= count;
            }
            return queued;
        }

        void FlushDenseDetailBatchAccumulators(Camera camera)
        {
            for (int index = 0; index < m_ActiveDenseDetailBatchAccumulators.Count; index++)
            {
                DenseDetailBatchAccumulator accumulator = m_ActiveDenseDetailBatchAccumulators[index];
                if (accumulator.count > 0)
                    SubmitDenseDetailBatch(accumulator, camera);
                accumulator.active = false;
            }
            m_ActiveDenseDetailBatchAccumulators.Clear();
        }

        void SubmitDenseDetailBatch(DenseDetailBatchAccumulator accumulator, Camera camera)
        {
            if (accumulator.count <= 0)
                return;
            DrawBatch batch = accumulator.batch;
            try
            {
                Graphics.DrawMeshInstanced(
                    batch.mesh,
                    batch.subMesh,
                    batch.material,
                    accumulator.matrices,
                    accumulator.count,
                    null,
                    accumulator.shadowCasting,
                    batch.prototype.ReceiveShadows,
                    gameObject.layer,
                    camera,
                    batch.lightProbeUsage);
                m_LastDensityDetailDrawCalls++;
            }
            catch (InvalidOperationException)
            {
                batch.forceNonInstanced = true;
                for (int index = 0; index < accumulator.count; index++)
                {
                    Graphics.DrawMesh(
                        batch.mesh,
                        accumulator.matrices[index],
                        batch.material,
                        gameObject.layer,
                        camera,
                        batch.subMesh,
                        null,
                        accumulator.shadowCasting,
                        batch.prototype.ReceiveShadows,
                        null,
                        batch.lightProbeUsage,
                        null);
                }
                m_LastDensityDetailDrawCalls += accumulator.count;
            }
            accumulator.count = 0;
        }

        bool IsDensityDetailStreamingCamera(Camera camera)
        {
            if (!CanCameraRenderDensityDetails(camera))
                return false;

            Camera mainCamera = Camera.main;
            if (CanCameraRenderDensityDetails(mainCamera))
                return camera == mainCamera;

            // Some runtime camera stacks have no tagged MainCamera, replace it during
            // transitions, or render the final view through an intermediate texture.
            // Keep the first valid gameplay camera while it is alive, then allow a clean
            // takeover. A target texture is not a reason to reject a camera: HDRP and
            // Project X can legitimately render their gameplay view through one.
            if (m_DetailStreamingCamera != null && CanCameraRenderDensityDetails(m_DetailStreamingCamera))
                return camera == m_DetailStreamingCamera;

            return true;
        }

        bool CanCameraRenderDensityDetails(Camera camera)
        {
            if (camera == null || camera.cameraType != CameraType.Game || !camera.isActiveAndEnabled)
                return false;

            int terrainLayerMask = 1 << gameObject.layer;
            return (camera.cullingMask & terrainLayerMask) != 0;
        }

        bool CanReuseDensityDetailStreamingSet(Camera camera)
        {
            if (!m_DetailStreamingSettled || !IsDensityDetailBrgActive || !m_HasDetailStreamingCamera)
                return false;
            if (camera.GetInstanceID() != m_DetailStreamingCameraId)
                return false;

            float refreshDistance = Mathf.Max(0.1f, m_DetailStreamingRefreshDistance);
            if ((camera.transform.position - m_DetailStreamingCameraPosition).sqrMagnitude
                >= refreshDistance * refreshDistance)
            {
                return false;
            }

            float refreshAngle = Mathf.Clamp(m_DetailStreamingRefreshAngle, 0.25f, 45f);
            return Quaternion.Angle(camera.transform.rotation, m_DetailStreamingCameraRotation) < refreshAngle;
        }

        void RememberDensityDetailStreamingCamera(Camera camera)
        {
            m_HasDetailStreamingCamera = true;
            m_DetailStreamingCameraId = camera.GetInstanceID();
            m_DetailStreamingCamera = camera;
            m_DetailStreamingCameraPosition = camera.transform.position;
            m_DetailStreamingCameraRotation = camera.transform.rotation;
        }

        void BuildFixedDetailCellCandidates(
            Camera camera,
            Plane[] planes,
            Vector3 cameraWorld,
            int layerIndex,
            DensityDetailLayer layer,
            Bounds surfaceBounds,
            int width,
            int height,
            int leafCellSize,
            float maximumDistance,
            List<DetailCandidateChunk> candidates)
        {
            candidates.Clear();
            for (int firstZ = 0; firstZ < height; firstZ += leafCellSize)
            {
                for (int firstX = 0; firstX < width; firstX += leafCellSize)
                {
                    Bounds bounds = CalculateDetailChunkBounds(
                        surfaceBounds,
                        width,
                        height,
                        leafCellSize,
                        firstX,
                        firstZ,
                        layer.MaximumPaintedHeight,
                        layer.YOffset);
                    float distance = Vector3.Distance(cameraWorld, bounds.ClosestPoint(cameraWorld));
                    if (maximumDistance > 0f && distance > maximumDistance)
                        continue;

                    int densityLod = GetFixedDetailCellDensityLod(
                        camera,
                        layerIndex,
                        firstX,
                        firstZ,
                        leafCellSize,
                        distance);
                    candidates.Add(new DetailCandidateChunk(
                        firstX,
                        firstZ,
                        leafCellSize,
                        densityLod,
                        bounds,
                        distance,
                        GeometryUtility.TestPlanesAABB(planes, bounds)));
                }
            }
        }

        int GetFixedDetailCellDensityLod(
            Camera camera,
            int layerIndex,
            int firstX,
            int firstZ,
            int cellSize,
            float distance)
        {
            if (!m_UseDetailDensityLod)
                return 0;

            float nearEnd = Mathf.Max(0f, m_FullDetailDensityDistance);
            float midEnd = Mathf.Max(nearEnd, m_MidDetailDensityDistance);
            int cameraId = camera != null ? camera.GetInstanceID() : 0;
            var key = new DetailLodStateKey(cameraId, layerIndex, firstX, firstZ, cellSize);
            if (!m_DetailLodStates.TryGetValue(key, out DetailLodState state))
            {
                state = new DetailLodState
                {
                    densityLod = distance <= nearEnd ? 0 : distance <= midEnd ? 1 : 2,
                    lastUsedTick = m_DetailRenderTick
                };
                m_DetailLodStates.Add(key, state);
                return state.densityLod;
            }

            float hysteresis = Mathf.Max(0f, m_DetailDensityLodHysteresis);
            if (state.densityLod == 0)
            {
                if (distance > nearEnd + hysteresis)
                    state.densityLod = distance > midEnd + hysteresis ? 2 : 1;
            }
            else if (state.densityLod == 1)
            {
                if (distance < nearEnd - hysteresis)
                    state.densityLod = 0;
                else if (distance > midEnd + hysteresis)
                    state.densityLod = 2;
            }
            else if (distance < midEnd - hysteresis)
            {
                state.densityLod = distance < nearEnd - hysteresis ? 0 : 1;
            }
            state.lastUsedTick = m_DetailRenderTick;
            return state.densityLod;
        }

        void BuildDetailHierarchyCandidates(
            Camera camera,
            Plane[] planes,
            Vector3 cameraWorld,
            int layerIndex,
            DensityDetailLayer layer,
            Bounds surfaceBounds,
            int width,
            int height,
            int leafCellSize,
            float maximumDistance,
            List<DetailCandidateChunk> candidates)
        {
            candidates.Clear();
            int farCellSize = m_UseDetailDensityLod && m_AppearanceCaptureCamera == null ? leafCellSize * 4 : leafCellSize;
            int startingLod = m_UseDetailDensityLod && m_AppearanceCaptureCamera == null ? 2 : 0;
            for (int firstZ = 0; firstZ < height; firstZ += farCellSize)
            {
                for (int firstX = 0; firstX < width; firstX += farCellSize)
                {
                    AddDetailHierarchyNode(
                        camera,
                        planes,
                        cameraWorld,
                        layerIndex,
                        layer,
                        surfaceBounds,
                        width,
                        height,
                        leafCellSize,
                        firstX,
                        firstZ,
                        farCellSize,
                        startingLod,
                        maximumDistance,
                        candidates);
                }
            }
        }

        void AddDetailHierarchyNode(
            Camera camera,
            Plane[] planes,
            Vector3 cameraWorld,
            int layerIndex,
            DensityDetailLayer layer,
            Bounds surfaceBounds,
            int width,
            int height,
            int leafCellSize,
            int firstX,
            int firstZ,
            int cellSize,
            int densityLod,
            float maximumDistance,
            List<DetailCandidateChunk> candidates)
        {
            Bounds bounds = CalculateDetailChunkBounds(
                surfaceBounds,
                width,
                height,
                cellSize,
                firstX,
                firstZ,
                layer.MaximumPaintedHeight,
                layer.YOffset);
            if (!GeometryUtility.TestPlanesAABB(planes, bounds))
                return;
            float distance = Vector3.Distance(cameraWorld, bounds.ClosestPoint(cameraWorld));
            if (maximumDistance > 0f && distance > maximumDistance)
                return;

            if (densityLod > 0)
            {
                float splitDistance = densityLod == 2
                    ? Mathf.Max(m_FullDetailDensityDistance, m_MidDetailDensityDistance)
                    : Mathf.Max(0f, m_FullDetailDensityDistance);
                if (ShouldSplitDetailHierarchyNode(
                    camera,
                    layerIndex,
                    firstX,
                    firstZ,
                    cellSize,
                    distance,
                    splitDistance))
                {
                    int childSize = Mathf.Max(leafCellSize, cellSize / 2);
                    int endX = Mathf.Min(width, firstX + cellSize);
                    int endZ = Mathf.Min(height, firstZ + cellSize);
                    for (int childZ = firstZ; childZ < endZ; childZ += childSize)
                    {
                        for (int childX = firstX; childX < endX; childX += childSize)
                        {
                            AddDetailHierarchyNode(
                                camera,
                                planes,
                                cameraWorld,
                                layerIndex,
                                layer,
                                surfaceBounds,
                                width,
                                height,
                                leafCellSize,
                                childX,
                                childZ,
                                childSize,
                                densityLod - 1,
                                maximumDistance,
                                candidates);
                        }
                    }
                    return;
                }
            }

            candidates.Add(new DetailCandidateChunk(
                firstX,
                firstZ,
                cellSize,
                densityLod,
                bounds,
                distance));
        }

        bool ShouldSplitDetailHierarchyNode(
            Camera camera,
            int layerIndex,
            int firstX,
            int firstZ,
            int cellSize,
            float distance,
            float splitDistance)
        {
            int cameraId = camera != null ? camera.GetHashCode() : 0;
            var key = new DetailLodStateKey(cameraId, layerIndex, firstX, firstZ, cellSize);
            if (!m_DetailLodStates.TryGetValue(key, out DetailLodState state))
            {
                state = new DetailLodState
                {
                    split = distance <= splitDistance,
                    lastUsedTick = m_DetailRenderTick
                };
                m_DetailLodStates.Add(key, state);
                return state.split;
            }

            float hysteresis = Mathf.Max(0f, m_DetailDensityLodHysteresis);
            if (state.split)
            {
                if (distance > splitDistance + hysteresis)
                    state.split = false;
            }
            else if (distance < splitDistance - hysteresis)
            {
                state.split = true;
            }
            state.lastUsedTick = m_DetailRenderTick;
            return state.split;
        }

        Bounds CalculateDetailChunkBounds(Bounds surfaceBounds, int width, int height, int chunkCells, int firstX, int firstZ, float maximumHeight, float yOffset)
        {
            int endX = Mathf.Min(width, firstX + chunkCells);
            int endZ = Mathf.Min(height, firstZ + chunkCells);
            float minX = Mathf.Lerp(surfaceBounds.min.x, surfaceBounds.max.x, firstX / (float)width);
            float maxX = Mathf.Lerp(surfaceBounds.min.x, surfaceBounds.max.x, endX / (float)width);
            float minZ = Mathf.Lerp(surfaceBounds.min.z, surfaceBounds.max.z, firstZ / (float)height);
            float maxZ = Mathf.Lerp(surfaceBounds.min.z, surfaceBounds.max.z, endZ / (float)height);
            Bounds localBounds = new Bounds(
                new Vector3((minX + maxX) * 0.5f, surfaceBounds.center.y + yOffset + maximumHeight * 0.5f, (minZ + maxZ) * 0.5f),
                new Vector3(maxX - minX, surfaceBounds.size.y + maximumHeight, maxZ - minZ));
            return TransformBounds(localBounds, transform.localToWorldMatrix);
        }

        float GetDetailDensityScale(int densityLod)
        {
            if (m_AppearanceCaptureCamera != null) return 1f;
            float overall = Mathf.Clamp01(m_OverallDetailDensity);
            if (!m_UseDetailDensityLod || densityLod <= 0)
                return overall;
            if (densityLod == 1)
                return overall * Mathf.Clamp(m_MidDetailDensity, 0.01f, 1f);
            return overall * Mathf.Clamp(m_FarDetailDensity, 0.01f, 1f);
        }

        static bool HasRenderableDetailData(DensityDetailChunk chunk) =>
            chunk != null && (chunk.gpuProcedural || chunk.combinedDraws.Count > 0 || chunk.batches.Count > 0);

        static bool IsDetailChunkFullyReady(DensityDetailChunk chunk) =>
            chunk != null && chunk.pendingCombinedDraws.Count == 0;

        DensityDetailChunk FindDetailHierarchyParentFallback(
            int layerIndex,
            int firstX,
            int firstZ,
            int cellSize,
            int desiredLod,
            int leafCellSize)
        {
            int parentSize = cellSize;
            for (int parentLod = desiredLod + 1; parentLod <= 2; parentLod++)
            {
                parentSize = Mathf.Max(parentSize * 2, leafCellSize << parentLod);
                int parentFirstX = firstX / parentSize * parentSize;
                int parentFirstZ = firstZ / parentSize * parentSize;
                if (m_DensityDetailCache.TryGetValue(
                        new DetailChunkKey(layerIndex, parentFirstX, parentFirstZ, parentSize, parentLod),
                        out DensityDetailChunk parentChunk)
                    && IsDetailChunkFullyReady(parentChunk)
                    && HasRenderableDetailData(parentChunk))
                {
                    return parentChunk;
                }
            }
            return null;
        }

        DensityDetailChunk BuildDensityDetailChunk(
            int layerIndex,
            DensityDetailLayer layer,
            Prototype prototype,
            Bounds surfaceBounds,
            int chunkCells,
            int firstX,
            int firstZ,
            int densityLod,
            float densityScale)
        {
            var chunk = new DensityDetailChunk
            {
                densityLod = densityLod,
                layerIndex = layerIndex,
                gpuProcedural = ShouldBuildGpuProceduralDetailCells()
            };
            chunk.worldBounds = CalculateDetailChunkBounds(surfaceBounds, layer.DensityMap.width, layer.DensityMap.height, chunkCells, firstX, firstZ, layer.MaximumPaintedHeight, layer.YOffset);
            DenseDetailPrototypeParts prototypeParts = GetDenseDetailRenderParts(prototype);
            List<RenderPart> parts = prototypeParts.parts;
            m_LastDensityDetailSourceParts = Mathf.Max(m_LastDensityDetailSourceParts, prototypeParts.sourcePartCount);
            m_LastDensityDetailBatchedParts = Mathf.Max(m_LastDensityDetailBatchedParts, parts.Count);
            if (parts.Count == 0)
                return chunk;
            var matricesByPart = new List<Matrix4x4>[parts.Count];
            for (int index = 0; index < parts.Count; index++)
                matricesByPart[index] = new List<Matrix4x4>();

            int width = layer.DensityMap.width;
            int height = layer.DensityMap.height;
            int endX = Mathf.Min(width, firstX + chunkCells);
            int endZ = Mathf.Min(height, firstZ + chunkCells);
            long sourceInstanceCount = 0;
            for (int z = firstZ; z < endZ; z++)
                for (int x = firstX; x < endX; x++)
                    sourceInstanceCount += ReadDensity(layer.DensityMap, x, z);
            if (sourceInstanceCount == 0)
                return chunk;

            int generated = 0;
            // Mesh complexity and fallback upload limits must not alter the density
            // population. Oversized combined draws use the instanced fallback below.
            // Use transformed terrain X/Z area, not density-map resolution or the
            // axis-aligned world bounds (which grow when the terrain rotates).
            double cellWidth = surfaceBounds.size.x * (double)(endX - firstX) / width;
            double cellDepth = surfaceBounds.size.z * (double)(endZ - firstZ) / height;
            double areaScale = Vector3.Cross(transform.TransformVector(Vector3.right),
                transform.TransformVector(Vector3.forward)).magnitude;
            int densityNormalizedLimit = (int)Math.Min(
                int.MaxValue,
                Math.Floor(cellWidth * cellDepth * areaScale * MaximumDetailInstancesPerSquareMetre));
            // Thin the area-based population by the density multiplier. Larger
            // HLOD cells get proportionally larger populations before thinning.
            double requestedGenerationScale = Math.Min(
                    1.0,
                    densityNormalizedLimit / (double)sourceInstanceCount)
                * Mathf.Clamp01(densityScale);
            int requestedInstanceCount = (int)Math.Min(
                int.MaxValue,
                Math.Ceiling(sourceInstanceCount * requestedGenerationScale));
            int limit = Mathf.Max(0, requestedInstanceCount);
            if (m_AppearanceCaptureCamera != null)
            {
                m_AppearanceCapturePopulation += limit;
                if (m_AppearanceCapturePopulation > 1000000)
                {
                    // Discard this render and retry smaller tiles. Never save a
                    // capacity-limited image or silently reduce a layer's density.
                    AppearanceCaptureNeedsSubdivision = true;
                    return chunk;
                }
            }
            double generationScale = Math.Min(
                requestedGenerationScale,
                limit / (double)sourceInstanceCount);

            if (chunk.gpuProcedural)
            {
                // Cache one compact span per occupied density texel. A span can
                // represent thousands of blades and is expanded by compute only
                // when its cell is submitted. This is the same fundamental idea
                // as Unity Terrain's represented detail counts: the population is
                // stored, not hundreds of millions of object matrices.
                for (int z = firstZ; z < endZ && generated < limit; z++)
                {
                    for (int x = firstX; x < endX && generated < limit; x++)
                    {
                        int sourceCount = ReadDensity(layer.DensityMap, x, z);
                        double desiredCount = sourceCount * generationScale;
                        int count = Mathf.FloorToInt((float)desiredCount);
                        float remainder = (float)(desiredCount - count);
                        uint cellHash = Hash((uint)layer.Seed ^ (uint)(x * 73856093) ^ (uint)(z * 19349663));
                        if (Hash01(cellHash) < remainder)
                            count++;
                        count = Mathf.Min(count, limit - generated);
                        if (count <= 0)
                            continue;
                        chunk.proceduralSpawns.Add(new DensityDetailSpawn(x, z, count, layer.GetPaintedSizes(x, z)));
                        generated += count;
                    }
                }
                chunk.instanceCount = generated;
                return chunk;
            }

            for (int z = firstZ; z < endZ && generated < limit; z++)
            {
                for (int x = firstX; x < endX && generated < limit; x++)
                {
                    int sourceCount = ReadDensity(layer.DensityMap, x, z);
                    double desiredCount = sourceCount * generationScale;
                    int count = Mathf.FloorToInt((float)desiredCount);
                    float remainder = (float)(desiredCount - count);
                    uint cellHash = Hash((uint)layer.Seed ^ (uint)(x * 73856093) ^ (uint)(z * 19349663));
                    if (Hash01(cellHash) < remainder)
                        count++;
                    count = Mathf.Min(count, limit - generated);
                    Vector4 sizes = layer.GetPaintedSizes(x, z);
                    for (int item = 0; item < count; item++)
                    {
                        uint hash = Hash(cellHash ^ (uint)(item * 83492791));
                        float jitterX = Hash01(hash);
                        float jitterZ = Hash01(Hash(hash + 0x9E3779B9u));
                        float paintedSize = Mathf.Lerp(Mathf.Lerp(sizes.x, sizes.y, jitterX), Mathf.Lerp(sizes.z, sizes.w, jitterX), jitterZ);
                        float widthScale = Mathf.Lerp(layer.MinWidth, layer.MaxWidth, Hash01(Hash(hash + 0x85EBCA6Bu)));
                        float heightScale = Mathf.Lerp(layer.MinHeight, layer.MaxHeight, Hash01(Hash(hash + 0xC2B2AE35u)));
                        float angle = Hash01(Hash(hash + 0x27D4EB2Fu)) * 360f;
                        float normalizedX = (x + jitterX) / width;
                        float normalizedZ = (z + jitterZ) / height;
                        float localX = Mathf.Lerp(surfaceBounds.min.x, surfaceBounds.max.x, normalizedX);
                        float localZ = Mathf.Lerp(surfaceBounds.min.z, surfaceBounds.max.z, normalizedZ);
                        float localY = SampleSurfaceHeight(normalizedX, normalizedZ) + layer.YOffset;
                        Matrix4x4 instanceMatrix = Matrix4x4.TRS(
                            new Vector3(localX, localY, localZ),
                            Quaternion.Euler(0f, angle, 0f),
                            new Vector3(widthScale, heightScale, widthScale) * paintedSize);
                        for (int partIndex = 0; partIndex < parts.Count; partIndex++)
                            matricesByPart[partIndex].Add(instanceMatrix * parts[partIndex].relativeMatrix);
                        generated++;
                    }
                }
            }
            chunk.instanceCount = generated;
            uint orderHash = Hash(
                (uint)layer.Seed
                ^ (uint)(firstX * 73856093)
                ^ (uint)(firstZ * 19349663));
            int permutationOffset = generated > 0 ? (int)(orderHash % (uint)generated) : 0;
            int permutationStride = GetCoprimeStride(generated, Hash(orderHash + 0x9E3779B9u));

            for (int partIndex = 0; partIndex < parts.Count; partIndex++)
            {
                RenderPart part = parts[partIndex];
                List<Matrix4x4> matrices = matricesByPart[partIndex];
                long combinedVertexCount = (long)part.mesh.vertexCount * matrices.Count;
                if (m_CombineDenseDetailMeshes
                    && m_AppearanceCaptureCamera == null
                    && !(m_UseBatchRendererGroup && Application.isPlaying)
                    && !RequiresPerInstanceObjectTransform(part.material)
                    && !IsDetailPaintRegion(layerIndex, firstX, firstZ, chunkCells, width, height)
                    && matrices.Count > 1
                    && combinedVertexCount <= Mathf.Max(65535, m_MaxCombinedDetailVerticesPerChunk))
                {
                    CombinedMeshSource source = GetCombinedMeshSource(part);
                    if (source != null)
                    {
                        int instancesPerUpload = Mathf.Clamp(
                            Mathf.Max(32768, m_MaxDetailVerticesPerUpload) / Mathf.Max(1, source.vertices.Length),
                            1,
                            1023);
                        bool queuedAny = false;
                        for (int start = 0; start < matrices.Count; start += instancesPerUpload)
                        {
                            int count = Mathf.Min(instancesPerUpload, matrices.Count - start);
                            PendingCombinedDetailDraw pending = QueueCombinedDetailMesh(
                                source,
                                matrices,
                                start,
                                count,
                                part.material,
                                prototype);
                            if (pending == null)
                                continue;
                            chunk.pendingCombinedDraws.Add(pending);
                            queuedAny = true;
                        }
                        if (queuedAny)
                            continue;
                    }
                }

                var batch = new DrawBatch
                {
                    mesh = part.mesh,
                    subMesh = part.subMesh,
                    material = GetInstancedMaterial(part.material),
                    prototype = prototype,
                    lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off,
                    shadowCastingOverride = m_DenseDetailShadows
                        ? prototype.ShadowCasting
                        : UnityEngine.Rendering.ShadowCastingMode.Off
                };
                for (int start = 0; start < matrices.Count; start += 1023)
                {
                    int count = Mathf.Min(1023, matrices.Count - start);
                    var matrixChunk = new Matrix4x4[count];
                    for (int matrixIndex = 0; matrixIndex < count; matrixIndex++)
                    {
                        int orderedIndex = (int)((permutationOffset
                            + (long)(start + matrixIndex) * permutationStride) % matrices.Count);
                        matrixChunk[matrixIndex] = transform.localToWorldMatrix * matrices[orderedIndex];
                    }
                    batch.matrixChunks.Add(matrixChunk);
                }
                if (batch.matrixChunks.Count > 0)
                    chunk.batches.Add(batch);
            }
            return chunk;
        }

        DenseDetailPrototypeParts GetDenseDetailRenderParts(Prototype prototype)
        {
            if (m_DenseDetailPrototypeParts.TryGetValue(prototype, out DenseDetailPrototypeParts cached))
                return cached;

            var result = new DenseDetailPrototypeParts();
            List<RenderPart> sourceParts = GetRenderParts(prototype);
            result.sourcePartCount = sourceParts.Count;
            if (sourceParts.Count <= 1)
            {
                result.parts.AddRange(sourceParts);
                m_DenseDetailPrototypeParts[prototype] = result;
                return result;
            }

            // A detail prefab often contains several child MeshRenderers that all
            // use the same material. Drawing every child separately multiplies
            // the 1023-instance batches (and HDRP repeats those submissions in
            // its depth and GBuffer passes). Merge only the small prototype
            // geometry here. Each grass clump still receives its own object
            // matrix, so object-space wind, scale and texture mapping stay intact.
            var groups = new Dictionary<Material, List<RenderPart>>();
            var materialOrder = new List<Material>();
            for (int partIndex = 0; partIndex < sourceParts.Count; partIndex++)
            {
                RenderPart part = sourceParts[partIndex];
                if (!groups.TryGetValue(part.material, out List<RenderPart> group))
                {
                    group = new List<RenderPart>();
                    groups.Add(part.material, group);
                    materialOrder.Add(part.material);
                }
                group.Add(part);
            }

            for (int materialIndex = 0; materialIndex < materialOrder.Count; materialIndex++)
            {
                Material material = materialOrder[materialIndex];
                List<RenderPart> group = groups[material];
                if (group.Count == 1)
                {
                    result.parts.Add(group[0]);
                    continue;
                }

                Mesh merged = TryCreateDenseDetailPrototypeMesh(prototype, material, group);
                if (merged == null)
                {
                    result.parts.AddRange(group);
                    continue;
                }
                result.generatedMeshes.Add(merged);
                result.parts.Add(new RenderPart(merged, 0, material, Matrix4x4.identity));
            }

            m_DenseDetailPrototypeParts[prototype] = result;
            return result;
        }

        Mesh TryCreateDenseDetailPrototypeMesh(
            Prototype prototype,
            Material material,
            List<RenderPart> parts)
        {
            var combines = new CombineInstance[parts.Count];
            long vertexCount = 0;
            for (int partIndex = 0; partIndex < parts.Count; partIndex++)
            {
                RenderPart part = parts[partIndex];
                if (part.mesh == null)
                    return null;
                vertexCount += part.mesh.vertexCount;
                combines[partIndex] = new CombineInstance
                {
                    mesh = part.mesh,
                    subMeshIndex = part.subMesh,
                    transform = part.relativeMatrix
                };
            }

            Mesh merged = null;
            try
            {
                merged = new Mesh
                {
                    name = $"{(prototype.Prefab != null ? prototype.Prefab.name : material.name)} (MG Dense Prototype)",
                    hideFlags = HideFlags.HideAndDontSave,
                    indexFormat = vertexCount > ushort.MaxValue
                        ? IndexFormat.UInt32
                        : IndexFormat.UInt16
                };
                merged.CombineMeshes(combines, true, true, false);
                merged.RecalculateBounds();
                return merged;
            }
            catch (Exception exception)
            {
                if (merged != null)
                {
                    if (Application.isPlaying)
                        Destroy(merged);
                    else
                        DestroyImmediate(merged);
                }
                Debug.LogWarning(
                    $"MG Terrain could not consolidate detail prototype '{(prototype.Prefab != null ? prototype.Prefab.name : material.name)}'. " +
                    $"It will keep the original renderer parts. {exception.Message}",
                    this);
                return null;
            }
        }

        static bool RequiresPerInstanceObjectTransform(Material material)
        {
            if (material == null || material.shader == null)
                return false;

            // Baking many instances into one mesh changes unity_ObjectToWorld from
            // the blade transform to the whole terrain transform. Wind/fade graphs
            // that use Object Position then deform every baked blade as if it were
            // one enormous object. Keep those materials on the GPU-instanced path,
            // which retains a real object matrix per blade.
            string shaderName = material.shader.name;
            if (shaderName.IndexOf("MG_Grass", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return material.HasProperty("_Use_Quixel_wind")
                || material.HasProperty("_Use_Quixel_Green_for_wind");
        }

        CombinedMeshSource GetCombinedMeshSource(RenderPart part)
        {
            if (part.mesh == null)
                return null;
            var key = new CombinedDetailSourceKey(part.mesh, part.subMesh);
            if (m_CombinedDetailSources.TryGetValue(key, out CombinedMeshSource cached))
                return cached;

            try
            {
                var source = new CombinedMeshSource
                {
                    vertices = part.mesh.vertices,
                    normals = part.mesh.normals,
                    tangents = part.mesh.tangents,
                    uv = part.mesh.uv,
                    colors = part.mesh.colors32,
                    indices = part.mesh.GetIndices(part.subMesh),
                    topology = part.mesh.GetTopology(part.subMesh)
                };
                if (source.vertices.Length == 0 || source.indices.Length == 0)
                    return null;
                m_CombinedDetailSources.Add(key, source);
                return source;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"MG Terrain could not cache a dense detail prototype: {exception.Message}", this);
                return null;
            }
        }

        PendingCombinedDetailDraw QueueCombinedDetailMesh(
            CombinedMeshSource source,
            List<Matrix4x4> matrices,
            int matrixStart,
            int matrixCount,
            Material material,
            Prototype prototype)
        {
            try
            {
                return new PendingCombinedDetailDraw
                {
                    material = material,
                    prototype = prototype,
                    instanceStart = matrixStart,
                    instanceCount = matrixCount,
                    estimatedBytes = EstimateCombinedMeshBytes(source, matrixCount),
                    task = Task.Run(() => BuildCombinedMeshData(source, matrices, matrixStart, matrixCount))
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"MG Terrain could not queue a dense detail chunk build: {exception.Message}", this);
                return null;
            }
        }

        static CombinedMeshData BuildCombinedMeshData(
            CombinedMeshSource source,
            List<Matrix4x4> matrices,
            int matrixStart,
            int matrixCount)
        {
            Vector3[] sourceVertices = source.vertices;
            int sourceVertexCount = sourceVertices.Length;
            int instanceCount = matrixCount;
            int vertexCount = checked(sourceVertexCount * instanceCount);
            int indexCount = checked(source.indices.Length * instanceCount);
            var result = new CombinedMeshData
            {
                vertices = new CombinedVertex[vertexCount],
                indices = new int[indexCount],
                topology = source.topology
            };
            bool hasNormals = source.normals.Length == sourceVertexCount;
            bool hasTangents = source.tangents.Length == sourceVertexCount;
            bool hasUv = source.uv.Length == sourceVertexCount;
            bool hasColors = source.colors.Length == sourceVertexCount;
            Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            for (int instanceIndex = 0; instanceIndex < instanceCount; instanceIndex++)
            {
                Matrix4x4 matrix = matrices[matrixStart + instanceIndex];
                Matrix4x4 normalMatrix = hasNormals ? matrix.inverse.transpose : Matrix4x4.identity;
                int vertexOffset = instanceIndex * sourceVertexCount;
                for (int vertexIndex = 0; vertexIndex < sourceVertexCount; vertexIndex++)
                {
                    int outputIndex = vertexOffset + vertexIndex;
                    Vector3 position = matrix.MultiplyPoint3x4(sourceVertices[vertexIndex]);
                    var vertex = new CombinedVertex
                    {
                        position = position,
                        normal = hasNormals ? normalMatrix.MultiplyVector(source.normals[vertexIndex]).normalized : Vector3.up,
                        tangent = new Vector4(1f, 0f, 0f, 1f),
                        color = hasColors ? source.colors[vertexIndex] : new Color32(255, 255, 255, 255),
                        uv = hasUv ? source.uv[vertexIndex] : Vector2.zero
                    };
                    if (hasTangents)
                    {
                        Vector4 sourceTangent = source.tangents[vertexIndex];
                        Vector3 tangent = matrix.MultiplyVector(sourceTangent).normalized;
                        vertex.tangent = new Vector4(tangent.x, tangent.y, tangent.z, sourceTangent.w);
                    }
                    result.vertices[outputIndex] = vertex;
                    minimum = Vector3.Min(minimum, position);
                    maximum = Vector3.Max(maximum, position);
                }
                int indexOffset = instanceIndex * source.indices.Length;
                for (int index = 0; index < source.indices.Length; index++)
                    result.indices[indexOffset + index] = source.indices[index] + vertexOffset;
            }
            result.bounds = new Bounds();
            result.bounds.SetMinMax(minimum, maximum);
            return result;
        }

        static long EstimateCombinedMeshBytes(CombinedMeshSource source, int instanceCount)
        {
            if (source == null || instanceCount <= 0)
                return 0L;
            return (long)source.vertices.Length * instanceCount * CombinedVertexStrideBytes
                + (long)source.indices.Length * instanceCount * sizeof(int);
        }

        void FinalizeCompletedDetailMeshes(DensityDetailChunk chunk, ref int remainingUploads)
        {
            if (remainingUploads <= 0)
                return;
            for (int index = chunk.pendingCombinedDraws.Count - 1; index >= 0 && remainingUploads > 0; index--)
            {
                PendingCombinedDetailDraw pending = chunk.pendingCombinedDraws[index];
                if (!pending.task.IsCompleted)
                    continue;
                chunk.pendingCombinedDraws.RemoveAt(index);
                if (pending.abandoned)
                    continue;
                if (pending.task.IsFaulted)
                {
                    Debug.LogException(pending.task.Exception, this);
                    continue;
                }
                try
                {
                    CombinedMeshData data = pending.task.Result;
                    var mesh = new Mesh
                    {
                        name = "MG Streamed Detail Chunk",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    const MeshUpdateFlags updateFlags = MeshUpdateFlags.DontRecalculateBounds
                        | MeshUpdateFlags.DontValidateIndices
                        | MeshUpdateFlags.DontNotifyMeshUsers;
                    mesh.SetVertexBufferParams(
                        data.vertices.Length,
                        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                        new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4),
                        new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
                    mesh.SetVertexBufferData(data.vertices, 0, 0, data.vertices.Length, 0, updateFlags);
                    mesh.SetIndexBufferParams(data.indices.Length, IndexFormat.UInt32);
                    mesh.SetIndexBufferData(data.indices, 0, 0, data.indices.Length, updateFlags);
                    mesh.subMeshCount = 1;
                    mesh.SetSubMesh(
                        0,
                        new SubMeshDescriptor(0, data.indices.Length, data.topology)
                        {
                            bounds = data.bounds,
                            vertexCount = data.vertices.Length
                        },
                        updateFlags);
                    mesh.bounds = data.bounds;
                    mesh.UploadMeshData(true);
                    chunk.combinedDraws.Add(new CombinedDetailDraw
                    {
                        mesh = mesh,
                        material = pending.material,
                        prototype = pending.prototype,
                        instanceStart = pending.instanceStart,
                        instanceCount = pending.instanceCount,
                        estimatedBytes = pending.estimatedBytes
                    });
                    remainingUploads--;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
        }

        void DestroyDensityDetailChunk(DensityDetailChunk chunk)
        {
            if (chunk == null)
                return;
            for (int index = 0; index < chunk.pendingCombinedDraws.Count; index++)
                chunk.pendingCombinedDraws[index].abandoned = true;
            chunk.pendingCombinedDraws.Clear();
            for (int index = 0; index < chunk.combinedDraws.Count; index++)
            {
                Mesh mesh = chunk.combinedDraws[index].mesh;
                if (mesh == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(mesh);
                else
                    DestroyImmediate(mesh);
            }
            chunk.combinedDraws.Clear();
        }

        int ReadDensity(Texture2D densityMap, int x, int z)
        {
            if (densityMap.format == TextureFormat.R16)
            {
                try
                {
                    var values = densityMap.GetRawTextureData<ushort>();
                    return values[z * densityMap.width + x];
                }
                catch (UnityException)
                {
                    return 0;
                }
            }
            try { return Mathf.RoundToInt(densityMap.GetPixel(x, z).r * 65535f); }
            catch (UnityException) { return 0; }
        }

        float SampleSurfaceHeight(float normalizedX, float normalizedZ)
        {
            Mesh mesh = MeshFilter != null ? MeshFilter.sharedMesh : null;
            if (mesh == null)
                return 0f;
            if (m_CachedDetailSurfaceMesh != mesh || m_CachedDetailSurfaceVertices == null || m_CachedDetailSurfaceVertices.Length != mesh.vertexCount)
            {
                m_CachedDetailSurfaceMesh = mesh;
                m_CachedDetailSurfaceVertices = mesh.vertices;
            }
            Vector3[] vertices = m_CachedDetailSurfaceVertices;
            int width = Mathf.Max(2, m_SurfaceGridWidth);
            int height = Mathf.Max(2, m_SurfaceGridHeight);
            if (vertices.Length != width * height)
            {
                int square = Mathf.RoundToInt(Mathf.Sqrt(vertices.Length));
                if (square * square != vertices.Length)
                    return mesh.bounds.center.y;
                width = height = square;
            }
            float gridX = Mathf.Clamp01(normalizedX) * (width - 1);
            float gridZ = Mathf.Clamp01(normalizedZ) * (height - 1);
            int x0 = Mathf.Min(Mathf.FloorToInt(gridX), width - 2);
            int z0 = Mathf.Min(Mathf.FloorToInt(gridZ), height - 2);
            float tx = gridX - x0;
            float tz = gridZ - z0;
            float bottom = Mathf.Lerp(vertices[z0 * width + x0].y, vertices[z0 * width + x0 + 1].y, tx);
            float top = Mathf.Lerp(vertices[(z0 + 1) * width + x0].y, vertices[(z0 + 1) * width + x0 + 1].y, tx);
            return Mathf.Lerp(bottom, top, tz);
        }

        void PruneDetailChunkCache()
        {
            if (Application.isPlaying && m_UseBatchRendererGroup && m_RetainFixedDetailCells)
                return;
            int maximum = Mathf.Max(32, m_MaxCachedDetailChunks);
            while (m_DensityDetailCache.Count > maximum)
            {
                DetailChunkKey oldestKey = default;
                int oldestTick = int.MaxValue;
                foreach (KeyValuePair<DetailChunkKey, DensityDetailChunk> pair in m_DensityDetailCache)
                {
                    // During an LOD handoff both the requested cell and its old
                    // fallback are touched this tick. The cache limit is soft for
                    // that working set so pruning cannot create a one-frame hole.
                    if (pair.Value.lastUsedTick >= m_DetailRenderTick)
                        continue;
                    if (pair.Value.lastUsedTick >= oldestTick)
                        continue;
                    oldestTick = pair.Value.lastUsedTick;
                    oldestKey = pair.Key;
                }
                if (oldestTick == int.MaxValue)
                    break;
                if (m_DensityDetailCache.TryGetValue(oldestKey, out DensityDetailChunk oldestChunk))
                    DestroyDensityDetailChunk(oldestChunk);
                m_DensityDetailCache.Remove(oldestKey);
            }
        }

        int CountPendingDetailBuilds()
        {
            int count = 0;
            foreach (DensityDetailChunk chunk in m_DensityDetailCache.Values)
                count += chunk.pendingCombinedDraws.Count;
            return count;
        }

        void OnDrawGizmosSelected()
        {
            if (!m_DebugDrawDensityDetailCells)
                return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = Matrix4x4.identity;

            for (int index = 0; index < m_VisibleDensityDetails.Count; index++)
            {
                DensityDetailChunk chunk = m_VisibleDensityDetails[index].chunk;
                if (chunk == null)
                    continue;

                Gizmos.color = GetDensityDetailDebugColor(chunk.densityLod, 0.9f);
                Bounds bounds = chunk.worldBounds;
                Vector3 center = bounds.center;
                Vector3 size = bounds.size;
                // A flat footprint is much easier to read over a terrain than
                // the full vertical culling AABB.
                center.y = bounds.max.y + 0.15f;
                size.y = 0.1f;
                Gizmos.DrawWireCube(center, size);
            }

            if (m_HasDensityDetailDebugCamera)
            {
                Vector3 center = m_LastDensityDetailCameraWorld;
                float hysteresis = Mathf.Max(0f, m_DetailDensityLodHysteresis);
                if (m_UseDetailDensityLod)
                {
                    DrawDensityDetailDebugBoundary(
                        center,
                        Mathf.Max(0f, m_FullDetailDensityDistance),
                        hysteresis,
                        GetDensityDetailDebugColor(0, 0.8f));
                    DrawDensityDetailDebugBoundary(
                        center,
                        Mathf.Max(m_FullDetailDensityDistance, m_MidDetailDensityDistance),
                        hysteresis,
                        GetDensityDetailDebugColor(1, 0.8f));
                }

                if (m_MaxDensityDetailDistance > 0f)
                {
                    Gizmos.color = new Color(0.25f, 0.7f, 1f, 0.75f);
                    Gizmos.DrawWireSphere(center, m_MaxDensityDetailDistance);
                }
            }

            Gizmos.color = previousColor;
            Gizmos.matrix = previousMatrix;
        }

        static Color GetDensityDetailDebugColor(int densityLod, float alpha)
        {
            if (densityLod <= 0)
                return new Color(0.2f, 1f, 0.25f, alpha);
            if (densityLod == 1)
                return new Color(1f, 0.82f, 0.1f, alpha);
            return new Color(1f, 0.28f, 0.12f, alpha);
        }

        static void DrawDensityDetailDebugBoundary(
            Vector3 center,
            float distance,
            float hysteresis,
            Color boundaryColor)
        {
            if (distance <= 0f)
                return;

            Color bandColor = boundaryColor;
            bandColor.a *= 0.35f;
            if (distance - hysteresis > 0f)
            {
                Gizmos.color = bandColor;
                Gizmos.DrawWireSphere(center, distance - hysteresis);
            }
            if (hysteresis > 0f)
            {
                Gizmos.color = bandColor;
                Gizmos.DrawWireSphere(center, distance + hysteresis);
            }

            Gizmos.color = boundaryColor;
            Gizmos.DrawWireSphere(center, distance);
        }

        static uint Hash(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }

        static int GetCoprimeStride(int count, uint hash)
        {
            if (count <= 1)
                return 1;
            int stride = 1 + (int)(hash % (uint)(count - 1));
            while (GreatestCommonDivisor(stride, count) != 1)
            {
                stride++;
                if (stride >= count)
                    stride = 1;
            }
            return stride;
        }

        static int GreatestCommonDivisor(int left, int right)
        {
            while (right != 0)
            {
                int remainder = left % right;
                left = right;
                right = remainder;
            }
            return Mathf.Abs(left);
        }

        static float Hash01(uint value) => (value & 0x00FFFFFFu) / 16777216f;
    }
}
