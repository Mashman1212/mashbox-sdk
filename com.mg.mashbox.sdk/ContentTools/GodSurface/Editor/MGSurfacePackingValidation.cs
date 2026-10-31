using System;
using System.Text;
using UnityEngine;

namespace MG.GodSurface.Editor
{
    public static class MGSurfacePackingValidation
    {
        public static string Run(Material source)
        {
            var log = new StringBuilder();
            var a = new Material(source); var b = new Material(source);
            var x = new Texture2D(4, 4); var y = new Texture2D(4, 4);
            var target = Shader.Find(MGSurfaceLibrary.ShaderName);
            void Check(bool pass, string message)
            { if (!pass) throw new InvalidOperationException(message); log.AppendLine("PASS " + message); }
            bool Same() => MGSurfaceBaker.RenderKey(a, target) == MGSurfaceBaker.RenderKey(b, target);
            try
            {
                a.SetTexture("_BaseColorMap", x); b.SetTexture("_BaseColorMap", y);
                x.wrapMode = y.wrapMode = TextureWrapMode.Repeat;
                x.filterMode = FilterMode.Bilinear; x.anisoLevel = 1; x.mipMapBias = -1;
                y.filterMode = FilterMode.Trilinear; y.anisoLevel = 10; y.mipMapBias = 0;
                Check(Same(), "Different source filtering shares one bank under the baked sampling policy");
                y.wrapModeU = TextureWrapMode.Clamp;
                Check(!Same(), "Different texture addressing remains separate"); y.wrapModeU = TextureWrapMode.Repeat;
                a.SetFloat("_DoubleSidedEnable", 0); b.SetFloat("_DoubleSidedEnable", 0);
                a.SetFloat("_DoubleSidedNormalMode", 1); b.SetFloat("_DoubleSidedNormalMode", 2);
                a.SetVector("_DoubleSidedConstants", Vector4.one); b.SetVector("_DoubleSidedConstants", -Vector4.one);
                a.SetFloat("_AlphaCutoffEnable", 0); b.SetFloat("_AlphaCutoffEnable", 0);
                a.SetFloat("_UseShadowThreshold", 0); b.SetFloat("_UseShadowThreshold", 1);
                Check(Same(), "Inactive double-sided and alpha shadow options do not fragment banks");
                a.SetFloat("_DoubleSidedEnable", 1); b.SetFloat("_DoubleSidedEnable", 1);
                Check(!Same(), "Active double-sided normal settings remain separate");
                a.SetFloat("_DoubleSidedEnable", 0); b.SetFloat("_DoubleSidedEnable", 0);
                a.SetFloat("_AlphaCutoffEnable", 1); b.SetFloat("_AlphaCutoffEnable", 1);
                Check(!Same(), "Active alpha shadow settings remain separate");
                a.SetFloat("_AlphaCutoffEnable", 0); b.SetFloat("_AlphaCutoffEnable", 0);
                b.renderQueue = a.renderQueue + 1;
                Check(!Same(), "Different render queues remain separate"); b.renderQueue = a.renderQueue;
                b.SetFloat("_SurfaceType", a.GetFloat("_SurfaceType") == 0 ? 1 : 0);
                Check(!Same(), "Opaque and transparent materials remain separate");
                b.SetFloat("_SurfaceType", a.GetFloat("_SurfaceType"));
                b.SetFloat("_CullMode", a.GetFloat("_CullMode") == 0 ? 2 : 0);
                Check(!Same(), "Different face culling remains separate");
                log.AppendLine("SUCCESS"); return log.ToString();
            }
            finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); UnityEngine.Object.DestroyImmediate(x); UnityEngine.Object.DestroyImmediate(y); }
        }
    }
}
