using System;
using System.Collections.Generic;
using UnityEngine;
namespace MashBoxSDK.Maps.Roads
{
    [DisallowMultipleComponent, AddComponentMenu("")]
    public sealed class MGRoadDetailLayers : MonoBehaviour
    {
#if UNITY_EDITOR
        [Serializable] public sealed class Cells
        {
            public int width, height;
            public int[] indices;
        }
        [Serializable] public sealed class Layer
        {
            public MGRoad road;
            public List<Cells> masks = new List<Cells>();
        }
        [HideInInspector] public List<Layer> layers = new List<Layer>();
#endif
    }
}