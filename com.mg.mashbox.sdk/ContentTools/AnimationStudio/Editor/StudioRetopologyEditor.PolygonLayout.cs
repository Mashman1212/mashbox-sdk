using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        // Fit four boundary chains, then use the same transfinite rows/columns
        // as Draw Patch. Extra clicks shape the sides instead of seeding fans.
        private static bool TryBuildPolygonLayout(Vector3[] points,float target,int divisions,Vector3 normal,out Part result)
        {
            result=null;
            if(points.Length<4)return false;
            int count=points.Length;
            var axis=Enumerable.Range(0,count).Select(i=>Vector3.ProjectOnPlane(points[(i+1)%count]-points[i],normal)).OrderByDescending(v=>v.sqrMagnitude).First().normalized;
            var up=Vector3.Cross(normal,axis);
            var plane=points.Select(p=>new Vector2(Vector3.Dot(p-points[0],axis),Vector3.Dot(p-points[0],up))).ToArray();
            var lengths=new float[count];float perimeter=0;
            for(int i=0;i<count;i++){lengths[i]=Vector3.Distance(points[i],points[(i+1)%count]);perimeter+=lengths[i];}
            float scale=plane.Max(p=>p.magnitude);if(scale<1e-9f)return false;
            float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
            float Turn(int i)
            {
                var a=(plane[i]-plane[(i+count-1)%count]).normalized;
                var b=(plane[(i+1)%count]-plane[i]).normalized;
                return Cross(a,b)>0?Mathf.Acos(Mathf.Clamp(Vector2.Dot(a,b),-1,1)):0;
            }
            var candidates=new HashSet<int>(Enumerable.Range(0,count).OrderByDescending(Turn).Take(8));
            // Include evenly spaced candidates so smooth outlines and circles
            // do not cluster all four corners at the beginning of the stroke.
            for(int i=0;i<8;i++)
            {
                float distance=perimeter*i/8;int index=0;
                while(index<count-1&&distance>lengths[index]){distance-=lengths[index];index++;}
                candidates.Add(index);
            }
            var choices=candidates.OrderBy(i=>i).ToArray();
            var sideCosts=new Dictionary<(int,int),float>();
            float SideCost(int a,int b)
            {
                if(sideCosts.TryGetValue((a,b),out float cached))return cached;
                var delta=plane[b]-plane[a];float span=delta.sqrMagnitude;
                if(span<scale*scale*1e-12f)return float.PositiveInfinity;
                float error=0,arc=0;
                for(int i=a;i!=b;i=(i+1)%count)
                {
                    int next=(i+1)%count;
                    float d=Cross(delta,plane[next]-plane[a]);
                    error+=d*d/span*lengths[i];arc+=lengths[i];
                }
                // A tiny tie-breaker distributes corners on featureless curves.
                error=error/(perimeter*scale*scale)+.000001f*arc*arc/(perimeter*perimeter);
                sideCosts[(a,b)]=error;return error;
            }
            var fits=new List<(float cost,int[] corners)>();
            for(int a=0;a<choices.Length-3;a++)for(int b=a+1;b<choices.Length-2;b++)
                for(int c=b+1;c<choices.Length-1;c++)for(int d=c+1;d<choices.Length;d++)
                {
                    var ids=new[]{choices[a],choices[b],choices[c],choices[d]};
                    if(Enumerable.Range(0,4).Any(i=>Cross(plane[ids[(i+1)%4]]-plane[ids[i]],plane[ids[(i+2)%4]]-plane[ids[(i+1)%4]])<=scale*scale*1e-9f))continue;
                    fits.Add((Enumerable.Range(0,4).Sum(i=>SideCost(ids[i],ids[(i+1)%4])),ids));
                }
            // Test a few layouts rather than accepting a folded parameterization.
            foreach(var fit in fits.OrderBy(f=>f.cost).Take(8))
            {
                var sides=new int[4][];var arcs=new float[4];
                for(int s=0;s<4;s++)
                {
                    var side=new List<int>();int i=fit.corners[s],end=fit.corners[(s+1)%4];
                    while(i!=end){side.Add(i);arcs[s]+=lengths[i];i=(i+1)%count;}side.Add(end);sides[s]=side.ToArray();
                }
                int columns=Mathf.Clamp(target>0?Mathf.CeilToInt((arcs[0]+arcs[2])/(2*target)):divisions,1,96);
                int rows=Mathf.Clamp(target>0?Mathf.CeilToInt((arcs[1]+arcs[3])/(2*target)):divisions,1,96);
                columns=Mathf.Max(columns,Mathf.Max(sides[0].Length,sides[2].Length)-1);
                rows=Mathf.Max(rows,Mathf.Max(sides[1].Length,sides[3].Length)-1);
                if(columns>96||rows>96)continue;
                // Give each clicked segment at least one interval, distributing
                // the rest by length. Every outline click remains an exact vertex.
                Vector3[] Sample(int side,int intervals,out int[] original)
                {
                    var ids=sides[side];var positions=new int[ids.Length];float arc=0;
                    for(int i=1;i<ids.Length-1;i++)
                    {
                        arc+=lengths[ids[i-1]];
                        positions[i]=Mathf.Clamp(Mathf.RoundToInt(arc/arcs[side]*intervals),positions[i-1]+1,intervals-(ids.Length-1-i));
                    }
                    positions[ids.Length-1]=intervals;
                    var values=new Vector3[intervals+1];original=Enumerable.Repeat(-1,intervals+1).ToArray();
                    for(int i=0;i<ids.Length;i++)original[positions[i]]=ids[i];
                    for(int i=0;i<ids.Length-1;i++)for(int j=positions[i];j<=positions[i+1];j++)
                        values[j]=Vector3.Lerp(points[ids[i]],points[ids[i+1]],(float)(j-positions[i])/(positions[i+1]-positions[i]));
                    return values;
                }
                var bottom=Sample(0,columns,out var bottomIDs);var right=Sample(1,rows,out var rightIDs);
                var top=Sample(2,columns,out var topIDs);var left=Sample(3,rows,out var leftIDs);
                var patch=new Part{vertices=points.ToList(),uv=points.Select(p=>Vector2.zero).ToList()};
                var grid=new int[columns+1,rows+1];
                for(int y=0;y<=rows;y++)for(int x=0;x<=columns;x++)
                {
                    float u=(float)x/columns,v=(float)y/rows;
                    var bilinear=Vector3.Lerp(Vector3.Lerp(bottom[0],bottom[columns],u),Vector3.Lerp(top[columns],top[0],u),v);
                    var position=Vector3.Lerp(bottom[x],top[columns-x],v)+Vector3.Lerp(left[rows-y],right[y],u)-bilinear;
                    int id=y==0?bottomIDs[x]:x==columns?rightIDs[y]:y==rows?topIDs[columns-x]:x==0?leftIDs[rows-y]:-1;
                    if(id<0){id=patch.vertices.Count;patch.vertices.Add(position);patch.uv.Add(new Vector2(u,v));}
                    grid[x,y]=id;
                }
                bool valid=true;
                for(int y=0;y<rows&&valid;y++)for(int x=0;x<columns;x++)
                {
                    var ids=new[]{grid[x,y],grid[x+1,y],grid[x+1,y+1],grid[x,y+1]};
                    for(int i=0;i<4;i++)
                    {
                        var a=patch.vertices[ids[i]];var b=patch.vertices[ids[(i+1)%4]];var c=patch.vertices[ids[(i+2)%4]];
                        if(Vector3.Dot(Vector3.Cross(b-a,c-b),normal)<=scale*scale*1e-10f){valid=false;break;}
                    }
                    if(!valid)break;
                    patch.faces.Add(new Face(ids));
                }
                if(valid){result=patch;return true;}
            }
            return false;
        }
    }
}
