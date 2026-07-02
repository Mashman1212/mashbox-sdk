#if UNITY_EDITOR

using System.Collections.Generic;
using UnityEditor;

namespace MashBoxSDK.Exporting
{
    public class MapExportContext
    {
        public string OutputPath;
        public List<MapBundleEntry> Maps;
        public BuildTarget UnityTarget;
    }
}

#endif