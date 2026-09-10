#if UNITY_6000_0_OR_NEWER
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        [SerializeField] bool m_GpuDetailFrustumCulling;
        [SerializeField] bool m_GpuTerrainOcclusion;
        [SerializeField] bool m_GpuRenderedDepthOcclusion;
        [SerializeField, Min(0f)] float m_DetailOcclusionPadding = 1f;
        [StructLayout(LayoutKind.Sequential)]
        struct OcclusionDraw
        {
            internal Vector3 center;
            internal uint offset;
            internal Vector3 extents;
            internal float padding;
        }
        readonly List<OcclusionDraw> m_OcclusionDraws = new List<OcclusionDraw>();
        readonly Plane[] m_OcclusionPlanes = new Plane[6];
        readonly Vector4[] m_OcclusionPlaneVectors = new Vector4[6];
        GraphicsBuffer m_OcclusionDrawBuffer, m_OcclusionIndices, m_OcclusionArgs;
        Texture2D m_OcclusionHeightMin;
        bool m_OcclusionReady, m_OcclusionDirty = true;
        bool m_OcclusionFailed;
        int m_OcclusionKernel = -1, m_OcclusionResetKernel = -1;
        Camera m_OcclusionCamera;
        Matrix4x4 m_LastOcclusionView, m_LastOcclusionTransform;
        Vector3 m_LastOcclusionPosition;
        float m_LastOcclusionPadding;
        bool m_LastOcclusionFrustum, m_LastOcclusionTerrain;
        static readonly Unity.Profiling.ProfilerMarker s_TerrainOcclusion = new Unity.Profiling.ProfilerMarker("MGTerrain.GPUCulling");
        public bool IsGpuDetailCullingActive => m_OcclusionReady;
        string m_TerrainOcclusionStatus = "Waiting for gameplay camera";
        public string TerrainOcclusionStatus => m_GpuTerrainOcclusion ? m_TerrainOcclusionStatus : "Off";

        void ReleaseTerrainOcclusion()
        {
            m_OcclusionReady = false;
            m_OcclusionFailed = false;
            m_OcclusionDirty = true;
            m_OcclusionCamera = null;
            m_RenderedDepthFrame = -1;
            m_OcclusionDrawBuffer?.Dispose(); m_OcclusionDrawBuffer = null;
            m_OcclusionIndices?.Dispose(); m_OcclusionIndices = null;
            m_OcclusionArgs?.Dispose(); m_OcclusionArgs = null;
            if (m_OcclusionHeightMin != null)
            {
                if (Application.isPlaying) Destroy(m_OcclusionHeightMin); else DestroyImmediate(m_OcclusionHeightMin);
                m_OcclusionHeightMin = null;
            }
        }

        static void EnsureOcclusionBuffer(ref GraphicsBuffer buffer, GraphicsBuffer.Target target, int count, int stride)
        {
            if (buffer != null && buffer.count >= count) return;
            buffer?.Dispose();
            buffer = new GraphicsBuffer(target, Mathf.NextPowerOfTwo(Mathf.Max(64, count)), stride);
        }

        // Minima, not averages: each mip texel is a lower bound on the solid terrain
        // over its entire footprint. Padding outside the mesh can never occlude.
        bool BuildOcclusionHeightMin()
        {
            if (m_OcclusionHeightMin != null) return true;
            var mesh = MeshFilter != null ? MeshFilter.sharedMesh : null;
            if (mesh == null || !mesh.isReadable) return false;
            int width = m_DetailGpuSurfaceWidth, height = m_DetailGpuSurfaceHeight;
            var vertices = m_CachedDetailSurfaceVertices;
            if (vertices == null || vertices.Length != width * height || width < 2 || height < 2) return false;
            // Only fully covered grid quads may occlude. A missing triangle is an
            // opening, even if the other half of the quad remains visible.
            var coverage = new byte[(width - 1) * (height - 1)];
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != UnityEngine.MeshTopology.Triangles) return false;
                int[] triangles = mesh.GetTriangles(s);
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    int ax = a % width, az = a / width, bx = b % width, bz = b / width, cx = c % width, cz = c / width;
                    int x = Mathf.Min(ax, Mathf.Min(bx, cx)), z = Mathf.Min(az, Mathf.Min(bz, cz));
                    if (Mathf.Max(ax, Mathf.Max(bx, cx)) - x != 1 || Mathf.Max(az, Mathf.Max(bz, cz)) - z != 1) return false;
                    int corners = (1 << (ax - x + (az - z) * 2)) | (1 << (bx - x + (bz - z) * 2)) | (1 << (cx - x + (cz - z) * 2));
                    int missing = 15 ^ corners;
                    if (missing != 1 && missing != 2 && missing != 4 && missing != 8) return false;
                    coverage[z * (width - 1) + x] |= (byte)missing;
                }
            }
            int w = Mathf.NextPowerOfTwo(width - 1), h = Mathf.NextPowerOfTwo(height - 1);
            if (w > SystemInfo.maxTextureSize || h > SystemInfo.maxTextureSize) return false;
            var texture = new Texture2D(w, h, TextureFormat.RFloat, true, true)
                { name = "MG Terrain conservative height minima", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            try
            {
                float[] values = new float[w * h];
                for (int z = 0; z < h; z++) for (int x = 0; x < w; x++)
                {
                    float value = -1e20f;
                    byte covered = x < width - 1 && z < height - 1 ? coverage[z * (width - 1) + x] : (byte)0;
                    if ((covered & 9) == 9 || (covered & 6) == 6)
                    {
                        int i = z * width + x;
                        value = Mathf.Min(Mathf.Min(vertices[i].y, vertices[i + 1].y),
                            Mathf.Min(vertices[i + width].y, vertices[i + width + 1].y));
                    }
                    values[z * w + x] = value;
                }
                int mip = 0;
                while (true)
                {
                    texture.SetPixelData(values, mip++);
                    if (w == 1 && h == 1) break;
                    int nw = Mathf.Max(1, w / 2), nh = Mathf.Max(1, h / 2);
                    var next = new float[nw * nh];
                    for (int z = 0; z < nh; z++) for (int x = 0; x < nw; x++)
                    {
                        int x0 = x * 2, x1 = Mathf.Min(x0 + 1, w - 1);
                        int z0 = z * 2, z1 = Mathf.Min(z0 + 1, h - 1);
                        next[z * nw + x] = Mathf.Min(Mathf.Min(values[z0 * w + x0], values[z0 * w + x1]),
                            Mathf.Min(values[z1 * w + x0], values[z1 * w + x1]));
                    }
                    values = next; w = nw; h = nh;
                }
                texture.Apply(false, true);
                m_OcclusionHeightMin = texture;
                return true;
            }
            catch { if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture); throw; }
        }

        void UpdateTerrainOcclusion(Camera camera)
        {
            if (camera != m_CachedGameplayCamera) return;
            m_TerrainOcclusionStatus = "Bypassed: requires a perspective gameplay camera and ready resident indirect draws";
            bool hadResult = m_OcclusionReady;
            m_OcclusionReady = false;
            if ((!m_GpuDetailFrustumCulling && !m_GpuTerrainOcclusion && !m_GpuRenderedDepthOcclusion) || m_OcclusionFailed
                || !Application.isPlaying || (!UsesWorldBudget && (!KeepAllDetailCellsResident || !m_FullResidentReady))
                || !m_DetailBrgUsesGpuGeneration
                || !m_IndirectDetailDrawsReady || m_DetailBrgVisibleCount == 0
                || m_AppearanceCaptureCamera != null || camera.stereoEnabled || camera.orthographic) return;
            using var profile = s_TerrainOcclusion.Auto();
            try
            {
                var surfaceRenderer = MeshRenderer;
                bool surfaceVisible = surfaceRenderer != null && surfaceRenderer.enabled
                    && !(m_MasterRenderingSuppressed ? m_MasterWasForceRenderingOff : surfaceRenderer.forceRenderingOff);
                bool terrainOcclusion = m_GpuTerrainOcclusion && surfaceVisible && BuildOcclusionHeightMin();
                m_TerrainOcclusionStatus = terrainOcclusion ? "Active (hole-aware)"
                    : !surfaceVisible ? "Bypassed: terrain surface hidden" : "Bypassed: terrain height grid unavailable or unsupported";
                if (!m_GpuDetailFrustumCulling && !terrainOcclusion && !m_GpuRenderedDepthOcclusion) return;
                Matrix4x4 view = camera.nonJitteredProjectionMatrix * camera.worldToCameraMatrix;
                Matrix4x4 terrainTransform = transform.localToWorldMatrix;
                Vector3 cameraPosition = camera.transform.position;
                if (!m_GpuRenderedDepthOcclusion && hadResult && !m_OcclusionDirty && camera == m_OcclusionCamera
                    && view == m_LastOcclusionView && terrainTransform == m_LastOcclusionTransform
                    && cameraPosition.Equals(m_LastOcclusionPosition)
                    && m_DetailOcclusionPadding == m_LastOcclusionPadding
                    && m_GpuDetailFrustumCulling == m_LastOcclusionFrustum && terrainOcclusion == m_LastOcclusionTerrain)
                { m_OcclusionReady = true; return; }
                if (m_OcclusionKernel < 0)
                {
                    m_OcclusionKernel = m_DetailGpuGenerator.FindKernel("CullTerrainDetails");
                    m_OcclusionResetKernel = m_DetailGpuGenerator.FindKernel("ResetCulledArgs");
                }
                EnsureOcclusionBuffer(ref m_OcclusionDrawBuffer, GraphicsBuffer.Target.Structured, m_OcclusionDraws.Count, 32);
                EnsureOcclusionBuffer(ref m_OcclusionIndices, GraphicsBuffer.Target.Raw, m_DetailBrgVisibleCount, 4);
                EnsureOcclusionBuffer(ref m_OcclusionArgs, GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments, m_IndirectArguments.Count, 4);
                if (m_OcclusionDirty)
                {
                    m_OcclusionDrawBuffer.SetData(m_OcclusionDraws);
                    m_OcclusionDirty = false;
                }
                var shader = m_DetailGpuGenerator;
                int kernel = m_OcclusionKernel;
                shader.SetInt("_OcclusionDrawCount", m_OcclusionDraws.Count);
                shader.SetBuffer(m_OcclusionResetKernel, "_SourceArgs", m_IndirectArgs);
                shader.SetBuffer(m_OcclusionResetKernel, "_CulledArgs", m_OcclusionArgs);
                shader.Dispatch(m_OcclusionResetKernel, (m_OcclusionDraws.Count + 63) / 64, 1, 1);
                shader.SetBuffer(kernel, "_OcclusionDraws", m_OcclusionDrawBuffer);
                shader.SetBuffer(kernel, "_VisibilityRanges", m_IndirectVisibilityInput);
                shader.SetBuffer(kernel, "_InstanceData", m_DetailBrgInstanceBuffer);
                shader.SetBuffer(kernel, "_CulledIndices", m_OcclusionIndices);
                shader.SetBuffer(kernel, "_CulledArgs", m_OcclusionArgs);
                shader.SetInt("_ObjectToWorldAddress", BrgZeroPrefixBytes);
                shader.SetInt("_CullDetailFrustum", m_GpuDetailFrustumCulling ? 1 : 0);
                shader.SetInt("_CullTerrainOcclusion", terrainOcclusion ? 1 : 0);
                shader.SetInt("_CullRenderedDepth", 0);
                shader.SetTexture(kernel, "_RenderedDepthPyramid", Texture2D.whiteTexture);
                shader.SetTexture(kernel, "_TerrainHeightMin", terrainOcclusion ? m_OcclusionHeightMin : Texture2D.blackTexture);
                shader.SetMatrix("_OcclusionWorldToLocal", transform.worldToLocalMatrix);
                shader.SetVector("_OcclusionCameraLocal", transform.InverseTransformPoint(camera.transform.position));
                // Row lengths bound a world-space displacement in terrain-local axes.
                Matrix4x4 inverse = transform.worldToLocalMatrix;
                float padding = Mathf.Max(0f, m_DetailOcclusionPadding);
                shader.SetVector("_OcclusionPadding", new Vector4(((Vector3)inverse.GetRow(0)).magnitude * padding,
                    ((Vector3)inverse.GetRow(1)).magnitude * padding, ((Vector3)inverse.GetRow(2)).magnitude * padding, 0));
                Bounds surface = MeshFilter.sharedMesh.bounds;
                shader.SetVector("_OcclusionSurface", new Vector4(surface.min.x, surface.min.z,
                    (m_DetailGpuSurfaceWidth - 1) / surface.size.x, (m_DetailGpuSurfaceHeight - 1) / surface.size.z));
                shader.SetVector("_OcclusionGrid", new Vector4(m_DetailGpuSurfaceWidth - 1, m_DetailGpuSurfaceHeight - 1,
                    terrainOcclusion ? m_OcclusionHeightMin.mipmapCount - 1 : 0, 0));
                GeometryUtility.CalculateFrustumPlanes(camera.nonJitteredProjectionMatrix * camera.worldToCameraMatrix * transform.localToWorldMatrix, m_OcclusionPlanes);
                for (int i = 0; i < 6; i++)
                { Plane p = m_OcclusionPlanes[i]; m_OcclusionPlaneVectors[i] = new Vector4(p.normal.x, p.normal.y, p.normal.z, p.distance); }
                shader.SetVectorArray("_OcclusionPlanes", m_OcclusionPlaneVectors);
                for (int start = 0; start < m_IndirectVisibilityRanges.Count; start += 65535)
                {
                    shader.SetInt("_VisibilityRangeOffset", start);
                    shader.Dispatch(kernel, Mathf.Min(65535, m_IndirectVisibilityRanges.Count - start), 1, 1);
                }
                m_OcclusionCamera = camera;
                m_LastOcclusionView = view; m_LastOcclusionTransform = terrainTransform;
                m_LastOcclusionPosition = cameraPosition; m_LastOcclusionPadding = m_DetailOcclusionPadding;
                m_LastOcclusionFrustum = m_GpuDetailFrustumCulling; m_LastOcclusionTerrain = terrainOcclusion;
                m_OcclusionReady = true;
                // Initialize the camera buffers every frame before HDRP's culling callback.
                // The optional integration replaces them later in the same frame, on GPU.
                if (m_GpuRenderedDepthOcclusion) RenderedDepthRequested?.Invoke(this, camera);
            }
            catch (Exception exception)
            {
                m_OcclusionFailed = true;
                m_TerrainOcclusionStatus = "Disabled after GPU culling error; see Console";
                Debug.LogWarning($"MG Terrain optional GPU culling disabled: {exception.Message}", this);
            }
        }
    }
}
#endif
