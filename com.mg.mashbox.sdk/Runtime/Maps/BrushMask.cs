using System;
using UnityEngine;

namespace MashBoxSDK.Maps
{
    // Immutable pixel snapshot: strokes survive changes to the selected texture/importer.
    [Serializable]
    public sealed class BrushMask
    {
        public int size;
        public byte[] pixels;
        public Vector3 axisU = Vector3.right;
        public Vector3 axisV = Vector3.forward;

        public float Sample(Vector3 delta, float radius)
        {
            if (pixels == null || size < 2 || pixels.Length != size * size) return 1f;
            return SampleUV(Vector3.Dot(delta, axisU) / radius, Vector3.Dot(delta, axisV) / radius);
        }

        public float SampleUV(float x, float y)
        {
            if (pixels == null || size < 2 || pixels.Length != size * size) return 1f;
            if (x * x + y * y > 1f) return 0f;
            float u = (x * .5f + .5f) * (size - 1), v = (y * .5f + .5f) * (size - 1);
            int ix = Mathf.Clamp(Mathf.FloorToInt(u), 0, size - 1), iy = Mathf.Clamp(Mathf.FloorToInt(v), 0, size - 1);
            int nx = Mathf.Min(ix + 1, size - 1), ny = Mathf.Min(iy + 1, size - 1);
            return Mathf.Lerp(Mathf.Lerp(pixels[iy * size + ix], pixels[iy * size + nx], u - ix),
                Mathf.Lerp(pixels[ny * size + ix], pixels[ny * size + nx], u - ix), v - iy) / 255f;
        }
    }
}
