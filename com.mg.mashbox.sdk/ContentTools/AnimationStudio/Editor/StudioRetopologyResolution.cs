using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioRetopologyResolution
    {
        // Propagate opposite-edge cuts through quad strips so refinement never
        // leaves T-junctions. Nearby faces may split to maintain connectivity.
        internal static int Increase(Part p,HashSet<int> painted,StudioRetopologySurface surface,float offset)
        {
            var cuts=new HashSet<(int,int)>();int count=0;
            foreach(int f in painted.Where(i=>i>=0&&i<p.faces.Count))
            {var ids=p.faces[f].vertices;if(ids.Length!=4)continue;count++;for(int i=0;i<4;i++)cuts.Add(StudioRetopologyOperations.Edge(ids[i],ids[(i+1)%4]));}
            if(count==0)return 0;
            bool changed=true;
            while(changed)
            {
                changed=false;
                foreach(var face in p.faces)
                {
                    var ids=face.vertices;var split=Enumerable.Range(0,ids.Length).Where(i=>cuts.Contains(StudioRetopologyOperations.Edge(ids[i],ids[(i+1)%ids.Length]))).ToArray();
                    if(split.Length==0)continue;
                    if(ids.Length!=4)throw new InvalidOperationException("Resolution increase needs quad strips here. This stroke reaches a triangle or n-gon; use Quad Draw to clean that transition first.");
                    foreach(int i in split)changed|=cuts.Add(StudioRetopologyOperations.Edge(ids[(i+2)%4],ids[(i+3)%4]));
                }
            }
            var midpoint=new Dictionary<(int,int),int>();
            foreach(var edge in cuts)
            {
                var point=(p.vertices[edge.Item1]+p.vertices[edge.Item2])*.5f;
                midpoint[edge]=p.vertices.Count;p.vertices.Add(surface==null?point:surface.Project(point,offset));p.uv.Add((p.uv[edge.Item1]+p.uv[edge.Item2])*.5f);
            }
            var output=new List<Face>();
            foreach(var face in p.faces)
            {
                var ids=face.vertices;var split=Enumerable.Range(0,ids.Length).Where(i=>cuts.Contains(StudioRetopologyOperations.Edge(ids[i],ids[(i+1)%ids.Length]))).ToArray();
                if(split.Length==0){output.Add(face);continue;}
                int Mid(int i)=>midpoint[StudioRetopologyOperations.Edge(ids[i%4],ids[(i+1)%4])];
                if(split.Length==4)
                {
                    var point=Vector3.zero;var uv=Vector2.zero;foreach(int i in ids){point+=p.vertices[i];uv+=p.uv[i];}point*=.25f;
                    int center=p.vertices.Count;p.vertices.Add(surface==null?point:surface.Project(point,offset));p.uv.Add(uv*.25f);
                    for(int i=0;i<4;i++)output.Add(new Face(new[]{ids[i],Mid(i),center,Mid((i+3)%4)},face.material));
                }
                else
                {
                    int i=split[0];output.Add(new Face(new[]{ids[i],Mid(i),Mid(i+2),ids[(i+3)%4]},face.material));
                    output.Add(new Face(new[]{Mid(i),ids[(i+1)%4],ids[(i+2)%4],Mid(i+2)},face.material));
                }
            }
            p.faces=output;p.needsUV=true;return count;
        }
        internal static int Decrease(Part p,HashSet<int> painted)
        {
            var targets=new HashSet<Face>(painted.Where(i=>i>=0&&i<p.faces.Count).Select(i=>p.faces[i]));
            var candidates=targets.SelectMany(f=>f.vertices).Distinct().ToArray();int count=0;
            foreach(int center in candidates)
            {
                var block=p.faces.Where(f=>f.vertices.Contains(center)).ToArray();
                if(block.Length!=4||block.Any(f=>f.vertices.Length!=4||!targets.Contains(f))||block.Select(f=>f.material).Distinct().Count()!=1)continue;
                var mids=new HashSet<int>();var directed=new Dictionary<int,int>();bool valid=true;
                foreach(var face in block)
                {
                    var ids=face.vertices;int c=Array.IndexOf(ids,center);mids.Add(ids[(c+1)%4]);mids.Add(ids[(c+3)%4]);
                    for(int j=0;j<4;j++)
                    {
                        int a=ids[j],b=ids[(j+1)%4];if(a==center||b==center)continue;
                        if(directed.ContainsKey(a)){valid=false;break;}directed[a]=b;
                    }
                }
                if(!valid||mids.Count!=4||directed.Count!=8)continue;
                int first=directed.Keys.First(),current=first;var ring=new List<int>();
                do{if(!directed.TryGetValue(current,out int next)){valid=false;break;}ring.Add(current);current=next;}while(current!=first&&ring.Count<=8);
                var outer=ring.Where(i=>!mids.Contains(i)).ToArray();
                if(!valid||ring.Count!=8||current!=first||outer.Length!=4)continue;
                var neighbors=p.faces.Except(block).Where(f=>f.vertices.Any(mids.Contains)).ToArray();
                // Reject non-manifold or unrelated branches incident to removed midpoints.
                var edges=StudioRetopologyOperations.Edges(p);
                if(edges.Any(e=>(mids.Contains(e.Key.Item1)||mids.Contains(e.Key.Item2))&&e.Value.Count>2))continue;
                // Shared border vertices must survive. An eight-vertex border can
                // be covered by three quads without its center, avoiding T-junctions.
                var replacements=new List<Face>();
                if(neighbors.Length==0)replacements.Add(new Face(outer,block[0].material));
                else
                {
                    int start=ring.FindIndex(mids.Contains);var r=Enumerable.Range(0,8).Select(i=>ring[(start+i)%8]).ToArray();
                    for(int i=1;i<7;i+=2)replacements.Add(new Face(new[]{r[0],r[i],r[i+1],r[i+2]},block[0].material));
                }
                var normal=Vector3.zero;
                foreach(var face in block){var ids=face.vertices;normal+=Vector3.Cross(p.vertices[ids[1]]-p.vertices[ids[0]],p.vertices[ids[2]]-p.vertices[ids[0]]);}
                if(normal.sqrMagnitude<1e-16f)continue;normal.Normalize();
                if(replacements.Any(f=>Enumerable.Range(1,2).Any(i=>Vector3.Dot(Vector3.Cross(p.vertices[f.vertices[i]]-p.vertices[f.vertices[0]],p.vertices[f.vertices[i+1]]-p.vertices[f.vertices[0]]),normal)<=1e-12f)))continue;
                foreach(var face in block)p.faces.Remove(face);
                p.faces.AddRange(replacements);count++;
            }
            if(count>0){StudioRetopologyOperations.Compact(p);p.needsUV=true;}return count;
        }
    }
}
