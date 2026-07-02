#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.Exporting
{
    [System.Serializable]
    public class MapBundleEntry
    {
        public SceneAsset scene;
        public string bundleName;
        public bool includeInBuild = true;
        public Texture2D screenshot;
    }
}

#endif