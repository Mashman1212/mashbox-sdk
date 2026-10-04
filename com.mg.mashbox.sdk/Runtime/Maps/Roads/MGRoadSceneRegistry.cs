#if UNITY_EDITOR
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.Maps.Roads
{
    // Live editor objects register from their lifecycle callbacks. Serialized
    // layer stores and mesh owners remain registered while disabled, until
    // destruction/unload, so restore, bake and mesh isolation still see them.
    public static class MGRoadSceneRegistry
    {
        static readonly HashSet<MGRoad> roads = new HashSet<MGRoad>();
        static readonly HashSet<MGRoadTerrainLayers> terrainLayers = new HashSet<MGRoadTerrainLayers>();
        static readonly HashSet<MGRoadDetailLayers> detailLayers = new HashSet<MGRoadDetailLayers>();
        static readonly HashSet<MGTerrain> terrains = new HashSet<MGTerrain>();
        public static IReadOnlyCollection<MGRoad> Roads => roads;
        public static IReadOnlyCollection<MGRoadTerrainLayers> TerrainLayers => terrainLayers;
        public static IReadOnlyCollection<MGRoadDetailLayers> DetailLayers => detailLayers;
        public static IReadOnlyCollection<MGTerrain> Terrains => terrains;
        public static int Revision { get; private set; }
        public static bool HasLayerWork => roads.Count != 0 || terrainLayers.Count != 0;
        public static bool HasDetailWork => roads.Count != 0 || detailLayers.Count != 0;
        public static void NotifyChanged() { unchecked { Revision++; } }
        public static void Register(MGRoad value) { if (value && value.isActiveAndEnabled && roads.Add(value)) NotifyChanged(); }
        public static void Unregister(MGRoad value) { if (roads.Remove(value)) NotifyChanged(); }
        public static void Register(MGRoadTerrainLayers value) { if (value && terrainLayers.Add(value)) NotifyChanged(); }
        public static void Unregister(MGRoadTerrainLayers value) { if (terrainLayers.Remove(value)) NotifyChanged(); }
        public static void Register(MGRoadDetailLayers value) { if (value && detailLayers.Add(value)) NotifyChanged(); }
        public static void Unregister(MGRoadDetailLayers value) { if (detailLayers.Remove(value)) NotifyChanged(); }
        public static void Register(MGTerrain value) { if (value && terrains.Add(value)) NotifyChanged(); }
        public static void Unregister(MGTerrain value) { if (terrains.Remove(value)) NotifyChanged(); }
        public static void RemoveScene(Scene scene)
        {
            roads.RemoveWhere(v => !v || v.gameObject.scene == scene);
            terrainLayers.RemoveWhere(v => !v || v.gameObject.scene == scene);
            detailLayers.RemoveWhere(v => !v || v.gameObject.scene == scene);
            terrains.RemoveWhere(v => !v || v.gameObject.scene == scene);
            NotifyChanged();
        }
    }
}
#endif
