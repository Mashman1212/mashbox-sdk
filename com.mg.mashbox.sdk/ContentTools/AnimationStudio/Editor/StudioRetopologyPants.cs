using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;
namespace MashBoxSDK.AnimationStudio
{
    // A garment cage, not triangle decimation. Cross sections fit a fixed pants
    // layout so surface wrinkles cannot redirect the longitudinal edge loops.
    internal static class StudioRetopologyPants
    {
        private struct Segment { internal Vector2 a,b; }
        private sealed class Sections
        {
            readonly Vector3[] vertices; readonly int[] triangles;
            internal readonly Bounds bounds;
            internal Sections(Mesh mesh){vertices=mesh.vertices;triangles=mesh.triangles;bounds=mesh.bounds;}
            internal List<Segment> At(float y)
            {
                var result=new List<Segment>();
                for(int t=0;t<triangles.Length;t+=3)
                {
                    var a=vertices[triangles[t]];var b=vertices[triangles[t+1]];var c=vertices[triangles[t+2]];
                    Vector2 first=default,second=default;int count=0;
                    void Cut(Vector3 p,Vector3 q)
                    {
                        if((p.y<=y&&q.y>y)||(q.y<=y&&p.y>y))
                        {var v=Vector3.Lerp(p,q,(y-p.y)/(q.y-p.y));if(count++==0)first=new Vector2(v.x,v.z);else second=new Vector2(v.x,v.z);}
                    }
                    Cut(a,b);Cut(b,c);Cut(c,a);
                    if(count==2&&(first-second).sqrMagnitude>1e-16f)result.Add(new Segment{a=first,b=second});
                }
                return result;
            }
        }
        private static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        private static Vector2 Center(List<Segment> segments)
        {
            if(segments.Count==0)throw new InvalidOperationException("Pants cross section is empty. Use upright pants with two separated legs.");
            float x0=float.PositiveInfinity,x1=float.NegativeInfinity,z0=x0,z1=x1;
            foreach(var s in segments)foreach(var p in new[]{s.a,s.b}){x0=Mathf.Min(x0,p.x);x1=Mathf.Max(x1,p.x);z0=Mathf.Min(z0,p.y);z1=Mathf.Max(z1,p.y);}
            return new Vector2((x0+x1)*.5f,(z0+z1)*.5f);
        }
        private static Vector3 Sample(List<Segment> segments,Vector2 center,float angle,float y)
        {
            var d=new Vector2(-Mathf.Sin(angle),Mathf.Cos(angle));float best=float.PositiveInfinity;
            foreach(var s in segments)
            {
                var edge=s.b-s.a;float den=Cross(d,edge);if(Mathf.Abs(den)<1e-10f)continue;
                float distance=Cross(s.a-center,edge)/den,u=Cross(s.a-center,d)/den;
                // The first outward surface excludes pockets and hanging accessories.
                if(distance>0&&u>=-1e-5f&&u<=1.00001f)best=Mathf.Min(best,distance);
            }
            if(float.IsInfinity(best))throw new InvalidOperationException($"Could not fit pants cross section at y={y}, angle={angle}, center={center}, segments={segments.Count}.");
            var p=center+d*best;return new Vector3(p.x,y,p.y);
        }
        private static bool Split(List<Segment> section,float middle,float minGap,out float split)
        {
            split=middle;
            var intervals=section.Select(s=>(lo:Mathf.Min(s.a.x,s.b.x),hi:Mathf.Max(s.a.x,s.b.x))).OrderBy(s=>s.lo).ToArray();
            if(intervals.Length==0)return false;
            float end=intervals[0].hi,best=0;
            for(int i=1;i<intervals.Length;i++)
            {
                float gap=intervals[i].lo-end;
                if(gap>minGap&&Mathf.Abs((end+intervals[i].lo)*.5f-middle)<minGap*8&&gap>best)
                {best=gap;split=(end+intervals[i].lo)*.5f;}
                end=Mathf.Max(end,intervals[i].hi);
            }
            return best>0;
        }
        internal static Part Generate(Mesh mesh,int triangleBudget,float offset,Func<float,bool> cancelled=null)
        {
            var sections=new Sections(mesh);var bounds=sections.bounds;float h=bounds.size.y;
            if(h<=0||h<bounds.size.x*.7f)throw new InvalidOperationException("Pants generation expects upright Y-up pants with the legs side by side on X.");
            float middle=bounds.center.x,crotch=0,split=middle;bool found=false;
            // Locate the transition from separated legs to the joined pelvis.
            for(int i=20;i<=85;i++)
            {
                float y=bounds.min.y+h*i/100f;
                if(Split(sections.At(y),middle,bounds.size.x*.025f,out float at))
                {crotch=y;split=at;found=true;}
            }
            if(!found||crotch>bounds.min.y+h*.82f)throw new InvalidOperationException("Could not identify a pants crotch. Use the Pants preset on one upright pants mesh, without the body or accessories combined into it.");
            float top=bounds.max.y-h*.012f,join=Mathf.Min(crotch+h*.035f,top-h*.1f),bottom=bounds.min.y+h*.012f;
            // Waist count is divisible by six: each leg uses 2/3 of it, with a
            // shared crotch strip joining the two half-waist loops.
            int fittingBudget=Mathf.Max(5000,triangleBudget);
            int waist=Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(Mathf.Max(200,fittingBudget)*.26f)/6)*6,18,96);
            int half=waist/2,bridge=waist/6,legCount=half+bridge;
            float ratio=(top-join)/(join-bottom);
            int legRows=Mathf.Max(4,Mathf.RoundToInt(fittingBudget/(2f*(waist*ratio+2*legCount))));
            legRows=Mathf.Min(legRows,128);int hipRows=Mathf.Max(2,Mathf.RoundToInt(legRows*ratio));
            var p=new Part{name="Pants quad cage",reference=mesh,needsUV=true};
            int Add(Vector3 position){int id=p.vertices.Count;p.vertices.Add(position);p.uv.Add(Vector2.zero);return id;}
            void Connect(int[] upper,int[] lower)
            {for(int i=0;i<upper.Length;i++){int j=(i+1)%upper.Length;p.faces.Add(new Face(new[]{upper[i],lower[i],lower[j],upper[j]}));}}
            // Waist/cuffs can be slanted. Find an intact section inside the opening,
            // rather than assuming the highest vertex is a complete ring.
            bool Closed(List<Segment> section,int count)
            {
                if(section.Count==0)return false;
                var c=Center(section);
                try{for(int i=0;i<count;i++)Sample(section,c,i*2*Mathf.PI/count,0);return true;}
                catch(InvalidOperationException){return false;}
            }
            for(int step=0;step<20&&!Closed(sections.At(top),waist);step++)top-=h*.005f;
            if(!Closed(sections.At(top),waist))throw new InvalidOperationException("Could not find the waist opening.");
            var hipRings=new List<int[]>();var legRings=new List<int[]>[2];
            int[] last=null;
            for(int row=0;row<=hipRows;row++)
            {
                float y=Mathf.Lerp(top,join,(float)row/hipRows);var section=sections.At(y);var center=Center(section);
                var ring=new int[waist];for(int i=0;i<waist;i++)ring[i]=Add(Sample(section,center,i*Mathf.PI*2/waist,y));
                if(last!=null)Connect(last,ring);last=ring;hipRings.Add(ring);
            }
            var surface=new StudioRetopologySurface(mesh);var crotchIds=new List<int>{last[half]};
            for(int i=1;i<bridge;i++)
            {
                var v=Vector3.Lerp(p.vertices[last[half]],p.vertices[last[0]],(float)i/bridge);v.x=split;v.y=crotch;
                crotchIds.Add(Add(surface.ProjectAlong(v,Vector3.up,0)));
            }
            crotchIds.Add(last[0]);
            for(int side=0;side<2;side++)
            {
                var start=new List<int>();
                if(side==0){for(int i=0;i<=half;i++)start.Add(last[i]);start.AddRange(crotchIds.Skip(1).Take(bridge-1));}
                else {for(int i=half;i<waist;i++)start.Add(last[i]);start.Add(last[0]);start.AddRange(crotchIds.AsEnumerable().Reverse().Skip(1).Take(bridge-1));}
                float cuff=bottom;
                List<Segment> LegSection(float y)=>sections.At(y).Where(s=>side==0?Mathf.Max(s.a.x,s.b.x)<split:Mathf.Min(s.a.x,s.b.x)>split).ToList();
                for(int step=0;step<20&&!Closed(LegSection(cuff),legCount);step++)cuff+=h*.005f;
                if(!Closed(LegSection(cuff),legCount))throw new InvalidOperationException("Could not find a closed cuff section.");
                var previous=start.ToArray();legRings[side]=new List<int[]>{previous};
                for(int row=1;row<=legRows;row++)
                {
                    if(cancelled!=null&&cancelled((side+(float)row/legRows)*.5f))throw new OperationCanceledException("Pants generation cancelled; working mesh unchanged.");
                    float t=(float)row/legRows,y=Mathf.Lerp(join,cuff,t);
                    var section=LegSection(Mathf.Min(y,crotch-h*.02f));
                    var center=Center(section);var ring=new int[legCount];
                    for(int i=0;i<legCount;i++)
                    {
                        float angle=(side==0?-.25f:.75f)*Mathf.PI+i*2*Mathf.PI/legCount;
                        var target=Sample(section,center,angle,Mathf.Min(y,crotch-h*.02f));
                        // Ease the crotch junction into regular circular leg loops.
                        float blend=Mathf.SmoothStep(0,1,Mathf.Clamp01(t*5));
                        var from=p.vertices[start[i]];from.y=y;
                        ring[i]=Add(surface.Project(Vector3.Lerp(from,target,blend),0));
                    }
                    Connect(previous,ring);previous=ring;legRings[side].Add(ring);
                }
            }
            // Fair the cage instead of reproducing every high-frequency wrinkle.
            // Pin the open hems, retaining their silhouette.
            StudioRetopologyOperations.Relax(p,null,true,0);
            StudioRetopologyOperations.Relax(p,null,true,0);
            // Connect() follows clockwise XZ rings; orient outward.
            foreach(var f in p.faces)Array.Reverse(f.vertices);
            if(offset!=0)
            {
                var normals=new Vector3[p.vertices.Count];
                foreach(var f in p.faces){var ids=f.vertices;var n=Vector3.Cross(p.vertices[ids[1]]-p.vertices[ids[0]],p.vertices[ids[2]]-p.vertices[ids[0]]);foreach(int id in ids)normals[id]+=n;}
                for(int i=0;i<p.vertices.Count;i++)p.vertices[i]+=normals[i].normalized*offset;
            }
            // Nearest-surface fitting can pinch adjacent rows at the crotch.
            // Fair only those local neighborhoods; keep the three opening rings fixed.
            bool Folded(Face f)
            {
                var a=p.vertices[f.vertices[0]];var b=p.vertices[f.vertices[1]];
                var c=p.vertices[f.vertices[2]];var d=p.vertices[f.vertices[3]];
                return Vector3.Dot(Vector3.Cross(b-a,c-a),Vector3.Cross(c-a,d-a))<=0;
            }
            for(int pass=0;pass<32;pass++)
            {
                var bad=p.faces.Where(Folded).ToArray();
                if(bad.Length==0)break;
                var affected=new HashSet<int>(bad.SelectMany(f=>f.vertices));
                var region=new HashSet<int>(affected);
                for(int ring=0;ring<2;ring++)
                {
                    affected=new HashSet<int>(region);
                    foreach(var f in p.faces)
                        if(f.vertices.Any(affected.Contains))region.UnionWith(f.vertices);
                }
                StudioRetopologyOperations.Relax(p,null,true,0,region);
            }
            int folded=p.faces.Count(Folded);
            if(folded>0||StudioRetopologyOperations.Edges(p).Any(e=>e.Value.Count>2)||StudioRetopologyLoops.Boundaries(p).Count!=3)
                throw new InvalidOperationException($"Pants cage validation failed ({folded} folded quads); the working mesh was not changed.");
            // Fit and fair the junction at a stable resolution first. Lower budgets
            // remove whole rows away from the saddle, retaining the welded quad flow.
            if(p.faces.Count*2>triangleBudget)
            {
                int[] rows={hipRings.Count-1,legRings[0].Count-1,legRings[1].Count-1};
                int Triangles()=>2*(rows[0]*waist+(rows[1]+rows[2])*legCount);
                while(Triangles()>triangleBudget)
                {
                    int candidate=-1;float density=-1;
                    for(int i=0;i<3;i++)
                    {
                        int minimum=i==0?2:5;
                        if(rows[i]<=minimum)continue;
                        float length=i==0?top-join:join-bottom;
                        float value=rows[i]/Mathf.Max(length,.00001f);
                        if(value>density){density=value;candidate=i;}
                    }
                    if(candidate<0)throw new InvalidOperationException("Triangle budget is too small for the pants junction. Increase the budget.");
                    rows[candidate]--;
                }
                p.faces.Clear();
                void Reconnect(List<int[]> rings,int count,bool leg)
                {
                    // Keep three fitted rows immediately below the saddle.
                    int keep=leg?3:0;
                    int[] previous=rings[0];
                    for(int row=1;row<=count;row++)
                    {
                        int index=leg&&row<=keep?row:keep+Mathf.RoundToInt((rings.Count-1-keep)*(float)(row-keep)/(count-keep));
                        var next=rings[index];Connect(previous,next);previous=next;
                    }
                }
                Reconnect(hipRings,rows[0],false);
                Reconnect(legRings[0],rows[1],true);Reconnect(legRings[1],rows[2],true);
                foreach(var face in p.faces)Array.Reverse(face.vertices);
                if(p.faces.Any(Folded))
                    throw new InvalidOperationException("Pants row fitting failed; the working mesh was not changed.");
                var used=p.faces.SelectMany(face=>face.vertices).Distinct().OrderBy(i=>i).ToArray();
                var remap=new Dictionary<int,int>();
                for(int i=0;i<used.Length;i++)remap[used[i]]=i;
                p.vertices=used.Select(i=>p.vertices[i]).ToList();p.uv=used.Select(i=>p.uv[i]).ToList();
                foreach(var face in p.faces)face.vertices=face.vertices.Select(i=>remap[i]).ToArray();
            }
            return p;
        }
    }
}
