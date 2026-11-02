using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        // A disk with four boundary arcs becomes a structured quad lattice.
        // Boundary vertices never move: only interior points are relaxed.
        private static List<List<int>> TryBisectGrid(BisectBuffer buffer, List<int> boundary,
            Vector3 normal, List<Face> faces, List<List<int>> render, float epsilon)
        {
            var loop = new List<int>(boundary);
            var midpoint = new Dictionary<(int,int),int>();
            if ((loop.Count & 1) != 0)
            {
                // An all-quad disk needs an even boundary. Split one longest edge,
                // not every side edge as the triangle-refinement fallback does.
                int edge = Enumerable.Range(0,loop.Count).OrderByDescending(i =>
                    (buffer.Positions[loop[i]]-buffer.Positions[loop[(i+1)%loop.Count]]).sqrMagnitude).First();
                int a=loop[edge],b=loop[(edge+1)%loop.Count];
                int mid=buffer.Interpolate(a,b,buffer.NextGeometry++);
                midpoint.Add((Math.Min(buffer.Geometry[a],buffer.Geometry[b]),Math.Max(buffer.Geometry[a],buffer.Geometry[b])),mid);
                loop.Insert(edge+1,mid);
            }
            int n=loop.Count,half=n/2;
            if(n<4 || n>512)return null;
            var chart=new CutChart(buffer.Positions[loop[0]],normal);
            var points=loop.Select(v=>chart.Project(buffer.Positions[v])).ToArray();
            var lengths=new double[n*2+1];
            for(int i=0;i<n*2;i++)lengths[i+1]=lengths[i]+Vector2.Distance(points[i%n],points[(i+1)%n]);
            double perimeter=lengths[n];if(perimeter<=epsilon*4)return null;
            var turns=new double[n];
            for(int i=0;i<n;i++)
            {
                var a=(points[i]-points[(i+n-1)%n]).normalized;var b=(points[(i+1)%n]-points[i]).normalized;
                turns[i]=Math.Atan2(CutCross(a,b),Vector2.Dot(a,b));
            }
            var starts=new HashSet<int>(Enumerable.Range(0,n).Where(i=>n<=128||i%Math.Max(1,n/64)==0));
            foreach(int i in Enumerable.Range(0,n).OrderByDescending(i=>turns[i]).Take(16))starts.Add(i);
            var candidates=new List<(double score,int start,int u,int v)>();
            foreach(int start in starts.OrderBy(i=>i))for(int u=1;u<half;u++)
            {
                int v=half-u;if(u*v>16384)continue;
                int c1=start+u,c2=start+half,c3=start+half+u;
                double a=lengths[c1]-lengths[start],b=lengths[c2]-lengths[c1],c=lengths[c3]-lengths[c2],d=lengths[start+n]-lengths[c3];
                double du=(a+c)/(2*u),dv=(b+d)/(2*v);
                double score=Math.Abs(Math.Log(du/dv))+Math.Abs(Math.Log(a/c))+Math.Abs(Math.Log(b/d));
                var corners=new[]{start,c1%n,c2%n,c3%n};
                if(corners.Any(i=>turns[i]<=1e-6))continue; // A straight boundary sample cannot be a grid corner.
                for(int k=0;k<4;k++)
                {
                    int corner=corners[k];score+=.35*Math.Abs(Math.PI*.5-turns[corner]);
                    var incoming=(points[corner]-points[corners[(k+3)%4]]).normalized;
                    var outgoing=(points[corners[(k+1)%4]]-points[corner]).normalized;
                    score+=2*Math.Abs(Vector2.Dot(incoming,outgoing));
                }
                candidates.Add((score,start,u,v));
            }
            Vector2[] bestPoints=null;int[] bestBoundary=null;int bestU=0,bestV=0;double bestScore=double.PositiveInfinity;
            var ordered=candidates.OrderBy(c=>c.score).ThenBy(c=>c.start).ThenBy(c=>c.u).ToArray();
            // Uneven production boundaries need several possible corner starts
            // per aspect ratio; cheap perimeter scores alone can miss the clean grid.
            int trials=n<=192?64:12;
            var shortlist=ordered.GroupBy(c=>c.u).SelectMany(g=>g.Take(n<=192?2:1)).Take(trials).Concat(ordered.Take(trials)).Distinct();
            foreach(var candidate in shortlist)
            {
                int u=candidate.u,v=candidate.v,stride=u+1;var grid=new Vector2[(u+1)*(v+1)];
                var ids=Enumerable.Repeat(-1,grid.Length).ToArray();
                void Boundary(int x,int y,int offset){int at=y*stride+x;int k=(candidate.start+offset)%n;grid[at]=points[k];ids[at]=loop[k];}
                for(int x=0;x<=u;x++){Boundary(x,0,x);Boundary(x,v,2*u+v-x);}
                for(int y=0;y<=v;y++){Boundary(u,y,u+y);Boundary(0,y,n-y);}
                for(int y=1;y<v;y++)for(int x=1;x<u;x++)
                {
                    float s=(float)x/u,t=(float)y/v;
                    grid[y*stride+x]=Vector2.Lerp(grid[x],grid[v*stride+x],t)+Vector2.Lerp(grid[y*stride],grid[y*stride+u],s)
                        -Vector2.Lerp(Vector2.Lerp(grid[0],grid[u],s),Vector2.Lerp(grid[v*stride],grid[v*stride+u],s),t);
                }
                // Discrete harmonic solve: spacing follows the boundary while the
                // regular valence-four interior avoids triangle-fan poles.
                float stop=(float)(perimeter*1e-7);int passes=Math.Min(320,32+4*Math.Max(u,v));
                for(int pass=0;pass<passes;pass++)
                {
                    float change=0;
                    for(int y=1;y<v;y++)for(int x=1;x<u;x++)
                    {
                        int i=y*stride+x;var next=(grid[i-1]+grid[i+1]+grid[i-stride]+grid[i+stride])*.25f;
                        change=Math.Max(change,(next-grid[i]).sqrMagnitude);grid[i]=next;
                    }
                    if(change<stop*stop)break;
                }
                double quality=0,minQuality=1;bool valid=true;
                for(int y=0;y<v&&valid;y++)for(int x=0;x<u;x++)
                {
                    int i=y*stride+x;var quad=new[]{grid[i],grid[i+1],grid[i+1+stride],grid[i+stride]};
                    double q=BisectQuadQuality(quad,epsilon);
                    if(q<=1e-5){valid=false;break;}minQuality=Math.Min(minQuality,q);quality+=1-q;
                }
                if(!valid)continue;
                double score=quality/(u*v)+.3*(1-minQuality)+.025*candidate.score;
                if(score>=bestScore)continue;
                bestScore=score;bestPoints=grid;bestBoundary=ids;bestU=u;bestV=v;
            }
            if(bestPoints==null)return null;
            int width=bestU+1;
            for(int y=1;y<bestV;y++)for(int x=1;x<bestU;x++)
            {
                int i=y*width+x;
                int id=buffer.Interpolate(bestBoundary[x],bestBoundary[bestV*width+x],buffer.NextGeometry++,(float)y/bestV);
                var p=bestPoints[i];buffer.Positions[id]=chart.Origin+chart.X*p.x+chart.Y*p.y;bestBoundary[i]=id;
            }
            var quads=new List<List<int>>(bestU*bestV);
            for(int y=0;y<bestV;y++)for(int x=0;x<bestU;x++)
            {int i=y*width+x;quads.Add(new List<int>{bestBoundary[i],bestBoundary[i+1],bestBoundary[i+1+width],bestBoundary[i+width]});}
            PropagateBisectMidpoints(buffer,midpoint,faces,render);
            RelaxBisectPatch(buffer,quads,normal,epsilon);
            return quads;
        }

        private static double BisectQuadQuality(Vector2[] p,float epsilon)
        {
            double quality=1;
            for(int i=0;i<4;i++)
            {
                var a=p[(i+1)%4]-p[i];var b=p[(i+3)%4]-p[i];
                double cross=CutCross(a,b);if(cross<=epsilon*epsilon)return 0;
                // Scaled Jacobian plus edge aspect discourages skinny diamonds.
                quality=Math.Min(quality,2*cross/((double)a.sqrMagnitude+b.sqrMagnitude));
            }
            return quality;
        }

        private static void PropagateBisectMidpoints(BisectBuffer buffer,Dictionary<(int,int),int> mids,List<Face> faces,List<List<int>> render)
        {
            if(mids.Count==0)return;
            for(int f=0;f<faces.Count;f++)
            {
                var old=faces[f].Vertices;var expanded=new List<int>();var splits=new List<(int a,int b,int mid)>();
                for(int i=0;i<old.Count;i++)
                {
                    int a=old[i],b=old[(i+1)%old.Count];expanded.Add(a);
                    var key=(Math.Min(buffer.Geometry[a],buffer.Geometry[b]),Math.Max(buffer.Geometry[a],buffer.Geometry[b]));
                    if(mids.TryGetValue(key,out int mid))
                    {int added=buffer.Interpolate(a,b,buffer.Geometry[mid]);expanded.Add(added);splits.Add((a,b,added));}
                }
                if(splits.Count==0)continue;
                faces[f].Vertices=expanded;foreach(var split in splits)SplitSliceBoundaryTriangles(render[f],split.a,split.b,split.mid);
            }
        }

        private static void RelaxBisectPatch(BisectBuffer buffer,List<List<int>> quads,Vector3 normal,float epsilon)
        {
            var chart=new CutChart(buffer.Positions[quads[0][0]],normal);
            var uses=new Dictionary<(int,int),int>();var incident=new Dictionary<int,List<int>>();var neighbors=new Dictionary<int,HashSet<int>>();
            for(int f=0;f<quads.Count;f++)for(int k=0;k<4;k++)
            {
                int a=quads[f][k],b=quads[f][(k+1)%4];var edge=(Math.Min(a,b),Math.Max(a,b));uses.TryGetValue(edge,out int count);uses[edge]=count+1;
                if(!incident.TryGetValue(a,out var list))incident[a]=list=new List<int>();list.Add(f);
                if(!neighbors.TryGetValue(a,out var near))neighbors[a]=near=new HashSet<int>();near.Add(b);near.Add(quads[f][(k+3)%4]);
            }
            var fixedVertices=new HashSet<int>(uses.Where(p=>p.Value==1).SelectMany(p=>new[]{p.Key.Item1,p.Key.Item2}));
            double Quality(int f)=>BisectQuadQuality(quads[f].Select(v=>chart.Project(buffer.Positions[v])).ToArray(),epsilon);
            double Energy(List<int> adjacent)
            {
                double value=0;
                foreach(int f in adjacent){double q=Quality(f);if(q<=1e-8)return double.PositiveInfinity;value-=Math.Log(q);}
                return value;
            }
            var movable=incident.Keys.Where(i=>!fixedVertices.Contains(i)).OrderBy(i=>i).ToArray();
            var directions=new[]{chart.X,-chart.X,chart.Y,-chart.Y,(chart.X+chart.Y).normalized,(chart.X-chart.Y).normalized,(-chart.X+chart.Y).normalized,(-chart.X-chart.Y).normalized};
            // Optimize shape, not only Laplacian position: irregular boundary
            // spacing otherwise carries thin rows across the entire patch.
            for(int pass=0;pass<20;pass++)
            {
                bool changed=false;
                foreach(int id in movable)
                {
                    var old=buffer.Positions[id];var best=old;double energy=Energy(incident[id]);
                    float step=neighbors[id].Min(v=>(buffer.Positions[v]-old).magnitude)*(.3f/(1+pass*.15f));
                    foreach(var direction in directions)
                    {
                        buffer.Positions[id]=old+direction*step;double next=Energy(incident[id]);
                        if(next<energy-1e-6){energy=next;best=buffer.Positions[id];}
                    }
                    buffer.Positions[id]=best;if(best!=old)changed=true;
                }
                if(!changed)break;
            }
        }
    }
}
