#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
namespace MashBoxSDK.Exporting
{
    /// <summary>Runs scene preparation before Unity collects bundle dependencies.</summary>
    public static class MGBundleBuildPreparation
    {
        public static event Action<string[]> BeforeBuild;
        static bool preparing;
        public static void PrepareAssignedBundles() => Prepare(AssetDatabase.GetAllAssetBundleNames()
            .SelectMany(AssetDatabase.GetAssetPathsFromAssetBundle).ToArray());
        public static void Prepare(string[] assetPaths)
        {
            if (preparing) throw new InvalidOperationException("Recursive bundle preparation is not supported.");
            var scenes = (assetPaths ?? Array.Empty<string>()).Where(p => !string.IsNullOrEmpty(p) && p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (scenes.Length == 0) return;
            preparing = true;
            try { BeforeBuild?.Invoke(scenes); }
            finally { preparing = false; }
        }
    }
}
#endif
