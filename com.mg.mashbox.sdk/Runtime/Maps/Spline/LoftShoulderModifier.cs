using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

namespace MashBoxSDK.Maps.Spline
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class LoftShoulderModifier : MonoBehaviour
    {
        public enum Edge
        {
            Left,
            Right,
            Start,
            Finish
        }

        [Serializable]
        public sealed class ShoulderProfile
        {
            public bool enabled;
            [Min(0.01f)] public float width = 3f;
            [Min(1)] public int segments = 4;
            [Tooltip("Vertical offset in meters from the loft edge across the normalized shoulder width.")]
            public AnimationCurve height = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.55f, -1f),
                new Keyframe(1f, -0.35f));
            [Min(0f)] public float verticalScale = 1f;
            public Material materialOverride;
            public bool generateCollider = true;

            [Header("Trail Surface Layers")]
            [Tooltip("Writes MG_Lit_Blend weights to vertex colors: base layer = soil, red = exposed rock, green = grass, blue = puddles (left at zero).")]
            public bool generateLayerMasks;
            [Range(0f, 1f)] public float rockBandStart = 0.24f;
            [Range(0f, 1f)] public float rockBandEnd = 0.68f;
            [Range(0f, 1f)] public float grassBandStart = 0.84f;
            [Range(0.001f, 0.25f)] public float layerBlend = 0.07f;
            [Range(0f, 0.25f)] public float layerVariation = 0.06f;

            [Header("Erosion Breakup")]
            [Tooltip("For left/right trail edges, generate a cut bank only when this edge is higher than the opposite edge.")]
            public bool highSideOnly;
            [Min(0.001f)] public float highSideBlendHeight = 0.2f;
            [Min(0f)] public float erosionAmplitude;
            [Min(0.01f)] public float erosionFrequency = 0.45f;
            [Tooltip("Maximum side-to-side breakup of the generated bank face, in meters.")]
            [Min(0f)] public float horizontalErosionAmplitude;
            [Tooltip("World-space frequency of the side-to-side bank breakup.")]
            [Min(0.01f)] public float horizontalErosionFrequency = 0.5f;
            public float erosionSeed;
        }

        [SerializeField] MultiSplineLoft m_Loft;
        [SerializeField] Material m_LayeredBankMaterial;
        [SerializeField] bool m_UseSharedSideProfile;
        [SerializeField, Tooltip("Makes each generated shoulder's outside-edge normals match the active Terrain directly underneath them. This softens the lighting seam without moving any vertices.")]
        bool m_MatchOuterNormalsToTerrain;
        [SerializeField] ShoulderProfile m_SharedSideProfile = new ShoulderProfile();
        [SerializeField] ShoulderProfile m_Left = new ShoulderProfile();
        [SerializeField] ShoulderProfile m_Right = new ShoulderProfile();
        [SerializeField] ShoulderProfile m_Start = new ShoulderProfile();
        [SerializeField] ShoulderProfile m_Finish = new ShoulderProfile();

        sealed class GeneratedShoulder
        {
            public Mesh mesh;
            public Material material;
            public bool generateCollider;
            public int[] outerVertexIndices;
        }

        readonly List<int> m_CombinedOuterVertexIndices = new List<int>();
        readonly List<int> m_CombinedShoulderSubmeshes = new List<int>();

        public MultiSplineLoft Loft { get => m_Loft; set => m_Loft = value; }
        public Material LayeredBankMaterial { get => m_LayeredBankMaterial; set => m_LayeredBankMaterial = value; }
        public bool UseSharedSideProfile { get => m_UseSharedSideProfile; set => m_UseSharedSideProfile = value; }
        public bool MatchOuterNormalsToTerrain { get => m_MatchOuterNormalsToTerrain; set => m_MatchOuterNormalsToTerrain = value; }
        public ShoulderProfile SharedSideProfile => m_SharedSideProfile;
        public ShoulderProfile Left => m_Left;
        public ShoulderProfile Right => m_Right;
        public ShoulderProfile Start => m_Start;
        public ShoulderProfile Finish => m_Finish;

        internal float GetPackedUv2SideExtent(Edge edge)
        {
            if (edge != Edge.Left && edge != Edge.Right)
                return 0f;

            ShoulderProfile profile = m_UseSharedSideProfile ? m_SharedSideProfile
                : edge == Edge.Left ? m_Left : m_Right;
            if (profile == null || !profile.enabled)
                return 0f;

            const int samples = 16;
            float extent = 0f;
            Vector2 previous = new Vector2(
                0f,
                profile.height != null ? profile.height.Evaluate(0f) * profile.verticalScale : 0f);
            for (int sample = 1; sample <= samples; sample++)
            {
                float t = sample / (float)samples;
                Vector2 current = new Vector2(
                    profile.width * t,
                    profile.height != null ? profile.height.Evaluate(t) * profile.verticalScale : 0f);
                extent += Vector2.Distance(previous, current);
                previous = current;
            }

            // Reserve a little extra atlas width for the maximum erosion offsets.
            return extent + profile.erosionAmplitude * 2f + profile.horizontalErosionAmplitude * 2f;
        }

        public void ApplyErodedTrailPreset()
        {
            m_UseSharedSideProfile = true;
            ConfigureErodedTrailSide(m_SharedSideProfile, 11.37f);
            // Keep the individual profiles useful if the shared profile is
            // disabled later.
            ConfigureErodedTrailSide(m_Left, 11.37f);
            ConfigureErodedTrailSide(m_Right, 37.91f);
            m_Start.enabled = false;
            m_Finish.enabled = false;
        }

        void Reset()
        {
            m_Loft = GetComponentInParent<MultiSplineLoft>();
        }

        void OnValidate()
        {
            m_Left = Sanitize(m_Left);
            m_Right = Sanitize(m_Right);
            m_SharedSideProfile = Sanitize(m_SharedSideProfile);
            m_Start = Sanitize(m_Start);
            m_Finish = Sanitize(m_Finish);
            if (m_Loft == null)
                m_Loft = GetComponentInParent<MultiSplineLoft>();
            m_Loft?.QueueRegenerate();
        }

        public void RebuildFromLoft(MultiSplineLoft loft)
        {
            if (loft == null)
                return;
            m_Loft = loft;
            Transform root = EnsureGeneratedRoot(loft.transform);
            ClearGeneratedShoulders(root);
            m_CombinedOuterVertexIndices.Clear();
            m_CombinedShoulderSubmeshes.Clear();
            var generatedShoulders = new List<GeneratedShoulder>();
            BuildEdge(root, Edge.Left, m_Left, generatedShoulders);
            BuildEdge(root, Edge.Right, m_Right, generatedShoulders);
            BuildEdge(root, Edge.Start, m_Start, generatedShoulders);
            BuildEdge(root, Edge.Finish, m_Finish, generatedShoulders);
            CombineWithLoftMesh(generatedShoulders);
        }

        public void ClearGenerated()
        {
            Transform root = FindGeneratedRoot();
            if (root != null)
                ClearGeneratedShoulders(root);
            m_CombinedOuterVertexIndices.Clear();
            m_CombinedShoulderSubmeshes.Clear();
            m_Loft?.SetShoulderColliderSubmeshes(null);
        }

        void BuildEdge(Transform root, Edge edge, ShoulderProfile profile, List<GeneratedShoulder> generatedShoulders)
        {
            bool usesSharedSideProfile = m_UseSharedSideProfile && (edge == Edge.Left || edge == Edge.Right);
            if (usesSharedSideProfile)
                profile = m_SharedSideProfile;
            if (!profile.enabled)
                return;

            var boundary = new List<Vector3>();
            var inner = new List<Vector3>();
            var pathDistances = new List<float>();
            if (!m_Loft.TryGetShoulderEdge(edge, boundary, inner, pathDistances) || boundary.Count < 2)
                return;

            List<Vector3> oppositeBoundary = GetOppositeBoundary(edge, profile);
            List<Vector3> generatedOuterEdge = BuildGeneratedOuterEdge(edge, profile, boundary, inner, oppositeBoundary);
            LoftShoulderEdgeSpline edgeSpline = EnsureEdgeSpline(root, edge);
            if (m_Loft.TryGetShoulderSourceKnotCount(edge, out int sourceKnotCount))
                edgeSpline.GeneratedPointCount = sourceKnotCount;
            edgeSpline.RefreshGeneratedPath(generatedOuterEdge, m_Loft);
            Mesh mesh = BuildShoulderMesh(
                edge,
                profile,
                boundary,
                inner,
                oppositeBoundary,
                pathDistances,
                edgeSpline,
                usesSharedSideProfile,
                out int[] packedOuterVertexIndices);
            generatedShoulders.Add(new GeneratedShoulder
            {
                mesh = mesh,
                material = profile.materialOverride != null ? profile.materialOverride : m_LayeredBankMaterial,
                generateCollider = profile.generateCollider,
                outerVertexIndices = packedOuterVertexIndices
                    ?? BuildOuterVertexIndices(boundary.Count, Mathf.Max(1, profile.segments) + 1)
            });
        }

        static int[] BuildOuterVertexIndices(int pathCount, int profilePoints)
        {
            var indices = new int[pathCount];
            int outerAcross = profilePoints - 1;
            for (int path = 0; path < pathCount; path++)
                indices[path] = path * profilePoints + outerAcross;
            return indices;
        }

        void CombineWithLoftMesh(IReadOnlyList<GeneratedShoulder> shoulders)
        {
            Mesh loftMesh = m_Loft.GeneratedMesh;
            MeshRenderer loftRenderer = m_Loft.GetComponent<MeshRenderer>();
            Material[] currentMaterials = loftRenderer.sharedMaterials;
            Material baseMaterial = currentMaterials.Length > 0 ? currentMaterials[0] : null;
            if (loftMesh == null || shoulders == null || shoulders.Count == 0)
            {
                m_CombinedOuterVertexIndices.Clear();
                m_CombinedShoulderSubmeshes.Clear();
                loftRenderer.sharedMaterials = new[] { baseMaterial };
                m_Loft.SetShoulderColliderSubmeshes(null);
                return;
            }

            Mesh baseMesh = Instantiate(loftMesh);
            baseMesh.hideFlags = HideFlags.DontSave;
            if (baseMesh.colors == null || baseMesh.colors.Length != baseMesh.vertexCount)
            {
                var baseColors = new Color[baseMesh.vertexCount];
                for (int index = 0; index < baseColors.Length; index++)
                    baseColors[index] = Color.white;
                baseMesh.colors = baseColors;
            }
            var combine = new CombineInstance[shoulders.Count + 1];
            combine[0] = new CombineInstance { mesh = baseMesh, subMeshIndex = 0, transform = Matrix4x4.identity };
            var materials = new Material[combine.Length];
            materials[0] = baseMaterial;
            var colliderSubmeshes = new List<int>();
            int totalVertexCount = baseMesh.vertexCount;
            int vertexOffset = baseMesh.vertexCount;
            m_CombinedOuterVertexIndices.Clear();
            m_CombinedShoulderSubmeshes.Clear();

            for (int index = 0; index < shoulders.Count; index++)
            {
                GeneratedShoulder shoulder = shoulders[index];
                combine[index + 1] = new CombineInstance { mesh = shoulder.mesh, subMeshIndex = 0, transform = Matrix4x4.identity };
                materials[index + 1] = shoulder.material != null ? shoulder.material : baseMaterial;
                totalVertexCount += shoulder.mesh.vertexCount;
                if (shoulder.generateCollider)
                    colliderSubmeshes.Add(index + 1);
                m_CombinedShoulderSubmeshes.Add(index + 1);
                if (shoulder.outerVertexIndices != null)
                {
                    for (int outerIndex = 0; outerIndex < shoulder.outerVertexIndices.Length; outerIndex++)
                        m_CombinedOuterVertexIndices.Add(vertexOffset + shoulder.outerVertexIndices[outerIndex]);
                }
                vertexOffset += shoulder.mesh.vertexCount;
            }

            loftMesh.indexFormat = totalVertexCount > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            loftMesh.CombineMeshes(combine, false, false, false);
            loftMesh.name = $"{m_Loft.gameObject.name} Loft";
            loftMesh.RecalculateBounds();
            loftRenderer.sharedMaterials = materials;
            m_Loft.SetShoulderColliderSubmeshes(colliderSubmeshes);

            DestroyGeneratedMesh(baseMesh);
            for (int index = 0; index < shoulders.Count; index++)
                DestroyGeneratedMesh(shoulders[index].mesh);
        }

        internal bool ApplyTerrainMatchedOuterNormals()
        {
            Mesh mesh = m_Loft != null ? m_Loft.GeneratedMesh : null;
            bool inheritedFromLoft = m_Loft != null && m_Loft.MatchSideNormalsToTerrain;
            if ((!m_MatchOuterNormalsToTerrain && !inheritedFromLoft) ||
                mesh == null ||
                (m_CombinedOuterVertexIndices.Count == 0 && m_CombinedShoulderSubmeshes.Count == 0))
                return false;

            Vector3[] vertices = mesh.vertices;
            var normals = new List<Vector3>();
            mesh.GetNormals(normals);
            if (normals.Count != vertices.Length)
                return false;

            Terrain[] terrains = Terrain.activeTerrains;
            if (terrains == null || terrains.Length == 0)
                return false;

            var matchedVertices = new HashSet<int>();
            for (int index = 0; index < m_CombinedOuterVertexIndices.Count; index++)
                matchedVertices.Add(m_CombinedOuterVertexIndices[index]);

            if (inheritedFromLoft && m_Loft.MatchTerrainIntersectingFaces)
            {
                for (int index = 0; index < m_CombinedShoulderSubmeshes.Count; index++)
                {
                    int submesh = m_CombinedShoulderSubmeshes[index];
                    if (submesh < 0 || submesh >= mesh.subMeshCount)
                        continue;

                    int[] triangles = mesh.GetTriangles(submesh);
                    TerrainNormalUtility.AddIntersectingFaceVertices(
                        m_Loft.transform,
                        vertices,
                        triangles,
                        triangles.Length,
                        m_Loft.TerrainNormalContactDistance,
                        terrains,
                        matchedVertices);
                }
            }

            bool changed = false;
            foreach (int vertexIndex in matchedVertices)
            {
                if ((uint)vertexIndex >= vertices.Length ||
                    !TerrainNormalUtility.TryGetLocalNormal(
                        m_Loft.transform,
                        vertices[vertexIndex],
                        normals[vertexIndex],
                        terrains,
                        out Vector3 terrainNormal))
                    continue;

                normals[vertexIndex] = terrainNormal;
                changed = true;
            }

            if (changed)
                mesh.SetNormals(normals);
            return changed;
        }

        Mesh BuildShoulderMesh(
            Edge edge,
            ShoulderProfile profile,
            IReadOnlyList<Vector3> boundary,
            IReadOnlyList<Vector3> inner,
            IReadOnlyList<Vector3> oppositeBoundary,
            IReadOnlyList<float> pathDistances,
            LoftShoulderEdgeSpline edgeSpline,
            bool usesSharedSideProfile,
            out int[] packedOuterVertexIndices)
        {
            packedOuterVertexIndices = null;
            int profileSegments = Mathf.Max(1, profile.segments);
            int profilePoints = profileSegments + 1;
            int pathCount = boundary.Count;
            var vertices = new List<Vector3>(pathCount * profilePoints);
            var uvs = new List<Vector2>(pathCount * profilePoints);
            var triangles = new List<int>((pathCount - 1) * profileSegments * 6);
            Vector3 localUp = m_Loft.transform.InverseTransformDirection(Vector3.up).normalized;
            if (localUp.sqrMagnitude <= Mathf.Epsilon)
                localUp = Vector3.up;
            float erosionSeed = profile.erosionSeed
                + (usesSharedSideProfile && edge == Edge.Right ? 23.17f : 0f);

            for (int path = 0; path < pathCount; path++)
            {
                float pathDistance = path < pathDistances.Count ? pathDistances[path] : path;
                float pathT = pathCount > 1 ? path / (float)(pathCount - 1) : 0f;
                float highSideBlend = EvaluateHighSideBlend(profile, boundary, oppositeBoundary, path, localUp);
                Vector3 outward = ResolveShoulderDirection(
                    edge,
                    boundary[path],
                    inner[Mathf.Min(path, inner.Count - 1)],
                    localUp,
                    highSideBlend);
                Vector3 bendOffset = Vector3.zero;
                edgeSpline?.TryEvaluateOffset(pathT, m_Loft, out bendOffset);
                for (int across = 0; across < profilePoints; across++)
                {
                    float t = across / (float)profileSegments;
                    float height = profile.height != null
                        ? profile.height.Evaluate(t) * profile.verticalScale * highSideBlend
                        : 0f;
                    float bendInfluence = edgeSpline != null ? edgeSpline.EvaluateInfluence(t) : 0f;
                    float erosionMask = Mathf.Sin(t * Mathf.PI);
                    erosionMask *= erosionMask;
                    float erosion = 0f;
                    float outwardErosion = 0f;
                    if (profile.erosionAmplitude > Mathf.Epsilon)
                    {
                        float noisePosition = pathDistance * profile.erosionFrequency + erosionSeed;
                        erosion = (Mathf.PerlinNoise(noisePosition, t * 3.17f + erosionSeed) - 0.5f)
                            * 2f
                            * profile.erosionAmplitude
                            * erosionMask
                            * highSideBlend;
                    }
                    if (profile.horizontalErosionAmplitude > Mathf.Epsilon)
                    {
                        float horizontalNoisePosition =
                            pathDistance * profile.horizontalErosionFrequency + erosionSeed;
                        outwardErosion = (Mathf.PerlinNoise(
                            horizontalNoisePosition + 19.31f,
                            t * 4.73f + erosionSeed) - 0.5f)
                            * 2f
                            * profile.horizontalErosionAmplitude
                            * erosionMask
                            * highSideBlend;
                    }

                    vertices.Add(
                        boundary[path]
                        + outward * (profile.width * t + outwardErosion)
                        + localUp * (height + erosion)
                        + bendOffset * bendInfluence);
                }
            }

            BuildShoulderUvs(edge, vertices, pathDistances, pathCount, profilePoints, uvs);

            for (int path = 0; path < pathCount - 1; path++)
            {
                for (int across = 0; across < profileSegments; across++)
                {
                    int a = path * profilePoints + across;
                    int b = (path + 1) * profilePoints + across;
                    int c = a + 1;
                    int d = b + 1;
                    bool reverseWinding = Vector3.Dot(
                        Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]),
                        localUp) < 0f;
                    if (reverseWinding)
                    {
                        triangles.Add(a);
                        triangles.Add(c);
                        triangles.Add(b);
                        triangles.Add(c);
                        triangles.Add(d);
                        triangles.Add(b);
                    }
                    else
                    {
                        triangles.Add(a);
                        triangles.Add(b);
                        triangles.Add(c);
                        triangles.Add(c);
                        triangles.Add(b);
                        triangles.Add(d);
                    }
                }
            }

            var mesh = new Mesh
            {
                name = $"{m_Loft.gameObject.name} {GetEdgeName(edge)}",
                hideFlags = HideFlags.DontSave,
                indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            var colors = new List<Color>(vertices.Count);
            if (profile.generateLayerMasks)
            {
                for (int path = 0; path < pathCount; path++)
                {
                    float pathDistance = path < pathDistances.Count ? pathDistances[path] : path;
                    float highSideBlend = EvaluateHighSideBlend(profile, boundary, oppositeBoundary, path, localUp);
                    float layerNoise = (Mathf.PerlinNoise(
                        pathDistance * profile.erosionFrequency + erosionSeed + 53.17f,
                        erosionSeed + 7.13f) - 0.5f) * 2f;
                    for (int across = 0; across < profilePoints; across++)
                    {
                        float t = across / (float)profileSegments;
                        float variationMask = Mathf.Sin(t * Mathf.PI);
                        float variedT = Mathf.Clamp01(t + layerNoise * profile.layerVariation * variationMask);
                        colors.Add(EvaluateLayerMask(profile, variedT, highSideBlend));
                    }
                }
            }
            else
            {
                for (int index = 0; index < vertices.Count; index++)
                    colors.Add(Color.white);
            }
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            if (m_Loft.GeneratePackedUv2)
            {
                if (edge == Edge.Left || edge == Edge.Right)
                {
                    packedOuterVertexIndices = ApplyPackedUv2ToSideShoulder(
                        mesh,
                        edge,
                        pathDistances,
                        pathCount,
                        profilePoints);
                }
                else
                {
                    var packedUv2s = new List<Vector2>(mesh.vertexCount);
                    for (int index = 0; index < mesh.vertexCount; index++)
                        packedUv2s.Add(Vector2.zero);
                    mesh.SetUVs(2, packedUv2s);
                }
            }
            return mesh;
        }

        int[] ApplyPackedUv2ToSideShoulder(
            Mesh mesh,
            Edge edge,
            IReadOnlyList<float> pathDistances,
            int pathCount,
            int profilePoints)
        {
            Vector3[] sourceVertices = mesh.vertices;
            Vector3[] sourceNormals = mesh.normals;
            Vector4[] sourceTangents = mesh.tangents;
            Vector2[] sourceUv0 = mesh.uv;
            Color[] sourceColors = mesh.colors;
            int[] sourceTriangles = mesh.triangles;
            var surfaceDistances = new float[sourceVertices.Length];
            for (int path = 0; path < pathCount; path++)
            {
                float distance = 0f;
                int rowStart = path * profilePoints;
                for (int across = 1; across < profilePoints; across++)
                {
                    distance += Vector3.Distance(
                        sourceVertices[rowStart + across - 1],
                        sourceVertices[rowStart + across]);
                    surfaceDistances[rowStart + across] = distance;
                }
            }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uv0 = new List<Vector2>();
            var packedUv2 = new List<Vector2>();
            var colors = new List<Color>();
            var triangles = new List<int>(sourceTriangles.Length);
            var remappedVertices = new Dictionary<long, int>();
            var outerVertices = new HashSet<int>();

            for (int triangle = 0; triangle + 2 < sourceTriangles.Length; triangle += 3)
            {
                int sourceA = sourceTriangles[triangle];
                int sourceB = sourceTriangles[triangle + 1];
                int sourceC = sourceTriangles[triangle + 2];
                int segment = Mathf.Clamp(
                    Mathf.Min(sourceA / profilePoints, sourceB / profilePoints, sourceC / profilePoints),
                    0,
                    pathCount - 2);
                float startDistance = segment < pathDistances.Count ? pathDistances[segment] : segment;
                float endDistance = segment + 1 < pathDistances.Count
                    ? pathDistances[segment + 1]
                    : segment + 1;
                int shell = m_Loft.GetPackedUv2ShellForDistanceRange(startDistance, endDistance);

                triangles.Add(GetPackedShoulderVertex(sourceA, shell));
                triangles.Add(GetPackedShoulderVertex(sourceB, shell));
                triangles.Add(GetPackedShoulderVertex(sourceC, shell));
            }

            mesh.Clear(false);
            mesh.indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            if (tangents.Count == vertices.Count)
                mesh.SetTangents(tangents);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(2, packedUv2);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return new List<int>(outerVertices).ToArray();

            int GetPackedShoulderVertex(int sourceVertex, int shell)
            {
                long key = ((long)shell << 32) | (uint)sourceVertex;
                if (remappedVertices.TryGetValue(key, out int existing))
                    return existing;

                int path = sourceVertex / profilePoints;
                float alongDistance = path < pathDistances.Count ? pathDistances[path] : path;
                int remapped = vertices.Count;
                remappedVertices.Add(key, remapped);
                vertices.Add(sourceVertices[sourceVertex]);
                normals.Add(sourceVertex < sourceNormals.Length ? sourceNormals[sourceVertex] : Vector3.up);
                if (sourceTangents.Length == sourceVertices.Length)
                    tangents.Add(sourceTangents[sourceVertex]);
                uv0.Add(sourceVertex < sourceUv0.Length ? sourceUv0[sourceVertex] : Vector2.zero);
                packedUv2.Add(m_Loft.CalculatePackedUv2ForSideShoulder(
                    edge,
                    surfaceDistances[sourceVertex],
                    alongDistance,
                    shell));
                colors.Add(sourceVertex < sourceColors.Length ? sourceColors[sourceVertex] : Color.white);
                if (sourceVertex % profilePoints == profilePoints - 1)
                    outerVertices.Add(remapped);
                return remapped;
            }
        }

        void BuildShoulderUvs(
            Edge edge,
            IReadOnlyList<Vector3> vertices,
            IReadOnlyList<float> pathDistances,
            int pathCount,
            int profilePoints,
            List<Vector2> uvs)
        {
            float acrossUvPerMeter = m_Loft.CurrentUvAcrossPerMeter;
            float alongUvPerMeter = m_Loft.CurrentUvAlongPerMeter;
            bool sideEdge = edge == Edge.Left || edge == Edge.Right;
            float edgeUv = edge switch
            {
                Edge.Right => m_Loft.CurrentUvAcrossExtent,
                Edge.Finish => m_Loft.CurrentAlongDistance * alongUvPerMeter,
                _ => 0f
            };
            float outwardSign = edge == Edge.Left || edge == Edge.Start ? -1f : 1f;

            for (int path = 0; path < pathCount; path++)
            {
                float mainSurfaceDistance = path < pathDistances.Count ? pathDistances[path] : path;
                float shoulderSurfaceDistance = 0f;
                for (int across = 0; across < profilePoints; across++)
                {
                    int vertexIndex = path * profilePoints + across;
                    if (across > 0)
                        shoulderSurfaceDistance += Vector3.Distance(
                            vertices[vertexIndex - 1],
                            vertices[vertexIndex]);

                    if (sideEdge)
                    {
                        float u = edgeUv + outwardSign * shoulderSurfaceDistance * acrossUvPerMeter;
                        float v = mainSurfaceDistance * alongUvPerMeter;
                        uvs.Add(new Vector2(u, v));
                    }
                    else
                    {
                        float u = mainSurfaceDistance * acrossUvPerMeter;
                        float v = edgeUv + outwardSign * shoulderSurfaceDistance * alongUvPerMeter;
                        uvs.Add(new Vector2(u, v));
                    }
                }
            }
        }

        static Color EvaluateLayerMask(ShoulderProfile profile, float t, float highSideBlend)
        {
            float blend = Mathf.Max(0.001f, profile.layerBlend);
            float rockIn = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(profile.rockBandStart - blend, profile.rockBandStart + blend, t));
            float rockOut = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(profile.rockBandEnd - blend, profile.rockBandEnd + blend, t));
            float rock = Mathf.Clamp01(rockIn * rockOut) * highSideBlend;
            float grass = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(profile.grassBandStart - blend, profile.grassBandStart + blend, t));
            rock *= 1f - grass;
            // MG_Lit_Blend treats Layer 1 as the unpainted base, then uses red
            // for Layer 2, green for Layer 3, and blue for puddles.
            return new Color(rock, grass, 0f, 1f);
        }

        List<Vector3> BuildGeneratedOuterEdge(
            Edge edge,
            ShoulderProfile profile,
            IReadOnlyList<Vector3> boundary,
            IReadOnlyList<Vector3> inner,
            IReadOnlyList<Vector3> oppositeBoundary)
        {
            var outerEdge = new List<Vector3>(boundary.Count);
            Vector3 localUp = m_Loft.transform.InverseTransformDirection(Vector3.up).normalized;
            if (localUp.sqrMagnitude <= Mathf.Epsilon)
                localUp = Vector3.up;
            float outerHeight = profile.height != null ? profile.height.Evaluate(1f) * profile.verticalScale : 0f;

            for (int path = 0; path < boundary.Count; path++)
            {
                float highSideBlend = EvaluateHighSideBlend(profile, boundary, oppositeBoundary, path, localUp);
                Vector3 outward = ResolveShoulderDirection(
                    edge,
                    boundary[path],
                    inner[Mathf.Min(path, inner.Count - 1)],
                    localUp,
                    highSideBlend);
                outerEdge.Add(
                    boundary[path]
                    + outward * profile.width
                    + localUp * (outerHeight * highSideBlend));
            }

            return outerEdge;
        }

        List<Vector3> GetOppositeBoundary(Edge edge, ShoulderProfile profile)
        {
            if (!profile.highSideOnly || (edge != Edge.Left && edge != Edge.Right))
                return null;

            var oppositeBoundary = new List<Vector3>();
            var oppositeInner = new List<Vector3>();
            var oppositeDistances = new List<float>();
            Edge oppositeEdge = edge == Edge.Left ? Edge.Right : Edge.Left;
            return m_Loft.TryGetShoulderEdge(oppositeEdge, oppositeBoundary, oppositeInner, oppositeDistances)
                ? oppositeBoundary
                : null;
        }

        static float EvaluateHighSideBlend(
            ShoulderProfile profile,
            IReadOnlyList<Vector3> boundary,
            IReadOnlyList<Vector3> oppositeBoundary,
            int path,
            Vector3 localUp)
        {
            if (!profile.highSideOnly
                || oppositeBoundary == null
                || oppositeBoundary.Count == 0
                || boundary == null
                || boundary.Count == 0)
                return 1f;

            int oppositeIndex = Mathf.Clamp(path, 0, oppositeBoundary.Count - 1);
            float heightDelta = Vector3.Dot(boundary[path] - oppositeBoundary[oppositeIndex], localUp);
            return Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(heightDelta / Mathf.Max(0.001f, profile.highSideBlendHeight)));
        }

        static Vector3 ResolveShoulderDirection(
            Edge edge,
            Vector3 boundary,
            Vector3 inner,
            Vector3 localUp,
            float highSideBlend)
        {
            Vector3 faceDirection = boundary - inner;
            if (faceDirection.sqrMagnitude <= 0.000001f)
            {
                faceDirection = edge switch
                {
                    Edge.Left => Vector3.left,
                    Edge.Right => Vector3.right,
                    Edge.Start => Vector3.back,
                    _ => Vector3.forward
                };
            }
            faceDirection.Normalize();

            Vector3 flatDirection = Vector3.ProjectOnPlane(faceDirection, localUp);
            if (flatDirection.sqrMagnitude <= 0.000001f)
                flatDirection = faceDirection;
            else
                flatDirection.Normalize();

            Vector3 direction = Vector3.Lerp(flatDirection, faceDirection, highSideBlend);
            return direction.sqrMagnitude > 0.000001f ? direction.normalized : flatDirection;
        }

        LoftShoulderEdgeSpline EnsureEdgeSpline(Transform root, Edge edge)
        {
            string objectName = $"{GetEdgeName(edge)} Bend Spline";
            Transform child = root.Find(objectName);
            if (child == null)
            {
                var splineObject = new GameObject(objectName, typeof(SplineContainer), typeof(LoftShoulderEdgeSpline));
                splineObject.transform.SetParent(root, false);
                child = splineObject.transform;
            }

            child.gameObject.layer = m_Loft.gameObject.layer;
            child.gameObject.isStatic = false;
            LoftShoulderEdgeSpline edgeSpline = child.GetComponent<LoftShoulderEdgeSpline>();
            edgeSpline.Modifier = this;
            edgeSpline.Edge = edge;
            return edgeSpline;
        }

        Vector3 GetFallbackOutward(Edge edge)
        {
            return edge switch
            {
                Edge.Left => Vector3.left,
                Edge.Right => Vector3.right,
                Edge.Start => Vector3.back,
                _ => Vector3.forward
            };
        }

        Transform EnsureGeneratedRoot(Transform parent)
        {
            Transform root = FindGeneratedRoot();
            if (root != null)
                return root;
            var rootObject = new GameObject("Shoulders");
            rootObject.layer = parent.gameObject.layer;
            rootObject.isStatic = parent.gameObject.isStatic;
            rootObject.transform.SetParent(parent, false);
            return rootObject.transform;
        }

        Transform FindGeneratedRoot()
        {
            Transform parent = m_Loft != null ? m_Loft.transform : transform;
            return parent.Find("Shoulders");
        }

        static ShoulderProfile Sanitize(ShoulderProfile profile)
        {
            profile ??= new ShoulderProfile();
            profile.width = Mathf.Max(0.01f, profile.width);
            profile.segments = Mathf.Max(1, profile.segments);
            profile.verticalScale = Mathf.Max(0f, profile.verticalScale);
            profile.rockBandStart = Mathf.Clamp01(profile.rockBandStart);
            profile.rockBandEnd = Mathf.Clamp(profile.rockBandEnd, profile.rockBandStart, 1f);
            profile.grassBandStart = Mathf.Clamp(profile.grassBandStart, profile.rockBandEnd, 1f);
            profile.layerBlend = Mathf.Clamp(profile.layerBlend, 0.001f, 0.25f);
            profile.layerVariation = Mathf.Clamp(profile.layerVariation, 0f, 0.25f);
            profile.highSideBlendHeight = Mathf.Max(0.001f, profile.highSideBlendHeight);
            profile.erosionAmplitude = Mathf.Max(0f, profile.erosionAmplitude);
            profile.erosionFrequency = Mathf.Max(0.01f, profile.erosionFrequency);
            profile.horizontalErosionAmplitude = Mathf.Max(0f, profile.horizontalErosionAmplitude);
            profile.horizontalErosionFrequency = Mathf.Max(0.01f, profile.horizontalErosionFrequency);
            profile.height ??= AnimationCurve.Linear(0f, 0f, 1f, 0f);
            return profile;
        }

        static void ConfigureErodedTrailSide(ShoulderProfile profile, float seed)
        {
            profile.enabled = true;
            profile.width = 3f;
            profile.segments = 6;
            profile.height = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f),
                new Keyframe(0.38f, 1f, 0.35f, 0.35f),
                new Keyframe(0.6f, 1.16f, 0f, 0f),
                new Keyframe(1f, -2.8f, 0f, 0f));
            profile.verticalScale = 0.25f;
            profile.generateCollider = true;
            profile.generateLayerMasks = true;
            profile.rockBandStart = 0.24f;
            profile.rockBandEnd = 0.68f;
            profile.grassBandStart = 0.84f;
            profile.layerBlend = 0.0623f;
            profile.layerVariation = 0.065f;
            profile.highSideOnly = true;
            profile.highSideBlendHeight = 1f;
            profile.erosionAmplitude = 0.25f;
            profile.erosionFrequency = 0.5f;
            profile.horizontalErosionAmplitude = 0.09f;
            profile.horizontalErosionFrequency = 0.5f;
            profile.erosionSeed = seed;
        }

        static string GetEdgeName(Edge edge)
        {
            return edge switch
            {
                Edge.Left => "Left Shoulder",
                Edge.Right => "Right Shoulder",
                Edge.Start => "Start Shoulder",
                _ => "Finish Shoulder"
            };
        }

        static void ClearGeneratedShoulders(Transform root)
        {
            for (int index = root.childCount - 1; index >= 0; index--)
            {
                GameObject child = root.GetChild(index).gameObject;
                if (child.GetComponent<LoftShoulderEdgeSpline>() == null)
                    DestroyShoulderObject(child);
            }
        }

        static void DestroyShoulderObject(GameObject shoulderObject)
        {
            if (shoulderObject == null)
                return;
            MeshFilter filter = shoulderObject.GetComponent<MeshFilter>();
            if (filter != null)
            {
                Mesh mesh = filter.sharedMesh;
                filter.sharedMesh = null;
                MeshCollider collider = shoulderObject.GetComponent<MeshCollider>();
                if (collider != null)
                    collider.sharedMesh = null;
                DestroyGeneratedMesh(mesh);
            }
            DestroyGeneratedObject(shoulderObject);
        }

        static void DestroyGeneratedMesh(Mesh mesh)
        {
            if (mesh != null && (mesh.hideFlags & HideFlags.DontSave) != 0)
                DestroyGeneratedObject(mesh);
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
    }
}
