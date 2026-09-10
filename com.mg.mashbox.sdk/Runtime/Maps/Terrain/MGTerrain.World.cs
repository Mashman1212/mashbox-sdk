using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        [NonSerialized] MGTerrainWorld m_World;
        [NonSerialized] MGTerrainWorld m_BrgWorld;
        bool m_WorldDefersDraw, m_WorldPendingDraw, m_WorldDetailsSleeping;
        bool m_WorldReuseSelection;
        int m_WorldDrawBudget = int.MaxValue;
        public MGTerrainWorld World => m_World;
        internal int WorldPendingBuilds => CountPendingDetailBuilds();
        bool UsesWorldBudget => m_World != null && m_AppearanceCaptureCamera == null;
        float EffectiveDetailDistance => UsesWorldBudget ? m_World.DetailDistance : m_MaxDensityDetailDistance;
        bool RetainDetailCells => !UsesWorldBudget && m_RetainFixedDetailCells;
        bool TryBuildWorldCell() => !UsesWorldBudget || m_World.TryBuildCell();
        bool TryUploadWorldMesh() => !UsesWorldBudget || m_World.TryUploadMesh();

        public void RefreshWorldOwnership()
        {
            MGTerrainWorld next = null;
            if (isActiveAndEnabled)
                for (Transform parent = transform.parent; parent != null; parent = parent.parent)
                {
                    var candidate = parent.GetComponent<MGTerrainWorld>();
                    if (candidate != null && candidate.isActiveAndEnabled) { next = candidate; break; }
                }
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            if (m_World != next)
            {
                ReleaseDetailRenderCache();
                m_VisibleDensityDetails.Clear();
                if (m_World != null) m_World.Unregister(this);
                m_World = next;
                if (m_World != null) m_World.Register(this);
                m_WorldDetailsSleeping = false;
                InvalidateRenderCache();
            }
            if (isActiveAndEnabled && m_World == null)
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        void OnTransformParentChanged() => RefreshWorldOwnership();
        internal long WorldSubmittedDetails => m_WorldPendingDraw ? m_LastSubmittedDensityDetailInstances : 0;
        internal long WorldDetailDemand
        {
            get
            {
                if (!m_WorldPendingDraw) return 0;
                long count = 0;
                foreach (var visible in m_VisibleDensityDetails) count += GetVisibleDensityDetailInstanceCount(visible);
                return count;
            }
        }

        internal void PrepareWorldCamera(Camera camera, float unloadDistance)
        {
            m_WorldPendingDraw = false;
            m_WorldDefersDraw = UsesWorldBudget;
            try
            {
                // Only the detail streaming camera may unload persistent GPU batches.
                bool canStream = !Application.isPlaying || !m_UseBatchRendererGroup || IsDensityDetailStreamingCamera(camera);
                m_WorldReuseSelection = UsesWorldBudget && Application.isPlaying && canStream
                    && !m_DetailRenderCacheDirty && CanReuseDensityDetailStreamingSet(camera);
                bool outside = UsesWorldBudget && canStream && MeshRenderer != null
                    && MeshRenderer.bounds.SqrDistance(camera.transform.position) > unloadDistance * unloadDistance;
                if (outside && !m_WorldDetailsSleeping)
                {
                    ReleaseDetailRenderCache();
                    m_VisibleDensityDetails.Clear();
                    InvalidateDetailRenderCache();
                    m_WorldDetailsSleeping = true;
                }
                else if (!outside && canStream) m_WorldDetailsSleeping = false;
                OnBeginCameraRendering(default, camera);
                // Streaming selection can reuse the previous view; it still participates in this frame's shared budget.
                if (m_WorldDefersDraw && !m_WorldDetailsSleeping && canStream && m_DrawInstances
                    && (Application.isPlaying || m_DrawInstancesInEditMode)) m_WorldPendingDraw = true;
            }
            finally { m_WorldDefersDraw = false; }
        }

        internal void SubmitWorldDetails(Camera camera, int budget)
        {
            if (!m_WorldPendingDraw) return;
            m_WorldDrawBudget = budget;
            try
            {
                DrawVisibleDensityDetails(camera);
#if UNITY_6000_0_OR_NEWER
                UpdateTerrainOcclusion(camera);
#endif
            }
            finally { m_WorldDrawBudget = int.MaxValue; }
        }

        void SetDetailBrgBounds(Bounds bounds)
        {
            if (m_BrgWorld != null) m_BrgWorld.SetChunkBounds(this, bounds);
            else m_DetailBrg.SetGlobalBounds(bounds);
        }
    }
}
