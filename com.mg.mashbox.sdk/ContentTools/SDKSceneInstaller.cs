#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace MashBoxSDK.ContentTools
{
    [InitializeOnLoad]
    public static class SDKSceneInstaller
    {
        private const ReflectionProbeTextureCacheResolution ReflectionAtlasSize =
            ReflectionProbeTextureCacheResolution.Resolution8192x4096;
        private static double nextValidationTime;

        static SDKSceneInstaller()
        {
            EditorApplication.delayCall += ValidateHDRPLightProbeSystem;
            EditorApplication.update += EnforceReflectionAtlasSize;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void ValidateHDRPLightProbeSystem()
        {
            // Both legacy probes and APVs are valid authoring choices. Never reset
            // a project's lighting backend when scripts reload or the editor opens.
            bool found = ValidateReflectionAtlasSizes();
            if (!found)
                Debug.Log("[MashBoxSDK] No HDRP assets found in Quality Settings or Graphics Settings.");
        }

        private static void EnforceReflectionAtlasSize()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.timeSinceStartup < nextValidationTime)
                return;

            nextValidationTime = EditorApplication.timeSinceStartup + 1.0;
            ValidateReflectionAtlasSizes();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
                ValidateReflectionAtlasSizes();
        }

        internal static bool ValidateReflectionAtlasSizes()
        {
            // Quality levels with no override inherit the default pipeline.
            bool found = EnforceReflectionAtlasSize(GraphicsSettings.defaultRenderPipeline);
            for (int i = 0; i < QualitySettings.names.Length; i++)
                found |= EnforceReflectionAtlasSize(QualitySettings.GetRenderPipelineAssetAt(i));
            return found;
        }

        private static bool EnforceReflectionAtlasSize(RenderPipelineAsset pipeline)
        {
            if (!(pipeline is HDRenderPipelineAsset hdrp))
                return false;

            if (hdrp.currentPlatformRenderPipelineSettings.lightLoopSettings.reflectionProbeTexCacheSize == ReflectionAtlasSize)
                return true;

            // Apply through serialization so HDRP validates and refreshes the asset.
            using (var serialized = new SerializedObject(hdrp))
            {
                var atlasSize = serialized.FindProperty(
                    "m_RenderPipelineSettings.lightLoopSettings.reflectionProbeTexCacheSize");
                atlasSize.intValue = (int)ReflectionAtlasSize;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssetIfDirty(hdrp);
            Debug.Log($"[MashBoxSDK] Set Reflection 2D Atlas Size to 8192x4096 on '{hdrp.name}'.", hdrp);
            return true;
        }
    }

    internal sealed class SDKReflectionAtlasBuildProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            SDKSceneInstaller.ValidateReflectionAtlasSizes();
        }
    }
}
#endif
