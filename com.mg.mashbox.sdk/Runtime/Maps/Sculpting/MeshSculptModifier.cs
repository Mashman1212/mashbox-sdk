using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.Spline;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEngine;

namespace MashBoxSDK.Maps.Sculpting
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class MeshSculptModifier : MonoBehaviour
    {
        public enum SculptMode { Displace, Smooth, Flatten, Noise, SeamFit, MeshStamp, SetHeight }
        public enum StrokeSpace { World, TargetLocal }

        [Serializable]
        public sealed class Stroke
        {
            public SculptMode mode;
            public StrokeSpace space;
            public Vector3 position;
            public Vector3 direction = Vector3.up;
            [Min(0.001f)] public float radius = 1f;
            public float strength = 0.1f;
            [Min(0.01f)] public float falloff = 2f;
            public BrushMask brushMask;
            public int noiseSeed;
            public float targetHeight;
            [Range(1, 128)] public int smoothIterations = 1;
            // Seam fitting is baked at paint time. Sparse local deltas and normal
            // samples keep replay independent of target loft edits or deletion.
            public int seamVertexCount;
            public SeamVertex[] seamVertices;
        }

        [Serializable]
        public struct SeamVertex
        {
            public int index;
            public Vector3 delta;
            public Vector3 normal;
            public float normalWeight;
        }

        [SerializeField] MeshFilter m_Target;
        [SerializeField] MultiSplineLoft m_LinkedLoft;
        [SerializeField] bool m_UpdateMeshCollider = true;
        [SerializeField] List<Stroke> m_Strokes = new List<Stroke>();
        [SerializeField, HideInInspector] Mesh m_SourceMesh;
        [SerializeField, HideInInspector] Mesh m_OutputMesh;

        [NonSerialized] readonly List<Vector3> m_DirectVertexReadback = new List<Vector3>();
        [NonSerialized] Vector3[] m_DirectVertices;
        [NonSerialized] Vector3[] m_BaseVertices;
        [NonSerialized] Mesh m_BaseMesh;
        [NonSerialized] List<int>[] m_Neighbours;
        [NonSerialized] Vector3[] m_SmoothSnapshot;
        [NonSerialized] Vector3[] m_LoftSeamBase;
        [NonSerialized] readonly List<List<int>> m_LoftSeamGroups = new List<List<int>>();
        [NonSerialized] readonly List<int> m_SmoothAffected = new List<int>();
        [NonSerialized] readonly List<float> m_SmoothWeights = new List<float>();
        [NonSerialized] readonly List<float> m_SmoothHeights = new List<float>();
        static readonly Unity.Profiling.ProfilerMarker s_TerrainSmoothMarker = new Unity.Profiling.ProfilerMarker("MeshSculpt.SmoothTerrain");

#if UNITY_EDITOR
        // OnValidate is called for the component Unity restores, including an
        // undo that removes its final stroke. Do not inspect/replay other meshes.
        public static event Action<MeshSculptModifier> RestoredByUndo;
        void OnValidate()
        {
            if (UnityEditor.Undo.isProcessing) RestoredByUndo?.Invoke(this);
        }
#endif

        public MeshFilter Target => m_Target;
        public MultiSplineLoft LinkedLoft => m_LinkedLoft;
        public bool UpdateMeshCollider { get => m_UpdateMeshCollider; set => m_UpdateMeshCollider = value; }
        [NonSerialized] Stroke m_TerrainStroke;
        public bool IsDirectTerrain => m_LinkedLoft == null && m_Target != null
            && m_Target.GetComponentInParent<MGTerrain>() is MGTerrain terrain && terrain.MeshFilter == m_Target;
#if UNITY_EDITOR
        public static event Action<MeshSculptModifier> PrepareTerrainEdit;
#endif
        public List<Stroke> Strokes => m_Strokes;
        public int StrokeCount => m_Strokes.Count;

        public void SetTarget(MeshFilter target)
        {
            if (m_Target == target) return;
            m_Target = target;
            m_LinkedLoft = target != null ? target.GetComponent<MultiSplineLoft>() : null;
            m_SourceMesh = null;
            m_OutputMesh = null;
            ClearBaseCache();
        }

        public void LinkToLoft(MultiSplineLoft loft)
        {
            m_LinkedLoft = loft;
            m_Target = loft != null ? loft.GetComponent<MeshFilter>() : m_Target;
            ClearBaseCache();
        }

        public Stroke CreateStroke(SculptMode mode, StrokeSpace space, Vector3 worldPosition, Vector3 worldDirection, float radius, float strength, float falloff)
        {
            Transform targetTransform = m_Target != null ? m_Target.transform : transform;
            return new Stroke
            {
                mode = mode,
                space = space,
                position = space == StrokeSpace.World ? worldPosition : targetTransform.InverseTransformPoint(worldPosition),
                direction = space == StrokeSpace.World ? worldDirection.normalized : targetTransform.InverseTransformDirection(worldDirection).normalized,
                radius = Mathf.Max(0.001f, radius),
                strength = strength,
                falloff = Mathf.Max(0.01f, falloff),
                noiseSeed = Guid.NewGuid().GetHashCode()
            };
        }

        public void AddStroke(Stroke stroke)
        {
            if (stroke == null) return;
            stroke.radius = Mathf.Max(0.001f, stroke.radius);
            stroke.falloff = Mathf.Max(0.01f, stroke.falloff);
            stroke.direction = stroke.direction.sqrMagnitude > Mathf.Epsilon ? stroke.direction.normalized : Vector3.up;
            if (IsDirectTerrain)
            {
#if UNITY_EDITOR
                PrepareTerrainEdit?.Invoke(this);
#endif
                m_Strokes.Clear();
                m_TerrainStroke = stroke;
                return;
            }
            m_Strokes.Add(stroke);
        }

        public void RemoveLastStroke() { if (m_Strokes.Count > 0) m_Strokes.RemoveAt(m_Strokes.Count - 1); }
        public void ClearStrokes() { m_Strokes.Clear(); }

        public void Rebuild()
        {
            if (IsDirectTerrain)
            {
                m_Target.GetComponentInParent<MGTerrain>().NotifySurfaceMeshChanged();
                FinalizeStrokePreview();
                return;
            }
            if (m_LinkedLoft != null)
            {
                if (m_BaseMesh == m_LinkedLoft.GeneratedMesh && m_BaseVertices != null)
                {
                    ApplyFromCachedBase(m_LinkedLoft.GeneratedMesh);
                    UVSpline uvSpline = m_LinkedLoft.GeneratedUvSpline;
                    if (uvSpline != null && (m_LinkedLoft.GenerateUvSplineWithLoft || uvSpline.OutputMesh != null))
                    {
                        m_Target.sharedMesh = m_LinkedLoft.GeneratedMesh;
                        uvSpline.RebuildOutputMesh(forceSourceRefresh: true);
                    }
                }
                else
                    m_LinkedLoft.Regenerate();
                return;
            }

            if (m_Target == null || m_Target.sharedMesh == null) return;
            EnsureStandaloneOutput();
            ApplyFromCachedBase(m_OutputMesh);
        }

        // Applies only the newest stroke to the current mesh.  The full replay is
        // still used when the loft changes or when undo/redo needs to restore a
        // deterministic result, but editor dragging must not replay every earlier
        // stroke for each new brush sample.
        public void ApplyLatestStrokePreview()
        {
            if (IsDirectTerrain)
            {
                if (m_TerrainStroke == null || m_Target.sharedMesh == null) return;
                Mesh directMesh = m_Target.sharedMesh;
                if (m_BaseMesh != directMesh) { ClearBaseCache(); m_BaseMesh = directMesh; }
                // Always refresh from the mesh: Undo and seam joining can change it between dabs.
                directMesh.GetVertices(m_DirectVertexReadback);
                if (m_DirectVertices == null || m_DirectVertices.Length != m_DirectVertexReadback.Count)
                    m_DirectVertices = new Vector3[m_DirectVertexReadback.Count];
                m_DirectVertexReadback.CopyTo(m_DirectVertices);
                var directVertices = m_DirectVertices;
                ApplyStroke(directVertices, directMesh, m_TerrainStroke);
                directMesh.vertices = directVertices;
                directMesh.RecalculateNormals();
                directMesh.RecalculateBounds();
                // Seam normals use only this dab; no replay history is retained.
                m_Strokes.Add(m_TerrainStroke);
                try { ApplySeamNormals(directMesh); }
                finally { m_Strokes.Clear(); }
                if (directMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent)) directMesh.RecalculateTangents();
                directMesh.UploadMeshData(false);
                m_Target.GetComponentInParent<MGTerrain>().NotifySurfaceMeshChanged();
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(directMesh);
#endif
                return;
            }
            if (m_Strokes.Count == 0 || m_Target == null)
                return;

            Mesh mesh = m_LinkedLoft != null ? m_LinkedLoft.GeneratedMesh : m_OutputMesh;
            if (mesh == null || m_BaseMesh != mesh || m_BaseVertices == null)
            {
                Rebuild();
                PublishLoftStrokePreview();
                return;
            }

            Vector3[] vertices = mesh.vertices;
            ApplyStroke(vertices, mesh, m_Strokes[m_Strokes.Count - 1]);
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            bool matchedTerrainNormals = m_LinkedLoft != null && m_LinkedLoft.RefreshTerrainMatchedNormals();
            bool matchedSeamNormals = ApplySeamNormals(mesh);
            if ((!matchedTerrainNormals || matchedSeamNormals) && mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent))
                mesh.RecalculateTangents();
            mesh.UploadMeshData(false);
            PublishLoftStrokePreview();
            if (m_Target != null)
            {
                var terrain = m_Target.GetComponentInParent<MGTerrain>();
                if (terrain != null && terrain.MeshFilter == m_Target)
                    terrain.NotifySurfaceMeshChanged();
            }
        }

        void PublishLoftStrokePreview()
        {
            if (m_LinkedLoft == null || m_Target == null) return;
            Mesh generated = m_LinkedLoft.GeneratedMesh;
            if (generated == null) return;
            UVSpline uvSpline = m_LinkedLoft.GeneratedUvSpline;
            if (uvSpline == null || uvSpline.Target != m_Target)
            {
                uvSpline = null;
                foreach (var candidate in m_LinkedLoft.GetComponentsInChildren<UVSpline>(true))
                    if (candidate.Target == m_Target)
                    {
                        uvSpline = candidate;
                        break;
                    }
            }
            if (uvSpline != null && uvSpline.OutputMesh != null)
            {
                uvSpline.RefreshSculptPreview(generated);
                m_Target.sharedMesh = uvSpline.OutputMesh;
            }
            else m_Target.sharedMesh = generated;
        }

        // Expensive derived data is refreshed once after a drag, rather than for
        // every sampled brush position.
        public void FinalizeStrokePreview()
        {
            if (m_Target == null)
                return;

            if (m_UpdateMeshCollider && m_LinkedLoft != null)
                m_LinkedLoft.RebuildColliderChunks();
            else if (m_UpdateMeshCollider && (!IsDirectTerrain
                || m_Target.GetComponentInParent<MGTerrain>().SurfaceColliderChunks.Count == 0)
                && m_Target.TryGetComponent(out MeshCollider collider))
            {
                collider.sharedMesh = null;
                collider.sharedMesh = m_Target.sharedMesh;
            }

            if (m_Target != null)
            {
                var terrain = m_Target.GetComponentInParent<MGTerrain>();
                if (terrain != null && terrain.MeshFilter == m_Target)
                    terrain.RefreshSurfaceCollidersFromMesh();
            }
            ConformTerrainInstancesForLatestStroke();
            if (IsDirectTerrain)
            {
                m_TerrainStroke = null;
#if UNITY_EDITOR
                // Leave terrain assets dirty for Unity's normal explicit save workflow.
                // Saving here stalls each tile at stroke finalization.
                UnityEditor.EditorUtility.SetDirty(m_Target.sharedMesh);
#endif
            }

            UVSpline uvSpline = m_LinkedLoft != null ? m_LinkedLoft.GeneratedUvSpline : null;
            if (uvSpline != null && (m_LinkedLoft.GenerateUvSplineWithLoft || uvSpline.OutputMesh != null))
            {
                m_Target.sharedMesh = m_LinkedLoft.GeneratedMesh;
                uvSpline.RebuildOutputMesh(forceSourceRefresh: true);
            }
        }

        public void ApplyToFreshMesh(Mesh mesh)
        {
            if (mesh == null || m_Target == null) return;
            m_BaseMesh = mesh;
            m_BaseVertices = mesh.vertices;
            m_Neighbours = null;
            ApplyFromCachedBase(mesh, false);
        }

        void EnsureStandaloneOutput()
        {
            if (m_Target == null || m_Target.sharedMesh == null) return;
            if (m_OutputMesh != null && m_Target.sharedMesh == m_OutputMesh && m_BaseVertices != null) return;

            if (m_OutputMesh != null && m_Target.sharedMesh == m_OutputMesh && m_SourceMesh != null)
            {
                m_BaseMesh = m_OutputMesh;
                m_BaseVertices = m_SourceMesh.vertices;
                m_Neighbours = null;
                return;
            }

            m_SourceMesh = m_Target.sharedMesh;
            m_OutputMesh = Instantiate(m_SourceMesh);
            m_OutputMesh.name = m_SourceMesh.name + " Sculpted";
            m_OutputMesh.MarkDynamic();
            m_Target.sharedMesh = m_OutputMesh;
            m_BaseMesh = m_OutputMesh;
            m_BaseVertices = m_SourceMesh.vertices;
            m_Neighbours = null;
        }

        void ApplyFromCachedBase(Mesh mesh, bool updateCollider = true)
        {
            if (mesh == null || m_BaseVertices == null || m_BaseVertices.Length != mesh.vertexCount || m_Target == null) return;
            Vector3[] vertices = (Vector3[])m_BaseVertices.Clone();
            for (int i = 0; i < m_Strokes.Count; i++) ApplyStroke(vertices, mesh, m_Strokes[i]);

            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            // Fresh loft generation finishes its normal passes after sculpting.
            // Apply seam normals there once, after the loft's terrain matching.
            bool deferNormals = m_LinkedLoft != null && !updateCollider;
            bool matchedTerrainNormals = !deferNormals && m_LinkedLoft != null && m_LinkedLoft.RefreshTerrainMatchedNormals();
            bool matchedSeamNormals = !deferNormals && ApplySeamNormals(mesh);
            if ((!matchedTerrainNormals || matchedSeamNormals) && mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent))
                mesh.RecalculateTangents();
            mesh.UploadMeshData(false);
            if (m_Target != null)
            {
                var terrain = m_Target.GetComponentInParent<MGTerrain>();
                if (terrain != null && terrain.MeshFilter == m_Target)
                    terrain.NotifySurfaceMeshChanged();
            }

            if (m_UpdateMeshCollider && updateCollider && m_LinkedLoft != null)
            {
                m_LinkedLoft.RebuildColliderChunks();
            }
            else if (m_UpdateMeshCollider && updateCollider && m_Target.TryGetComponent(out MeshCollider collider))
            {
                collider.sharedMesh = null;
                collider.sharedMesh = mesh;
            }

            if (updateCollider)
            {
                var terrain = m_Target.GetComponentInParent<MGTerrain>();
                if (terrain != null && terrain.MeshFilter == m_Target)
                    terrain.RefreshSurfaceCollidersFromMesh();
                ConformTerrainInstances();
            }
        }

        void ApplyStroke(Vector3[] vertices, Mesh mesh, Stroke stroke)
        {
            if (stroke == null || stroke.radius <= Mathf.Epsilon) return;
            int passes = stroke.mode == SculptMode.Smooth ? Mathf.Clamp(stroke.smoothIterations, 1, 128) : 1;
            if (stroke.mode == SculptMode.Smooth)
            {
                var terrain = m_Target.GetComponentInParent<MGTerrain>();
                if (terrain != null && terrain.HeightOnlySculpt)
                {
                    ApplyTerrainSmooth(vertices, mesh, stroke, passes);
                    return;
                }
            }
            if (m_LinkedLoft != null) EnsureLoftSeamGroups();
            for (int pass = 0; pass < passes; pass++)
            {
                ApplyStrokePass(vertices, mesh, stroke);
                if (m_LinkedLoft != null) SynchronizeLoftSeams(vertices);
            }
        }

        void EnsureLoftSeamGroups()
        {
            if (ReferenceEquals(m_LoftSeamBase, m_BaseVertices)) return;
            m_LoftSeamBase = m_BaseVertices;
            m_LoftSeamGroups.Clear();
            if (m_LoftSeamBase == null) return;
            // Use the clean base so group membership survives deformation and
            // replay. Keep topology, UV seams and hard normals separate; only
            // vertices at exactly the same original position are constrained.
            var firstAtPosition = new Dictionary<Vector3, int>();
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < m_LoftSeamBase.Length; i++)
            {
                if (!firstAtPosition.TryGetValue(m_LoftSeamBase[i], out int first))
                {
                    firstAtPosition.Add(m_LoftSeamBase[i], i);
                    continue;
                }
                if (!groups.TryGetValue(first, out var group))
                {
                    group = new List<int> { first };
                    groups.Add(first, group);
                    m_LoftSeamGroups.Add(group);
                }
                group.Add(i);
            }
        }

        void SynchronizeLoftSeams(Vector3[] vertices)
        {
            foreach (var group in m_LoftSeamGroups)
            {
                Vector3 position = vertices[group[0]];
                Vector3 delta = Vector3.zero;
                for (int i = 1; i < group.Count; i++) delta += vertices[group[i]] - position;
                position += delta / group.Count;
                foreach (int index in group) vertices[index] = position;
            }
        }

        void ApplyTerrainSmooth(Vector3[] vertices, Mesh mesh, Stroke stroke, int passes)
        {
            using var profile = s_TerrainSmoothMarker.Auto();
            float strength = Mathf.Abs(stroke.strength);
            if (strength == 0f) return;
            var targetTransform = m_Target.transform;
            Vector3 localCenter = stroke.space == StrokeSpace.World
                ? targetTransform.InverseTransformPoint(stroke.position) : stroke.position;
            Matrix4x4 localToWorld = targetTransform.localToWorldMatrix;
            float radiusSquared = stroke.radius * stroke.radius;
            m_SmoothAffected.Clear();
            m_SmoothWeights.Clear();
            m_SmoothHeights.Clear();

            // Height-only smoothing cannot change footprint membership or falloff.
            // Calculate them once per dab instead of scanning/transformation of
            // the whole terrain again for each of the smoothing passes.
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 delta = vertices[i] - localCenter;
                delta.y = 0f;
                float distanceSquared = localToWorld.MultiplyVector(delta).sqrMagnitude;
                if (distanceSquared >= radiusSquared) continue;
                float influence = Mathf.Pow(1f - Mathf.Sqrt(distanceSquared) / stroke.radius, stroke.falloff)
                    * (stroke.brushMask?.Sample(localToWorld.MultiplyVector(delta), stroke.radius) ?? 1f);
                m_SmoothAffected.Add(i);
                m_SmoothWeights.Add(Mathf.Clamp01(strength * influence));
                m_SmoothHeights.Add(0f);
            }
            if (m_SmoothAffected.Count == 0) return;
            if (m_Neighbours == null) BuildNeighbours(mesh);
            for (int pass = 0; pass < passes; pass++)
            {
                // Compute every result before writing any heights, preserving
                // the original simultaneous neighbour averaging without a full
                // mesh snapshot. Neighbours outside the brush remain unchanged.
                for (int i = 0; i < m_SmoothAffected.Count; i++)
                {
                    int index = m_SmoothAffected[i];
                    var neighbours = m_Neighbours[index];
                    float height = vertices[index].y;
                    if (neighbours.Count > 0)
                    {
                        float average = 0f;
                        for (int n = 0; n < neighbours.Count; n++) average += vertices[neighbours[n]].y;
                        height = Mathf.Lerp(height, average / neighbours.Count, m_SmoothWeights[i]);
                    }
                    m_SmoothHeights[i] = height;
                }
                for (int i = 0; i < m_SmoothAffected.Count; i++)
                {
                    int index = m_SmoothAffected[i];
                    Vector3 vertex = vertices[index];
                    vertex.y = m_SmoothHeights[i];
                    vertices[index] = vertex;
                }
            }
        }

        void ApplyStrokePass(Vector3[] vertices, Mesh mesh, Stroke stroke)
        {
            if (stroke == null || stroke.radius <= Mathf.Epsilon) return;
            if (stroke.mode == SculptMode.SeamFit || stroke.mode == SculptMode.MeshStamp)
            {
                if (stroke.seamVertexCount != vertices.Length || stroke.seamVertices == null) return;
                foreach (SeamVertex sample in stroke.seamVertices)
                    if ((uint)sample.index < (uint)vertices.Length)
                        vertices[sample.index] += sample.delta;
                return;
            }
            Transform targetTransform = m_Target.transform;
            MGTerrain terrain = m_Target.GetComponent<MGTerrain>()
                ?? m_Target.GetComponentInParent<MGTerrain>();
            if (terrain != null && terrain.HeightOnlySculpt)
            {
                ApplyHeightOnlyStroke(vertices, mesh, stroke, targetTransform);
                return;
            }
            Vector3 center = stroke.space == StrokeSpace.World ? stroke.position : targetTransform.TransformPoint(stroke.position);
            Vector3 direction = stroke.space == StrokeSpace.World ? stroke.direction.normalized : targetTransform.TransformDirection(stroke.direction).normalized;
            Vector3[] before = stroke.mode == SculptMode.Smooth ? CaptureSmoothSnapshot(vertices) : null;
            if (stroke.mode == SculptMode.Smooth && m_Neighbours == null) BuildNeighbours(mesh);

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = targetTransform.TransformPoint(vertices[i]);
                float distance = stroke.mode == SculptMode.SetHeight
                    ? new Vector2(world.x - center.x, world.z - center.z).magnitude : Vector3.Distance(world, center);
                if (distance >= stroke.radius) continue;
                float influence = Mathf.Pow(1f - distance / stroke.radius, stroke.falloff)
                    * (stroke.brushMask?.Sample(world - center, stroke.radius) ?? 1f);

                if (stroke.mode == SculptMode.SetHeight)
                    world.y = Mathf.Lerp(world.y, stroke.targetHeight, Mathf.Clamp01(Mathf.Abs(stroke.strength) * influence));
                else if (stroke.mode == SculptMode.Displace)
                    world += direction * (stroke.strength * influence);
                else if (stroke.mode == SculptMode.Noise)
                    world += direction * (SignedNoise(stroke.noiseSeed, i) * Mathf.Abs(stroke.strength) * influence);
                else if (stroke.mode == SculptMode.Flatten)
                    world -= direction * Vector3.Dot(world - center, direction) * Mathf.Clamp01(Mathf.Abs(stroke.strength) * influence);
                else if (m_Neighbours != null && m_Neighbours[i].Count > 0)
                {
                    Vector3 average = Vector3.zero;
                    for (int n = 0; n < m_Neighbours[i].Count; n++) average += targetTransform.TransformPoint(before[m_Neighbours[i][n]]);
                    average /= m_Neighbours[i].Count;
                    world = Vector3.Lerp(world, average, Mathf.Clamp01(Mathf.Abs(stroke.strength) * influence));
                }

                vertices[i] = targetTransform.InverseTransformPoint(world);
            }
        }

        Vector3[] CaptureSmoothSnapshot(Vector3[] vertices)
        {
            if (m_SmoothSnapshot == null || m_SmoothSnapshot.Length != vertices.Length)
                m_SmoothSnapshot = new Vector3[vertices.Length];
            Array.Copy(vertices, m_SmoothSnapshot, vertices.Length);
            return m_SmoothSnapshot;
        }

        void ApplyHeightOnlyStroke(Vector3[] vertices, Mesh mesh, Stroke stroke, Transform targetTransform)
        {
            Vector3 worldCenter = stroke.space == StrokeSpace.World
                ? stroke.position
                : targetTransform.TransformPoint(stroke.position);
            Vector3 localCenter = targetTransform.InverseTransformPoint(worldCenter);
            Vector3[] before = stroke.mode == SculptMode.Smooth ? CaptureSmoothSnapshot(vertices) : null;
            if (stroke.mode == SculptMode.Smooth && m_Neighbours == null)
                BuildNeighbours(mesh);

            Matrix4x4 localToWorld = targetTransform.localToWorldMatrix;
            Vector3 worldUp = localToWorld.MultiplyVector(Vector3.up);
            float radiusSquared = stroke.radius * stroke.radius;
            float worldUnitsPerLocalY = worldUp.magnitude;
            float localStrength = stroke.strength / Mathf.Max(0.00001f, worldUnitsPerLocalY);
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 planarDelta = vertices[index] - localCenter;
                planarDelta.y = 0f;
                float distanceSquared = localToWorld.MultiplyVector(planarDelta).sqrMagnitude;
                if (distanceSquared >= radiusSquared)
                    continue;
                float distance = Mathf.Sqrt(distanceSquared);
                float influence = Mathf.Pow(1f - distance / stroke.radius, stroke.falloff)
                    * (stroke.brushMask?.Sample(localToWorld.MultiplyVector(planarDelta), stroke.radius) ?? 1f);
                Vector3 vertex = vertices[index];

                if (stroke.mode == SculptMode.SetHeight)
                {
                    Vector3 world = localToWorld.MultiplyPoint3x4(vertex);
                    float yScale = worldUp.y;
                    if (Mathf.Abs(yScale) > 0.00001f)
                        vertex.y += (stroke.targetHeight - world.y) / yScale * Mathf.Clamp01(Mathf.Abs(stroke.strength) * influence);
                }
                else if (stroke.mode == SculptMode.Displace)
                    vertex.y += localStrength * influence;
                else if (stroke.mode == SculptMode.Noise)
                    vertex.y += SignedNoise(stroke.noiseSeed, index) * Mathf.Abs(localStrength) * influence;
                else if (stroke.mode == SculptMode.Flatten)
                    vertex.y = Mathf.Lerp(vertex.y, localCenter.y, Mathf.Clamp01(Mathf.Abs(stroke.strength) * influence));
                else if (m_Neighbours != null && m_Neighbours[index].Count > 0)
                {
                    float averageHeight = 0f;
                    for (int neighbour = 0; neighbour < m_Neighbours[index].Count; neighbour++)
                        averageHeight += before[m_Neighbours[index][neighbour]].y;
                    averageHeight /= m_Neighbours[index].Count;
                    vertex.y = Mathf.Lerp(vertex.y, averageHeight, Mathf.Clamp01(Mathf.Abs(stroke.strength) * influence));
                }

                // X/Z intentionally remain untouched: an MG Terrain is always a
                // height field even when the shared sculpt tool is used on it.
                vertices[index] = vertex;
            }
        }

        void ConformTerrainInstances()
        {
            if (m_Target == null)
                return;
            MGTerrain terrain = m_Target.GetComponent<MGTerrain>()
                ?? m_Target.GetComponentInParent<MGTerrain>();
            if (terrain != null)
                terrain.ConformInstancesToSurface();
        }

        public bool ApplySeamNormals(Mesh mesh)
        {
            if (mesh == null || m_Target == null) return false;
            Vector3[] normals = null;
            foreach (Stroke stroke in m_Strokes)
            {
                if (stroke == null || stroke.mode != SculptMode.SeamFit
                    || stroke.seamVertexCount != mesh.vertexCount || stroke.seamVertices == null) continue;
                foreach (SeamVertex sample in stroke.seamVertices)
                {
                    if (sample.normalWeight <= 0f || sample.normal.sqrMagnitude < 0.000001f
                        || (uint)sample.index >= (uint)mesh.vertexCount) continue;
                    normals ??= mesh.normals;
                    // Blend in world space so nonuniform terrain scale does not
                    // distort the interpolation. Normals use inverse transpose.
                    Vector3 current = m_Target.transform.worldToLocalMatrix.transpose.MultiplyVector(normals[sample.index]).normalized;
                    Vector3 desired = m_Target.transform.worldToLocalMatrix.transpose.MultiplyVector(sample.normal).normalized;
                    Vector3 blended = Vector3.Slerp(current, desired, Mathf.Clamp01(sample.normalWeight)).normalized;
                    normals[sample.index] = m_Target.transform.localToWorldMatrix.transpose.MultiplyVector(blended).normalized;
                }
            }
            if (normals != null) mesh.normals = normals;
            return normals != null;
        }

        void ConformTerrainInstancesForLatestStroke()
        {
            if (m_Target == null || (m_Strokes.Count == 0 && m_TerrainStroke == null))
                return;
            MGTerrain terrain = m_Target.GetComponent<MGTerrain>()
                ?? m_Target.GetComponentInParent<MGTerrain>();
            if (terrain == null)
                return;
            Stroke stroke = IsDirectTerrain ? m_TerrainStroke : m_Strokes[m_Strokes.Count - 1];
            if (stroke == null) return;
            Vector3 center = stroke.space == StrokeSpace.World
                ? stroke.position
                : m_Target.transform.TransformPoint(stroke.position);
            terrain.ConformInstancesToSurface(center, stroke.radius);
        }

        static float SignedNoise(int seed, int vertexIndex)
        {
            unchecked
            {
                uint value = (uint)(seed ^ (vertexIndex * 374761393));
                value = (value ^ (value >> 13)) * 1274126177u;
                value ^= value >> 16;
                return (value & 0x00FFFFFFu) / 8388607.5f - 1f;
            }
        }

        void BuildNeighbours(Mesh mesh)
        {
            m_Neighbours = new List<int>[mesh.vertexCount];
            for (int i = 0; i < m_Neighbours.Length; i++) m_Neighbours[i] = new List<int>(6);
            int[] triangles = mesh.triangles;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                AddNeighbour(triangles[i], triangles[i + 1]);
                AddNeighbour(triangles[i + 1], triangles[i + 2]);
                AddNeighbour(triangles[i + 2], triangles[i]);
            }
        }

        void AddNeighbour(int a, int b)
        {
            if (!m_Neighbours[a].Contains(b)) m_Neighbours[a].Add(b);
            if (!m_Neighbours[b].Contains(a)) m_Neighbours[b].Add(a);
        }

        void ClearBaseCache()
        {
            m_BaseVertices = null;
            m_BaseMesh = null;
            m_Neighbours = null;
        }
    }
}
