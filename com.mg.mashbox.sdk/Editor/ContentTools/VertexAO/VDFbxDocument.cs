using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

// A narrow binary FBX editor. Unchanged property payloads (including compressed arrays)
// stay byte-for-byte intact; only color layers and absolute node offsets are rewritten.
internal sealed class VDFbxDocument
{
    internal sealed class Node
    {
        internal string name;
        internal byte[] properties;
        internal ulong count;
        internal bool hasScope;
        internal readonly List<Node> children = new List<Node>();
        internal Node Child(string id) => children.FirstOrDefault(n => n.name == id);
    }
    internal readonly List<Node> roots = new List<Node>();
    byte[] header, footer;
    bool wide;
    int sentinel => wide ? 25 : 13;
    internal IEnumerable<Node> Geometries => roots.First(n => n.name == "Objects").children
        .Where(n => n.name == "Geometry" && n.Child("PolygonVertexIndex") != null);

    internal static VDFbxDocument Read(string path)
    {
        var doc = new VDFbxDocument();
        using (var reader = new BinaryReader(File.OpenRead(path)))
        {
            doc.header = reader.ReadBytes(27);
            if (doc.header.Length != 27 || Encoding.ASCII.GetString(doc.header, 0, 21) != "Kaydara FBX Binary  \0")
                throw new NotSupportedException("FBX overwrite currently supports binary FBX 7.x. Re-export ASCII FBX as binary first.");
            uint version = BitConverter.ToUInt32(doc.header, 23);
            if (version < 7000 || version > 7700) throw new NotSupportedException("Unsupported binary FBX version: " + version);
            doc.wide = version >= 7500;
            Node node;
            while ((node = doc.ReadNode(reader, 0)) != null) doc.roots.Add(node);
            doc.footer = reader.ReadBytes(checked((int)(reader.BaseStream.Length - reader.BaseStream.Position)));
        }
        return doc;
    }

    Node ReadNode(BinaryReader r, int depth)
    {
        if (depth > 128) throw new InvalidDataException("FBX nesting exceeds 128 levels.");
        ulong end = wide ? r.ReadUInt64() : r.ReadUInt32();
        ulong count = wide ? r.ReadUInt64() : r.ReadUInt32();
        ulong bytes = wide ? r.ReadUInt64() : r.ReadUInt32();
        byte length = r.ReadByte();
        if (end == 0) return null;
        if (end > (ulong)r.BaseStream.Length || bytes > int.MaxValue || end <= (ulong)r.BaseStream.Position)
            throw new InvalidDataException("Invalid FBX node bounds.");
        var n = new Node { name = Encoding.UTF8.GetString(r.ReadBytes(length)), count = count, properties = r.ReadBytes((int)bytes) };
        if ((ulong)r.BaseStream.Position < end)
        {
            n.hasScope = true;
            while ((ulong)r.BaseStream.Position < end)
            {
                var child = ReadNode(r, depth + 1);
                if (child == null) break;
                n.children.Add(child);
            }
        }
        if ((ulong)r.BaseStream.Position != end) throw new InvalidDataException("FBX node length mismatch.");
        return n;
    }

    internal void Write(string path)
    {
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write(header);
            foreach (var n in roots) WriteNode(w, n);
            w.Write(new byte[sentinel]);
            w.Write(footer);
        }
    }
    void WriteNode(BinaryWriter w, Node n)
    {
        long start = w.BaseStream.Position;
        w.Write(new byte[sentinel - 1]);
        byte[] name = Encoding.UTF8.GetBytes(n.name);
        w.Write(checked((byte)name.Length)); w.Write(name); w.Write(n.properties);
        foreach (var child in n.children) WriteNode(w, child);
        if (n.hasScope || n.children.Count > 0) w.Write(new byte[sentinel]);
        long end = w.BaseStream.Position;
        w.BaseStream.Position = start;
        if (wide) { w.Write((ulong)end); w.Write(n.count); w.Write((ulong)n.properties.Length); }
        else { w.Write(checked((uint)end)); w.Write(checked((uint)n.count)); w.Write(checked((uint)n.properties.Length)); }
        w.BaseStream.Position = end;
    }

    internal static int CornerCount(Node geometry)
    {
        byte[] props = geometry.Child("PolygonVertexIndex").properties;
        if (props.Length < 13 || props[0] != (byte)'i') throw new InvalidDataException("Expected FBX polygon indices.");
        return checked((int)BitConverter.ToUInt32(props, 1));
    }
    internal static long GeometryId(Node n)
    {
        if (n.properties[0] != (byte)'L') throw new InvalidDataException("Expected FBX object ID.");
        return BitConverter.ToInt64(n.properties, 1);
    }

    static Node Value(string name, object value)
    {
        using (var stream = new MemoryStream()) using (var w = new BinaryWriter(stream))
        {
            if (value is int integer) { w.Write((byte)'I'); w.Write(integer); }
            else if (value is string text) { byte[] bytes = Encoding.UTF8.GetBytes(text); w.Write((byte)'S'); w.Write(bytes.Length); w.Write(bytes); }
            else if (value is double[] array)
            {
                w.Write((byte)'d'); w.Write(array.Length); w.Write(0); w.Write(checked(array.Length * 8));
                foreach (double d in array) w.Write(d);
            }
            else throw new ArgumentException("Unsupported FBX property type.");
            return new Node { name = name, properties = stream.ToArray(), count = 1 };
        }
    }

    internal static void SetColors(Node geometry, Color32[] colors)
    {
        if (colors.Length != CornerCount(geometry)) throw new InvalidDataException("FBX corner count changed.");
        var existing = geometry.children.Where(n => n.name == "LayerElementColor").ToArray();
        int index = existing.Length == 0 ? 0 : BitConverter.ToInt32(existing[0].properties, 1);
        // Replace only the first color set; keep secondary sets and all other FBX data.
        if (existing.Length > 0) geometry.children.Remove(existing[0]);
        var layer = Value("LayerElementColor", index);
        layer.children.Add(Value("Version", 101)); layer.children.Add(Value("Name", "VertexDirt"));
        layer.children.Add(Value("MappingInformationType", "ByPolygonVertex"));
        layer.children.Add(Value("ReferenceInformationType", "Direct"));
        var array = new double[colors.Length * 4];
        for (int i = 0; i < colors.Length; i++)
        {
            array[i * 4] = colors[i].r / 255.0; array[i * 4 + 1] = colors[i].g / 255.0;
            array[i * 4 + 2] = colors[i].b / 255.0; array[i * 4 + 3] = colors[i].a / 255.0;
        }
        layer.children.Add(Value("Colors", array)); geometry.children.Add(layer);
        var baseLayer = geometry.children.FirstOrDefault(n => n.name == "Layer" && BitConverter.ToInt32(n.properties, 1) == 0);
        if (baseLayer == null)
        {
            baseLayer = Value("Layer", 0); baseLayer.children.Add(Value("Version", 100)); geometry.children.Add(baseLayer);
        }
        baseLayer.children.RemoveAll(n => n.name == "LayerElement" && n.Child("Type") != null
            && StringValue(n.Child("Type")) == "LayerElementColor");
        var reference = new Node { name = "LayerElement", count = 0, properties = Array.Empty<byte>() };
        reference.children.Add(Value("Type", "LayerElementColor")); reference.children.Add(Value("TypedIndex", index));
        baseLayer.children.Add(reference);
    }
    static string StringValue(Node n) => Encoding.UTF8.GetString(n.properties, 5, BitConverter.ToInt32(n.properties, 1));

    // Tests compare every non-color payload, which includes hierarchy, geometry, skinning,
    // animations, material links and texture references. Offsets are intentionally excluded.
    internal string NonColorFingerprint()
    {
        using (var stream = new MemoryStream()) using (var w = new BinaryWriter(stream))
        {
            foreach (var n in roots) Fingerprint(w, n);
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(stream.ToArray()));
        }
    }
    static void Fingerprint(BinaryWriter w, Node n)
    {
        if (n.name == "LayerElementColor") return;
        if (n.name == "LayerElement" && n.Child("Type") != null && StringValue(n.Child("Type")) == "LayerElementColor") return;
        if (n.name == "Layer" && !n.children.Any(c => c.name == "LayerElement" && c.Child("Type") != null && StringValue(c.Child("Type")) != "LayerElementColor")) return;
        w.Write(n.name); w.Write(n.count); w.Write(n.properties.Length); w.Write(n.properties);
        foreach (var c in n.children) Fingerprint(w, c);
        w.Write((byte)0);
    }
}
