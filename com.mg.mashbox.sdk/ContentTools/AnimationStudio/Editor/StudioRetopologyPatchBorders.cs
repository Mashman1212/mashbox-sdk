using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioRetopologyPatchBorders
    {
        internal static int[][] Find(Part part,Vector3[] corners,float tolerance)
        {
            var edges=StudioRetopologyOperations.Edges(part);
            var neighbors=new Dictionary<int,List<int>>();
            foreach(var edge in edges.Where(e=>e.Value.Count==1))
            {
                int a=edge.Key.Item1,b=edge.Key.Item2;
                if(!neighbors.ContainsKey(a))neighbors[a]=new List<int>();
                if(!neighbors.ContainsKey(b))neighbors[b]=new List<int>();
                neighbors[a].Add(b);neighbors[b].Add(a);
            }
            var result=new int[4][];
            for(int side=0;side<4;side++)
            {
                var from=corners[side];var to=corners[(side+1)%4];var axis=to-from;float length=axis.magnitude;
                if(length<=tolerance)continue;
                int start=part.vertices.FindIndex(p=>(p-from).sqrMagnitude<=tolerance*tolerance);
                int end=part.vertices.FindIndex(p=>(p-to).sqrMagnitude<=tolerance*tolerance);
                if(start<0||end<0||start==end||!neighbors.ContainsKey(start)||!neighbors.ContainsKey(end))continue;
                // A projected border can bend back along its chord or wrap around
                // a leg. Trace its actual topology instead of clipping it to a line.
                var paths=new List<int[]>();
                foreach(int first in neighbors[start])
                {
                    var path=new List<int>{start};var visited=new HashSet<int>{start};int previous=start,current=first;
                    while(visited.Add(current))
                    {
                        path.Add(current);if(current==end){paths.Add(path.ToArray());break;}
                        bool otherCorner=Enumerable.Range(0,4).Where(c=>c!=side&&c!=(side+1)%4)
                            .Any(c=>(part.vertices[current]-corners[c]).sqrMagnitude<=tolerance*tolerance);
                        if(otherCorner||neighbors[current].Count!=2)break;
                        int next=neighbors[current][0]==previous?neighbors[current][1]:neighbors[current][0];
                        previous=current;current=next;
                    }
                }
                float PathLength(int[] path)
                {float sum=0;for(int i=1;i<path.Length;i++)sum+=Vector3.Distance(part.vertices[path[i-1]],part.vertices[path[i]]);return sum;}
                // Connectivity alone does not identify a seam: on a garment the
                // same boundary may travel around the back to the other corner.
                // Keep curved local seams, but do not reuse remote perimeter arcs.
                bool LocalRoute(int[] path)
                {
                    float corridor = Mathf.Max(tolerance * 4, length * .5f);
                    foreach (int vertex in path)
                    {
                        var p = part.vertices[vertex];
                        float t = Mathf.Clamp01(Vector3.Dot(p-from,axis)/(length*length));
                        if ((p-(from+axis*t)).sqrMagnitude > corridor*corridor) return false;
                    }
                    return true;
                }
                paths=paths.Where(LocalRoute).OrderBy(PathLength).ToList();
                // Corners on one connected perimeter can still border an empty gap.
                // A route through another corner or around the back is not a seam.
                if(paths.Count==0)continue;
                if(PathLength(paths[0])>length*4||paths.Count>1&&PathLength(paths[1])<PathLength(paths[0])*1.1f)
                    throw new InvalidOperationException("Two possible border routes between the snapped corners. Choose closer corners to identify the seam; no patch was added.");
                result[side]=paths[0];
            }
            return result;
        }
        internal static void Match(Part part,int[][] sides,ref int columns,ref int rows,ref bool reverse)
        {
            int Count(int side)=>sides[side]==null?0:sides[side].Length-1;
            int bottom=Count(0),right=Count(1),top=Count(2),left=Count(3);
            if(bottom>0&&top>0&&bottom!=top||left>0&&right>0&&left!=right)
                throw new InvalidOperationException("Opposite existing borders have different edge counts. Draw smaller patches or bridge matching border sections; no geometry was added.");
            if(bottom>0||top>0)columns=Mathf.Max(bottom,top);
            if(left>0||right>0)rows=Mathf.Max(left,right);
            var edges=StudioRetopologyOperations.Edges(part);bool? winding=null;
            foreach(var side in sides)
            {
                if(side==null)continue;
                for(int j=0;j+1<side.Length;j++)
                {
                    var edge=StudioRetopologyOperations.Edge(side[j],side[j+1]);
                    if(!edges.TryGetValue(edge,out var faces)||faces.Count!=1)throw new InvalidOperationException("Patch attachment must follow an open border.");
                    var ids=part.faces[faces[0]].vertices;
                    bool forward=Enumerable.Range(0,ids.Length).Any(i=>ids[i]==side[j]&&ids[(i+1)%ids.Length]==side[j+1]);
                    if(winding.HasValue&&winding.Value!=forward)throw new InvalidOperationException("These borders cannot form one consistently wound patch. Check the selected corners.");
                    winding=forward;
                }
            }
            if(winding.HasValue)reverse=winding.Value;
        }
    }
}
