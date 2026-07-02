#if UNITY_EDITOR

using System.IO;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.SDKMain
{
    public class SampleExporter
    {
#if MashBoxDev
        private static readonly string[] SourceCandidates =
        {
            @"D:\MashBoxSDK\Assets\MashBoxSamples",
            @"D:\BMXStreets\Assets\MashBoxSamples"
        };

        private static readonly string[] DestinationCandidates =
        {
            @"D:\mashbox-sdk-samples\com.mg.mashbox.samples\Samples~",
            @"D:\mashbox-sdk\com.mg.mashbox.sdk\Samples~"
        };

        [MenuItem("MashBox/Dev/Export Samples")]
        public static void ExportSamples()
        {
            var source = FindExistingDirectory(SourceCandidates);
            if (string.IsNullOrEmpty(source))
            {
                Debug.LogError("[MashBoxSDK] Could not find a MashBox samples source folder.");
                return;
            }

            var destination = FindExistingDirectory(DestinationCandidates);
            if (string.IsNullOrEmpty(destination))
                destination = DestinationCandidates[0];

            if (Directory.Exists(destination))
                Directory.Delete(destination, true);

            EnsureParentDirectoryExists(destination);
            FileUtil.CopyFileOrDirectory(source, destination);

            AssetDatabase.Refresh();
            Debug.Log($"[MashBoxSDK] Samples exported to: {destination}");
        }

        private static string FindExistingDirectory(string[] candidates)
        {
            for (var index = 0; index < candidates.Length; index++)
            {
                var candidate = candidates[index];
                if (Directory.Exists(candidate))
                    return candidate;
            }

            return string.Empty;
        }

        private static void EnsureParentDirectoryExists(string path)
        {
            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent))
                return;

            if (!Directory.Exists(parent))
                Directory.CreateDirectory(parent);
        }
#endif
    }
}

#endif
