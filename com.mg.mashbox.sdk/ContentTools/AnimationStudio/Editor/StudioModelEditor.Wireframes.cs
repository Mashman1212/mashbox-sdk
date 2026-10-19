using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private sealed class SelectionWireframe
        {
            internal int[] indices;
            internal Vector3[] lines;
            internal Mesh baked;
            internal readonly List<Vector3> vertices = new List<Vector3>();
        }
        private readonly Dictionary<StudioModelDocument.Node, SelectionWireframe> selectionWireframes = new Dictionary<StudioModelDocument.Node, SelectionWireframe>();
        private void ClearSelectionWireframes()
        {
            ClearComponentDrawing();
            foreach (var wire in selectionWireframes.Values) if (wire.baked) UnityEngine.Object.DestroyImmediate(wire.baked);
            selectionWireframes.Clear();
        }
        internal IEnumerable<int> SelectedWireframeNodes()
        {
            if (!Document) yield break;
            // Iterate each node once even if both it and its parent are selected.
            for (int i = 0; i < Transforms.Length; i++)
            {
                if (!Document.nodes[i].mesh || !Transforms[i].gameObject.activeInHierarchy || (!objects.Contains(i) && !HasSelectedParent(i))) continue;
                var renderer = Transforms[i].GetComponent<Renderer>();
                if (renderer && renderer.enabled) yield return i;
            }
        }
        private void DrawSelectedObjectWireframes()
        {
            foreach (int i in SelectedWireframeNodes())
            {
                var lines = SelectionWireframeLines(i);
                if (lines.Length == 0) continue;
                using (new Handles.DrawingScope(Transforms[i].localToWorldMatrix)) Handles.DrawLines(lines);
            }
        }
        // Local-space edge endpoints use the same transform as the renderer, including
        // rotation, nonuniform scale and inherited shear. Bounds are only used for framing.
        internal Vector3[] SelectionWireframeLines(int index)
        {
            var node = Document.nodes[index]; var mesh = node.mesh;
            if (!mesh) return Array.Empty<Vector3>();
            if (!selectionWireframes.TryGetValue(node, out var wire))
            {
                wire = new SelectionWireframe(); mesh.GetVertices(wire.vertices);
                var canonical = new int[wire.vertices.Count]; var positions = new Dictionary<Vector3, int>();
                for (int i = 0; i < canonical.Length; i++)
                {
                    if (node.skinned) { canonical[i] = i; continue; }
                    if (!positions.TryGetValue(wire.vertices[i], out int id)) positions[wire.vertices[i]] = id = positions.Count;
                    canonical[i] = id;
                }
                var edges = new HashSet<(int, int)>(); var indices = new List<int>();
                void Edge(int a, int b)
                {
                    int x = canonical[a], y = canonical[b];
                    if (x == y || !edges.Add((Math.Min(x, y), Math.Max(x, y)))) return;
                    indices.Add(a); indices.Add(b);
                }
                bool triangles = true;
                for (int sub = 0; sub < mesh.subMeshCount; sub++) if (mesh.GetTopology(sub) != MeshTopology.Triangles) triangles = false;
                if (triangles)
                {
                    foreach (var face in new StudioMeshCuts(node).Faces)
                        for (int e = 0; e < face.Vertices.Count; e++) Edge(face.Vertices[e], face.Vertices[(e + 1) % face.Vertices.Count]);
                }
                else for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var topology = mesh.GetTopology(sub); var list = mesh.GetIndices(sub);
                    if (topology == MeshTopology.Triangles || topology == MeshTopology.Quads)
                    {
                        int size = topology == MeshTopology.Quads ? 4 : 3;
                        for (int i = 0; i < list.Length; i += size) for (int j = 0; j < size; j++) Edge(list[i + j], list[i + (j + 1) % size]);
                    }
                    else if (topology == MeshTopology.Lines) for (int i = 0; i + 1 < list.Length; i += 2) Edge(list[i], list[i + 1]);
                    else if (topology == MeshTopology.LineStrip) for (int i = 0; i + 1 < list.Length; i++) Edge(list[i], list[i + 1]);
                }
                wire.indices = indices.ToArray(); wire.lines = new Vector3[indices.Count];
                if (node.skinned) wire.baked = new Mesh { name = "MashBox Studio selection preview", hideFlags = HideFlags.HideAndDontSave };
                else for (int i = 0; i < wire.lines.Length; i++) wire.lines[i] = wire.vertices[wire.indices[i]];
                selectionWireframes.Add(node, wire);
            }
            if (wire.baked)
            {
                Transforms[index].GetComponent<SkinnedMeshRenderer>().BakeMesh(wire.baked);
                wire.baked.GetVertices(wire.vertices);
                for (int i = 0; i < wire.lines.Length; i++) wire.lines[i] = wire.vertices[wire.indices[i]];
            }
            return wire.lines;
        }
    }
}
