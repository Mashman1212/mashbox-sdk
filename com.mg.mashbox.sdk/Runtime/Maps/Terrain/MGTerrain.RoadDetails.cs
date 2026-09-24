using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        // Final, scene-serialized masks. Gameplay reads these directly: no road evaluation,
        // authoring history, texture duplication or modification of painted density maps.
        [Serializable]
        public sealed class DetailExclusionMask
        {
            public int width, height;
            public byte[] cells;
        }
        [SerializeField, HideInInspector] List<DetailExclusionMask> m_RoadDetailMasks = new List<DetailExclusionMask>();

        public bool IsRoadDetailCellCleared(int width, int height, int x, int z)
        {
            if (m_RoadDetailMasks == null) return false;
            foreach (var mask in m_RoadDetailMasks)
                if (mask.width == width && mask.height == height && mask.cells != null
                    && (uint)x < width && (uint)z < height && z * width + x < mask.cells.Length)
                    return mask.cells[z * width + x] != 0;
            return false;
        }

        // Editor authoring API. Refresh just the cells whose union mask changed.
        public void SetRoadDetailMasks(List<DetailExclusionMask> masks)
        {
            Rect dirty = default;
            bool changed = false;
            void Compare(DetailExclusionMask a, DetailExclusionMask b)
            {
                var source = a ?? b;
                for (int i = 0; i < source.width * source.height; i++)
                {
                    byte oldValue = a != null && a.cells != null && i < a.cells.Length ? a.cells[i] : (byte)0;
                    byte newValue = b != null && b.cells != null && i < b.cells.Length ? b.cells[i] : (byte)0;
                    if (oldValue == newValue) continue;
                    int x = i % source.width, z = i / source.width;
                    var cell = Rect.MinMaxRect(x / (float)source.width, z / (float)source.height,
                        (x + 1f) / source.width, (z + 1f) / source.height);
                    dirty = changed ? Rect.MinMaxRect(Mathf.Min(dirty.xMin, cell.xMin), Mathf.Min(dirty.yMin, cell.yMin),
                        Mathf.Max(dirty.xMax, cell.xMax), Mathf.Max(dirty.yMax, cell.yMax)) : cell;
                    changed = true;
                }
            }
            if (m_RoadDetailMasks != null)
                foreach (var old in m_RoadDetailMasks)
                    Compare(old, masks.Find(m => m.width == old.width && m.height == old.height));
            foreach (var next in masks)
                if (m_RoadDetailMasks == null || !m_RoadDetailMasks.Exists(m => m.width == next.width && m.height == next.height)) Compare(null, next);
            m_RoadDetailMasks = masks;
            if (changed)
                for (int i = 0; i < m_DensityDetailLayers.Count; i++)
                {
                    var map = m_DensityDetailLayers[i]?.DensityMap;
                    if (map == null) continue;
                    if (map.isReadable && map.format == TextureFormat.R16) RefreshDetailPaintRegion(i, dirty);
                    else InvalidateDetailRenderCache();
                }
        }
        public void RefreshRoadDetailMasks() => InvalidateDetailRenderCache();
    }
}