using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering.RendererUtils;

namespace MashBoxSDK.Rendering
{
    // Created only when an enabled terrain requests scene-depth culling.
    public sealed class MGTerrainRenderedDepthHDRP : MonoBehaviour
    {
        static MGTerrainRenderedDepthHDRP s_Instance;
        readonly List<MGTerrain> m_Terrains = new List<MGTerrain>();
        CustomPassVolume m_Volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            MGTerrain.RenderedDepthRequested -= Request;
            MGTerrain.RenderedDepthRequested += Request;
            if (s_Instance != null) Destroy(s_Instance.gameObject);
            s_Instance = null;
        }
        static void Request(MGTerrain terrain, Camera camera)
        {
            if (!(RenderPipelineManager.currentPipeline is HDRenderPipeline)) return;
            if (s_Instance == null)
            {
                var root = new GameObject("MG Terrain Rendered Depth") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(root);
                s_Instance = root.AddComponent<MGTerrainRenderedDepthHDRP>();
                s_Instance.m_Volume = root.AddComponent<CustomPassVolume>();
                s_Instance.m_Volume.isGlobal = true;
                s_Instance.m_Volume.injectionPoint = CustomPassInjectionPoint.BeforeRendering;
                s_Instance.m_Volume.customPasses.Add(new SceneDepthPass { owner = s_Instance, name = "MG Terrain Scene Depth", targetColorBuffer = CustomPass.TargetBuffer.None, targetDepthBuffer = CustomPass.TargetBuffer.None });
            }
            if (!s_Instance.m_Terrains.Contains(terrain)) s_Instance.m_Terrains.Add(terrain);
        }
        void LateUpdate()
        {
            for (int i = m_Terrains.Count - 1; i >= 0; i--)
                if (m_Terrains[i] == null || !m_Terrains[i].WantsRenderedDepthOcclusion) m_Terrains.RemoveAt(i);
            if (m_Terrains.Count == 0) { m_Volume.enabled = false; Destroy(gameObject); }
        }
        void OnDestroy() { if (s_Instance == this) s_Instance = null; }

        [Serializable]
        sealed class SceneDepthPass : CustomPass
        {
            [NonSerialized] internal MGTerrainRenderedDepthHDRP owner;
            RenderTexture depth;
            TerrainRenderedDepthPyramid pyramid;
            bool failed;
            [NonSerialized] ShaderTagId[] m_Tags;
            protected override bool executeInSceneView => false;
            protected override void Execute(CustomPassContext ctx)
            {
                if (failed || owner == null || ctx.hdCamera.camera.stereoEnabled || ctx.hdCamera.camera.orthographic) return;
                Camera camera = ctx.hdCamera.camera;
                bool needed = false;
                foreach (var terrain in owner.m_Terrains)
                    if (terrain != null && terrain.CanRecordRenderedDepth(camera)) { needed = true; break; }
                if (!needed) return;
                try
                {
                    // Unity may construct serialized custom-pass types on its loading thread.
                    // ShaderTagId invokes native TagToID, so initialize only during rendering.
                    m_Tags ??= new[] { new ShaderTagId("DepthOnly"), new ShaderTagId("DepthForwardOnly") };
                    int width = ctx.hdCamera.actualWidth, height = ctx.hdCamera.actualHeight;
                    if (depth == null || depth.width != width || depth.height != height)
                    {
                        ReleaseDepth();
                        depth = new RenderTexture(width, height, 24, RenderTextureFormat.Depth)
                        { name = "MG Current Scene Occluders", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
                        if (!depth.Create()) throw new InvalidOperationException("Could not allocate scene occluder depth.");
                    }
                    ctx.cmd.BeginSample("MGTerrain.RenderedDepth.Occluders");
                    ctx.cmd.SetRenderTarget(depth);
                    ctx.cmd.SetViewport(new Rect(0, 0, width, height));
                    ctx.cmd.ClearRenderTarget(true, false, Color.clear, 1f);
                    var list = new RendererListDesc(m_Tags, ctx.cameraCullingResults, camera)
                    {
                        renderQueueRange = RenderQueueRange.opaque,
                        sortingCriteria = SortingCriteria.CommonOpaque,
                        layerMask = camera.cullingMask,
                        // Native material depth passes preserve alpha clipping. No shader override.
                        batchLayerMask = ~(1u << MGTerrain.DetailBatchLayer),
                        rendererConfiguration = PerObjectData.None
                    };
                    CoreUtils.DrawRendererList(ctx.cmd, ctx.renderContext.CreateRendererList(list));
                    ctx.cmd.EndSample("MGTerrain.RenderedDepth.Occluders");
                    pyramid ??= new TerrainRenderedDepthPyramid();
                    pyramid.Record(ctx.cmd, depth, width, height, SystemInfo.usesReversedZBuffer);
                    Matrix4x4 worldToClip = ctx.hdCamera.mainViewConstants.viewProjMatrix;
                    if (ShaderConfig.s_CameraRelativeRendering != 0)
                        worldToClip *= Matrix4x4.Translate(-ctx.hdCamera.mainViewConstants.worldSpaceCameraPos);
                    bool glDepth = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore
                        || SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3;
                    foreach (var terrain in owner.m_Terrains)
                        if (terrain != null) terrain.RecordRenderedDepthCulling(ctx.cmd, camera, pyramid.Texture,
                            worldToClip, width, height, pyramid.Texture.mipmapCount, SystemInfo.graphicsUVStartsAtTop,
                            SystemInfo.usesReversedZBuffer, glDepth);
                }
                catch (Exception e)
                {
                    failed = true;
                    Debug.LogWarning($"MG rendered depth occlusion bypassed: {e.Message}");
                }
                finally
                {
                    CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ctx.cameraDepthBuffer);
                    ctx.cmd.SetViewport(new Rect(0, 0, ctx.hdCamera.actualWidth, ctx.hdCamera.actualHeight));
                }
            }
            void ReleaseDepth()
            {
                if (depth == null) return;
                depth.Release(); CoreUtils.Destroy(depth); depth = null;
            }
            protected override void Cleanup() { ReleaseDepth(); pyramid?.Dispose(); pyramid = null; }
        }
    }
}

