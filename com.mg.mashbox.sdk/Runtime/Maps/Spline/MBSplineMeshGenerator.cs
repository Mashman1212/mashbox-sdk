using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace MashBoxSDK.Maps.Spline
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class MBSplineMeshGenerator : MonoBehaviour
    {
        public enum SplineMeshStyle
        {
            Flat,
            Angle90Inside,
            Angle90Outside,
            Vertical
        }
        public enum MaterialT
        {
            Custom,
            Checker,
            Dirt01,
        }

        public MaterialT _materialType = MaterialT.Checker; 
        

        [Header("Spline")]
        public SplineContainer container;
        
        Material _currentMaterial;
        
        [Header("Shape")]
        public SplineMeshStyle style = SplineMeshStyle.Angle90Inside;
        public float width = .1f;
        public float height = .1f;

        [Header("Offset")]
        public float verticalOffset = 0.001f;
        public float lateralOffset = -0.001f;
        public float forwardOffset = 0f;
        public float cornerOffset = 0.01f;
        
        [Header("UV")]
        public Vector2 tiling = Vector2.one;
        [Range(0.0f,1.0f)] public float _UVShellStretch = 0.0f;
        
        Mesh mesh;

        bool isDirty = true;
        UnityEngine.Splines.Spline splineCache;

        List<Vector3> vertices = new();
        List<int> triangles = new();
        List<Vector2> uvs = new();

        Vector2[] profileCache;
        SplineMeshStyle lastStyle;
        float lastWidth, lastHeight;
        List<int> ringStarts = new();
        void OnEnable()
        {
            EnsureMesh();
            UnityEngine.Splines.Spline.Changed += OnSplineChanged;
            isDirty = true;
            SetMaterial();
            UpdateProfileIfNeeded();
            Generate();
        }

        void SetMaterial()
        {
            if (_materialType == MaterialT.Checker)
            {
                _currentMaterial = Resources.Load<Material>("DefaultSplineMesh_Mat");
            }
            else if (_materialType == MaterialT.Dirt01)
            {
                _currentMaterial = Resources.Load<Material>("MB_DirtTrim_01_Mat");
            }
            else if (_materialType == MaterialT.Custom)
            {
                _currentMaterial = GetComponent<MeshRenderer>().sharedMaterial;
            }
            GetComponent<MeshRenderer>().sharedMaterial = _currentMaterial;
        }

        void OnDisable()
        {
            UnityEngine.Splines.Spline.Changed -= OnSplineChanged;
        }

        void OnValidate()
        {
            isDirty = true;

            SetMaterial();
        }

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying)
                Generate();
        }

        void OnSplineChanged(UnityEngine.Splines.Spline spline, int knotIndex, SplineModification modification)
        {
            if (container == null) return;
            if (spline != container.Spline) return;

            isDirty = true;
        }
#if UNITY_EDITOR
        void Update()
        {
            if (container == null) return;

            if (splineCache != container.Spline)
            {
                splineCache = container.Spline;
                isDirty = true;
            }

            UpdateProfileIfNeeded();

            if (isDirty)
            {
                Generate();
                isDirty = false;
            }
        }
#endif
        
        void EnsureMesh()
        {
            if (mesh == null)
            {
                mesh = new Mesh
                {
                    name = "SplineMesh"
                };

                mesh.MarkDynamic();
                GetComponent<MeshFilter>().sharedMesh = mesh;
            }
            else
            {
                mesh.hideFlags = HideFlags.None;
            }
            
        }

        void UpdateProfileIfNeeded()
        {
            if (profileCache == null ||
                lastStyle != style ||
                lastWidth != width ||
                lastHeight != height)
            {
                profileCache = GetProfile(style, width, height);

                lastStyle = style;
                lastWidth = width;
                lastHeight = height;

                isDirty = true;
            }
        }

        void Generate()
        {
            if (container == null)
                container = GetComponent<SplineContainer>();

            if (container == null || profileCache == null)
                return;

            EnsureMesh();

            vertices.Clear();
            triangles.Clear();
            uvs.Clear();

            var spline = container.Spline;
            int knotCount = spline.Count;
            bool isClosed = spline.Closed;

            if (knotCount < 2)
                return;

            int vertsPerRing = profileCache.Length;

            float uAccum = 0f;

            Vector3[] points = new Vector3[knotCount];
            Vector3[] forwards = new Vector3[knotCount];

            // 🔥 FIXED: wrap-aware forward calculation
            for (int i = 0; i < knotCount; i++)
            {
                points[i] = spline[i].Position;

                int nextIndex = (i + 1 < knotCount) ? i + 1 : (isClosed ? 0 : i);
                Vector3 next = spline[nextIndex].Position;

                forwards[i] = (next - points[i]).normalized;
            }

            List<int> ringStarts = new();

            for (int i = 0; i < knotCount; i++)
            {
                int prevIndex = (i - 1 >= 0) ? i - 1 : (isClosed ? knotCount - 1 : i);
                int nextIndex = (i + 1 < knotCount) ? i + 1 : (isClosed ? 0 : i);

                if (i > 0 || isClosed)
                {
                    float len = Vector3.Distance(points[prevIndex], points[i]);
                    uAccum += len * tiling.x;
                }

                Vector3 point = points[i];

                Vector3 forwardPrev = forwards[prevIndex];
                Vector3 forwardNext = forwards[i];

                Vector3 cross = Vector3.Cross(forwardPrev, forwardNext);
                float turn = Vector3.Dot(cross, Vector3.up);

                // 🔥 bisector
                Vector3 bisector = (forwardPrev + forwardNext);
                if (bisector.sqrMagnitude < 0.0001f)
                    bisector = forwardNext;
                bisector.Normalize();

                // 🔥 frame
                Vector3 up = Vector3.up;
                Vector3 right = Vector3.Cross(up, bisector).normalized;
                up = Vector3.Cross(bisector, right).normalized;

                // 🔥 miter
                float dot = Vector3.Dot(forwardPrev, forwardNext);
                float miter = 1f;

                if (dot < 0.999f)
                {
                    float angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));
                    miter = 1f / Mathf.Cos(angle * 0.5f);
                }

                miter = Mathf.Clamp(miter, 1f, 4f);

                //miter = 1.0f;

                if (i == knotCount - 1)
                {
                    miter = 1.0f;
                }
                
                float uScale = miter;
                
                float u = uAccum * 4.0f;
                float baseU = uAccum * 4.0f;
                float ringSpacing = 0.01f; // tweak this
// BEFORE
                float uBefore = baseU - ringSpacing;
// MAIN
                float uMain = baseU;
// AFTER
                float uAfter = baseU + ringSpacing;

                float verticalTurn = forwardNext.y - forwardPrev.y;
                bool goingUp = verticalTurn > 0f;
                bool goingDown = verticalTurn < 0f;

                float UVMiterAccum = 0;
                // =========================
                // 🔴 BEFORE LOOP
                // =========================
                if(1 ==2 )
                //if (isClosed || ((i > 0) && i != knotCount - 1))
                {
                    ringStarts.Add(vertices.Count);

                    for (int j = 0; j < vertsPerRing; j++)
                    {
                        Vector2 p = profileCache[j];
                        bool isTopVertex = Mathf.Approximately(p.y, height);

                        Vector3 offset = Vector3.one;

                        if (Mathf.Abs(verticalTurn) > 0.1f)
                        {
                            offset = right * (p.x + lateralOffset) + up * (p.y * miter + verticalOffset) + forwardNext * forwardOffset;
                            
                            if (isTopVertex && goingDown)
                                offset -= forwardPrev * (height * Mathf.Abs(1f - dot));

                            if (!isTopVertex && goingUp)
                                offset -= forwardPrev * (height * Mathf.Abs(1f - dot));
                        }
                        else
                        {
                            offset = right * (p.x * miter + lateralOffset) + up * (p.y  + verticalOffset) + forwardNext * forwardOffset;
                            
                            if (turn > 0f && j == 0)
                                offset -= forwardPrev * (height * Mathf.Abs(1f - dot));
                        }

                        vertices.Add(point - forwardPrev * cornerOffset + offset);

                        float v = (j / (float)(vertsPerRing - 1)) * tiling.y;
                        
                        if (v < .5f)
                        {
                            v += _UVShellStretch;
                        }
                        else if (v > .5f)
                        {
                            v -= _UVShellStretch;
                        }

                        //uAccum += -(Mathf.Abs(1f - dot) * .5f);
                        //uvs.Add(new Vector2(u -(Mathf.Abs(1f - dot) * .5f), v));
                        uvs.Add(new Vector2(u, v));
                    }
                }

                // =========================
                // 🔴 MAIN RING
                // =========================
                ringStarts.Add(vertices.Count);

                for (int j = 0; j < vertsPerRing; j++)
                {
                    Vector2 p = profileCache[j];
                    
                    Vector3 offset = Vector3.one;

                    if (Mathf.Abs(verticalTurn) > 0.1f)
                    {
                        offset = right * (p.x + lateralOffset) + up * (p.y * miter + verticalOffset) + forwardNext * forwardOffset;
                        
                    }
                    else
                    {
                        offset = right * (p.x * miter + lateralOffset) + up * (p.y  + verticalOffset) + forwardNext * forwardOffset;
                    }

                    vertices.Add(point + offset);

                    float v = (j / (float)(vertsPerRing - 1)) * tiling.y;
                    
                    if (v < .5f)
                    {
                        v += _UVShellStretch;
                    }
                    else if (v > .5f)
                    {
                        v -= _UVShellStretch;
                    }

                    if (i == 0)
                    {
                        uvs.Add(new Vector2(u, v));
                    }
                    else
                    {
                        if (j == 2)
                        {
                            if (verticalTurn < -0.1f)
                            {
                                uvs.Add(new Vector2(u + (miter * .25f), v));
                            }
                            else if (verticalTurn > 0.1f)
                            {
                                uvs.Add(new Vector2(u + (miter * .25f), v));
                            }
                            else
                            {
                                uvs.Add(new Vector2(u, v));
                            }
                        }
                        else if (j == 1)
                        {
                            if (verticalTurn < -0.1f)
                            {
                                uvs.Add(new Vector2(u, v));
                            }
                            else if (verticalTurn > 0.1f)
                            {
                                uvs.Add(new Vector2(u, v));
                            }
                            else
                            {
                                uvs.Add(new Vector2(u, v));
                            }
                        }
                        else
                        {
                            uvs.Add(new Vector2(u, v));
                        }
                    }
                    
                }

                // =========================
                // 🔴 AFTER LOOP
                // =========================
                if (1==2 )
                //if (isClosed || i < knotCount - 1)
                {
                    ringStarts.Add(vertices.Count);

                    for (int j = 0; j < vertsPerRing; j++)
                    {
                        Vector2 p = profileCache[j];
                        bool isTopVertex = Mathf.Approximately(p.y, height);
                        Vector3 offset = Vector3.one;// 

                        if (Mathf.Abs(verticalTurn) > 0.1f)
                        {
                            offset = right * (p.x + lateralOffset) + up * (p.y * miter + verticalOffset) + forwardNext * forwardOffset;
                            
                            if (isTopVertex && goingDown)
                                offset += forwardNext * (height * Mathf.Abs(1f - dot));

                            if (!isTopVertex && goingUp)
                                offset += forwardNext * (height * Mathf.Abs(1f - dot));
                        }
                        else
                        {
                            offset = right * (p.x * miter + lateralOffset) + up * (p.y  + verticalOffset) + forwardNext * forwardOffset;
                            
                            if (turn > 0f && j == 0)
                                offset += forwardNext * (height * Mathf.Abs(1f - dot));
                        }

                        vertices.Add(point + forwardNext * cornerOffset + offset);

                        float v = (j / (float)(vertsPerRing - 1)) * tiling.y;

                        if (v < .5f)
                        {
                            v += _UVShellStretch;
                        }
                        else if (v > .5f)
                        {
                            v -= _UVShellStretch;
                        }
                        
                        uvs.Add(new Vector2(u, v));
                    }
                }
            }

            // 🔺 triangles
            int ringCount = ringStarts.Count;

            for (int i = 0; i < ringCount - 1; i++)
            {
                int startA = ringStarts[i];
                int startB = ringStarts[i + 1];

                for (int j = 0; j < vertsPerRing - 1; j++)
                {
                    int a = startA + j;
                    int b = startB + j;
                    int c = startB + j + 1;
                    int d = startA + j + 1;

                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);

                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);

            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            GetComponent<MeshRenderer>().sharedMaterial = _currentMaterial;
        }

        void BuildRing(Vector3 center, Vector3 forward, float u)
        {
            Vector3 up = Vector3.up;
            Vector3 right = Vector3.Cross(up, forward).normalized;
            up = Vector3.Cross(forward, right).normalized;

            int startIndex = vertices.Count;

            for (int j = 0; j < profileCache.Length; j++)
            {
                Vector2 p = profileCache[j];

                Vector3 offset =
                    right * (p.x + lateralOffset) +
                    up * (p.y + verticalOffset) +
                    forward * forwardOffset;

                vertices.Add(center + offset);

                float v = (j / (float)(profileCache.Length - 1)) * tiling.y;
                uvs.Add(new Vector2(u, v));
            }

            ringStarts.Add(startIndex);
        }
        Vector2[] GetProfile(SplineMeshStyle style, float width, float height)
        {
            switch (style)
            {
                case SplineMeshStyle.Flat:
                    return new Vector2[]
                    {
                        new Vector2(-width * 0.5f, 0),
                        new Vector2(width * 0.5f, 0)
                    };

                case SplineMeshStyle.Vertical:
                    return new Vector2[]
                    {
                        new Vector2(0, 0),
                        new Vector2(0, height)
                    };

                case SplineMeshStyle.Angle90Inside:
                    return new Vector2[]
                    {
                        new Vector2(-width, 0),
                        new Vector2(0, 0),
                        new Vector2(0, height)
                    };

                case SplineMeshStyle.Angle90Outside:
                    return new Vector2[]
                    {
                        new Vector2(0, 0),
                        new Vector2(width, 0),
                        new Vector2(width, height)
                    };
            }

            return null;
        }
    }
}