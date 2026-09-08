using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.Maps.Spline
{
    /// <summary>Partitions triangles without changing their geometry or interpolating painted data.</summary>
    public static class LoftVisualMeshBuilder
    {
        public static List<Mesh> Split(Mesh mesh, float[] alongDistances, float sectionLength)
        {
            if (mesh == null || !mesh.isReadable)
                throw new InvalidOperationException("Loft visual chunking requires a readable source mesh.");
            if (alongDistances == null || alongDistances.Length != mesh.vertexCount)
                throw new ArgumentException("One along distance is required per rendered vertex.");
            if (!(sectionLength >= 1f) || float.IsInfinity(sectionLength))
                throw new ArgumentOutOfRangeException(nameof(sectionLength));

            var sections = new SortedDictionary<int, List<int>[]>();
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                if (mesh.GetTopology(submesh) != MeshTopology.Triangles)
                    throw new InvalidOperationException("Loft visual chunks support triangle meshes only.");
                int[] triangles = mesh.GetTriangles(submesh);
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    float distance = (alongDistances[triangles[t]] + alongDistances[triangles[t + 1]] +
                        alongDistances[triangles[t + 2]]) / 3f;
                    int section = Mathf.Max(0, Mathf.FloorToInt(distance / sectionLength));
                    if (!sections.TryGetValue(section, out List<int>[] indices))
                    {
                        indices = new List<int>[mesh.subMeshCount];
                        for (int s = 0; s < indices.Length; s++) indices[s] = new List<int>();
                        sections.Add(section, indices);
                    }
                    indices[submesh].Add(triangles[t]);
                    indices[submesh].Add(triangles[t + 1]);
                    indices[submesh].Add(triangles[t + 2]);
                }
            }

            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            Color[] colors = mesh.colors;
            var uv = new List<Vector4>[8];
            for (int channel = 0; channel < uv.Length; channel++)
            {
                uv[channel] = new List<Vector4>();
                mesh.GetUVs(channel, uv[channel]);
            }
            var result = new List<Mesh>();
            try
            {
                foreach (var section in sections)
                {
                    var remap = new Dictionary<int, int>();
                    var originals = new List<int>();
                    foreach (List<int> indices in section.Value)
                        for (int i = 0; i < indices.Count; i++)
                        {
                            int original = indices[i];
                            if (!remap.TryGetValue(original, out int mapped))
                            {
                                mapped = originals.Count;
                                remap.Add(original, mapped);
                                originals.Add(original);
                            }
                            indices[i] = mapped;
                        }
                    var chunk = new Mesh
                    {
                        name = $"{mesh.name} Visual {section.Key + 1:000} [{section.Key * sectionLength:0}m]",
                        indexFormat = originals.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
                    };
                    result.Add(chunk);
                    chunk.vertices = Copy(vertices, originals);
                    if (normals.Length == vertices.Length) chunk.normals = Copy(normals, originals);
                    if (tangents.Length == vertices.Length) chunk.tangents = Copy(tangents, originals);
                    if (colors.Length == vertices.Length) chunk.colors = Copy(colors, originals);
                    for (int channel = 0; channel < uv.Length; channel++)
                        if (uv[channel].Count == vertices.Length)
                            chunk.SetUVs(channel, Copy(uv[channel], originals));
                    chunk.subMeshCount = mesh.subMeshCount;
                    for (int s = 0; s < mesh.subMeshCount; s++) chunk.SetTriangles(section.Value[s], s, false);
                    // Never recalculate normals/tangents: preserve shading at chunk and UV seams.
                    chunk.RecalculateBounds();
                }
                return result;
            }
            catch
            {
                foreach (Mesh chunk in result) DestroyGenerated(chunk);
                throw;
            }
        }

        static T[] Copy<T>(IReadOnlyList<T> source, List<int> indices)
        {
            var result = new T[indices.Count];
            for (int i = 0; i < result.Length; i++) result[i] = source[indices[i]];
            return result;
        }

        internal static void DestroyGenerated(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        public static void CopyRenderer(MeshRenderer source, MeshRenderer target)
        {
            target.sharedMaterials = source.sharedMaterials;
            target.enabled = source.enabled;
            target.shadowCastingMode = source.shadowCastingMode;
            target.receiveShadows = source.receiveShadows;
            target.lightProbeUsage = source.lightProbeUsage;
            target.reflectionProbeUsage = source.reflectionProbeUsage;
            target.probeAnchor = source.probeAnchor;
            target.lightProbeProxyVolumeOverride = source.lightProbeProxyVolumeOverride;
            target.lightmapIndex = source.lightmapIndex;
            target.lightmapScaleOffset = source.lightmapScaleOffset;
            target.realtimeLightmapIndex = source.realtimeLightmapIndex;
            target.realtimeLightmapScaleOffset = source.realtimeLightmapScaleOffset;
            target.renderingLayerMask = source.renderingLayerMask;
            target.motionVectorGenerationMode = source.motionVectorGenerationMode;
            target.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
            target.sortingLayerID = source.sortingLayerID;
            target.sortingOrder = source.sortingOrder;
            var block = new MaterialPropertyBlock();
            source.GetPropertyBlock(block);
            target.SetPropertyBlock(block);
            for (int i = 0; i < source.sharedMaterials.Length; i++)
            {
                block.Clear();
                source.GetPropertyBlock(block, i);
                if (!block.isEmpty) target.SetPropertyBlock(block, i);
            }
        }
    }
}
