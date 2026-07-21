using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using UnitySpline = UnityEngine.Splines.Spline;

namespace MashBoxSDK.Maps.Spline
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SplineContainer))]
    public sealed class LoftShoulderEdgeSpline : MonoBehaviour
    {
        [SerializeField] LoftShoulderModifier m_Modifier;
        [SerializeField] LoftShoulderModifier.Edge m_Edge;
        [SerializeField, Range(0f, 1f)] float m_PositionInfluence = 1f;
        [SerializeField] AnimationCurve m_AcrossInfluence = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField, Min(2)] int m_GeneratedPointCount = 16;
        [SerializeField, HideInInspector] List<Vector3> m_GeneratedBasePositions = new List<Vector3>();

        SplineContainer m_Container;
        UnitySpline m_BaseSpline;
        bool m_Rebuilding;

        public LoftShoulderModifier Modifier { get => m_Modifier; set => m_Modifier = value; }
        public LoftShoulderModifier.Edge Edge { get => m_Edge; set => m_Edge = value; }
        public float PositionInfluence { get => m_PositionInfluence; set => m_PositionInfluence = Mathf.Clamp01(value); }
        public AnimationCurve AcrossInfluence => m_AcrossInfluence;
        public int GeneratedPointCount { get => m_GeneratedPointCount; set => m_GeneratedPointCount = Mathf.Max(2, value); }
        public SplineContainer Container => m_Container != null ? m_Container : m_Container = GetComponent<SplineContainer>();

        void OnEnable()
        {
            UnitySpline.Changed += OnSplineChanged;
        }

        void OnDisable()
        {
            UnitySpline.Changed -= OnSplineChanged;
        }

        void OnValidate()
        {
            m_PositionInfluence = Mathf.Clamp01(m_PositionInfluence);
            m_GeneratedPointCount = Mathf.Max(2, m_GeneratedPointCount);
            m_AcrossInfluence ??= AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            if (!m_Rebuilding)
                m_Modifier?.Loft?.QueueRegenerate();
        }

        void OnSplineChanged(UnitySpline spline, int knotIndex, SplineModification modification)
        {
            if (!m_Rebuilding && spline == Container.Spline)
                m_Modifier?.Loft?.QueueRegenerate();
        }

        public float EvaluateInfluence(float acrossT)
        {
            float curveValue = m_AcrossInfluence != null ? m_AcrossInfluence.Evaluate(Mathf.Clamp01(acrossT)) : acrossT;
            return Mathf.Clamp01(curveValue) * m_PositionInfluence;
        }

        public bool TryEvaluateOffset(float pathT, MultiSplineLoft loft, out Vector3 loftLocalOffset)
        {
            loftLocalOffset = Vector3.zero;
            UnitySpline spline = Container.Spline;
            EnsureBaseSpline();
            if (loft == null || spline == null || spline.Count < 2 || m_BaseSpline == null || m_BaseSpline.Count != spline.Count)
                return false;

            float t = Mathf.Clamp01(pathT);
            Vector3 currentPosition = (Vector3)SplineUtility.EvaluatePosition(spline, t);
            Vector3 basePosition = (Vector3)SplineUtility.EvaluatePosition(m_BaseSpline, t);
            Vector3 worldOffset = transform.TransformVector(currentPosition - basePosition);
            loftLocalOffset = loft.transform.InverseTransformVector(worldOffset);
            return IsFinite(loftLocalOffset);
        }

        public void RefreshGeneratedPath(IReadOnlyList<Vector3> loftLocalOuterEdge, MultiSplineLoft loft)
        {
            if (loft == null || loftLocalOuterEdge == null || loftLocalOuterEdge.Count < 2)
                return;

            UnitySpline spline = Container.Spline;
            var previousOffsets = CaptureOffsets(spline);
            int count = Mathf.Max(2, m_GeneratedPointCount);
            m_Rebuilding = true;
            try
            {
                spline.Clear();
                m_GeneratedBasePositions.Clear();
                for (int index = 0; index < count; index++)
                {
                    float pathT = index / (float)(count - 1);
                    Vector3 loftLocalBase = EvaluatePolyline(loftLocalOuterEdge, pathT);
                    Vector3 worldBase = loft.transform.TransformPoint(loftLocalBase);
                    Vector3 splineLocalBase = transform.InverseTransformPoint(worldBase);
                    Vector3 preservedOffset = EvaluatePolyline(previousOffsets, pathT);
                    m_GeneratedBasePositions.Add(splineLocalBase);
                    spline.Add(new BezierKnot(splineLocalBase + preservedOffset), TangentMode.AutoSmooth);
                }
                RebuildBaseSpline();
            }
            finally
            {
                m_Rebuilding = false;
            }
        }

        public void ResetToGeneratedEdge()
        {
            UnitySpline spline = Container.Spline;
            if (spline == null || spline.Count != m_GeneratedBasePositions.Count)
                return;

            m_Rebuilding = true;
            try
            {
                spline.Clear();
                for (int index = 0; index < m_GeneratedBasePositions.Count; index++)
                    spline.Add(new BezierKnot(m_GeneratedBasePositions[index]), TangentMode.AutoSmooth);
            }
            finally
            {
                m_Rebuilding = false;
            }
            m_Modifier?.Loft?.QueueRegenerate();
        }

        List<Vector3> CaptureOffsets(UnitySpline spline)
        {
            var offsets = new List<Vector3>();
            int count = Mathf.Min(spline != null ? spline.Count : 0, m_GeneratedBasePositions.Count);
            for (int index = 0; index < count; index++)
                offsets.Add((Vector3)spline[index].Position - m_GeneratedBasePositions[index]);
            return offsets;
        }

        void EnsureBaseSpline()
        {
            if (m_BaseSpline == null || m_BaseSpline.Count != m_GeneratedBasePositions.Count)
                RebuildBaseSpline();
        }

        void RebuildBaseSpline()
        {
            m_BaseSpline = new UnitySpline();
            for (int index = 0; index < m_GeneratedBasePositions.Count; index++)
                m_BaseSpline.Add(new BezierKnot(m_GeneratedBasePositions[index]), TangentMode.AutoSmooth);
        }

        static Vector3 EvaluatePolyline(IReadOnlyList<Vector3> points, float t)
        {
            if (points == null || points.Count == 0)
                return Vector3.zero;
            if (points.Count == 1)
                return points[0];

            float scaled = Mathf.Clamp01(t) * (points.Count - 1);
            int index = Mathf.Min(Mathf.FloorToInt(scaled), points.Count - 2);
            return Vector3.LerpUnclamped(points[index], points[index + 1], scaled - index);
        }

        static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }
    }
}
