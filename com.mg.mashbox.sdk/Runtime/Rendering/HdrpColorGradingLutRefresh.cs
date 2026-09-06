#if UNITY_6000_0_OR_NEWER
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace MashBoxSDK.Rendering
{
    /// <summary>
    /// Works around HDRP 17.4 retaining a stale color-grading LUT when Volume
    /// parameters change. Changing an HDRP asset recreates this cache, which is
    /// why the new grading values otherwise appear only after touching the asset.
    /// </summary>
    internal static class HdrpColorGradingLutRefresh
    {
        private const string LutHashFieldName = "m_LutHash";

        private static readonly BindingFlags InstanceFieldFlags =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private static FieldInfo lutHashField;
        private static bool missingFieldReported;
        private static int invalidHash = int.MinValue;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeForPlayer()
        {
            lutHashField = null;
            missingFieldReported = false;
            invalidHash = int.MinValue;
            Install();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeForEditor()
        {
            Install();
        }
#endif

        private static void Install()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCameraRendering;
            RenderPipelineManager.beginCameraRendering += BeforeCameraRendering;
        }

        private static void BeforeCameraRendering(ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (!(RenderPipelineManager.currentPipeline is HDRenderPipeline pipeline))
                return;

            if (lutHashField == null)
                lutHashField = typeof(HDRenderPipeline).GetField(LutHashFieldName, InstanceFieldFlags);

            if (lutHashField == null)
            {
                if (!missingFieldReported)
                {
                    missingFieldReported = true;
                    Debug.LogWarning(
                        "HDRP color-grading LUT refresh workaround could not find the HDRP LUT cache. " +
                        "Check whether the installed HDRP version still requires this workaround.");
                }

                return;
            }

            // Alternate sentinels so the cache cannot remain valid if a grading
            // hash happens to equal one of them.
            invalidHash = invalidHash == int.MinValue ? int.MaxValue : int.MinValue;
            lutHashField.SetValue(pipeline, invalidHash);
        }
    }
}

#endif
