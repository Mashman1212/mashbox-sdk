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
        internal static Mesh BuildPolygon(IList<Vector3> points, out int[] polygons, out Vector3 center)
        {
            if (points == null || points.Count < 3) throw new InvalidOperationException("Place at least three polygon vertices before pressing W.");
            center = Vector3.zero;
            foreach (var p in points)
            {
                if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z)) throw new InvalidOperationException("Polygon vertices must be finite.");
                center += p / points.Count;
            }
            var vertices = new List<Vector3>();
            foreach (var p in points) vertices.Add(p - center);
            var normal = Vector3.zero;
            for (int i = 0; i < vertices.Count; i++) normal += Vector3.Cross(vertices[i], vertices[(i + 1) % vertices.Count]);
            if (normal.sqrMagnitude <= 1e-24f) throw new InvalidOperationException("The polygon outline has no surface area.");
            normal.Normalize();
            var ring = Enumerable.Range(0, vertices.Count).ToList();
            var triangles = new List<int>();
            StudioMeshCuts.TriangulateSliceCap(ring, vertices, normal, triangles);
            var x = Vector3.Cross(Mathf.Abs(normal.y) < .9f ? Vector3.up : Vector3.right, normal).normalized;
            var y = Vector3.Cross(normal, x);
            var uv = vertices.Select(p => new Vector2(Vector3.Dot(p, x), Vector3.Dot(p, y))).ToArray();
            var mesh = new Mesh { name = "Polygon Mesh", indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.uv = uv;
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            polygons = new[] { 0, vertices.Count }.Concat(ring).ToArray();
            return mesh;
        }
        internal int AddPolygon(IList<Vector3> points)
        {
            if (!HasStorage) throw new InvalidOperationException("Save the working model before creating geometry.");
            var mesh = BuildPolygon(points, out var polygons, out var center);
            string name = ObjectNames.GetUniqueName(nodes.Select(n => n.name).ToArray(), "Polygon");
            mesh.name = name + " Mesh";
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Polygon"); Undo.RegisterCompleteObjectUndo(this, "Create Polygon");
            try
            {
                AttachOwnedMesh(mesh);
                nodes.Add(new Node { name = name, position = center, mesh = mesh, materials = new[] { StudioMeshMaterialPreview.DefaultMaterial }, cutPolygons = polygons, cutTriangles = mesh.triangles, geometryVertices = Enumerable.Range(0, mesh.vertexCount).ToArray() });
                EditorUtility.SetDirty(this); EditorUtility.SetDirty(mesh);
                return nodes.Count - 1;
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
            finally { Undo.CollapseUndoOperations(group); }
        }
    }
}
