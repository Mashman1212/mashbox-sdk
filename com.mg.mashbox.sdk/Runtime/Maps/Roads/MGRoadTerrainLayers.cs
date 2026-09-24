using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEngine;

namespace MashBoxSDK.Maps.Roads
{
    // Editor-only payload. The build processor removes this component;
    // the persistent terrain mesh is the complete playable result.
    [DisallowMultipleComponent, AddComponentMenu("")]
    public sealed class MGRoadTerrainLayers : MonoBehaviour
    {
#if UNITY_EDITOR
        [Serializable] public struct Sample
        {
            public int index;
            public float height, weight, smoothing;
        }
        [Serializable] public sealed class Layer
        {
            public MGRoad road;
            public string fingerprint, order;
            public RoadHeightMode heightMode;
            public Sample[] samples;
            public int smoothingPasses;
        }
        [HideInInspector] public MGTerrain terrain;
        [HideInInspector] public Mesh output;
        [HideInInspector] public Vector3[] baseline;
        [HideInInspector] public int topology;
        [HideInInspector] public List<Layer> layers = new List<Layer>();
#endif
    }
}
