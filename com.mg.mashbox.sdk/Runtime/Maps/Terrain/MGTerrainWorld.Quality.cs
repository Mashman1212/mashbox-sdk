using System;
using UnityEngine;
namespace MashBoxSDK.Maps.TerrainSystem
{
    [Serializable]
    public sealed class MGTerrainWorldQuality
    {
        public bool m_DrawInstances = true;
        public bool m_DrawInstancesInEditMode = true;
        [Range(2, 64)] public int m_DetailChunkCells = 64;
        [Range(0f, 1f)] public float m_OverallDetailDensity = 1f;
        public bool m_UseDetailDensityLod = true;
        public bool m_UseStaticDetailCells;
        [Min(0f)] public float m_DensityTransitionWidth = 10f;
        [Min(0f)] public float m_FullDetailDensityDistance = 35f;
        [Min(0f)] public float m_MidDetailDensityDistance = 100f;
        [Range(0f, 1f)] public float m_MidDetailDensity = 0.5f;
        [Range(0f, 1f)] public float m_FarDetailDensity = 0.15f;
        [Min(0f)] public float m_DetailDensityLodHysteresis = 8f;
        [Range(0f, .75f)] public float m_DistantDetailBudgetReserve = 0.25f;
        public bool m_UseBatchRendererGroup = true;
        public bool m_UseGpuProceduralDetailGeneration = true;
        public bool m_UseIndirectDetailDraws;
        public bool m_PrewarmFixedDetailCells = true;
        public float m_DetailStreamingRefreshDistance = 2f;
        public float m_DetailStreamingRefreshAngle = 3f;
        public bool m_CombineDenseDetailMeshes = true;
        public bool m_DenseDetailShadows;
        [Min(65535)] public int m_MaxCombinedDetailVerticesPerChunk = 600000;
        [Range(32768, 262144)] public int m_MaxDetailVerticesPerUpload = 131072;
    }
    public sealed partial class MGTerrain
    {
        public MGTerrainWorldQuality CaptureWorldQuality() => new MGTerrainWorldQuality
        {
            m_DrawInstances = m_DrawInstances,
            m_DrawInstancesInEditMode = m_DrawInstancesInEditMode,
            m_DetailChunkCells = m_DetailChunkCells,
            m_OverallDetailDensity = m_OverallDetailDensity,
            m_UseDetailDensityLod = m_UseDetailDensityLod,
            m_UseStaticDetailCells = m_UseStaticDetailCells,
            m_DensityTransitionWidth = m_DensityTransitionWidth,
            m_FullDetailDensityDistance = m_FullDetailDensityDistance,
            m_MidDetailDensityDistance = m_MidDetailDensityDistance,
            m_MidDetailDensity = m_MidDetailDensity,
            m_FarDetailDensity = m_FarDetailDensity,
            m_DetailDensityLodHysteresis = m_DetailDensityLodHysteresis,
            m_DistantDetailBudgetReserve = m_DistantDetailBudgetReserve,
            m_UseBatchRendererGroup = m_UseBatchRendererGroup,
            m_UseGpuProceduralDetailGeneration = m_UseGpuProceduralDetailGeneration,
            m_UseIndirectDetailDraws = m_UseIndirectDetailDraws,
            m_PrewarmFixedDetailCells = m_PrewarmFixedDetailCells,
            m_DetailStreamingRefreshDistance = m_DetailStreamingRefreshDistance,
            m_DetailStreamingRefreshAngle = m_DetailStreamingRefreshAngle,
            m_CombineDenseDetailMeshes = m_CombineDenseDetailMeshes,
            m_DenseDetailShadows = m_DenseDetailShadows,
            m_MaxCombinedDetailVerticesPerChunk = m_MaxCombinedDetailVerticesPerChunk,
            m_MaxDetailVerticesPerUpload = m_MaxDetailVerticesPerUpload,
        };
        internal void ApplyWorldQuality(MGTerrainWorldQuality settings)
        {
            if (settings == null) return;
            bool changed = m_DrawInstances != settings.m_DrawInstances || m_DrawInstancesInEditMode != settings.m_DrawInstancesInEditMode || m_DetailChunkCells != settings.m_DetailChunkCells
                || m_OverallDetailDensity != settings.m_OverallDetailDensity
                || m_UseDetailDensityLod != settings.m_UseDetailDensityLod
                || m_UseStaticDetailCells != settings.m_UseStaticDetailCells
                || m_DensityTransitionWidth != settings.m_DensityTransitionWidth
                || m_FullDetailDensityDistance != settings.m_FullDetailDensityDistance
                || m_MidDetailDensityDistance != settings.m_MidDetailDensityDistance
                || m_MidDetailDensity != settings.m_MidDetailDensity
                || m_FarDetailDensity != settings.m_FarDetailDensity
                || m_DetailDensityLodHysteresis != settings.m_DetailDensityLodHysteresis
                || m_DistantDetailBudgetReserve != settings.m_DistantDetailBudgetReserve
                || m_UseBatchRendererGroup != settings.m_UseBatchRendererGroup
                || m_UseGpuProceduralDetailGeneration != settings.m_UseGpuProceduralDetailGeneration
                || m_UseIndirectDetailDraws != settings.m_UseIndirectDetailDraws
                || m_PrewarmFixedDetailCells != settings.m_PrewarmFixedDetailCells
                || m_DetailStreamingRefreshDistance != settings.m_DetailStreamingRefreshDistance
                || m_DetailStreamingRefreshAngle != settings.m_DetailStreamingRefreshAngle
                || m_CombineDenseDetailMeshes != settings.m_CombineDenseDetailMeshes
                || m_DenseDetailShadows != settings.m_DenseDetailShadows
                || m_MaxCombinedDetailVerticesPerChunk != settings.m_MaxCombinedDetailVerticesPerChunk
                || m_MaxDetailVerticesPerUpload != settings.m_MaxDetailVerticesPerUpload;
            if (!changed) return;
            m_DrawInstances = settings.m_DrawInstances;
            m_DrawInstancesInEditMode = settings.m_DrawInstancesInEditMode;
            m_DetailChunkCells = settings.m_DetailChunkCells;
            m_OverallDetailDensity = settings.m_OverallDetailDensity;
            m_UseDetailDensityLod = settings.m_UseDetailDensityLod;
            m_UseStaticDetailCells = settings.m_UseStaticDetailCells;
            m_DensityTransitionWidth = settings.m_DensityTransitionWidth;
            m_FullDetailDensityDistance = settings.m_FullDetailDensityDistance;
            m_MidDetailDensityDistance = settings.m_MidDetailDensityDistance;
            m_MidDetailDensity = settings.m_MidDetailDensity;
            m_FarDetailDensity = settings.m_FarDetailDensity;
            m_DetailDensityLodHysteresis = settings.m_DetailDensityLodHysteresis;
            m_DistantDetailBudgetReserve = settings.m_DistantDetailBudgetReserve;
            m_UseBatchRendererGroup = settings.m_UseBatchRendererGroup;
            m_UseGpuProceduralDetailGeneration = settings.m_UseGpuProceduralDetailGeneration;
            m_UseIndirectDetailDraws = settings.m_UseIndirectDetailDraws;
            m_PrewarmFixedDetailCells = settings.m_PrewarmFixedDetailCells;
            m_DetailStreamingRefreshDistance = settings.m_DetailStreamingRefreshDistance;
            m_DetailStreamingRefreshAngle = settings.m_DetailStreamingRefreshAngle;
            m_CombineDenseDetailMeshes = settings.m_CombineDenseDetailMeshes;
            m_DenseDetailShadows = settings.m_DenseDetailShadows;
            m_MaxCombinedDetailVerticesPerChunk = settings.m_MaxCombinedDetailVerticesPerChunk;
            m_MaxDetailVerticesPerUpload = settings.m_MaxDetailVerticesPerUpload;
            InvalidateRenderCache();
        }
    }
}



