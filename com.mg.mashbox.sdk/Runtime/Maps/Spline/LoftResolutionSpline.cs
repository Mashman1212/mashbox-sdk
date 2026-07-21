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
    public sealed class LoftResolutionSpline : MonoBehaviour
    {
        public const float MinimumResolutionScale = 0.25f;
        public const float MaximumResolutionScale = 10f;
        public const float ResolutionScaleStep = 0.25f;

        [Serializable]
        public sealed class ControlPoint
        {
            [Range(0f, 1f)] public float pathT;
            [Range(MinimumResolutionScale, MaximumResolutionScale)] public float resolutionScale = 1f;
            [HideInInspector] public Vector3 generatedLocalPosition;
        }

        [SerializeField] MultiSplineLoft m_Loft;
        [SerializeField, Min(2)] int m_GeneratedPointCount = 8;
        [SerializeField] bool m_SmoothInterpolation = true;
        [SerializeField] List<ControlPoint> m_ControlPoints = new List<ControlPoint>();

        SplineContainer m_Container;

        public MultiSplineLoft Loft { get => m_Loft; set => m_Loft = value; }
        public int GeneratedPointCount { get => m_GeneratedPointCount; set => m_GeneratedPointCount = Mathf.Max(2, value); }
        public bool SmoothInterpolation { get => m_SmoothInterpolation; set => m_SmoothInterpolation = value; }
        public List<ControlPoint> ControlPoints => m_ControlPoints;
        public SplineContainer Container => m_Container != null ? m_Container : m_Container = GetComponent<SplineContainer>();
        public bool HasCustomScales
        {
            get
            {
                foreach (ControlPoint point in m_ControlPoints)
                {
                    if (point != null && !Mathf.Approximately(point.resolutionScale, 1f))
                        return true;
                }
                return false;
            }
        }

        void OnValidate()
        {
            m_GeneratedPointCount = Mathf.Max(2, m_GeneratedPointCount);
            for (int i = 0; i < m_ControlPoints.Count; i++)
            {
                m_ControlPoints[i] ??= new ControlPoint();
                m_ControlPoints[i].pathT = Mathf.Clamp01(m_ControlPoints[i].pathT);
                m_ControlPoints[i].resolutionScale = SnapResolutionScale(m_ControlPoints[i].resolutionScale);
            }

            m_Loft?.QueueRegenerate();
        }

        public bool GenerateFromLoft(out string error)
        {
            error = null;
            if (m_Loft == null)
            {
                error = "Assign the Multi-Spline Loft that owns this resolution spline.";
                return false;
            }

            int count = Mathf.Max(2, m_GeneratedPointCount);
            var worldPoints = new Vector3[count];
            for (int index = 0; index < count; index++)
            {
                float pathT = index / (float)(count - 1);
                if (!m_Loft.TryEvaluateResolutionCenterline(pathT, out worldPoints[index]))
                {
                    error = "The loft needs at least two valid source splines before generating a resolution spline.";
                    return false;
                }
            }

            RebuildSpline(worldPoints);
            return true;
        }

        public float EvaluateScale(float pathT)
        {
            if (m_ControlPoints.Count == 0)
                return 1f;
            if (m_ControlPoints.Count == 1)
                return Mathf.Clamp(m_ControlPoints[0].resolutionScale, 0.1f, 10f);

            pathT = Mathf.Clamp01(pathT);
            int segment = 0;
            while (segment < m_ControlPoints.Count - 2 && pathT > m_ControlPoints[segment + 1].pathT)
                segment++;

            ControlPoint a = m_ControlPoints[segment];
            ControlPoint b = m_ControlPoints[Mathf.Min(segment + 1, m_ControlPoints.Count - 1)];
            float t = Mathf.InverseLerp(a.pathT, b.pathT, pathT);
            if (m_SmoothInterpolation)
                t = Mathf.SmoothStep(0f, 1f, t);
            return Mathf.Clamp(Mathf.Lerp(a.resolutionScale, b.resolutionScale, t), 0.1f, 10f);
        }

        public void ResetScales()
        {
            foreach (ControlPoint point in m_ControlPoints)
            {
                if (point != null)
                    point.resolutionScale = 1f;
            }
            m_Loft?.QueueRegenerate();
        }

        void RebuildSpline(IReadOnlyList<Vector3> worldPoints)
        {
            var previousPoints = new List<ControlPoint>(m_ControlPoints);
            m_ControlPoints.Clear();
            UnitySpline spline = Container.Spline;
            spline.Clear();

            for (int index = 0; index < worldPoints.Count; index++)
            {
                float pathT = worldPoints.Count > 1 ? index / (float)(worldPoints.Count - 1) : 0f;
                Vector3 localPosition = transform.InverseTransformPoint(worldPoints[index]);
                float preservedScale = EvaluatePreviousScale(previousPoints, pathT);
                m_ControlPoints.Add(new ControlPoint
                {
                    pathT = pathT,
                    resolutionScale = SnapResolutionScale(preservedScale),
                    generatedLocalPosition = localPosition
                });
                spline.Add(new BezierKnot(localPosition), TangentMode.AutoSmooth);
            }
        }

        public static float SnapResolutionScale(float value)
        {
            float snapped = Mathf.Round(value / ResolutionScaleStep) * ResolutionScaleStep;
            return Mathf.Clamp(snapped, MinimumResolutionScale, MaximumResolutionScale);
        }

        static float EvaluatePreviousScale(IReadOnlyList<ControlPoint> points, float pathT)
        {
            if (points == null || points.Count == 0)
                return 1f;
            if (points.Count == 1)
                return Mathf.Clamp(points[0]?.resolutionScale ?? 1f, 0.1f, 10f);

            int segment = 0;
            while (segment < points.Count - 2 && pathT > (points[segment + 1]?.pathT ?? 1f))
                segment++;

            ControlPoint a = points[segment];
            ControlPoint b = points[Mathf.Min(segment + 1, points.Count - 1)];
            if (a == null || b == null)
                return 1f;
            float t = Mathf.InverseLerp(a.pathT, b.pathT, pathT);
            return Mathf.Clamp(Mathf.Lerp(a.resolutionScale, b.resolutionScale, t), 0.1f, 10f);
        }
    }
}
