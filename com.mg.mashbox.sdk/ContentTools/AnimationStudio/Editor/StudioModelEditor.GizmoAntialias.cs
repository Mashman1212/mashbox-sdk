using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private RenderTexture gizmoAaInput, gizmoAaOutput;
        private Material gizmoAaMaterial;
        private void ReleaseGizmoAntialias()
        {
            if (gizmoAaInput) Object.DestroyImmediate(gizmoAaInput);
            if (gizmoAaOutput) Object.DestroyImmediate(gizmoAaOutput);
            if (gizmoAaMaterial) Object.DestroyImmediate(gizmoAaMaterial);
            gizmoAaInput = gizmoAaOutput = null; gizmoAaMaterial = null;
        }
        private void AntialiasTransformGizmo(Vector3 center, float radius)
        {
            if (Event.current.type != EventType.Repaint || !(SceneView.currentDrawingSceneView is MashBoxStudioViewport view)) return;
            var target = RenderTexture.active; var camera = view.camera;
            if (!target || !camera || target.antiAliasing != 1 || target.dimension != TextureDimension.Tex2D ||
                (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0) return;
            var point = camera.WorldToViewportPoint(center);
            if (point.z <= 0) return;
            var side = camera.WorldToViewportPoint(center + camera.transform.right * radius);
            int pixels = Mathf.CeilToInt(Mathf.Abs(side.x-point.x)*target.width)+4;
            int x = Mathf.Clamp(Mathf.FloorToInt(point.x*target.width)-pixels,0,target.width);
            int y = Mathf.Clamp(Mathf.FloorToInt(point.y*target.height)-pixels,0,target.height);
            int width = Mathf.Clamp(Mathf.CeilToInt(point.x*target.width)+pixels,0,target.width)-x;
            int height = Mathf.Clamp(Mathf.CeilToInt(point.y*target.height)+pixels,0,target.height)-y;
            if (width <= 0 || height <= 0) return;
            if (!gizmoAaMaterial)
            {
                var shader = Shader.Find("Hidden/MashBox/GizmoAntialias");
                if (!shader || !shader.isSupported) return;
                gizmoAaMaterial = new Material(shader) { hideFlags=HideFlags.HideAndDontSave };
            }
            if (!gizmoAaInput || gizmoAaInput.width < width || gizmoAaInput.height < height || gizmoAaInput.graphicsFormat != target.graphicsFormat)
            {
                if (gizmoAaInput) Object.DestroyImmediate(gizmoAaInput);
                if (gizmoAaOutput) Object.DestroyImmediate(gizmoAaOutput);
                var descriptor = target.descriptor;
                descriptor.width = Mathf.NextPowerOfTwo(width); descriptor.height = Mathf.NextPowerOfTwo(height);
                descriptor.depthBufferBits = 0; descriptor.msaaSamples = 1;
                descriptor.useMipMap = descriptor.autoGenerateMips = false;
                gizmoAaInput = new RenderTexture(descriptor) { name="Studio gizmo AA input",hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp };
                gizmoAaOutput = new RenderTexture(descriptor) { name="Studio gizmo AA output",hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp };
                gizmoAaInput.Create(); gizmoAaOutput.Create();
            }
            bool srgb = GL.sRGBWrite;
            GL.PushMatrix();
            try
            {
                RenderTexture.active = gizmoAaInput; GL.Clear(false,true,Color.clear);
                Graphics.SetRenderTarget(null);
                Graphics.CopyTexture(target,0,0,x,y,width,height,gizmoAaInput,0,0,0,0);
                GL.sRGBWrite = gizmoAaOutput.sRGB;
                Graphics.Blit(gizmoAaInput,gizmoAaOutput,gizmoAaMaterial);
                Graphics.SetRenderTarget(null);
                Graphics.CopyTexture(gizmoAaOutput,0,0,0,0,width,height,target,0,0,x,y);
            }
            finally { GL.sRGBWrite = srgb; RenderTexture.active = target; GL.PopMatrix(); }
        }
    }
}
