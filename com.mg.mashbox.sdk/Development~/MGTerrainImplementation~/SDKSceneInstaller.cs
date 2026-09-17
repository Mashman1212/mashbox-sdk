#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
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
            // Both legacy probes and APVs are valid authoring choices. Never reset
            // a project's lighting backend when scripts reload or the editor opens.
            for (int i = 0; i < QualitySettings.names.Length; i++)
                if (QualitySettings.GetRenderPipelineAssetAt(i) is HDRenderPipelineAsset)
                    return;

            Debug.Log("[MashBoxSDK] No HDRP assets found in Quality Settings.");
        }
    }
}
#endif
