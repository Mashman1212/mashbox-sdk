using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainEditor
    {
        bool m_WorldDetailControls;

        internal static int FindWorldFloodLayer(MGTerrain source, int index, MGTerrain tile)
        {
            if (source == null || tile == null || (uint)index >= source.DensityDetailLayerCount) return -1;
            if (source == tile) return index;
            var a = source.DensityDetailLayers[index];
            if (!string.IsNullOrEmpty(a.WorldDetailId))
            {
                for (int n = 0; n < tile.DensityDetailLayerCount; n++)
                    if (tile.DensityDetailLayers[n].WorldDetailId == a.WorldDetailId) return n;
            }
            if (a == null || (uint)a.PrototypeIndex >= source.Prototypes.Count) return -1;
            var p = source.Prototypes[a.PrototypeIndex];
            int match = -1;
            for (int i = 0; i < tile.DensityDetailLayerCount; i++)
            {
                var b = tile.DensityDetailLayers[i];
                if (b == null || b.PaletteSourceOnly || (uint)b.PrototypeIndex >= tile.Prototypes.Count) continue;
                var q = tile.Prototypes[b.PrototypeIndex];
                // Flood detaches palette provenance, so it is not part of detail identity.
                if (p == null || q == null || p.Kind != q.Kind || p.Prefab != q.Prefab
                    || p.Mesh != q.Mesh || p.Material != q.Material || a.UsesGrassArray != b.UsesGrassArray
                    || (a.GrassIdMap != null) != (b.GrassIdMap != null)
                    || (a.GrassIdMap != null ? a.GrassPopulation != b.GrassPopulation : a.TextureSlice != b.TextureSlice)) continue;
                if (match >= 0) return -1;
                match = i;
            }
            return match;
        }

        void FloodWorldDetail(MGTerrain source, int index, bool onlyThisDetail)
        {
            var targets = new List<(MGTerrain tile, int layer)>();
            var invalid = new List<string>();
            foreach (var tile in source.World.GetComponentsInChildren<MGTerrain>(true))
            {
                if (tile.GetComponentInParent<MGTerrainWorld>() != source.World) continue;
                int layer = FindWorldFloodLayer(source, index, tile);
                if (layer < 0) invalid.Add(tile.name);
                else targets.Add((tile, layer));
            }
            if (invalid.Count > 0)
            {
                EditorUtility.DisplayDialog("Cannot Flood World Detail",
                    "These tiles need a matching detail layer before flooding:\n\n"
                    + string.Join("\n", invalid) + "\n\nNo tiles were changed.", "OK");
                return;
            }
            if (targets.Count == 0) return;
            if (!EditorUtility.DisplayDialog("Flood World Detail?",
                $"Flood the matching detail on all {targets.Count} tiles (including inactive tiles) at {m_FloodDensity:N0} per texel?\n\n"
                + (onlyThisDetail ? "Other density layers will be removed on every tile. Their texture assets are kept.\n\n" : "Other detail layers are preserved.\n\n")
                + "Each tile receives its own new density map. Undo restores all tile assignments. Rendering budgets still apply.", "Flood World", "Cancel")) return;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Flood World Detail");
            try
            {
                foreach (var target in targets)
                {
                    var editor = (MGTerrainEditor)CreateEditor(target.tile);
                    try
                    {
                        editor.m_FloodDensity = m_FloodDensity;
                        editor.FloodDensityLayer(target.tile, target.layer, onlyThisDetail, true);
                    }
                    finally { DestroyImmediate(editor); }
                }
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
            finally { Undo.CollapseUndoOperations(group); serializedObject.Update(); SceneView.RepaintAll(); }
        }
    }
}
