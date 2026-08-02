using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.Maps.Spline
{
    /// <summary>
    /// Generates a separate, visual-only loft mesh displaced by the blended height
    /// evaluated from MG_Lit_Trail materials. The source loft and its colliders are
    /// never modified.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("MashBox/Maps/MicroBump Layer")]
    public sealed class LoftHeightOverlayModifier : MonoBehaviour
    {
        public const string GeneratedObjectName = "MicroBump Layer";
        public const string LegacyGeneratedObjectName = "Height Overlay";
        const string GeneratedLayerName = "MicroBump";
        const string GeneratedTagName = "dirt";
        const string BakeShaderResourceName = "MGLoftHeightBake";
        static bool s_HasWarnedMissingLayer;
        static bool s_HasWarnedMissingTag;

        [SerializeField]
        MultiSplineLoft m_Loft;

        [SerializeField, Range(128, 4096)]
        int m_BakeResolution = 1024;

        [SerializeField, Range(0, 3), Tooltip("Each level splits every source triangle into four triangles.")]
        int m_SubdivisionLevels = 1;

        [SerializeField, Min(0f), Tooltip("Maximum mesh displacement in local-space units.")]
        float m_DisplacementScale = 0.05f;

        [SerializeField, Range(0f, 1f), Tooltip("Baked height that produces zero displacement.")]
        float m_HeightCenter;

        [SerializeField, Min(0f), Tooltip("Small lift that keeps the visual overlay above the original loft.")]
        float m_SurfaceOffset = 0.002f;

        [SerializeField, Min(1f), Tooltip("Maximum along-loft length of each generated renderer mesh.")]
        float m_ChunkLength = 50f;

        [SerializeField, Tooltip("Returns open boundary vertices to the original loft surface to hide exposed overlay edges.")]
        bool m_FadeBoundaryEdges = true;

        [SerializeField, HideInInspector]
        GameObject m_OverlayObject;

        [SerializeField, HideInInspector]
        Mesh m_OverlayMesh;

        [SerializeField, HideInInspector]
        Texture2D m_BakedMicroBumpTexture;

        [SerializeField, HideInInspector]
        Material m_DebugMaterial;

        [SerializeField, HideInInspector]
        string m_LastError;

        public MultiSplineLoft Loft => m_Loft;
        public GameObject OverlayObject => m_OverlayObject != null ? m_OverlayObject : gameObject;
        public Mesh OverlayMesh
        {
            get
            {
                if (m_OverlayMesh != null)
                    return m_OverlayMesh;
                MeshFilter filter = GetComponentInChildren<MeshFilter>(true);
                return filter != null ? filter.sharedMesh : null;
            }
        }
        public string LastError => m_LastError;
        public Texture2D BakedMicroBumpTexture => m_BakedMicroBumpTexture;
        public Material DebugMaterial => m_DebugMaterial;

        public void LinkToLoft(MultiSplineLoft loft)
        {
            m_Loft = loft;
        }

        public bool Rebuild()
        {
            MultiSplineLoft loft = ResolveLoft();
            return RebuildFromLoft(loft);
        }

        public bool RebuildFromLoft(MultiSplineLoft loft)
        {
            m_LastError = string.Empty;
            if (loft == null)
                return Fail("Assign a Multi Spline Loft before rebuilding the MicroBump Layer.");

            m_Loft = loft;
            if (transform == loft.transform)
                return Fail("Move this modifier to the loft's MicroBump Layer child, or use Add/Rebuild MicroBump Layer on the loft inspector to migrate it automatically.");

            Mesh sourceMesh = loft.GeneratedMesh;
            MeshRenderer sourceRenderer = loft.GetComponent<MeshRenderer>();
            if (sourceMesh == null || sourceMesh.vertexCount == 0 || sourceRenderer == null)
                return Fail("Generate the source loft mesh before rebuilding the MicroBump Layer.");

            var packedUv2 = new List<Vector4>();
            sourceMesh.GetUVs(2, packedUv2);
            if (packedUv2.Count != sourceMesh.vertexCount)
                return Fail("The MicroBump Layer requires Packed UV2 on the source loft.");

            Texture2D bakedHeight = null;
            Mesh bakeMesh = null;
            try
            {
                // UV2/TEXCOORD2 remains the splat-paint channel. The temporary bake
                // mesh mirrors that non-overlapping layout into UV3/TEXCOORD3, which
                // is used only as the height-atlas destination and lookup channel.
                bakeMesh = Instantiate(sourceMesh);
                bakeMesh.name = $"{sourceMesh.name} MicroBump Bake";
                bakeMesh.hideFlags = HideFlags.HideAndDontSave;
                bakeMesh.SetUVs(3, packedUv2);

                if (!TryBakeHeight(loft, bakeMesh, sourceRenderer, out bakedHeight, out string bakeError))
                    return Fail(bakeError);

                Mesh rebuiltMesh = BuildOverlayMesh(sourceMesh, bakedHeight);
                if (rebuiltMesh == null)
                    return Fail("The displaced overlay mesh could not be generated.");

                EnsureOverlayContainer(loft, sourceRenderer);
                if (!ReplaceDebugResources(loft, bakedHeight, out string materialError))
                {
                    DestroyGeneratedObject(rebuiltMesh);
                    return Fail(materialError);
                }
                ReplaceOverlayChunks(loft, rebuiltMesh, sourceRenderer, m_DebugMaterial);
                DestroyGeneratedObject(rebuiltMesh);
                m_OverlayObject.SetActive(true);
                return true;
            }
            catch (Exception exception)
            {
                return Fail($"MicroBump Layer rebuild failed: {exception.Message}");
            }
            finally
            {
                DestroyGeneratedObject(bakedHeight);
                DestroyGeneratedObject(bakeMesh);
            }
        }

        public void ClearGenerated()
        {
            Transform container = transform;
            for (int index = container.childCount - 1; index >= 0; index--)
            {
                GameObject chunkObject = container.GetChild(index).gameObject;
                MeshFilter filter = chunkObject.GetComponent<MeshFilter>();
                Mesh chunkMesh = filter != null ? filter.sharedMesh : null;
                if (filter != null)
                    filter.sharedMesh = null;
                MeshCollider collider = chunkObject.GetComponent<MeshCollider>();
                if (collider != null)
                    collider.sharedMesh = null;
                DestroyGeneratedObject(chunkMesh);
                DestroyGeneratedObject(chunkObject);
            }

            MeshFilter legacyFilter = GetComponent<MeshFilter>();
            if (legacyFilter != null)
                legacyFilter.sharedMesh = null;
            DestroyGeneratedObject(m_OverlayMesh);
            DestroyGeneratedObject(m_DebugMaterial);
            DestroyGeneratedObject(m_BakedMicroBumpTexture);
            m_OverlayObject = gameObject;
            m_OverlayMesh = null;
            m_DebugMaterial = null;
            m_BakedMicroBumpTexture = null;
            m_LastError = string.Empty;
        }

        void OnValidate()
        {
            m_BakeResolution = Mathf.Clamp(m_BakeResolution, 128, 4096);
            m_SubdivisionLevels = Mathf.Clamp(m_SubdivisionLevels, 0, 3);
            m_DisplacementScale = Mathf.Max(0f, m_DisplacementScale);
            m_HeightCenter = Mathf.Clamp01(m_HeightCenter);
            m_SurfaceOffset = Mathf.Max(0f, m_SurfaceOffset);
            m_ChunkLength = Mathf.Max(1f, m_ChunkLength);
        }

        MultiSplineLoft ResolveLoft()
        {
            if (m_Loft == null)
                m_Loft = GetComponentInParent<MultiSplineLoft>();
            return m_Loft;
        }

        bool TryBakeHeight(
            MultiSplineLoft loft,
            Mesh sourceMesh,
            MeshRenderer sourceRenderer,
            out Texture2D bakedHeight,
            out string error)
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
            var descriptor = new RenderTextureDescriptor(
                resolution,
                resolution,
                RenderTextureFormat.RFloat,
                0)
            {
                sRGB = false,
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            RenderTexture target = RenderTexture.GetTemporary(descriptor);
            target.name = $"{loft.name} MicroBump Bake";
            target.wrapMode = TextureWrapMode.Clamp;
            target.filterMode = FilterMode.Bilinear;

            var bakeMaterials = new List<Material>();
            var commands = new CommandBuffer { name = "MashBox Loft MicroBump Bake" };
            try
            {
                commands.SetRenderTarget(target);
                commands.ClearRenderTarget(false, true, new Color(0.5f, 0f, 0f, 1f));
                Material[] sourceMaterials = sourceRenderer.sharedMaterials;
                int drawCount = Mathf.Min(sourceMesh.subMeshCount, sourceMaterials.Length);
                bool drewSupportedMaterial = false;
                for (int submesh = 0; submesh < drawCount; submesh++)
                {
                    Material sourceMaterial = sourceMaterials[submesh];
                    if (sourceMaterial == null || !sourceMaterial.HasProperty("_ControlMap1"))
                        continue;

                    var bakeMaterial = new Material(bakeShader)
                    {
                        name = $"{sourceMaterial.name} MicroBump Bake",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    bakeMaterial.CopyPropertiesFromMaterial(sourceMaterial);
                    bakeMaterials.Add(bakeMaterial);
                    commands.DrawMesh(
                        sourceMesh,
                        loft.transform.localToWorldMatrix,
                        bakeMaterial,
                        submesh,
                        0);
                    drewSupportedMaterial = true;
                }

                if (!drewSupportedMaterial)
                {
                    error = "No loft material exposes the MG_Lit_Trail control-map properties.";
                    return false;
                }

                Graphics.ExecuteCommandBuffer(commands);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    bakedHeight = new Texture2D(
                        resolution,
                        resolution,
                        TextureFormat.RFloat,
                        false,
                        true)
                    {
                        name = $"{loft.name} Baked MicroBump",
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

        Mesh BuildOverlayMesh(Mesh sourceMesh, Texture2D bakedHeight)
        {
            Vector3[] sourceVertices = sourceMesh.vertices;
            Vector3[] sourceNormals = sourceMesh.normals;
            Vector4[] sourceTangents = sourceMesh.tangents;
            Color[] sourceColors = sourceMesh.colors;

            var vertices = new List<Vector3>(sourceVertices);
            var normals = new List<Vector3>(sourceVertices.Length);
            var tangents = new List<Vector4>(sourceVertices.Length);
            var colors = new List<Color>(sourceVertices.Length);
            for (int index = 0; index < sourceVertices.Length; index++)
            {
                normals.Add(sourceNormals.Length == sourceVertices.Length
                    ? sourceNormals[index].normalized
                    : Vector3.up);
                tangents.Add(sourceTangents.Length == sourceVertices.Length
                    ? sourceTangents[index]
                    : new Vector4(1f, 0f, 0f, 1f));
                colors.Add(sourceColors.Length == sourceVertices.Length
                    ? sourceColors[index]
                    : Color.white);
            }

            var uvChannels = new List<Vector4>[4];
            var hasUvChannel = new bool[4];
            for (int channel = 0; channel < uvChannels.Length; channel++)
            {
                uvChannels[channel] = new List<Vector4>();
                sourceMesh.GetUVs(channel, uvChannels[channel]);
                hasUvChannel[channel] = uvChannels[channel].Count == sourceVertices.Length;
                if (!hasUvChannel[channel])
                {
                    uvChannels[channel].Clear();
                    for (int index = 0; index < sourceVertices.Length; index++)
                        uvChannels[channel].Add(Vector4.zero);
                }
            }

            // UV3/TEXCOORD3 is private to the MicroBump Layer. Start with the
            // already-packed UV2 layout so it is unique across the loft and its
            // generated shoulders without changing the painter's UV channel.
            uvChannels[3].Clear();
            uvChannels[3].AddRange(uvChannels[2]);
            hasUvChannel[3] = true;

            var submeshTriangles = new List<int>[sourceMesh.subMeshCount];
            for (int submesh = 0; submesh < sourceMesh.subMeshCount; submesh++)
                submeshTriangles[submesh] = new List<int>(sourceMesh.GetTriangles(submesh));

            for (int level = 0; level < m_SubdivisionLevels; level++)
            {
                var midpointCache = new Dictionary<ulong, int>();
                for (int submesh = 0; submesh < submeshTriangles.Length; submesh++)
                {
                    List<int> sourceTriangles = submeshTriangles[submesh];
                    var subdivided = new List<int>(sourceTriangles.Count * 4);
                    for (int triangle = 0; triangle + 2 < sourceTriangles.Count; triangle += 3)
                    {
                        int a = sourceTriangles[triangle];
                        int b = sourceTriangles[triangle + 1];
                        int c = sourceTriangles[triangle + 2];
                        int ab = GetOrCreateMidpoint(
                            a, b, midpointCache, vertices, normals, tangents, colors, uvChannels);
                        int bc = GetOrCreateMidpoint(
                            b, c, midpointCache, vertices, normals, tangents, colors, uvChannels);
                        int ca = GetOrCreateMidpoint(
                            c, a, midpointCache, vertices, normals, tangents, colors, uvChannels);
                        AddTriangle(subdivided, a, ab, ca);
                        AddTriangle(subdivided, ab, b, bc);
                        AddTriangle(subdivided, ca, bc, c);
                        AddTriangle(subdivided, ab, bc, ca);
                    }

                    submeshTriangles[submesh] = subdivided;
                }
            }

            HashSet<int> boundaryVertices = m_FadeBoundaryEdges
                ? FindBoundaryVertices(submeshTriangles)
                : null;
            for (int index = 0; index < vertices.Count; index++)
            {
                Vector4 packedUv = uvChannels[3][index];
                float height = bakedHeight.GetPixelBilinear(
                    Mathf.Clamp01(packedUv.x),
                    Mathf.Clamp01(packedUv.y)).r;
                float displacement = (height - m_HeightCenter) * m_DisplacementScale + m_SurfaceOffset;
                if (boundaryVertices != null && boundaryVertices.Contains(index))
                    displacement = m_SurfaceOffset;
                vertices[index] += normals[index] * displacement;
            }

            var overlayMesh = new Mesh
            {
                name = $"{sourceMesh.name} MicroBump Layer",
                indexFormat = vertices.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            overlayMesh.SetVertices(vertices);
            overlayMesh.SetNormals(normals);
            overlayMesh.SetTangents(tangents);
            overlayMesh.SetColors(colors);
            for (int channel = 0; channel < uvChannels.Length; channel++)
            {
                if (hasUvChannel[channel] || channel == 2 || channel == 3)
                    overlayMesh.SetUVs(channel, uvChannels[channel]);
            }

            overlayMesh.subMeshCount = submeshTriangles.Length;
            for (int submesh = 0; submesh < submeshTriangles.Length; submesh++)
                overlayMesh.SetTriangles(submeshTriangles[submesh], submesh, false);
            overlayMesh.RecalculateBounds();
            return overlayMesh;
        }

        static int GetOrCreateMidpoint(
            int a,
            int b,
            Dictionary<ulong, int> cache,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Color> colors,
            IReadOnlyList<List<Vector4>> uvChannels)
        {
            uint minimum = (uint)Mathf.Min(a, b);
            uint maximum = (uint)Mathf.Max(a, b);
            ulong key = ((ulong)minimum << 32) | maximum;
            if (cache.TryGetValue(key, out int existing))
                return existing;

            int midpoint = vertices.Count;
            vertices.Add((vertices[a] + vertices[b]) * 0.5f);
            normals.Add((normals[a] + normals[b]).normalized);
            Vector4 tangent = Vector4.Lerp(tangents[a], tangents[b], 0.5f);
            Vector3 tangentDirection = new Vector3(tangent.x, tangent.y, tangent.z).normalized;
            tangents.Add(new Vector4(
                tangentDirection.x,
                tangentDirection.y,
                tangentDirection.z,
                tangent.w >= 0f ? 1f : -1f));
            colors.Add(Color.Lerp(colors[a], colors[b], 0.5f));
            for (int channel = 0; channel < uvChannels.Count; channel++)
                uvChannels[channel].Add(Vector4.Lerp(uvChannels[channel][a], uvChannels[channel][b], 0.5f));
            cache[key] = midpoint;
            return midpoint;
        }

        static void AddTriangle(List<int> triangles, int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        static HashSet<int> FindBoundaryVertices(IReadOnlyList<List<int>> submeshTriangles)
        {
            var edgeCounts = new Dictionary<ulong, int>();
            for (int submesh = 0; submesh < submeshTriangles.Count; submesh++)
            {
                List<int> triangles = submeshTriangles[submesh];
                for (int triangle = 0; triangle + 2 < triangles.Count; triangle += 3)
                {
                    CountEdge(edgeCounts, triangles[triangle], triangles[triangle + 1]);
                    CountEdge(edgeCounts, triangles[triangle + 1], triangles[triangle + 2]);
                    CountEdge(edgeCounts, triangles[triangle + 2], triangles[triangle]);
                }
            }

            var boundary = new HashSet<int>();
            foreach (KeyValuePair<ulong, int> pair in edgeCounts)
            {
                if (pair.Value != 1)
                    continue;
                boundary.Add((int)(pair.Key >> 32));
                boundary.Add((int)(pair.Key & uint.MaxValue));
            }
            return boundary;
        }

        static void CountEdge(Dictionary<ulong, int> counts, int a, int b)
        {
            uint minimum = (uint)Mathf.Min(a, b);
            uint maximum = (uint)Mathf.Max(a, b);
            ulong key = ((ulong)minimum << 32) | maximum;
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }

        void EnsureOverlayContainer(MultiSplineLoft loft, MeshRenderer sourceRenderer)
        {
            m_OverlayObject = gameObject;
            gameObject.name = GeneratedObjectName;
            gameObject.isStatic = loft.gameObject.isStatic;
            ApplyGeneratedIdentity(gameObject);
            transform.SetParent(loft.transform, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            // Migrate the original single-mesh version into a pure container.
            MeshFilter legacyFilter = GetComponent<MeshFilter>();
            if (legacyFilter != null)
            {
                legacyFilter.sharedMesh = null;
                DestroyGeneratedObject(legacyFilter);
            }
            MeshRenderer legacyRenderer = GetComponent<MeshRenderer>();
            if (legacyRenderer != null)
                DestroyGeneratedObject(legacyRenderer);
        }

        void ReplaceOverlayChunks(MultiSplineLoft loft, Mesh sourceMesh, MeshRenderer sourceRenderer, Material debugMaterial)
        {
            Vector3[] vertices = sourceMesh.vertices;
            Vector2[] uv0 = sourceMesh.uv;
            float totalDistance = Mathf.Max(0f, loft.CurrentAlongDistance);
            float chunkLength = Mathf.Max(1f, m_ChunkLength);
            int chunkCount = Mathf.Max(1, Mathf.CeilToInt(totalDistance / chunkLength));
            var chunkTriangles = new List<int>[chunkCount][];
            for (int chunk = 0; chunk < chunkCount; chunk++)
            {
                chunkTriangles[chunk] = new List<int>[sourceMesh.subMeshCount];
                for (int submesh = 0; submesh < sourceMesh.subMeshCount; submesh++)
                    chunkTriangles[chunk][submesh] = new List<int>();
            }

            float uvAlongPerMeter = Mathf.Max(0.0001f, loft.CurrentUvAlongPerMeter);
            for (int submesh = 0; submesh < sourceMesh.subMeshCount; submesh++)
            {
                int[] triangles = sourceMesh.GetTriangles(submesh);
                for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
                {
                    int a = triangles[triangle];
                    int b = triangles[triangle + 1];
                    int c = triangles[triangle + 2];
                    if ((uint)a >= vertices.Length || (uint)b >= vertices.Length || (uint)c >= vertices.Length)
                        continue;

                    float alongDistance = uv0.Length == vertices.Length
                        ? (uv0[a].y + uv0[b].y + uv0[c].y) / (3f * uvAlongPerMeter)
                        : totalDistance * triangle / Mathf.Max(1f, triangles.Length);
                    int chunk = Mathf.Clamp(Mathf.FloorToInt(alongDistance / chunkLength), 0, chunkCount - 1);
                    chunkTriangles[chunk][submesh].Add(a);
                    chunkTriangles[chunk][submesh].Add(b);
                    chunkTriangles[chunk][submesh].Add(c);
                }
            }

            int builtChunkCount = 0;
            Mesh firstMesh = null;
            for (int chunk = 0; chunk < chunkCount; chunk++)
            {
                bool hasTriangles = false;
                for (int submesh = 0; submesh < sourceMesh.subMeshCount; submesh++)
                    hasTriangles |= chunkTriangles[chunk][submesh].Count > 0;
                if (!hasTriangles)
                    continue;

                GameObject chunkObject = builtChunkCount < transform.childCount
                    ? transform.GetChild(builtChunkCount).gameObject
                    : new GameObject();
                chunkObject.transform.SetParent(transform, false);
                chunkObject.isStatic = gameObject.isStatic;
                ApplyGeneratedIdentity(chunkObject);
                float startDistance = chunk * chunkLength;
                float endDistance = totalDistance > 0f
                    ? Mathf.Min(totalDistance, startDistance + chunkLength)
                    : startDistance + chunkLength;
                chunkObject.name = $"MicroBump {chunk + 1:000} [{startDistance:0}-{endDistance:0}m]";

                MeshFilter filter = chunkObject.GetComponent<MeshFilter>();
                if (filter == null)
                    filter = chunkObject.AddComponent<MeshFilter>();
                Mesh previousMesh = filter.sharedMesh;
                MeshCollider collider = chunkObject.GetComponent<MeshCollider>();
                if (collider == null)
                    collider = chunkObject.AddComponent<MeshCollider>();
                collider.sharedMesh = null;
                Mesh chunkMesh = BuildOverlayChunkMesh(sourceMesh, chunkTriangles[chunk], chunk);
                filter.sharedMesh = chunkMesh;
                collider.convex = false;
                collider.sharedMesh = chunkMesh;
                if (previousMesh != null && previousMesh != chunkMesh)
                    DestroyGeneratedObject(previousMesh);

                MeshRenderer renderer = chunkObject.GetComponent<MeshRenderer>();
                if (renderer == null)
                    renderer = chunkObject.AddComponent<MeshRenderer>();
                CopyRendererSettings(sourceRenderer, renderer, debugMaterial);
                firstMesh ??= chunkMesh;
                builtChunkCount++;
            }

            for (int index = transform.childCount - 1; index >= builtChunkCount; index--)
            {
                GameObject chunkObject = transform.GetChild(index).gameObject;
                MeshFilter filter = chunkObject.GetComponent<MeshFilter>();
                Mesh chunkMesh = filter != null ? filter.sharedMesh : null;
                if (filter != null)
                    filter.sharedMesh = null;
                MeshCollider collider = chunkObject.GetComponent<MeshCollider>();
                if (collider != null)
                    collider.sharedMesh = null;
                DestroyGeneratedObject(chunkMesh);
                DestroyGeneratedObject(chunkObject);
            }

            if (m_OverlayMesh != null && m_OverlayMesh != firstMesh)
                DestroyGeneratedObject(m_OverlayMesh);
            m_OverlayMesh = firstMesh;
        }

        static Mesh BuildOverlayChunkMesh(Mesh sourceMesh, IReadOnlyList<List<int>> sourceTriangles, int chunkIndex)
        {
            Vector3[] sourceVertices = sourceMesh.vertices;
            Vector3[] sourceNormals = sourceMesh.normals;
            Vector4[] sourceTangents = sourceMesh.tangents;
            Color[] sourceColors = sourceMesh.colors;
            var sourceUvs = new List<Vector4>[4];
            for (int channel = 0; channel < sourceUvs.Length; channel++)
            {
                sourceUvs[channel] = new List<Vector4>();
                sourceMesh.GetUVs(channel, sourceUvs[channel]);
            }

            var remap = new Dictionary<int, int>();
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var colors = new List<Color>();
            var uvs = new List<Vector4>[4];
            for (int channel = 0; channel < uvs.Length; channel++)
                uvs[channel] = new List<Vector4>();
            var triangles = new List<int>[sourceTriangles.Count];

            for (int submesh = 0; submesh < sourceTriangles.Count; submesh++)
            {
                triangles[submesh] = new List<int>(sourceTriangles[submesh].Count);
                foreach (int sourceIndex in sourceTriangles[submesh])
                {
                    if (!remap.TryGetValue(sourceIndex, out int targetIndex))
                    {
                        targetIndex = vertices.Count;
                        remap.Add(sourceIndex, targetIndex);
                        vertices.Add(sourceVertices[sourceIndex]);
                        normals.Add(sourceNormals.Length == sourceVertices.Length ? sourceNormals[sourceIndex] : Vector3.up);
                        tangents.Add(sourceTangents.Length == sourceVertices.Length ? sourceTangents[sourceIndex] : new Vector4(1f, 0f, 0f, 1f));
                        colors.Add(sourceColors.Length == sourceVertices.Length ? sourceColors[sourceIndex] : Color.white);
                        for (int channel = 0; channel < uvs.Length; channel++)
                        {
                            if (sourceUvs[channel].Count == sourceVertices.Length)
                                uvs[channel].Add(sourceUvs[channel][sourceIndex]);
                        }
                    }
                    triangles[submesh].Add(targetIndex);
                }
            }

            var mesh = new Mesh
            {
                name = $"{sourceMesh.name} MicroBump Chunk {chunkIndex + 1:000}",
                indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetColors(colors);
            for (int channel = 0; channel < uvs.Length; channel++)
            {
                if (uvs[channel].Count == vertices.Count)
                    mesh.SetUVs(channel, uvs[channel]);
            }
            mesh.subMeshCount = triangles.Length;
            for (int submesh = 0; submesh < triangles.Length; submesh++)
                mesh.SetTriangles(triangles[submesh], submesh, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        bool ReplaceDebugResources(MultiSplineLoft loft, Texture2D bakedHeight, out string error)
        {
            error = string.Empty;
            Shader litShader = Shader.Find("HDRP/Lit");
            if (litShader == null)
            {
                error = "The HDRP/Lit shader could not be found for the MicroBump debug material.";
                return false;
            }

            var debugTexture = new Texture2D(
                bakedHeight.width,
                bakedHeight.height,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = $"{loft.name} MicroBump Bake Debug",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Color[] sourcePixels = bakedHeight.GetPixels();
            var debugPixels = new Color32[sourcePixels.Length];
            for (int index = 0; index < sourcePixels.Length; index++)
            {
                byte height = (byte)Mathf.RoundToInt(Mathf.Clamp01(sourcePixels[index].r) * 255f);
                debugPixels[index] = new Color32(height, height, height, 255);
            }
            debugTexture.SetPixels32(debugPixels);
            debugTexture.Apply(false, false);

            var debugMaterial = new Material(litShader)
            {
                name = $"{loft.name} MicroBump Debug HDRP Lit"
            };
            if (debugMaterial.HasProperty("_BaseColorMap"))
                debugMaterial.SetTexture("_BaseColorMap", debugTexture);
            if (debugMaterial.HasProperty("_BaseMap"))
                debugMaterial.SetTexture("_BaseMap", debugTexture);
            if (debugMaterial.HasProperty("_BaseColor"))
                debugMaterial.SetColor("_BaseColor", Color.white);
            if (debugMaterial.HasProperty("_UVBase"))
                debugMaterial.SetFloat("_UVBase", 3f);

            Material previousMaterial = m_DebugMaterial;
            Texture2D previousTexture = m_BakedMicroBumpTexture;
            m_DebugMaterial = debugMaterial;
            m_BakedMicroBumpTexture = debugTexture;
            DestroyGeneratedObject(previousMaterial);
            DestroyGeneratedObject(previousTexture);
            return true;
        }

        static void CopyRendererSettings(MeshRenderer source, MeshRenderer target, Material debugMaterial)
        {
            int materialCount = Mathf.Max(1, source.sharedMaterials.Length);
            var defaultMaterials = new Material[materialCount];
            for (int index = 0; index < defaultMaterials.Length; index++)
                defaultMaterials[index] = debugMaterial;
            target.sharedMaterials = defaultMaterials;
            target.enabled = false;
            target.shadowCastingMode = source.shadowCastingMode;
            target.receiveShadows = source.receiveShadows;
            target.lightProbeUsage = source.lightProbeUsage;
            target.reflectionProbeUsage = source.reflectionProbeUsage;
            target.renderingLayerMask = source.renderingLayerMask;
        }

        public static void ApplyGeneratedIdentity(GameObject target)
        {
            if (target == null)
                return;

            int layer = LayerMask.NameToLayer(GeneratedLayerName);
            if (layer >= 0)
            {
                target.layer = layer;
            }
            else if (!s_HasWarnedMissingLayer)
            {
                s_HasWarnedMissingLayer = true;
                Debug.LogWarning($"MicroBump Layer could not use physics layer '{GeneratedLayerName}' because it is not defined in Tags and Layers.");
            }

            try
            {
                target.tag = GeneratedTagName;
            }
            catch (UnityException)
            {
                if (s_HasWarnedMissingTag)
                    return;
                s_HasWarnedMissingTag = true;
                Debug.LogWarning($"MicroBump Layer could not use tag '{GeneratedTagName}' because it is not defined in Tags and Layers.");
            }
        }

        bool Fail(string message)
        {
            m_LastError = message;
            Debug.LogWarning($"{nameof(LoftHeightOverlayModifier)} on '{name}': {message}", this);
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
    }
}
