using UnityEngine.Scripting.APIUpdating;
using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEngine;

namespace MashBoxSDK.Maps.Roads
{
    [MovedFrom(true, "MappyX.Roads", "Assembly-CSharp", null)]
    public enum RoadTerrainMode { Independent, RoadFollowsTerrain, TerrainFollowsRoad }
    [MovedFrom(true, "MappyX.Roads", "Assembly-CSharp", null)]
    public enum RoadHeightMode { RaiseAndLower, LowerOnly, RaiseOnly }

    [Serializable]
    [MovedFrom(true, "MappyX.Roads", "Assembly-CSharp", null)]
    public class RoadTerrainSettings
    {
        public RoadTerrainMode mode;
        [Tooltip("In edit mode, replace the non-destructive terrain layer after releasing a road edit. Gameplay uses the saved terrain mesh.")] public bool autoApplyTerrain;
        // Retained for serialized compatibility. Road Apply always includes terrain-cell support.
        [HideInInspector] public bool matchTerrainCells = true;
        [Tooltip("Road clearance above terrain when following it.")] public float clearance = .05f;
        [Tooltip("Terrain height relative to the road surface.")] public float terrainOffset = -.05f;
        [Tooltip("Blend distance measured beyond the road, its supporting terrain cells, and one extra full-height cell. The roadbed inside that margin receives full strength.")] [Min(0)] public float falloffDistance = 5;
        [Tooltip("Average heights only in the transition. The full-height roadbed stays fixed.")] public bool smoothFalloff = true;
        [Range(0, 1)] public float falloffSmoothing = .65f;
        [Range(1, 12)] public int falloffSmoothingPasses = 4;
        [Tooltip("When smoothing is enabled, widen short falloffs to at least this many local terrain-cell spans. Set zero to use only Falloff Distance.")] [Range(0, 8)] public float minimumFalloffCells = 6;
        public AnimationCurve falloff = AnimationCurve.EaseInOut(0, 1, 1, 0);
        [Range(0, 1)] public float strength = 1;
        public RoadHeightMode heightMode;
    }

    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("MashBox/Maps/Road Network")]
    [MovedFrom(true, "MappyX.Roads", "Assembly-CSharp", null)]
    public sealed class MGRoadNetwork : MonoBehaviour
    {
        public MGTerrainWorld terrainWorld;
        public RoadTerrainSettings terrain = new RoadTerrainSettings();
        public Material defaultRoadMaterial;
        public Material defaultShoulderMaterial;
        [Min(.1f)] public float defaultWidth = 6;
        bool dirty;
        public MGRoad[] Roads
        {
            get
            {
                var roads = new List<MGRoad>();
                foreach (var road in GetComponentsInChildren<MGRoad>(true)) if (road.Network == this) roads.Add(road);
                return roads.ToArray();
            }
        }
        public void Rebuild()
        {
            foreach (var road in Roads) if (road.Network == this) road.RequestRebuild();
        }
        void OnValidate() { dirty = true; }
        void Update() { if (dirty) { dirty = false; Rebuild(); } }
    }
}
