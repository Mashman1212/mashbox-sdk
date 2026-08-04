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
        [Serializable]
        public sealed class KnotProfileOverride
        {
            public bool enabled;
            public bool initialized;
            public AnimationCurve height = AnimationCurve.Linear(0f, 0f, 1f, 0f);
        }

        [SerializeField] LoftShoulderModifier m_Modifier;
        [SerializeField] LoftShoulderModifier.Edge m_Edge;
        [SerializeField, Range(0f, 1f)] float m_PositionInfluence = 1f;
        [SerializeField] AnimationCurve m_AcrossInfluence = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField, Min(2)] int m_GeneratedPointCount = 16;
        [SerializeField, HideInInspector] List<Vector3> m_GeneratedBasePositions = new List<Vector3>();
        [SerializeField, HideInInspector] List<KnotProfileOverride> m_KnotProfileOverrides = new List<KnotProfileOverride>();

        SplineContainer m_Container;
        UnitySpline m_BaseSpline;
        bool m_Rebuilding;

        public LoftShoulderModifier Modifier { get => m_Modifier; set => m_Modifier = value; }
        public LoftShoulderModifier.Edge Edge { get => m_Edge; set => m_Edge = value; }
        public float PositionInfluence { get => m_PositionInfluence; set => m_PositionInfluence = Mathf.Clamp01(value); }
        public AnimationCurve AcrossInfluence => m_AcrossInfluence;
        public int GeneratedPointCount { get => m_GeneratedPointCount; set => m_GeneratedPointCount = Mathf.Max(2, value); }
        public SplineContainer Container => m_Container != null ? m_Container : m_Container = GetComponent<SplineContainer>();
        public int KnotProfileCount => m_KnotProfileOverrides?.Count ?? 0;

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
            EnsureKnotProfileCount(m_GeneratedPointCount);
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

        public bool IsKnotProfileEnabled(int knotIndex)
        {
            return knotIndex >= 0
                && knotIndex < KnotProfileCount
                && m_KnotProfileOverrides[knotIndex] != null
                && m_KnotProfileOverrides[knotIndex].enabled;
        }

        public AnimationCurve GetKnotProfileCurve(int knotIndex)
        {
            if (knotIndex < 0 || knotIndex >= KnotProfileCount)
                return null;
            return m_KnotProfileOverrides[knotIndex]?.height;
        }

        public void SetKnotProfileEnabled(int knotIndex, bool enabled, AnimationCurve sourceProfile)
        {
            EnsureKnotProfileCount(Mathf.Max(m_GeneratedPointCount, knotIndex + 1));
            if (knotIndex < 0 || knotIndex >= KnotProfileCount)
                return;

            KnotProfileOverride profileOverride = m_KnotProfileOverrides[knotIndex];
            if (enabled && !profileOverride.initialized)
            {
                profileOverride.height = CopyCurve(sourceProfile);
                profileOverride.initialized = true;
            }
            profileOverride.enabled = enabled;
            m_Modifier?.Loft?.QueueRegenerate();
        }

        public void SetKnotProfileCurve(int knotIndex, AnimationCurve curve)
        {
            EnsureKnotProfileCount(Mathf.Max(m_GeneratedPointCount, knotIndex + 1));
            if (knotIndex < 0 || knotIndex >= KnotProfileCount)
                return;

            m_KnotProfileOverrides[knotIndex].height = CopyCurve(curve);
            m_KnotProfileOverrides[knotIndex].initialized = true;
            m_Modifier?.Loft?.QueueRegenerate();
        }

        public float EvaluateProfileHeight(float pathT, float acrossT, AnimationCurve fallbackProfile)
        {
            int count = Mathf.Min(Container.Spline != null ? Container.Spline.Count : 0, KnotProfileCount);
            if (count <= 0)
                return EvaluateCurve(fallbackProfile, acrossT);
            if (count == 1)
                return EvaluateKnotProfile(0, acrossT, fallbackProfile);

            float scaled = Mathf.Clamp01(pathT) * (count - 1);
            int fromIndex = Mathf.Min(Mathf.FloorToInt(scaled), count - 1);
            int toIndex = Mathf.Min(fromIndex + 1, count - 1);
            float blend = scaled - fromIndex;
            float fromHeight = EvaluateKnotProfile(fromIndex, acrossT, fallbackProfile);
            float toHeight = EvaluateKnotProfile(toIndex, acrossT, fallbackProfile);
            return Mathf.LerpUnclamped(fromHeight, toHeight, blend);
        }

        public void RefreshGeneratedPath(IReadOnlyList<Vector3> loftLocalOuterEdge, MultiSplineLoft loft)
        {
            if (loft == null || loftLocalOuterEdge == null || loftLocalOuterEdge.Count < 2)
                return;

            UnitySpline spline = Container.Spline;
            var previousOffsets = CaptureOffsets(spline);
            int count = Mathf.Max(2, m_GeneratedPointCount);
            EnsureKnotProfileCount(count);
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

        void EnsureKnotProfileCount(int count)
        {
            count = Mathf.Max(0, count);
            m_KnotProfileOverrides ??= new List<KnotProfileOverride>();
            while (m_KnotProfileOverrides.Count < count)
                m_KnotProfileOverrides.Add(new KnotProfileOverride());
            if (m_KnotProfileOverrides.Count > count)
                m_KnotProfileOverrides.RemoveRange(count, m_KnotProfileOverrides.Count - count);

            for (int index = 0; index < m_KnotProfileOverrides.Count; index++)
            {
                m_KnotProfileOverrides[index] ??= new KnotProfileOverride();
                m_KnotProfileOverrides[index].height ??= AnimationCurve.Linear(0f, 0f, 1f, 0f);
            }
        }

        float EvaluateKnotProfile(int knotIndex, float acrossT, AnimationCurve fallbackProfile)
        {
            AnimationCurve curve = IsKnotProfileEnabled(knotIndex)
                ? m_KnotProfileOverrides[knotIndex].height
                : fallbackProfile;
            return EvaluateCurve(curve, acrossT);
        }

        static float EvaluateCurve(AnimationCurve curve, float t)
        {
            return curve != null ? curve.Evaluate(Mathf.Clamp01(t)) : 0f;
        }

        static AnimationCurve CopyCurve(AnimationCurve source)
        {
            if (source == null)
                return AnimationCurve.Linear(0f, 0f, 1f, 0f);

            var copy = new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode
            };
            return copy;
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
