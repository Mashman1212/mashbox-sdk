using System;
using System.Collections.Generic;
using System.Linq;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private bool CanGrowComponentSelection => Document && mode!=ComponentMode.Object && ComponentTargetNodes(true).Any(IsolationNodeVisible);
        private void GrowComponentSelection(bool shrink)
        {
            var before=CaptureSelection();
            foreach(int node in ComponentTargetNodes(true))
            {
                if(!IsolationNodeVisible(node))continue;
                using(new ComponentTargetScope(this,node))ResizeComponentSelection(shrink);
            }
            RecordSelectionChange(before);repaint();
        }
        private void ResizeComponentSelection(bool shrink)
        {
            var points=ComponentPositions();var vertices=new int[points.Count];
            var weldedIds=new Dictionary<UnityEngine.Vector3,int>();
            for(int i=0;i<points.Count;i++)
            {
                if(!weld){vertices[i]=i;continue;}
                if(!weldedIds.TryGetValue(points[i],out int id)){id=weldedIds.Count;weldedIds.Add(points[i],id);}vertices[i]=id;
            }
            (int,int) Key(int a,int b)=>a<b?(a,b):(b,a);
            var edges=Edges();var groups=new List<HashSet<int>>();int[] logical;
            if(mode==ComponentMode.Vertex)
            {
                logical=vertices;
                foreach(var edge in edges)groups.Add(new HashSet<int>{vertices[edge.a],vertices[edge.b]});
            }
            else if(mode==ComponentMode.Edge)
            {
                logical=new int[edges.Count];var ids=new Dictionary<(int,int),int>();var incident=new Dictionary<int,HashSet<int>>();
                for(int i=0;i<edges.Count;i++)
                {
                    int a=vertices[edges[i].a],b=vertices[edges[i].b];var key=Key(a,b);
                    if(!ids.TryGetValue(key,out int id)){id=ids.Count;ids.Add(key,id);}logical[i]=id;
                    foreach(int vertex in new[]{a,b}){if(!incident.TryGetValue(vertex,out var list))incident[vertex]=list=new HashSet<int>();list.Add(id);}
                }
                groups.AddRange(incident.Values);
            }
            else
            {
                var faces=Faces();logical=Enumerable.Range(0,faces.Count).ToArray();var incident=new Dictionary<int,HashSet<int>>();
                for(int i=0;i<faces.Count;i++)
                {
                    var face=faces[i].Vertices;
                    for(int e=0;e<face.Count;e++)
                    {
                        // Corner neighbors belong to the same square face ring.
                        int key=vertices[face[e]];
                        if(!incident.TryGetValue(key,out var list))incident[key]=list=new HashSet<int>();list.Add(i);
                    }
                }
                groups.AddRange(incident.Values);
            }
            var eligible=new HashSet<int>();var selected=new HashSet<int>();
            for(int i=0;i<logical.Length;i++)if(IsolationAllowsComponent(i))
            {eligible.Add(logical[i]);if(Components.Contains(i))selected.Add(logical[i]);}
            var next=new HashSet<int>(selected);
            // Read only the captured selection, so each invocation is one ring.
            // Incidence groups avoid quadratic edge-adjacency cliques at high valence.
            foreach(var group in groups)
            {
                bool boundary=shrink?group.Any(i=>eligible.Contains(i)&&!selected.Contains(i)):group.Any(selected.Contains);
                if(!boundary)continue;
                foreach(int id in group)if(eligible.Contains(id)){if(shrink)next.Remove(id);else next.Add(id);}
            }
            for(int i=0;i<logical.Length;i++)if(IsolationAllowsComponent(i))
            {if(next.Contains(logical[i]))Components.Add(i);else Components.Remove(i);}
        }
    }
}
