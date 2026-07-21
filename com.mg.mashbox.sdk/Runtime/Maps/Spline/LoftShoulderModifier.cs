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
        }

        [SerializeField] MultiSplineLoft m_Loft;
        [SerializeField] ShoulderProfile m_Left = new ShoulderProfile();
        [SerializeField] ShoulderProfile m_Right = new ShoulderProfile();
        [SerializeField] ShoulderProfile m_Start = new ShoulderProfile();
        [SerializeField] ShoulderProfile m_Finish = new ShoulderProfile();

        sealed class GeneratedShoulder
        {
            public Mesh mesh;
            public Material material;
            public bool generateCollider;
        }

        public MultiSplineLoft Loft { get => m_Loft; set => m_Loft = value; }
        public ShoulderProfile Left => m_Left;
        public ShoulderProfile Right => m_Right;
        public ShoulderProfile Start => m_Start;
        public ShoulderProfile Finish => m_Finish;

        void Reset()
        {
            m_Loft = GetComponentInParent<MultiSplineLoft>();
        }

        void OnValidate()
        {
            m_Left = Sanitize(m_Left);
            m_Right = Sanitize(m_Right);
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
            m_Loft?.SetShoulderColliderSubmeshes(null);
        }

        void BuildEdge(Transform root, Edge edge, ShoulderProfile profile, List<GeneratedShoulder> generatedShoulders)
        {
            if (!profile.enabled)
                return;

            var boundary = new List<Vector3>();
            var inner = new List<Vector3>();
            var pathDistances = new List<float>();
            if (!m_Loft.TryGetShoulderEdge(edge, boundary, inner, pathDistances) || boundary.Count < 2)
                return;

            List<Vector3> generatedOuterEdge = BuildGeneratedOuterEdge(edge, profile, boundary, inner);
            LoftShoulderEdgeSpline edgeSpline = EnsureEdgeSpline(root, edge);
            if (m_Loft.TryGetShoulderSourceKnotCount(edge, out int sourceKnotCount))
                edgeSpline.GeneratedPointCount = sourceKnotCount;
            edgeSpline.RefreshGeneratedPath(generatedOuterEdge, m_Loft);
            Mesh mesh = BuildShoulderMesh(edge, profile, boundary, inner, pathDistances, edgeSpline);
            generatedShoulders.Add(new GeneratedShoulder
            {
                mesh = mesh,
                material = profile.materialOverride,
                generateCollider = profile.generateCollider
            });
        }

        void CombineWithLoftMesh(IReadOnlyList<GeneratedShoulder> shoulders)
        {
            Mesh loftMesh = m_Loft.GeneratedMesh;
            MeshRenderer loftRenderer = m_Loft.GetComponent<MeshRenderer>();
            Material[] currentMaterials = loftRenderer.sharedMaterials;
            Material baseMaterial = currentMaterials.Length > 0 ? currentMaterials[0] : null;
            if (loftMesh == null || shoulders == null || shoulders.Count == 0)
            {
                loftRenderer.sharedMaterials = new[] { baseMaterial };
                m_Loft.SetShoulderColliderSubmeshes(null);
                return;
            }

            Mesh baseMesh = Instantiate(loftMesh);
            baseMesh.hideFlags = HideFlags.DontSave;
            var combine = new CombineInstance[shoulders.Count + 1];
            combine[0] = new CombineInstance { mesh = baseMesh, subMeshIndex = 0, transform = Matrix4x4.identity };
            var materials = new Material[combine.Length];
            materials[0] = baseMaterial;
            var colliderSubmeshes = new List<int>();
            int totalVertexCount = baseMesh.vertexCount;

            for (int index = 0; index < shoulders.Count; index++)
            {
                GeneratedShoulder shoulder = shoulders[index];
                combine[index + 1] = new CombineInstance { mesh = shoulder.mesh, subMeshIndex = 0, transform = Matrix4x4.identity };
                materials[index + 1] = shoulder.material != null ? shoulder.material : baseMaterial;
                totalVertexCount += shoulder.mesh.vertexCount;
                if (shoulder.generateCollider)
                    colliderSubmeshes.Add(index + 1);
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

        Mesh BuildShoulderMesh(
            Edge edge,
            ShoulderProfile profile,
            IReadOnlyList<Vector3> boundary,
            IReadOnlyList<Vector3> inner,
            IReadOnlyList<float> pathDistances,
            LoftShoulderEdgeSpline edgeSpline)
        {
            int profileSegments = Mathf.Max(1, profile.segments);
            int profilePoints = profileSegments + 1;
            int pathCount = boundary.Count;
            var vertices = new List<Vector3>(pathCount * profilePoints);
            var uvs = new List<Vector2>(pathCount * profilePoints);
            var triangles = new List<int>((pathCount - 1) * profileSegments * 6);
            Vector3 localUp = m_Loft.transform.InverseTransformDirection(Vector3.up).normalized;
            if (localUp.sqrMagnitude <= Mathf.Epsilon)
                localUp = Vector3.up;

            for (int path = 0; path < pathCount; path++)
            {
                Vector3 outward = boundary[path] - inner[Mathf.Min(path, inner.Count - 1)];
                if (outward.sqrMagnitude <= 0.000001f)
                    outward = GetFallbackOutward(edge);
                outward.Normalize();

                float pathDistance = path < pathDistances.Count ? pathDistances[path] : path;
                float pathT = pathCount > 1 ? path / (float)(pathCount - 1) : 0f;
                Vector3 bendOffset = Vector3.zero;
                edgeSpline?.TryEvaluateOffset(pathT, m_Loft, out bendOffset);
                for (int across = 0; across < profilePoints; across++)
                {
                    float t = across / (float)profileSegments;
                    float height = profile.height != null ? profile.height.Evaluate(t) * profile.verticalScale : 0f;
                    float bendInfluence = edgeSpline != null ? edgeSpline.EvaluateInfluence(t) : 0f;
                    vertices.Add(boundary[path] + outward * (profile.width * t) + localUp * height + bendOffset * bendInfluence);
                    uvs.Add(new Vector2(t, pathDistance));
                }
            }

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
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        List<Vector3> BuildGeneratedOuterEdge(
            Edge edge,
            ShoulderProfile profile,
            IReadOnlyList<Vector3> boundary,
            IReadOnlyList<Vector3> inner)
        {
            var outerEdge = new List<Vector3>(boundary.Count);
            Vector3 localUp = m_Loft.transform.InverseTransformDirection(Vector3.up).normalized;
            if (localUp.sqrMagnitude <= Mathf.Epsilon)
                localUp = Vector3.up;
            float outerHeight = profile.height != null ? profile.height.Evaluate(1f) * profile.verticalScale : 0f;

            for (int path = 0; path < boundary.Count; path++)
            {
                Vector3 outward = boundary[path] - inner[Mathf.Min(path, inner.Count - 1)];
                if (outward.sqrMagnitude <= 0.000001f)
                    outward = GetFallbackOutward(edge);
                outward.Normalize();
                outerEdge.Add(boundary[path] + outward * profile.width + localUp * outerHeight);
            }

            return outerEdge;
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
            profile.height ??= AnimationCurve.Linear(0f, 0f, 1f, 0f);
            return profile;
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
