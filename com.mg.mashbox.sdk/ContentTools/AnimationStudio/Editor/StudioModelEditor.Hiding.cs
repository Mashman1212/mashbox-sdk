using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        // Masks are transient display data, just like component isolation. Each
        // entry owns the previous masks until restored or the display is reset.
        private sealed class HiddenSelection
        {
            internal readonly Dictionary<StudioModelDocument.Node, IsolatedComponents> masks = new Dictionary<StudioModelDocument.Node, IsolatedComponents>();
            internal readonly HashSet<StudioModelDocument.Node> objects = new HashSet<StudioModelDocument.Node>();
        }
        private readonly Stack<HiddenSelection> hiddenSelections = new Stack<HiddenSelection>();
        private void ClearHiddenSelectionHistory()
        {
            foreach (var entry in hiddenSelections)
                foreach (var mask in entry.masks.Values) mask?.Dispose();
            hiddenSelections.Clear();
        }
        private void BindComponentMask(int node, IsolatedComponents mask)
        {
            var filter = Transforms[node].GetComponent<MeshFilter>();
            var skin = Transforms[node].GetComponent<SkinnedMeshRenderer>();
            var mesh = mask?.preview ? mask.preview : Document.nodes[node].mesh;
            if (filter) filter.sharedMesh = mesh;
            if (skin) skin.sharedMesh = mesh;
        }
        private bool CanHideViewportSelection => CanIsolateViewportSelection &&
            (mode!=ComponentMode.Object || objects.Any(IsVisibleHideTarget));
        private bool IsVisibleHideTarget(int node) => Document && node>=0 && node<Document.nodes.Count &&
            node<Transforms.Length && Transforms[node] && Document.nodes[node].active && Transforms[node].gameObject.activeInHierarchy;
        internal void HideViewportSelection()
        {
            // A no-op hide must not finish previews or touch previous hide history.
            if (!CanHideViewportSelection) return;
            FinishLiveTool();
            if (!CanHideViewportSelection) return;
            var entry = new HiddenSelection();
            if (mode == ComponentMode.Object)
            {
                foreach (int node in objects.Where(IsVisibleHideTarget))
                    entry.objects.Add(Document.nodes[node]);
                if (entry.objects.Count == 0) return;
                ChangeHierarchyVisibility("Hide selection", i => !entry.objects.Contains(Document.nodes[i]) && Document.nodes[i].active);
                objects.Clear(); objectSelectionOrder.Clear();
            }
            else foreach (int node in ComponentTargetNodes(true).ToArray()) using (new ComponentTargetScope(this, node))
            {
                var old = ComponentIsolation(node);
                var mask = new IsolatedComponents { source = ActiveMesh };
                var triangles = new HashSet<(int,int,int)>();
                var selectedVertices = mode == ComponentMode.Vertex ? SelectedVertices() : new HashSet<int>();
                var selectedEdges = new HashSet<(int,int)>();
                if (mode == ComponentMode.Edge)
                {
                    var edges = Edges();
                    foreach (int i in Components.Where(i => i >= 0 && i < edges.Count)) selectedEdges.Add(IsolationEdge(edges[i].a, edges[i].b));
                }
                var faces = Faces();
                bool changed = false;
                for (int i = 0; i < faces.Count; i++)
                {
                    if (old != null && !old.faces.Contains(i)) continue;
                    var vertices = faces[i].Vertices;
                    bool hide = mode == ComponentMode.Face ? Components.Contains(i) : mode == ComponentMode.Vertex ? vertices.Any(selectedVertices.Contains) :
                        Enumerable.Range(0, vertices.Count).Any(j => selectedEdges.Contains(IsolationEdge(vertices[j], vertices[(j + 1) % vertices.Count])));
                    if (hide) { changed = true; continue; }
                    mask.faces.Add(i);
                    for (int j = 0; j < vertices.Count; j++) { mask.vertices.Add(vertices[j]); mask.edges.Add(IsolationEdge(vertices[j], vertices[(j + 1) % vertices.Count])); }
                    var indices = cachedFaceTriangles[i];
                    for (int j = 0; j + 2 < indices.Count; j += 3) triangles.Add((indices[j], indices[j + 1], indices[j + 2]));
                }
                if (!changed) continue;
                mask.preview = UnityEngine.Object.Instantiate(mask.source);
                mask.preview.name = "Studio hidden selection";
                mask.preview.hideFlags = HideFlags.HideAndDontSave;
                for (int sub = 0; sub < mask.source.subMeshCount; sub++)
                {
                    var kept = new List<int>(); var source = mask.source.GetTriangles(sub);
                    for (int t = 0; t + 2 < source.Length; t += 3)
                        if (triangles.Contains((source[t], source[t + 1], source[t + 2]))) { kept.Add(source[t]); kept.Add(source[t + 1]); kept.Add(source[t + 2]); }
                    mask.preview.SetTriangles(kept, sub, false);
                }
                entry.masks.Add(Document.nodes[node], old);
                isolatedComponents[Document.nodes[node]] = mask;
                BindComponentMask(node, mask);
                Components.Clear();
            }
            if (entry.objects.Count == 0 && entry.masks.Count == 0) return;
            hiddenSelections.Push(entry);
            ClearSelectionWireframes(); ClearAllComponentDrawCaches(); ResetComponentHover(); SelectionDisplayChanged();
        }
        internal void UnhideLastSelection()
        {
            FinishLiveTool();
            if (!Document || hiddenSelections.Count == 0) return;
            var entry = hiddenSelections.Pop();
            if (entry.objects.Count > 0)
                ChangeHierarchyVisibility("Unhide last hidden", i => Document.nodes[i].active || entry.objects.Any(n =>
                    Document.nodes.IndexOf(n) is int child && child >= 0 && IsHierarchyDescendant(child, i)));
            foreach (var pair in entry.masks)
            {
                int node = Document.nodes.IndexOf(pair.Key);
                if (node < 0) { pair.Value?.Dispose(); continue; }
                if (isolatedComponents.TryGetValue(pair.Key, out var current)) current.Dispose();
                if (pair.Value == null) isolatedComponents.Remove(pair.Key);
                else
                {
                    isolatedComponents[pair.Key] = pair.Value;
                    pair.Value.preview.vertices = pair.Value.source.vertices;
                    pair.Value.preview.normals = pair.Value.source.normals;
                    pair.Value.preview.RecalculateBounds();
                }
                BindComponentMask(node, pair.Value);
            }
            ClearSelectionWireframes(); ClearAllComponentDrawCaches(); ResetComponentHover(); SelectionDisplayChanged(false);
        }
    }
}
