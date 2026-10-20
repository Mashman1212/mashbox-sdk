using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal static partial class StudioWorkspaceText
    {
        [Serializable] private sealed class MeshData
        {
            public bool index32;
            public float[] v, n, t, c, poses, bounds;
            public float[] numbers;
            public int[] vm, nm, tm, cm;
            public Channel[] uv;
            public Submesh[] sub;
            public int[] boneCounts, boneIndices;
            public float[] boneWeights;
            public Morph[] morphs;
        }
        [Serializable] private sealed class Channel { public int channel, dimensions; public float[] values; public int[] map; }
        [Serializable] private sealed class Submesh { public int topology, baseVertex; public int[] indices; }
        [Serializable] private sealed class Morph { public string name; public float weight; public float[] v, n, t; }

        private static MeshData CaptureMesh(Mesh mesh)
        {
            if (!mesh.isReadable)
            {
                var snapshot = ReadableSnapshot(mesh);
                try { return CaptureMesh(snapshot); } finally { UnityEngine.Object.DestroyImmediate(snapshot); }
            }
            var channels = new List<Channel>();
            var values = new List<Vector4>();
            for (int c = 0; c < 8; c++)
            {
                var attribute = VertexAttribute.TexCoord0 + c;
                if (!mesh.HasVertexAttribute(attribute)) continue;
                mesh.GetUVs(c, values); int dimensions = mesh.GetVertexAttributeDimension(attribute);
                var packed = new float[values.Count * dimensions];
                for (int i = 0; i < values.Count; i++) for (int j = 0; j < dimensions; j++) packed[i * dimensions + j] = values[i][j];
                var channel = new Channel { channel = c, dimensions = dimensions, values = packed };
                Deduplicate(ref channel.values, out channel.map, dimensions); channels.Add(channel);
            }
            var submeshes = new Submesh[mesh.subMeshCount];
            for (int i = 0; i < submeshes.Length; i++) submeshes[i] = new Submesh { topology = (int)mesh.GetTopology(i), baseVertex = (int)mesh.GetBaseVertex(i), indices = mesh.GetIndices(i, false) };
            var poses = mesh.bindposes; var poseValues = new float[poses.Length * 16];
            for (int i = 0; i < poses.Length; i++) for (int j = 0; j < 16; j++) poseValues[i * 16 + j] = poses[i][j];
            var counts = mesh.GetBonesPerVertex(); var weights = mesh.GetAllBoneWeights();
            var bones = new int[counts.Length]; for (int i = 0; i < bones.Length; i++) bones[i] = counts[i];
            var boneIndices = new int[weights.Length]; var boneWeights = new float[weights.Length];
            for (int i = 0; i < weights.Length; i++) { boneIndices[i] = weights[i].boneIndex; boneWeights[i] = weights[i].weight; }
            var morphs = new List<Morph>();
            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
                for (int frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); frame++)
                {
                    var v = new Vector3[mesh.vertexCount]; var n = new Vector3[v.Length]; var t = new Vector3[v.Length];
                    mesh.GetBlendShapeFrameVertices(shape, frame, v, n, t);
                    morphs.Add(new Morph { name = mesh.GetBlendShapeName(shape), weight = mesh.GetBlendShapeFrameWeight(shape, frame), v = Pack(v), n = Pack(n), t = Pack(t) });
                }
            var colors = mesh.colors; var colorValues = new float[colors.Length * 4];
            for (int i = 0; i < colors.Length; i++) for (int j = 0; j < 4; j++) colorValues[i * 4 + j] = colors[i][j];
            var b = mesh.bounds;
            var data = new MeshData { index32 = mesh.indexFormat == IndexFormat.UInt32, v = Pack(mesh.vertices), n = Pack(mesh.normals), t = Pack(mesh.tangents), c = colorValues,
                uv = channels.ToArray(), sub = submeshes, poses = poseValues, bounds = new[] { b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z },
                boneCounts = bones, boneIndices = boneIndices, boneWeights = boneWeights, morphs = morphs.ToArray() };
            Deduplicate(ref data.v, out data.vm, 3); Deduplicate(ref data.n, out data.nm, 3);
            Deduplicate(ref data.t, out data.tm, 4); Deduplicate(ref data.c, out data.cm, 4);
            PoolNumbers(data);
            return data;
        }

        private static int[] MeshTriangles(Mesh mesh)
        {
            if (mesh.isReadable) return mesh.triangles;
            var snapshot = ReadableSnapshot(mesh);
            try { return snapshot.triangles; } finally { UnityEngine.Object.DestroyImmediate(snapshot); }
        }

        // Imported/native legacy meshes may have Read/Write disabled. The Editor
        // snapshot API can read them without modifying the mesh or its importer.
        private static Mesh ReadableSnapshot(Mesh source)
        {
            var copy = new Mesh { name = source.name };
            try
            {
                using (var read = MeshUtility.AcquireReadOnlyMeshData(source))
                {
                    var write = Mesh.AllocateWritableMeshData(1);
                    var from = read[0]; var to = write[0];
                    to.SetVertexBufferParams(from.vertexCount, source.GetVertexAttributes());
                    for (int stream = 0; stream < source.vertexBufferCount; stream++) to.GetVertexData<byte>(stream).CopyFrom(from.GetVertexData<byte>(stream));
                    var indices = from.GetIndexData<byte>();
                    to.SetIndexBufferParams(indices.Length / (source.indexFormat == IndexFormat.UInt32 ? 4 : 2), source.indexFormat);
                    to.GetIndexData<byte>().CopyFrom(indices);
                    to.subMeshCount = source.subMeshCount;
                    for (int i = 0; i < source.subMeshCount; i++) to.SetSubMesh(i, source.GetSubMesh(i), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                    Mesh.ApplyAndDisposeWritableMeshData(write, copy, MeshUpdateFlags.DontRecalculateBounds);
                }
                copy.bounds = source.bounds; copy.bindposes = source.bindposes;
                var counts = source.GetBonesPerVertex(); var weights = source.GetAllBoneWeights();
                if (weights.Length > 0) copy.SetBoneWeights(counts, weights);
                for (int shape = 0; shape < source.blendShapeCount; shape++)
                    for (int frame = 0; frame < source.GetBlendShapeFrameCount(shape); frame++)
                    {
                        var v = new Vector3[source.vertexCount]; var n = new Vector3[v.Length]; var t = new Vector3[v.Length];
                        source.GetBlendShapeFrameVertices(shape, frame, v, n, t);
                        copy.AddBlendShapeFrame(source.GetBlendShapeName(shape), source.GetBlendShapeFrameWeight(shape, frame), v, n, t);
                    }
                return copy;
            }
            catch { UnityEngine.Object.DestroyImmediate(copy); throw; }
        }

        private static Mesh RestoreMesh(MeshData data)
        {
            if (data == null || data.v == null || data.sub == null) throw new InvalidDataException("Missing workspace mesh data.");
            var mesh = new Mesh();
            try
            {
                float[] Decode(float[] buffer) => DecodeNumbers(data.numbers, buffer);
                var positions = Expand(Decode(data.v), data.vm, 3);
                var normals = Expand(Decode(data.n), data.nm, 3); var tangents = Expand(Decode(data.t), data.tm, 4); var colorData = Expand(Decode(data.c), data.cm, 4);
                mesh.indexFormat = data.index32 || positions.Length / 3 > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.vertices = V3(positions); int count = mesh.vertexCount;
                void Attribute(float[] a, int dimensions) { CheckNumbers(a, dimensions); if (a != null && a.Length != 0 && a.Length != count * dimensions) throw new InvalidDataException("Mesh attribute count does not match its vertices."); }
                Attribute(normals, 3); Attribute(tangents, 4); Attribute(colorData, 4);
                if (normals != null && normals.Length > 0) mesh.normals = V3(normals);
                if (tangents != null && tangents.Length > 0) mesh.tangents = V4(tangents);
                if (colorData != null && colorData.Length > 0)
                {
                    var colors = new Color[count]; for (int i = 0; i < count; i++) colors[i] = new Color(colorData[i * 4], colorData[i * 4 + 1], colorData[i * 4 + 2], colorData[i * 4 + 3]); mesh.colors = colors;
                }
                var seen = new HashSet<int>();
                foreach (var channel in data.uv ?? Array.Empty<Channel>())
                {
                    if (channel == null || channel.channel < 0 || channel.channel > 7 || !seen.Add(channel.channel) || channel.dimensions < 2 || channel.dimensions > 4) throw new InvalidDataException("Invalid mesh UV channel.");
                    var values = Expand(Decode(channel.values), channel.map, channel.dimensions); Attribute(values, channel.dimensions);
                    if (channel.dimensions == 2) mesh.SetUVs(channel.channel, V2(values));
                    else if (channel.dimensions == 3) mesh.SetUVs(channel.channel, V3(values));
                    else mesh.SetUVs(channel.channel, V4(values));
                }
                mesh.subMeshCount = data.sub.Length;
                for (int i = 0; i < data.sub.Length; i++)
                {
                    var sub = data.sub[i];
                    if (sub == null || sub.indices == null || !Enum.IsDefined(typeof(MeshTopology), sub.topology) || sub.baseVertex < 0 ||
                        sub.indices.Any(index => index < 0 || (long)index + sub.baseVertex >= count)) throw new InvalidDataException("Invalid mesh index/submesh data.");
                    var topology = (MeshTopology)sub.topology;
                    if ((topology == MeshTopology.Triangles && sub.indices.Length % 3 != 0) || (topology == MeshTopology.Quads && sub.indices.Length % 4 != 0) || (topology == MeshTopology.Lines && sub.indices.Length % 2 != 0)) throw new InvalidDataException("Incomplete mesh primitives.");
                    mesh.SetIndices(sub.indices, topology, i, false, sub.baseVertex);
                }
                var poses = Decode(data.poses); CheckNumbers(poses, 16);
                if (poses != null && poses.Length > 0)
                {
                    var matrices = new Matrix4x4[poses.Length / 16];
                    for (int i = 0; i < matrices.Length; i++) for (int j = 0; j < 16; j++) matrices[i][j] = poses[i * 16 + j];
                    mesh.bindposes = matrices;
                }
                var boneWeights = Decode(data.boneWeights);
                if (boneWeights != null && boneWeights.Length > 0)
                {
                    CheckNumbers(boneWeights, 1);
                    if (data.boneCounts == null || data.boneCounts.Length != count || data.boneIndices == null || data.boneIndices.Length != boneWeights.Length ||
                        data.boneCounts.Any(n => n < 0 || n > 255) || data.boneCounts.Sum(n => (long)n) != boneWeights.Length ||
                        data.boneIndices.Any(i => i < 0 || i >= mesh.bindposes.Length) || boneWeights.Any(w => w < 0)) throw new InvalidDataException("Invalid mesh skinning data.");
                    var values = new BoneWeight1[boneWeights.Length];
                    for (int i = 0; i < values.Length; i++) values[i] = new BoneWeight1 { boneIndex = data.boneIndices[i], weight = boneWeights[i] };
                    using (var counts = new NativeArray<byte>(data.boneCounts.Select(n => (byte)n).ToArray(), Allocator.Temp))
                    using (var weights = new NativeArray<BoneWeight1>(values, Allocator.Temp))
                    {
                        mesh.SetBoneWeights(counts, weights);
                    }
                }
                foreach (var morph in data.morphs ?? Array.Empty<Morph>())
                {
                    if (morph == null || string.IsNullOrEmpty(morph.name) || morph.v == null || morph.v.Length != count * 3 || float.IsNaN(morph.weight) || float.IsInfinity(morph.weight)) throw new InvalidDataException("Invalid mesh blend-shape frame.");
                    Attribute(morph.n, 3); Attribute(morph.t, 3);
                    mesh.AddBlendShapeFrame(morph.name, morph.weight, V3(Decode(morph.v)), V3(Decode(morph.n)), V3(Decode(morph.t)));
                }
                var bounds = Decode(data.bounds); CheckNumbers(bounds, 6);
                if (bounds != null && bounds.Length == 6) mesh.bounds = new Bounds(new Vector3(bounds[0], bounds[1], bounds[2]), new Vector3(bounds[3], bounds[4], bounds[5]));
                else mesh.RecalculateBounds();
                return mesh;
            }
            catch { UnityEngine.Object.DestroyImmediate(mesh); throw; }
        }

        private static void CheckNumbers(float[] values, int stride)
        {
            if (values == null) return;
            if (values.Length % stride != 0 || values.Any(v => float.IsNaN(v) || float.IsInfinity(v))) throw new InvalidDataException("Invalid numeric mesh data.");
        }
        // Optional tuple dictionaries compact repeated positions/normals/UVs without
        // merging render vertices or their seams. Skip high-entropy buffers early.
        private static void Deduplicate(ref float[] values, out int[] map, int stride)
        {
            map = null; if (values == null || values.Length < stride * 8) return;
            CheckNumbers(values, stride); int count = values.Length / stride;
            Vector4 Key(int i, float[] data) { var v = Vector4.zero; for (int j = 0; j < stride; j++) v[j] = data[i * stride + j]; return v; }
            var sample = new HashSet<Vector4>(); int size = Math.Min(count, 2048);
            for (int i = 0; i < size; i++) sample.Add(Key(i, values));
            if (sample.Count > size * .85f) return;
            var ids = new Dictionary<Vector4, int>(); var unique = new List<float>(); var indices = new int[count];
            long before = 0, after = 0;
            for (int i = 0; i < count; i++)
            {
                var key = Key(i, values);
                if (!ids.TryGetValue(key, out int id))
                { id = ids.Count; ids.Add(key, id); for (int j = 0; j < stride; j++) { unique.Add(key[j]); after += key[j].ToString("R", CultureInfo.InvariantCulture).Length + 1; } }
                indices[i] = id; after += id.ToString(CultureInfo.InvariantCulture).Length + 1;
                for (int j = 0; j < stride; j++) before += key[j].ToString("R", CultureInfo.InvariantCulture).Length + 1;
            }
            if (after + 16 >= before) return;
            values = unique.ToArray(); map = indices;
        }
        private static float[] Expand(float[] values, int[] map, int stride)
        {
            CheckNumbers(values, stride);
            if (map == null || map.Length == 0) return values;
            if (values == null || map.Any(i => i < 0 || i >= values.Length / stride)) throw new InvalidDataException("Invalid mesh attribute dictionary.");
            var result = new float[checked(map.Length * stride)];
            for (int i = 0; i < map.Length; i++) Array.Copy(values, map[i] * stride, result, i * stride, stride);
            return result;
        }
        // A lossless, optional scalar dictionary spans all attributes of a mesh.
        // Integer tokens and their numeric dictionary remain ordinary readable JSON.
        private static void PoolNumbers(MeshData data)
        {
            var buffers = new List<float[]> { data.v, data.n, data.t, data.c, data.poses, data.bounds, data.boneWeights };
            buffers.AddRange(data.uv.Select(c => c.values));
            foreach (var morph in data.morphs) { buffers.Add(morph.v); buffers.Add(morph.n); buffers.Add(morph.t); }
            buffers = buffers.Where(b => b != null && b.Length > 0).Distinct().ToList();
            var sample = buffers.SelectMany(b => b.Take(256)).Take(2048).ToArray();
            if (sample.Length < 64 || sample.Distinct().Count() > sample.Length * .85f) return;
            var ids = new Dictionary<int, int>(); var values = new List<float>(); var lengths = new List<int>();
            long before = 0, after = 32;
            foreach (var buffer in buffers) foreach (float number in buffer)
            {
                if (float.IsNaN(number) || float.IsInfinity(number)) throw new InvalidDataException("Invalid numeric mesh data.");
                int bits = BitConverter.SingleToInt32Bits(number);
                if (!ids.TryGetValue(bits, out int id))
                {
                    if (ids.Count >= 1000000) return; // Avoid a large dictionary for high-entropy geometry.
                    id = ids.Count; ids.Add(bits, id); values.Add(number);
                    int length = number.ToString("R", CultureInfo.InvariantCulture).Length + 1; lengths.Add(length); after += length;
                }
                before += lengths[id]; after += id < 10 ? 2 : id < 100 ? 3 : id < 1000 ? 4 : id < 10000 ? 5 : id < 100000 ? 6 : 7;
            }
            if (after >= before) return;
            data.numbers = values.ToArray();
            foreach (var buffer in buffers) for (int i = 0; i < buffer.Length; i++) buffer[i] = ids[BitConverter.SingleToInt32Bits(buffer[i])];
        }
        private static float[] DecodeNumbers(float[] numbers, float[] tokens)
        {
            if (numbers == null || numbers.Length == 0 || tokens == null || tokens.Length == 0) return tokens;
            CheckNumbers(numbers, 1); var values = new float[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                float token = tokens[i];
                if (token < 0 || token >= numbers.Length || token != (int)token) throw new InvalidDataException("Invalid mesh numeric dictionary.");
                values[i] = numbers[(int)token];
            }
            return values;
        }
        private static float[] Pack(Vector2[] a) { if (a == null) return null; var r = new float[a.Length * 2]; for (int i = 0; i < a.Length; i++) { r[i * 2] = a[i].x; r[i * 2 + 1] = a[i].y; } return r; }
        private static float[] Pack(Vector3[] a) { if (a == null) return null; var r = new float[a.Length * 3]; for (int i = 0; i < a.Length; i++) for (int j = 0; j < 3; j++) r[i * 3 + j] = a[i][j]; return r; }
        private static float[] Pack(Vector4[] a) { if (a == null) return null; var r = new float[a.Length * 4]; for (int i = 0; i < a.Length; i++) for (int j = 0; j < 4; j++) r[i * 4 + j] = a[i][j]; return r; }
        private static Vector2[] V2(float[] a) { CheckNumbers(a, 2); if (a == null) return Array.Empty<Vector2>(); var r = new Vector2[a.Length / 2]; for (int i = 0; i < r.Length; i++) r[i] = new Vector2(a[i * 2], a[i * 2 + 1]); return r; }
        private static Vector3[] V3(float[] a) { CheckNumbers(a, 3); if (a == null) return Array.Empty<Vector3>(); var r = new Vector3[a.Length / 3]; for (int i = 0; i < r.Length; i++) r[i] = new Vector3(a[i * 3], a[i * 3 + 1], a[i * 3 + 2]); return r; }
        private static Vector4[] V4(float[] a) { CheckNumbers(a, 4); if (a == null) return Array.Empty<Vector4>(); var r = new Vector4[a.Length / 4]; for (int i = 0; i < r.Length; i++) r[i] = new Vector4(a[i * 4], a[i * 4 + 1], a[i * 4 + 2], a[i * 4 + 3]); return r; }
    }
}
