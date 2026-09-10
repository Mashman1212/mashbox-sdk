#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainHeightEncoding
    {
        const string DeltaPrefix = "MGDistantDeltaR16:";
        const string Prefix = "MGDistantHeightR16:";
        [Serializable] sealed class Encoding { public float minimum; public float range; }

        internal static Texture2D Encode(Texture2D source, out Vector4 decode)
        {
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.R16))
                throw new InvalidOperationException("This device does not support R16 height textures.");
            if (source.format != TextureFormat.RFloat || !source.isReadable)
                throw new InvalidOperationException("Height encoding requires readable RFloat capture data.");
            var raw = source.GetPixelData<float>(0);
            float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
            for (int i = 0; i < raw.Length; i++)
                if (Valid(raw[i])) { minimum = Mathf.Min(minimum, raw[i]); maximum = Mathf.Max(maximum, raw[i]); }
            if (float.IsPositiveInfinity(minimum)) throw new InvalidOperationException("Height capture contains no surface.");
            float range = maximum - minimum;
            var pixels = new ushort[raw.Length];
            // Zero marks missing surface; 1..65535 encode the complete valid height range.
            for (int i = 0; i < raw.Length; i++)
                if (Valid(raw[i])) pixels[i] = (ushort)(1 + Mathf.RoundToInt((range > 0 ? Mathf.Clamp01((raw[i] - minimum) / range) : 0) * 65534f));
            var result = new Texture2D(source.width, source.height, TextureFormat.R16, false, true)
            { name = source.name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            result.SetPixelData(pixels, 0); result.Apply(false, false);
            decode = new Vector4(minimum, range, 1, 0);
            return result;
        }

        // Compare the final smoothed grid triangles with the original mesh at the same local XZ.
        // This is an upward-only canopy morph: zero means no lift (including missing coverage).
        internal static Texture2D EncodeDifference(Mesh original, Bounds bounds, float[] surface,
            int nx, int nz, int width, int height, out Vector4 decode)
        {
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.R16))
                throw new InvalidOperationException("This device does not support R16 height textures.");
            var ground = new float[width * height];
            for (int i = 0; i < ground.Length; i++) ground[i] = float.NegativeInfinity;
            var vertices = original.vertices;
            var triangles = original.triangles;
            Vector3 Pixel(Vector3 p) => new Vector3((p.x - bounds.min.x) / bounds.size.x * width - .5f,
                p.y, (p.z - bounds.min.z) / bounds.size.z * height - .5f);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                if (t % 12288 == 0 && EditorUtility.DisplayCancelableProgressBar("Distant Surface Height",
                    "Comparing original terrain with the baked mesh", t / (float)triangles.Length))
                    throw new OperationCanceledException("Distant height comparison cancelled.");
                Vector3 a = Pixel(vertices[triangles[t]]), b = Pixel(vertices[triangles[t + 1]]), c = Pixel(vertices[triangles[t + 2]]);
                float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(denominator) < 1e-12f) continue;
                int x0 = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
                int x1 = Mathf.Min(width - 1, Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
                int z0 = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(a.z, Mathf.Min(b.z, c.z))));
                int z1 = Mathf.Min(height - 1, Mathf.FloorToInt(Mathf.Max(a.z, Mathf.Max(b.z, c.z))));
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                {
                    float u = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / denominator;
                    float v = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / denominator;
                    float w = 1 - u - v;
                    if (u < -1e-5f || v < -1e-5f || w < -1e-5f) continue;
                    int i = z * width + x;
                    ground[i] = Mathf.Max(ground[i], u * a.y + v * b.y + w * c.y);
                }
            }
            float maximum = 0;
            for (int z = 0; z < height; z++) for (int x = 0; x < width; x++)
            {
                int pixel = z * width + x;
                float gx = (x + .5f) / width * nx, gz = (z + .5f) / height * nz;
                int ix = Mathf.Min(nx - 1, Mathf.FloorToInt(gx)), iz = Mathf.Min(nz - 1, Mathf.FloorToInt(gz));
                float fx = gx - ix, fz = gz - iz;
                int i = iz * (nx + 1) + ix;
                float a = surface[i], b = surface[i + 1], c = surface[i + nx + 1], d = surface[i + nx + 2];
                float delta = 0;
                // Match SaveDistantSurface's diagonal and its whole-cell hole rejection exactly.
                if (Valid(ground[pixel]) && Valid(a) && Valid(b) && Valid(c) && Valid(d))
                {
                    float target = fx + fz <= 1 ? a + (b - a) * fx + (c - a) * fz
                        : d + (c - d) * (1 - fx) + (b - d) * (1 - fz);
                    delta = Mathf.Max(0, target - ground[pixel]);
                    if (delta < .001f) delta = 0; // Suppress sub-millimetre rasterization noise.
                }
                ground[pixel] = delta;
                maximum = Mathf.Max(maximum, delta);
            }
            var pixels = new ushort[ground.Length];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = maximum > 0 ? (ushort)Mathf.RoundToInt(Mathf.Clamp01(ground[i] / maximum) * 65535f) : (ushort)0;
            var result = new Texture2D(width, height, TextureFormat.R16, false, true)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            result.SetPixelData(pixels, 0); result.Apply(false, false);
            decode = new Vector4(0, maximum, 2, 0);
            return result;
        }

        internal static void SaveMetadata(string path, Vector4 decode)
        {
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("Cannot save height encoding metadata: " + path);
            importer.userData = (decode.z > 1.5f ? DeltaPrefix : Prefix) + JsonUtility.ToJson(new Encoding { minimum = decode.x, range = decode.y });
            importer.SaveAndReimport();
        }

        internal static Vector4 ReadDecode(Texture2D height)
        {
            if (height.format == TextureFormat.RFloat) return new Vector4(0, 1, 0, 0);
            if (height.format != TextureFormat.R16) throw new InvalidOperationException("Use a baked R16 or legacy RFloat height asset.");
            string data = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(height))?.userData;
            bool delta = data != null && data.StartsWith(DeltaPrefix, StringComparison.Ordinal);
            if (data == null || (!delta && !data.StartsWith(Prefix, StringComparison.Ordinal)))
                throw new InvalidOperationException("This R16 height map is missing its min/range metadata. Restore its original .meta file or rebake.");
            var encoding = JsonUtility.FromJson<Encoding>(data.Substring(delta ? DeltaPrefix.Length : Prefix.Length));
            if (encoding == null || !Valid(encoding.minimum) || !Valid(encoding.range) || encoding.range < 0)
                throw new InvalidOperationException("Invalid height encoding metadata.");
            return new Vector4(encoding.minimum, encoding.range, delta ? 2 : 1, 0);
        }

        internal static float Maximum(Texture2D height, Vector4 decode)
        {
            if (decode.z > .5f) return decode.x + decode.y;
            float top = float.NegativeInfinity;
            var values = height.GetPixelData<float>(0);
            for (int i = 0; i < values.Length; i++) if (Valid(values[i])) top = Mathf.Max(top, values[i]);
            if (float.IsNegativeInfinity(top)) throw new InvalidOperationException("This height map contains no captured surface.");
            return top;
        }
        static bool Valid(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && v > -1e19f && v < 1e19f;
    }
}
#endif
