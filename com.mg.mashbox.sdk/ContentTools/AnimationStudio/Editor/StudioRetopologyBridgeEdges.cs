using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioRetopologyBridgeEdges
    {
        internal static string Validate(Part part,HashSet<(int,int)> selection,out int[][] chains,out bool closed)
        {
            chains=null;closed=false;
            if(part==null||selection==null||selection.Count<2)return "Select facing boundary edges on both patches. Shift-click adds edges; double-click selects a full boundary loop.";
            var edges=StudioRetopologyOperations.Edges(part);
            var next=new Dictionary<int,int>();var previous=new Dictionary<int,int>();
            foreach(var edge in selection)
            {
                if(!edges.TryGetValue(edge,out var faces)||faces.Count!=1)return "Bridge requires open boundary edges (edges with a face on only one side).";
                var ids=part.faces[faces[0]].vertices;
                for(int i=0;i<ids.Length;i++)
                {
                    int a=ids[i],b=ids[(i+1)%ids.Length];if(StudioRetopologyOperations.Edge(a,b)!=edge)continue;
                    if(next.ContainsKey(a)||previous.ContainsKey(b))return "Selected borders branch or have inconsistent face winding. Select two simple edge chains.";
                    next.Add(a,b);previous.Add(b,a);
                }
            }
            var pending=new HashSet<int>(next.Keys);var paths=new List<int[]>();var rings=new List<bool>();
            while(pending.Count>0)
            {
                int start=pending.FirstOrDefault(v=>!previous.ContainsKey(v));
                if(!pending.Contains(start)||previous.ContainsKey(start))start=pending.First();
                var path=new List<int>{start};int current=start;
                while(pending.Remove(current))
                {
                    int end=next[current];if(end==start){current=end;break;}
                    path.Add(end);current=end;
                }
                bool ring=current==start;
                paths.Add(path.ToArray());rings.Add(ring);
            }
            if(paths.Count!=2)return "Select exactly two separate boundary chains or loops, one on each side of the gap.";
            if(rings[0]!=rings[1])return "Select two open chains or two closed loops, not one of each.";
            if(paths[0].Length!=paths[1].Length)return "Both borders need the same number of edges. Select matching sections or adjust their patch divisions.";
            if(paths[0].Intersect(paths[1]).Any())return "The two borders must not share vertices.";
            chains=paths.ToArray();closed=rings[0];return null;
        }
        internal static void Bridge(Part part,HashSet<(int,int)> selection,int segments,StudioRetopologySurface surface,float offset)
        {
            string issue=Validate(part,selection,out var chains,out bool closed);if(issue!=null)throw new InvalidOperationException(issue);
            if(closed){StudioRetopologyLoops.Bridge(part,new HashSet<int>(chains.SelectMany(c=>c)),segments,surface,offset);return;}
            var a=chains[0];var b=chains[1].Reverse().ToArray();
            int material=part.faces.First(f=>f.vertices.Contains(a[0])&&f.vertices.Contains(a[1])).material;
            segments=Mathf.Clamp(segments,1,24);var previous=a;
            for(int step=1;step<=segments;step++)
            {
                var row=b;
                if(step<segments)
                {
                    row=new int[a.Length];float t=(float)step/segments;
                    for(int i=0;i<a.Length;i++)
                    {
                        var point=Vector3.Lerp(part.vertices[a[i]],part.vertices[b[i]],t);
                        row[i]=part.vertices.Count;part.vertices.Add(surface==null?point:surface.Project(point,offset));
                        part.uv.Add(Vector2.Lerp(part.uv[a[i]],part.uv[b[i]],t));
                    }
                }
                for(int i=0;i+1<a.Length;i++)part.faces.Add(new Face(new[]{previous[i+1],previous[i],row[i],row[i+1]},material));
                previous=row;
            }
            part.needsUV=true;
        }
    }
}
