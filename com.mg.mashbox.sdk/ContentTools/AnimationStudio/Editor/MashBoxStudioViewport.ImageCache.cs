using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class MashBoxStudioViewport
    {
        private static readonly FieldInfo NativeSceneTarget = typeof(SceneView).GetField("m_SceneTargetTexture", WindowMembers);
        private const BindingFlags CacheStaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly PropertyInfo SceneBlitMaterial = typeof(EditorGUIUtility).GetProperty("GUITextureBlit2SRGBMaterial", CacheStaticMembers);
        private static readonly PropertyInfo OverlayBlitMaterial = typeof(EditorGUIUtility).GetProperty("GUITextureBlitSceneGUIMaterial", CacheStaticMembers);
        private RenderTexture cachedScene, cachedOverlay;
        private Material cachedSceneMaterial, cachedOverlayMaterial;
        private bool cacheHooks, capturingViewport, capturedSceneThisFrame, viewportCacheReady;
        private int cachedNearestControl;
        private CacheViewState cachedView;
        internal bool ViewportCacheReady => viewportCacheReady && cachedScene && cachedOverlay;
#if MASHBOX_STUDIO_TESTS
        internal int ViewportCachePresentCount { get; private set; }
#endif
#if MASHBOX_STUDIO_TESTS
        internal int ViewportCacheCaptureCount { get; private set; }
#endif
        internal bool ViewportImageReused { get; private set; }
        private bool UseViewportCache => panelsModeling && !StudioSettings.Current.disableViewportImageCache &&
            !EditorApplication.isPlayingOrWillChangePlaymode && NativeSceneTarget != null && SceneBlitMaterial != null && OverlayBlitMaterial != null;

        private struct CacheViewState
        {
            internal Vector3 pivot;
            internal Quaternion rotation;
            internal float size, pixels, fov, near, far;
            internal Rect viewport;
            internal bool ortho, dynamicClip, lighting, grid;
            internal DrawCameraMode mode;
            internal bool Same(CacheViewState other) => pivot.Equals(other.pivot) && rotation.Equals(other.rotation) &&
                size == other.size && pixels == other.pixels && fov == other.fov && near == other.near && far == other.far &&
                viewport.Equals(other.viewport) && ortho == other.ortho && dynamicClip == other.dynamicClip &&
                lighting == other.lighting && grid == other.grid && mode == other.mode;
        }
        private CacheViewState CurrentCacheView() => new CacheViewState {
            pivot = pivot, rotation = rotation, size = size, ortho = orthographic,
            pixels = EditorGUIUtility.pixelsPerPoint, viewport = cameraViewport,
            fov = cameraSettings.fieldOfView, near = cameraSettings.nearClip, far = cameraSettings.farClip,
            dynamicClip = cameraSettings.dynamicClip, mode = cameraMode.drawMode, lighting = sceneLighting, grid = modelGridVisible
        };

        private bool PresentUnchangedViewport()
        {
            if (!UseViewportCache) { ReleaseViewportImageCache(); return false; }
            if (!ViewportCacheReady || GUIUtility.hotControl != 0 || navigating ||
                (viewportDirty & (DirtyState.Scene | DirtyState.Camera | DirtyState.Overlay)) != 0 ||
                !cachedView.Same(CurrentCacheView()) || cachedNearestControl != HandleUtility.nearestControl) return false;
            // These are Unity's own scene and premultiplied-overlay compositing materials.
            // Keep their color conversions separate, exactly as the native SceneView does.
            var rect = cameraViewport;
            var uv = new Rect(0, 0, 1, 1);
            Graphics.DrawTexture(rect, cachedScene, uv, 0, 0, 0, 0, Color.white, cachedSceneMaterial);
            Graphics.DrawTexture(rect, cachedOverlay, uv, 0, 0, 0, 0, Color.white, cachedOverlayMaterial);
            viewportDirty = DirtyState.None;
            ViewportImageReused = true;
#if MASHBOX_STUDIO_TESTS
                ViewportCachePresentCount++;
#endif
            return true;
        }
        private void BeginViewportImageCapture()
        {
            capturedSceneThisFrame = false;
            ViewportImageReused = false;
            viewportCacheReady = false;
            // Active drags render natively without paying for image copies. Capture
            // once the interaction releases, ready for subsequent idle repaints.
            capturingViewport = UseViewportCache && GUIUtility.hotControl == 0 && !navigating;
            if (!capturingViewport) return;
            if (!cacheHooks)
            {
                RenderPipelineManager.endCameraRendering += CapturePipelineScene;
                Camera.onPostRender += CaptureBuiltinScene;
                cacheHooks = true;
                cachedSceneMaterial = SceneBlitMaterial.GetValue(null) as Material;
                cachedOverlayMaterial = OverlayBlitMaterial.GetValue(null) as Material;
            }
        }
        private void CapturePipelineScene(ScriptableRenderContext context, Camera renderedCamera) => CaptureViewportScene(renderedCamera);
        private void CaptureBuiltinScene(Camera renderedCamera)
        {
            if (!GraphicsSettings.currentRenderPipeline) CaptureViewportScene(renderedCamera);
        }
        private void CaptureViewportScene(Camera renderedCamera)
        {
            if (!capturingViewport || renderedCamera != camera) return;
            var source = NativeSceneTarget.GetValue(this) as RenderTexture;
            if (!source || !source.IsCreated()) return;
            CopyViewportLayer(source, ref cachedScene, "MashBox cached scene");
            capturedSceneThisFrame = true;
        }
        private void FinishViewportImageCapture()
        {
            try
            {
                if (!capturingViewport || !capturedSceneThisFrame || !cachedSceneMaterial || !cachedOverlayMaterial) return;
                var source = NativeSceneTarget.GetValue(this) as RenderTexture;
                if (!source || !source.IsCreated()) return;
                CopyViewportLayer(source, ref cachedOverlay, "MashBox cached overlays");
                cachedView = CurrentCacheView();
                cachedNearestControl = HandleUtility.nearestControl;
                viewportCacheReady = true;
#if MASHBOX_STUDIO_TESTS
                ViewportCacheCaptureCount++;
#endif
            }
            finally { capturingViewport = false; }
        }
        private static void CopyViewportLayer(RenderTexture source, ref RenderTexture destination, string name)
        {
            if (!destination || destination.width != source.width || destination.height != source.height || destination.graphicsFormat != source.graphicsFormat)
            {
                if (destination) DestroyImmediate(destination);
                var descriptor = source.descriptor;
                descriptor.depthBufferBits = 0; descriptor.msaaSamples = 1;
                descriptor.useMipMap = descriptor.autoGenerateMips = false;
                destination = new RenderTexture(descriptor) { name = name, hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
                destination.Create();
            }
            var active = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = destination.sRGB;
                Graphics.Blit(source, destination);
            }
            finally { GL.sRGBWrite = srgb; RenderTexture.active = active; }
        }
        private void ReleaseViewportImageCache()
        {
            if (cacheHooks)
            {
                RenderPipelineManager.endCameraRendering -= CapturePipelineScene;
                Camera.onPostRender -= CaptureBuiltinScene;
                cacheHooks = false;
            }
            if (cachedScene) DestroyImmediate(cachedScene);
            if (cachedOverlay) DestroyImmediate(cachedOverlay);
            cachedScene = cachedOverlay = null;
            cachedSceneMaterial = cachedOverlayMaterial = null;
            viewportCacheReady = capturingViewport = false;
            ViewportImageReused = false;
        }
    }
}

