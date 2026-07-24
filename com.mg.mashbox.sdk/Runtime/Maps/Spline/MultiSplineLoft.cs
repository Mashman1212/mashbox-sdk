using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.Painting;
using MashBoxSDK.Maps.Sculpting;
using UnityEngine;
using UnityEngine.Splines;
using UnitySpline = UnityEngine.Splines.Spline;

namespace MashBoxSDK.Maps.Spline
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class MultiSplineLoft : MonoBehaviour
    {
        const string GeneratedColliderTag = "dirt";
        static bool s_HasWarnedAboutMissingColliderTag;

        public enum NormalMode
        {
            Recalculate,
            SmoothedFaces,
            Face,
            LoftGrid
        }

        public enum AcrossInterpolation
        {
            Linear,
            CatmullRom
        }

        public enum AlongResolutionMode
        {
            FixedSamples,
            Distance
        }

        public enum AlongAlignment
        {
            Parameter,
            ReferencePerpendicular
        }

        [Serializable]
        public sealed class ResolutionZone
        {
            [Range(0f, 1f)]
            public float center = 0.5f;

            [Range(0.001f, 1f)]
            public float radius = 0.1f;

            [Min(0.1f)]
            public float density = 2f;
        }

        [Serializable]
        public sealed class SplineSource
        {
            public SplineContainer container;
            public int splineIndex;
            public bool reverse;

            public bool IsValid => container != null
                && container.Splines != null
                && splineIndex >= 0
                && splineIndex < container.Splines.Count
                && container.Splines[splineIndex] != null
                && container.Splines[splineIndex].Count >= 2;
        }

        [SerializeField]
        List<SplineSource> m_Sources = new List<SplineSource>();

        [SerializeField, Min(2)]
        int m_SamplesAlong = 32;

        [SerializeField, Min(1)]
        int m_SegmentsAcross = 4;

        [SerializeField]
        AcrossInterpolation m_AcrossInterpolation = AcrossInterpolation.CatmullRom;

        [SerializeField]
        AlongResolutionMode m_AlongResolutionMode = AlongResolutionMode.Distance;

        [SerializeField]
        AlongAlignment m_AlongAlignment = AlongAlignment.ReferencePerpendicular;

        [SerializeField]
        int m_AlignmentReferenceSource = -1;

        [SerializeField, Min(0.01f)]
        float m_TargetSegmentLength = 1f;

        [SerializeField, Min(2)]
        int m_MaxDistanceSamples = 10000;

        [SerializeField]
        List<ResolutionZone> m_ResolutionZones = new List<ResolutionZone>();

        [SerializeField]
        bool m_GenerateResolutionSplineWithLoft;

        [SerializeField, Min(2)]
        int m_ResolutionSplinePointCount = 8;

        [SerializeField]
        LoftResolutionSpline m_ResolutionSpline;

        [SerializeField]
        bool m_AutoRegenerate = true;

        [SerializeField]
        bool m_CloseAlongClosedSplines = true;

        [SerializeField]
        bool m_CloseAcrossSplines;

        [SerializeField]
        bool m_CapStart;

        [SerializeField]
        bool m_CapEnd;

        [SerializeField]
        bool m_DoubleSided;

        [SerializeField]
        bool m_UpdateMeshCollider = true;

        [SerializeField, Min(1f)]
        float m_ColliderChunkLength = 50f;

        [SerializeField]
        NormalMode m_NormalMode = NormalMode.SmoothedFaces;

        [SerializeField]
        bool m_FlipNormals = true;

        [SerializeField, Min(0.0001f)]
        float m_UvScaleAlong = 1f;

        [SerializeField, Min(0.0001f)]
        float m_UvScaleAcross = 1f;

        [SerializeField]
        bool m_GenerateUvSplineWithLoft;

        [SerializeField, Range(0, 3)]
        int m_UvSplineChannel;

        [SerializeField]
        UVSpline.LongitudinalAxis m_UvSplineDirection = UVSpline.LongitudinalAxis.V;

        [SerializeField, Min(2)]
        int m_UvSplinePointCount = 8;

        [SerializeField]
        UVSpline m_UvSpline;

        [SerializeField]
        MeshSculptModifier m_SculptModifier;

        [SerializeField]
        VertexPaintModifier m_VertexPaintModifier;

        [SerializeField]
        LoftShoulderModifier m_ShoulderModifier;

        [SerializeField]
        Mesh m_GeneratedMesh;

        readonly List<Vector3> m_Vertices = new List<Vector3>();
        readonly List<Vector3> m_Normals = new List<Vector3>();
        readonly List<Vector2> m_Uvs = new List<Vector2>();
        readonly List<int> m_Triangles = new List<int>();
        readonly List<int> m_ShoulderColliderSubmeshes = new List<int>();
        readonly List<Vector3> m_FlatVertices = new List<Vector3>();
        readonly List<Vector3> m_FlatNormals = new List<Vector3>();
        readonly List<Vector2> m_FlatUvs = new List<Vector2>();
        readonly List<int> m_FlatTriangles = new List<int>();
        readonly List<int> m_ValidSourceIndices = new List<int>();
        readonly List<float> m_CrossDistances = new List<float>();
        readonly List<float> m_AlongDistances = new List<float>();
        readonly List<float> m_AlongParameters = new List<float>();
        readonly List<float> m_DistanceLookup = new List<float>();
        readonly List<Vector3> m_PreviousSourceSamples = new List<Vector3>();
        readonly List<Vector3> m_CurrentSourceSamples = new List<Vector3>();
        Vector3[,] m_SourcePoints;
        Vector3[,] m_SampledPoints;
        int m_CrossSampleCount;
        int m_AlongSampleCount;
        int m_SurfaceVertexCount;
        int m_SurfaceTriangleCount;
        int m_PrimaryTriangleCount;
        bool m_RegenerateQueued;
        int m_GenerationVersion;

        public List<SplineSource> Sources => m_Sources;
        public int SamplesAlong { get => m_SamplesAlong; set => m_SamplesAlong = Mathf.Max(2, value); }
        public int SegmentsAcross { get => m_SegmentsAcross; set => m_SegmentsAcross = Mathf.Max(1, value); }
        public int CurrentSamplesAlong => m_AlongSampleCount;
        public int CurrentSamplesAcross => m_CrossSampleCount;
        public int GenerationVersion => m_GenerationVersion;
        public AcrossInterpolation AcrossMode { get => m_AcrossInterpolation; set => m_AcrossInterpolation = value; }
        public AlongResolutionMode AlongResolution { get => m_AlongResolutionMode; set => m_AlongResolutionMode = value; }
        public AlongAlignment Alignment { get => m_AlongAlignment; set => m_AlongAlignment = value; }
        public int AlignmentReferenceSource { get => m_AlignmentReferenceSource; set => m_AlignmentReferenceSource = value; }
        public float TargetSegmentLength { get => m_TargetSegmentLength; set => m_TargetSegmentLength = Mathf.Max(0.01f, value); }
        public bool GenerateResolutionSplineWithLoft { get => m_GenerateResolutionSplineWithLoft; set => m_GenerateResolutionSplineWithLoft = value; }
        public int ResolutionSplinePointCount { get => m_ResolutionSplinePointCount; set => m_ResolutionSplinePointCount = Mathf.Max(2, value); }
        public LoftResolutionSpline GeneratedResolutionSpline => ResolveResolutionSpline();
        public bool AutoRegenerate { get => m_AutoRegenerate; set => m_AutoRegenerate = value; }
        public bool CloseAlongClosedSplines { get => m_CloseAlongClosedSplines; set => m_CloseAlongClosedSplines = value; }
        public bool CloseAcrossSplines { get => m_CloseAcrossSplines; set => m_CloseAcrossSplines = value; }
        public bool CapStart { get => m_CapStart; set => m_CapStart = value; }
        public bool CapEnd { get => m_CapEnd; set => m_CapEnd = value; }
        public bool DoubleSided { get => m_DoubleSided; set => m_DoubleSided = value; }
        public bool UpdateMeshCollider { get => m_UpdateMeshCollider; set => m_UpdateMeshCollider = value; }
        public float ColliderChunkLength { get => m_ColliderChunkLength; set => m_ColliderChunkLength = Mathf.Max(1f, value); }
        public NormalMode SurfaceNormalMode { get => m_NormalMode; set => m_NormalMode = value; }
        public bool FlipNormals { get => m_FlipNormals; set => m_FlipNormals = value; }
        public bool GenerateUvSplineWithLoft { get => m_GenerateUvSplineWithLoft; set => m_GenerateUvSplineWithLoft = value; }
        public int UvSplineChannel { get => m_UvSplineChannel; set => m_UvSplineChannel = Mathf.Clamp(value, 0, 3); }
        public UVSpline.LongitudinalAxis UvSplineDirection { get => m_UvSplineDirection; set => m_UvSplineDirection = value; }
        public int UvSplinePointCount { get => m_UvSplinePointCount; set => m_UvSplinePointCount = Mathf.Max(2, value); }
        public UVSpline GeneratedUvSpline => m_UvSpline;
        public MeshSculptModifier SculptModifier { get => m_SculptModifier; set => m_SculptModifier = value; }
        public VertexPaintModifier VertexPaintModifier { get => ResolveVertexPaintModifier(); set => m_VertexPaintModifier = value; }
        public LoftShoulderModifier ShoulderModifier { get => ResolveShoulderModifier(); set => m_ShoulderModifier = value; }
        public Mesh GeneratedMesh => m_GeneratedMesh;

        public bool SynchronizeUvSplineSettings(UVSpline uvSpline)
        {
            if (uvSpline == null)
                return false;

            UVSpline generatedUvSpline = m_UvSpline != null
                ? m_UvSpline
                : GetComponentInChildren<UVSpline>(true);
            if (generatedUvSpline != uvSpline)
                return false;

            m_UvSpline = uvSpline;
            m_UvSplineChannel = uvSpline.UvChannel;
            m_UvSplineDirection = uvSpline.Direction;
            m_UvSplinePointCount = uvSpline.GeneratedPointCount;
            return true;
        }

        public void SetShoulderColliderSubmeshes(IEnumerable<int> submeshIndices)
        {
            m_ShoulderColliderSubmeshes.Clear();
            if (submeshIndices == null)
                return;
            foreach (int submeshIndex in submeshIndices)
            {
                if (submeshIndex > 0 && !m_ShoulderColliderSubmeshes.Contains(submeshIndex))
                    m_ShoulderColliderSubmeshes.Add(submeshIndex);
            }
        }

        public void SetGeneratedMesh(Mesh mesh)
        {
            m_GeneratedMesh = mesh;
            EnsureMesh();
        }

        void OnEnable()
        {
            EnsureMesh();
            UnitySpline.Changed += OnSplineChanged;
            SplineContainer.SplineAdded += OnSplineSetChanged;
            SplineContainer.SplineRemoved += OnSplineSetChanged;
            SplineContainer.SplineReordered += OnSplineReordered;

            if (m_AutoRegenerate)
                QueueRegenerate();
        }

        void OnDisable()
        {
            UnitySpline.Changed -= OnSplineChanged;
            SplineContainer.SplineAdded -= OnSplineSetChanged;
            SplineContainer.SplineRemoved -= OnSplineSetChanged;
            SplineContainer.SplineReordered -= OnSplineReordered;
        }

        void OnValidate()
        {
            m_SamplesAlong = Mathf.Max(2, m_SamplesAlong);
            m_SegmentsAcross = Mathf.Max(1, m_SegmentsAcross);
            m_TargetSegmentLength = Mathf.Max(0.01f, m_TargetSegmentLength);
            m_MaxDistanceSamples = Mathf.Max(2, m_MaxDistanceSamples);
            m_ResolutionSplinePointCount = Mathf.Max(2, m_ResolutionSplinePointCount);
            m_ColliderChunkLength = Mathf.Max(1f, m_ColliderChunkLength);
            m_UvScaleAlong = Mathf.Max(0.0001f, m_UvScaleAlong);
            m_UvScaleAcross = Mathf.Max(0.0001f, m_UvScaleAcross);
            m_UvSplineChannel = Mathf.Clamp(m_UvSplineChannel, 0, 3);
            m_UvSplinePointCount = Mathf.Max(2, m_UvSplinePointCount);

            for (int i = 0; i < m_Sources.Count; i++)
            {
                if (m_Sources[i] == null)
                    m_Sources[i] = new SplineSource();
                else if (m_Sources[i].container != null)
                    m_Sources[i].splineIndex = Mathf.Clamp(m_Sources[i].splineIndex, 0, Mathf.Max(0, m_Sources[i].container.Splines.Count - 1));
            }

            for (int i = 0; i < m_ResolutionZones.Count; i++)
            {
                if (m_ResolutionZones[i] == null)
                    m_ResolutionZones[i] = new ResolutionZone();

                m_ResolutionZones[i].center = Mathf.Clamp01(m_ResolutionZones[i].center);
                m_ResolutionZones[i].radius = Mathf.Clamp(m_ResolutionZones[i].radius, 0.001f, 1f);
                m_ResolutionZones[i].density = Mathf.Max(0.1f, m_ResolutionZones[i].density);
            }

            if (isActiveAndEnabled && m_AutoRegenerate)
                QueueRegenerate();
        }

        void Update()
        {
            if (!m_RegenerateQueued)
                return;

            m_RegenerateQueued = false;
            Regenerate();
        }

        void OnSplineChanged(UnitySpline spline, int knotIndex, SplineModification modification)
        {
            if (!m_AutoRegenerate || spline == null)
                return;

            for (int i = 0; i < m_Sources.Count; i++)
            {
                var source = m_Sources[i];
                if (source?.IsValid == true && source.container.Splines[source.splineIndex] == spline)
                {
                    QueueRegenerate();
                    return;
                }
            }
        }

        void OnSplineSetChanged(SplineContainer container, int index)
        {
            if (m_AutoRegenerate && UsesContainer(container))
                QueueRegenerate();
        }

        void OnSplineReordered(SplineContainer container, int previousIndex, int newIndex)
        {
            if (m_AutoRegenerate && UsesContainer(container))
                QueueRegenerate();
        }

        bool UsesContainer(SplineContainer container)
        {
            if (container == null)
                return false;

            for (int i = 0; i < m_Sources.Count; i++)
            {
                if (m_Sources[i]?.container == container)
                    return true;
            }

            return false;
        }

        public void QueueRegenerate()
        {
            m_RegenerateQueued = true;
        }

        public void Regenerate()
        {
            m_RegenerateQueued = false;

            // Generated meshes and modifier children are allowed to change, but the
            // authored loft root is not. Preserve its transform defensively around
            // the entire regeneration pipeline, including callbacks and early exits.
            Transform loftTransform = transform;
            Vector3 localPosition = loftTransform.localPosition;
            Quaternion localRotation = loftTransform.localRotation;
            Vector3 localScale = loftTransform.localScale;
            bool hadChanged = loftTransform.hasChanged;

            try
            {
                RegenerateInternal();
            }
            finally
            {
                if (loftTransform.localPosition != localPosition)
                    loftTransform.localPosition = localPosition;
                if (loftTransform.localRotation != localRotation)
                    loftTransform.localRotation = localRotation;
                if (loftTransform.localScale != localScale)
                    loftTransform.localScale = localScale;
                loftTransform.hasChanged = hadChanged;
            }
        }

        void RegenerateInternal()
        {
            EnsureMesh();
            ClearBuildBuffers();
            m_ShoulderColliderSubmeshes.Clear();
            CollectValidSources();

            if (m_ValidSourceIndices.Count < 2)
            {
                ApplyMesh();
                ResolveShoulderModifier()?.ClearGenerated();
                RebuildColliderChunks();
                unchecked { m_GenerationVersion++; }
                return;
            }

            if (ResolveResolutionSpline() != null || m_GenerateResolutionSplineWithLoft)
                GenerateResolutionSpline(out _);

            BuildSampleGrid();
            BuildVertices();
            BuildSurfaceTriangles();
            m_SurfaceTriangleCount = m_Triangles.Count;

            if (m_CapStart)
                BuildCap(0, false);

            if (m_CapEnd)
                BuildCap(m_AlongSampleCount - 1, true);

            m_PrimaryTriangleCount = m_Triangles.Count;

            if (m_DoubleSided)
                DuplicateBackFaces();

            ApplyMesh();

            ResolveShoulderModifier()?.RebuildFromLoft(this);

            if (m_SculptModifier != null)
                m_SculptModifier.ApplyToFreshMesh(m_GeneratedMesh);

            ResolveVertexPaintModifier()?.ApplyToFreshMesh(m_GeneratedMesh);

            RebuildColliderChunks();

            if (m_GenerateUvSplineWithLoft || (m_UvSpline != null && m_UvSpline.OutputMesh != null))
                RegenerateUvSpline(out _);

            unchecked { m_GenerationVersion++; }
        }

        public bool RegenerateUvSpline(out string error)
        {
            error = null;
            if (m_GeneratedMesh == null || m_GeneratedMesh.vertexCount == 0)
            {
                error = "Generate a valid loft mesh before generating its UV spline.";
                return false;
            }

            EnsureUvSpline();
            m_UvSpline.Target = GetComponent<MeshFilter>();
            m_UvSpline.UvChannel = m_UvSplineChannel;
            m_UvSpline.Direction = m_UvSplineDirection;
            m_UvSpline.GeneratedPointCount = m_UvSplinePointCount;
            if (!m_UvSpline.GenerateFromTarget(out error))
                return false;
            // The generated loft reuses one Mesh instance. Vertex count equality
            // therefore does not mean the UV output's geometry or colors are still
            // current after regeneration.
            m_UvSpline.RebuildOutputMesh(forceSourceRefresh: true);
            return true;
        }

        public bool GenerateResolutionSpline(out string error)
        {
            error = null;
            if (!TryEvaluateResolutionCenterline(0f, out _))
            {
                error = "The loft needs at least two valid source splines before generating a resolution spline.";
                return false;
            }

            EnsureResolutionSpline();
            m_ResolutionSpline.Loft = this;
            m_ResolutionSpline.GeneratedPointCount = m_ResolutionSplinePointCount;
            return m_ResolutionSpline.GenerateFromLoft(out error);
        }

        public bool TryEvaluateResolutionCenterline(float pathT, out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;
            var validSourceIndices = new List<int>();
            for (int index = 0; index < m_Sources.Count; index++)
            {
                if (m_Sources[index]?.IsValid == true)
                    validSourceIndices.Add(index);
            }

            if (validSourceIndices.Count < 2)
                return false;

            int sourceListIndex = validSourceIndices.Count / 2;
            if (m_AlignmentReferenceSource >= 0)
            {
                int configuredIndex = validSourceIndices.IndexOf(m_AlignmentReferenceSource);
                if (configuredIndex >= 0)
                    sourceListIndex = configuredIndex;
            }

            SplineSource source = m_Sources[validSourceIndices[sourceListIndex]];
            worldPosition = EvaluateSourcePosition(source, Mathf.Clamp01(pathT));
            return IsFinite(worldPosition);
        }

        public bool TryEvaluateSurfaceCenterline(float pathT, out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;
            if (m_SampledPoints == null || m_CrossSampleCount < 2 || m_AlongSampleCount < 2)
                return false;

            Vector3[] renderedVertices = m_NormalMode != NormalMode.Face && m_GeneratedMesh != null
                ? m_GeneratedMesh.vertices
                : null;
            bool canUseRenderedVertices = renderedVertices != null && renderedVertices.Length >= m_SurfaceVertexCount;

            Vector3 CenterAtColumn(int along)
            {
                Vector3 center = Vector3.zero;
                for (int cross = 0; cross < m_CrossSampleCount; cross++)
                {
                    int vertexIndex = VertexIndex(cross, along);
                    center += canUseRenderedVertices && vertexIndex < renderedVertices.Length
                        ? renderedVertices[vertexIndex]
                        : m_SampledPoints[cross, along];
                }

                return center / m_CrossSampleCount;
            }

            float normalizedDistance = Mathf.Clamp01(pathT);
            int lower = 0;
            int upper = m_AlongSampleCount - 1;
            float blend = normalizedDistance;

            if (m_AlongDistances.Count == m_AlongSampleCount && m_AlongDistances[m_AlongSampleCount - 1] > Mathf.Epsilon)
            {
                float targetDistance = normalizedDistance * m_AlongDistances[m_AlongSampleCount - 1];
                upper = 1;
                while (upper < m_AlongSampleCount - 1 && m_AlongDistances[upper] < targetDistance)
                    upper++;

                lower = Mathf.Max(0, upper - 1);
                blend = Mathf.InverseLerp(m_AlongDistances[lower], m_AlongDistances[upper], targetDistance);
            }
            else
            {
                float samplePosition = normalizedDistance * (m_AlongSampleCount - 1);
                lower = Mathf.Min(Mathf.FloorToInt(samplePosition), m_AlongSampleCount - 2);
                upper = lower + 1;
                blend = samplePosition - lower;
            }

            Vector3 localPosition = Vector3.Lerp(CenterAtColumn(lower), CenterAtColumn(upper), blend);
            worldPosition = transform.TransformPoint(localPosition);
            return IsFinite(worldPosition);
        }

        void EnsureUvSpline()
        {
            if (m_UvSpline == null)
                m_UvSpline = GetComponentInChildren<UVSpline>(true);

            if (m_UvSpline != null)
                return;

            var splineObject = new GameObject("UV Spline", typeof(SplineContainer), typeof(UVSpline));
            splineObject.transform.SetParent(transform, false);
            m_UvSpline = splineObject.GetComponent<UVSpline>();
        }

        void EnsureResolutionSpline()
        {
            if (ResolveResolutionSpline() != null)
                return;

            var splineObject = new GameObject("Loft Resolution Spline", typeof(SplineContainer), typeof(LoftResolutionSpline));
            splineObject.transform.SetParent(transform, false);
            m_ResolutionSpline = splineObject.GetComponent<LoftResolutionSpline>();
            m_ResolutionSpline.Loft = this;
        }

        LoftResolutionSpline ResolveResolutionSpline()
        {
            if (m_ResolutionSpline == null)
                m_ResolutionSpline = GetComponentInChildren<LoftResolutionSpline>(true);
            return m_ResolutionSpline;
        }

        VertexPaintModifier ResolveVertexPaintModifier()
        {
            if (m_VertexPaintModifier == null)
                m_VertexPaintModifier = GetComponent<VertexPaintModifier>();

            if (m_VertexPaintModifier != null
                && (m_VertexPaintModifier.LinkedLoft != this || m_VertexPaintModifier.Target != GetComponent<MeshFilter>()))
            {
                m_VertexPaintModifier.LinkToLoft(this);
            }

            return m_VertexPaintModifier;
        }

        LoftShoulderModifier ResolveShoulderModifier()
        {
            if (m_ShoulderModifier == null)
                m_ShoulderModifier = GetComponentInChildren<LoftShoulderModifier>(true);
            return m_ShoulderModifier;
        }

        public bool TryGetShoulderEdge(
            LoftShoulderModifier.Edge edge,
            List<Vector3> boundary,
            List<Vector3> inner,
            List<float> pathDistances)
        {
            boundary?.Clear();
            inner?.Clear();
            pathDistances?.Clear();
            if (boundary == null || inner == null || pathDistances == null || m_SampledPoints == null || m_CrossSampleCount < 2 || m_AlongSampleCount < 2)
                return false;

            Vector3[] renderedVertices = m_NormalMode != NormalMode.Face && m_GeneratedMesh != null
                ? m_GeneratedMesh.vertices
                : null;
            bool canUseRenderedVertices = renderedVertices != null && renderedVertices.Length >= m_SurfaceVertexCount;

            Vector3 GetPoint(int cross, int along)
            {
                int vertexIndex = VertexIndex(cross, along);
                return canUseRenderedVertices && vertexIndex < renderedVertices.Length
                    ? renderedVertices[vertexIndex]
                    : m_SampledPoints[cross, along];
            }

            if (edge == LoftShoulderModifier.Edge.Left || edge == LoftShoulderModifier.Edge.Right)
            {
                int boundaryCross = edge == LoftShoulderModifier.Edge.Left ? 0 : m_CrossSampleCount - 1;
                int innerCross = edge == LoftShoulderModifier.Edge.Left ? 1 : m_CrossSampleCount - 2;
                for (int along = 0; along < m_AlongSampleCount; along++)
                {
                    boundary.Add(GetPoint(boundaryCross, along));
                    inner.Add(GetPoint(innerCross, along));
                    pathDistances.Add(along < m_AlongDistances.Count ? m_AlongDistances[along] : along);
                }
            }
            else
            {
                int boundaryAlong = edge == LoftShoulderModifier.Edge.Start ? 0 : m_AlongSampleCount - 1;
                int innerAlong = edge == LoftShoulderModifier.Edge.Start ? 1 : m_AlongSampleCount - 2;
                for (int cross = 0; cross < m_CrossSampleCount; cross++)
                {
                    boundary.Add(GetPoint(cross, boundaryAlong));
                    inner.Add(GetPoint(cross, innerAlong));
                    pathDistances.Add(cross < m_CrossDistances.Count ? m_CrossDistances[cross] : cross);
                }
            }

            return boundary.Count >= 2;
        }

        public bool TryGetShoulderSourceKnotCount(LoftShoulderModifier.Edge edge, out int knotCount)
        {
            knotCount = 0;
            if (edge != LoftShoulderModifier.Edge.Left && edge != LoftShoulderModifier.Edge.Right)
                return false;

            int start = edge == LoftShoulderModifier.Edge.Left ? 0 : m_Sources.Count - 1;
            int direction = edge == LoftShoulderModifier.Edge.Left ? 1 : -1;
            for (int index = start; index >= 0 && index < m_Sources.Count; index += direction)
            {
                SplineSource source = m_Sources[index];
                if (source?.IsValid != true)
                    continue;

                knotCount = source.container.Splines[source.splineIndex].Count;
                return knotCount >= 2;
            }

            return false;
        }

        public void AddSelectedSpline(SplineContainer container, int splineIndex = 0)
        {
            if (container == null)
                return;

            m_Sources.Add(new SplineSource
            {
                container = container,
                splineIndex = Mathf.Clamp(splineIndex, 0, Mathf.Max(0, container.Splines.Count - 1))
            });

            if (m_AutoRegenerate)
                QueueRegenerate();
        }

        public void ClearSources()
        {
            m_Sources.Clear();

            if (m_AutoRegenerate)
                QueueRegenerate();
        }

        void EnsureMesh()
        {
            if (m_GeneratedMesh == null)
            {
                m_GeneratedMesh = new Mesh
                {
                    name = $"{gameObject.name} Multi Spline Loft"
                };
                m_GeneratedMesh.MarkDynamic();
            }

            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter.sharedMesh != m_GeneratedMesh)
                meshFilter.sharedMesh = m_GeneratedMesh;

            ClearLegacyRootCollider();
        }

        void ClearBuildBuffers()
        {
            // Discard the previous vertex layout as well as its data. Keeping the
            // layout preserves a zero-filled Color channel after vertex painting;
            // paint replay can then mistake transparent black for the clean loft
            // base after a reload or regeneration.
            m_GeneratedMesh.Clear(false);
            m_Vertices.Clear();
            m_Normals.Clear();
            m_Uvs.Clear();
            m_Triangles.Clear();
            m_FlatVertices.Clear();
            m_FlatNormals.Clear();
            m_FlatUvs.Clear();
            m_FlatTriangles.Clear();
            m_ValidSourceIndices.Clear();
            m_CrossDistances.Clear();
            m_AlongDistances.Clear();
            m_AlongParameters.Clear();
            m_DistanceLookup.Clear();
            m_PrimaryTriangleCount = 0;
            m_SurfaceTriangleCount = 0;
            m_SurfaceVertexCount = 0;
            m_AlongSampleCount = 0;
        }

        void CollectValidSources()
        {
            for (int i = 0; i < m_Sources.Count; i++)
            {
                if (m_Sources[i]?.IsValid == true)
                    m_ValidSourceIndices.Add(i);
            }
        }

        void BuildSampleGrid()
        {
            int sourceCount = m_ValidSourceIndices.Count;
            bool closedAlong = ShouldCloseAlong();
            int sourceSpans = m_CloseAcrossSplines ? sourceCount : sourceCount - 1;
            m_CrossSampleCount = m_CloseAcrossSplines ? sourceSpans * m_SegmentsAcross : sourceSpans * m_SegmentsAcross + 1;
            BuildAlongParameters(sourceCount, closedAlong);
            m_AlongSampleCount = m_AlongParameters.Count;

            if (m_SourcePoints == null || m_SourcePoints.GetLength(0) != sourceCount || m_SourcePoints.GetLength(1) != m_AlongSampleCount)
                m_SourcePoints = new Vector3[sourceCount, m_AlongSampleCount];

            if (m_SampledPoints == null || m_SampledPoints.GetLength(0) != m_CrossSampleCount || m_SampledPoints.GetLength(1) != m_AlongSampleCount)
                m_SampledPoints = new Vector3[m_CrossSampleCount, m_AlongSampleCount];

            if (m_AlongAlignment == AlongAlignment.ReferencePerpendicular)
                BuildReferenceAlignedSourcePoints(sourceCount, closedAlong);
            else
                BuildParameterAlignedSourcePoints(sourceCount);

            for (int cross = 0; cross < m_CrossSampleCount; cross++)
            {
                GetAcrossSegment(cross, sourceCount, out int segment, out float t);

                for (int along = 0; along < m_AlongSampleCount; along++)
                {
                    var sampledPoint = EvaluateAcross(segment, t, along, sourceCount);
                    m_SampledPoints[cross, along] = IsFinite(sampledPoint) ? sampledPoint : Vector3.zero;
                }
            }

            BuildDistanceTables(m_CrossSampleCount);
        }

        void BuildParameterAlignedSourcePoints(int sourceCount)
        {
            for (int sourceIndex = 0; sourceIndex < sourceCount; sourceIndex++)
            {
                var source = m_Sources[m_ValidSourceIndices[sourceIndex]];
                for (int along = 0; along < m_AlongSampleCount; along++)
                    m_SourcePoints[sourceIndex, along] = ToLocalPoint(EvaluateSourcePosition(source, m_AlongParameters[along]));
            }
        }

        void BuildReferenceAlignedSourcePoints(int sourceCount, bool closedAlong)
        {
            int referenceIndex = ResolveAlignmentReference(sourceCount);
            var referenceSource = m_Sources[m_ValidSourceIndices[referenceIndex]];

            for (int along = 0; along < m_AlongSampleCount; along++)
                m_SourcePoints[referenceIndex, along] = ToLocalPoint(EvaluateSourcePosition(referenceSource, m_AlongParameters[along]));

            for (int sourceIndex = 0; sourceIndex < sourceCount; sourceIndex++)
            {
                if (sourceIndex == referenceIndex)
                    continue;

                var source = m_Sources[m_ValidSourceIndices[sourceIndex]];
                float previousParameter = 0f;
                for (int along = 0; along < m_AlongSampleCount; along++)
                {
                    float referenceParameter = m_AlongParameters[along];
                    Vector3 referencePoint = EvaluateSourcePosition(referenceSource, referenceParameter);
                    Vector3 referenceTangent = EvaluateSourceTangent(referenceSource, referenceParameter, closedAlong);
                    float sourceParameter;

                    if (!closedAlong && along == 0)
                        sourceParameter = 0f;
                    else if (!closedAlong && along == m_AlongSampleCount - 1)
                        sourceParameter = 1f;
                    else
                    {
                        float referenceStep = GetReferenceParameterStep(along, closedAlong);
                        float expectedParameter = along == 0
                            ? referenceParameter
                            : Mathf.Clamp01(previousParameter + referenceStep);
                        // A perpendicular plane can intersect a winding source spline
                        // many times. Restrict the solve to the neighboring parameter
                        // range so it cannot jump across a hairpin to a distant branch.
                        float backtrackTolerance = Mathf.Max(0.0025f, referenceStep * 0.75f);
                        float forwardSearchRadius = Mathf.Max(0.01f, referenceStep * 2f);
                        float minimumParameter = Mathf.Max(0f, previousParameter - backtrackTolerance);
                        float maximumParameter = Mathf.Min(
                            1f,
                            Mathf.Max(previousParameter, expectedParameter) + forwardSearchRadius);

                        sourceParameter = FindPlaneIntersectionParameter(
                            source,
                            referencePoint,
                            referenceTangent,
                            expectedParameter,
                            minimumParameter,
                            maximumParameter);

                        if (along > 0)
                        {
                            Vector3 previousSourcePoint = EvaluateSourcePosition(source, previousParameter);
                            Vector3 candidateSourcePoint = EvaluateSourcePosition(source, sourceParameter);
                            Vector3 expectedSourcePoint = EvaluateSourcePosition(source, expectedParameter);
                            Vector3 previousReferencePoint = EvaluateSourcePosition(referenceSource, m_AlongParameters[along - 1]);
                            float candidateTravel = Vector3.Distance(previousSourcePoint, candidateSourcePoint);
                            float expectedTravel = Vector3.Distance(previousSourcePoint, expectedSourcePoint);
                            float referenceTravel = Vector3.Distance(previousReferencePoint, referencePoint);
                            float maximumLocalTravel = Mathf.Max(0.01f, Mathf.Max(expectedTravel * 4f, referenceTravel * 6f));

                            // Parameter spacing can be very uneven on edited splines.
                            // Keep a root only if it is also spatially local; otherwise
                            // use the continuous prediction instead of stretching a quad
                            // across a distant portion of the course.
                            if (candidateTravel > maximumLocalTravel)
                                sourceParameter = expectedParameter;
                        }
                    }

                    previousParameter = sourceParameter;
                    m_SourcePoints[sourceIndex, along] = ToLocalPoint(EvaluateSourcePosition(source, sourceParameter));
                }
            }
        }

        float GetReferenceParameterStep(int along, bool closedAlong)
        {
            if (m_AlongSampleCount < 2)
                return 1f;

            if (along > 0)
                return Mathf.Abs(m_AlongParameters[along] - m_AlongParameters[along - 1]);

            if (closedAlong)
                return 1f / m_AlongSampleCount;

            return Mathf.Abs(m_AlongParameters[1] - m_AlongParameters[0]);
        }

        int ResolveAlignmentReference(int sourceCount)
        {
            if (m_AlignmentReferenceSource >= 0)
            {
                for (int validIndex = 0; validIndex < m_ValidSourceIndices.Count; validIndex++)
                {
                    if (m_ValidSourceIndices[validIndex] == m_AlignmentReferenceSource)
                        return validIndex;
                }
            }

            return Mathf.Clamp(sourceCount / 2, 0, sourceCount - 1);
        }

        Vector3 EvaluateSourcePosition(SplineSource source, float logicalParameter)
        {
            float t = source.reverse ? 1f - logicalParameter : logicalParameter;
            var point = source.container.EvaluatePosition(source.splineIndex, Mathf.Clamp01(t));
            return new Vector3(point.x, point.y, point.z);
        }

        Vector3 EvaluateSourceTangent(SplineSource source, float logicalParameter, bool closed)
        {
            const float step = 0.001f;
            float before = logicalParameter - step;
            float after = logicalParameter + step;

            if (closed)
            {
                before = Mathf.Repeat(before, 1f);
                after = Mathf.Repeat(after, 1f);
            }
            else
            {
                before = Mathf.Clamp01(before);
                after = Mathf.Clamp01(after);
            }

            Vector3 tangent = EvaluateSourcePosition(source, after) - EvaluateSourcePosition(source, before);
            if (!IsFinite(tangent) || tangent.sqrMagnitude < 0.000001f)
                return Vector3.forward;

            return tangent.normalized;
        }

        float FindPlaneIntersectionParameter(
            SplineSource source,
            Vector3 planePoint,
            Vector3 planeNormal,
            float preferredParameter,
            float minimumParameter,
            float maximumParameter)
        {
            const int searchSteps = 48;
            const int refinementSteps = 10;
            float min = Mathf.Clamp01(minimumParameter);
            float max = Mathf.Clamp(maximumParameter, min, 1f);
            float bestParameter = Mathf.Clamp(preferredParameter, min, max);
            float bestDistance = Mathf.Abs(SignedPlaneDistance(source, bestParameter, planePoint, planeNormal));
            float previousParameter = min;
            float previousDistance = SignedPlaneDistance(source, previousParameter, planePoint, planeNormal);
            float bestRoot = bestParameter;
            float bestRootPreference = float.PositiveInfinity;

            if (Mathf.Abs(previousDistance) < bestDistance)
            {
                bestDistance = Mathf.Abs(previousDistance);
                bestParameter = previousParameter;
            }

            for (int step = 1; step <= searchSteps; step++)
            {
                float parameter = Mathf.Lerp(min, max, step / (float)searchSteps);
                float distance = SignedPlaneDistance(source, parameter, planePoint, planeNormal);
                float absoluteDistance = Mathf.Abs(distance);
                if (absoluteDistance < bestDistance)
                {
                    bestDistance = absoluteDistance;
                    bestParameter = parameter;
                }

                if (previousDistance == 0f || distance == 0f || Mathf.Sign(previousDistance) != Mathf.Sign(distance))
                {
                    float lower = previousParameter;
                    float upper = parameter;
                    float lowerDistance = previousDistance;
                    for (int refinement = 0; refinement < refinementSteps; refinement++)
                    {
                        float middle = (lower + upper) * 0.5f;
                        float middleDistance = SignedPlaneDistance(source, middle, planePoint, planeNormal);
                        if (Mathf.Sign(lowerDistance) == Mathf.Sign(middleDistance))
                        {
                            lower = middle;
                            lowerDistance = middleDistance;
                        }
                        else
                        {
                            upper = middle;
                        }
                    }

                    float root = (lower + upper) * 0.5f;
                    float preference = Mathf.Abs(root - preferredParameter);
                    if (preference < bestRootPreference)
                    {
                        bestRootPreference = preference;
                        bestRoot = root;
                    }
                }

                previousParameter = parameter;
                previousDistance = distance;
            }

            return bestRootPreference < float.PositiveInfinity ? bestRoot : bestParameter;
        }

        float SignedPlaneDistance(SplineSource source, float parameter, Vector3 planePoint, Vector3 planeNormal)
        {
            return Vector3.Dot(EvaluateSourcePosition(source, parameter) - planePoint, planeNormal);
        }

        Vector3 ToLocalPoint(Vector3 worldPoint)
        {
            var localPoint = transform.InverseTransformPoint(worldPoint);
            return IsFinite(localPoint) ? localPoint : Vector3.zero;
        }

        void BuildAlongParameters(int sourceCount, bool closedAlong)
        {
            m_AlongParameters.Clear();

            if (m_AlongResolutionMode == AlongResolutionMode.FixedSamples)
            {
                int count = Mathf.Max(2, m_SamplesAlong);
                LoftResolutionSpline resolutionSpline = ResolveResolutionSpline();
                if (resolutionSpline != null && resolutionSpline.HasCustomScales)
                {
                    BuildResolutionScaledFixedParameters(count, closedAlong, resolutionSpline);
                    return;
                }

                for (int along = 0; along < count; along++)
                    m_AlongParameters.Add(closedAlong ? along / (float)count : along / (float)(count - 1));

                return;
            }

            BuildDistanceBasedAlongParameters(sourceCount, closedAlong);
        }

        void BuildResolutionScaledFixedParameters(int baseSampleCount, bool closedAlong, LoftResolutionSpline resolutionSpline)
        {
            float baseStep = 1f / Mathf.Max(1, closedAlong ? baseSampleCount : baseSampleCount - 1);
            int maximumSamples = (int)Math.Min(100000L, Math.Max(2L, (long)baseSampleCount * 10L));
            float parameter = 0f;
            m_AlongParameters.Add(0f);

            while (m_AlongParameters.Count < maximumSamples)
            {
                float density = Mathf.Clamp(resolutionSpline.EvaluateScale(parameter), 0.1f, 10f);
                float nextParameter = parameter + Mathf.Max(0.00001f, baseStep / density);
                if (nextParameter >= 1f - 0.00001f)
                    break;

                m_AlongParameters.Add(nextParameter);
                parameter = nextParameter;
            }

            if (!closedAlong)
                m_AlongParameters.Add(1f);

            if (m_AlongParameters.Count < 2)
                BuildFallbackAlongParameters(closedAlong);
        }

        void BuildDistanceBasedAlongParameters(int sourceCount, bool closedAlong)
        {
            const int lookupSteps = 512;
            m_DistanceLookup.Clear();
            m_DistanceLookup.Capacity = Mathf.Max(m_DistanceLookup.Capacity, lookupSteps + 1);
            m_DistanceLookup.Add(0f);

            SampleSourcePointsAt(0f, sourceCount, m_PreviousSourceSamples);
            float totalDistance = 0f;
            for (int i = 1; i <= lookupSteps; i++)
            {
                float t = i / (float)lookupSteps;
                SampleSourcePointsAt(t, sourceCount, m_CurrentSourceSamples);
                totalDistance += AverageSourceDistance(m_PreviousSourceSamples, m_CurrentSourceSamples);
                m_DistanceLookup.Add(totalDistance);

                m_PreviousSourceSamples.Clear();
                m_PreviousSourceSamples.AddRange(m_CurrentSourceSamples);
                m_CurrentSourceSamples.Clear();
            }

            if (totalDistance <= 0.0001f)
            {
                BuildFallbackAlongParameters(closedAlong);
                return;
            }

            int maxSamples = Mathf.Max(2, m_MaxDistanceSamples);
            float distance = 0f;
            m_AlongParameters.Add(0f);

            while (distance < totalDistance && m_AlongParameters.Count < maxSamples)
            {
                float currentT = DistanceToParameter(distance, totalDistance);
                float step = Mathf.Max(0.001f, m_TargetSegmentLength / GetResolutionDensity(currentT));
                distance += step;

                if (!closedAlong && distance >= totalDistance)
                    break;

                float nextT = DistanceToParameter(Mathf.Min(distance, totalDistance), totalDistance);
                if (nextT - m_AlongParameters[m_AlongParameters.Count - 1] > 0.0001f)
                    m_AlongParameters.Add(nextT);
                else
                    break;
            }

            if (!closedAlong && m_AlongParameters[m_AlongParameters.Count - 1] < 0.9999f)
                m_AlongParameters.Add(1f);

            if (m_AlongParameters.Count < 2)
                BuildFallbackAlongParameters(closedAlong);
        }

        void BuildFallbackAlongParameters(bool closedAlong)
        {
            m_AlongParameters.Clear();
            int count = Mathf.Max(2, m_SamplesAlong);
            for (int along = 0; along < count; along++)
                m_AlongParameters.Add(closedAlong ? along / (float)count : along / (float)(count - 1));
        }

        void SampleSourcePointsAt(float t, int sourceCount, List<Vector3> results)
        {
            results.Clear();
            results.Capacity = Mathf.Max(results.Capacity, sourceCount);

            for (int sourceIndex = 0; sourceIndex < sourceCount; sourceIndex++)
            {
                var source = m_Sources[m_ValidSourceIndices[sourceIndex]];
                float sampleT = source.reverse ? 1f - t : t;
                var worldPoint = source.container.EvaluatePosition(source.splineIndex, sampleT);
                var localPoint = transform.InverseTransformPoint(new Vector3(worldPoint.x, worldPoint.y, worldPoint.z));
                results.Add(IsFinite(localPoint) ? localPoint : Vector3.zero);
            }
        }

        static float AverageSourceDistance(List<Vector3> previous, List<Vector3> current)
        {
            int count = Mathf.Min(previous.Count, current.Count);
            if (count == 0)
                return 0f;

            float distance = 0f;
            for (int i = 0; i < count; i++)
                distance += Vector3.Distance(previous[i], current[i]);

            return distance / count;
        }

        float DistanceToParameter(float distance, float totalDistance)
        {
            if (totalDistance <= 0.0001f)
                return 0f;

            distance = Mathf.Clamp(distance, 0f, totalDistance);
            for (int i = 1; i < m_DistanceLookup.Count; i++)
            {
                if (m_DistanceLookup[i] < distance)
                    continue;

                float previousDistance = m_DistanceLookup[i - 1];
                float nextDistance = m_DistanceLookup[i];
                float t = Mathf.InverseLerp(previousDistance, nextDistance, distance);
                return ((i - 1) + t) / (m_DistanceLookup.Count - 1);
            }

            return 1f;
        }

        float GetResolutionDensity(float t)
        {
            float density = 1f;
            for (int i = 0; i < m_ResolutionZones.Count; i++)
            {
                ResolutionZone zone = m_ResolutionZones[i];
                if (zone == null || zone.radius <= 0f)
                    continue;

                float influence = Mathf.Clamp01(1f - Mathf.Abs(t - zone.center) / zone.radius);
                if (influence <= 0f)
                    continue;

                density = Mathf.Max(density, Mathf.Lerp(1f, Mathf.Max(0.1f, zone.density), Mathf.SmoothStep(0f, 1f, influence)));
            }

            LoftResolutionSpline resolutionSpline = ResolveResolutionSpline();
            if (resolutionSpline != null)
                density *= resolutionSpline.EvaluateScale(t);

            return Mathf.Max(0.1f, density);
        }

        void GetAcrossSegment(int crossSample, int sourceCount, out int segment, out float t)
        {
            float scaled = crossSample / (float)m_SegmentsAcross;
            int maxOpenSegment = Mathf.Max(0, sourceCount - 2);
            segment = Mathf.FloorToInt(scaled);
            t = scaled - segment;

            if (m_CloseAcrossSplines)
            {
                segment %= sourceCount;
                return;
            }

            if (segment > maxOpenSegment)
            {
                segment = maxOpenSegment;
                t = 1f;
            }
        }

        Vector3 EvaluateAcross(int segment, float t, int along, int sourceCount)
        {
            int next = WrapOrClampSourceIndex(segment + 1, sourceCount);

            if (m_AcrossInterpolation == AcrossInterpolation.Linear)
                return Vector3.Lerp(m_SourcePoints[segment, along], m_SourcePoints[next, along], t);

            int previous = WrapOrClampSourceIndex(segment - 1, sourceCount);
            int afterNext = WrapOrClampSourceIndex(segment + 2, sourceCount);
            return CatmullRom(
                m_SourcePoints[previous, along],
                m_SourcePoints[segment, along],
                m_SourcePoints[next, along],
                m_SourcePoints[afterNext, along],
                t);
        }

        int WrapOrClampSourceIndex(int index, int sourceCount)
        {
            if (m_CloseAcrossSplines)
                return (index % sourceCount + sourceCount) % sourceCount;

            return Mathf.Clamp(index, 0, sourceCount - 1);
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        void BuildDistanceTables(int crossCount)
        {
            m_CrossDistances.Add(0f);
            for (int cross = 1; cross < crossCount; cross++)
                m_CrossDistances.Add(m_CrossDistances[cross - 1] + AverageDistanceBetweenRows(cross - 1, cross));

            m_AlongDistances.Add(0f);
            for (int along = 1; along < m_AlongSampleCount; along++)
                m_AlongDistances.Add(m_AlongDistances[along - 1] + AverageDistanceBetweenColumns(along - 1, along));
        }

        float AverageDistanceBetweenRows(int a, int b)
        {
            float distance = 0f;
            for (int along = 0; along < m_AlongSampleCount; along++)
                distance += Vector3.Distance(m_SampledPoints[a, along], m_SampledPoints[b, along]);

            return distance / m_AlongSampleCount;
        }

        float AverageDistanceBetweenColumns(int a, int b)
        {
            float distance = 0f;
            int crossCount = m_CrossSampleCount;
            for (int cross = 0; cross < crossCount; cross++)
                distance += Vector3.Distance(m_SampledPoints[cross, a], m_SampledPoints[cross, b]);

            return distance / crossCount;
        }

        void BuildVertices()
        {
            int crossCount = m_CrossSampleCount;
            int vertexCount = crossCount * m_AlongSampleCount;
            float totalAcrossDistance = crossCount > 1 ? m_CrossDistances[crossCount - 1] : 0f;
            float inverseAcrossDistance = totalAcrossDistance > Mathf.Epsilon ? 1f / totalAcrossDistance : 0f;
            m_Vertices.Capacity = Mathf.Max(m_Vertices.Capacity, vertexCount);
            m_Normals.Capacity = Mathf.Max(m_Normals.Capacity, vertexCount);
            m_Uvs.Capacity = Mathf.Max(m_Uvs.Capacity, vertexCount);

            for (int cross = 0; cross < crossCount; cross++)
            {
                for (int along = 0; along < m_AlongSampleCount; along++)
                {
                    m_Vertices.Add(m_SampledPoints[cross, along]);
                    m_Normals.Add(CalculateGridNormal(cross, along));
                    float acrossUv = m_CrossDistances[cross] * inverseAcrossDistance * m_UvScaleAcross;
                    m_Uvs.Add(new Vector2(acrossUv, m_AlongDistances[along] * m_UvScaleAlong));
                }
            }

            m_SurfaceVertexCount = m_Vertices.Count;
        }

        Vector3 CalculateGridNormal(int cross, int along)
        {
            int crossCount = m_CrossSampleCount;
            Vector3 across = DeltaAcross(cross, along, crossCount);
            Vector3 forward = DeltaAlong(cross, along);
            Vector3 normal = Vector3.Cross(across, forward);

            if (!IsFinite(normal) || normal.sqrMagnitude < 0.000001f)
                normal = Vector3.up;

            normal.Normalize();
            return m_FlipNormals ? -normal : normal;
        }

        Vector3 DeltaAcross(int cross, int along, int crossCount)
        {
            if (m_CloseAcrossSplines)
            {
                int previous = (cross - 1 + crossCount) % crossCount;
                int next = (cross + 1) % crossCount;
                return m_SampledPoints[next, along] - m_SampledPoints[previous, along];
            }

            if (cross == 0)
                return m_SampledPoints[1, along] - m_SampledPoints[0, along];

            if (cross == crossCount - 1)
                return m_SampledPoints[crossCount - 1, along] - m_SampledPoints[crossCount - 2, along];

            return m_SampledPoints[cross + 1, along] - m_SampledPoints[cross - 1, along];
        }

        Vector3 DeltaAlong(int cross, int along)
        {
            bool closedAlong = ShouldCloseAlong();
            if (closedAlong)
            {
                int previous = (along - 1 + m_AlongSampleCount) % m_AlongSampleCount;
                int next = (along + 1) % m_AlongSampleCount;
                return m_SampledPoints[cross, next] - m_SampledPoints[cross, previous];
            }

            if (along == 0)
                return m_SampledPoints[cross, 1] - m_SampledPoints[cross, 0];

            if (along == m_AlongSampleCount - 1)
                return m_SampledPoints[cross, m_AlongSampleCount - 1] - m_SampledPoints[cross, m_AlongSampleCount - 2];

            return m_SampledPoints[cross, along + 1] - m_SampledPoints[cross, along - 1];
        }

        void BuildSurfaceTriangles()
        {
            int crossCount = m_CrossSampleCount;
            int crossSegments = m_CloseAcrossSplines ? crossCount : crossCount - 1;
            int alongSegments = ShouldCloseAlong() ? m_AlongSampleCount : m_AlongSampleCount - 1;

            for (int cross = 0; cross < crossSegments; cross++)
            {
                int nextCross = (cross + 1) % crossCount;

                for (int along = 0; along < alongSegments; along++)
                {
                    int nextAlong = (along + 1) % m_AlongSampleCount;
                    int a = VertexIndex(cross, along);
                    int b = VertexIndex(nextCross, along);
                    int c = VertexIndex(cross, nextAlong);
                    int d = VertexIndex(nextCross, nextAlong);

                    AddQuad(a, b, c, d);
                }
            }
        }

        bool ShouldCloseAlong()
        {
            if (!m_CloseAlongClosedSplines)
                return false;

            for (int i = 0; i < m_ValidSourceIndices.Count; i++)
            {
                var source = m_Sources[m_ValidSourceIndices[i]];
                if (!source.container.Splines[source.splineIndex].Closed)
                    return false;
            }

            return true;
        }

        int VertexIndex(int cross, int along)
        {
            return cross * m_AlongSampleCount + along;
        }

        void AddQuad(int a, int b, int c, int d)
        {
            if (m_FlipNormals)
            {
                m_Triangles.Add(a);
                m_Triangles.Add(c);
                m_Triangles.Add(b);
                m_Triangles.Add(c);
                m_Triangles.Add(d);
                m_Triangles.Add(b);
                return;
            }

            m_Triangles.Add(a);
            m_Triangles.Add(b);
            m_Triangles.Add(c);
            m_Triangles.Add(c);
            m_Triangles.Add(b);
            m_Triangles.Add(d);
        }

        void BuildCap(int alongIndex, bool endCap)
        {
            int crossCount = m_CrossSampleCount;
            int centerIndex = m_Vertices.Count;
            Vector3 center = Vector3.zero;
            Vector3 capNormal = CalculateCapNormal(alongIndex, endCap);

            for (int cross = 0; cross < crossCount; cross++)
                center += m_SampledPoints[cross, alongIndex];

            center /= crossCount;
            m_Vertices.Add(center);
            m_Normals.Add(capNormal);
            m_Uvs.Add(new Vector2(0.5f, 0.5f));

            int firstRimIndex = m_Vertices.Count;
            for (int cross = 0; cross < crossCount; cross++)
            {
                int sourceVertex = VertexIndex(cross, alongIndex);
                m_Vertices.Add(m_Vertices[sourceVertex]);
                m_Normals.Add(capNormal);
                m_Uvs.Add(m_Uvs[sourceVertex]);
            }

            int last = m_CloseAcrossSplines ? crossCount : crossCount - 1;
            for (int cross = 0; cross < last; cross++)
            {
                int nextCross = (cross + 1) % crossCount;
                int a = firstRimIndex + cross;
                int b = firstRimIndex + nextCross;

                bool reverseWinding = endCap ^ m_FlipNormals;
                if (reverseWinding)
                {
                    m_Triangles.Add(centerIndex);
                    m_Triangles.Add(a);
                    m_Triangles.Add(b);
                }
                else
                {
                    m_Triangles.Add(centerIndex);
                    m_Triangles.Add(b);
                    m_Triangles.Add(a);
                }
            }
        }

        Vector3 CalculateCapNormal(int alongIndex, bool endCap)
        {
            Vector3 direction = Vector3.zero;
            int compareIndex = endCap ? Mathf.Max(0, alongIndex - 1) : Mathf.Min(m_AlongSampleCount - 1, alongIndex + 1);

            for (int cross = 0; cross < m_CrossSampleCount; cross++)
                direction += m_SampledPoints[cross, alongIndex] - m_SampledPoints[cross, compareIndex];

            if (direction.sqrMagnitude < 0.000001f)
                direction = endCap ? Vector3.forward : Vector3.back;

            direction.Normalize();
            return m_FlipNormals ? -direction : direction;
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        void DuplicateBackFaces()
        {
            int triangleCount = m_Triangles.Count;
            m_Triangles.Capacity = Mathf.Max(m_Triangles.Capacity, triangleCount * 2);

            for (int i = 0; i < triangleCount; i += 3)
            {
                m_Triangles.Add(m_Triangles[i + 2]);
                m_Triangles.Add(m_Triangles[i + 1]);
                m_Triangles.Add(m_Triangles[i]);
            }
        }

        void ApplyMesh()
        {
            if (m_NormalMode == NormalMode.Face)
                ConvertToFlatFaces();
            else if (m_NormalMode == NormalMode.SmoothedFaces)
                RebuildSmoothedFaceNormals();
            else if (m_NormalMode == NormalMode.LoftGrid)
                RebuildLoftGridNormals();

            // Unity meshes default to 16-bit indices. Higher along-sample counts can
            // push the loft past 65,535 vertices, at which point 16-bit triangle
            // indices wrap and the surface appears to explode across the scene.
            m_GeneratedMesh.indexFormat = m_Vertices.Count > ushort.MaxValue
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            m_GeneratedMesh.SetVertices(m_Vertices);
            m_GeneratedMesh.SetUVs(0, m_Uvs);
            m_GeneratedMesh.SetTriangles(m_Triangles, 0);

            if (m_NormalMode != NormalMode.Recalculate && m_Normals.Count == m_Vertices.Count)
                m_GeneratedMesh.SetNormals(m_Normals);
            else
                m_GeneratedMesh.RecalculateNormals();

            m_GeneratedMesh.RecalculateBounds();
            if (m_Vertices.Count > 0)
                m_GeneratedMesh.RecalculateTangents();

            var meshFilter = GetComponent<MeshFilter>();
            meshFilter.sharedMesh = m_GeneratedMesh;
        }

        public void RebuildColliderChunks()
        {
            ClearLegacyRootCollider();
            Transform chunksRoot = FindColliderChunksRoot();
            if (!m_UpdateMeshCollider || m_GeneratedMesh == null || m_GeneratedMesh.vertexCount == 0 || m_SurfaceTriangleCount <= 0)
            {
                if (chunksRoot != null)
                    DestroyColliderChunksRoot(chunksRoot);
                return;
            }

            if (chunksRoot == null)
            {
                var rootObject = new GameObject("Collider Chunks");
                rootObject.transform.SetParent(transform, false);
                chunksRoot = rootObject.transform;
            }
            else
            {
                for (int index = chunksRoot.childCount - 1; index >= 0; index--)
                    DestroyColliderChunkObject(chunksRoot.GetChild(index).gameObject);
            }

            chunksRoot.gameObject.isStatic = true;
            chunksRoot.gameObject.layer = gameObject.layer;
            ApplyGeneratedColliderTag(chunksRoot.gameObject);

            Vector3[] sourceVertices = m_GeneratedMesh.vertices;
            Vector2[] sourceUvs = m_GeneratedMesh.uv;
            int[] baseTriangles = m_GeneratedMesh.GetTriangles(0);
            int surfaceIndexCount = Mathf.Min(m_SurfaceTriangleCount, baseTriangles.Length);
            var sourceTriangles = new List<int>(surfaceIndexCount);
            for (int index = 0; index < surfaceIndexCount; index++)
                sourceTriangles.Add(baseTriangles[index]);
            foreach (int submeshIndex in m_ShoulderColliderSubmeshes)
            {
                if (submeshIndex >= 0 && submeshIndex < m_GeneratedMesh.subMeshCount)
                    sourceTriangles.AddRange(m_GeneratedMesh.GetTriangles(submeshIndex));
            }
            float totalDistance = m_AlongDistances.Count > 0 ? m_AlongDistances[m_AlongDistances.Count - 1] : 0f;
            float chunkLength = Mathf.Max(1f, m_ColliderChunkLength);
            int chunkCount = Mathf.Max(1, Mathf.CeilToInt(totalDistance / chunkLength));
            var chunkTriangleIndices = new List<int>[chunkCount];

            for (int triangle = 0; triangle + 2 < sourceTriangles.Count; triangle += 3)
            {
                int a = sourceTriangles[triangle];
                int b = sourceTriangles[triangle + 1];
                int c = sourceTriangles[triangle + 2];
                if (a < 0 || b < 0 || c < 0 || a >= sourceVertices.Length || b >= sourceVertices.Length || c >= sourceVertices.Length)
                    continue;

                float alongDistance;
                if (sourceUvs.Length == sourceVertices.Length)
                    alongDistance = (sourceUvs[a].y + sourceUvs[b].y + sourceUvs[c].y) / (3f * Mathf.Max(0.0001f, m_UvScaleAlong));
                else
                    alongDistance = totalDistance * triangle / Mathf.Max(1f, sourceTriangles.Count);

                int chunkIndex = Mathf.Clamp(Mathf.FloorToInt(alongDistance / chunkLength), 0, chunkCount - 1);
                chunkTriangleIndices[chunkIndex] ??= new List<int>();
                chunkTriangleIndices[chunkIndex].Add(a);
                chunkTriangleIndices[chunkIndex].Add(b);
                chunkTriangleIndices[chunkIndex].Add(c);
            }

            for (int chunkIndex = 0; chunkIndex < chunkTriangleIndices.Length; chunkIndex++)
            {
                List<int> sourceIndices = chunkTriangleIndices[chunkIndex];
                if (sourceIndices == null || sourceIndices.Count == 0)
                    continue;

                Mesh colliderMesh = BuildColliderChunkMesh(sourceVertices, sourceIndices, chunkIndex);
                float startDistance = chunkIndex * chunkLength;
                float endDistance = Mathf.Min(totalDistance, startDistance + chunkLength);
                var chunkObject = new GameObject($"Collider {chunkIndex + 1:000} [{startDistance:0}-{endDistance:0}m]");
                chunkObject.layer = gameObject.layer;
                chunkObject.isStatic = true;
                ApplyGeneratedColliderTag(chunkObject);
                chunkObject.transform.SetParent(chunksRoot, false);
                chunkObject.AddComponent<MeshCollider>().sharedMesh = colliderMesh;
            }
        }

        static void ApplyGeneratedColliderTag(GameObject target)
        {
            try
            {
                target.tag = GeneratedColliderTag;
            }
            catch (UnityException)
            {
                if (s_HasWarnedAboutMissingColliderTag)
                    return;

                s_HasWarnedAboutMissingColliderTag = true;
                Debug.LogWarning(
                    $"MultiSplineLoft could not tag generated collider objects as '{GeneratedColliderTag}' because that tag is not defined. " +
                    "Add it in Project Settings > Tags and Layers to enable the default loft collider tag.");
            }
        }

        Mesh BuildColliderChunkMesh(Vector3[] sourceVertices, List<int> sourceIndices, int chunkIndex)
        {
            var remappedIndices = new Dictionary<int, int>();
            var vertices = new List<Vector3>();
            var triangles = new List<int>(sourceIndices.Count);
            foreach (int sourceIndex in sourceIndices)
            {
                if (!remappedIndices.TryGetValue(sourceIndex, out int remappedIndex))
                {
                    remappedIndex = vertices.Count;
                    remappedIndices.Add(sourceIndex, remappedIndex);
                    vertices.Add(sourceVertices[sourceIndex]);
                }
                triangles.Add(remappedIndex);
            }

            var mesh = new Mesh
            {
                name = $"{gameObject.name} Collider Chunk {chunkIndex + 1:000}",
                hideFlags = HideFlags.DontSave,
                indexFormat = vertices.Count > ushort.MaxValue
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        Transform FindColliderChunksRoot()
        {
            Transform child = transform.Find("Collider Chunks");
            return child != null && child.parent == transform ? child : null;
        }

        void ClearLegacyRootCollider()
        {
            if (!TryGetComponent(out MeshCollider collider))
                return;

            collider.sharedMesh = null;
            collider.enabled = false;
            DestroyGeneratedObject(collider);
        }

        static void DestroyColliderChunksRoot(Transform chunksRoot)
        {
            for (int index = chunksRoot.childCount - 1; index >= 0; index--)
                DestroyColliderChunkObject(chunksRoot.GetChild(index).gameObject);
            DestroyGeneratedObject(chunksRoot.gameObject);
        }

        static void DestroyColliderChunkObject(GameObject chunkObject)
        {
            if (chunkObject == null)
                return;
            MeshCollider collider = chunkObject.GetComponent<MeshCollider>();
            Mesh colliderMesh = collider != null ? collider.sharedMesh : null;
            if (collider != null)
                collider.sharedMesh = null;
            if (colliderMesh != null && (colliderMesh.hideFlags & HideFlags.DontSave) != 0)
                DestroyGeneratedObject(colliderMesh);
            DestroyGeneratedObject(chunkObject);
        }

        static void DestroyGeneratedObject(UnityEngine.Object value)
        {
            if (value == null)
                return;
            if (Application.isPlaying)
                Destroy(value);
            else
                DestroyImmediate(value);
        }

        void RebuildSmoothedFaceNormals()
        {
            List<Vector3> capNormals = null;
            if (m_SurfaceVertexCount < m_Normals.Count)
                capNormals = new List<Vector3>(m_Normals.GetRange(m_SurfaceVertexCount, m_Normals.Count - m_SurfaceVertexCount));

            m_Normals.Clear();
            for (int i = 0; i < m_Vertices.Count; i++)
            {
                if (i < m_SurfaceVertexCount)
                    m_Normals.Add(Vector3.zero);
                else if (capNormals != null && i - m_SurfaceVertexCount < capNormals.Count)
                    m_Normals.Add(capNormals[i - m_SurfaceVertexCount]);
                else
                    m_Normals.Add(Vector3.up);
            }

            int triangleLimit = m_SurfaceTriangleCount > 0 ? m_SurfaceTriangleCount : m_Triangles.Count;
            triangleLimit = Mathf.Min(triangleLimit, m_Triangles.Count);

            for (int i = 0; i + 2 < triangleLimit; i += 3)
            {
                int a = m_Triangles[i];
                int b = m_Triangles[i + 1];
                int c = m_Triangles[i + 2];

                if (!IsValidVertexIndex(a) || !IsValidVertexIndex(b) || !IsValidVertexIndex(c))
                    continue;

                Vector3 faceNormal = Vector3.Cross(m_Vertices[b] - m_Vertices[a], m_Vertices[c] - m_Vertices[a]);
                if (!IsFinite(faceNormal) || faceNormal.sqrMagnitude < 0.000001f)
                    continue;

                m_Normals[a] += faceNormal;
                m_Normals[b] += faceNormal;
                m_Normals[c] += faceNormal;
            }

            for (int i = 0; i < m_Normals.Count; i++)
            {
                if (i < m_SurfaceVertexCount)
                {
                    Vector3 gridNormal = CalculateGridNormal(i / m_AlongSampleCount, i % m_AlongSampleCount);
                    Vector3 faceNormal = m_Normals[i];
                    if (IsFinite(faceNormal) && faceNormal.sqrMagnitude > 0.000001f && Vector3.Dot(gridNormal, faceNormal) < 0f)
                        gridNormal = -gridNormal;

                    m_Normals[i] = gridNormal;
                    continue;
                }

                if (!IsFinite(m_Normals[i]) || m_Normals[i].sqrMagnitude < 0.000001f)
                    m_Normals[i] = Vector3.up;
                else
                    m_Normals[i] = m_Normals[i].normalized;
            }
        }

        void RebuildLoftGridNormals()
        {
            for (int i = 0; i < m_SurfaceVertexCount && i < m_Normals.Count; i++)
                m_Normals[i] = CalculateGridNormal(i / m_AlongSampleCount, i % m_AlongSampleCount);
        }

        void ConvertToFlatFaces()
        {
            m_FlatVertices.Clear();
            m_FlatNormals.Clear();
            m_FlatUvs.Clear();
            m_FlatTriangles.Clear();

            for (int i = 0; i + 2 < m_Triangles.Count; i += 3)
            {
                int a = m_Triangles[i];
                int b = m_Triangles[i + 1];
                int c = m_Triangles[i + 2];

                if (!IsValidVertexIndex(a) || !IsValidVertexIndex(b) || !IsValidVertexIndex(c))
                    continue;

                Vector3 faceNormal = Vector3.Cross(m_Vertices[b] - m_Vertices[a], m_Vertices[c] - m_Vertices[a]);
                if (!IsFinite(faceNormal) || faceNormal.sqrMagnitude < 0.000001f)
                    faceNormal = Vector3.up;
                else
                    faceNormal.Normalize();

                AddFlatVertex(a, faceNormal);
                AddFlatVertex(b, faceNormal);
                AddFlatVertex(c, faceNormal);
            }

            m_Vertices.Clear();
            m_Vertices.AddRange(m_FlatVertices);
            m_Normals.Clear();
            m_Normals.AddRange(m_FlatNormals);
            m_Uvs.Clear();
            m_Uvs.AddRange(m_FlatUvs);
            m_Triangles.Clear();
            m_Triangles.AddRange(m_FlatTriangles);
        }

        void AddFlatVertex(int sourceVertex, Vector3 faceNormal)
        {
            int flatIndex = m_FlatVertices.Count;
            m_FlatVertices.Add(m_Vertices[sourceVertex]);
            m_FlatNormals.Add(faceNormal);
            m_FlatUvs.Add(sourceVertex < m_Uvs.Count ? m_Uvs[sourceVertex] : Vector2.zero);
            m_FlatTriangles.Add(flatIndex);
        }

        bool IsValidVertexIndex(int index)
        {
            return index >= 0 && index < m_Vertices.Count;
        }

        void OnDrawGizmosSelected()
        {
            if (m_SampledPoints == null || m_ValidSourceIndices.Count < 2)
                return;

            Gizmos.color = new Color(0.1f, 0.9f, 1f, 0.75f);
            int crossCount = m_CrossSampleCount;
            int stride = Mathf.Max(1, m_AlongSampleCount / 12);

            for (int along = 0; along < m_AlongSampleCount; along += stride)
            {
                for (int cross = 0; cross < crossCount - 1; cross++)
                    Gizmos.DrawLine(transform.TransformPoint(m_SampledPoints[cross, along]), transform.TransformPoint(m_SampledPoints[cross + 1, along]));
            }
        }
    }
}
