#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace MashBoxSDK.ContentTools
{
    [InitializeOnLoad]
    public static class SDKSceneInstaller
    {
        static SDKSceneInstaller()
        {
            EditorApplication.delayCall += ValidateHDRPLightProbeSystem;
        }

        public static void ValidateHDRPLightProbeSystem()
        {
            bool foundHDRP = false;

            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                var rpAsset = QualitySettings.GetRenderPipelineAssetAt(i);

                if (rpAsset is HDRenderPipelineAsset hdAsset)
                {
                    foundHDRP = true;

                    var settings = hdAsset.currentPlatformRenderPipelineSettings;

                    if (settings.lightProbeSystem != RenderPipelineSettings.LightProbeSystem.LegacyLightProbes)
                    {
                        settings.lightProbeSystem = RenderPipelineSettings.LightProbeSystem.LegacyLightProbes;

                        hdAsset.currentPlatformRenderPipelineSettings = settings;

                        EditorUtility.SetDirty(hdAsset);
                        AssetDatabase.SaveAssets();

                        Debug.LogWarning($"[MashBoxSDK] Quality '{QualitySettings.names[i]}' HDRP Asset was auto-set to 'Light Probe Groups'.");
                    }
                }
            }

            if (!foundHDRP)
            {
                Debug.Log("[MashBoxSDK] No HDRP assets found in Quality Settings.");
            }
        }
    }
}

#endif
