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
            public bool mirrorSplitToNext;
            [Range(0f, 1f)] public float mirrorBlendLength = 0.15f;
            [Min(0f)] public float mirrorBlendWidth = 0.05f;
            public float mirrorLeftOffset;
            public float mirrorRightOffset;
            public bool flipMirrorLeft = true;
            public bool flipMirrorRight;
            [Min(0.01f)] public float mirrorBranchUvWidth = 0.5f;
            [Min(0.01f)] public float mirrorLeftScale = 1f;
            [Min(0.01f)] public float mirrorRightScale = 1f;
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
        [SerializeField, HideInInspector] int m_MirrorSettingsVersion;
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
        [NonSerialized] readonly List<Vector2> m_WorkingLeftSeamUvs = new List<Vector2>();
        [NonSerialized] readonly List<Vector2> m_WorkingRightSeamUvs = new List<Vector2>();
        [NonSerialized] readonly List<bool> m_WorkingSeamCandidates = new List<bool>();
        [NonSerialized] readonly Dictionary<float, int> m_WorkingCrossUvCounts = new Dictionary<float, int>();

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
            if (m_MirrorSettingsVersion < 1)
            {
                for (int index = 0; index < m_ControlPoints.Count; index++)
                    m_ControlPoints[index].flipMirrorLeft = true;
                m_MirrorSettingsVersion = 1;
            }
            for (int index = 0; index < m_ControlPoints.Count; index++)
            {
                if (m_MirrorSettingsVersion < 2)
                    m_ControlPoints[index].mirrorBranchUvWidth = 0.5f;
                if (m_MirrorSettingsVersion < 3)
                {
                    m_ControlPoints[index].mirrorLeftScale = 1f;
                    m_ControlPoints[index].mirrorRightScale = 1f;
                }
                m_ControlPoints[index].mirrorBlendLength = Mathf.Clamp01(m_ControlPoints[index].mirrorBlendLength);
                m_ControlPoints[index].mirrorBlendWidth = Mathf.Max(0f, m_ControlPoints[index].mirrorBlendWidth);
                m_ControlPoints[index].mirrorBranchUvWidth = Mathf.Max(0.01f, m_ControlPoints[index].mirrorBranchUvWidth);
                m_ControlPoints[index].mirrorLeftScale = Mathf.Max(0.01f, m_ControlPoints[index].mirrorLeftScale);
                m_ControlPoints[index].mirrorRightScale = Mathf.Max(0.01f, m_ControlPoints[index].mirrorRightScale);
            }
            m_MirrorSettingsVersion = 3;

            MultiSplineLoft owningLoft = GetComponentInParent<MultiSplineLoft>();
            owningLoft?.SynchronizeUvSplineSettings(this);
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
            bool supportsTopologySeam = m_Target.GetComponent<MultiSplineLoft>() != null;
            float topologySeamCross = supportsTopologySeam
                ? FindClosestLoftSeamCross(sourceUvs, longIsU, pivot, minCross, maxCross)
                : pivot;
            m_WorkingOutputUvs.Clear();
            m_WorkingLeftSeamUvs.Clear();
            m_WorkingRightSeamUvs.Clear();
            m_WorkingSeamCandidates.Clear();
            if (m_WorkingOutputUvs.Capacity < sourceUvs.Count)
            {
                m_WorkingOutputUvs.Capacity = sourceUvs.Count;
                m_WorkingLeftSeamUvs.Capacity = sourceUvs.Count;
                m_WorkingRightSeamUvs.Capacity = sourceUvs.Count;
                m_WorkingSeamCandidates.Capacity = sourceUvs.Count;
            }

            float seamTolerance = Mathf.Max(0.00001f, (maxCross - minCross) * 0.0001f);

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

                float leftSeamCross = outputCross;
                float rightSeamCross = outputCross;
                bool seamCandidate = false;
                if (m_ControlPoints[segment].mirrorSplitToNext)
                {
                    ControlPoint segmentPoint = m_ControlPoints[segment];
                    float offsetT = m_SmoothInterpolation ? Mathf.SmoothStep(0f, 1f, segmentT) : segmentT;
                    float leftOffset = Mathf.Lerp(m_ControlPoints[aIndex].mirrorLeftOffset, m_ControlPoints[bIndex].mirrorLeftOffset, offsetT);
                    float rightOffset = Mathf.Lerp(m_ControlPoints[aIndex].mirrorRightOffset, m_ControlPoints[bIndex].mirrorRightOffset, offsetT);
                    float mappedPivotA = GetMappedCross(aIndex, pivot, pivot);
                    float mappedPivotB = GetMappedCross(bIndex, pivot, pivot);
                    float mappedPivot = m_SmoothInterpolation
                        ? Mathf.SmoothStep(mappedPivotA, mappedPivotB, segmentT)
                        : Mathf.Lerp(mappedPivotA, mappedPivotB, segmentT);
                    float commonOffset = supportsTopologySeam ? mappedPivot - pivot : 0f;
                    float crossDistance = supportsTopologySeam ? outputCross - mappedPivot : outputCross - pivot;
                    float absoluteDistance = Mathf.Abs(crossDistance);
                    // Independent branch offsets cannot meet at one shared center UV
                    // without a transition. Enforce enough width to keep that
                    // transition's UV derivative bounded instead of producing a
                    // razor-thin, heavily stretched stripe down the split seam.
                    float stretchSafeWidth = supportsTopologySeam ? 0f : Mathf.Abs(rightOffset - leftOffset) * 0.75f;
                    float blendWidth = supportsTopologySeam
                        ? 0f
                        : Mathf.Max(Mathf.Max(0f, segmentPoint.mirrorBlendWidth), stretchSafeWidth);
                    if (blendWidth > Mathf.Epsilon && absoluteDistance < blendWidth)
                    {
                        float widthT = absoluteDistance / blendWidth;
                        absoluteDistance = blendWidth * Mathf.SmoothStep(0f, 1f, widthT);
                    }

                    float sideBlend = supportsTopologySeam
                        ? cross < topologySeamCross ? 0f : 1f
                        : blendWidth > Mathf.Epsilon
                            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(pivot - blendWidth, pivot + blendWidth, outputCross))
                            : crossDistance < 0f ? 0f : 1f;
                    float branchDistance = absoluteDistance * 2f * Mathf.Max(0.01f, segmentPoint.mirrorBranchUvWidth);
                    float leftBranchDistance = branchDistance * Mathf.Max(0.01f, segmentPoint.mirrorLeftScale);
                    float rightBranchDistance = branchDistance * Mathf.Max(0.01f, segmentPoint.mirrorRightScale);
                    float leftMirroredCross = segmentPoint.flipMirrorLeft
                        ? maxCross - leftBranchDistance + commonOffset + leftOffset
                        : minCross + leftBranchDistance + commonOffset + leftOffset;
                    float rightMirroredCross = segmentPoint.flipMirrorRight
                        ? maxCross - rightBranchDistance + commonOffset + rightOffset
                        : minCross + rightBranchDistance + commonOffset + rightOffset;

                    // Moving an individual branch can otherwise push part of its
                    // crosswise UV range past the source shell. Repeat-wrapped
                    // materials then bring the opposite side of the texture back
                    // into that branch, which looks like a second narrow fork.
                    // Hold the source shell's outer edge instead; the branch can
                    // still be positioned independently without introducing a
                    // wrapped copy beside the topology seam.
                    float branchMinCross = minCross + commonOffset;
                    float branchMaxCross = maxCross + commonOffset;
                    leftMirroredCross = Mathf.Clamp(leftMirroredCross, branchMinCross, branchMaxCross);
                    rightMirroredCross = Mathf.Clamp(rightMirroredCross, branchMinCross, branchMaxCross);
                    float mirroredCross = Mathf.Lerp(leftMirroredCross, rightMirroredCross, sideBlend);

                    float blendLength = Mathf.Clamp01(segmentPoint.mirrorBlendLength);
                    float alongBlend = 1f;
                    bool previousIsSplit = segment > 0 && m_ControlPoints[segment - 1].mirrorSplitToNext;
                    bool nextIsSplit = segment + 1 < m_ControlPoints.Count - 1 && m_ControlPoints[segment + 1].mirrorSplitToNext;
                    if (!previousIsSplit && blendLength > Mathf.Epsilon)
                        alongBlend *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(segmentT / blendLength));
                    if (!nextIsSplit && blendLength > Mathf.Epsilon)
                        alongBlend *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - segmentT) / blendLength));

                    leftSeamCross = Mathf.Lerp(outputCross, leftMirroredCross, alongBlend);
                    rightSeamCross = Mathf.Lerp(outputCross, rightMirroredCross, alongBlend);
                    outputCross = Mathf.Lerp(outputCross, mirroredCross, alongBlend);
                    seamCandidate = Mathf.Abs(cross - topologySeamCross) <= seamTolerance && alongBlend > 0.0001f;
                }

                m_WorkingOutputUvs.Add(longIsU ? new Vector2(outputLong, outputCross) : new Vector2(outputCross, outputLong));
                m_WorkingLeftSeamUvs.Add(longIsU ? new Vector2(outputLong, leftSeamCross) : new Vector2(leftSeamCross, outputLong));
                m_WorkingRightSeamUvs.Add(longIsU ? new Vector2(outputLong, rightSeamCross) : new Vector2(rightSeamCross, outputLong));
                m_WorkingSeamCandidates.Add(seamCandidate);
            }

            Mesh result = reusableOutput != null && reusableOutput.vertexCount == source.vertexCount
                ? reusableOutput
                : Instantiate(source);
            if (result != reusableOutput)
                result.name = source.name + "_UVSpline";
            result.SetUVs(m_UvChannel, m_WorkingOutputUvs);
            if (supportsTopologySeam)
                SplitMirroredLoftUvSeams(result, sourceUvs, longIsU, topologySeamCross);
            return result;
        }

        float FindClosestLoftSeamCross(
            IReadOnlyList<Vector2> sourceUvs,
            bool longIsU,
            float pivot,
            float minCross,
            float maxCross)
        {
            m_WorkingCrossUvCounts.Clear();
            int highestCount = 0;
            for (int index = 0; index < sourceUvs.Count; index++)
            {
                float cross = longIsU ? sourceUvs[index].y : sourceUvs[index].x;
                m_WorkingCrossUvCounts.TryGetValue(cross, out int count);
                count++;
                m_WorkingCrossUvCounts[cross] = count;
                highestCount = Mathf.Max(highestCount, count);
            }

            // Loft surface rows repeat the same cross UV for every along sample.
            // Caps and auxiliary geometry can contain isolated values near the
            // requested pivot, so only consider values repeated like a real row.
            int minimumRowCount = Mathf.Max(2, Mathf.CeilToInt(highestCount * 0.5f));
            float edgeTolerance = Mathf.Max(0.00001f, (maxCross - minCross) * 0.0001f);
            float closest = pivot;
            float closestDistance = float.MaxValue;
            foreach (KeyValuePair<float, int> pair in m_WorkingCrossUvCounts)
            {
                float cross = pair.Key;
                if (pair.Value < minimumRowCount
                    || cross <= minCross + edgeTolerance
                    || cross >= maxCross - edgeTolerance)
                    continue;

                float distance = Mathf.Abs(cross - pivot);
                if (distance < closestDistance)
                {
                    closest = cross;
                    closestDistance = distance;
                }
            }

            return closest;
        }

        void SplitMirroredLoftUvSeams(Mesh mesh, IReadOnlyList<Vector2> sourceUvs, bool longIsU, float seamCross)
        {
            int originalVertexCount = sourceUvs.Count;
            if (mesh == null
                || mesh.vertexCount != originalVertexCount
                || m_WorkingSeamCandidates.Count != originalVertexCount)
                return;

            bool hasCandidates = false;
            for (int index = 0; index < originalVertexCount; index++)
            {
                if (m_WorkingSeamCandidates[index])
                {
                    hasCandidates = true;
                    break;
                }
            }
            if (!hasCandidates)
                return;

            var vertices = new List<Vector3>(originalVertexCount + EstimateSeamCapacity(originalVertexCount));
            mesh.GetVertices(vertices);
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var colors = new List<Color>();
            mesh.GetNormals(normals);
            mesh.GetTangents(tangents);
            mesh.GetColors(colors);
            bool hasNormals = normals.Count == originalVertexCount;
            bool hasTangents = tangents.Count == originalVertexCount;
            bool hasColors = colors.Count == originalVertexCount;

            var uvChannels = new List<Vector4>[8];
            for (int channel = 0; channel < uvChannels.Length; channel++)
            {
                uvChannels[channel] = new List<Vector4>();
                mesh.GetUVs(channel, uvChannels[channel]);
            }

            List<Vector4> editedUvs = uvChannels[m_UvChannel];
            if (editedUvs.Count != originalVertexCount)
                return;

            for (int index = 0; index < originalVertexCount; index++)
            {
                if (!m_WorkingSeamCandidates[index])
                    continue;
                Vector4 uv = editedUvs[index];
                Vector2 leftUv = m_WorkingLeftSeamUvs[index];
                uv.x = leftUv.x;
                uv.y = leftUv.y;
                editedUvs[index] = uv;
            }

            var duplicateForRightSide = new Dictionary<int, int>();
            int subMeshCount = mesh.subMeshCount;
            var subMeshTriangles = new int[subMeshCount][];
            for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
            {
                int[] triangles = mesh.GetTriangles(subMesh);
                subMeshTriangles[subMesh] = triangles;
                for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
                {
                    float averageCross = 0f;
                    for (int corner = 0; corner < 3; corner++)
                    {
                        Vector2 sourceUv = sourceUvs[triangles[triangle + corner]];
                        averageCross += longIsU ? sourceUv.y : sourceUv.x;
                    }
                    bool rightSide = averageCross / 3f >= seamCross;
                    if (!rightSide)
                        continue;

                    for (int corner = 0; corner < 3; corner++)
                    {
                        int originalIndex = triangles[triangle + corner];
                        if (!m_WorkingSeamCandidates[originalIndex])
                            continue;

                        if (!duplicateForRightSide.TryGetValue(originalIndex, out int duplicateIndex))
                        {
                            duplicateIndex = vertices.Count;
                            duplicateForRightSide.Add(originalIndex, duplicateIndex);
                            vertices.Add(vertices[originalIndex]);
                            if (hasNormals) normals.Add(normals[originalIndex]);
                            if (hasTangents) tangents.Add(tangents[originalIndex]);
                            if (hasColors) colors.Add(colors[originalIndex]);

                            for (int channel = 0; channel < uvChannels.Length; channel++)
                            {
                                List<Vector4> channelUvs = uvChannels[channel];
                                if (channelUvs.Count != duplicateIndex)
                                    continue;
                                Vector4 duplicateUv = channelUvs[originalIndex];
                                if (channel == m_UvChannel)
                                {
                                    Vector2 rightUv = m_WorkingRightSeamUvs[originalIndex];
                                    duplicateUv.x = rightUv.x;
                                    duplicateUv.y = rightUv.y;
                                }
                                channelUvs.Add(duplicateUv);
                            }
                        }

                        triangles[triangle + corner] = duplicateIndex;
                    }
                }
            }

            if (duplicateForRightSide.Count == 0)
                return;

            mesh.Clear(false);
            mesh.indexFormat = vertices.Count > ushort.MaxValue
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            if (hasNormals) mesh.SetNormals(normals);
            if (hasTangents) mesh.SetTangents(tangents);
            if (hasColors) mesh.SetColors(colors);
            for (int channel = 0; channel < uvChannels.Length; channel++)
            {
                if (uvChannels[channel].Count == vertices.Count)
                    mesh.SetUVs(channel, uvChannels[channel]);
            }
            mesh.subMeshCount = subMeshCount;
            for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                mesh.SetTriangles(subMeshTriangles[subMesh], subMesh, false);
            mesh.RecalculateBounds();
        }

        static int EstimateSeamCapacity(int vertexCount)
        {
            return Mathf.Max(8, Mathf.CeilToInt(Mathf.Sqrt(vertexCount)));
        }

        public Mesh RebuildOutputMesh(bool forceSourceRefresh = false)
        {
            Mesh previousOutput = m_OutputMesh;
            Mesh source = ResolveSourceMesh();
            bool geometryIsUnchanged = !forceSourceRefresh
                && previousOutput != null
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
                m_ControlPoints[i].mirrorSplitToNext = false;
                m_ControlPoints[i].mirrorBlendLength = 0.15f;
                m_ControlPoints[i].mirrorBlendWidth = 0.05f;
                m_ControlPoints[i].mirrorLeftOffset = 0f;
                m_ControlPoints[i].mirrorRightOffset = 0f;
                m_ControlPoints[i].flipMirrorLeft = true;
                m_ControlPoints[i].flipMirrorRight = false;
                m_ControlPoints[i].mirrorBranchUvWidth = 0.5f;
                m_ControlPoints[i].mirrorLeftScale = 1f;
                m_ControlPoints[i].mirrorRightScale = 1f;
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
                alongOffset = Mathf.Lerp(points[a].alongOffset, points[b].alongOffset, blend),
                mirrorSplitToNext = blend < 0.5f ? points[a].mirrorSplitToNext : points[b].mirrorSplitToNext,
                mirrorBlendLength = Mathf.Lerp(points[a].mirrorBlendLength, points[b].mirrorBlendLength, blend),
                mirrorBlendWidth = Mathf.Lerp(points[a].mirrorBlendWidth, points[b].mirrorBlendWidth, blend),
                mirrorLeftOffset = Mathf.Lerp(points[a].mirrorLeftOffset, points[b].mirrorLeftOffset, blend),
                mirrorRightOffset = Mathf.Lerp(points[a].mirrorRightOffset, points[b].mirrorRightOffset, blend),
                flipMirrorLeft = blend < 0.5f ? points[a].flipMirrorLeft : points[b].flipMirrorLeft,
                flipMirrorRight = blend < 0.5f ? points[a].flipMirrorRight : points[b].flipMirrorRight,
                mirrorBranchUvWidth = Mathf.Lerp(points[a].mirrorBranchUvWidth, points[b].mirrorBranchUvWidth, blend),
                mirrorLeftScale = Mathf.Lerp(points[a].mirrorLeftScale, points[b].mirrorLeftScale, blend),
                mirrorRightScale = Mathf.Lerp(points[a].mirrorRightScale, points[b].mirrorRightScale, blend)
            };
        }

        public bool TryGetCrossUvBounds(out float minCross, out float maxCross)
        {
            minCross = 0f;
            maxCross = 1f;
            Mesh source = ResolveSourceMesh();
            if (source == null)
                return false;

            var uvs = new List<Vector2>();
            source.GetUVs(m_UvChannel, uvs);
            if (uvs.Count != source.vertexCount || uvs.Count == 0)
                return false;

            bool longIsU = ResolveLongitudinalAxis(uvs);
            GetUvBounds(uvs, longIsU, out _, out _, out minCross, out maxCross);
            return maxCross - minCross > Mathf.Epsilon;
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
