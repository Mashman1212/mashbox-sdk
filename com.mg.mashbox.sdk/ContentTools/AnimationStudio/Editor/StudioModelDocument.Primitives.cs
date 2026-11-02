using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioModelDocument
    {
        public static StudioModelDocument CreateEmpty(string path)
        {
            if (System.IO.File.Exists(path) || (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) && AssetDatabase.LoadMainAssetAtPath(path))) throw new InvalidOperationException("Choose a new model filename.");
            var doc = CreateInstance<StudioModelDocument>();
            if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) AssetDatabase.CreateAsset(doc, path); else doc.SetWorkspacePath(path); doc.Save(); return doc;
        }

        public int AddEmptyTransform(int parent = -1, Vector3? position = null)
        {
            if (parent < -1 || parent >= nodes.Count) throw new ArgumentOutOfRangeException(nameof(parent));
            string name = ObjectNames.GetUniqueName(nodes.Where(n => n.parent == parent).Select(n => n.name).ToArray(), "Empty");
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create empty transform");
            Undo.RegisterCompleteObjectUndo(this, "Create empty transform");
            nodes.Add(new Node { name = name, parent = parent, position = position ?? Vector3.zero });
            EditorUtility.SetDirty(this);
            Undo.CollapseUndoOperations(group);
            return nodes.Count - 1;
        }

        public int AddPrimitive(PrimitiveType type)
        {
            if (!HasStorage) throw new InvalidOperationException("Save the working model before creating geometry.");
            var parameters = CreationPrimitiveParameters(type, sceneUnits);
            var mesh = BuildPrimitive(type, parameters, out var polygons);
            string name = ObjectNames.GetUniqueName(nodes.Select(n => n.name).ToArray(), type.ToString());
            mesh.name = name + " Mesh";
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create " + type);
            Undo.RegisterCompleteObjectUndo(this, "Create " + type);
            AttachOwnedMesh(mesh);
            // Undo removes the node, while its owned mesh stays available for Redo.
            // Destroying a persistent subasset through creation undo loses its asset reference.
            var material = StudioMeshMaterialPreview.DefaultMaterial;
            var node = new Node { name = name, mesh = mesh, materials = new[] { material }, cutPolygons = polygons, cutTriangles = mesh.triangles, geometryVertices = StudioSubdivisionData.Primitive(mesh) };
            var primitive = new StudioModifierOperation { kind = "Primitive", name = type.ToString(), primitiveType = type, parameters = parameters, cubeDivisions = type == PrimitiveType.Cube ? Vector3Int.one : Vector3Int.zero };
            primitive.Bind(node);
            node.modifiers = new StudioModifierHistory { source = StudioModifierSource.Capture(node), operations = new List<StudioModifierOperation> { primitive } };
            nodes.Add(node);
            EditorUtility.SetDirty(this); EditorUtility.SetDirty(mesh);
            Undo.CollapseUndoOperations(group);
            return nodes.Count - 1;
        }

        // Generate directly into owned mesh data; never instantiate objects in the gameplay scene.
        internal static Mesh BuildPrimitive(PrimitiveType type) => BuildPrimitive(type, out _);
        internal static Vector4 DefaultPrimitiveParameters(PrimitiveType type) => type switch
        {
            PrimitiveType.Cube => new Vector4(1, 1, 1, 1),
            PrimitiveType.Sphere => new Vector4(.5f, 32, 16, 0),
            PrimitiveType.Plane or PrimitiveType.Quad => new Vector4(1, 1, 1, 1),
            PrimitiveType.Cylinder => new Vector4(.5f, 2, 32, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(type), "Unsupported modeling primitive.")
        };
        // Creation-only conversion. The shared builders also serve vehicle templates
        // and must retain their fixed meter-based contract.
        internal static Vector4 CreationPrimitiveParameters(PrimitiveType type, StudioLengthUnit unit)
        {
            var parameters = DefaultPrimitiveParameters(type);
            float factor = (float)StudioSceneUnits.MetersPerUnit(unit);
            parameters.x *= factor;
            if (type != PrimitiveType.Sphere) parameters.y *= factor;
            if (type == PrimitiveType.Cube) parameters.z *= factor;
            return parameters;
        }
        internal static Mesh BuildPrimitive(PrimitiveType type, out int[] polygons) => BuildPrimitive(type, DefaultPrimitiveParameters(type), out polygons);
        internal static Vector3Int ResolveCubeDivisions(Vector3Int divisions, float legacyCount)
        {
            int fallback = Mathf.Clamp(Mathf.RoundToInt(legacyCount), 1, 64);
            int Axis(int value) => value <= 0 ? fallback : Mathf.Clamp(value, 1, 64);
            return new Vector3Int(Axis(divisions.x), Axis(divisions.y), Axis(divisions.z));
        }
        internal static Mesh BuildPrimitive(PrimitiveType type, Vector4 parameters, out int[] polygons, Vector3Int cubeDivisions = default)
        {
            foreach (float value in new[] { parameters.x, parameters.y, parameters.z, parameters.w })
                if (float.IsNaN(value) || float.IsInfinity(value) || float.IsInfinity(value * value))
                    throw new ArgumentException("Primitive parameters must be finite.");
            float Dimension(float value) => value >= .0001f ? value : throw new ArgumentException("Primitive dimensions must be at least 0.0001.");
            int Count(float value, int min, int max) => Mathf.Clamp(Mathf.RoundToInt(value), min, max);
            var vertices = new List<Vector3>(); var normals = new List<Vector3>();
            var uv = new List<Vector2>(); var triangles = new List<int>(); var faces = new List<int>();
            void Vertex(Vector3 p, Vector3 n, Vector2 t) { vertices.Add(p); normals.Add(n); uv.Add(t); }
            void Triangle(int a, int b, int c, bool record = true)
            {
                if (Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), normals[a] + normals[b] + normals[c]) < 0)
                { int swap = b; b = c; c = swap; }
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                if (record) { faces.Add(0); faces.Add(3); faces.Add(a); faces.Add(b); faces.Add(c); }
            }
            void RecordQuad(int a, int b, int c, int d)
            {
                if (Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), normals[a] + normals[b] + normals[c]) < 0)
                { int swap = b; b = d; d = swap; }
                faces.Add(0); faces.Add(4); faces.Add(a); faces.Add(b); faces.Add(c); faces.Add(d);
            }
            void Grid(Vector3 center, Vector3 right, Vector3 up, Vector3 normal, int columns, int rows)
            {
                int start = vertices.Count;
                for (int y = 0; y <= rows; y++) for (int x = 0; x <= columns; x++)
                {
                    float u = (float)x / columns, v = (float)y / rows;
                    Vertex(center + right * ((2 * x - columns) / (float)columns) + up * ((2 * y - rows) / (float)rows), normal, new Vector2(u, v));
                }
                for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
                {
                    int a = start + y * (columns + 1) + x, b = a + columns + 1;
                    Triangle(a, a + 1, b + 1, false); Triangle(a, b + 1, b, false);
                    RecordQuad(a, a + 1, b + 1, b);
                }
            }
            var p = parameters;
            switch (type)
            {
                case PrimitiveType.Cube:
                {
                    float x = Dimension(p.x) * .5f, y = Dimension(p.y) * .5f, z = Dimension(p.z) * .5f;
                    var divisions = ResolveCubeDivisions(cubeDivisions, p.w);
                    Grid(Vector3.right * x, Vector3.forward * z, Vector3.up * y, Vector3.right, divisions.z, divisions.y);
                    Grid(Vector3.left * x, Vector3.back * z, Vector3.up * y, Vector3.left, divisions.z, divisions.y);
                    Grid(Vector3.up * y, Vector3.right * x, Vector3.forward * z, Vector3.up, divisions.x, divisions.z);
                    Grid(Vector3.down * y, Vector3.right * x, Vector3.back * z, Vector3.down, divisions.x, divisions.z);
                    Grid(Vector3.forward * z, Vector3.right * x, Vector3.up * y, Vector3.forward, divisions.x, divisions.y);
                    Grid(Vector3.back * z, Vector3.left * x, Vector3.up * y, Vector3.back, divisions.x, divisions.y);
                    break;
                }
                case PrimitiveType.Plane:
                case PrimitiveType.Quad:
                    Grid(Vector3.zero, Vector3.right * (Dimension(p.x) * .5f),
                        (type == PrimitiveType.Plane ? Vector3.forward : Vector3.up) * (Dimension(p.y) * .5f),
                        type == PrimitiveType.Plane ? Vector3.up : Vector3.back, Count(p.z, 1, 128), Count(p.w, 1, 128));
                    break;
                case PrimitiveType.Sphere:
                {
                    float radius = Dimension(p.x); int segments = Count(p.y, 3, 128), rings = Count(p.z, 2, 128);
                    for (int y = 0; y <= rings; y++) for (int x = 0; x <= segments; x++)
                    {
                        float v = (float)y / rings, u = (float)x / segments, phi = v * Mathf.PI, theta = x == segments ? 0 : u * 2 * Mathf.PI;
                        var n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                        if (y == 0) n = Vector3.up; else if (y == rings) n = Vector3.down;
                        Vertex(n * radius, n, new Vector2(u, 1 - v));
                    }
                    for (int y = 0; y < rings; y++) for (int x = 0; x < segments; x++)
                    {
                        int a = y * (segments + 1) + x, b = a + segments + 1;
                        if (y > 0) Triangle(a, a + 1, b, y == rings - 1);
                        if (y < rings - 1) Triangle(a + 1, b + 1, b, y == 0);
                        if (y > 0 && y < rings - 1) RecordQuad(a, a + 1, b + 1, b);
                    }
                    break;
                }
                case PrimitiveType.Cylinder:
                {
                    float radius = Dimension(p.x), halfHeight = Dimension(p.y) * .5f;
                    int segments = Count(p.z, 3, 128), capRings = Count(p.w, 1, 64);
                    for (int x = 0; x <= segments; x++)
                    {
                        float u = (float)x / segments, angle = x == segments ? 0 : u * Mathf.PI * 2;
                        var n = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                        Vertex(n * radius + Vector3.down * halfHeight, n, new Vector2(u, 0));
                        Vertex(n * radius + Vector3.up * halfHeight, n, new Vector2(u, 1));
                    }
                    for (int x = 0; x < segments; x++)
                    {
                        int a = x * 2; Triangle(a, a + 1, a + 2, false); Triangle(a + 1, a + 3, a + 2, false);
                        RecordQuad(a, a + 1, a + 3, a + 2);
                    }
                    foreach (float sign in new[] { -1f, 1f })
                    {
                        int center = vertices.Count; var n = Vector3.up * sign;
                        Vertex(n * halfHeight, n, new Vector2(.5f, .5f));
                        for (int ring = 1; ring <= capRings; ring++) for (int x = 0; x <= segments; x++)
                        {
                            float angle = x == segments ? 0 : (float)x / segments * Mathf.PI * 2;
                            var unit = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * ((float)ring / capRings);
                            var point = ring == capRings ? vertices[x * 2 + (sign > 0 ? 1 : 0)] : n * halfHeight + unit * radius;
                            Vertex(point, n, new Vector2(unit.x * .5f + .5f, unit.z * .5f + .5f));
                        }
                        for (int x = 0; x < segments; x++) Triangle(center, center + x + 1, center + x + 2);
                        for (int ring = 1; ring < capRings; ring++) for (int x = 0; x < segments; x++)
                        {
                            int a = center + 1 + (ring - 1) * (segments + 1) + x, b = a + segments + 1;
                            Triangle(a, b, b + 1, false); Triangle(a, b + 1, a + 1, false);
                            RecordQuad(a, b, b + 1, a + 1);
                        }
                    }
                    break;
                }
                default: throw new ArgumentOutOfRangeException(nameof(type), "Unsupported modeling primitive.");
            }
            var mesh = new Mesh { name = type + " Mesh" };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds(); mesh.RecalculateTangents(); polygons = faces.ToArray(); return mesh;
        }
    }
}
