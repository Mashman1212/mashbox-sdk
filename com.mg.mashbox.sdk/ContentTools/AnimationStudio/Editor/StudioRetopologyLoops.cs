using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioRetopologyLoops
    {
        internal static List<int[]> Boundaries(Part part)
        {
            if(part==null||part.faces==null)return new List<int[]>();
            var edges=StudioRetopologyOperations.Edges(part);
            var next=new Dictionary<int,int>();var ambiguous=new HashSet<int>();
            foreach(var entry in edges.Where(e=>e.Value.Count==1))
            {
                var ids=part.faces[entry.Value[0]].vertices;
                for(int j=0;j<ids.Length;j++)
                {
                    int a=ids[j],b=ids[(j+1)%ids.Length];
                    if(StudioRetopologyOperations.Edge(a,b)!=entry.Key)continue;
                    if(next.ContainsKey(a))ambiguous.Add(a);else next[a]=b;
                }
            }
            var visited=new HashSet<int>();var loops=new List<int[]>();
            foreach(int start in next.Keys)
            {
                if(visited.Contains(start))continue;
                var ring=new List<int>();int current=start;
                while(next.ContainsKey(current)&&!visited.Contains(current)&&!ambiguous.Contains(current))
                {visited.Add(current);ring.Add(current);current=next[current];}
                if(current==start&&ring.Count>=3)loops.Add(ring.ToArray());
            }
            return loops;
        }
        internal static string BridgeIssue(Part part,HashSet<int> selected)
        {
            const string select="Select two complete open boundary loops. Double-click a boundary vertex, then Shift-double-click the other loop.";
            if(part==null||selected==null||selected.Count<6)return select;
            var loops=Boundaries(part).Where(loop=>loop.All(selected.Contains)).ToArray();
            if(loops.Length!=2)return select;
            if(loops[0].Length!=loops[1].Length)return "Bridge needs matching vertex counts on the two loops. Subdivide / edit the loops in Modeling first.";
            if(loops[0].Intersect(loops[1]).Any())return "Choose two separate boundary loops that do not share vertices.";
            return null;
        }
        internal static void Bridge(Part part,HashSet<int> selected,int segments,StudioRetopologySurface surface,float offset)
        {
            string issue=BridgeIssue(part,selected);if(issue!=null)throw new InvalidOperationException(issue);
            var loops=Boundaries(part).Where(loop=>loop.All(selected.Contains)).ToArray();
            if(loops.Length!=2)throw new InvalidOperationException("Select two complete open boundary loops. Double-click a boundary vertex, then Shift-double-click the other loop.");
            var a=loops[0];var b=loops[1].Reverse().ToArray();
            if(a.Length!=b.Length)throw new InvalidOperationException("Bridge needs matching vertex counts on the two loops. Subdivide / edit the loops in Modeling first.");
            // Keep winding consistent with both existing boundaries; choose the
            // cyclic correspondence with the smallest total span to avoid a twist.
            int best=0;float score=float.MaxValue;
            for(int shift=0;shift<b.Length;shift++)
            {
                float value=0;for(int i=0;i<a.Length;i++)value+=(part.vertices[a[i]]-part.vertices[b[(i+shift)%b.Length]]).sqrMagnitude;
                if(value<score){score=value;best=shift;}
            }
            b=Enumerable.Range(0,b.Length).Select(i=>b[(i+best)%b.Length]).ToArray();
            segments=Mathf.Clamp(segments,1,24);var previous=a;
            int material=part.faces.First(f=>f.vertices.Contains(a[0])&&f.vertices.Contains(a[1])).material;
            for(int step=1;step<=segments;step++)
            {
                var row=b;
                if(step<segments)
                {
                    row=new int[a.Length];float t=(float)step/segments;
                    for(int i=0;i<a.Length;i++)
                    {
                        var p=Vector3.Lerp(part.vertices[a[i]],part.vertices[b[i]],t);
                        row[i]=part.vertices.Count;part.vertices.Add(surface==null?p:surface.Project(p,offset));
                        part.uv.Add(Vector2.Lerp(part.uv[a[i]],part.uv[b[i]],t));
                    }
                }
                for(int i=0;i<a.Length;i++)part.faces.Add(new Face(new[]{previous[(i+1)%a.Length],previous[i],row[i],row[(i+1)%a.Length]},material));
                previous=row;
            }
            part.needsUV=true;
        }
    }
}
