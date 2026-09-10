#if UNITY_EDITOR
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainEditor
    {
        readonly Dictionary<MGTerrain, MGTerrainEditor> m_WorldPaintEditors = new Dictionary<MGTerrain, MGTerrainEditor>();
        readonly HashSet<MGTerrain> m_WorldPaintSkipped = new HashSet<MGTerrain>();
        bool m_WorldPaintDelegate;

        bool RaycastDetailWorld(MGTerrain terrain, Ray ray, out RaycastHit hit)
        {
            if (terrain.World != null) return terrain.World.RaycastSurface(ray, out hit, out _, float.MaxValue);
            return terrain.RaycastSurface(ray, out hit, float.MaxValue);
        }

        internal static int FindWorldPaintLayer(MGTerrain source, int sourceIndex, MGTerrain destination)
        {
            if ((uint)sourceIndex >= source.DensityDetailLayerCount) return -1;
            var a = source.DensityDetailLayers[sourceIndex];
            if ((uint)a.PrototypeIndex >= source.Prototypes.Count) return -1;
            var p = source.Prototypes[a.PrototypeIndex];
            int match = -1;
            for (int index = 0; index < destination.DensityDetailLayerCount; index++)
            {
                var b = destination.DensityDetailLayers[index];
                if (b == null || b.PaletteSourceOnly || (uint)b.PrototypeIndex >= destination.Prototypes.Count) continue;
                var q = destination.Prototypes[b.PrototypeIndex];
                if (q == null || p == null || p.Kind != q.Kind || p.Prefab != q.Prefab || p.Mesh != q.Mesh || p.Material != q.Material
                    || a.GeneratedByPalette != b.GeneratedByPalette || a.PaletteEntryIndex != b.PaletteEntryIndex
                    || a.UsesGrassArray != b.UsesGrassArray || (a.GrassIdMap != null) != (b.GrassIdMap != null)
                      || (a.GrassIdMap != null ? a.GrassPopulation != b.GrassPopulation : a.TextureSlice != b.TextureSlice)) continue;
                // Duplicate definitions are ambiguous; never guess a destination painted layer.
                if (match >= 0) return -1;
                match = index;
            }
            return match;
        }

        void PaintWorldNeighbours(MGTerrain terrain, Vector3 point, bool erase)
        {
            if (m_WorldPaintDelegate || terrain.World == null) return;
            foreach (var chunk in terrain.World.Chunks)
            {
                if (chunk == null || chunk == terrain || !chunk.isActiveAndEnabled || chunk.MeshFilter == null
                    || chunk.MeshFilter.sharedMesh == null) continue;
                var bounds = chunk.MeshFilter.sharedMesh.bounds;
                Vector3 local = chunk.transform.InverseTransformPoint(point);
                // Brush falloff operates on the local XZ plane, matching the existing detail brush.
                Vector3 nearest = new Vector3(Mathf.Clamp(local.x, bounds.min.x, bounds.max.x), local.y,
                    Mathf.Clamp(local.z, bounds.min.z, bounds.max.z));
                if (chunk.transform.TransformVector(local - nearest).sqrMagnitude >= MBEditorToolState.BrushRadius * MBEditorToolState.BrushRadius) continue;
                if (!m_WorldPaintEditors.TryGetValue(chunk, out var editor))
                {
                    int index = FindWorldPaintLayer(terrain, m_PaintDetailIndex, chunk);
                    if (index < 0)
                    {
                        if (m_WorldPaintSkipped.Add(chunk)) Debug.LogWarning($"Skipped cross-chunk paint on '{chunk.name}': no unique matching detail definition. Match the chunk's detail setup before painting across this border.", chunk);
                        continue;
                    }
                    editor = (MGTerrainEditor)CreateEditor(chunk, typeof(MGTerrainEditor));
                    editor.m_WorldPaintDelegate = true;
                    editor.m_PaintDetailIndex = index;
                    editor.m_GrassPaintSubId = m_GrassPaintSubId;
                    editor.m_GrassIdOnly = m_GrassIdOnly;
                    editor.m_GrassPaintPopulation = m_GrassPaintPopulation;
                    editor.m_PaintChannel = m_PaintChannel; editor.m_PaintDensity = m_PaintDensity;
                    editor.m_PaintSize = m_PaintSize; editor.m_RandomPaintSize = m_RandomPaintSize;
                    editor.m_PaintSizeMin = m_PaintSizeMin; editor.m_PaintSizeMax = m_PaintSizeMax;
                    editor.m_PaintSizeSeed = m_PaintSizeSeed;
                    m_WorldPaintEditors.Add(chunk, editor);
                    if (!editor.BeginDetailStroke(chunk)) continue;
                }
                editor.PaintDetailDab(chunk, point, erase);
            }
        }

        void FinishWorldPaintStroke()
        {
            if (m_WorldPaintDelegate) return;
            Tool tool = Tools.current;
            bool editing = MBEditorToolState.ActiveEditing;
            foreach (var editor in m_WorldPaintEditors.Values)
                if (editor != null) { editor.FinishDetailStroke(); DestroyImmediate(editor); }
            Tools.current = tool; MBEditorToolState.ActiveEditing = editing;
            m_WorldPaintEditors.Clear(); m_WorldPaintSkipped.Clear();
        }
    }
}
#endif
