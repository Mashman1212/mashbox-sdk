using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private sealed class IsolatedComponents : IDisposable
        {
            internal Mesh source, preview;
            internal readonly HashSet<int> vertices = new HashSet<int>(), faces = new HashSet<int>();
            internal readonly HashSet<(int,int)> edges = new HashSet<(int,int)>();
            internal GraphicsBuffer vertexIds;
            internal GraphicsBuffer VertexIds
            {
                get
                {
                    if (vertexIds == null && vertices.Count > 0)
                    {
                        vertexIds = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertices.Count, 4);
                        vertexIds.SetData(vertices.OrderBy(i=>i).ToArray());
                    }
                    return vertexIds;
                }
            }
            public void Dispose() { vertexIds?.Dispose(); if(preview) UnityEngine.Object.DestroyImmediate(preview); }
        }
        private readonly Dictionary<StudioModelDocument.Node,IsolatedComponents> isolatedComponents = new Dictionary<StudioModelDocument.Node,IsolatedComponents>();
        private readonly Dictionary<Transform,bool> isolationObjects = new Dictionary<Transform,bool>();
        private readonly Dictionary<Renderer,bool> isolationRenderers = new Dictionary<Renderer,bool>();
        private bool IsolationNodeVisible(int index) => !DisplayRendererHidden(Transforms[index].GetComponent<Renderer>());
        private static (int,int) IsolationEdge(int a,int b) => (Math.Min(a,b),Math.Max(a,b));
        private IsolatedComponents ComponentIsolation(int index) => Document && index>=0 && index<Document.nodes.Count && isolatedComponents.TryGetValue(Document.nodes[index],out var value) ? value : null;
        internal bool CanIsolateViewportSelection => Document && (mode==ComponentMode.Object ? objects.Count>0 : ComponentTargetNodes(true).Count>0);
        private bool IsolationAllowsComponent(int index)
        {
            var visible=ComponentIsolation(Active);
            if(visible==null) return true;
            if(mode==ComponentMode.Vertex) return visible.vertices.Contains(index);
            if(mode==ComponentMode.Face) return visible.faces.Contains(index);
            var edges=Edges();
            return index>=0 && index<edges.Count && visible.edges.Contains(IsolationEdge(edges[index].a,edges[index].b));
        }
        private bool IsolationAllowsEdge(int node,int a,int b)
        { var visible=ComponentIsolation(node);return visible==null || visible.edges.Contains(IsolationEdge(a,b)); }
        private Mesh IsolationPickMesh(int node)
        { var visible=ComponentIsolation(node);return visible?.preview ? visible.preview : Document.nodes[node].mesh; }
        private void ClearComponentIsolation()
        {
            foreach(var pair in isolatedComponents)
            {
                int index=Document?Document.nodes.IndexOf(pair.Key):-1;
                if(index>=0 && index<Transforms.Length && Transforms[index])
                {
                    var filter=Transforms[index].GetComponent<MeshFilter>();
                    var skin=Transforms[index].GetComponent<SkinnedMeshRenderer>();
                    if(filter && filter.sharedMesh==pair.Value.preview)filter.sharedMesh=pair.Value.source;
                    if(skin && skin.sharedMesh==pair.Value.preview)skin.sharedMesh=pair.Value.source;
                }
                pair.Value.Dispose();
            }
            isolatedComponents.Clear();
        }
        private void ClearViewportIsolation()
        {
            ClearComponentIsolation();
            foreach(var pair in isolationObjects)if(pair.Key)pair.Key.gameObject.SetActive(pair.Value);
            isolationObjects.Clear();
            foreach(var pair in isolationRenderers)if(pair.Key)SetDisplayRendererHidden(pair.Key,pair.Value);
            isolationRenderers.Clear();
        }
        // Topology tools publish new component identities. Restore their source
        // render mesh before capture, retaining the isolated object cohort.
        private void PrepareIsolationTopologyEdit()
        {
            if(isolatedComponents.Count==0)return;
            ClearComponentIsolation();ClearSelectionWireframes();ClearAllComponentDrawCaches();ResetComponentHover();
        }
        internal bool ViewportIsolationActive => isolationObjects.Count>0 || isolatedComponents.Count>0;
        internal void RunViewportIsolation(bool showAll) => Run(() => { if(showAll || ViewportIsolationActive)ShowAllViewportComponents();else IsolateViewportSelection(); });
        internal void IsolateViewportSelection()
        {
            FinishLiveTool();
            if(!CanIsolateViewportSelection)return;
            var targets=mode==ComponentMode.Object?objects.ToArray():ComponentTargetNodes(true).ToArray();
            ClearViewportIsolation();
            for(int i=0;i<Transforms.Length;i++)
            {
                isolationObjects[Transforms[i]]=Transforms[i].gameObject.activeSelf;
                bool keep=targets.Any(n=>IsHierarchyDescendant(n,i) || (mode==ComponentMode.Object && IsHierarchyDescendant(i,n)));
                Transforms[i].gameObject.SetActive(keep);
                var renderer=Transforms[i].GetComponent<Renderer>();
                if(renderer)
                {
                    isolationRenderers[renderer]=DisplayRendererHidden(renderer);
                    bool geometry=targets.Any(n=>i==n || (mode==ComponentMode.Object && IsHierarchyDescendant(i,n)));
                    SetDisplayRendererHidden(renderer,DisplayRendererHidden(renderer) || !geometry);
                }
            }
            if(mode!=ComponentMode.Object)
            foreach(int node in targets)using(new ComponentTargetScope(this,node))
            {
                var mask=new IsolatedComponents {source=ActiveMesh};
                var triangles=new HashSet<(int,int,int)>();
                void IncludeFace(int index)
                {
                    var face=Faces()[index].Vertices;mask.faces.Add(index);
                    for(int j=0;j<face.Count;j++){mask.vertices.Add(face[j]);mask.edges.Add(IsolationEdge(face[j],face[(j+1)%face.Count]));}
                    var list=cachedFaceTriangles[index];
                    for(int t=0;t+2<list.Count;t+=3)triangles.Add((list[t],list[t+1],list[t+2]));
                }
                if(mode==ComponentMode.Vertex)
                {
                    // Capture the vertex cohort before adding face-boundary vertices:
                    // this is a direct incident-face patch, never an island flood-fill.
                    var selected=SelectedVertices();mask.vertices.UnionWith(selected);
                    var faces=Faces();
                    for(int index=0;index<faces.Count;index++)
                        if(faces[index].Vertices.Any(selected.Contains))IncludeFace(index);
                }
                else if(mode==ComponentMode.Edge)
                {
                    var edges=Edges();
                    foreach(int index in Components.Where(i=>i>=0&&i<edges.Count))
                    {var edge=edges[index];mask.edges.Add(IsolationEdge(edge.a,edge.b));mask.vertices.Add(edge.a);mask.vertices.Add(edge.b);}
                }
                else
                {
                    var faces=Faces();
                    foreach(int index in Components.Where(i=>i>=0&&i<faces.Count))
                    {
                        IncludeFace(index);
                    }
                }
                mask.preview=UnityEngine.Object.Instantiate(mask.source);
                mask.preview.name="Studio isolated components";mask.preview.hideFlags=HideFlags.HideAndDontSave;
                for(int sub=0;sub<mask.source.subMeshCount;sub++)
                {
                    var kept=new List<int>();var source=mask.source.GetTriangles(sub);
                    for(int t=0;t+2<source.Length;t+=3)if(triangles.Contains((source[t],source[t+1],source[t+2])))
                    {kept.Add(source[t]);kept.Add(source[t+1]);kept.Add(source[t+2]);}
                    mask.preview.SetTriangles(kept,sub,false);
                }
                var filter=Transforms[node].GetComponent<MeshFilter>();var skin=Transforms[node].GetComponent<SkinnedMeshRenderer>();
                if(filter)filter.sharedMesh=mask.preview;if(skin)skin.sharedMesh=mask.preview;
                isolatedComponents.Add(Document.nodes[node],mask);
            }
            ClearSelectionWireframes();ClearAllComponentDrawCaches();ResetComponentHover();repaint();
        }
        internal void ShowAllViewportComponents()
        {
            ClearViewportIsolation();ClearSelectionWireframes();ClearAllComponentDrawCaches();ResetComponentHover();
            if(Document)ShowAllHierarchyObjects();
            repaint();
        }
        private void RefreshIsolatedPositions(IList<Vector3> positions)
        {
            var mask=ComponentIsolation(Active);
            if(mask==null || mask.faces.Count==0)return;
            mask.preview.vertices=positions?.ToArray()??mask.source.vertices;
            mask.preview.normals=mask.source.normals;
            mask.preview.RecalculateBounds();
        }
    }
}
