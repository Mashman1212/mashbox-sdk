using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    // Rasterize original prefab triangles directly into the destination map resolution.
    // Each map remains GPU resident for the complete stroke; read back only once.
    internal sealed class PrefabStampProjection : IDisposable
    {
        readonly PrefabStampSource source;
        readonly Material[] materials;
        Mesh shape;
        Vector4 lastShape;
        bool lastInvert;
        public PrefabStampProjection(PrefabStampSource source, bool normals)
        {
            this.source = source;
            var shader = Shader.Find("Hidden/MashBox/PrefabStampProjection");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("GPU stamp projection shader is unavailable. Let Unity finish importing.");
            materials = new Material[source.Materials.Length];
            try
            {
                for(int s=0;s<materials.Length;s++)
                {
                    var original=source.Materials[s];
                    bool hd=original.shader.name=="HDRP/Lit" || original.shader.name=="HDRP/LitTessellation";
                    bool urp=original.shader.name=="Universal Render Pipeline/Lit";
                    bool standard=original.shader.name=="Standard" || original.shader.name=="Standard (Specular setup)";
                    if(!hd && !urp && !standard) throw new InvalidOperationException("Unsupported stamp material shader: "+original.shader.name);
                    if(hd && (original.GetFloat("_UVBase")!=0 || original.GetFloat("_NormalMapSpace")!=0 || original.GetTexture("_DetailMap")!=null))
                        throw new InvalidOperationException("Stamp transfer needs UV0, tangent-space normals and no detail-map layering: "+original.name);
                    string baseName=hd?"_BaseColorMap":urp?"_BaseMap":"_MainTex";
                    var baseMap=original.GetTexture(baseName);
                    var normalMap=normals?original.GetTexture(hd?"_NormalMap":"_BumpMap"):null;
                    if((baseMap!=null || normalMap!=null) && !source.Mesh.HasVertexAttribute(VertexAttribute.TexCoord0))
                        throw new InvalidOperationException("Stamp texture transfer requires UV0.");
                    var m=new Material(shader){hideFlags=HideFlags.HideAndDontSave}; materials[s]=m;
                    m.SetTexture("_BaseMap",baseMap!=null?baseMap:Texture2D.whiteTexture);
                    m.SetColor("_Tint",original.GetColor(hd||urp?"_BaseColor":"_Color"));
                    var scale=original.GetTextureScale(baseName); var offset=original.GetTextureOffset(baseName);
                    m.SetVector("_BaseST",new Vector4(scale.x,scale.y,offset.x,offset.y));
                    m.SetTexture("_NormalMap",normalMap); m.SetFloat("_HasNormal",normalMap!=null?1:0);
                    m.SetFloat("_NormalScale",original.GetFloat(hd?"_NormalScale":"_BumpScale"));
                    m.SetFloat("_Cutoff",original.IsKeywordEnabled("_ALPHATEST_ON")?original.GetFloat(hd?"_AlphaCutoff":"_Cutoff"):-1);
                }
            }
            catch {Dispose(); throw;}
        }
        public void SetShape(PrefabStampAppearance.Dab dab)
        {
            var key=new Vector4(dab.radius,dab.height,dab.rotation,dab.falloff);
            if(shape!=null && key==lastShape && dab.invert==lastInvert) return;
            if(shape!=null) Object.DestroyImmediate(shape);
            shape=source.Shape(Vector3.zero,dab.radius,dab.height,dab.rotation,dab.falloff,dab.invert);
            lastShape=key; lastInvert=dab.invert;
        }
        public void Draw(Map map, PrefabStampAppearance.Dab dab)
        {
            var bounds=MGTerrainTileAuthoring.BoundsOf(dab.tile);
            var view=Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(
                new Vector3(bounds.center.x,dab.height+10,bounds.center.z),
                Quaternion.LookRotation(Vector3.down,Vector3.forward),Vector3.one).inverse;
            var projection=GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-bounds.extents.x,bounds.extents.x,-bounds.extents.z,bounds.extents.z,.1f,dab.height+20),true);
            using(var commands=new CommandBuffer{name="Prefab stamp texture projection"})
            {
                commands.Blit(map.current,map.scratch);
                commands.SetRenderTarget(map.scratch);
                commands.ClearRenderTarget(true,false,Color.clear);
                var properties=new MaterialPropertyBlock();
                properties.SetMatrix("_WorldToClip",projection*view);
                properties.SetVector("_TileRect",new Vector4(bounds.min.x,bounds.min.z,bounds.size.x,bounds.size.z));
                properties.SetVector("_StampCenter",dab.center);
                properties.SetFloat("_Radius",dab.radius); properties.SetFloat("_Falloff",dab.falloff);
                properties.SetFloat("_Invert",dab.invert?-1:1);
                properties.SetFloat("_WriteNormal",map.normal?1:0);
                properties.SetTexture("_PreviousMap",map.current);
                for(int s=0;s<materials.Length;s++) commands.DrawMesh(shape,Matrix4x4.identity,materials[s],s,0,properties);
                Graphics.ExecuteCommandBuffer(commands);
            }
            var swap=map.current;map.current=map.scratch;map.scratch=swap;
        }
        internal sealed class Map : IDisposable
        {
            internal RenderTexture current,scratch;
            internal readonly bool normal;
            public Map(Texture original,bool normal)
            {
                this.normal=normal;
                try
                {
                    var format=normal?RenderTextureReadWrite.Linear:RenderTextureReadWrite.sRGB;
                    current=RenderTexture.GetTemporary(original.width,original.height,24,RenderTextureFormat.ARGB32,format);
                    scratch=RenderTexture.GetTemporary(original.width,original.height,24,RenderTextureFormat.ARGB32,format);
                    current.wrapMode=scratch.wrapMode=TextureWrapMode.Clamp;
                    var previous=GL.sRGBWrite;
                    try { GL.sRGBWrite=!normal;Graphics.Blit(original,current); }
                    finally {GL.sRGBWrite=previous;}
                }
                catch {Dispose();throw;}
            }
            public Texture2D Read()
            {
                var previous=RenderTexture.active;
                var texture=new Texture2D(current.width,current.height,TextureFormat.RGBA32,true,normal){wrapMode=TextureWrapMode.Clamp};
                try {RenderTexture.active=current;texture.ReadPixels(new Rect(0,0,current.width,current.height),0,0);texture.Apply();return texture;}
                catch {Object.DestroyImmediate(texture);throw;}
                finally {RenderTexture.active=previous;}
            }
            public void Dispose(){if(current!=null)RenderTexture.ReleaseTemporary(current);if(scratch!=null)RenderTexture.ReleaseTemporary(scratch);current=scratch=null;}
        }
        public void Dispose(){if(shape!=null)Object.DestroyImmediate(shape);if(materials!=null)foreach(var m in materials)if(m!=null)Object.DestroyImmediate(m);}
    }
}
