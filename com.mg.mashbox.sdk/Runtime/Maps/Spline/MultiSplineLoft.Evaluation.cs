using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace MashBoxSDK.Maps.Spline
{
    public sealed partial class MultiSplineLoft
    {
        // A perpendicular solve evaluates each rail thousands of times. Unity's
        // normalized spline lookup scans all preceding curves for EACH evaluation.
        // Keep the same curve lengths/interpolation, but find the curve in log(n).
        readonly Dictionary<SplineSource, SourceEvaluation> m_SourceEvaluations = new Dictionary<SplineSource, SourceEvaluation>();
        readonly List<SplineSource> m_ObsoleteSourceEvaluations = new List<SplineSource>();
        static readonly Unity.Profiling.ProfilerMarker LoftSamplingMarker = new Unity.Profiling.ProfilerMarker("MapEditor.Loft.SampleGrid");
        static readonly Unity.Profiling.ProfilerMarker LoftCollisionMarker = new Unity.Profiling.ProfilerMarker("MapEditor.Loft.Collision");
        bool m_SourceEvaluationActive;
        sealed class SourceEvaluation : IDisposable
        {
            readonly ISpline spline;
            readonly UnityEngine.Splines.Spline managed;
            readonly BezierKnot[] knots;
            readonly bool closed;
            readonly NativeSpline native;
            readonly bool worldSpace;
            readonly Matrix4x4 matrix;
            readonly float[] ends;
            readonly BezierCurve[] curves;
            readonly DistanceToInterpolation[] distances;
            readonly bool[] cachedInterpolation;
            const int DistanceResolution = 30; // Unity Splines' Spline/NativeSpline distance LUT resolution.
            readonly float length;
            public SourceEvaluation(SplineSource source)
            {
                var container = source.container;
                managed = container.Splines[source.splineIndex];
                closed = managed.Closed; knots = new BezierKnot[managed.Count];
                for (int i = 0; i < knots.Length; i++) knots[i] = managed[i];
                matrix = container.transform.localToWorldMatrix;
                Vector3 scale = container.transform.lossyScale;
                worldSpace = scale.x != scale.y || scale.x != scale.z;
                if (worldSpace)
                {
                    native = new NativeSpline(managed, (float4x4)matrix, Allocator.Persistent);
                    spline = native;
                }
                else spline = managed;
                int count = spline.Closed ? spline.Count : spline.Count - 1;
                ends = new float[count]; curves = new BezierCurve[count];
                distances = new DistanceToInterpolation[count * DistanceResolution]; cachedInterpolation = new bool[count];
                using var lookup = new NativeArray<DistanceToInterpolation>(DistanceResolution, Allocator.Temp);
                float distance = 0;
                for (int i = 0; i < count; i++)
                {
                    distance += spline.GetCurveLength(i); ends[i] = distance;
                    curves[i] = spline.GetCurve(i);
                    CurveUtility.CalculateCurveLengths(curves[i], lookup);
                    for (int j = 0; j < DistanceResolution; j++) distances[i * DistanceResolution + j] = lookup[j];
                    // Keep a public-API fallback for spline implementations with
                    // different arc tables (including degenerate split curves).
                    cachedInterpolation[i] = lookup[DistanceResolution - 1].Distance == spline.GetCurveLength(i);
                }
                length = spline.GetLength();
            }
            public bool Matches(SplineSource source)
            {
                if (source.container.Splines[source.splineIndex] != managed || managed.Closed != closed || managed.Count != knots.Length ||
                    source.container.transform.localToWorldMatrix != matrix) return false;
                // Snapshot comparison also catches edits made with SetKnotNoNotify
                // and containers whose authoring component is disabled at runtime.
                for (int i = 0; i < knots.Length; i++) if (!knots[i].Equals(managed[i])) return false;
                return true;
            }
            public Vector3 Evaluate(float t)
            {
                float distance = Mathf.Clamp01(t) * length;
                int low = 0, high = ends.Length - 1;
                while (low < high)
                {
                    int middle = (low + high) >> 1;
                    if (distance <= ends[middle]) high = middle; else low = middle + 1;
                }
                float curveT = distance > ends[low] ? 1 : Interpolation(low, distance - (low == 0 ? 0 : ends[low - 1]));
                // Same polynomial and operation order as CurveUtility, avoiding
                // repeated temporary float3 values in the hot solve loop.
                var curve = curves[low]; float t2 = curveT * curveT, t3 = t2 * curveT;
                float a = -1f * t3 + 3f * t2 - 3f * curveT + 1f;
                float b = 3f * t3 - 6f * t2 + 3f * curveT, c = -3f * t3 + 3f * t2;
                Vector3 point = new Vector3(curve.P0.x * a + curve.P1.x * b + curve.P2.x * c + curve.P3.x * t3,
                    curve.P0.y * a + curve.P1.y * b + curve.P2.y * c + curve.P3.y * t3,
                    curve.P0.z * a + curve.P1.z * b + curve.P2.z * c + curve.P3.z * t3);
                return worldSpace ? point : matrix.MultiplyPoint3x4(point);
            }
            float Interpolation(int curve, float distance)
            {
                if (!cachedInterpolation[curve]) return spline.GetCurveInterpolation(curve, distance);
                if (distance <= 0) return 0;
                int start = curve * DistanceResolution, last = start + DistanceResolution - 1;
                if (distance >= distances[last].Distance) return 1;
                int low = start + 1, high = last;
                while (low < high)
                {
                    int middle = (low + high) >> 1;
                    if (distance < distances[middle].Distance) high = middle; else low = middle + 1;
                }
                var previous = distances[low - 1]; var next = distances[low];
                return math.lerp(previous.T, next.T, (distance - previous.Distance) / (next.Distance - previous.Distance));
            }
            public void Dispose() { if (worldSpace) native.Dispose(); }
        }
        void PrepareSourceEvaluations()
        {
            m_ObsoleteSourceEvaluations.Clear();
            foreach (var pair in m_SourceEvaluations)
                if (!m_Sources.Contains(pair.Key) || !pair.Key.IsValid) m_ObsoleteSourceEvaluations.Add(pair.Key);
            foreach (var source in m_ObsoleteSourceEvaluations)
            { m_SourceEvaluations[source].Dispose(); m_SourceEvaluations.Remove(source); }
            foreach (var source in m_Sources)
                if (source?.IsValid == true)
                {
                    if (m_SourceEvaluations.TryGetValue(source, out var evaluation))
                    {
                        if (evaluation.Matches(source)) continue;
                        evaluation.Dispose(); m_SourceEvaluations.Remove(source);
                    }
                    m_SourceEvaluations.Add(source, new SourceEvaluation(source));
                }
            m_SourceEvaluationActive = true;
        }
        void ReleaseSourceEvaluations() => m_SourceEvaluationActive = false;
        void ClearSourceEvaluations()
        {
            m_SourceEvaluationActive = false;
            foreach (var evaluation in m_SourceEvaluations.Values) evaluation.Dispose();
            m_SourceEvaluations.Clear();
            m_ObsoleteSourceEvaluations.Clear();
        }
    }
}
