using System;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    /// <summary>Vertex-preserving, simultaneous Laplacian smoothing of an ordered 3D path.</summary>
    public static class StudioEdgeFlow
    {
        /// <summary>Returns a new array. Open endpoints stay exact; closed paths wrap around.
        /// Influence is 0..1 and each iteration uses a stable half-step. Source arc-length
        /// weights preserve a straight path even when its samples are unevenly spaced.</summary>
        public static Vector3[] Smooth(Vector3[] points, float influence = .5f, int iterations = 4, bool closed = false)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (float.IsNaN(influence) || float.IsInfinity(influence)) throw new ArgumentOutOfRangeException(nameof(influence));
            if (iterations < 0) throw new ArgumentOutOfRangeException(nameof(iterations));
            foreach (var point in points) if (!Finite(point)) throw new ArgumentException("Path points must be finite.", nameof(points));
            var current = (Vector3[])points.Clone();
            int count = current.Length;
            bool duplicateEnd = closed && count > 1 && current[0].Equals(current[count - 1]);
            int unique = duplicateEnd ? count - 1 : count;
            influence = Mathf.Clamp01(influence);
            if (unique < 3 || influence == 0 || iterations == 0) return current;
            var next = new Vector3[count];
            var weights = new double[unique];
            int first = closed ? 0 : 1, end = closed ? unique : unique - 1;
            for (int i = first; i < end; i++)
            {
                int left = (i + unique - 1) % unique, right = (i + 1) % unique;
                double a = Distance(points[left], points[i]), b = Distance(points[i], points[right]);
                weights[i] = a + b > 1e-20f ? a / (a + b) : .5f;
            }
            float step = influence * .5f;
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                if (!closed) { next[0] = points[0]; next[unique - 1] = points[unique - 1]; }
                for (int i = first; i < end; i++)
                {
                    var average = Blend(current[(i + unique - 1) % unique], current[(i + 1) % unique], weights[i]);
                    next[i] = Blend(current[i], average, step);
                }
                if (duplicateEnd) next[count - 1] = next[0];
                var swap = current; current = next; next = swap;
            }
            return current;
        }
        private static double Distance(Vector3 a,Vector3 b)
        {double x=(double)a.x-b.x,y=(double)a.y-b.y,z=(double)a.z-b.z;return Math.Sqrt(x*x+y*y+z*z);}
        private static Vector3 Blend(Vector3 a,Vector3 b,double t)
            =>new Vector3((float)(a.x*(1-t)+b.x*t),(float)(a.y*(1-t)+b.y*t),(float)(a.z*(1-t)+b.z*t));
        internal static bool Finite(Vector3 p) => !(float.IsNaN(p.x) || float.IsInfinity(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.z));
    }
}
