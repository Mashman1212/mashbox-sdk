using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.MapTools
{
    // Reads geometry only: prefab scripts, lights and colliders are never instantiated.
    internal sealed class PrefabStampSource : IDisposable
    {
        static readonly Unity.Profiling.ProfilerMarker PreviewMarker = new Unity.Profiling.ProfilerMarker("MG.Stamp.MaterialPreview.Rebuild");
        public readonly GameObject Source;
        public readonly Mesh Mesh;
        public readonly Material[] Materials;
        public readonly MaterialPropertyBlock[] PropertyBlocks;
        readonly Vector3[] sourcePoints;
        readonly Vector3[] previewPoints;
        readonly Vector2[] footprintPoints;
        readonly int[] footprintIndices;
        readonly float[] normalizedHeights;
        readonly Vector3[] shapedFootprints;
        readonly float[] shapedHeights;
        readonly float[] surfaceHeights;
        bool shapeReady;
        Vector4 cachedShape;
        bool cachedInvert;
        Mesh preview;
        bool previewDirty = true;
        Vector3 lastCenter;
        Vector4 lastShape;
        bool lastInvert;
        public PrefabStampSource(GameObject source)
        {
            Source = source;
            var copies = new List<Mesh>();
            var combine = new List<CombineInstance>();
            var materials = new List<Material>();
            var blocks = new List<MaterialPropertyBlock>();
            var excluded = new HashSet<Renderer>();
            foreach (var group in source.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = group.GetLODs();
                for (int i = 1; i < lods.Length; i++) foreach (var renderer in lods[i].renderers) excluded.Add(renderer);
                if (lods.Length > 0) foreach (var renderer in lods[0].renderers) excluded.Remove(renderer);
            }
            try
            {
                foreach (var filter in source.GetComponentsInChildren<MeshFilter>(false))
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (renderer == null || !renderer.enabled || excluded.Contains(renderer) || filter.sharedMesh == null) continue;
                    var mesh = ReadMesh(filter.sharedMesh); copies.Add(mesh);
                    var slots = renderer.sharedMaterials;
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        if (s >= slots.Length || slots[s] == null || mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                        combine.Add(new CombineInstance { mesh = mesh, subMeshIndex = s,
                            transform = source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix });
                        materials.Add(slots[s]);
                        var block = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(block, s);
                        if (block.isEmpty) renderer.GetPropertyBlock(block);
                        blocks.Add(block.isEmpty ? null : block);
                    }
                }
                if (combine.Count == 0) throw new InvalidOperationException("Prefab needs a static MeshRenderer with a mesh and material. Skinned meshes are not supported.");
                Mesh = new Mesh { name = source.name + " Stamp", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
                Mesh.CombineMeshes(combine.ToArray(), false, true);
                Mesh.RecalculateBounds(); Materials = materials.ToArray();
                PropertyBlocks = blocks.ToArray();
                sourcePoints = Mesh.vertices;
                previewPoints = new Vector3[sourcePoints.Length];
                footprintIndices = new int[sourcePoints.Length];
                normalizedHeights = new float[sourcePoints.Length];
                shapedHeights = new float[sourcePoints.Length];
                var unique = new Dictionary<Vector2, int>();
                var footprints = new List<Vector2>();
                var bounds = Mesh.bounds;
                float sourceRadius = Mathf.Max(.00001f, new Vector2(bounds.extents.x, bounds.extents.z).magnitude);
                float sourceHeight = Mathf.Max(.00001f, bounds.size.y);
                for (int i = 0; i < sourcePoints.Length; i++)
                {
                    var v = sourcePoints[i];
                    var xz = new Vector2(v.x - bounds.center.x, v.z - bounds.center.z) / sourceRadius;
                    if (!unique.TryGetValue(xz, out int index))
                    { index = footprints.Count; unique.Add(xz, index); footprints.Add(xz); }
                    footprintIndices[i] = index;
                    normalizedHeights[i] = (v.y - bounds.min.y) / sourceHeight;
                }
                footprintPoints = footprints.ToArray();
                shapedFootprints = new Vector3[footprintPoints.Length];
                surfaceHeights = new float[footprintPoints.Length];
            }
            finally { foreach (var mesh in copies) UnityEngine.Object.DestroyImmediate(mesh); }
        }

        static Mesh ReadMesh(Mesh source)
        {
            var result = new Mesh { name = source.name, indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
            try
            {
                using (var snapshot = MeshUtility.AcquireReadOnlyMeshData(source))
                {
                    var data = snapshot[0];
                    using (var positions = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                    { data.GetVertices(positions); result.vertices = positions.ToArray(); }
                    if (data.HasVertexAttribute(VertexAttribute.Normal))
                        using (var values = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                        { data.GetNormals(values); result.normals = values.ToArray(); }
                    if (data.HasVertexAttribute(VertexAttribute.Tangent))
                        using (var values = new NativeArray<Vector4>(data.vertexCount, Allocator.Temp))
                        { data.GetTangents(values); result.tangents = values.ToArray(); }
                    if (data.HasVertexAttribute(VertexAttribute.Color))
                        using (var values = new NativeArray<Color>(data.vertexCount, Allocator.Temp))
                        { data.GetColors(values); result.colors = values.ToArray(); }
                    for (int channel = 0; channel < 8; channel++)
                        if (data.HasVertexAttribute((VertexAttribute)((int)VertexAttribute.TexCoord0 + channel)))
                            using (var values = new NativeArray<Vector4>(data.vertexCount, Allocator.Temp))
                            { data.GetUVs(channel, values); result.SetUVs(channel, values.ToArray()); }
                    result.subMeshCount = data.subMeshCount;
                    for (int s = 0; s < data.subMeshCount; s++)
                        using (var indices = new NativeArray<int>(data.GetSubMesh(s).indexCount, Allocator.Temp))
                        { data.GetIndices(indices, s, true); result.SetIndices(indices.ToArray(), data.GetSubMesh(s).topology, s); }
                }
                if (!result.HasVertexAttribute(VertexAttribute.Normal)) result.RecalculateNormals();
                result.RecalculateBounds(); return result;
            }
            catch { UnityEngine.Object.DestroyImmediate(result); throw; }
        }

        public Mesh Shape(Vector3 center, float radius, float height, float rotation, float falloff, bool invert,
            Func<Vector3, float?> surface = null)
        {
            var mesh = UnityEngine.Object.Instantiate(Mesh);
            mesh.hideFlags = HideFlags.HideAndDontSave;
            ShapeInto(mesh, new Vector3[sourcePoints.Length], center, radius, height, rotation, falloff, invert, surface);
            return mesh;
        }

        void ShapeInto(Mesh mesh, Vector3[] points, Vector3 center, float radius, float height,
            float rotation, float falloff, bool invert, Func<Vector3, float?> surface)
        {
            var bounds = Mesh.bounds;
            float sourceRadius = Mathf.Max(.00001f, new Vector2(bounds.extents.x, bounds.extents.z).magnitude);
            float sourceHeight = Mathf.Max(.00001f, bounds.size.y);
            var orientation = Quaternion.Euler(0, rotation, 0);
            for (int i = 0; i < points.Length; i++)
            {
                var v = sourcePoints[i];
                var xz = new Vector2(v.x - bounds.center.x, v.z - bounds.center.z) / sourceRadius;
                float weight = Mathf.Pow(Mathf.Clamp01(1 - xz.magnitude), falloff);
                var footprint = center + orientation * new Vector3(xz.x * radius, 0, xz.y * radius);
                float baseline = surface?.Invoke(footprint) ?? center.y;
                points[i] = new Vector3(footprint.x, baseline + (v.y - bounds.min.y) / sourceHeight * height * weight * (invert ? -1 : 1), footprint.z);
            }
            mesh.vertices = points; mesh.RecalculateNormals();
            if (mesh.HasVertexAttribute(VertexAttribute.TexCoord0)) mesh.RecalculateTangents();
            mesh.RecalculateBounds();
        }

        public void InvalidatePreview() => previewDirty = true;

        internal Mesh PreparePreview(Vector3 center, float radius, float height, float rotation, float falloff,
            bool invert, Func<Vector3, float?> surface)
        {
            var shape = new Vector4(radius, height, rotation, falloff);
            if (preview == null || previewDirty || lastCenter != center || lastShape != shape || lastInvert != invert)
            {
                if (preview == null)
                {
                    preview = UnityEngine.Object.Instantiate(Mesh);
                    preview.hideFlags = HideFlags.HideAndDontSave;
                    preview.MarkDynamic();
                }
                using (PreviewMarker.Auto())
                {
                    bool sameShape = shapeReady && cachedShape == shape && cachedInvert == invert;
                    if (!sameShape)
                    {
                        var orientation = Quaternion.Euler(0, rotation, 0);
                        for (int i = 0; i < footprintPoints.Length; i++)
                        {
                            var xz = footprintPoints[i];
                            shapedFootprints[i] = orientation * new Vector3(xz.x * radius, 0, xz.y * radius);
                            surfaceHeights[i] = Mathf.Pow(Mathf.Clamp01(1 - xz.magnitude), falloff);
                        }
                        for (int i = 0; i < shapedHeights.Length; i++)
                            shapedHeights[i] = normalizedHeights[i] * height * surfaceHeights[footprintIndices[i]] * (invert ? -1 : 1);
                        cachedShape = shape; cachedInvert = invert; shapeReady = true;
                    }
                    for (int i = 0; i < shapedFootprints.Length; i++)
                        surfaceHeights[i] = surface?.Invoke(center + shapedFootprints[i]) ?? center.y;
                    bool translationOnly = sameShape;
                    float verticalTranslation = previewPoints.Length == 0 ? 0 : surfaceHeights[footprintIndices[0]] + shapedHeights[0] - previewPoints[0].y;
                    for (int i = 0; i < previewPoints.Length; i++)
                    {
                        int footprint = footprintIndices[i];
                        var point = center + shapedFootprints[footprint];
                        float y = surfaceHeights[footprint] + shapedHeights[i];
                        // Exact comparison: any change in relative height requires fresh shading.
                        if (y - previewPoints[i].y != verticalTranslation) translationOnly = false;
                        previewPoints[i] = new Vector3(point.x, y, point.z);
                    }
                    preview.vertices = previewPoints;
                    if (!translationOnly)
                    {
                        preview.RecalculateNormals();
                        if (preview.HasVertexAttribute(VertexAttribute.TexCoord0)) preview.RecalculateTangents();
                    }
                    preview.RecalculateBounds();
                }
                lastCenter = center; lastShape = shape; lastInvert = invert; previewDirty = false;
            }
            return preview;
        }
        public void Draw(Vector3 center, float radius, float height, float rotation, float falloff, bool invert, Func<Vector3, float?> surface)
        {
            PreparePreview(center, radius, height, rotation, falloff, invert, surface);
            var camera = SceneView.currentDrawingSceneView?.camera;
            if (camera == null) return;
            for (int s = 0; s < Materials.Length; s++)
                Graphics.DrawMesh(preview, Matrix4x4.identity, Materials[s], 0, camera, s, PropertyBlocks[s], ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
        }
        public void Dispose() { if (preview != null) UnityEngine.Object.DestroyImmediate(preview); preview = null; if (Mesh != null) UnityEngine.Object.DestroyImmediate(Mesh); }
    }
}
