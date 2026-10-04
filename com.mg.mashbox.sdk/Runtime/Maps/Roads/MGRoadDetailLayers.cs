using System;
using System.Collections.Generic;
using UnityEngine;
namespace MashBoxSDK.Maps.Roads
{
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("")]
    public sealed class MGRoadDetailLayers : MonoBehaviour
    {
#if UNITY_EDITOR
        void OnEnable() => MGRoadSceneRegistry.Register(this);
        void OnDestroy() => MGRoadSceneRegistry.Unregister(this);
        void OnValidate() => MGRoadSceneRegistry.NotifyChanged();
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