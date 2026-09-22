using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainSceneExposure
    {
        internal static bool TryGet(out float value, out string message)
        {
            value = 0;
            var camera = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : Camera.main;
            if (camera == null || !VolumeManager.instance.isInitialized)
            { message = "Open a Scene view with HDRP active to resolve scene exposure."; return false; }
            var hd = camera.GetComponent<HDAdditionalCameraData>();
            var stack = VolumeManager.instance.CreateStack();
            try
            {
                VolumeManager.instance.Update(stack, hd != null && hd.volumeAnchorOverride != null ? hd.volumeAnchorOverride : camera.transform,
                    hd != null ? hd.volumeLayerMask : (LayerMask)(-1));
                var exposure = stack.GetComponent<Exposure>();
                if (exposure == null || !exposure.active || !exposure.fixedExposure.overrideState || exposure.mode.value != ExposureMode.Fixed)
                { message = "Scene exposure is not Fixed. Use a fixed lighting-volume exposure or turn off Sync Exposure to Scene."; return false; }
                value = exposure.fixedExposure.value;
                message = "Scene exposure: " + value.ToString("0.##") + " EV100";
                return true;
            }
            catch (Exception e) { message = e.Message; return false; }
            finally { VolumeManager.instance.DestroyStack(stack); }
        }
        static double nextUpdate;
        static string cachedMessage;
        internal static string Description()
        {
            if (EditorApplication.timeSinceStartup >= nextUpdate || cachedMessage == null)
            { TryGet(out _, out cachedMessage); nextUpdate = EditorApplication.timeSinceStartup + .5; }
            return cachedMessage;
        }
    }
}
