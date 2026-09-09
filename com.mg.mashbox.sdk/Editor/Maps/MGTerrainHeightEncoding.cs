#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainHeightEncoding
    {
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

        internal static void SaveMetadata(string path, Vector4 decode)
        {
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("Cannot save height encoding metadata: " + path);
            importer.userData = Prefix + JsonUtility.ToJson(new Encoding { minimum = decode.x, range = decode.y });
            importer.SaveAndReimport();
        }

        internal static Vector4 ReadDecode(Texture2D height)
        {
            if (height.format == TextureFormat.RFloat) return new Vector4(0, 1, 0, 0);
            if (height.format != TextureFormat.R16) throw new InvalidOperationException("Use a baked R16 or legacy RFloat height asset.");
            string data = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(height))?.userData;
            if (data == null || !data.StartsWith(Prefix, StringComparison.Ordinal))
                throw new InvalidOperationException("This R16 height map is missing its min/range metadata. Restore its original .meta file or rebake.");
            var encoding = JsonUtility.FromJson<Encoding>(data.Substring(Prefix.Length));
            if (encoding == null || !Valid(encoding.minimum) || !Valid(encoding.range) || encoding.range < 0)
                throw new InvalidOperationException("Invalid height encoding metadata.");
            return new Vector4(encoding.minimum, encoding.range, 1, 0);
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
