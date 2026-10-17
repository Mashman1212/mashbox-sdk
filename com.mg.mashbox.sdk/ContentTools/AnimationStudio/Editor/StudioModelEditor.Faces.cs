using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private StudioModelDocument.Node faceNode;
        private List<StudioMeshCuts.Face> cachedFaces;
        private List<int>[] cachedFaceTriangles;

        private List<StudioMeshCuts.Face> Faces()
        {
            var node = Document.nodes[Active];
            if (faceNode != node || cachedFaces == null)
            {
                cachedFaces = new StudioMeshCuts(node).Faces;
                faceNode = node;
                CacheFaceTriangles(node.mesh);
            }
            return cachedFaces;
        }

        private void CacheFaceTriangles(Mesh mesh)
        {
            cachedFaceTriangles = new List<int>[cachedFaces.Count];
            var memberships = new Dictionary<int, List<int>>();
            for (int f = 0; f < cachedFaces.Count; f++)
            {
                cachedFaceTriangles[f] = new List<int>();
                foreach (int vertex in cachedFaces[f].Vertices)
                {
                    if (!memberships.TryGetValue(vertex, out var faces)) memberships[vertex] = faces = new List<int>();
                    faces.Add(f);
                }
            }
            // Use the mesh's actual diagonal for hit tests and fill, including warped quads.
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var triangles = mesh.GetTriangles(sub);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    if (!memberships.TryGetValue(triangles[i], out var candidates)) continue;
                    foreach (int f in candidates)
                    {
                        var face = cachedFaces[f];
                        if (face.Submesh != sub || !face.Vertices.Contains(triangles[i + 1]) || !face.Vertices.Contains(triangles[i + 2])) continue;
                        cachedFaceTriangles[f].Add(triangles[i]); cachedFaceTriangles[f].Add(triangles[i + 1]); cachedFaceTriangles[f].Add(triangles[i + 2]);
                        break;
                    }
                }
            }
        }

        private void PreserveFaceTopology()
        {
            var node = Document.nodes[Active];
            var triangles = ActiveMesh.triangles;
            if (node.cutPolygons != null && node.cutTriangles != null && node.cutTriangles.SequenceEqual(triangles)) return;
            // Retain inferred quad boundaries before moving vertices makes them non-planar.
            // Save and Undo/Redo must restore the same face identities as the live drag.
            Undo.RegisterCompleteObjectUndo(Document, "Transform mesh components");
            node.cutPolygons = Faces().SelectMany(f => new[] { f.Submesh, f.Vertices.Count }.Concat(f.Vertices)).ToArray();
            node.cutTriangles = triangles;
        }

        private static Vector3 FaceCenter(StudioMeshCuts.Face face, Vector3[] vertices)
        {
            var center = Vector3.zero;
            foreach (int i in face.Vertices) center += vertices[i];
            return center / face.Vertices.Count;
        }

        private static bool RayFace(Ray ray, List<int> triangles, Vector3[] vertices, Transform transform, out float distance)
        {
            distance = float.PositiveInfinity;
            for (int i = 0; i < triangles.Count; i += 3)
                if (RayTriangle(ray, transform.TransformPoint(vertices[triangles[i]]), transform.TransformPoint(vertices[triangles[i + 1]]),
                    transform.TransformPoint(vertices[triangles[i + 2]]), out float hit) && hit < distance) distance = hit;
            return !float.IsPositiveInfinity(distance);
        }
    }
}
