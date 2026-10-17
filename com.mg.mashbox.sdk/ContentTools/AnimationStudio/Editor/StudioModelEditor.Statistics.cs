using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        // Rebuilt only after edits/Undo/load, never by reading mesh buffers on every repaint.
        private readonly Dictionary<StudioModelDocument.Node, long[]> statisticsCache = new Dictionary<StudioModelDocument.Node, long[]>();
        private static readonly string[] StatisticsLabels = { "Verts", "Edges", "Faces", "Tris", "UVs" };
        internal (long[] scene, long[] selected) MeshStatistics()
        {
            var scene = new long[5]; var selected = new long[5];
            if (!Document) return (scene, selected);
            for (int i = 0; i < Document.nodes.Count && i < Transforms.Length; i++)
            {
                var node = Document.nodes[i];
                if (!node.mesh || !Transforms[i].gameObject.activeInHierarchy) continue;
                var renderer = Transforms[i].GetComponent<Renderer>();
                if (!renderer || !renderer.enabled) continue;
                if (!statisticsCache.TryGetValue(node, out var counts)) statisticsCache[node] = counts = CountMesh(node);
                bool isSelected = objects.Contains(i) || HasSelectedParent(i);
                for (int c = 0; c < 5; c++) { scene[c] += counts[c]; if (isSelected) selected[c] += counts[c]; }
            }
            return (scene, selected);
        }
        private static long[] CountMesh(StudioModelDocument.Node node)
        {
            var mesh = node.mesh;
            var positions = mesh.vertices;
            var welded = new int[positions.Length]; var ids = new Dictionary<Vector3, int>();
            for (int i = 0; i < positions.Length; i++)
            { if (!ids.TryGetValue(positions[i], out int id)) ids[positions[i]] = id = ids.Count; welded[i] = id; }
            var edges = new HashSet<(int, int)>(); long faces = 0, triangles = 0;
            void Edge(int a, int b) { a = welded[a]; b = welded[b]; if (a != b) edges.Add((Math.Min(a, b), Math.Max(a, b))); }
            bool triangleMesh = true;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) triangleMesh = false;
                else triangles += (long)mesh.GetIndexCount(sub) / 3;
            }
            if (triangleMesh)
            {
                var topology = new StudioMeshCuts(node); faces = topology.Faces.Count;
                foreach (var face in topology.Faces)
                    for (int i = 0; i < face.Vertices.Count; i++) Edge(face.Vertices[i], face.Vertices[(i + 1) % face.Vertices.Count]);
            }
            else for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var indices = mesh.GetIndices(sub); var type = mesh.GetTopology(sub);
                int stride = type == MeshTopology.Triangles ? 3 : type == MeshTopology.Quads ? 4 : type == MeshTopology.Lines ? 2 : 1;
                if (type == MeshTopology.Triangles || type == MeshTopology.Quads)
                {
                    faces += indices.Length / stride;
                    if (type == MeshTopology.Quads) triangles += indices.Length / 2;
                    for (int i = 0; i < indices.Length; i += stride) for (int e = 0; e < stride; e++) Edge(indices[i + e], indices[i + (e + 1) % stride]);
                }
                else if (type == MeshTopology.Lines) for (int i = 0; i + 1 < indices.Length; i += 2) Edge(indices[i], indices[i + 1]);
                else if (type == MeshTopology.LineStrip) for (int i = 0; i + 1 < indices.Length; i++) Edge(indices[i], indices[i + 1]);
            }
            var uv = new List<Vector2>(); mesh.GetUVs(0, uv);
            return new[] { (long)ids.Count, edges.Count, faces, triangles, uv.Count };
        }
        internal void DrawStatistics()
        {
            if (Event.current.type != EventType.Repaint || !StudioSettings.Current.showMeshStatistics) return;
            var counts = MeshStatistics();
            // Text stays readable over both lit geometry and the gray viewport, without a panel fill.
            void Label(Rect rect, string text)
            {
                var color = GUI.contentColor;
                GUI.contentColor = Color.black; GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, StudioTheme.Styles.miniLabel);
                GUI.contentColor = Color.white; GUI.Label(rect, text, StudioTheme.Styles.miniLabel); GUI.contentColor = color;
            }
            Label(new Rect(64, 0, 88, 18), "Scene"); Label(new Rect(158, 0, 90, 18), "Selected");
            for (int i = 0; i < StatisticsLabels.Length; i++)
            {
                float y = 18 + i * 17;
                Label(new Rect(0, y, 64, 18), StatisticsLabels[i]);
                Label(new Rect(64, y, 88, 18), counts.scene[i].ToString("N0"));
                Label(new Rect(158, y, 90, 18), counts.selected[i].ToString("N0"));
            }
            if (mode != ComponentMode.Object && Components.Count > 0)
                Label(new Rect(0, 106, 256, 18), $"Selected {mode.ToString().ToLowerInvariant()}: {Components.Count:N0}");
        }
    }
}
