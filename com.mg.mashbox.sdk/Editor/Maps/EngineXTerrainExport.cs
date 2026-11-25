#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.MapTools
{
    // Explicit draft exporter. Never edits the selected scene, meshes or importers.
    public static class EngineXTerrainExport
    {
        [Serializable] public sealed class Payload
        {
            public string path, semantic, encoding, colorSpace, sha256;
            public long bytes, count;
            public int components, width, height;
        }
        [Serializable] public sealed class SurfaceMesh
        {
            public string id, positions, normals = "", tangents = "", colors = "";
            public string[] uv = { "", "", "", "" };
            public string[] submeshes;
            public float[] bounds;
        }
        [Serializable] public sealed class Tile
        {
            public string id, mesh, control1, control2;
            public float[] localToWorld;
        }
        [Serializable] public sealed class Manifest
        {
            public string format = "EngineX.TerrainWorld";
            public int version = 1;
            public string units = "metres", up = "Y", handedness = "left", winding = "clockwise", byteOrder = "little";
            public string scope = "draft-surface-only";
            public List<Payload> payloads = new List<Payload>();
            public List<SurfaceMesh> meshes = new List<SurfaceMesh>();
            public List<Tile> tiles = new List<Tile>();
        }
        static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        sealed class Writer
        {
            public readonly Manifest manifest = new Manifest();
            readonly string directory;
            readonly long budget;
            long totalBytes;
            public Writer(string directory, long budget) { this.directory = directory; this.budget = budget; }
            public string Buffer(string semantic, string encoding, int components, int count, Action<BinaryWriter> write, int width = 0, int height = 0, string colorSpace = "none")
            {
                long length = checked((long)count * components * (encoding == "u8" ? 1 : 4));
                if (length > 128L * 1024 * 1024 || totalBytes + length > budget || manifest.payloads.Count >= 4096)
                    throw new InvalidOperationException("Selection exceeds the draft reader budget. Export a smaller selection.");
                byte[] bytes;
                using (var memory = new MemoryStream())
                {
                    using (var binary = new BinaryWriter(memory, Encoding.UTF8, true)) write(binary);
                    bytes = memory.ToArray();
                }
                if (bytes.LongLength != length) throw new InvalidDataException("Buffer length mismatch.");
                string path = "p" + manifest.payloads.Count.ToString("D6") + ".bin";
                File.WriteAllBytes(Path.Combine(directory, path), bytes);
                manifest.payloads.Add(new Payload { path = path, semantic = semantic, encoding = encoding, components = components, count = count,
                    width = width, height = height, colorSpace = colorSpace, bytes = length, sha256 = Hash(bytes) });
                totalBytes += length;
                return path;
            }
            static void Float(BinaryWriter writer, float value)
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("Nonfinite authored value.");
                writer.Write(value);
            }
            public string Mesh(Mesh source)
            {
                if (source == null || !source.isReadable || source.vertexCount == 0) throw new InvalidOperationException("A readable, nonempty terrain mesh is required.");
                var result = new SurfaceMesh { id = "mesh-" + manifest.meshes.Count };
                var vertices = source.vertices;
                result.positions = Buffer("positions", "f32", 3, vertices.Length, w => { foreach (var v in vertices) { Float(w,v.x); Float(w,v.y); Float(w,v.z); } });
                var normals = source.normals;
                if (normals.Length != 0) result.normals = Buffer("normals", "f32", 3, vertices.Length, w => { foreach (var v in normals) { Float(w,v.x); Float(w,v.y); Float(w,v.z); } });
                var tangents = source.tangents;
                if (tangents.Length != 0) result.tangents = Buffer("tangents", "f32", 4, vertices.Length, w => { foreach (var v in tangents) { Float(w,v.x); Float(w,v.y); Float(w,v.z); Float(w,v.w); } });
                var colors = source.colors;
                if (colors.Length != 0) result.colors = Buffer("colors", "f32", 4, vertices.Length, w => { foreach (var v in colors) { Float(w,v.r); Float(w,v.g); Float(w,v.b); Float(w,v.a); } });
                for (int channel = 0; channel < 4; ++channel)
                {
                    var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                    if (!source.HasVertexAttribute(attribute)) continue;
                    int dimension = source.GetVertexAttributeDimension(attribute);
                    var values = new List<Vector4>(); source.GetUVs(channel, values);
                    result.uv[channel] = Buffer("uv", "f32", dimension, vertices.Length, w => { foreach (var v in values) for (int i = 0; i < dimension; ++i) Float(w,v[i]); });
                }
                result.submeshes = new string[source.subMeshCount];
                for (int s = 0; s < source.subMeshCount; ++s)
                {
                    if (source.GetTopology(s) != MeshTopology.Triangles) throw new InvalidOperationException("Terrain submeshes must contain triangles.");
                    var indices = source.GetIndices(s, true);
                    result.submeshes[s] = Buffer("indices", "u32", 1, indices.Length, w => { foreach (int i in indices) { if ((uint)i >= vertices.Length) throw new InvalidDataException("Invalid mesh index."); w.Write((uint)i); } });
                }
                var bounds = source.bounds;
                result.bounds = new[] { bounds.min.x, bounds.min.y, bounds.min.z, bounds.max.x, bounds.max.y, bounds.max.z };
                manifest.meshes.Add(result); return result.id;
            }
            public string Control(Texture2D texture)
            {
                if (texture == null) return "";
                if (!texture.isReadable) throw new InvalidOperationException("Control map is not readable. No importer changes were made.");
                // Float RGBA preserves precision of the source's decoded channels.
                var colors = texture.GetPixels(0);
                return Buffer("control", "f32", 4, colors.Length, w => { foreach (var c in colors) { Float(w,c.r); Float(w,c.g); Float(w,c.b); Float(w,c.a); } }, texture.width, texture.height, "linear");
            }
        }
        [MenuItem("MashBox/Export/EngineX Terrain World")]
        public static void ExportSelection()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before exporting.");
            var tiles = Selection.gameObjects.SelectMany(g => g.GetComponentsInChildren<MGTerrain>(true)).Distinct().ToArray();
            if (tiles.Length == 0) { EditorUtility.DisplayDialog("EngineX Terrain World", "Select a terrain tile or terrain world.", "OK"); return; }
            if (!EditorUtility.DisplayDialog("Draft surface export", "This implementation exports surface meshes, transforms and control maps only. Materials, holes authoring data, foliage, lofts and roads are not yet supported. The report will mark the bundle incomplete.", "Export surfaces", "Cancel")) return;
            var parent = EditorUtility.OpenFolderPanel("Export terrain surfaces", "", "");
            if (string.IsNullOrEmpty(parent)) return;
            var destination = Path.Combine(parent, "enginex-terrain-" + Guid.NewGuid().ToString("N"));
            ExportSurfaces(tiles, destination);
            EditorUtility.RevealInFinder(destination);
        }
        public static void ExportSurfaces(IEnumerable<MGTerrain> tiles, string destination)
        {
            destination = Path.GetFullPath(destination);
            var selection = tiles.Distinct().ToArray();
            if (selection.Length == 0) throw new InvalidOperationException("No terrain tiles selected.");
            bool Inside(string root) => (destination + Path.DirectorySeparatorChar).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (Inside("D:/MappyX") || Inside("D:/mashbox-sdk")) throw new IOException("The reference project and SDK are read-only export destinations.");
            if (Inside("D:/EngineX") && (!Inside("D:/EngineX/tests/fixtures/mappy") || selection.Length != 1))
                throw new IOException("EngineX accepts only a single small fixture tile under tests/fixtures/mappy. Real content belongs in PJX.");
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Export destination must not exist.");
            Directory.CreateDirectory(destination);
            try
            {
                var writer = new Writer(destination, (Inside("D:/EngineX") ? 4L : 500L) * 1024 * 1024);
                foreach (var terrain in selection)
                {
                    // GetComponent avoids MGTerrain's lazy ResolveComponents mutation.
                    var filter = terrain.GetComponent<MeshFilter>();
                    var matrix = filter.transform.localToWorldMatrix;
                    var transform = new float[16];
                    for (int row = 0; row < 4; ++row) for (int col = 0; col < 4; ++col) transform[row*4+col] = matrix[row,col];
                    writer.manifest.tiles.Add(new Tile { id = "tile-" + writer.manifest.tiles.Count, mesh = writer.Mesh(filter.sharedMesh),
                        localToWorld = transform, control1 = writer.Control(terrain.ControlMap1), control2 = writer.Control(terrain.ControlMap2) });
                }
                File.WriteAllText(Path.Combine(destination, "manifest.json"), JsonUtility.ToJson(writer.manifest, true), new UTF8Encoding(false));
                var readBack = JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(destination, "manifest.json")));
                foreach (var payload in readBack.payloads)
                {
                    byte[] bytes = File.ReadAllBytes(Path.Combine(destination, payload.path));
                    if (bytes.LongLength != payload.bytes || Hash(bytes) != payload.sha256) throw new InvalidDataException("Export read-back failed: " + payload.path);
                }
                File.WriteAllText(Path.Combine(destination, "validation-report.txt"), "INCOMPLETE PHASE 0 EXPORT\nPayload byte counts and SHA-256 read-back passed.\nMissing: terrain layer materials, grid identity, original hole topology/masks, density layers, prototypes/FBX/PNG, instances, lofts/roads and reference cameras.\nNo behavioural or rendering parity claimed.\n");
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(destination, "validation-report.txt"), "FAILED\n" + error.Message + "\n");
                throw;
            }
        }
    }
}
#endif
