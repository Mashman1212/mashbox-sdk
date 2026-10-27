using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // Probe anchors are Unity renderer settings, so persist the export policy in
    // importer metadata and apply it whenever Unity rebuilds the FBX model.
    internal sealed class StudioSkinningProbeAnchors : AssetPostprocessor
    {
        private const string Marker = "MashBoxSkinningRootProbeAnchor:1";

        internal static bool Configure(ModelImporter importer)
        {
            string data = importer.userData ?? string.Empty;
            if (HasMarker(data)) return false;
            importer.userData = data + (data.Length > 0 && !data.EndsWith("\n", StringComparison.Ordinal) ? "\n" : string.Empty) + Marker;
            return true;
        }

        private static bool HasMarker(string data) =>
            (data ?? string.Empty).Split('\n').Any(line => line.TrimEnd('\r') == Marker);

        public override uint GetVersion() => 1;

        private void OnPostprocessModel(GameObject root)
        {
            if (!HasMarker(assetImporter.userData)) return;
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                renderer.probeAnchor = root.transform;
        }
    }
}
