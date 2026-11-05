#if UNITY_EDITOR && UNITY_6000_0_OR_NEWER
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    // Undo is global. Use its published object changes instead of invalidating
    // every terrain, every world tile, or every mesh ever sculpted this session.
    [InitializeOnLoad]
    internal static class MGTerrainUndoRefresh
    {
        static bool undoPending;
        static readonly HashSet<MGTerrain> surfaces = new HashSet<MGTerrain>();
        static readonly HashSet<MGTerrain> details = new HashSet<MGTerrain>();
        static readonly HashSet<Texture2D> maps = new HashSet<Texture2D>();

        static MGTerrainUndoRefresh()
        {
            Undo.undoRedoPerformed += () => undoPending = true;
            ObjectChangeEvents.changesPublished += OnChanges;
        }

        static void OnChanges(ref ObjectChangeEventStream stream)
        {
            if (!undoPending) return;
            undoPending = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            try
            {
                for (int i = 0; i < stream.length; i++)
                {
                    switch (stream.GetEventType(i))
                    {
                        case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                            stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var component);
                            Collect(EditorUtility.InstanceIDToObject(component.instanceId));
                            break;
                        case ObjectChangeKind.ChangeAssetObjectProperties:
                            stream.GetChangeAssetObjectPropertiesEvent(i, out var asset);
                            Collect(EditorUtility.InstanceIDToObject(asset.instanceId));
                            break;
                    }
                }
                foreach (var map in maps)
                {
                    // Native texture Undo restores CPU data; upload only maps in
                    // this replay, even when no terrain inspector remains open.
                    if (map.isReadable) map.Apply(false, false);
                    EditorUtility.SetDirty(map);
                }
                if (maps.Count > 0) MGTerrainEditor.ResetDensityPaintProgress();
                foreach (var terrain in surfaces)
                {
                    var mesh = terrain.MeshFilter != null ? terrain.MeshFilter.sharedMesh : null;
                    if (mesh != null)
                    {
                        if (mesh.isReadable) mesh.UploadMeshData(false);
                        EditorUtility.SetDirty(mesh);
                    }
                    terrain.NotifySurfaceMeshChanged(true);
                    terrain.RefreshSurfaceCollidersFromMesh(recordUndo: false);
                }
                foreach (var terrain in details) terrain.InvalidateRenderCache();
                if (surfaces.Count > 0 || details.Count > 0) SceneView.RepaintAll();
            }
            finally { surfaces.Clear(); details.Clear(); maps.Clear(); }
        }

        static void Collect(Object changed)
        {
            if (changed == null) return;
            if (changed is MGTerrain tile)
            {
                if (tile.isActiveAndEnabled && !EditorUtility.IsPersistent(tile)) details.Add(tile);
                return;
            }
            if (!(changed is Mesh) && !(changed is MeshFilter) && !(changed is Texture2D)) return;
            foreach (var terrain in MGTerrain.EditorActiveTerrains)
            {
                if (terrain == null || EditorUtility.IsPersistent(terrain)) continue;
                var filter = terrain.MeshFilter;
                if (filter != null && (changed == filter || changed == filter.sharedMesh))
                {
                    surfaces.Add(terrain);
                    details.Add(terrain);
                    continue;
                }
                if (changed is Texture2D map && UsesMap(terrain, map))
                {
                    maps.Add(map);
                    details.Add(terrain);
                }
            }
        }

        internal static bool UsesMap(MGTerrain terrain, Texture2D map)
        {
            if (map == null) return false;
            if (terrain.ControlMap1 == map || terrain.ControlMap2 == map) return true;
            foreach (var layer in terrain.DensityDetailLayers)
                if (layer != null && (layer.DensityMap == map || layer.SizeMap == map
                    || layer.HealthMap == map || layer.GrassIdMap == map || layer.PaletteSourceMap == map)) return true;
            foreach (var binding in terrain.DetailFoliagePalettes)
                if (binding != null && binding.SourceDensityMap == map) return true;
            return false;
        }
    }
}
#endif