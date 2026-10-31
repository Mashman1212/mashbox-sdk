using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MG.GodSurface.Editor
{
    public static class MGSurfaceMeshAudit
    {
        // Matching vertex counts alone do not establish morph correspondence. UV seams, triangle order,
        // vertex colours and tangent handedness must also agree before one carrier can safely represent both.
        public static string Compatibility(Mesh mesh)
        {
            using var data = MeshUtility.AcquireReadOnlyMeshData(mesh); var read = data[0];
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            writer.Write(read.vertexCount); writer.Write(read.subMeshCount);
            for (int part = 0; part < read.subMeshCount; part++)
            {
                var submesh = read.GetSubMesh(part); writer.Write((int)submesh.topology); writer.Write(submesh.indexCount);
                using var indices = new NativeArray<int>(submesh.indexCount, Allocator.Temp); read.GetIndices(indices, part);
                foreach (int index in indices) writer.Write(index);
            }
            for (int channel = 0; channel < 8; channel++)
            {
                bool exists = read.HasVertexAttribute(VertexAttribute.TexCoord0 + channel); writer.Write(exists); if (!exists) continue;
                using var uv = new NativeArray<Vector4>(read.vertexCount, Allocator.Temp); read.GetUVs(channel, uv);
                foreach (var value in uv) { writer.Write(value.x); writer.Write(value.y); writer.Write(value.z); writer.Write(value.w); }
            }
            bool tangents = read.HasVertexAttribute(VertexAttribute.Tangent); writer.Write(tangents);
            if (tangents) { using var values = new NativeArray<Vector4>(read.vertexCount, Allocator.Temp); read.GetTangents(values); foreach (var value in values) writer.Write(value.w); }
            bool colors = read.HasVertexAttribute(VertexAttribute.Color); writer.Write(colors);
            if (colors) { using var values = new NativeArray<Color>(read.vertexCount, Allocator.Temp); read.GetColors(values); foreach (var value in values) { writer.Write(value.r); writer.Write(value.g); writer.Write(value.b); writer.Write(value.a); } }
            writer.Flush(); using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "");
        }
        public static string Report(MGSurfaceWorld world)
        {
            var meshes = world.EnumerateRenderers().Select(r => r.GetComponent<MeshFilter>()).Where(f => f && f.sharedMesh).Select(f => f.sharedMesh).Distinct().ToArray();
            var log = new StringBuilder(); log.AppendLine("Morph carrier compatibility audit (mesh topology and UV correspondence)");
            int families = 0;
            foreach (var group in meshes.GroupBy(Compatibility))
            {
                var first = group.First(); string tier = first.vertexCount <= 300 ? "Small" : first.vertexCount <= 2000 ? "Medium" : first.vertexCount <= 10000 ? "Large" : "Above cap";
                if (group.Count() > 1 && first.vertexCount <= 10000) families++;
                log.AppendLine($"{tier}: {first.vertexCount} vertices; {group.Count()} compatible mesh(es): {string.Join(", ", group.Select(m => m.name))}");
            }
            log.AppendLine($"{meshes.Length} unique meshes; {families} multi-shape families within the 10,000-vertex cap.");
            log.AppendLine("Different topology requires authored vertex correspondence before smooth morphing; vertex-count buckets do not supply it.");
            return log.ToString();
        }
    }
}
