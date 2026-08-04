using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.Maps
{
    /// <summary>
    /// Bakes MG_Lit_Trail height blending and builds a regular, square-chunked
    /// collision grid over a terrain-like mesh. Generation is explicit and offline.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("MashBox/Maps/MicroBump Mesh Colliders")]
    public sealed class MicroBumpMeshColliderGenerator : MonoBehaviour
    {
        const string GeneratedRootName = "MicroBump Collider Chunks";
        const string GeneratedLayerName = "MicroBump";
        const string GeneratedTagName = "dirt";
        const string BakeShaderResourceName = "MGLoftHeightBake";

        [SerializeField, Range(128, 4096)]
        int m_BakeResolution = 2048;

        [SerializeField, Min(1f), Tooltip("Width and length of each collider chunk in source-mesh local units.")]
        float m_ChunkSize = 32f;

        [SerializeField, Min(0.05f), Tooltip("Distance between collision vertices in source-mesh local units.")]
        float m_GridSpacing = 0.5f;

        [SerializeField, Min(0f), Tooltip("Maximum displacement along the interpolated source normal.")]
        float m_DisplacementScale = 0.05f;

        [SerializeField, Range(0f, 1f), Tooltip("Baked height that produces zero displacement.")]
        float m_HeightCenter;

        [SerializeField, Tooltip("Constant displacement added along the interpolated source normal.")]
        float m_SurfaceOffset;

        [SerializeField, Tooltip("Disables this object's original coarse MeshCollider while generated chunks are active.")]
        bool m_DisableSourceCollider = true;

        [SerializeField, HideInInspector]
        bool m_SourceColliderDisabledByGenerator;

        [SerializeField, HideInInspector]
        GameObject m_GeneratedRoot;

        [SerializeField, HideInInspector]
        Texture2D m_BakedHeightPreview;

        [SerializeField, HideInInspector]
        string m_LastError;

        [SerializeField, HideInInspector]
        int m_GeneratedChunkCount;

        [SerializeField, HideInInspector]
        int m_GeneratedVertexCount;

        public GameObject GeneratedRoot => m_GeneratedRoot;
        public Texture2D BakedHeightPreview => m_BakedHeightPreview;
        public string LastError => m_LastError;
        public int GeneratedChunkCount => m_GeneratedChunkCount;
        public int GeneratedVertexCount => m_GeneratedVertexCount;

        public int EstimatedChunkCount
        {
            get
            {
                MeshFilter filter = GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                    return 0;
                Bounds bounds = mesh.bounds;
                int chunksX = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / Mathf.Max(1f, m_ChunkSize)));
                int chunksZ = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / Mathf.Max(1f, m_ChunkSize)));
                return chunksX * chunksZ;
            }
        }

        public long EstimatedVertexCount
        {
            get
            {
                MeshFilter filter = GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                    return 0;
                Bounds bounds = mesh.bounds;
                float chunkSize = Mathf.Max(1f, m_ChunkSize);
                float spacing = Mathf.Max(0.05f, m_GridSpacing);
                int chunksX = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / chunkSize));
                int chunksZ = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / chunkSize));
                long total = 0;
                for (int z = 0; z < chunksZ; z++)
                {
                    float length = Mathf.Min(chunkSize, bounds.size.z - z * chunkSize);
                    long rows = Mathf.Max(1, Mathf.CeilToInt(length / spacing)) + 1L;
                    for (int x = 0; x < chunksX; x++)
                    {
                        float width = Mathf.Min(chunkSize, bounds.size.x - x * chunkSize);
                        long columns = Mathf.Max(1, Mathf.CeilToInt(width / spacing)) + 1L;
                        total += columns * rows;
                    }
                }
                return total;
            }
        }

        public bool Rebuild()
        {
            m_LastError = string.Empty;
            MeshFilter sourceFilter = GetComponent<MeshFilter>();
            MeshRenderer sourceRenderer = GetComponent<MeshRenderer>();
            Mesh sourceMesh = sourceFilter != null ? sourceFilter.sharedMesh : null;
            if (sourceMesh == null || sourceMesh.vertexCount == 0)
                return Fail("Assign a readable source mesh before rebuilding MicroBump colliders.");
            if (sourceRenderer == null)
                return Fail("A MeshRenderer with the source MG_Lit_Trail material is required.");

            var splatUvs = new List<Vector4>();
            sourceMesh.GetUVs(2, splatUvs);
            if (splatUvs.Count != sourceMesh.vertexCount)
                return Fail("The source mesh requires splat-paint UV2/TEXCOORD2 on every vertex. Converted terrain meshes already include it.");

            Texture2D bakedHeight = null;
            Mesh bakeMesh = null;
            var generatedMeshes = new List<GeneratedChunk>();
            try
            {
                bakeMesh = Instantiate(sourceMesh);
                bakeMesh.name = $"{sourceMesh.name} MicroBump Bake";
                bakeMesh.hideFlags = HideFlags.HideAndDontSave;
                bakeMesh.SetUVs(3, splatUvs);

                if (!TryBakeHeight(bakeMesh, sourceRenderer, out bakedHeight, out string bakeError))
                    return Fail(bakeError);

                if (!TryBuildChunks(sourceMesh, bakedHeight, splatUvs, generatedMeshes, out string buildError))
                    return Fail(buildError);

                ReplaceGeneratedChunks(generatedMeshes);
                ReplacePreview(bakedHeight);
                ApplySourceColliderState();
                return true;
            }
            catch (Exception exception)
            {
                return Fail($"MicroBump collider rebuild failed: {exception.Message}");
            }
            finally
            {
                for (int index = 0; index < generatedMeshes.Count; index++)
                {
                    if (!generatedMeshes[index].WasAdopted)
                        DestroyGeneratedObject(generatedMeshes[index].Mesh);
                }
                DestroyGeneratedObject(bakedHeight);
                DestroyGeneratedObject(bakeMesh);
            }
        }

        public void ClearGenerated()
        {
            DestroyGeneratedRoot();
            DestroyGeneratedObject(m_BakedHeightPreview);
            RestoreSourceCollider();
            m_BakedHeightPreview = null;
            m_LastError = string.Empty;
            m_GeneratedChunkCount = 0;
            m_GeneratedVertexCount = 0;
        }

        void OnValidate()
        {
            m_BakeResolution = Mathf.Clamp(m_BakeResolution, 128, 4096);
            m_ChunkSize = Mathf.Max(1f, m_ChunkSize);
            m_GridSpacing = Mathf.Max(0.05f, m_GridSpacing);
            m_DisplacementScale = Mathf.Max(0f, m_DisplacementScale);
            m_HeightCenter = Mathf.Clamp01(m_HeightCenter);
        }

        bool TryBakeHeight(Mesh bakeMesh, MeshRenderer sourceRenderer, out Texture2D bakedHeight, out string error)
        {
            bakedHeight = null;
            error = string.Empty;
            Shader bakeShader = Resources.Load<Shader>(BakeShaderResourceName);
            if (bakeShader == null)
            {
                error = $"Height bake shader resource '{BakeShaderResourceName}' was not found.";
                return false;
            }

            int resolution = Mathf.Clamp(m_BakeResolution, 128, 4096);
            var descriptor = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.RFloat, 0)
            {
                sRGB = false,
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            RenderTexture target = RenderTexture.GetTemporary(descriptor);
            target.name = $"{name} MicroBump Collider Bake";
            target.wrapMode = TextureWrapMode.Clamp;
            target.filterMode = FilterMode.Bilinear;

            var bakeMaterials = new List<Material>();
            var commands = new CommandBuffer { name = "MashBox Mesh MicroBump Collider Bake" };
            try
            {
                commands.SetRenderTarget(target);
                commands.ClearRenderTarget(false, true, new Color(0.5f, 0f, 0f, 1f));
                Material[] sourceMaterials = sourceRenderer.sharedMaterials;
                int drawCount = Mathf.Min(bakeMesh.subMeshCount, sourceMaterials.Length);
                bool drewSupportedMaterial = false;
                for (int submesh = 0; submesh < drawCount; submesh++)
                {
                    Material sourceMaterial = sourceMaterials[submesh];
                    if (sourceMaterial == null || !sourceMaterial.HasProperty("_ControlMap1"))
                        continue;

                    var bakeMaterial = new Material(bakeShader)
                    {
                        name = $"{sourceMaterial.name} MicroBump Collider Bake",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    bakeMaterial.CopyPropertiesFromMaterial(sourceMaterial);
                    bakeMaterials.Add(bakeMaterial);
                    commands.DrawMesh(bakeMesh, transform.localToWorldMatrix, bakeMaterial, submesh, 0);
                    drewSupportedMaterial = true;
                }

                if (!drewSupportedMaterial)
                {
                    error = "No source material exposes the MG_Lit_Trail control-map properties.";
                    return false;
                }

                Graphics.ExecuteCommandBuffer(commands);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    bakedHeight = new Texture2D(resolution, resolution, TextureFormat.RFloat, false, true)
                    {
                        name = $"{name} Baked MicroBump Collider Height",
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    bakedHeight.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0, false);
                    bakedHeight.Apply(false, false);
                }
                finally
                {
                    RenderTexture.active = previous;
                }
            }
            finally
            {
                commands.Release();
                for (int index = 0; index < bakeMaterials.Count; index++)
                    DestroyGeneratedObject(bakeMaterials[index]);
                RenderTexture.ReleaseTemporary(target);
            }

            return bakedHeight != null;
        }

        bool TryBuildChunks(
            Mesh sourceMesh,
            Texture2D bakedHeight,
            IReadOnlyList<Vector4> splatUvs,
            ICollection<GeneratedChunk> output,
            out string error)
        {
            error = string.Empty;
            Vector3[] vertices = sourceMesh.vertices;
            if (vertices.Length == 0)
            {
                error = "The source mesh has no vertices.";
                return false;
            }

            Vector3[] normals = sourceMesh.normals;
            var baseUvs = new List<Vector4>();
            sourceMesh.GetUVs(0, baseUvs);
            if (baseUvs.Count != sourceMesh.vertexCount)
            {
                baseUvs.Clear();
                for (int index = 0; index < splatUvs.Count; index++)
                    baseUvs.Add(splatUvs[index]);
            }
            Bounds bounds = sourceMesh.bounds;
            if (bounds.size.x <= Mathf.Epsilon || bounds.size.z <= Mathf.Epsilon)
            {
                error = "The source mesh must cover a non-zero area in its local X/Z plane.";
                return false;
            }

            float chunkSize = Mathf.Max(1f, m_ChunkSize);
            int chunksX = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / chunkSize));
            int chunksZ = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / chunkSize));
            var surfaceTriangles = new List<SurfaceTriangle>();

            int triangleCount = 0;
            for (int submesh = 0; submesh < sourceMesh.subMeshCount; submesh++)
            {
                int[] indices = sourceMesh.GetTriangles(submesh);
                for (int index = 0; index + 2 < indices.Length; index += 3)
                {
                    int a = indices[index];
                    int b = indices[index + 1];
                    int c = indices[index + 2];
                    if ((uint)a >= vertices.Length || (uint)b >= vertices.Length || (uint)c >= vertices.Length)
                        continue;

                    var triangle = new SurfaceTriangle(a, b, c, vertices, normals, baseUvs, splatUvs);
                    if (triangle.ProjectedArea <= 0.0000001f)
                        continue;
                    surfaceTriangles.Add(triangle);
                    triangleCount++;
                }
            }

            if (triangleCount == 0)
            {
                error = "The source mesh contains no upward terrain-like triangles that can be sampled in local X/Z.";
                return false;
            }

            var surfaceLookup = new SurfaceLookup(bounds, surfaceTriangles, m_GridSpacing);
            var heightSampler = new HeightSampler(bakedHeight);

            int chunkNumber = 0;
            for (int z = 0; z < chunksZ; z++)
            {
                float minimumZ = bounds.min.z + z * chunkSize;
                float maximumZ = Mathf.Min(bounds.max.z, minimumZ + chunkSize);
                for (int x = 0; x < chunksX; x++)
                {
                    float minimumX = bounds.min.x + x * chunkSize;
                    float maximumX = Mathf.Min(bounds.max.x, minimumX + chunkSize);
                    Mesh mesh = BuildChunkMesh(
                        surfaceLookup,
                        heightSampler,
                        minimumX,
                        maximumX,
                        minimumZ,
                        maximumZ,
                        x,
                        z,
                        ++chunkNumber);
                    if (mesh != null)
                        output.Add(new GeneratedChunk(mesh, x, z));
                }
            }

            if (output.Count == 0)
            {
                error = "No collision surface could be projected from the source mesh.";
                return false;
            }
            return true;
        }

        Mesh BuildChunkMesh(
            SurfaceLookup surfaceLookup,
            HeightSampler heightSampler,
            float minimumX,
            float maximumX,
            float minimumZ,
            float maximumZ,
            int chunkX,
            int chunkZ,
            int chunkNumber)
        {
            float width = maximumX - minimumX;
            float length = maximumZ - minimumZ;
            int cellsX = Mathf.Max(1, Mathf.CeilToInt(width / Mathf.Max(0.05f, m_GridSpacing)));
            int cellsZ = Mathf.Max(1, Mathf.CeilToInt(length / Mathf.Max(0.05f, m_GridSpacing)));
            int rowWidth = cellsX + 1;
            var vertices = new List<Vector3>(rowWidth * (cellsZ + 1));
            var baseUvs = new List<Vector2>(rowWidth * (cellsZ + 1));
            var splatUvs = new List<Vector2>(rowWidth * (cellsZ + 1));
            var valid = new bool[rowWidth * (cellsZ + 1)];

            for (int z = 0; z <= cellsZ; z++)
            {
                float localZ = Mathf.Lerp(minimumZ, maximumZ, z / (float)cellsZ);
                for (int x = 0; x <= cellsX; x++)
                {
                    float localX = Mathf.Lerp(minimumX, maximumX, x / (float)cellsX);
                    int vertexIndex = z * rowWidth + x;
                    if (!surfaceLookup.TrySample(
                        localX,
                        localZ,
                        out Vector3 position,
                        out Vector3 normal,
                        out Vector2 baseUv,
                        out Vector2 bakeUv))
                    {
                        vertices.Add(new Vector3(localX, 0f, localZ));
                        baseUvs.Add(Vector2.zero);
                        splatUvs.Add(Vector2.zero);
                        continue;
                    }

                    // Height Center 0 is the bottom-up mode: malformed or
                    // overdriven bake values must never push below the source.
                    float height = Mathf.Clamp01(heightSampler.SampleBilinear(bakeUv.x, bakeUv.y));
                    float displacement = (height - m_HeightCenter) * m_DisplacementScale + m_SurfaceOffset;
                    vertices.Add(position + normal * displacement);
                    baseUvs.Add(baseUv);
                    splatUvs.Add(bakeUv);
                    valid[vertexIndex] = true;
                }
            }

            var triangles = new List<int>(cellsX * cellsZ * 6);
            for (int z = 0; z < cellsZ; z++)
            {
                for (int x = 0; x < cellsX; x++)
                {
                    int a = z * rowWidth + x;
                    int b = (z + 1) * rowWidth + x;
                    int c = a + 1;
                    int d = b + 1;
                    if (valid[a] && valid[b] && valid[c])
                    {
                        triangles.Add(a);
                        triangles.Add(b);
                        triangles.Add(c);
                    }
                    if (valid[c] && valid[b] && valid[d])
                    {
                        triangles.Add(c);
                        triangles.Add(b);
                        triangles.Add(d);
                    }
                }
            }

            if (triangles.Count == 0)
                return null;

            // Remove samples that did not participate in a complete grid cell.
            // Apart from saving memory, this keeps collider bounds from being
            // expanded by placeholder vertices around terrain holes.
            var compactVertices = new List<Vector3>(vertices.Count);
            var compactBaseUvs = new List<Vector2>(vertices.Count);
            var compactSplatUvs = new List<Vector2>(vertices.Count);
            var compactTriangles = new List<int>(triangles.Count);
            var remap = new int[vertices.Count];
            for (int index = 0; index < remap.Length; index++)
                remap[index] = -1;
            for (int index = 0; index < triangles.Count; index++)
            {
                int sourceIndex = triangles[index];
                int targetIndex = remap[sourceIndex];
                if (targetIndex < 0)
                {
                    targetIndex = compactVertices.Count;
                    remap[sourceIndex] = targetIndex;
                    compactVertices.Add(vertices[sourceIndex]);
                    compactBaseUvs.Add(baseUvs[sourceIndex]);
                    compactSplatUvs.Add(splatUvs[sourceIndex]);
                }
                compactTriangles.Add(targetIndex);
            }

            var mesh = new Mesh
            {
                name = $"{name} MicroBump Collider [{chunkX},{chunkZ}] {chunkNumber:000}",
                indexFormat = compactVertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(compactVertices);
            mesh.SetUVs(0, compactBaseUvs);
            mesh.SetUVs(2, compactSplatUvs);
            mesh.SetUVs(3, compactSplatUvs);
            mesh.SetTriangles(compactTriangles, 0, false);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        void ReplaceGeneratedChunks(IReadOnlyList<GeneratedChunk> chunks)
        {
            DestroyGeneratedRoot();
            var root = new GameObject(GeneratedRootName);
            root.transform.SetParent(transform, false);
            root.isStatic = gameObject.isStatic;
            ApplyGeneratedIdentity(root);
            m_GeneratedRoot = root;
            m_GeneratedChunkCount = chunks.Count;
            m_GeneratedVertexCount = 0;

            for (int index = 0; index < chunks.Count; index++)
            {
                GeneratedChunk chunk = chunks[index];
                var chunkObject = new GameObject($"MicroBump Collider [{chunk.X},{chunk.Z}]");
                chunkObject.transform.SetParent(root.transform, false);
                chunkObject.isStatic = gameObject.isStatic;
                ApplyGeneratedIdentity(chunkObject);
                MeshFilter filter = chunkObject.AddComponent<MeshFilter>();
                filter.sharedMesh = chunk.Mesh;
                MeshCollider collider = chunkObject.AddComponent<MeshCollider>();
                collider.convex = false;
                collider.cookingOptions = MeshColliderCookingOptions.UseFastMidphase;
                collider.sharedMesh = chunk.Mesh;
                chunk.WasAdopted = true;
                m_GeneratedVertexCount += chunk.Mesh.vertexCount;
            }
        }

        void ReplacePreview(Texture2D source)
        {
            var preview = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true)
            {
                name = $"{name} MicroBump Collider Bake Preview",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Color[] sourcePixels = source.GetPixels();
            var pixels = new Color32[sourcePixels.Length];
            for (int index = 0; index < sourcePixels.Length; index++)
            {
                byte value = (byte)Mathf.RoundToInt(Mathf.Clamp01(sourcePixels[index].r) * 255f);
                pixels[index] = new Color32(value, value, value, 255);
            }
            preview.SetPixels32(pixels);
            preview.Apply(false, false);
            DestroyGeneratedObject(m_BakedHeightPreview);
            m_BakedHeightPreview = preview;
        }

        void ApplySourceColliderState()
        {
            MeshCollider sourceCollider = GetComponent<MeshCollider>();
            if (sourceCollider == null)
            {
                m_SourceColliderDisabledByGenerator = false;
                return;
            }

            if (m_DisableSourceCollider)
            {
                if (sourceCollider.enabled)
                {
                    sourceCollider.enabled = false;
                    m_SourceColliderDisabledByGenerator = true;
                }
            }
            else
            {
                RestoreSourceCollider();
            }
        }

        void RestoreSourceCollider()
        {
            if (!m_SourceColliderDisabledByGenerator)
                return;
            MeshCollider sourceCollider = GetComponent<MeshCollider>();
            if (sourceCollider != null)
                sourceCollider.enabled = true;
            m_SourceColliderDisabledByGenerator = false;
        }

        void DestroyGeneratedRoot()
        {
            if (m_GeneratedRoot == null)
                return;

            MeshFilter[] filters = m_GeneratedRoot.GetComponentsInChildren<MeshFilter>(true);
            for (int index = 0; index < filters.Length; index++)
            {
                Mesh mesh = filters[index].sharedMesh;
                filters[index].sharedMesh = null;
                MeshCollider collider = filters[index].GetComponent<MeshCollider>();
                if (collider != null)
                    collider.sharedMesh = null;
                DestroyGeneratedObject(mesh);
            }
            DestroyGeneratedObject(m_GeneratedRoot);
            m_GeneratedRoot = null;
        }

        static void ApplyGeneratedIdentity(GameObject target)
        {
            int layer = LayerMask.NameToLayer(GeneratedLayerName);
            if (layer >= 0)
                target.layer = layer;
            try
            {
                target.tag = GeneratedTagName;
            }
            catch (UnityException)
            {
                // Projects using the SDK settings profile define this tag. Keep
                // generation usable in projects that have not imported it yet.
            }
        }

        bool Fail(string message)
        {
            m_LastError = message;
            Debug.LogWarning($"{nameof(MicroBumpMeshColliderGenerator)} on '{name}': {message}", this);
            return false;
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

        sealed class GeneratedChunk
        {
            public readonly Mesh Mesh;
            public readonly int X;
            public readonly int Z;
            public bool WasAdopted;

            public GeneratedChunk(Mesh mesh, int x, int z)
            {
                Mesh = mesh;
                X = x;
                Z = z;
            }
        }

        sealed class SurfaceLookup
        {
            readonly Bounds m_Bounds;
            readonly IReadOnlyList<SurfaceTriangle> m_Triangles;
            readonly List<int>[] m_Buckets;
            readonly int m_Columns;
            readonly int m_Rows;
            readonly float m_CellWidth;
            readonly float m_CellLength;

            public SurfaceLookup(Bounds bounds, IReadOnlyList<SurfaceTriangle> triangles, float gridSpacing)
            {
                m_Bounds = bounds;
                m_Triangles = triangles;

                // Aim for a few source triangles per bucket. Output spacing is a
                // lower bound so a very dense collision grid does not create an
                // unnecessarily enormous lookup table.
                float averageProjectedSize = Mathf.Sqrt(
                    Mathf.Max(0.000001f, bounds.size.x * bounds.size.z) /
                    Mathf.Max(1, triangles.Count));
                float cellSize = Mathf.Max(Mathf.Max(0.05f, gridSpacing), averageProjectedSize * 2f);
                m_Columns = Mathf.Clamp(Mathf.CeilToInt(bounds.size.x / cellSize), 1, 2048);
                m_Rows = Mathf.Clamp(Mathf.CeilToInt(bounds.size.z / cellSize), 1, 2048);
                m_CellWidth = bounds.size.x / m_Columns;
                m_CellLength = bounds.size.z / m_Rows;
                m_Buckets = new List<int>[m_Columns * m_Rows];

                for (int triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
                {
                    SurfaceTriangle triangle = triangles[triangleIndex];
                    int minimumX = GetColumn(triangle.MinimumX);
                    int maximumX = GetColumn(triangle.MaximumX);
                    int minimumZ = GetRow(triangle.MinimumZ);
                    int maximumZ = GetRow(triangle.MaximumZ);
                    for (int z = minimumZ; z <= maximumZ; z++)
                    {
                        for (int x = minimumX; x <= maximumX; x++)
                        {
                            int bucketIndex = z * m_Columns + x;
                            List<int> bucket = m_Buckets[bucketIndex];
                            if (bucket == null)
                            {
                                bucket = new List<int>(8);
                                m_Buckets[bucketIndex] = bucket;
                            }
                            bucket.Add(triangleIndex);
                        }
                    }
                }
            }

            public bool TrySample(
                float x,
                float z,
                out Vector3 position,
                out Vector3 normal,
                out Vector2 baseUv,
                out Vector2 bakeUv)
            {
                position = default;
                normal = Vector3.up;
                baseUv = default;
                bakeUv = default;
                List<int> bucket = m_Buckets[GetRow(z) * m_Columns + GetColumn(x)];
                if (bucket == null)
                    return false;

                bool found = false;
                float highestY = float.NegativeInfinity;
                for (int index = 0; index < bucket.Count; index++)
                {
                    SurfaceTriangle triangle = m_Triangles[bucket[index]];
                    if (!triangle.TryGetBarycentric(x, z, out Vector3 barycentric))
                        continue;

                    Vector3 candidatePosition = triangle.A * barycentric.x + triangle.B * barycentric.y + triangle.C * barycentric.z;
                    if (found && candidatePosition.y <= highestY)
                        continue;

                    position = candidatePosition;
                    normal = (triangle.NormalA * barycentric.x + triangle.NormalB * barycentric.y + triangle.NormalC * barycentric.z).normalized;
                    if (normal.sqrMagnitude < 0.5f)
                        normal = Vector3.up;
                    else if (normal.y < 0f)
                        normal = -normal;
                    baseUv = triangle.BaseUvA * barycentric.x + triangle.BaseUvB * barycentric.y + triangle.BaseUvC * barycentric.z;
                    bakeUv = triangle.UvA * barycentric.x + triangle.UvB * barycentric.y + triangle.UvC * barycentric.z;
                    highestY = candidatePosition.y;
                    found = true;
                }
                return found;
            }

            int GetColumn(float x)
            {
                return Mathf.Clamp(Mathf.FloorToInt((x - m_Bounds.min.x) / m_CellWidth), 0, m_Columns - 1);
            }

            int GetRow(float z)
            {
                return Mathf.Clamp(Mathf.FloorToInt((z - m_Bounds.min.z) / m_CellLength), 0, m_Rows - 1);
            }
        }

        readonly struct HeightSampler
        {
            readonly NativeArray<float> m_Pixels;
            readonly int m_Width;
            readonly int m_Height;

            public HeightSampler(Texture2D texture)
            {
                m_Pixels = texture.GetRawTextureData<float>();
                m_Width = texture.width;
                m_Height = texture.height;
            }

            public float SampleBilinear(float u, float v)
            {
                float x = Mathf.Clamp01(u) * (m_Width - 1);
                float y = Mathf.Clamp01(v) * (m_Height - 1);
                int x0 = Mathf.FloorToInt(x);
                int y0 = Mathf.FloorToInt(y);
                int x1 = Mathf.Min(x0 + 1, m_Width - 1);
                int y1 = Mathf.Min(y0 + 1, m_Height - 1);
                float lower = Mathf.Lerp(m_Pixels[y0 * m_Width + x0], m_Pixels[y0 * m_Width + x1], x - x0);
                float upper = Mathf.Lerp(m_Pixels[y1 * m_Width + x0], m_Pixels[y1 * m_Width + x1], x - x0);
                return Mathf.Lerp(lower, upper, y - y0);
            }
        }

        readonly struct SurfaceTriangle
        {
            const float BarycentricTolerance = 0.0001f;

            public readonly Vector3 A;
            public readonly Vector3 B;
            public readonly Vector3 C;
            public readonly Vector3 NormalA;
            public readonly Vector3 NormalB;
            public readonly Vector3 NormalC;
            public readonly Vector2 UvA;
            public readonly Vector2 UvB;
            public readonly Vector2 UvC;
            public readonly Vector2 BaseUvA;
            public readonly Vector2 BaseUvB;
            public readonly Vector2 BaseUvC;
            public readonly float MinimumX;
            public readonly float MaximumX;
            public readonly float MinimumZ;
            public readonly float MaximumZ;
            public readonly float ProjectedArea;

            public SurfaceTriangle(
                int a,
                int b,
                int c,
                IReadOnlyList<Vector3> vertices,
                IReadOnlyList<Vector3> normals,
                IReadOnlyList<Vector4> baseUvs,
                IReadOnlyList<Vector4> uvs)
            {
                A = vertices[a];
                B = vertices[b];
                C = vertices[c];
                NormalA = normals.Count == vertices.Count ? normals[a].normalized : Vector3.up;
                NormalB = normals.Count == vertices.Count ? normals[b].normalized : Vector3.up;
                NormalC = normals.Count == vertices.Count ? normals[c].normalized : Vector3.up;
                UvA = new Vector2(uvs[a].x, uvs[a].y);
                UvB = new Vector2(uvs[b].x, uvs[b].y);
                UvC = new Vector2(uvs[c].x, uvs[c].y);
                BaseUvA = new Vector2(baseUvs[a].x, baseUvs[a].y);
                BaseUvB = new Vector2(baseUvs[b].x, baseUvs[b].y);
                BaseUvC = new Vector2(baseUvs[c].x, baseUvs[c].y);
                MinimumX = Mathf.Min(A.x, Mathf.Min(B.x, C.x));
                MaximumX = Mathf.Max(A.x, Mathf.Max(B.x, C.x));
                MinimumZ = Mathf.Min(A.z, Mathf.Min(B.z, C.z));
                MaximumZ = Mathf.Max(A.z, Mathf.Max(B.z, C.z));
                ProjectedArea = Mathf.Abs((B.z - C.z) * (A.x - C.x) + (C.x - B.x) * (A.z - C.z));
            }

            public bool TryGetBarycentric(float x, float z, out Vector3 barycentric)
            {
                float denominator = (B.z - C.z) * (A.x - C.x) + (C.x - B.x) * (A.z - C.z);
                if (Mathf.Abs(denominator) <= 0.0000001f)
                {
                    barycentric = default;
                    return false;
                }

                float first = ((B.z - C.z) * (x - C.x) + (C.x - B.x) * (z - C.z)) / denominator;
                float second = ((C.z - A.z) * (x - C.x) + (A.x - C.x) * (z - C.z)) / denominator;
                float third = 1f - first - second;
                barycentric = new Vector3(first, second, third);
                return first >= -BarycentricTolerance && second >= -BarycentricTolerance && third >= -BarycentricTolerance;
            }
        }
    }
}
