using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.Spline;
using UnityEngine;

namespace MashBoxSDK.Maps.Painting
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class VertexPaintModifier : MonoBehaviour
    {
        [Serializable]
        public sealed class Stroke
        {
            public bool fill;
            public Vector3 localPosition;
            public bool hasTopologyAnchor;
            public int anchorVertexIndex = -1;
            public int anchorVertexCount;
            public Vector3 localAnchorOffset;
            public bool hasUvAnchor;
            public Vector2 uvAnchor;
            public Color color = Color.white;
            [Min(0.001f)] public float radius = 1f;
            [Range(0f, 1f)] public float strength = 0.5f;
            public bool useFalloff = true;
        }

        [SerializeField] MeshFilter m_Target;
        [SerializeField] MultiSplineLoft m_LinkedLoft;
        [SerializeField] List<Stroke> m_Strokes = new List<Stroke>();

        [NonSerialized] Mesh m_BaseMesh;
        [NonSerialized] Color[] m_BaseColors;

        public MeshFilter Target => m_Target;
        public MultiSplineLoft LinkedLoft => m_LinkedLoft;
        public List<Stroke> Strokes => m_Strokes;
        public int StrokeCount => m_Strokes.Count;

        void OnEnable()
        {
            MultiSplineLoft loft = m_LinkedLoft != null ? m_LinkedLoft : GetComponent<MultiSplineLoft>();
            if (loft == null)
                return;

            if (m_LinkedLoft != loft || m_Target != loft.GetComponent<MeshFilter>())
                LinkToLoft(loft);
            loft.VertexPaintModifier = this;
            loft.QueueRegenerate();
        }

        public void LinkToLoft(MultiSplineLoft loft)
        {
            m_LinkedLoft = loft;
            m_Target = loft != null ? loft.GetComponent<MeshFilter>() : null;
            ClearBaseCache();
        }

        public Stroke CreateStroke(Mesh mesh, Vector3 worldPosition, Color color, float radius, float strength, bool useFalloff)
        {
            Transform targetTransform = m_Target != null ? m_Target.transform : transform;
            var stroke = new Stroke
            {
                localPosition = targetTransform.InverseTransformPoint(worldPosition),
                color = color,
                radius = Mathf.Max(0.001f, radius),
                strength = Mathf.Clamp01(strength),
                useFalloff = useFalloff
            };

            if (TryFindClosestAnchor(mesh, worldPosition, targetTransform, out int vertexIndex, out Vector2 uvAnchor, out Vector3 localVertex))
            {
                stroke.hasTopologyAnchor = true;
                stroke.anchorVertexIndex = vertexIndex;
                stroke.anchorVertexCount = mesh.vertexCount;
                stroke.localAnchorOffset = stroke.localPosition - localVertex;
                stroke.hasUvAnchor = true;
                stroke.uvAnchor = uvAnchor;
            }

            return stroke;
        }

        public Stroke CreateFill(Color color)
        {
            return new Stroke
            {
                fill = true,
                color = color,
                strength = 1f,
                useFalloff = false
            };
        }

        public void AddStroke(Stroke stroke)
        {
            if (stroke == null) return;
            stroke.radius = Mathf.Max(0.001f, stroke.radius);
            stroke.strength = Mathf.Clamp01(stroke.strength);
            m_Strokes.Add(stroke);
        }

        public void AddStrokeAndApply(Stroke stroke)
        {
            AddStroke(stroke);
            if (stroke == null || m_LinkedLoft == null || m_Target == null)
                return;

            Mesh generatedMesh = m_LinkedLoft.GeneratedMesh;
            if (generatedMesh == null
                || m_BaseMesh != generatedMesh
                || m_BaseColors == null
                || m_BaseColors.Length != generatedMesh.vertexCount)
            {
                Rebuild();
                return;
            }

            Vector3? worldCenter = null;
            if (!stroke.fill)
            {
                Vector3[] vertices = generatedMesh.vertices;
                worldCenter = TryResolveTopologyAnchor(vertices, stroke, m_Target.transform, out Vector3 topologyCenter)
                    ? topologyCenter
                    : m_Target.transform.TransformPoint(stroke.localPosition);
            }

            ApplySingleStroke(generatedMesh, stroke, worldCenter);

            Mesh displayedMesh = m_Target.sharedMesh;
            if (displayedMesh != null && displayedMesh != generatedMesh)
                ApplySingleStroke(displayedMesh, stroke, worldCenter);
        }

        public void RemoveLastStroke()
        {
            if (m_Strokes.Count > 0)
                m_Strokes.RemoveAt(m_Strokes.Count - 1);
        }

        public void ClearStrokes()
        {
            m_Strokes.Clear();
        }

        public void Rebuild()
        {
            if (m_LinkedLoft == null || m_Target == null)
                return;

            Mesh generatedMesh = m_LinkedLoft.GeneratedMesh;
            if (generatedMesh == null || m_BaseMesh != generatedMesh || m_BaseColors == null || m_BaseColors.Length != generatedMesh.vertexCount)
            {
                m_LinkedLoft.Regenerate();
                return;
            }

            ApplyFromCachedBase(generatedMesh);
            RefreshUvOutput();
        }

        public void ApplyToFreshMesh(Mesh mesh)
        {
            if (mesh == null || m_Target == null)
                return;

            m_BaseMesh = mesh;
            Color[] sourceColors = mesh.colors;
            m_BaseColors = sourceColors.Length == mesh.vertexCount
                ? (Color[])sourceColors.Clone()
                : CreateWhiteColors(mesh.vertexCount);
            ApplyFromCachedBase(mesh);
        }

        void ApplyFromCachedBase(Mesh mesh)
        {
            if (mesh == null || m_BaseColors == null || m_BaseColors.Length != mesh.vertexCount || m_Target == null)
                return;

            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            Color[] colors = (Color[])m_BaseColors.Clone();
            Transform targetTransform = m_Target.transform;

            for (int strokeIndex = 0; strokeIndex < m_Strokes.Count; strokeIndex++)
            {
                Stroke stroke = m_Strokes[strokeIndex];
                if (stroke == null)
                    continue;

                if (stroke.fill)
                {
                    for (int vertexIndex = 0; vertexIndex < colors.Length; vertexIndex++)
                        colors[vertexIndex] = stroke.color;
                    continue;
                }

                if ((!stroke.hasTopologyAnchor
                    || stroke.anchorVertexCount != vertices.Length
                    || stroke.anchorVertexIndex < 0
                    || stroke.anchorVertexIndex >= vertices.Length)
                    && TryFindClosestAnchor(
                        mesh,
                        targetTransform.TransformPoint(stroke.localPosition),
                        targetTransform,
                        out int migratedVertexIndex,
                        out Vector2 migratedUvAnchor,
                        out Vector3 migratedLocalVertex))
                {
                    stroke.hasTopologyAnchor = true;
                    stroke.anchorVertexIndex = migratedVertexIndex;
                    stroke.anchorVertexCount = vertices.Length;
                    Vector3 candidateOffset = stroke.localPosition - migratedLocalVertex;
                    float worldOffset = targetTransform.TransformVector(candidateOffset).magnitude;
                    // Preserve the exact hit point when it is still on this surface.
                    // If the loft moved farther than the brush radius, snap the old
                    // stroke back onto the nearest current surface position instead.
                    stroke.localAnchorOffset = worldOffset < stroke.radius
                        ? candidateOffset
                        : Vector3.zero;
                    stroke.hasUvAnchor = true;
                    stroke.uvAnchor = migratedUvAnchor;
                }

                Vector3 center = TryResolveTopologyAnchor(vertices, stroke, targetTransform, out Vector3 topologyCenter)
                    ? topologyCenter
                    : TryResolveUvAnchor(uvs, vertices, stroke, targetTransform, out Vector3 anchoredCenter)
                    ? anchoredCenter
                    : targetTransform.TransformPoint(stroke.localPosition);
                for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                {
                    Vector3 worldVertex = targetTransform.TransformPoint(vertices[vertexIndex]);
                    float distance = Vector3.Distance(worldVertex, center);
                    if (distance >= stroke.radius)
                        continue;

                    float falloff = stroke.useFalloff ? Mathf.Clamp01(1f - distance / stroke.radius) : 1f;
                    colors[vertexIndex] = Color.Lerp(colors[vertexIndex], stroke.color, stroke.strength * falloff);
                }
            }

            mesh.colors = colors;
            mesh.UploadMeshData(false);
        }

        void ApplySingleStroke(Mesh mesh, Stroke stroke, Vector3? worldCenter)
        {
            if (mesh == null || stroke == null || m_Target == null)
                return;

            Vector3[] vertices = mesh.vertices;
            Color[] colors = mesh.colors;
            if (colors.Length != vertices.Length)
                colors = CreateWhiteColors(vertices.Length);

            if (stroke.fill)
            {
                for (int i = 0; i < colors.Length; i++)
                    colors[i] = stroke.color;
            }
            else if (worldCenter.HasValue)
            {
                Transform targetTransform = m_Target.transform;
                Vector3 center = worldCenter.Value;
                for (int i = 0; i < vertices.Length; i++)
                {
                    float distance = Vector3.Distance(targetTransform.TransformPoint(vertices[i]), center);
                    if (distance >= stroke.radius)
                        continue;

                    float falloff = stroke.useFalloff ? Mathf.Clamp01(1f - distance / stroke.radius) : 1f;
                    colors[i] = Color.Lerp(colors[i], stroke.color, stroke.strength * falloff);
                }
            }

            mesh.colors = colors;
            mesh.UploadMeshData(false);
        }

        static bool TryFindClosestAnchor(
            Mesh mesh,
            Vector3 worldPosition,
            Transform targetTransform,
            out int vertexIndex,
            out Vector2 uvAnchor,
            out Vector3 localVertex)
        {
            vertexIndex = -1;
            uvAnchor = default;
            localVertex = default;
            if (mesh == null || targetTransform == null)
                return false;

            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            if (uvs.Length != vertices.Length || vertices.Length == 0)
                return false;

            int closestIndex = 0;
            float closestDistance = float.MaxValue;
            for (int i = 0; i < vertices.Length; i++)
            {
                float distance = (targetTransform.TransformPoint(vertices[i]) - worldPosition).sqrMagnitude;
                if (distance >= closestDistance)
                    continue;

                closestDistance = distance;
                closestIndex = i;
            }

            vertexIndex = closestIndex;
            uvAnchor = uvs[closestIndex];
            localVertex = vertices[closestIndex];
            return true;
        }

        static bool TryResolveTopologyAnchor(Vector3[] vertices, Stroke stroke, Transform targetTransform, out Vector3 center)
        {
            center = default;
            if (!stroke.hasTopologyAnchor
                || targetTransform == null
                || stroke.anchorVertexCount != vertices.Length
                || stroke.anchorVertexIndex < 0
                || stroke.anchorVertexIndex >= vertices.Length)
            {
                return false;
            }

            center = targetTransform.TransformPoint(vertices[stroke.anchorVertexIndex] + stroke.localAnchorOffset);
            return true;
        }

        static bool TryResolveUvAnchor(Vector2[] uvs, Vector3[] vertices, Stroke stroke, Transform targetTransform, out Vector3 center)
        {
            center = default;
            if (!stroke.hasUvAnchor || targetTransform == null)
                return false;

            if (uvs.Length != vertices.Length || vertices.Length == 0)
                return false;

            int closestIndex = 0;
            float closestDistance = float.MaxValue;
            for (int i = 0; i < uvs.Length; i++)
            {
                float distance = (uvs[i] - stroke.uvAnchor).sqrMagnitude;
                if (distance >= closestDistance)
                    continue;

                closestDistance = distance;
                closestIndex = i;
            }

            center = targetTransform.TransformPoint(vertices[closestIndex] + stroke.localAnchorOffset);
            return true;
        }

        void RefreshUvOutput()
        {
            UVSpline uvSpline = m_LinkedLoft != null ? m_LinkedLoft.GeneratedUvSpline : null;
            if (uvSpline == null || (!m_LinkedLoft.GenerateUvSplineWithLoft && uvSpline.OutputMesh == null))
                return;

            m_Target.sharedMesh = m_LinkedLoft.GeneratedMesh;
            uvSpline.RebuildOutputMesh();
        }

        static Color[] CreateWhiteColors(int count)
        {
            var colors = new Color[count];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = Color.white;
            return colors;
        }

        void ClearBaseCache()
        {
            m_BaseMesh = null;
            m_BaseColors = null;
        }
    }
}
