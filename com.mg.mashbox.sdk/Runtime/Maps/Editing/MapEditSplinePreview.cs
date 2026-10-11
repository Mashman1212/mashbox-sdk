using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using UnitySpline = UnityEngine.Splines.Spline;

namespace MashBoxSDK.Maps.Editing
{
    /// <summary>Local-space guide curves; camera or object movement does not retessellate them.</summary>
    public sealed class MapEditSplinePreview : IDisposable
    {
        readonly Dictionary<UnitySpline, Vector3[]> samples = new Dictionary<UnitySpline, Vector3[]>();
        public int SampleBuilds { get; private set; }
        public MapEditSplinePreview() { UnitySpline.Changed += Changed; }
        void Changed(UnitySpline spline, int knotIndex, SplineModification modification) => samples.Remove(spline);
        public void Clear() => samples.Clear();
        public void Dispose() { UnitySpline.Changed -= Changed; Clear(); }
        public Vector3[] GetPoints(UnitySpline spline)
        {
            if (samples.TryGetValue(spline, out var points)) return points;
            if (spline.Count < 2) return Array.Empty<Vector3>();
            int curves = spline.Closed ? spline.Count : spline.Count - 1;
            if (curves <= 2048)
            {
                var vertices = new List<Vector3>(Mathf.Min(curves * 4 + 1, 2049));
                vertices.Add(spline.GetCurve(0).P0);
                int curvedSteps = Mathf.Clamp(2048 / curves, 1, 16);
                for (int curveIndex = 0; curveIndex < curves; curveIndex++)
                {
                    var curve = spline.GetCurve(curveIndex);
                    // Dense loft rails commonly use zero tangents. Such a Bezier
                    // lies exactly on its chord: sixteen samples draw the same line.
                    int count = curve.P0.Equals(curve.P1) && curve.P2.Equals(curve.P3) ? 1 : curvedSteps;
                    for (int step = 1; step <= count; step++)
                        vertices.Add(CurveUtility.EvaluatePosition(curve, step / (float)count));
                }
                points = vertices.ToArray(); samples.Add(spline, points); SampleBuilds++;
                return points;
            }
            int steps = Mathf.Clamp(curves * 16, 16, 2048);
            points = new Vector3[steps + 1];
            // Evaluate each Bezier directly instead of repeatedly searching the whole spline
            // for the curve at a normalized distance (quadratic on long trail splines).
            for (int i = 0; i <= steps; i++)
            {
                float t = i * (float)curves / steps;
                int curve = Mathf.Min((int)t, curves - 1);
                points[i] = CurveUtility.EvaluatePosition(spline.GetCurve(curve), t - curve);
            }
            samples.Add(spline, points); SampleBuilds++;
            return points;
        }
    }
}
