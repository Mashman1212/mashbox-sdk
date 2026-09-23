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
        [Tooltip("Road clearance above terrain when following it.")] public float clearance = .05f;
        [Tooltip("Terrain height relative to the road surface.")] public float terrainOffset = -.05f;
        [Min(0)] public float falloffDistance = 5;
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
