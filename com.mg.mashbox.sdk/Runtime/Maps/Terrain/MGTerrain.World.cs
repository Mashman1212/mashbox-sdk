using System;
using System.Collections.Generic;
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
        bool m_WorldDetailSubmissionRequired;
        int m_WorldLastSubmittedBudget = -1;
        public int WorldDetailSubmissionCount { get; private set; }
        public int WorldDetailReuseCount { get; private set; }
        int m_WorldDrawBudget = int.MaxValue;
        public MGTerrainWorld World => m_World;
        internal int WorldPendingBuilds => CountPendingDetailBuilds();
        bool UsesWorldBudget => m_World != null && m_AppearanceCaptureCamera == null;
        float EffectiveDetailDistance => UsesWorldBudget ? m_World.DetailDistance : m_MaxDensityDetailDistance;
        bool RetainDetailCells => !UsesWorldBudget && m_RetainFixedDetailCells;
        // Fully resident GPU cells must be ready for the first gameplay render.
        // They have no asynchronous mesh builds, so streaming limits do not apply.
        bool TryBuildWorldCell(DetailChunkKey key, float distance) =>
            !UsesWorldBudget || KeepAllDetailCellsResident || m_World.TryBuildCell(this, key, distance);
        bool TryUploadWorldMesh() => !UsesWorldBudget || m_World.TryUploadMesh();

        /// <summary>World terrain surfaces currently use dirt, including saved collision chunks.</summary>
        public void ApplyWorldSurfaceTag()
        {
            if (GetComponentInParent<MGTerrainWorld>(true) == null) return;
            gameObject.tag = "dirt";
            if (MeshCollider != null) MeshCollider.gameObject.tag = "dirt";
            if (m_SurfaceColliderRoot != null) m_SurfaceColliderRoot.gameObject.tag = "dirt";
            foreach (var collider in m_SurfaceColliderChunks)
                if (collider != null) collider.gameObject.tag = "dirt";
        }
        public void RefreshWorldOwnership()
        {
            ApplyWorldSurfaceTag();
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
        int GetBudgetedDetailInstanceCount(VisibleDensityDetail visible, float scale)
        {
            if (UsesWorldBudget)
                return GetVisibleDensityDetailInstanceCount(visible);
            return Mathf.FloorToInt(GetVisibleDensityDetailInstanceCount(visible) * scale);
        }

        static readonly Unity.Profiling.ProfilerMarker s_WorldPrepareMarker = new Unity.Profiling.ProfilerMarker("MGTerrain.WorldPrepare");
        static readonly Unity.Profiling.ProfilerMarker s_WorldSubmitMarker = new Unity.Profiling.ProfilerMarker("MGTerrain.WorldSubmit");
        internal void PrepareWorldCamera(Camera camera, float unloadDistance)
        {
            using var profile = s_WorldPrepareMarker.Auto();
            m_WorldPendingDraw = false;
            // Surface shader/geometry LOD is independent of the editor detail visibility toggle.
            PrepareSurfaceForCamera(camera);
            // Captures consume resident data, never the streaming or world budgets.
            if (camera != null && camera.cameraType == CameraType.Reflection)
            {
                RenderInstances(camera);
                return;
            }
            if (EditorDetailsHidden && m_AppearanceCaptureCamera == null) return;
            m_WorldDefersDraw = UsesWorldBudget;
            try
            {
                // Only the detail streaming camera may unload persistent GPU batches.
                bool canStream = !UseResidentDetailStreaming || IsDensityDetailStreamingCamera(camera);
                m_WorldReuseSelection = UsesWorldBudget && UseResidentDetailStreaming && canStream
                    && !m_DetailRenderCacheDirty && CanReuseDensityDetailStreamingSet(camera);
                // Grass streaming distance must not unload a tile that still has visible trees.
                foreach (var layer in m_DensityDetailLayers)
                {
                    if (layer == null || !layer.RenderingEnabled || layer.PaletteSourceOnly
                        || (uint)layer.PrototypeIndex >= m_Prototypes.Count) continue;
                    var prototype = m_Prototypes[layer.PrototypeIndex];
                    if (prototype == null || prototype.Kind != InstanceKind.Tree) continue;
                    float treeDistance = prototype.MaximumDrawDistance > 0f ? prototype.MaximumDrawDistance : camera.farClipPlane;
                    unloadDistance = Mathf.Max(unloadDistance, treeDistance + 64f);
                }
                bool outside = UsesWorldBudget && canStream && !KeepAllDetailCellsResident && MeshRenderer != null
                    && MeshRenderer.bounds.SqrDistance(camera.transform.position) > unloadDistance * unloadDistance;
                if (outside && !m_WorldDetailsSleeping)
                {
                    ReleaseDetailRenderCache();
                    m_VisibleDensityDetails.Clear();
                    InvalidateDetailRenderCache();
                    m_WorldDetailsSleeping = true;
                }
                else if (!outside && canStream) m_WorldDetailsSleeping = false;
                RenderInstances(camera);
                // Streaming selection can reuse the previous view; it still participates in this frame's shared budget.
                if (m_WorldDefersDraw && !m_WorldDetailsSleeping && canStream && m_DrawInstances
                    && (Application.isPlaying || m_DrawInstancesInEditMode)) m_WorldPendingDraw = true;
            }
            finally { m_WorldDefersDraw = false; }
        }

        // The grant pass builds only details. Surface and ordinary tree draws already ran
        // in PrepareWorldCamera; repeating those draws can double-submit the same geometry.
        internal void PrepareWorldGrantedDetails(Camera camera)
        {
            using var profile = s_WorldPrepareMarker.Auto();
            m_WorldDefersDraw = UsesWorldBudget;
            m_WorldReuseSelection = false;
            try { RenderDensityDetails(camera, m_InstanceFrustumPlanes); }
            finally { m_WorldDefersDraw = false; }
        }

        internal void SubmitWorldDetails(Camera camera, int budget)
        {
            using var profile = s_WorldSubmitMarker.Auto();
            if (!m_WorldPendingDraw) return;
            if (UseResidentDetailStreaming && !IsDensityDetailStreamingCamera(camera)) return;
#if UNITY_6000_0_OR_NEWER
            // BRG commands persist. A reused selection does not need sorting, population
            // scans or GPU command comparison again, but live shadow/occlusion updates do.
            if (!m_WorldDetailSubmissionRequired && !m_DetailRenderCacheDirty
                && m_WorldLastSubmittedBudget == budget && HasCurrentGpuDetailCells
                && m_DetailBrgHasSignature && (!m_UseIndirectDetailDraws || m_IndirectDetailDrawsReady))
            {
                WorldDetailReuseCount++;
                LastRegeneratedDetailInstances = 0;
                UpdateDetailShadowSelection(camera);
                UpdateTerrainOcclusion(camera);
                return;
            }
#endif
            SortVisibleDensityDetails();
            m_WorldDrawBudget = budget;
            try
            {
                DrawVisibleDensityDetails(camera);
                m_WorldDetailSubmissionRequired = false;
                m_WorldLastSubmittedBudget = budget;
                WorldDetailSubmissionCount++;
#if UNITY_6000_0_OR_NEWER
                UpdateTerrainOcclusion(camera);
#endif
            }
            finally { m_WorldDrawBudget = int.MaxValue; }
        }

#if UNITY_6000_0_OR_NEWER
        void SetDetailBrgBounds(Bounds bounds)
        {
            if (m_BrgWorld != null) m_BrgWorld.SetChunkBounds(this, bounds);
            else m_DetailBrg.SetGlobalBounds(bounds);
        }
#endif
    }
}
