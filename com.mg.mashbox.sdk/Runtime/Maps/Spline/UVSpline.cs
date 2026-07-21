using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace MashBoxSDK.Maps.Spline
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SplineContainer))]
    public sealed class UVSpline : MonoBehaviour
    {
        public enum LongitudinalAxis
        {
            Auto,
            U,
            V
        }

        [Serializable]
        public sealed class ControlPoint
        {
            [Range(0f, 1f)] public float pathT;
            [Min(0.01f)] public float widthScale = 1f;
            [Min(0.01f)] public float lengthScale = 1f;
            public float sideOffset;
            public float alongOffset;
            [HideInInspector] public Vector3 generatedLocalPosition;

            public ControlPoint Clone()
            {
                return (ControlPoint)MemberwiseClone();
            }
        }

        [SerializeField] MeshFilter m_Target;
        [SerializeField, Range(0, 3)] int m_UvChannel;
        [SerializeField] LongitudinalAxis m_LongitudinalAxis = LongitudinalAxis.V;
        [SerializeField, Min(2)] int m_GeneratedPointCount = 8;
        [SerializeField] bool m_SmoothInterpolation = true;
        [SerializeField] bool m_LivePreview = true;
        [SerializeField] bool m_AutoCrossPivot = true;
        [SerializeField] float m_CrossPivot = 0.5f;
        [SerializeField] bool m_MovingKnotsOffsetsUv = true;
        [SerializeField, Min(0.25f)] float m_MoveFalloffPoints = 2f;
        [SerializeField] bool m_AutomaticMoveSensitivity = true;
        [SerializeField] float m_AlongUvPerWorldUnit = 0.1f;
        [SerializeField] float m_SideUvPerWorldUnit = 0.1f;
        [SerializeField] List<ControlPoint> m_ControlPoints = new List<ControlPoint>();
        [SerializeField, HideInInspector] Mesh m_SourceMesh;
        [SerializeField, HideInInspector] Mesh m_OutputMesh;

        SplineContainer m_Container;
        [NonSerialized] float[] m_CachedAlongMoveOffsets;
        [NonSerialized] float[] m_CachedSideMoveOffsets;
        [NonSerialized] float[] m_CachedLongAnchors;
        [NonSerialized] float[] m_CachedLongTangents;
        [NonSerialized] Mesh m_CachedUvSource;
        [NonSerialized] int m_CachedUvChannel = -1;
        [NonSerialized] readonly List<Vector2> m_CachedSourceUvs = new List<Vector2>();
        [NonSerialized] readonly List<Vector2> m_WorkingOutputUvs = new List<Vector2>();

        public MeshFilter Target { get => m_Target; set => m_Target = value; }
        public int UvChannel { get => m_UvChannel; set => m_UvChannel = Mathf.Clamp(value, 0, 3); }
        public LongitudinalAxis Direction { get => m_LongitudinalAxis; set => m_LongitudinalAxis = value; }
        public int GeneratedPointCount { get => m_GeneratedPointCount; set => m_GeneratedPointCount = Mathf.Max(2, value); }
        public bool SmoothInterpolation { get => m_SmoothInterpolation; set => m_SmoothInterpolation = value; }
        public bool LivePreview { get => m_LivePreview; set => m_LivePreview = value; }
        public bool AutoCrossPivot { get => m_AutoCrossPivot; set => m_AutoCrossPivot = value; }
        public float CrossPivot { get => m_CrossPivot; set => m_CrossPivot = value; }
        public bool MovingKnotsOffsetsUv { get => m_MovingKnotsOffsetsUv; set => m_MovingKnotsOffsetsUv = value; }
        public float MoveFalloffPoints { get => m_MoveFalloffPoints; set => m_MoveFalloffPoints = Mathf.Max(0.25f, value); }
        public bool AutomaticMoveSensitivity { get => m_AutomaticMoveSensitivity; set => m_AutomaticMoveSensitivity = value; }
        public float AlongUvPerWorldUnit { get => m_AlongUvPerWorldUnit; set => m_AlongUvPerWorldUnit = value; }
        public float SideUvPerWorldUnit { get => m_SideUvPerWorldUnit; set => m_SideUvPerWorldUnit = value; }
        public List<ControlPoint> ControlPoints => m_ControlPoints;
        public Mesh SourceMesh => ResolveSourceMesh();
        public Mesh OutputMesh => m_OutputMesh;
        public SplineContainer Container => m_Container != null ? m_Container : m_Container = GetComponent<SplineContainer>();

        void OnValidate()
        {
            m_UvChannel = Mathf.Clamp(m_UvChannel, 0, 3);
            m_GeneratedPointCount = Mathf.Max(2, m_GeneratedPointCount);
            m_MoveFalloffPoints = Mathf.Max(0.25f, m_MoveFalloffPoints);
            EnsureControlPointCount(Mathf.Max(2, Container.Spline.Count));
        }

        public bool GenerateFromTarget(out string error)
        {
            error = null;
            if (m_Target == null || m_Target.sharedMesh == null)
            {
                error = "Assign a target MeshFilter with a mesh.";
                return false;
            }

            Mesh mesh = ResolveSourceMesh();
            if (mesh == null)
            {
                error = "The target MeshFilter has no source mesh.";
                return false;
            }
            var uvs = new List<Vector2>();
            mesh.GetUVs(m_UvChannel, uvs);
            if (uvs.Count != mesh.vertexCount)
            {
                error = $"UV channel {m_UvChannel} does not contain one UV per mesh vertex.";
                return false;
            }

            Vector3[] vertices;
            try
            {
                vertices = mesh.vertices;
            }
            catch (Exception exception)
            {
                error = "The target mesh is not readable: " + exception.Message;
                return false;
            }

            bool longIsU = ResolveLongitudinalAxis(uvs);
            GetUvBounds(uvs, longIsU, out float minLong, out float maxLong, out _, out _);
            if (maxLong - minLong <= Mathf.Epsilon)
            {
                error = "The selected UV channel has no usable range along the chosen direction.";
                return false;
            }

            int count = Mathf.Max(2, m_GeneratedPointCount);
            var worldPoints = new Vector3[count];
            MultiSplineLoft loft = m_Target.GetComponent<MultiSplineLoft>();
            bool generatedFromLoft = loft != null;
            if (generatedFromLoft)
            {
                for (int pointIndex = 0; pointIndex < count; pointIndex++)
                {
                    if (!loft.TryEvaluateSurfaceCenterline(pointIndex / (float)(count - 1), out worldPoints[pointIndex]))
                    {
                        generatedFromLoft = false;
                        break;
                    }
                }
            }

            if (!generatedFromLoft)
            {
                float radius = 0.75f / (count - 1);
                Matrix4x4 localToWorld = m_Target.transform.localToWorldMatrix;

                for (int pointIndex = 0; pointIndex < count; pointIndex++)
                {
                    float targetT = pointIndex / (float)(count - 1);
                    Vector3 weightedPosition = Vector3.zero;
                    float weightTotal = 0f;
                    int closestVertex = 0;
                    float closestDistance = float.MaxValue;

                    for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                    {
                        float longitudinal = longIsU ? uvs[vertexIndex].x : uvs[vertexIndex].y;
                        float vertexT = Mathf.InverseLerp(minLong, maxLong, longitudinal);
                        float distance = Mathf.Abs(vertexT - targetT);
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            closestVertex = vertexIndex;
                        }

                        if (distance <= radius)
                        {
                            float weight = 1f - distance / radius;
                            weightedPosition += localToWorld.MultiplyPoint3x4(vertices[vertexIndex]) * weight;
                            weightTotal += weight;
                        }
                    }

                    worldPoints[pointIndex] = weightTotal > Mathf.Epsilon
                        ? weightedPosition / weightTotal
                        : localToWorld.MultiplyPoint3x4(vertices[closestVertex]);
                }
            }

            RebuildSpline(worldPoints);
            if (m_AutomaticMoveSensitivity)
                EstimateMoveSensitivity(vertices, uvs, longIsU, minLong, maxLong, worldPoints);
            return true;
        }

        public Mesh CreateUvMesh()
        {
            return CreateUvMesh(null);
        }

        Mesh CreateUvMesh(Mesh reusableOutput)
        {
            if (m_Target == null || m_Target.sharedMesh == null || Container.Spline.Count < 2)
                return null;

            Mesh source = ResolveSourceMesh();
            if (source == null)
                return null;
            bool canReuseSourceUvs = reusableOutput != null
                && m_CachedUvSource == source
                && m_CachedUvChannel == m_UvChannel
                && m_CachedSourceUvs.Count == source.vertexCount;
            if (!canReuseSourceUvs)
            {
                m_CachedSourceUvs.Clear();
                source.GetUVs(m_UvChannel, m_CachedSourceUvs);
                m_CachedUvSource = source;
                m_CachedUvChannel = m_UvChannel;
            }
            List<Vector2> sourceUvs = m_CachedSourceUvs;
            if (sourceUvs.Count != source.vertexCount)
                return null;

            EnsureControlPointCount(Container.Spline.Count);
            BuildKnotMoveOffsetCache();
            bool longIsU = ResolveLongitudinalAxis(sourceUvs);
            GetUvBounds(sourceUvs, longIsU, out float minLong, out float maxLong, out float minCross, out float maxCross);
            BuildLongInterpolationCache(minLong, maxLong);
            float pivot = m_AutoCrossPivot ? (minCross + maxCross) * 0.5f : m_CrossPivot;
            m_WorkingOutputUvs.Clear();
            if (m_WorkingOutputUvs.Capacity < sourceUvs.Count)
                m_WorkingOutputUvs.Capacity = sourceUvs.Count;

            for (int vertexIndex = 0; vertexIndex < sourceUvs.Count; vertexIndex++)
            {
                Vector2 uv = sourceUvs[vertexIndex];
                float longitudinal = longIsU ? uv.x : uv.y;
                float pathT = Mathf.InverseLerp(minLong, maxLong, longitudinal);
                FindSegment(pathT, out int segment, out float segmentT);
                float cross = longIsU ? uv.y : uv.x;
                int aIndex = segment;
                int bIndex = segment + 1;
                float longA = GetMappedLong(aIndex, longitudinal, minLong, maxLong);
                float longB = GetMappedLong(bIndex, longitudinal, minLong, maxLong);
                float crossA = GetMappedCross(aIndex, cross, pivot);
                float crossB = GetMappedCross(bIndex, cross, pivot);

                float outputLong;
                float outputCross;
                if (m_SmoothInterpolation)
                {
                    outputLong = EvaluateSmoothLong(segment, segmentT, minLong, maxLong);
                    outputCross = Mathf.SmoothStep(crossA, crossB, segmentT);
                }
                else
                {
                    outputLong = Mathf.Lerp(longA, longB, segmentT);
                    outputCross = Mathf.Lerp(crossA, crossB, segmentT);
                }
                m_WorkingOutputUvs.Add(longIsU ? new Vector2(outputLong, outputCross) : new Vector2(outputCross, outputLong));
            }

            Mesh result = reusableOutput != null && reusableOutput.vertexCount == source.vertexCount
                ? reusableOutput
                : Instantiate(source);
            if (result != reusableOutput)
                result.name = source.name + "_UVSpline";
            result.SetUVs(m_UvChannel, m_WorkingOutputUvs);
            return result;
        }

        public Mesh RebuildOutputMesh()
        {
            Mesh previousOutput = m_OutputMesh;
            Mesh source = ResolveSourceMesh();
            bool geometryIsUnchanged = previousOutput != null
                && m_Target != null
                && m_Target.sharedMesh == previousOutput
                && source != null
                && previousOutput.vertexCount == source.vertexCount;
            Mesh result = CreateUvMesh(geometryIsUnchanged ? previousOutput : null);
            if (result == null)
                return null;

            m_OutputMesh = result;
            m_Target.sharedMesh = result;
            if (previousOutput != null && previousOutput != result && previousOutput != m_SourceMesh)
                DestroyGeneratedMesh(previousOutput);
            return result;
        }

        public void RestoreSourceMesh()
        {
            if (m_Target != null && m_SourceMesh != null)
            {
                m_Target.sharedMesh = m_SourceMesh;
                if (m_Target.TryGetComponent(out MeshCollider collider))
                {
                    collider.sharedMesh = null;
                    collider.sharedMesh = m_SourceMesh;
                }
            }
            Mesh previousOutput = m_OutputMesh;
            m_OutputMesh = null;
            if (previousOutput != null && previousOutput != m_SourceMesh)
                DestroyGeneratedMesh(previousOutput);
        }

        public void AdoptSourceMesh(Mesh mesh)
        {
            Mesh previousOutput = m_OutputMesh;
            m_OutputMesh = null;
            m_SourceMesh = mesh;
            if (m_Target != null)
            {
                m_Target.sharedMesh = mesh;
                if (m_Target.TryGetComponent(out MeshCollider collider))
                {
                    collider.sharedMesh = null;
                    collider.sharedMesh = mesh;
                }
            }
            if (previousOutput != null && previousOutput != mesh)
                DestroyGeneratedMesh(previousOutput);
        }

        Mesh ResolveSourceMesh()
        {
            if (m_Target == null)
                return null;
            Mesh assigned = m_Target.sharedMesh;
            if (assigned != null && assigned != m_OutputMesh)
                m_SourceMesh = assigned;
            return m_SourceMesh != null ? m_SourceMesh : assigned;
        }

        static void DestroyGeneratedMesh(Mesh mesh)
        {
            if (mesh == null)
                return;
            if (Application.isPlaying)
                Destroy(mesh);
            else
                DestroyImmediate(mesh);
        }

        float GetMappedLong(int index, float longitudinal, float minLong, float maxLong)
        {
            ControlPoint point = m_ControlPoints[index];
            GetKnotMoveOffsets(index, out float alongMove, out _);
            float pointPivot = Mathf.Lerp(minLong, maxLong, point.pathT);
            return pointPivot + (longitudinal - pointPivot) * point.lengthScale + point.alongOffset + alongMove;
        }

        float GetLongAnchor(int index, float minLong, float maxLong)
        {
            ControlPoint point = m_ControlPoints[index];
            GetKnotMoveOffsets(index, out float alongMove, out _);
            return Mathf.Lerp(minLong, maxLong, point.pathT) + point.alongOffset + alongMove;
        }

        float EvaluateSmoothLong(int segment, float t, float minLong, float maxLong)
        {
            int aIndex = segment;
            int bIndex = segment + 1;
            float xA = Mathf.Lerp(minLong, maxLong, m_ControlPoints[aIndex].pathT);
            float xB = Mathf.Lerp(minLong, maxLong, m_ControlPoints[bIndex].pathT);
            float yA = m_CachedLongAnchors[aIndex];
            float yB = m_CachedLongAnchors[bIndex];
            float interval = Mathf.Max(Mathf.Epsilon, xB - xA);
            float secant = (yB - yA) / interval;

            float tangentA = m_CachedLongTangents[aIndex] * m_ControlPoints[aIndex].lengthScale;
            float tangentB = m_CachedLongTangents[bIndex] * m_ControlPoints[bIndex].lengthScale;
            LimitMonotoneTangents(secant, ref tangentA, ref tangentB);

            float t2 = t * t;
            float t3 = t2 * t;
            float h00 = 2f * t3 - 3f * t2 + 1f;
            float h10 = t3 - 2f * t2 + t;
            float h01 = -2f * t3 + 3f * t2;
            float h11 = t3 - t2;
            return h00 * yA + h10 * interval * tangentA + h01 * yB + h11 * interval * tangentB;
        }

        void BuildLongInterpolationCache(float minLong, float maxLong)
        {
            int count = m_ControlPoints.Count;
            if (m_CachedLongAnchors == null || m_CachedLongAnchors.Length != count)
            {
                m_CachedLongAnchors = new float[count];
                m_CachedLongTangents = new float[count];
            }

            for (int i = 0; i < count; i++)
                m_CachedLongAnchors[i] = GetLongAnchor(i, minLong, maxLong);
            for (int i = 0; i < count; i++)
                m_CachedLongTangents[i] = GetMonotoneLongTangent(i, minLong, maxLong);
        }

        float GetMonotoneLongTangent(int index, float minLong, float maxLong)
        {
            int last = m_ControlPoints.Count - 1;
            if (index <= 0)
                return GetLongSecant(0, 1, minLong, maxLong);
            if (index >= last)
                return GetLongSecant(last - 1, last, minLong, maxLong);

            float previous = GetLongSecant(index - 1, index, minLong, maxLong);
            float next = GetLongSecant(index, index + 1, minLong, maxLong);
            if (Mathf.Approximately(previous, 0f) || Mathf.Approximately(next, 0f) || Mathf.Sign(previous) != Mathf.Sign(next))
                return 0f;

            float xPrevious = Mathf.Lerp(minLong, maxLong, m_ControlPoints[index - 1].pathT);
            float xCurrent = Mathf.Lerp(minLong, maxLong, m_ControlPoints[index].pathT);
            float xNext = Mathf.Lerp(minLong, maxLong, m_ControlPoints[index + 1].pathT);
            float previousInterval = Mathf.Max(Mathf.Epsilon, xCurrent - xPrevious);
            float nextInterval = Mathf.Max(Mathf.Epsilon, xNext - xCurrent);
            float previousWeight = 2f * nextInterval + previousInterval;
            float nextWeight = nextInterval + 2f * previousInterval;
            return (previousWeight + nextWeight) /
                (previousWeight / previous + nextWeight / next);
        }

        float GetLongSecant(int aIndex, int bIndex, float minLong, float maxLong)
        {
            float xA = Mathf.Lerp(minLong, maxLong, m_ControlPoints[aIndex].pathT);
            float xB = Mathf.Lerp(minLong, maxLong, m_ControlPoints[bIndex].pathT);
            return (GetLongAnchor(bIndex, minLong, maxLong) - GetLongAnchor(aIndex, minLong, maxLong)) /
                Mathf.Max(Mathf.Epsilon, xB - xA);
        }

        static void LimitMonotoneTangents(float secant, ref float tangentA, ref float tangentB)
        {
            if (Mathf.Abs(secant) <= Mathf.Epsilon)
            {
                tangentA = 0f;
                tangentB = 0f;
                return;
            }

            float a = tangentA / secant;
            float b = tangentB / secant;
            if (a < 0f)
            {
                tangentA = 0f;
                a = 0f;
            }
            if (b < 0f)
            {
                tangentB = 0f;
                b = 0f;
            }

            float magnitude = a * a + b * b;
            if (magnitude <= 9f)
                return;

            float scale = 3f / Mathf.Sqrt(magnitude);
            tangentA = scale * a * secant;
            tangentB = scale * b * secant;
        }

        float GetMappedCross(int index, float cross, float pivot)
        {
            ControlPoint point = m_ControlPoints[index];
            GetKnotMoveOffsets(index, out _, out float sideMove);
            return pivot + (cross - pivot) * point.widthScale + point.sideOffset + sideMove;
        }

        public void ResetControls()
        {
            EnsureControlPointCount(Mathf.Max(2, Container.Spline.Count));
            for (int i = 0; i < m_ControlPoints.Count; i++)
            {
                m_ControlPoints[i].widthScale = 1f;
                m_ControlPoints[i].lengthScale = 1f;
                m_ControlPoints[i].sideOffset = 0f;
                m_ControlPoints[i].alongOffset = 0f;
            }
        }

        void RebuildSpline(IReadOnlyList<Vector3> worldPoints)
        {
            List<ControlPoint> previous = new List<ControlPoint>(m_ControlPoints.Count);
            List<Vector3> previousKnotOffsets = new List<Vector3>(m_ControlPoints.Count);
            UnityEngine.Splines.Spline previousSpline = Container.Spline;
            foreach (ControlPoint point in m_ControlPoints)
                previous.Add(point.Clone());
            for (int i = 0; i < m_ControlPoints.Count; i++)
            {
                Vector3 offset = i < previousSpline.Count
                    ? (Vector3)previousSpline[i].Position - m_ControlPoints[i].generatedLocalPosition
                    : Vector3.zero;
                previousKnotOffsets.Add(offset);
            }

            var spline = new UnityEngine.Splines.Spline(worldPoints.Count, false);
            Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
            for (int i = 0; i < worldPoints.Count; i++)
            {
                float t = i / (float)(worldPoints.Count - 1);
                Vector3 generatedPosition = worldToLocal.MultiplyPoint3x4(worldPoints[i]);
                Vector3 preservedOffset = SamplePreviousOffset(previousKnotOffsets, t);
                spline.Add(new BezierKnot(generatedPosition + preservedOffset), TangentMode.AutoSmooth);
            }

            Container.Spline = spline;
            m_ControlPoints.Clear();
            for (int i = 0; i < worldPoints.Count; i++)
            {
                float t = i / (float)(worldPoints.Count - 1);
                ControlPoint point = SamplePrevious(previous, t);
                point.pathT = t;
                point.generatedLocalPosition = worldToLocal.MultiplyPoint3x4(worldPoints[i]);
                m_ControlPoints.Add(point);
            }
        }

        static Vector3 SamplePreviousOffset(IReadOnlyList<Vector3> offsets, float t)
        {
            if (offsets == null || offsets.Count == 0)
                return Vector3.zero;
            if (offsets.Count == 1)
                return offsets[0];

            float scaled = Mathf.Clamp01(t) * (offsets.Count - 1);
            int a = Mathf.Min(Mathf.FloorToInt(scaled), offsets.Count - 2);
            int b = a + 1;
            return Vector3.Lerp(offsets[a], offsets[b], scaled - a);
        }

        void BuildKnotMoveOffsetCache()
        {
            int count = Mathf.Min(Container.Spline.Count, m_ControlPoints.Count);
            if (m_CachedAlongMoveOffsets == null || m_CachedAlongMoveOffsets.Length != count)
            {
                m_CachedAlongMoveOffsets = new float[count];
                m_CachedSideMoveOffsets = new float[count];
            }

            for (int i = 0; i < count; i++)
                ComputeKnotMoveOffsets(i, out m_CachedAlongMoveOffsets[i], out m_CachedSideMoveOffsets[i]);
        }

        void GetKnotMoveOffsets(int index, out float alongOffset, out float sideOffset)
        {
            if (m_CachedAlongMoveOffsets != null && m_CachedSideMoveOffsets != null
                && index >= 0 && index < m_CachedAlongMoveOffsets.Length)
            {
                alongOffset = m_CachedAlongMoveOffsets[index];
                sideOffset = m_CachedSideMoveOffsets[index];
                return;
            }
            ComputeKnotMoveOffsets(index, out alongOffset, out sideOffset);
        }

        void ComputeKnotMoveOffsets(int index, out float alongOffset, out float sideOffset)
        {
            alongOffset = 0f;
            sideOffset = 0f;
            if (!m_MovingKnotsOffsetsUv || index < 0 || index >= Container.Spline.Count || index >= m_ControlPoints.Count)
                return;

            // Across movement stays local to this control point. Interpolation then
            // confines its bend to the intervals on either side of the selected knot.
            GetRawKnotMoveOffsets(index, out _, out sideOffset);

            float activeWeight = 0f;
            float radius = Mathf.Max(0.25f, m_MoveFalloffPoints);
            int count = Mathf.Min(Container.Spline.Count, m_ControlPoints.Count);
            for (int sourceIndex = 0; sourceIndex < count; sourceIndex++)
            {
                GetRawKnotMoveOffsets(sourceIndex, out float sourceAlong, out _);
                if (Mathf.Abs(sourceAlong) <= Mathf.Epsilon)
                    continue;

                float distance = (index - sourceIndex) / radius;
                float weight = Mathf.Exp(-0.5f * distance * distance);
                alongOffset += sourceAlong * weight;
                activeWeight += weight;
            }

            // A single edited knot keeps its full value at the handle. When several
            // edited knots overlap, average their influence instead of amplifying it.
            if (activeWeight > 1f)
            {
                alongOffset /= activeWeight;
            }
        }

        void GetRawKnotMoveOffsets(int index, out float alongOffset, out float sideOffset)
        {
            alongOffset = 0f;
            sideOffset = 0f;
            if (index < 0 || index >= Container.Spline.Count || index >= m_ControlPoints.Count)
                return;

            Vector3 current = Container.Spline[index].Position;
            Vector3 worldDelta = transform.TransformVector(current - m_ControlPoints[index].generatedLocalPosition);
            Vector3 tangent = GetKnotTangent(index);
            Vector3 side = Vector3.Cross(Vector3.up, tangent).normalized;
            if (side.sqrMagnitude <= Mathf.Epsilon)
                side = Vector3.Cross(Vector3.forward, tangent).normalized;

            alongOffset = -Vector3.Dot(worldDelta, tangent) * m_AlongUvPerWorldUnit;
            sideOffset = -Vector3.Dot(worldDelta, side) * m_SideUvPerWorldUnit;
        }

        Vector3 GetKnotTangent(int index)
        {
            int count = Container.Spline.Count;
            Vector3 current = transform.TransformPoint((Vector3)Container.Spline[index].Position);
            Vector3 tangent;
            if (index <= 0)
                tangent = transform.TransformPoint((Vector3)Container.Spline[1].Position) - current;
            else if (index >= count - 1)
                tangent = current - transform.TransformPoint((Vector3)Container.Spline[count - 2].Position);
            else
                tangent = transform.TransformPoint((Vector3)Container.Spline[index + 1].Position) - transform.TransformPoint((Vector3)Container.Spline[index - 1].Position);
            return tangent.sqrMagnitude > Mathf.Epsilon ? tangent.normalized : Vector3.forward;
        }

        void EstimateMoveSensitivity(Vector3[] localVertices, List<Vector2> uvs, bool longIsU, float minLong, float maxLong, IReadOnlyList<Vector3> worldPoints)
        {
            float pathLength = 0f;
            for (int i = 1; i < worldPoints.Count; i++)
                pathLength += Vector3.Distance(worldPoints[i - 1], worldPoints[i]);
            if (pathLength > Mathf.Epsilon)
                m_AlongUvPerWorldUnit = (maxLong - minLong) / pathLength;

            GetUvBounds(uvs, longIsU, out _, out _, out float minCross, out float maxCross);
            Matrix4x4 localToWorld = m_Target.transform.localToWorldMatrix;
            float totalHalfWidth = 0f;
            int samples = 0;
            int step = Mathf.Max(1, localVertices.Length / 20000);
            for (int i = 0; i < localVertices.Length; i += step)
            {
                float longitudinal = longIsU ? uvs[i].x : uvs[i].y;
                float t = Mathf.InverseLerp(minLong, maxLong, longitudinal);
                float scaled = t * (worldPoints.Count - 1);
                int segment = Mathf.Min(Mathf.FloorToInt(scaled), worldPoints.Count - 2);
                Vector3 center = Vector3.Lerp(worldPoints[segment], worldPoints[segment + 1], scaled - segment);
                totalHalfWidth += Vector3.Distance(localToWorld.MultiplyPoint3x4(localVertices[i]), center);
                samples++;
            }

            float averageHalfWidth = samples > 0 ? totalHalfWidth / samples : 0f;
            if (averageHalfWidth > Mathf.Epsilon)
                m_SideUvPerWorldUnit = (maxCross - minCross) / (averageHalfWidth * 2f);
        }

        void EnsureControlPointCount(int count)
        {
            count = Mathf.Max(2, count);
            if (m_ControlPoints.Count == count)
            {
                for (int i = 0; i < count; i++)
                    m_ControlPoints[i].pathT = i / (float)(count - 1);
                return;
            }

            var previous = new List<ControlPoint>(m_ControlPoints);
            m_ControlPoints.Clear();
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                ControlPoint point = SamplePrevious(previous, t);
                point.pathT = t;
                m_ControlPoints.Add(point);
            }
        }

        static ControlPoint SamplePrevious(IReadOnlyList<ControlPoint> points, float t)
        {
            if (points == null || points.Count == 0)
                return new ControlPoint { pathT = t };
            if (points.Count == 1)
                return points[0].Clone();

            float scaled = Mathf.Clamp01(t) * (points.Count - 1);
            int a = Mathf.Min(Mathf.FloorToInt(scaled), points.Count - 2);
            int b = a + 1;
            float blend = scaled - a;
            return new ControlPoint
            {
                pathT = t,
                widthScale = Mathf.Lerp(points[a].widthScale, points[b].widthScale, blend),
                lengthScale = Mathf.Lerp(points[a].lengthScale, points[b].lengthScale, blend),
                sideOffset = Mathf.Lerp(points[a].sideOffset, points[b].sideOffset, blend),
                alongOffset = Mathf.Lerp(points[a].alongOffset, points[b].alongOffset, blend)
            };
        }

        bool ResolveLongitudinalAxis(List<Vector2> uvs)
        {
            if (m_LongitudinalAxis != LongitudinalAxis.Auto)
                return m_LongitudinalAxis == LongitudinalAxis.U;
            GetUvBounds(uvs, true, out float minU, out float maxU, out float minV, out float maxV);
            return maxU - minU >= maxV - minV;
        }

        static void GetUvBounds(List<Vector2> uvs, bool longIsU, out float minLong, out float maxLong, out float minCross, out float maxCross)
        {
            minLong = minCross = float.MaxValue;
            maxLong = maxCross = float.MinValue;
            for (int i = 0; i < uvs.Count; i++)
            {
                float longitudinal = longIsU ? uvs[i].x : uvs[i].y;
                float cross = longIsU ? uvs[i].y : uvs[i].x;
                minLong = Mathf.Min(minLong, longitudinal);
                maxLong = Mathf.Max(maxLong, longitudinal);
                minCross = Mathf.Min(minCross, cross);
                maxCross = Mathf.Max(maxCross, cross);
            }
        }

        void FindSegment(float pathT, out int segment, out float segmentT)
        {
            int last = m_ControlPoints.Count - 1;
            if (pathT <= m_ControlPoints[0].pathT) { segment = 0; segmentT = 0f; return; }
            if (pathT >= m_ControlPoints[last].pathT) { segment = last - 1; segmentT = 1f; return; }
            segment = 0;
            while (segment < last - 1 && pathT > m_ControlPoints[segment + 1].pathT)
                segment++;
            float range = m_ControlPoints[segment + 1].pathT - m_ControlPoints[segment].pathT;
            segmentT = range > Mathf.Epsilon ? (pathT - m_ControlPoints[segment].pathT) / range : 0f;
        }
    }
}
