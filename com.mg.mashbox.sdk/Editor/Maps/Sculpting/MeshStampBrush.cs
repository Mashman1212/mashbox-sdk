using System.Collections.Generic;
using MashBoxSDK.Maps.Sculpting;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    // Cache projected triangles in spatial bins. Sampling is exact within each
    // triangle, independent of physics colliders and the source object's placement.
    internal sealed class MeshStampBrush
    {
        const int BinCount = 32;
        readonly Vector3[] vertices;
        readonly int[] triangles;
        readonly List<int>[] bins = new List<int>[BinCount * BinCount];
        readonly Vector3[] previewLine = new Vector3[2];
        public Mesh Source { get; }

        public MeshStampBrush(Mesh source)
        {
            Source = source;
            vertices = source.vertices;
            triangles = source.triangles;
            Bounds bounds = source.bounds;
            float radius = Mathf.Max(0.00001f, new Vector2(bounds.extents.x, bounds.extents.z).magnitude);
            float height = Mathf.Max(0.00001f, bounds.size.y);
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i];
                vertices[i] = new Vector3((v.x - bounds.center.x) / radius,
                    (v.y - bounds.min.y) / height, (v.z - bounds.center.z) / radius);
            }
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var b = vertices[triangles[i + 1]];
                var c = vertices[triangles[i + 2]];
                for (int z = Bin(Mathf.Min(a.z, b.z, c.z)); z <= Bin(Mathf.Max(a.z, b.z, c.z)); z++)
                    for (int x = Bin(Mathf.Min(a.x, b.x, c.x)); x <= Bin(Mathf.Max(a.x, b.x, c.x)); x++)
                    {
                        int index = z * BinCount + x;
                        (bins[index] ??= new List<int>()).Add(i);
                    }
            }
        }

        static int Bin(float coordinate) => Mathf.Clamp((int)((coordinate + 1f) * 0.5f * BinCount), 0, BinCount - 1);

        internal bool TryHeight(float x, float z, out float height)
        {
            height = 0f;
            if (x < -1f || x > 1f || z < -1f || z > 1f) return false;
            var candidates = bins[Bin(z) * BinCount + Bin(x)];
            if (candidates == null) return false;
            bool found = false;
            foreach (int i in candidates)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                float determinant = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(determinant) < 0.00000001f) continue;
                float u = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / determinant;
                float v = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / determinant;
                float w = 1f - u - v;
                if (u < -0.00001f || v < -0.00001f || w < -0.00001f) continue;
                float y = u * a.y + v * b.y + w * c.y;
                if (!found || y > height) height = y;
                found = true;
            }
            return found;
        }

        public MeshSculptModifier.SeamVertex[] Sample(MeshFilter target, Vector3 center, float radius,
            float height, float rotation, float strength, float falloff)
        {
            var result = new List<MeshSculptModifier.SeamVertex>();
            if (radius <= 0f) return result.ToArray();
            var inverseRotation = Quaternion.Euler(0, -rotation, 0);
            var points = target.sharedMesh.vertices;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 point = inverseRotation * (target.transform.TransformPoint(points[i]) - center) / radius;
                float distance = new Vector2(point.x, point.z).magnitude;
                if (distance >= 1f || !TryHeight(point.x, point.z, out float sample)) continue;
                float delta = sample * height * strength * Mathf.Pow(1f - distance, falloff);
                if (Mathf.Abs(delta) < 0.0000001f) continue;
                result.Add(new MeshSculptModifier.SeamVertex { index = i,
                    delta = target.transform.InverseTransformVector(Vector3.up * delta) });
            }
            return result.ToArray();
        }

        public void DrawPreview(Vector3 center, float radius, float height, float rotation, float strength, float falloff)
        {
            // Render the sampled top surface, including the same strength and
            // falloff as the dab. A bounded grid keeps dense source meshes responsive.
            var orientation = Quaternion.Euler(0, rotation, 0);
            Color previous = Handles.color;
            Handles.color = strength < 0f ? new Color(1f, .4f, .2f, .8f) : new Color(.1f, .9f, 1f, .8f);
            try
            {
                const int steps = 24;
                for (int z = 0; z <= steps; z++)
                    for (int x = 0; x <= steps; x++)
                    {
                        float px = -1f + 2f * x / steps, pz = -1f + 2f * z / steps;
                        if (x < steps) DrawSegment(px, pz, px + 2f / steps, pz);
                        if (z < steps) DrawSegment(px, pz, px, pz + 2f / steps);
                    }
            }
            finally { Handles.color = previous; }

            void DrawSegment(float ax, float az, float bx, float bz)
            {
                if (!TryHeight(ax, az, out float a) || !TryHeight(bx, bz, out float b)) return;
                previewLine[0] = Point(ax, az, a);
                previewLine[1] = Point(bx, bz, b);
                Handles.DrawAAPolyLine(2f, previewLine);
            }
            Vector3 Point(float x, float z, float y)
            {
                float weight = Mathf.Pow(Mathf.Clamp01(1f - new Vector2(x, z).magnitude), falloff);
                return center + orientation * new Vector3(x * radius, y * height * strength * weight, z * radius);
            }
        }
    }
}
