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
        [NonSerialized] Mesh m_PrimarySpatialMesh;
        [NonSerialized] WorldVertexGrid m_PrimarySpatialGrid;
        [NonSerialized] Mesh m_SecondarySpatialMesh;
        [NonSerialized] WorldVertexGrid m_SecondarySpatialGrid;

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
            radius = Mathf.Max(0.001f, radius);
            var stroke = new Stroke
            {
                localPosition = targetTransform.InverseTransformPoint(worldPosition),
                color = color,
                radius = radius,
                strength = Mathf.Clamp01(strength),
                useFalloff = useFalloff
            };

            WorldVertexGrid spatialGrid = GetSpatialGrid(mesh, targetTransform, radius);
            if (spatialGrid != null
                && spatialGrid.TryFindClosest(worldPosition, radius, out int vertexIndex))
            {
                Vector3[] vertices = spatialGrid.LocalVertices;
                Vector2[] uvs = mesh.uv;
                stroke.hasTopologyAnchor = true;
                stroke.anchorVertexIndex = vertexIndex;
                stroke.anchorVertexCount = mesh.vertexCount;
                stroke.localAnchorOffset = stroke.localPosition - vertices[vertexIndex];
                if (uvs.Length == vertices.Length)
                {
                    stroke.hasUvAnchor = true;
                    stroke.uvAnchor = uvs[vertexIndex];
                }
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
            if (stroke == null)
                return;

            if (m_LinkedLoft == null || m_Target == null)
            {
                AddStroke(stroke);
                return;
            }

            Mesh generatedMesh = m_LinkedLoft.GeneratedMesh;
            if (generatedMesh == null
                || m_BaseMesh != generatedMesh
                || m_BaseColors == null
                || m_BaseColors.Length != generatedMesh.vertexCount)
            {
                // Restore the existing history first. Adding the new dab before
                // regeneration used to replay the entire history again and made
                // a large modifier appear to stop accepting paint.
                m_LinkedLoft.Regenerate();
                generatedMesh = m_LinkedLoft.GeneratedMesh;
            }

            AddStroke(stroke);
            if (generatedMesh == null
                || m_BaseMesh != generatedMesh
                || m_BaseColors == null
                || m_BaseColors.Length != generatedMesh.vertexCount)
                return;

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
            m_PrimarySpatialMesh = null;
            m_PrimarySpatialGrid = null;
            m_SecondarySpatialMesh = null;
            m_SecondarySpatialGrid = null;
            // Shoulder generation can author deterministic soil/rock/grass masks
            // into vertex colors. Preserve those as the replay base; ordinary
            // lofts still have no color channel after Mesh.Clear and use white.
            Color[] generatedColors = mesh.colors;
            m_BaseColors = generatedColors != null && generatedColors.Length == mesh.vertexCount
                ? generatedColors
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
            WorldVertexGrid spatialGrid = GetSpatialGrid(mesh, targetTransform, GetReplayCellSize());

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
                    && spatialGrid != null
                    && spatialGrid.TryFindClosest(
                        targetTransform.TransformPoint(stroke.localPosition),
                        stroke.radius,
                        out int migratedVertexIndex))
                {
                    stroke.hasTopologyAnchor = true;
                    stroke.anchorVertexIndex = migratedVertexIndex;
                    stroke.anchorVertexCount = vertices.Length;
                    Vector3 candidateOffset = stroke.localPosition - vertices[migratedVertexIndex];
                    float worldOffset = targetTransform.TransformVector(candidateOffset).magnitude;
                    // Preserve the exact hit point when it is still on this surface.
                    // If the loft moved farther than the brush radius, snap the old
                    // stroke back onto the nearest current surface position instead.
                    stroke.localAnchorOffset = worldOffset < stroke.radius
                        ? candidateOffset
                        : Vector3.zero;
                    if (uvs.Length == vertices.Length)
                    {
                        stroke.hasUvAnchor = true;
                        stroke.uvAnchor = uvs[migratedVertexIndex];
                    }
                }

                Vector3 center = TryResolveTopologyAnchor(vertices, stroke, targetTransform, out Vector3 topologyCenter)
                    ? topologyCenter
                    : TryResolveUvAnchor(uvs, vertices, stroke, targetTransform, out Vector3 anchoredCenter)
                    ? anchoredCenter
                    : targetTransform.TransformPoint(stroke.localPosition);
                spatialGrid?.ApplyStroke(colors, stroke, center);
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
                GetSpatialGrid(mesh, targetTransform, stroke.radius)?.ApplyStroke(colors, stroke, worldCenter.Value);
            }

            mesh.colors = colors;
            mesh.UploadMeshData(false);
        }

        float GetReplayCellSize()
        {
            for (int i = m_Strokes.Count - 1; i >= 0; i--)
            {
                Stroke stroke = m_Strokes[i];
                if (stroke != null && !stroke.fill && stroke.radius > 0.001f)
                    return stroke.radius;
            }

            return 1f;
        }

        WorldVertexGrid GetSpatialGrid(Mesh mesh, Transform targetTransform, float preferredCellSize)
        {
            if (mesh == null || targetTransform == null || mesh.vertexCount == 0)
                return null;

            preferredCellSize = Mathf.Clamp(preferredCellSize, 0.05f, 25f);
            Matrix4x4 localToWorld = targetTransform.localToWorldMatrix;
            if (mesh == m_BaseMesh)
            {
                if (m_PrimarySpatialGrid == null
                    || m_PrimarySpatialMesh != mesh
                    || !m_PrimarySpatialGrid.Matches(localToWorld, preferredCellSize))
                {
                    m_PrimarySpatialMesh = mesh;
                    m_PrimarySpatialGrid = new WorldVertexGrid(mesh.vertices, localToWorld, preferredCellSize);
                }
                return m_PrimarySpatialGrid;
            }

            if (m_SecondarySpatialGrid == null
                || m_SecondarySpatialMesh != mesh
                || !m_SecondarySpatialGrid.Matches(localToWorld, preferredCellSize))
            {
                m_SecondarySpatialMesh = mesh;
                m_SecondarySpatialGrid = new WorldVertexGrid(mesh.vertices, localToWorld, preferredCellSize);
            }
            return m_SecondarySpatialGrid;
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
            uvSpline.RebuildOutputMesh(forceSourceRefresh: true);
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
            m_PrimarySpatialMesh = null;
            m_PrimarySpatialGrid = null;
            m_SecondarySpatialMesh = null;
            m_SecondarySpatialGrid = null;
        }

        sealed class WorldVertexGrid
        {
            readonly float m_CellSize;
            readonly Matrix4x4 m_LocalToWorld;
            readonly Vector3[] m_LocalVertices;
            readonly Vector3[] m_WorldVertices;
            readonly Dictionary<Vector3Int, List<int>> m_Cells = new Dictionary<Vector3Int, List<int>>();

            public Vector3[] LocalVertices => m_LocalVertices;

            public WorldVertexGrid(Vector3[] localVertices, Matrix4x4 localToWorld, float cellSize)
            {
                m_CellSize = cellSize;
                m_LocalToWorld = localToWorld;
                m_LocalVertices = localVertices;
                m_WorldVertices = new Vector3[localVertices.Length];
                for (int i = 0; i < localVertices.Length; i++)
                {
                    Vector3 worldVertex = localToWorld.MultiplyPoint3x4(localVertices[i]);
                    m_WorldVertices[i] = worldVertex;
                    Vector3Int key = GetCell(worldVertex);
                    if (!m_Cells.TryGetValue(key, out List<int> indices))
                    {
                        indices = new List<int>();
                        m_Cells.Add(key, indices);
                    }
                    indices.Add(i);
                }
            }

            public bool Matches(Matrix4x4 localToWorld, float cellSize)
            {
                return m_LocalToWorld == localToWorld
                    && Mathf.Abs(m_CellSize - cellSize) <= Mathf.Max(0.01f, m_CellSize * 0.25f);
            }

            public bool TryFindClosest(Vector3 worldPosition, float searchRadius, out int closestIndex)
            {
                int foundIndex = -1;
                float closestDistance = float.MaxValue;
                VisitCells(worldPosition, Mathf.Max(searchRadius, m_CellSize), index =>
                {
                    float distance = (m_WorldVertices[index] - worldPosition).sqrMagnitude;
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        foundIndex = index;
                    }
                });

                if (foundIndex >= 0)
                {
                    closestIndex = foundIndex;
                    return true;
                }

                for (int i = 0; i < m_WorldVertices.Length; i++)
                {
                    float distance = (m_WorldVertices[i] - worldPosition).sqrMagnitude;
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        foundIndex = i;
                    }
                }
                closestIndex = foundIndex;
                return foundIndex >= 0;
            }

            public void ApplyStroke(Color[] colors, Stroke stroke, Vector3 worldCenter)
            {
                if (colors == null || stroke == null || stroke.radius <= Mathf.Epsilon)
                    return;

                float radiusSquared = stroke.radius * stroke.radius;
                VisitCells(worldCenter, stroke.radius, index =>
                {
                    if (index < 0 || index >= colors.Length)
                        return;

                    float distanceSquared = (m_WorldVertices[index] - worldCenter).sqrMagnitude;
                    if (distanceSquared >= radiusSquared)
                        return;

                    float falloff = stroke.useFalloff
                        ? Mathf.Clamp01(1f - Mathf.Sqrt(distanceSquared) / stroke.radius)
                        : 1f;
                    colors[index] = Color.Lerp(colors[index], stroke.color, stroke.strength * falloff);
                });
            }

            void VisitCells(Vector3 center, float radius, Action<int> visitor)
            {
                Vector3Int minimum = GetCell(center - Vector3.one * radius);
                Vector3Int maximum = GetCell(center + Vector3.one * radius);
                for (int x = minimum.x; x <= maximum.x; x++)
                {
                    for (int y = minimum.y; y <= maximum.y; y++)
                    {
                        for (int z = minimum.z; z <= maximum.z; z++)
                        {
                            if (!m_Cells.TryGetValue(new Vector3Int(x, y, z), out List<int> indices))
                                continue;
                            for (int i = 0; i < indices.Count; i++)
                                visitor(indices[i]);
                        }
                    }
                }
            }

            Vector3Int GetCell(Vector3 worldPosition)
            {
                return new Vector3Int(
                    Mathf.FloorToInt(worldPosition.x / m_CellSize),
                    Mathf.FloorToInt(worldPosition.y / m_CellSize),
                    Mathf.FloorToInt(worldPosition.z / m_CellSize));
            }
        }
    }
}
