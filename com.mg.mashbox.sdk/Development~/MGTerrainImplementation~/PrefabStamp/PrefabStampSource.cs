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
        public readonly GameObject Source;
        public readonly Mesh Mesh;
        public readonly Material[] Materials;
        Mesh preview;
        Vector3 lastCenter;
        Vector4 lastShape;
        bool lastInvert;
        public PrefabStampSource(GameObject source)
        {
            Source = source;
            var copies = new List<Mesh>();
            var combine = new List<CombineInstance>();
            var materials = new List<Material>();
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
                    }
                }
                if (combine.Count == 0) throw new InvalidOperationException("Prefab needs a static MeshRenderer with a mesh and material. Skinned meshes are not supported.");
                Mesh = new Mesh { name = source.name + " Stamp", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
                Mesh.CombineMeshes(combine.ToArray(), false, true);
                Mesh.RecalculateBounds(); Materials = materials.ToArray();
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
            var bounds = Mesh.bounds;
            float sourceRadius = Mathf.Max(.00001f, new Vector2(bounds.extents.x, bounds.extents.z).magnitude);
            float sourceHeight = Mathf.Max(.00001f, bounds.size.y);
            var points = mesh.vertices;
            var orientation = Quaternion.Euler(0, rotation, 0);
            for (int i = 0; i < points.Length; i++)
            {
                var v = points[i];
                var xz = new Vector2(v.x - bounds.center.x, v.z - bounds.center.z) / sourceRadius;
                float weight = Mathf.Pow(Mathf.Clamp01(1 - xz.magnitude), falloff);
                var footprint = center + orientation * new Vector3(xz.x * radius, 0, xz.y * radius);
                float baseline = surface?.Invoke(footprint) ?? center.y;
                points[i] = new Vector3(footprint.x, baseline + (v.y - bounds.min.y) / sourceHeight * height * weight * (invert ? -1 : 1), footprint.z);
            }
            mesh.vertices = points; mesh.RecalculateNormals();
            if (mesh.HasVertexAttribute(VertexAttribute.TexCoord0)) mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        public void InvalidatePreview() { if (preview != null) UnityEngine.Object.DestroyImmediate(preview); preview = null; }
        public void Draw(Vector3 center, float radius, float height, float rotation, float falloff, bool invert, Func<Vector3, float?> surface)
        {
            var shape = new Vector4(radius, height, rotation, falloff);
            if (preview == null || lastCenter != center || lastShape != shape || lastInvert != invert)
            {
                InvalidatePreview(); preview = Shape(center, radius, height, rotation, falloff, invert, surface);
                lastCenter = center; lastShape = shape; lastInvert = invert;
            }
            var camera = SceneView.currentDrawingSceneView?.camera;
            if (camera == null) return;
            for (int s = 0; s < Materials.Length; s++)
                Graphics.DrawMesh(preview, Matrix4x4.identity, Materials[s], 0, camera, s, null, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
        }
        public void Dispose() { InvalidatePreview(); if (Mesh != null) UnityEngine.Object.DestroyImmediate(Mesh); }
    }
}
