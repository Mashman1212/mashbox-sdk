using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.Maps.TerrainSystem
{
    /// <summary>One scheduler and renderer for independently authored terrain chunks.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("MashBox/Maps/MG Terrain World")]
    public sealed partial class MGTerrainWorld : MonoBehaviour
    {
        [SerializeField, Min(1), Tooltip("Total visible density-detail instances across all chunks. Placed trees are separate.")]
        int m_VisibleDetailBudget = 50000;
        [SerializeField, Min(1)] int m_DetailCellsBuiltPerFrame = 8;
        [SerializeField, Min(1)] int m_DetailMeshUploadsPerFrame = 2;
        [SerializeField, Min(1)] int m_MaxPendingDetailBuilds = 8;
        [SerializeField, Min(32), Tooltip("Total soft cache target. Cells needed by the current view can exceed it.")]
        int m_CachedDetailCells = 512;
        [SerializeField, Min(1), Tooltip("Maximum detail distance in metres. Chunk prototype distances may shorten it.")]
        float m_DetailDistance = 250f;
        [SerializeField, Min(0), Tooltip("Extra distance before releasing a chunk's detail resources.")]
        float m_UnloadMargin = 64f;
        [SerializeField] MGTerrainWorldQuality m_Quality;
        [SerializeField, HideInInspector] bool m_QualityInitialized;
        [HideInInspector] public int CaptureResolution = 2048;
        [HideInInspector] public float CaptureExposure = 10f;
        [HideInInspector] public float CaptureDetailTilt;
        [HideInInspector] public float DistantMeshSpacing = 2f;
        public void ApplySharedQuality()
        {
            if ((!m_QualityInitialized || m_Quality == null) && m_Chunks.Count > 0 && m_Chunks[0] != null)
            {
                m_Quality = m_Chunks[0].CaptureWorldQuality();
                m_QualityInitialized = true;
            }
            foreach (var chunk in m_Chunks) if (chunk != null) chunk.ApplyWorldQuality(m_Quality);
        }
        public void ApplyQualityPreset(MGTerrain.DetailQualityPreset preset)
        {
            if (m_Chunks.Count == 0) return;
            m_Chunks[0].ApplyDetailQualityPreset(preset);
            m_Quality = m_Chunks[0].CaptureWorldQuality();
            m_QualityInitialized = true;
            ApplySharedQuality();
        }
        readonly List<MGTerrain> m_Chunks = new List<MGTerrain>();
        readonly List<MGTerrain> m_RenderChunks = new List<MGTerrain>();
        int m_BudgetFrame = -1, m_RemainingBuilds, m_RemainingUploads, m_FirstChunk;
        bool m_Rendering;
        public IReadOnlyList<MGTerrain> Chunks => m_Chunks;
        public float DetailDistance => Mathf.Max(1f, m_DetailDistance);
        internal int CacheAllowance => Mathf.Max(1, m_CachedDetailCells / Mathf.Max(1, m_RenderChunks.Count));
        public int VisibleDetailBudget => Mathf.Max(1, m_VisibleDetailBudget);
        public long LastSubmittedDetailInstances { get; private set; }

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RefreshChunks();
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            // Rebind while disabled, so children fall back to standalone or an outer world.
            foreach (var chunk in m_Chunks.ToArray())
                if (chunk != null) chunk.RefreshWorldOwnership();
#if UNITY_6000_0_OR_NEWER
            DisposeWorldRenderer();
#endif
        }

        void OnValidate()
        {
            ApplySharedQuality();
            foreach (var chunk in m_Chunks)
                if (chunk != null) chunk.InvalidateRenderCache();
        }

        void OnTransformChildrenChanged() => RefreshChunks();
        public void RefreshChunks()
        {
            foreach (var chunk in GetComponentsInChildren<MGTerrain>(true))
                chunk.RefreshWorldOwnership();
            foreach (var chunk in m_Chunks.ToArray())
                if (chunk == null) m_Chunks.Remove(chunk);
                else chunk.RefreshWorldOwnership();
        }

        internal void Register(MGTerrain chunk)
        {
            if (!m_Chunks.Contains(chunk)) m_Chunks.Add(chunk);
            if (!m_QualityInitialized || m_Quality == null)
            {
                m_Quality = chunk.CaptureWorldQuality();
                m_QualityInitialized = true;
            }
            chunk.ApplyWorldQuality(m_Quality);
        }
        internal void Unregister(MGTerrain chunk) => m_Chunks.Remove(chunk);
        internal bool TryBuildCell()
        {
            if (m_RemainingBuilds <= 0) return false;
            int pending = 0;
            foreach (var chunk in m_Chunks) if (chunk != null) pending += chunk.WorldPendingBuilds;
            if (pending >= Mathf.Max(1, m_MaxPendingDetailBuilds)) return false;
            m_RemainingBuilds--;
            return true;
        }
        internal bool TryUploadMesh()
        {
            if (m_RemainingUploads <= 0) return false;
            m_RemainingUploads--;
            return true;
        }

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera) => Render(camera);
        void OnRenderObject()
        {
            if (GraphicsSettings.currentRenderPipeline == null) Render(Camera.current);
        }

        void Render(Camera camera)
        {
            if (m_Rendering || camera == null || camera.cameraType == CameraType.Preview) return;
            m_Rendering = true;
            ApplySharedQuality();
            try
            {
                // Edit-mode repaints are independent work opportunities; gameplay cameras share a frame budget.
                if (!Application.isPlaying || m_BudgetFrame != Time.frameCount)
                {
                    m_BudgetFrame = Time.frameCount;
                    m_RemainingBuilds = Mathf.Max(1, m_DetailCellsBuiltPerFrame);
                    m_RemainingUploads = Mathf.Max(1, m_DetailMeshUploadsPerFrame);
                    m_FirstChunk++;
                }
                m_RenderChunks.Clear();
                foreach (var chunk in m_Chunks)
                    if (chunk != null && chunk.isActiveAndEnabled) m_RenderChunks.Add(chunk);
                // Rotate build priority so a dense first chunk cannot starve its neighbours.
                int first = m_RenderChunks.Count == 0 ? 0 : (m_FirstChunk & int.MaxValue) % m_RenderChunks.Count;
                for (int i = 0; i < m_RenderChunks.Count; i++)
                    m_RenderChunks[(i + first) % m_RenderChunks.Count]
                        .PrepareWorldCamera(camera, DetailDistance + Mathf.Max(0f, m_UnloadMargin));
                long demand = 0;
                foreach (var chunk in m_RenderChunks) demand += chunk.WorldDetailDemand;
                long remainingDemand = demand;
                int remaining = VisibleDetailBudget;
                LastSubmittedDetailInstances = 0;
                foreach (var chunk in m_RenderChunks)
                {
                    long requested = chunk.WorldDetailDemand;
                    int allocated = AllocateDetailBudget(requested, remainingDemand, remaining);
                    remainingDemand -= requested;
                    remaining -= allocated;
                    chunk.SubmitWorldDetails(camera, allocated);
                    LastSubmittedDetailInstances += chunk.WorldSubmittedDetails;
                }
            }
            finally { m_Rendering = false; }
        }

        // Integer remainder is carried forward; allocations never exceed the world budget.
        public static int AllocateDetailBudget(long demand, long remainingDemand, int remainingBudget)
        {
            if (demand <= 0 || remainingDemand <= 0 || remainingBudget <= 0) return 0;
            return (int)Math.Min(demand, Math.Min(remainingBudget,
                Math.Floor((double)remainingBudget * demand / remainingDemand)));
        }

        public bool RaycastSurface(Ray ray, out RaycastHit hit, out MGTerrain chunk, float maximumDistance)
        {
            hit = default; chunk = null;
            foreach (var candidate in m_Chunks)
                if (candidate != null && candidate.isActiveAndEnabled
                    && candidate.RaycastSurface(ray, out var next, maximumDistance))
                { hit = next; chunk = candidate; maximumDistance = next.distance; }
            return chunk != null;
        }
    }
}
