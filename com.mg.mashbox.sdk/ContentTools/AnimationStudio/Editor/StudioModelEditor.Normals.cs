using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private ToolChannelPane softenToolPane;
        private int[] cachedNormalGroups;
        private int normalGroupCount;
        private Vector3[] normalGroupSums = Array.Empty<Vector3>();
        private readonly List<Vector3> normalGroupValues = new List<Vector3>();
        private void RecalculateEditableNormals(Mesh mesh)
        {
            mesh.RecalculateNormals();
            var groups = Document && Active >= 0 && ActiveMesh == mesh ? Document.nodes[Active].normalGroups : null;
            if (groups == null || groups.Length != mesh.vertexCount) return;
            if (!ReferenceEquals(cachedNormalGroups, groups))
            {
                int count = 0; foreach (int group in groups) count = Math.Max(count, group+1);
                if (normalGroupSums.Length < count) normalGroupSums = new Vector3[count];
                cachedNormalGroups = groups; normalGroupCount = count;
            }
            Array.Clear(normalGroupSums, 0, normalGroupCount);
            normalGroupValues.Clear(); mesh.GetNormals(normalGroupValues);
            for (int i = 0; i < groups.Length; i++) normalGroupSums[groups[i]] += normalGroupValues[i];
            for (int i = 0; i < normalGroupCount; i++) if (normalGroupSums[i].sqrMagnitude > 1e-20f) normalGroupSums[i].Normalize();
            for (int i = 0; i < groups.Length; i++)
            {
                var sum = normalGroupSums[groups[i]];
                if (sum.sqrMagnitude > .5f) normalGroupValues[i] = sum;
            }
            mesh.SetNormals(normalGroupValues);
        }
        private const string SoftenTool = "Soften Edges";
        private static readonly GUIContent SoftenAngleContent = new GUIContent("Angle (degrees)", "Edges between faces within this angle are softened. This is also the default for Mesh Display > Soften.");
        private IEnumerable<int> NormalTargets()
        {
            if (!Document) yield break;
            if (mode != ComponentMode.Object) { if (Active >= 0 && ActiveMesh) yield return Active; yield break; }
            for (int i = 0; i < Document.nodes.Count; i++)
                if (Document.nodes[i].mesh && (objects.Contains(i) || HasSelectedParent(i))) yield return i;
        }
        private bool CanAdjustEdgeNormals
        {
            get
            {
                if (!Document || (mode != ComponentMode.Object && (Components.Count == 0 || !CanEditComponents()))) return false;
                bool found = false;
                foreach (int i in NormalTargets())
                {
                    var node = Document.nodes[i]; var mesh = node.mesh;
                    if (node.skinned || node.blendShape != null || mesh.blendShapeCount > 0 || mesh.bindposes.Length > 0 || !mesh.isReadable) return false;
                    for (int sub = 0; sub < mesh.subMeshCount; sub++) if (mesh.GetTopology(sub) != MeshTopology.Triangles) return false;
                    found = true;
                }
                return found;
            }
        }
        private void CloseNormalsToolWindow() { if (softenToolPane) softenToolPane.Close(); softenToolPane = null; }
        private void OpenSoftenTool()
        {
            if (softenToolPane) { softenToolPane.Focus(); return; }
            softenToolPane = ToolChannelPane.Open("Soften Edges", DrawSoftenTool, () => softenToolPane = null,
                execute: () => ApplyLiveTool(LiveTool.Soften), canExecute: () => CanRunLiveTool(LiveTool.Soften), failed: ex => { error = ex.Message; repaint(); }, owner: ToolChannelPaneOwner,
                preview: () => QueueLiveTool(LiveTool.Soften), cancelPreview: () => CancelLiveTool(LiveTool.Soften));
            QueueLiveTool(LiveTool.Soften);
        }
        private void DrawSoftenTool()
        {
            EditorGUIUtility.labelWidth = 106;
            ToolChannelPane.FloatParameter(SoftenTool, "Angle", SoftenAngleContent, 45, 0, 180);
            GUILayout.Label("Soften edges up to this angle.", StudioTheme.Styles.miniLabel);

        }
        private static (Vector3, Vector3) NormalEdgePositionKey(Vector3 a, Vector3 b)
        {
            int order = a.x.CompareTo(b.x); if (order == 0) order = a.y.CompareTo(b.y); if (order == 0) order = a.z.CompareTo(b.z);
            return order <= 0 ? (a, b) : (b, a);
        }
        private HashSet<(int, int)> SelectedNormalEdges(StudioMeshCuts topology)
        {
            if (mode == ComponentMode.Object) return null;
            var selected = new HashSet<(int, int)>();
            if (mode == ComponentMode.Edge)
            {
                var edges = Edges();
                foreach (int i in Components) if (i >= 0 && i < edges.Count) selected.Add(topology.NormalEdgeKey(edges[i].a, edges[i].b));
            }
            else
            {
                var vertices = new HashSet<Vector3>();
                if (mode == ComponentMode.Vertex) foreach (int i in Components) if (i >= 0 && i < topology.Positions.Length) vertices.Add(topology.Positions[i]);
                for (int f = 0; f < topology.Faces.Count; f++)
                {
                    if (mode == ComponentMode.Face && !Components.Contains(f)) continue;
                    var v = topology.Faces[f].Vertices;
                    for (int e = 0; e < v.Count; e++)
                        if (mode == ComponentMode.Face || vertices.Contains(topology.Positions[v[e]]) || vertices.Contains(topology.Positions[v[(e+1)%v.Count]]))
                            selected.Add(topology.NormalEdgeKey(v[e], v[(e+1)%v.Count]));
                }
            }
            return selected;
        }
        internal void ApplyEdgeNormals(bool harden) => ApplyEdgeNormalsChecked(harden);
        private void ApplyEdgeNormalsChecked(bool harden)
        {
            FinishLiveTool();
            if (!CanAdjustEdgeNormals) throw new InvalidOperationException("Select editable static mesh objects or mesh components.");
            if (GUIUtility.hotControl != 0) return;
            FinishChannelEdit(); StopCutTool(); CancelMarkingMenu(); EndPivotSnap();
            float angle = Mathf.Clamp(ToolChannelPane.GetFloatDefault(SoftenTool, "Angle", 45), 0, 180);
            var oldMode = mode; var oldComponents = Components.ToArray();
            var selectedPoints = new HashSet<Vector3>(); var selectedEdges = new HashSet<(Vector3, Vector3)>();
            if (mode == ComponentMode.Vertex) { var v = ActiveMesh.vertices; foreach (int i in Components) if (i >= 0 && i < v.Length) selectedPoints.Add(v[i]); }
            if (mode == ComponentMode.Edge)
            {
                var v = ActiveMesh.vertices; var edges = Edges();
                foreach (int i in Components) if (i >= 0 && i < edges.Count) selectedEdges.Add(NormalEdgePositionKey(v[edges[i].a], v[edges[i].b]));
            }
            var targets = NormalTargets().ToArray(); var seen = new HashSet<Mesh>();
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            string label = harden ? "Harden edges" : "Soften edges"; Undo.SetCurrentGroupName(label);
            try
            {
                foreach (int i in targets)
                {
                    var node = Document.nodes[i]; if (!seen.Add(node.mesh)) continue;
                    var topology = new StudioMeshCuts(node); var edges = SelectedNormalEdges(topology);
                    if (edges != null && edges.Count == 0) throw new InvalidOperationException("Select mesh components before adjusting edge normals.");
                    topology.SetEdgeNormals(node, edges, angle, harden,
                        () => Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { Document, node.mesh }, label));
                    foreach (var other in Document.nodes) if (other != node && other.mesh == node.mesh)
                    { other.cutPolygons = node.cutPolygons?.ToArray(); other.cutTriangles = node.cutTriangles?.ToArray(); other.normalGroups = node.normalGroups?.ToArray(); }
                    EditorUtility.SetDirty(node.mesh);
                }
                MeshTopologyChanged(); mode = oldMode;
                if (mode == ComponentMode.Face) { foreach (int i in oldComponents) if (i >= 0 && i < Faces().Count) Components.Add(i); }
                else if (mode == ComponentMode.Vertex)
                { var v = ActiveMesh.vertices; for (int i = 0; i < v.Length; i++) if (selectedPoints.Contains(v[i])) Components.Add(i); }
                else if (mode == ComponentMode.Edge)
                { var v = ActiveMesh.vertices; var edges = Edges(); for (int i = 0; i < edges.Count; i++) if (selectedEdges.Contains(NormalEdgePositionKey(v[edges[i].a], v[edges[i].b]))) Components.Add(i); }
                error = harden ? "Edges hardened." : "Edges softened up to " + angle.ToString("0.####") + " degrees.";
                repaint(); softenToolPane?.Repaint();
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
            finally { Undo.CollapseUndoOperations(group); }
        }
    }
}
