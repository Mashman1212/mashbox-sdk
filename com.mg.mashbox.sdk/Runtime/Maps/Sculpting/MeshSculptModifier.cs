using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.Spline;
using UnityEngine;

namespace MashBoxSDK.Maps.Sculpting
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class MeshSculptModifier : MonoBehaviour
    {
        public enum SculptMode { Displace, Smooth, Flatten, Noise }
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
            public int noiseSeed;
        }

        [SerializeField] MeshFilter m_Target;
        [SerializeField] MultiSplineLoft m_LinkedLoft;
        [SerializeField] bool m_UpdateMeshCollider = true;
        [SerializeField] List<Stroke> m_Strokes = new List<Stroke>();
        [SerializeField, HideInInspector] Mesh m_SourceMesh;
        [SerializeField, HideInInspector] Mesh m_OutputMesh;

        [NonSerialized] Vector3[] m_BaseVertices;
        [NonSerialized] Mesh m_BaseMesh;
        [NonSerialized] List<int>[] m_Neighbours;

        public MeshFilter Target => m_Target;
        public MultiSplineLoft LinkedLoft => m_LinkedLoft;
        public bool UpdateMeshCollider { get => m_UpdateMeshCollider; set => m_UpdateMeshCollider = value; }
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
            m_Strokes.Add(stroke);
        }

        public void RemoveLastStroke() { if (m_Strokes.Count > 0) m_Strokes.RemoveAt(m_Strokes.Count - 1); }
        public void ClearStrokes() { m_Strokes.Clear(); }

        public void Rebuild()
        {
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
            if (m_Strokes.Count == 0 || m_Target == null)
                return;

            Mesh mesh = m_LinkedLoft != null ? m_LinkedLoft.GeneratedMesh : m_OutputMesh;
            if (mesh == null || m_BaseMesh != mesh || m_BaseVertices == null)
            {
                Rebuild();
                return;
            }

            Vector3[] vertices = mesh.vertices;
            ApplyStroke(vertices, mesh, m_Strokes[m_Strokes.Count - 1]);
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent)) mesh.RecalculateTangents();
            mesh.UploadMeshData(false);
        }

        // Expensive derived data is refreshed once after a drag, rather than for
        // every sampled brush position.
        public void FinalizeStrokePreview()
        {
            if (m_Target == null)
                return;

            if (m_UpdateMeshCollider && m_LinkedLoft != null)
                m_LinkedLoft.RebuildColliderChunks();
            else if (m_UpdateMeshCollider && m_Target.TryGetComponent(out MeshCollider collider))
            {
                collider.sharedMesh = null;
                collider.sharedMesh = m_Target.sharedMesh;
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
            if (mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent)) mesh.RecalculateTangents();
            mesh.UploadMeshData(false);

            if (m_UpdateMeshCollider && updateCollider && m_LinkedLoft != null)
            {
                m_LinkedLoft.RebuildColliderChunks();
            }
            else if (m_UpdateMeshCollider && updateCollider && m_Target.TryGetComponent(out MeshCollider collider))
            {
                collider.sharedMesh = null;
                collider.sharedMesh = mesh;
            }
        }

        void ApplyStroke(Vector3[] vertices, Mesh mesh, Stroke stroke)
        {
            if (stroke == null || stroke.radius <= Mathf.Epsilon) return;
            Transform targetTransform = m_Target.transform;
            Vector3 center = stroke.space == StrokeSpace.World ? stroke.position : targetTransform.TransformPoint(stroke.position);
            Vector3 direction = stroke.space == StrokeSpace.World ? stroke.direction.normalized : targetTransform.TransformDirection(stroke.direction).normalized;
            Vector3[] before = stroke.mode == SculptMode.Smooth ? (Vector3[])vertices.Clone() : null;
            if (stroke.mode == SculptMode.Smooth && m_Neighbours == null) BuildNeighbours(mesh);

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = targetTransform.TransformPoint(vertices[i]);
                float distance = Vector3.Distance(world, center);
                if (distance >= stroke.radius) continue;
                float influence = Mathf.Pow(1f - distance / stroke.radius, stroke.falloff);

                if (stroke.mode == SculptMode.Displace)
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
