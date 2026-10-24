using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        // Scene totals are rebuilt only after edits/Undo/load; selection counts follow the active components.
        private readonly Dictionary<StudioModelDocument.Node, long[]> statisticsCache = new Dictionary<StudioModelDocument.Node, long[]>();
        private readonly HashSet<StudioModelDocument.Node> deferredPositionStatistics = new HashSet<StudioModelDocument.Node>();
        private void InvalidatePositionStatistics(Mesh mesh, bool defer)
        {
            if (!Document) return;
            foreach (var node in Document.nodes)
                if (node.mesh == mesh)
                {
                    if (defer) deferredPositionStatistics.Add(node);
                    else statisticsCache.Remove(node);
                }
        }
        private void FlushPositionStatistics()
        {
            if (deferredPositionStatistics.Count == 0) return;
            cachedSelectedStatistics = null;
            foreach (var node in deferredPositionStatistics) statisticsCache.Remove(node);
            deferredPositionStatistics.Clear();
            repaint();
        }
        private static readonly string[] StatisticsLabels = { "Verts", "Edges", "Faces", "Tris" };
        private static readonly GUIContent[] StatisticsRowContent=Array.ConvertAll(StatisticsLabels,s=>new GUIContent(s));
        private GUIStyle statisticsLabelStyle;
        private readonly GUIContent[] statisticsValues=new GUIContent[8];
        private readonly long[] statisticsLastValues=new long[8];
        private GUIContent StatisticsValue(int index,long value)
        {
            if(statisticsValues[index]==null)statisticsValues[index]=new GUIContent();
            if(statisticsValues[index].text.Length==0||statisticsLastValues[index]!=value)
            {statisticsLastValues[index]=value;statisticsValues[index].text=value.ToString("N0");}
            return statisticsValues[index];
        }
        internal (long[] scene, long[] selected) MeshStatistics()
        {
            var scene = new long[4]; var selected = new long[4];
            if (!Document) return (scene, selected);
            for (int i = 0; i < Document.nodes.Count && i < Transforms.Length; i++)
            {
                var node = Document.nodes[i];
                if (!node.mesh || !Transforms[i].gameObject.activeInHierarchy) continue;
                var renderer = Transforms[i].GetComponent<Renderer>();
                if (!renderer || !renderer.enabled) continue;
                if (!statisticsCache.TryGetValue(node, out var counts)) statisticsCache[node] = counts = CountMesh(node);
                bool isSelected = mode == ComponentMode.Object && (objects.Contains(i) || HasSelectedParent(i));
                for (int c = 0; c < 4; c++) { scene[c] += counts[c]; if (isSelected) selected[c] += counts[c]; }
            }
            if (mode != ComponentMode.Object && Active >= 0 && ActiveMesh && Transforms[Active].gameObject.activeInHierarchy)
            {
                var renderer = Transforms[Active].GetComponent<Renderer>();
                if (renderer && renderer.enabled) selected = CountSelectedComponents();
            }
            return (scene, selected);
        }
        private Mesh selectedStatisticsMesh;
        private ComponentMode selectedStatisticsMode;
        private bool selectedStatisticsWeld;
        private int selectedStatisticsVersion;
        private long[] cachedSelectedStatistics;
        private readonly HashSet<int> selectedStatisticsComponents = new HashSet<int>();
        private long[] CountSelectedComponents()
        {
            var mesh=ActiveMesh;
            int version=mesh ? EditorUtility.GetDirtyCount(mesh) : 0;
            bool deferred=Document && Active>=0 && deferredPositionStatistics.Contains(Document.nodes[Active]);
            if(cachedSelectedStatistics!=null && selectedStatisticsMesh==mesh && selectedStatisticsMode==mode &&
                selectedStatisticsWeld==weld && (selectedStatisticsVersion==version || deferred) &&
                selectedStatisticsComponents.SetEquals(Components)) return cachedSelectedStatistics;
            cachedSelectedStatistics=CalculateSelectedComponents();
            selectedStatisticsMesh=mesh;selectedStatisticsMode=mode;selectedStatisticsWeld=weld;selectedStatisticsVersion=version;
            selectedStatisticsComponents.Clear();selectedStatisticsComponents.UnionWith(Components);
            return cachedSelectedStatistics;
        }
        private long[] CalculateSelectedComponents()
        {
            var counts = new long[4];
            if (Components.Count == 0) return counts;
            var positions = ComponentPositions();
            var vertices = new HashSet<int>();
            var edges = new HashSet<(Vector3, Vector3)>();
            void Vertex(int i) { if (i >= 0 && i < positions.Count) vertices.Add(i); }
            void Edge(int a, int b)
            {
                Vertex(a); Vertex(b);
                var x = positions[a]; var y = positions[b];
                if (x != y && !edges.Contains((y, x))) edges.Add((x, y));
            }
            if (mode == ComponentMode.Vertex)
                foreach (int index in Components) Vertex(index);
            else if (mode == ComponentMode.Edge)
            {
                var allEdges = Edges();
                foreach (int index in Components)
                    if (index >= 0 && index < allEdges.Count) Edge(allEdges[index].a, allEdges[index].b);
            }
            else if (mode == ComponentMode.Face)
            {
                var faces = Faces();
                foreach (int index in Components)
                {
                    if (index < 0 || index >= faces.Count) continue;
                    counts[2]++;
                    var face = faces[index];
                    for (int e = 0; e < face.Vertices.Count; e++) Edge(face.Vertices[e], face.Vertices[(e + 1) % face.Vertices.Count]);
                    counts[3] += cachedFaceTriangles[index].Count / 3;
                    foreach (int vertex in cachedFaceTriangles[index]) Vertex(vertex);
                }
            }
            var selectedPositions = new HashSet<Vector3>(vertices.Select(i => positions[i]));
            counts[0] = selectedPositions.Count; counts[1] = edges.Count;
            return counts;
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
                // Counts need polygon boundaries, not a reconstructed neighbour graph.
                // Position drags retain this metadata; avoid rebuilding the entire
                // editable topology whenever their statistics are invalidated.
                var polygons = node.cutPolygons;
                if (polygons != null && node.cutTriangles != null && node.cutTriangles.SequenceEqual(mesh.triangles))
                {
                    for (int cursor = 0; cursor < polygons.Length;)
                    {
                        int count = polygons[cursor + 1]; cursor += 2; faces++;
                        for (int i = 0; i < count; i++) Edge(polygons[cursor + i], polygons[cursor + (i + 1) % count]);
                        cursor += count;
                    }
                }
                else
                {
                    var topology = new StudioMeshCuts(node); faces = topology.Faces.Count;
                    foreach (var face in topology.Faces)
                        for (int i = 0; i < face.Vertices.Count; i++) Edge(face.Vertices[i], face.Vertices[(i + 1) % face.Vertices.Count]);
                }
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
            return new[] { (long)ids.Count, edges.Count, faces, triangles };
        }
        internal void DrawStatistics()
        {
            if (Event.current.type != EventType.Repaint || !StudioSettings.Current.showMeshStatistics) return;
#if MASHBOX_STUDIO_TESTS
            ProfileScene("stats.start");
#endif
            var counts = MeshStatistics();
#if MASHBOX_STUDIO_TESTS
            ProfileScene("stats.count");
#endif
            // Explicit presentation also updates styles retained across editor hot reload.
            if (statisticsLabelStyle == null) statisticsLabelStyle = new GUIStyle(EditorStyles.label);
            statisticsLabelStyle.fontSize = 11;
            statisticsLabelStyle.normal.textColor = new Color(.82f, .82f, .82f, 1);
            void Label(Rect rect, GUIContent text)
            {
                var color = GUI.contentColor;
                var tint = GUI.color;
                var enabled = GUI.enabled;
                GUI.enabled = true;
                GUI.color = Color.white;
                GUI.contentColor = Color.black; statisticsLabelStyle.Draw(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, false, false, false, false);
                GUI.contentColor = Color.white; statisticsLabelStyle.Draw(rect, text, false, false, false, false);
                GUI.contentColor = color;
                GUI.color = tint;
                GUI.enabled = enabled;
            }
            for (int i = 0; i < StatisticsLabels.Length; i++)
            {
                float y = i * 17;
                Label(new Rect(0, y, 64, 18), StatisticsRowContent[i]);
                Label(new Rect(64, y, 88, 18), StatisticsValue(i,counts.scene[i]));
                Label(new Rect(158, y, 90, 18), StatisticsValue(i+4,counts.selected[i]));
            }
        }
    }
}
