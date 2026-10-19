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
            if (AssetDatabase.LoadMainAssetAtPath(path)) throw new InvalidOperationException("Choose a new model filename.");
            var doc = CreateInstance<StudioModelDocument>();
            AssetDatabase.CreateAsset(doc, path); doc.Save(); return doc;
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
            if (!AssetDatabase.Contains(this)) throw new InvalidOperationException("Save the working model before creating geometry.");
            var mesh = BuildPrimitive(type);
            string name = ObjectNames.GetUniqueName(nodes.Select(n => n.name).ToArray(), type.ToString());
            mesh.name = name + " Mesh";
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create " + type);
            Undo.RegisterCompleteObjectUndo(this, "Create " + type);
            AssetDatabase.AddObjectToAsset(mesh, this);
            // Undo removes the node, while its owned mesh stays available for Redo.
            // Destroying a persistent subasset through creation undo loses its asset reference.
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            var material = pipeline ? pipeline.defaultMaterial : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            nodes.Add(new Node { name = name, mesh = mesh, materials = new[] { material } });
            EditorUtility.SetDirty(this); EditorUtility.SetDirty(mesh);
            Undo.CollapseUndoOperations(group);
            return nodes.Count - 1;
        }

        // Generate directly into owned mesh data; never instantiate objects in the gameplay scene.
        internal static Mesh BuildPrimitive(PrimitiveType type)
        {
            var vertices = new List<Vector3>(); var normals = new List<Vector3>();
            var uv = new List<Vector2>(); var triangles = new List<int>();
            void Vertex(Vector3 p, Vector3 n, Vector2 t) { vertices.Add(p); normals.Add(n); uv.Add(t); }
            void Triangle(int a, int b, int c)
            {
                if (Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), normals[a] + normals[b] + normals[c]) < 0)
                { int swap = b; b = c; c = swap; }
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }
            void Quad(Vector3 center, Vector3 right, Vector3 up, Vector3 normal)
            {
                int start = vertices.Count;
                Vertex(center - right - up, normal, new Vector2(0, 0)); Vertex(center + right - up, normal, new Vector2(1, 0));
                Vertex(center + right + up, normal, new Vector2(1, 1)); Vertex(center - right + up, normal, new Vector2(0, 1));
                Triangle(start, start + 1, start + 2); Triangle(start, start + 2, start + 3);
            }
            const int segments = 32, rings = 16;
            switch (type)
            {
                case PrimitiveType.Cube:
                    Quad(Vector3.right * .5f, Vector3.forward * .5f, Vector3.up * .5f, Vector3.right);
                    Quad(Vector3.left * .5f, Vector3.back * .5f, Vector3.up * .5f, Vector3.left);
                    Quad(Vector3.up * .5f, Vector3.right * .5f, Vector3.forward * .5f, Vector3.up);
                    Quad(Vector3.down * .5f, Vector3.right * .5f, Vector3.back * .5f, Vector3.down);
                    Quad(Vector3.forward * .5f, Vector3.right * .5f, Vector3.up * .5f, Vector3.forward);
                    Quad(Vector3.back * .5f, Vector3.left * .5f, Vector3.up * .5f, Vector3.back); break;
                case PrimitiveType.Plane:
                    Quad(Vector3.zero, Vector3.right * .5f, Vector3.forward * .5f, Vector3.up); break;
                case PrimitiveType.Quad:
                    Quad(Vector3.zero, Vector3.right * .5f, Vector3.up * .5f, Vector3.back); break;
                case PrimitiveType.Sphere:
                    for (int y = 0; y <= rings; y++) for (int x = 0; x <= segments; x++)
                    {
                        float v = (float)y / rings, u = (float)x / segments, phi = v * Mathf.PI, theta = x == segments ? 0 : u * 2 * Mathf.PI;
                        var n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                        if (y == 0) n = Vector3.up; else if (y == rings) n = Vector3.down;
                        Vertex(n * .5f, n, new Vector2(u, 1 - v));
                    }
                    for (int y = 0; y < rings; y++) for (int x = 0; x < segments; x++)
                    {
                        int a = y * (segments + 1) + x, b = a + segments + 1;
                        if (y > 0) Triangle(a, a + 1, b);
                        if (y < rings - 1) Triangle(a + 1, b + 1, b);
                    }
                    break;
                case PrimitiveType.Cylinder:
                    for (int x = 0; x <= segments; x++)
                    {
                        float u = (float)x / segments, angle = x == segments ? 0 : u * Mathf.PI * 2;
                        var n = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                        Vertex(n * .5f + Vector3.down, n, new Vector2(u, 0)); Vertex(n * .5f + Vector3.up, n, new Vector2(u, 1));
                    }
                    for (int x = 0; x < segments; x++) { int a = x * 2; Triangle(a, a + 1, a + 2); Triangle(a + 1, a + 3, a + 2); }
                    foreach (float sign in new[] { -1f, 1f })
                    {
                        int center = vertices.Count; var n = Vector3.up * sign;
                        Vertex(n, n, new Vector2(.5f, .5f));
                        for (int x = 0; x <= segments; x++)
                        {
                            float angle = x == segments ? 0 : (float)x / segments * Mathf.PI * 2;
                            var radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * .5f;
                            Vertex(n + radial, n, new Vector2(radial.x + .5f, radial.z + .5f));
                        }
                        for (int x = 0; x < segments; x++) Triangle(center, center + x + 1, center + x + 2);
                    }
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(type), "Unsupported modeling primitive.");
            }
            var mesh = new Mesh { name = type + " Mesh" };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds(); mesh.RecalculateTangents(); return mesh;
        }
    }
}
