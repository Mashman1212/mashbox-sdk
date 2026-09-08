#if UNITY_6000_0_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        // Pipeline adapter lives in an optional HDRP assembly, keeping the core SDK portable.
        public static event Action<MGTerrain, Camera> RenderedDepthRequested;
        int m_RenderedDepthFrame = -1;
        string m_GpuCullingMeasurement = "Not measured";
        public string GpuCullingMeasurement => m_GpuCullingMeasurement;
        public void MeasureGpuCulling()
        {
            if (!Application.isPlaying || !m_OcclusionReady || m_IndirectArgs == null || m_OcclusionArgs == null)
            { m_GpuCullingMeasurement = "No active GPU culling result"; return; }
            int count = m_OcclusionDraws.Count;
            m_GpuCullingMeasurement = "Reading GPU counters…";
            long before = -1, after = -1;
            bool failed = false;
            Action<AsyncGPUReadbackRequest, bool> done = (request, original) =>
            {
                if (this == null) return;
                if (request.hasError) { failed = true; m_GpuCullingMeasurement = "Readback unavailable; try again"; return; }
                long total = 0; var data = request.GetData<uint>();
                for (int i = 0; i < count; i++) total += data[i * 5 + 1];
                if (original) before = total; else after = total;
                if (!failed && before >= 0 && after >= 0)
                    m_GpuCullingMeasurement = $"{before:N0} → {after:N0} mesh instances ({Math.Max(0, before - after):N0} culled)";
            };
            AsyncGPUReadback.Request(m_IndirectArgs, count * 5 * sizeof(uint), 0, request => done(request, true));
            AsyncGPUReadback.Request(m_OcclusionArgs, count * 5 * sizeof(uint), 0, request => done(request, false));
        }
        public bool WantsRenderedDepthOcclusion => isActiveAndEnabled && m_GpuRenderedDepthOcclusion;
        public bool CanRecordRenderedDepth(Camera camera) => WantsRenderedDepthOcclusion && m_OcclusionReady
            && camera == m_OcclusionCamera && !camera.stereoEnabled && !camera.orthographic;
        public string RenderedDepthStatus => !m_GpuRenderedDepthOcclusion ? "Off"
            : m_RenderedDepthFrame >= Time.frameCount - 1 && m_RenderedDepthFrame >= 0 ? "Active: current-frame scene depth"
            : "Waiting / bypassed: requires HDRP custom passes and resident indirect draws";

        public void RecordRenderedDepthCulling(CommandBuffer cmd, Camera camera, Texture pyramid,
            Matrix4x4 worldToClip, int width, int height, int mipCount, bool uvStartsAtTop, bool reversedZ, bool minusOneToOneDepth)
        {
            if (!CanRecordRenderedDepth(camera)) return;
            var shader = m_DetailGpuGenerator;
            int kernel = m_OcclusionKernel;
            cmd.BeginSample("MGTerrain.RenderedDepth.Cull");
            cmd.SetComputeIntParam(shader, "_OcclusionDrawCount", m_OcclusionDraws.Count);
            cmd.SetComputeBufferParam(shader, m_OcclusionResetKernel, "_SourceArgs", m_IndirectArgs);
            cmd.SetComputeBufferParam(shader, m_OcclusionResetKernel, "_CulledArgs", m_OcclusionArgs);
            cmd.DispatchCompute(shader, m_OcclusionResetKernel, (m_OcclusionDraws.Count + 63) / 64, 1, 1);
            cmd.SetComputeBufferParam(shader, kernel, "_OcclusionDraws", m_OcclusionDrawBuffer);
            cmd.SetComputeBufferParam(shader, kernel, "_VisibilityRanges", m_IndirectVisibilityInput);
            cmd.SetComputeBufferParam(shader, kernel, "_InstanceData", m_DetailBrgInstanceBuffer);
            cmd.SetComputeBufferParam(shader, kernel, "_CulledIndices", m_OcclusionIndices);
            cmd.SetComputeBufferParam(shader, kernel, "_CulledArgs", m_OcclusionArgs);
            cmd.SetComputeIntParam(shader, "_ObjectToWorldAddress", BrgZeroPrefixBytes);
            cmd.SetComputeIntParam(shader, "_CullDetailFrustum", m_LastOcclusionFrustum ? 1 : 0);
            cmd.SetComputeIntParam(shader, "_CullTerrainOcclusion", m_LastOcclusionTerrain ? 1 : 0);
            cmd.SetComputeTextureParam(shader, kernel, "_TerrainHeightMin", m_LastOcclusionTerrain ? m_OcclusionHeightMin : Texture2D.blackTexture);
            cmd.SetComputeMatrixParam(shader, "_OcclusionWorldToLocal", transform.worldToLocalMatrix);
            cmd.SetComputeVectorParam(shader, "_OcclusionCameraLocal", transform.InverseTransformPoint(camera.transform.position));
            Matrix4x4 inverse = transform.worldToLocalMatrix;
            float padding = Mathf.Max(0f, m_DetailOcclusionPadding);
            cmd.SetComputeVectorParam(shader, "_OcclusionPadding", new Vector4(((Vector3)inverse.GetRow(0)).magnitude * padding,
                ((Vector3)inverse.GetRow(1)).magnitude * padding, ((Vector3)inverse.GetRow(2)).magnitude * padding, 0));
            Bounds surface = MeshFilter.sharedMesh.bounds;
            cmd.SetComputeVectorParam(shader, "_OcclusionSurface", new Vector4(surface.min.x, surface.min.z,
                (m_DetailGpuSurfaceWidth - 1) / surface.size.x, (m_DetailGpuSurfaceHeight - 1) / surface.size.z));
            cmd.SetComputeVectorParam(shader, "_OcclusionGrid", new Vector4(m_DetailGpuSurfaceWidth - 1, m_DetailGpuSurfaceHeight - 1,
                m_LastOcclusionTerrain ? m_OcclusionHeightMin.mipmapCount - 1 : 0, 0));
            cmd.SetComputeVectorArrayParam(shader, "_OcclusionPlanes", m_OcclusionPlaneVectors);
            cmd.SetComputeIntParam(shader, "_CullRenderedDepth", 1);
            cmd.SetComputeTextureParam(shader, kernel, "_RenderedDepthPyramid", pyramid);
            cmd.SetComputeMatrixParam(shader, "_RenderedDepthLocalToClip", worldToClip * transform.localToWorldMatrix);
            cmd.SetComputeVectorParam(shader, "_RenderedDepthSize", new Vector4(width, height, mipCount - 1, reversedZ ? 1 : 0));
            cmd.SetComputeVectorParam(shader, "_RenderedDepthConvention", new Vector4(0.5f, uvStartsAtTop ? -0.5f : 0.5f,
                minusOneToOneDepth ? 0.5f : 1f, minusOneToOneDepth ? 0.5f : 0f));
            for (int start = 0; start < m_IndirectVisibilityRanges.Count; start += 65535)
            {
                cmd.SetComputeIntParam(shader, "_VisibilityRangeOffset", start);
                cmd.DispatchCompute(shader, kernel, Mathf.Min(65535, m_IndirectVisibilityRanges.Count - start), 1, 1);
            }
            cmd.EndSample("MGTerrain.RenderedDepth.Cull");
            m_RenderedDepthFrame = Time.frameCount;
        }
    }
}
#endif
