using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.MapTools
{
    // Samples the source triangle's material UVs directly at destination map texels.
    // No camera, light, exposure or intermediate stamp-resolution image is involved.
    internal sealed class PrefabStampSampler : IDisposable
    {
        readonly PrefabStampSource source;
        readonly int[] triangles, slots;
        readonly Vector2[] uv;
        readonly Surface[] surfaces;
        readonly List<UnityEngine.Object> resources = new List<UnityEngine.Object>();
        Vector3[] normals;
        Vector4[] tangents;
        struct Surface
        {
            public Texture2D colour, normal;
            public Color tint;
            public Vector2 scale, offset;
            public float strength, cutoff;
        }

        public PrefabStampSampler(PrefabStampSource source, bool colour, bool normal)
        {
            this.source = source;
            uv = source.Mesh.uv;
            var indices = new List<int>(); var materials = new List<int>();
            for (int s = 0; s < source.Mesh.subMeshCount; s++)
            {
                var sub = source.Mesh.GetTriangles(s); indices.AddRange(sub);
                for (int i = 0; i < sub.Length; i += 3) materials.Add(s);
            }
            triangles = indices.ToArray(); slots = materials.ToArray();
            surfaces = new Surface[source.Materials.Length];
            try
            {
                for (int s = 0; s < surfaces.Length; s++)
                {
                    var m = source.Materials[s];
                    bool hdrp = m.shader.name == "HDRP/Lit" || m.shader.name == "HDRP/LitTessellation";
                    bool standard = m.shader.name == "Standard" || m.shader.name == "Standard (Specular setup)";
                    bool urp = m.shader.name == "Universal Render Pipeline/Lit";
                    if (!hdrp && !standard && !urp)
                        throw new InvalidOperationException("Direct stamp transfer does not support shader '" + m.shader.name + "' on " + m.name + ". Use a UV-mapped Lit material.");
                    if (hdrp && (m.GetFloat("_UVBase") != 0 || m.GetFloat("_NormalMapSpace") != 0
                        || m.GetTexture("_DetailMap") != null))
                        throw new InvalidOperationException("Direct transfer needs UV0, tangent-space normals and no detail-map layering: " + m.name);
                    string baseMap = hdrp ? "_BaseColorMap" : urp ? "_BaseMap" : "_MainTex";
                    string normalMap = hdrp ? "_NormalMap" : "_BumpMap";
                    var texture = m.GetTexture(baseMap);
                    var n = normal ? m.GetTexture(normalMap) : null;
                    if ((texture != null || n != null) && uv.Length != source.Mesh.vertexCount)
                        throw new InvalidOperationException("Stamp mesh needs UV0 for texture transfer.");
                    surfaces[s] = new Surface {
                        colour = texture != null ? Copy(texture, false) : null,
                        normal = n != null ? Copy(n, true) : null,
                        tint = m.GetColor(hdrp || urp ? "_BaseColor" : "_Color"),
                        scale = m.GetTextureScale(baseMap), offset = m.GetTextureOffset(baseMap),
                        strength = m.GetFloat(hdrp ? "_NormalScale" : "_BumpScale"),
                        cutoff = hdrp ? (m.IsKeywordEnabled("_ALPHATEST_ON") ? m.GetFloat("_AlphaCutoff") : -1)
                            : (m.IsKeywordEnabled("_ALPHATEST_ON") ? m.GetFloat("_Cutoff") : -1)
                    };
                }
            }
            catch { Dispose(); throw; }
        }

        Texture2D Copy(Texture input, bool normal)
        {
            if (!(input is Texture2D)) throw new InvalidOperationException("Stamp transfer needs 2D textures.");
            if (!normal) { var copy = PrefabStampAppearance.CloneMap(input, 1, false, Color.white, resources); copy.wrapMode = input.wrapMode; return copy; }
            var shader = Shader.Find("Hidden/MashBox/StampUnpackNormal");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Stamp normal transfer shader has not imported yet.");
            var material = new Material(shader);
            var rt = RenderTexture.GetTemporary(input.width, input.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(input, rt, material);
                RenderTexture.active = rt;
                var result = new Texture2D(input.width, input.height, TextureFormat.RGBA32, false, true);
                resources.Add(result);
                result.ReadPixels(new Rect(0, 0, input.width, input.height), 0, 0); result.Apply();
                result.wrapMode = input.wrapMode; return result;
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(material); }
        }

        public void SetShape(PrefabStampAppearance.Dab dab)
        {
            var shape = source.Shape(Vector3.zero, dab.radius, dab.height, dab.rotation, dab.falloff, dab.invert);
            try { normals = shape.normals; tangents = shape.tangents; }
            finally { UnityEngine.Object.DestroyImmediate(shape); }
        }

        public bool Sample(int triangle, Vector3 bary, bool normal, out Color value)
        {
            int i = triangle * 3, a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            var s = surfaces[slots[triangle]];
            var texcoord = uv.Length == 0 ? Vector2.zero : uv[a] * bary.x + uv[b] * bary.y + uv[c] * bary.z;
            texcoord = Vector2.Scale(texcoord, s.scale) + s.offset;
            var rgb = s.colour != null ? s.colour.GetPixelBilinear(texcoord.x, texcoord.y) : Color.white;
            value = rgb * s.tint;
            if (value.a < s.cutoff) return false;
            if (!normal) return true;
            var n = (normals[a] * bary.x + normals[b] * bary.y + normals[c] * bary.z).normalized;
            if (s.normal != null)
            {
                if (tangents.Length != normals.Length) throw new InvalidOperationException("Normal transfer needs mesh tangents.");
                var t4 = tangents[a] * bary.x + tangents[b] * bary.y + tangents[c] * bary.z;
                var t = new Vector3(t4.x, t4.y, t4.z); t = (t - n * Vector3.Dot(n, t)).normalized;
                var bitangent = Vector3.Cross(n, t) * (t4.w < 0 ? -1 : 1);
                var pixel = s.normal.GetPixelBilinear(texcoord.x, texcoord.y);
                var detail = new Vector3((pixel.r * 2 - 1) * s.strength, (pixel.g * 2 - 1) * s.strength, pixel.b * 2 - 1).normalized;
                n = (t * detail.x + bitangent * detail.y + n * detail.z).normalized;
            }
            value = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1);
            return true;
        }
        public void Dispose() { foreach (var resource in resources) if (resource != null) UnityEngine.Object.DestroyImmediate(resource); resources.Clear(); }
    }
}
