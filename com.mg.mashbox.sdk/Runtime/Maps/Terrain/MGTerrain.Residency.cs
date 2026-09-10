using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        [SerializeField, Tooltip("Prepare all terrain detail cells once and retain their GPU transforms. Camera movement only updates visibility. Disable to use the nearby streaming cache.")]
        bool m_KeepAllDetailCellsResident = true;
        [SerializeField, Tooltip("Experimental: spread visibility scans across frames. Retains extra instances around the camera, increasing render and shadow work. Leave disabled unless full-frame profiling shows a benefit.")]
        bool m_AmortizeResidentVisibility = false;
        bool KeepAllDetailCellsResident => !UsesWorldBudget && m_KeepAllDetailCellsResident && ShouldBuildGpuProceduralDetailCells();
        bool m_FullResidentReady;
        Camera m_CachedGameplayCamera;
        Matrix4x4 m_ResidentProjection;
        readonly List<ResidentDetailSector> m_FullResidentSectors = new List<ResidentDetailSector>();
        static readonly Unity.Profiling.ProfilerMarker s_ResidentVisibilityMarker = new Unity.Profiling.ProfilerMarker("MGTerrain.ResidentVisibility");
        static readonly Unity.Profiling.ProfilerMarker s_ResidentScanMarker = new Unity.Profiling.ProfilerMarker("MGTerrain.ResidentVisibility.Scan");
        static readonly Unity.Profiling.ProfilerMarker s_ResidentSubmitMarker = new Unity.Profiling.ProfilerMarker("MGTerrain.ResidentVisibility.Submit");
        // Keep a conservative movement/turning margin while the next selection is prepared.
        const float ResidentPositionGuard = 12f;
        const float ResidentAngleGuard = 15f;
        const double ResidentScanMilliseconds = 0.25;
        readonly List<VisibleDensityDetail>[] m_ResidentPendingBands =
        {
            new List<VisibleDensityDetail>(), new List<VisibleDensityDetail>(), new List<VisibleDensityDetail>()
        };
        readonly Plane[] m_ResidentScanPlanes = new Plane[6];
        Vector3 m_ResidentSelectedPosition, m_ResidentScanPosition;
        Quaternion m_ResidentSelectedRotation, m_ResidentScanRotation;
        Matrix4x4 m_ResidentScanProjection;
        Camera m_ResidentSelectionCamera;
        int m_ResidentSectorCursor, m_ResidentCellCursor, m_ResidentLastFrame = -1;
        float m_ResidentSectorLimit;
        bool m_ResidentScanPending, m_ResidentSelectionValid, m_ResidentSelectionOrdered;
        bool m_ResidentSelectionAmortized;
        public int LastResidentVisibilityCellsTested { get; private set; }
        public bool ResidentVisibilityUpdatePending => m_ResidentScanPending;

        sealed class ResidentDetailSector
        {
            internal Bounds bounds;
            internal int layer;
            internal Prototype prototype;
            internal readonly List<DetailCandidateChunk> cells = new List<DetailCandidateChunk>();
        }

        void ResetFullDetailResidency()
        {
            m_FullResidentReady = false;
            m_FullResidentSectors.Clear();
            m_ResidentScanPending = m_ResidentSelectionValid = false;
            m_ResidentSelectionCamera = null;
            m_ResidentLastFrame = -1;
            foreach (var band in m_ResidentPendingBands) band.Clear();
        }

        void CacheFullResidentSectors(Camera camera)
        {
            m_FullResidentSectors.Clear();
            foreach (var layer in m_FixedCandidateCaches)
            {
                var sectors = new Dictionary<Vector2Int, ResidentDetailSector>();
                Prototype prototype = m_Prototypes[m_DensityDetailLayers[layer.Key].PrototypeIndex];
                foreach (var cell in layer.Value.candidates)
                {
                    if (cell.cachedChunk == null || cell.cachedChunk.instanceCount == 0) continue;
                    int size = cell.cellSize * 8;
                    var key = new Vector2Int(cell.firstX / size, cell.firstZ / size);
                    if (!sectors.TryGetValue(key, out var sector))
                    {
                        sector = new ResidentDetailSector { bounds = cell.bounds, layer = layer.Key, prototype = prototype };
                        sectors.Add(key, sector);
                        m_FullResidentSectors.Add(sector);
                    }
                    else sector.bounds.Encapsulate(cell.bounds);
                    sector.cells.Add(cell);
                }
            }
            m_ResidentScanPending = m_ResidentSelectionValid = false;
            m_ResidentLastFrame = -1;
            m_ResidentProjection = camera.nonJitteredProjectionMatrix;
            m_FullResidentReady = true;
        }

        void UpdateFullResidentVisibility(Camera camera, Plane[] planes)
        {
            using var profile = s_ResidentVisibilityMarker.Auto();
            if (m_ResidentLastFrame == Time.frameCount) return;
            m_ResidentLastFrame = Time.frameCount;
            LastResidentVisibilityCellsTested = 0;
            LastDetailCandidateBoundsBuilt = 0;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            Matrix4x4 projection = camera.nonJitteredProjectionMatrix;
            // Camera cuts and teleports must not keep an unrelated old view. Normal motion
            // uses bounded scan slices; the current complete draw set stays live meanwhile.
            bool urgent = !m_ResidentSelectionValid || camera != m_ResidentSelectionCamera
                || m_ResidentSelectionAmortized != m_AmortizeResidentVisibility
                || projection != m_ResidentProjection
                || (position - m_ResidentSelectedPosition).sqrMagnitude > ResidentPositionGuard * ResidentPositionGuard
                || Quaternion.Angle(rotation, m_ResidentSelectedRotation) > ResidentAngleGuard;
            if (!m_AmortizeResidentVisibility)
            {
                // Precise selection is the default: amortization's enlarged frustum and
                // denser populations can cost more in every render pass than the scan saves.
                if (!urgent && position.Equals(m_ResidentSelectedPosition)
                    && rotation.Equals(m_ResidentSelectedRotation)) return;
                urgent = true;
            }
            if (!m_ResidentScanPending || urgent)
            {
                if (!urgent && (position - m_ResidentSelectedPosition).sqrMagnitude < 4f
                    && Quaternion.Angle(rotation, m_ResidentSelectedRotation) < 3f) return;
                m_ResidentScanPosition = position;
                m_ResidentScanRotation = rotation;
                m_ResidentScanProjection = projection;
                GeometryUtility.CalculateFrustumPlanes(projection * camera.worldToCameraMatrix, m_ResidentScanPlanes);
                m_ResidentSelectionCamera = camera;
                m_ResidentSectorCursor = m_ResidentCellCursor = 0;
                foreach (var band in m_ResidentPendingBands) band.Clear();
                m_ResidentScanPending = true;
                m_DetailRenderTick++;
            }
            if (!AdvanceResidentVisibilityScan(camera, urgent)) return;
            m_ResidentScanPending = false;
            m_ResidentSelectionValid = true;
            m_ResidentSelectionAmortized = m_AmortizeResidentVisibility;
            m_ResidentSelectedPosition = m_ResidentScanPosition;
            m_ResidentSelectedRotation = m_ResidentScanRotation;
            m_ResidentProjection = m_ResidentScanProjection;
            m_VisibleDensityDetails.Clear();
            foreach (var band in m_ResidentPendingBands) m_VisibleDensityDetails.AddRange(band);
            // Stable traversal within each LOD band replaces a full O(n log n) resort.
            m_ResidentSelectionOrdered = true;
            try { using (s_ResidentSubmitMarker.Auto()) DrawVisibleDensityDetails(camera); }
            finally { m_ResidentSelectionOrdered = false; }
        }

        bool AdvanceResidentVisibilityScan(Camera camera, bool urgent)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            long budgetTicks = (long)(System.Diagnostics.Stopwatch.Frequency * ResidentScanMilliseconds / 1000.0);
            using (s_ResidentScanMarker.Auto())
            {
                int work = 0;
                while (m_ResidentSectorCursor < m_FullResidentSectors.Count)
                {
                    // Also count rejected sectors so an empty view cannot bypass the budget.
                    if (!urgent && ++work % 32 == 0 && System.Diagnostics.Stopwatch.GetTimestamp() - started >= budgetTicks)
                        return false;
                    var sector = m_FullResidentSectors[m_ResidentSectorCursor];
                    if (m_ResidentCellCursor == 0)
                    {
                        float limit = sector.prototype.MaximumDrawDistance;
                        if (limit <= 0f) limit = float.MaxValue;
                        if (m_MaxDensityDetailDistance > 0f) limit = Mathf.Min(limit, m_MaxDensityDetailDistance);
                        if (m_AmortizeResidentVisibility) limit += ResidentPositionGuard;
                        m_ResidentSectorLimit = limit * limit;
                        if (!ResidentBoundsVisible(sector.bounds, m_ResidentSectorLimit))
                        { m_ResidentSectorCursor++; continue; }
                    }
                    if (m_ResidentCellCursor >= sector.cells.Count)
                    { m_ResidentCellCursor = 0; m_ResidentSectorCursor++; continue; }
                    var cell = sector.cells[m_ResidentCellCursor++];
                    LastResidentVisibilityCellsTested++;
                    if (!ResidentBoundsVisible(cell.bounds, m_ResidentSectorLimit)) continue;
                    // Keep the denser population on the approach side of a density boundary.
                    // Grass shaders still evaluate their fade using the live camera position.
                    float distance = Mathf.Max(0f, Mathf.Sqrt(DetailBoundsDistanceSquared(cell.bounds, m_ResidentScanPosition))
                        - (m_AmortizeResidentVisibility ? ResidentPositionGuard : 0f));
                    int lod = GetFixedDetailCellDensityLod(camera, sector.layer, cell.firstX, cell.firstZ, cell.cellSize, distance);
                    m_ResidentPendingBands[Mathf.Clamp(lod, 0, 2)].Add(new VisibleDensityDetail(cell.cachedChunk, sector.prototype, distance, lod));
                }
            }
            return true;
        }

        bool ResidentBoundsVisible(Bounds bounds, float squaredLimit)
        {
            if (DetailBoundsDistanceSquared(bounds, m_ResidentScanPosition) > squaredLimit) return false;
            if (!m_AmortizeResidentVisibility)
                return GeometryUtility.TestPlanesAABB(m_ResidentScanPlanes, bounds);
            float radius = bounds.extents.magnitude;
            float distance = Vector3.Distance(bounds.center, m_ResidentScanPosition);
            bounds.Expand(2f * (ResidentPositionGuard + (distance + radius) * 0.28f)); // tan(15 degrees), rounded up
            return GeometryUtility.TestPlanesAABB(m_ResidentScanPlanes, bounds);
        }
    }
}
