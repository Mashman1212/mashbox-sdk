using System;
using System.Collections.Generic;
using System.Linq;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private bool CanSplitSelection => Document && mode != ComponentMode.Object && ComponentTargetNodes(true).Count > 0;
        private HashSet<int> SplitSelectionFaces()
        {
            var faces = Faces();
            if (mode == ComponentMode.Face) return new HashSet<int>(Components.Where(f => f >= 0 && f < faces.Count));
            var result = new HashSet<int>();
            if (mode == ComponentMode.Vertex)
            {
                // Match vertex isolation: capture once, then only direct incident faces.
                var selected = SelectedVertices();
                for (int f = 0; f < faces.Count; f++) if (faces[f].Vertices.Any(selected.Contains)) result.Add(f);
            }
            else if (mode == ComponentMode.Edge)
            {
                var topology = new StudioMeshCuts(Document.nodes[Active]); var edges = Edges();
                (int,int) Key(int a,int b) => weld ? topology.NormalEdgeKey(a,b) : (Math.Min(a,b),Math.Max(a,b));
                var selected = new HashSet<(int,int)>(Components.Where(e => e >= 0 && e < edges.Count).Select(e => Key(edges[e].a,edges[e].b)));
                for (int f = 0; f < faces.Count; f++)
                {
                    var vertices = faces[f].Vertices;
                    for (int e = 0; e < vertices.Count; e++) if (selected.Contains(Key(vertices[e],vertices[(e+1)%vertices.Count]))) { result.Add(f); break; }
                }
            }
            return result;
        }
        private void SplitSelectedComponents()
        {
            if (!CanSplitSelection) return;
            var selection = new Dictionary<int,HashSet<int>>();
            foreach (int node in ComponentTargetNodes(true)) using (new ComponentTargetScope(this,node))
            {
                var faces = SplitSelectionFaces(); if (faces.Count > 0) selection.Add(node,faces);
            }
            if (selection.Count == 0) throw new InvalidOperationException("Select faces, or vertices or edges directly attached to faces, to split.");
            var split = Document.SplitMeshes(selection);
            if (split.Length == 0) return;
            CancelMarkingMenu(); EndPivotSnap(); StopCutTool(); pivot = false; marquee = false; gestureUndo = -1;
            ClearAllComponentSelections(); objects.Clear();
            foreach (int node in split) objects.Add(node);
            Active = split[split.Length-1]; mode = ComponentMode.Object;
            foreach (var target in componentTargets.Values) target.mode = ComponentMode.Object;
            search = ""; UnityEngine.GUIUtility.hotControl = 0; Rebuild(); Changed(); RememberAction(RepeatKind.Split);
            // The shared Run command transaction snapshots source/result selection
            // together with this document action. No separate history stack or pane.
        }
    }
}
