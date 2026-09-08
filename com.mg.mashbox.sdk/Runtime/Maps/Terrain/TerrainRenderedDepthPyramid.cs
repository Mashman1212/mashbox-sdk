using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.Maps.TerrainSystem
{
    // Shared by the HDRP adapter and GPU correctness tests.
    public sealed class TerrainRenderedDepthPyramid : IDisposable
    {
        RenderTexture m_Pyramid, m_Scratch;
        ComputeShader m_Shader;
        int m_Copy, m_Reduce;
        public RenderTexture Texture => m_Pyramid;

        public void Record(CommandBuffer cmd, Texture depth, int width, int height, bool reversedZ)
        {
            int w = Mathf.NextPowerOfTwo(width), h = Mathf.NextPowerOfTwo(height);
            if (w > SystemInfo.maxTextureSize || h > SystemInfo.maxTextureSize) throw new NotSupportedException("Depth viewport exceeds texture limits.");
            if (m_Pyramid == null || m_Pyramid.width != w || m_Pyramid.height != h)
            {
                Dispose();
                m_Pyramid = Allocate(w, h, true, "MG Scene Depth Pyramid");
                m_Scratch = Allocate(w, h, false, "MG Scene Depth Reduction");
            }
            if (m_Shader == null)
            {
                m_Shader = Resources.Load<ComputeShader>("MGTerrainRenderedDepth");
                if (m_Shader == null) throw new InvalidOperationException("MG rendered depth compute shader is missing.");
                m_Copy = m_Shader.FindKernel("CopySceneDepth"); m_Reduce = m_Shader.FindKernel("ReduceSceneDepth");
            }
            cmd.BeginSample("MGTerrain.RenderedDepth.Pyramid");
            cmd.SetComputeIntParam(m_Shader, "_DepthReversed", reversedZ ? 1 : 0);
            cmd.SetComputeVectorParam(m_Shader, "_DepthSizes", new Vector4(width, height, w, h));
            cmd.SetComputeTextureParam(m_Shader, m_Copy, "_DepthInput", depth);
            cmd.SetComputeTextureParam(m_Shader, m_Copy, "_DepthOutput", m_Pyramid, 0);
            cmd.DispatchCompute(m_Shader, m_Copy, (w + 7) / 8, (h + 7) / 8, 1);
            for (int mip = 1; mip < m_Pyramid.mipmapCount; mip++)
            {
                int nw = Mathf.Max(1, w / 2), nh = Mathf.Max(1, h / 2);
                cmd.SetComputeIntParam(m_Shader, "_DepthSourceMip", mip - 1);
                cmd.SetComputeVectorParam(m_Shader, "_DepthSizes", new Vector4(w, h, nw, nh));
                cmd.SetComputeTextureParam(m_Shader, m_Reduce, "_DepthReduceInput", m_Pyramid);
                cmd.SetComputeTextureParam(m_Shader, m_Reduce, "_DepthOutput", m_Scratch);
                cmd.DispatchCompute(m_Shader, m_Reduce, (nw + 7) / 8, (nh + 7) / 8, 1);
                // Separate scratch avoids overlapping SRV/UAV bindings on the pyramid.
                cmd.CopyTexture(m_Scratch, 0, 0, 0, 0, nw, nh, m_Pyramid, 0, mip, 0, 0);
                w = nw; h = nh;
            }
            cmd.EndSample("MGTerrain.RenderedDepth.Pyramid");
        }

        static RenderTexture Allocate(int width, int height, bool mips, string name)
        {
            var texture = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
            { name = name, hideFlags = HideFlags.HideAndDontSave, enableRandomWrite = true, useMipMap = mips,
                autoGenerateMips = false, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            if (!texture.Create()) { UnityEngine.Object.DestroyImmediate(texture); throw new InvalidOperationException("Could not allocate rendered depth texture."); }
            return texture;
        }

        public void Dispose()
        {
            Release(ref m_Pyramid); Release(ref m_Scratch);
        }
        static void Release(ref RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture); else UnityEngine.Object.DestroyImmediate(texture);
            texture = null;
        }
    }
}
