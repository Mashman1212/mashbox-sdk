using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    // Versioned UTF-8 JSON. Numeric geometry is text, never a native mesh dump or base64 blob.
    internal static partial class StudioWorkspaceText
    {
        private const string Format = "MashBoxStudio.Workspace";
        private const int Version = 1;
        [Serializable] private sealed class FileData
        {
            public string format;
            public int version;
            public string source;
            public StudioLightingSettings lighting;
            public NodeData[] nodes;
            public MeshData[] meshes;
            public MeshInstance[] instances;
            public MaterialReference[] materials;
            public string resources;
        }
        [Serializable] private sealed class MeshInstance { public int data; public string name; }
        [Serializable] private sealed class NodeData
        {
            public string name, anchor, source;
            public int parent = -1, mesh = -1, siblingOrder = -1;
            public Vector3 position, scale, origin;
            public Quaternion rotation, axes;
            public float[] rotationChannels;
            public bool active, skinned, ao;
            public int slice, rootBone;
            public int[] bones, materials, polygons, topologyTriangles, normalGroups;
            public BlendData blend;
        }
        [Serializable] private sealed class BlendData
        {
            public ShapeData[] shapes;
            public float[] weights;
            public bool sequential;
            public float master;
        }
        [Serializable] private sealed class ShapeData
        {
            public string name;
            public float[] positions, normals, tangents, uv, ao;
        }

        internal static bool IsTextWorkspace(string path)
        {
            using (var reader = new StreamReader(path, Encoding.UTF8, true))
            { int c; do { c = reader.Read(); } while (c >= 0 && char.IsWhiteSpace((char)c)); return c == '{'; }
        }

        internal static void Write(StudioModelDocument document, string path)
        {
            var meshIds = new Dictionary<Mesh, int>();
            var materialIds = new Dictionary<Material, int>();
            var materials = new List<Material>();
            var meshes = new List<MeshData>();
            var instances = new List<MeshInstance>();
            var geometry = new Dictionary<string, int>();
            int MeshId(Mesh mesh)
            {
                if (!mesh) return -1;
                if (meshIds.TryGetValue(mesh, out int id)) return id;
                var data = CaptureMesh(mesh);
                string hash;
                using (var sha = SHA256.Create()) hash = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(data))));
                if (!geometry.TryGetValue(hash, out int payload)) { payload = meshes.Count; meshes.Add(data); geometry.Add(hash, payload); }
                id = instances.Count; instances.Add(new MeshInstance { data = payload, name = mesh.name }); meshIds.Add(mesh, id); return id;
            }
            int MaterialId(Material material)
            {
                if (!material) return -1;
                if (materialIds.TryGetValue(material, out int id)) return id;
                id = materials.Count; materials.Add(material); materialIds.Add(material, id); return id;
            }
            var data = new FileData { format = Format, version = Version, source = document.sourcePath, lighting = document.lighting,
                nodes = document.nodes.Select(node => new NodeData {
                    name = node.name, anchor = node.anchorType, source = node.sourcePath, parent = node.parent, siblingOrder = node.siblingOrder,
                    position = node.position, rotation = node.rotation, scale = node.scale, origin = node.positionOffset, axes = node.pivotRotation,
                    rotationChannels = new[] { node.RotationChannels.x, node.RotationChannels.y, node.RotationChannels.z },
                    active = node.active, skinned = node.skinned, ao = node.vertexAOBaked, slice = node.previewTextureSlice,
                    bones = node.bones, rootBone = node.rootBone, mesh = MeshId(node.mesh),
                    materials = (node.materials ?? Array.Empty<Material>()).Select(MaterialId).ToArray(), polygons = node.cutPolygons, normalGroups = node.normalGroups,
                    // A matching triangle cache is reconstructed from the saved mesh instead of written twice.
                    topologyTriangles = node.cutTriangles != null && node.mesh && node.cutTriangles.SequenceEqual(MeshTriangles(node.mesh)) ? null : node.cutTriangles,
                    blend = CaptureBlend(node.blendShape)
                }).ToArray(), meshes = meshes.ToArray(), instances = instances.ToArray() };
            data.materials = CaptureMaterials(materials, path, out data.resources);
            string text = CompactNumbers(JsonUtility.ToJson(data, false));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            { writer.Write(text); writer.Write('\n'); writer.Flush(); stream.Flush(true); }
        }

        // JsonUtility prints Single values as long double decimals. Round-trip Single
        // notation preserves the exact float while removing those redundant digits.
        private static string CompactNumbers(string json)
        {
            var result = new StringBuilder(json.Length); bool quoted = false, escaped = false;
            for (int i = 0; i < json.Length;)
            {
                char c = json[i];
                if (quoted)
                {
                    result.Append(c); i++;
                    if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '"') quoted = false;
                }
                else if (c == '"') { quoted = true; result.Append(c); i++; }
                else if (c == '-' || c >= '0' && c <= '9')
                {
                    int start = i++; bool real = false;
                    while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '.' || json[i] == 'e' || json[i] == 'E' || json[i] == '+' || json[i] == '-'))
                    { real |= json[i] == '.' || json[i] == 'e' || json[i] == 'E'; i++; }
                    if (real)
                    {
                        if (!float.TryParse(json.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out float number) || float.IsNaN(number) || float.IsInfinity(number))
                            throw new InvalidDataException("Cannot save non-finite workspace values.");
                        result.Append(number.ToString("R", CultureInfo.InvariantCulture));
                    }
                    else result.Append(json, start, i - start); // Preserve 64-bit asset IDs exactly.
                }
                else { result.Append(c); i++; }
            }
            return result.ToString();
        }

        internal static StudioModelDocument Read(string path)
        {
            var owned = new List<Object>();
            try
            {
                var data = JsonUtility.FromJson<FileData>(File.ReadAllText(path, Encoding.UTF8));
                if (data == null || data.format != Format || data.version != Version)
                    throw new InvalidDataException("Unsupported MashBox Studio workspace format/version. The file was not changed.");
                if (data.nodes == null || data.meshes == null || data.instances == null || data.materials == null)
                    throw new InvalidDataException("The workspace is missing required data tables.");
                var materials = RestoreMaterials(data.materials, data.resources, path, owned);
                var instances = data.instances.Select(instance => {
                    if (instance == null || instance.data < 0 || instance.data >= data.meshes.Length) throw new InvalidDataException("Invalid workspace mesh reference.");
                    var mesh = RestoreMesh(data.meshes[instance.data]); owned.Add(mesh); mesh.name = instance.name; return mesh;
                }).ToArray();
                var document = ScriptableObject.CreateInstance<StudioModelDocument>(); owned.Add(document);
                document.sourcePath = data.source; document.lighting = data.lighting ?? new StudioLightingSettings();
                for (int i = 0; i < data.nodes.Length; i++)
                {
                    var node = data.nodes[i];
                    if (node == null || node.parent < -1 || node.parent >= data.nodes.Length || node.parent == i || node.mesh < -1 || node.mesh >= instances.Length)
                        throw new InvalidDataException("Invalid workspace node or parent reference.");
                    int parent = node.parent, steps = 0;
                    while (parent >= 0)
                    {
                        if (parent >= data.nodes.Length || data.nodes[parent] == null || ++steps > data.nodes.Length) throw new InvalidDataException("Invalid or cyclic workspace hierarchy.");
                        parent = data.nodes[parent].parent;
                    }
                    if (node.rootBone < -1 || node.rootBone >= data.nodes.Length || (node.bones ?? Array.Empty<int>()).Any(b => b < -1 || b >= data.nodes.Length))
                        throw new InvalidDataException("Invalid workspace skin bone reference.");
                    Material MaterialAt(int id) { if (id < -1 || id >= materials.Length) throw new InvalidDataException("Invalid material reference."); return id < 0 ? null : materials[id]; }
                    var mesh = node.mesh < 0 ? null : instances[node.mesh];
                    var polygons = node.polygons != null && node.polygons.Length > 0 ? node.polygons : null;
                    var triangles = node.topologyTriangles != null && node.topologyTriangles.Length > 0 ? node.topologyTriangles : polygons != null && mesh ? mesh.triangles : null;
                    ValidatePolygons(polygons, mesh);
                    // JsonUtility restores an absent optional array as empty.
                    var groups = node.normalGroups != null && node.normalGroups.Length > 0 ? node.normalGroups : null;
                    if (groups != null && (!mesh || groups.Length != mesh.vertexCount || groups.Any(g => g < 0 || g >= mesh.vertexCount)))
                        throw new InvalidDataException("Invalid normal smoothing groups.");
                    document.nodes.Add(new StudioModelDocument.Node {
                        name = node.name, anchorType = node.anchor, sourcePath = node.source, parent = node.parent, siblingOrder = node.siblingOrder,
                        position = node.position, rotation = node.rotation, scale = node.scale, positionOffset = node.origin, pivotRotation = node.axes,
                        active = node.active, skinned = node.skinned, vertexAOBaked = node.ao, previewTextureSlice = node.slice,
                        bones = node.bones ?? Array.Empty<int>(), rootBone = node.rootBone, mesh = mesh,
                        materials = (node.materials ?? Array.Empty<int>()).Select(MaterialAt).ToArray(),
                        cutPolygons = polygons, cutTriangles = triangles, normalGroups = groups, blendShape = RestoreBlend(node.blend)
                    });
                    if (node.rotationChannels != null && node.rotationChannels.Length > 0)
                    {
                        if (node.rotationChannels.Length != 3 || node.rotationChannels.Any(v => float.IsNaN(v) || float.IsInfinity(v)))
                            throw new InvalidDataException("Invalid rotation channel values.");
                        var angles = new Vector3(node.rotationChannels[0], node.rotationChannels[1], node.rotationChannels[2]);
                        if (Quaternion.Angle(Quaternion.Euler(angles), node.rotation) > .01f)
                            throw new InvalidDataException("Rotation channels do not match the saved orientation.");
                        document.nodes[document.nodes.Count - 1].RotationChannels = angles;
                        document.nodes[document.nodes.Count - 1].rotation = node.rotation;
                    }
                }
                document.SetWorkspacePath(path);
                // Reconstructing meshes marks native objects dirty; these values
                // came from disk and do not represent unsaved user edits.
                UnityEditor.EditorUtility.ClearDirty(document);
                foreach (var mesh in instances) UnityEditor.EditorUtility.ClearDirty(mesh);
                return document;
            }
            catch (Exception error)
            {
                foreach (var value in owned) if (value && !UnityEditor.EditorUtility.IsPersistent(value)) Object.DestroyImmediate(value);
                if (error is InvalidDataException) throw;
                throw new InvalidDataException("Could not read the MashBox Studio workspace. The file was not changed.", error);
            }
        }

        private static void ValidatePolygons(int[] values, Mesh mesh)
        {
            if (values == null) return;
            for (int i = 0; i < values.Length;)
            {
                if (!mesh || i + 2 > values.Length) throw new InvalidDataException("Invalid polygon data.");
                int sub = values[i++], count = values[i++];
                if (sub < 0 || sub >= mesh.subMeshCount || count < 3 || count > values.Length - i) throw new InvalidDataException("Invalid polygon boundary.");
                for (int end = i + count; i < end; i++) if (values[i] < 0 || values[i] >= mesh.vertexCount) throw new InvalidDataException("Invalid polygon vertex.");
            }
        }
        private static BlendData CaptureBlend(StudioBlendShape blend) => blend == null ? null : new BlendData {
            weights = blend.weights, master = blend.master, sequential = blend.sequential,
            shapes = blend.shapes.Select(s => new ShapeData { name = s.name, positions = Pack(s.positions), normals = Pack(s.normals), tangents = Pack(s.tangents), uv = Pack(s.uv), ao = s.vertexAO }).ToArray()
        };
        private static StudioBlendShape RestoreBlend(BlendData blend) => blend == null || blend.shapes == null || blend.shapes.Length == 0 ? null : new StudioBlendShape {
            weights = blend.weights, master = blend.master, sequential = blend.sequential,
            shapes = blend.shapes.Select(s => new StudioBlendShape.Shape { name = s.name, positions = V3(s.positions), normals = V3(s.normals), tangents = V4(s.tangents), uv = V2(s.uv), vertexAO = s.ao }).ToArray()
        };
    }
}
