using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        private sealed class CircleRegion
        {
            internal List<int> Ring;
            internal HashSet<int> Faces=new HashSet<int>();
            internal HashSet<int> Interior=new HashSet<int>();
        }
        // p: radius offset, degrees, normal source, alignment. extra: divisions, support side.
        internal void Circularize(StudioModelDocument.Node node,int[] vertices,IEnumerable<(int a,int b)> edges,int[] faces,Vector4 p,Vector3 extra)
        {
            var mesh=node.mesh;
            if(!mesh||!mesh.isReadable||node.skinned||mesh.blendShapeCount>0||mesh.bindposes.Length>0||mesh.boneWeights.Length>0)
                throw new InvalidOperationException("Circularize requires a readable static mesh without skinning or blend shapes.");
            foreach(float v in new[]{p.x,p.y,p.z,p.w,extra.x,extra.y})if(float.IsNaN(v)||float.IsInfinity(v))throw new InvalidOperationException("Circularize values must be finite.");
            int divisions=Mathf.RoundToInt(extra.x),support=Mathf.RoundToInt(extra.y),alignment=Mathf.RoundToInt(p.w);
            if(divisions<0||divisions>32||support<0||support>3||alignment<0||alignment>2||p.z<0||p.z>1)throw new InvalidOperationException("Invalid Circularize options.");
            var representatives=new Dictionary<int,int>();for(int i=0;i<welded.Length;i++)representatives[welded[i]]=i;
            Vector3 Pos(int g)=>Positions[representatives[g]];
            var uses=new Dictionary<(int,int),List<int>>();
            for(int f=0;f<Faces.Count;f++)
            {
                var ring=Faces[f].Vertices;
                for(int e=0;e<ring.Count;e++){var k=Key(ring[e],ring[(e+1)%ring.Count]);if(!uses.TryGetValue(k,out var list))uses[k]=list=new List<int>();list.Add(f);}
            }
            List<List<int>> Loops(IEnumerable<(int,int)> keys)
            {
                var graph=new Dictionary<int,List<int>>();
                foreach(var k in keys.Distinct())foreach(var pair in new[]{(k.Item1,k.Item2),(k.Item2,k.Item1)})
                {if(!graph.TryGetValue(pair.Item1,out var list))graph[pair.Item1]=list=new List<int>();list.Add(pair.Item2);}
                if(graph.Count<3||graph.Values.Any(v=>v.Count!=2))throw new InvalidOperationException("Circularize requires closed, unbranched loops with at least three vertices.");
                var remaining=new HashSet<int>(graph.Keys);var result=new List<List<int>>();
                while(remaining.Count>0)
                {
                    int start=remaining.Min(),at=start,previous=-1;var ring=new List<int>();
                    do{if(!remaining.Remove(at))throw new InvalidOperationException("Circularize loops must not touch.");ring.Add(at);int next=graph[at].First(i=>i!=previous);previous=at;at=next;}while(at!=start);
                    if(ring.Count<3)throw new InvalidOperationException("Circularize needs at least three distinct vertices.");result.Add(ring);
                }
                return result;
            }
            var regions=new List<CircleRegion>();
            if(faces!=null&&faces.Length>0)
            {
                var remaining=new HashSet<int>(faces);
                if(remaining.Any(f=>f<0||f>=Faces.Count))throw new InvalidOperationException("Circularize face selection changed.");
                while(remaining.Count>0)
                {
                    var region=new CircleRegion();var queue=new Queue<int>();int seed=remaining.Min();remaining.Remove(seed);queue.Enqueue(seed);
                    while(queue.Count>0)
                    {
                        int f=queue.Dequeue();region.Faces.Add(f);var ring=Faces[f].Vertices;
                        for(int e=0;e<ring.Count;e++)foreach(int next in uses[Key(ring[e],ring[(e+1)%ring.Count])])if(remaining.Remove(next))queue.Enqueue(next);
                    }
                    var boundaries=uses.Where(k=>k.Value.Count(region.Faces.Contains)==1).Select(k=>k.Key).ToArray();
                    var loops=Loops(boundaries);
                    if(loops.Count!=1)throw new InvalidOperationException("Circularize face regions need one perimeter without holes. Select each border as an edge loop instead.");
                    region.Ring=loops[0];region.Interior.UnionWith(region.Faces.SelectMany(f=>Faces[f].Vertices).Select(v=>welded[v]));region.Interior.ExceptWith(region.Ring);regions.Add(region);
                }
            }
            else if(edges!=null)
            {
                var chosen=edges.Select(e=>{if(e.a<0||e.b<0||e.a>=welded.Length||e.b>=welded.Length)throw new InvalidOperationException("Circularize edge selection changed.");return Key(e.a,e.b);}).Distinct().ToArray();
                if(chosen.Any(k=>!uses.ContainsKey(k)||uses[k].Count>2))throw new InvalidOperationException("Circularize requires authored manifold edges.");
                regions.AddRange(Loops(chosen).Select(r=>new CircleRegion{Ring=r}));
            }
            else
            {
                if(vertices==null||vertices.Any(v=>v<0||v>=welded.Length))throw new InvalidOperationException("Circularize vertex selection changed.");
                var selected=new HashSet<int>(vertices.Select(v=>welded[v]));
                if(selected.Count<3)throw new InvalidOperationException("Circularize needs at least three distinct geometric vertices.");
                var fullFaces=Enumerable.Range(0,Faces.Count).Where(f=>Faces[f].Vertices.All(v=>selected.Contains(welded[v]))).ToArray();
                if(fullFaces.Length>0&&new HashSet<int>(fullFaces.SelectMany(f=>Faces[f].Vertices).Select(v=>welded[v])).SetEquals(selected))
                {Circularize(node,null,null,fullFaces,p,extra);return;}
                // Prefer authored loop ordering, but arbitrary vertex clouds are also valid.
                var induced=uses.Keys.Where(k=>selected.Contains(k.Item1)&&selected.Contains(k.Item2)).ToArray();
                var degrees=selected.ToDictionary(i=>i,i=>induced.Count(k=>k.Item1==i||k.Item2==i));
                if(degrees.Values.All(n=>n==2))regions.AddRange(Loops(induced).Select(r=>new CircleRegion{Ring=r}));
                else
                {
                    Vector3 center=selected.Aggregate(Vector3.zero,(s,i)=>s+Pos(i))/selected.Count;
                    Vector3 axis=(Pos(selected.OrderBy(i=>i).First())-center).normalized,normal=Vector3.zero;
                    foreach(int a in selected){var candidate=Vector3.Cross(axis,Pos(a)-center);if(candidate.sqrMagnitude>normal.sqrMagnitude)normal=candidate;}
                    if(normal.sqrMagnitude<1e-16f)throw new InvalidOperationException("Circularize cannot fit a circle to collinear vertices.");
                    var side=Vector3.Cross(normal.normalized,axis);
                    regions.Add(new CircleRegion{Ring=selected.OrderBy(i=>Mathf.Atan2(Vector3.Dot(Pos(i)-center,side),Vector3.Dot(Pos(i)-center,axis))).ToList()});
                }
            }
            var source=BevelSource(mesh);var points=Positions.ToList();var normals=source.RetainedNormals.ToList();var tangents=source.Tangents.ToList();var colors=source.Colors.ToList();
            var uv=new List<Vector4>[8];for(int c=0;c<8;c++)uv[c]=source.UV[c]?.ToList()??new List<Vector4>();
            var geometry=welded.ToList();var normalGroups=node.normalGroups?.ToList();
            var moved=new Dictionary<int,Vector3>();var splitPositions=new Dictionary<(int,int),Vector3[]>();var insideFaces=new HashSet<int>();
            var sourceTriangles=mesh.triangles;
            Vector3 ProjectSurface(Vector3 target,Vector3 direction)
            {
                float best=float.PositiveInfinity,fallbackScore=float.PositiveInfinity;Vector3 hit=target,fallback=target;
                for(int i=0;i<sourceTriangles.Length;i+=3)
                {
                    var a=Positions[sourceTriangles[i]];var b=Positions[sourceTriangles[i+1]];var c=Positions[sourceTriangles[i+2]];
                    var n=Vector3.Cross(b-a,c-a);float den=Vector3.Dot(n,direction);if(Mathf.Abs(den)<1e-12f)continue;
                    float t=Vector3.Dot(n,a-target)/den;var q=target+direction*t;
                    float score=(q-(a+b+c)/3).sqrMagnitude+t*t;
                    if(score<fallbackScore){fallbackScore=score;fallback=q;}
                    if(Mathf.Abs(t)>=best)continue;
                    float tolerance=-n.sqrMagnitude*1e-5f;
                    if(Vector3.Dot(Vector3.Cross(b-a,q-a),n)<tolerance||Vector3.Dot(Vector3.Cross(c-b,q-b),n)<tolerance||Vector3.Dot(Vector3.Cross(a-c,q-c),n)<tolerance)continue;
                    hit=q;best=Mathf.Abs(t);
                }
                if(float.IsPositiveInfinity(best))
                {if(float.IsPositiveInfinity(fallbackScore))throw new InvalidOperationException("Circularize cannot project along this normal. Use Automatic alignment.");return fallback;}
                return hit;
            }
            foreach(var region in regions)
            {
                var ring=region.Ring;int count=ring.Count;Vector3 center=ring.Aggregate(Vector3.zero,(s,i)=>s+Pos(i))/count;
                Vector3 loopNormal=Vector3.zero;
                for(int i=0;i<count;i++)loopNormal+=Vector3.Cross(Pos(ring[i])-center,Pos(ring[(i+1)%count])-center);
                Vector3 normal=Vector3.zero;
                var relevant=region.Faces.Count>0?region.Faces: new HashSet<int>(uses.Where(k=>ring.Contains(k.Key.Item1)&&ring.Contains(k.Key.Item2)).SelectMany(k=>k.Value));
                foreach(int f in relevant){var vs=Faces[f].Vertices;for(int i=0;i<vs.Count;i++)normal+=Vector3.Cross(Positions[vs[i]]-center,Positions[vs[(i+1)%vs.Count]]-center);}
                if(p.z>=.5f||normal.sqrMagnitude<1e-16f)normal=loopNormal;
                if(normal.sqrMagnitude<1e-16f)throw new InvalidOperationException("Circularize cannot determine a plane for this selection.");normal.Normalize();
                if(Vector3.Dot(loopNormal,normal)<0)ring.Reverse();
                var x=Vector3.ProjectOnPlane(Pos(ring[0])-center,normal).normalized;var y=Vector3.Cross(normal,x);
                if(x.sqrMagnitude<.5f)throw new InvalidOperationException("Circularize perimeter contains its center.");
                float radius=ring.Average(i=>Vector3.ProjectOnPlane(Pos(i)-center,normal).magnitude),outputRadius=radius+p.x;
                if(radius<=1e-8f||outputRadius<=1e-8f)throw new InvalidOperationException("Circularize radius must remain positive.");
                // Least-squares phase avoids anchoring the fit to an arbitrary first corner.
                float sin=0,cos=0;
                for(int i=0;i<count;i++){var d=Pos(ring[i])-center;float phase=Mathf.Atan2(Vector3.Dot(d,y),Vector3.Dot(d,x))-2*Mathf.PI*i/count;sin+=Mathf.Sin(phase);cos+=Mathf.Cos(phase);}
                float angle=Mathf.Atan2(sin,cos)+p.y*Mathf.Deg2Rad;
                Vector3 Destination(float at,Vector3 sourceNormal)
                {
                    float a=angle+2*Mathf.PI*at/count;var target=center+outputRadius*(x*Mathf.Cos(a)+y*Mathf.Sin(a));
                    if(alignment>0){var direction=alignment==2&&sourceNormal.sqrMagnitude>.5f?sourceNormal:normal;target=ProjectSurface(target,direction);}return target;
                }
                for(int i=0;i<count;i++)
                {
                    int g=ring[i],raw=representatives[g];var n=normals.Count>raw?normals[raw]:normal;
                    if(moved.ContainsKey(g))throw new InvalidOperationException("Circularize regions must not share vertices.");moved[g]=Destination(i,n);
                    int next=ring[(i+1)%count];var key=FlowKey(g,next);
                    if(divisions>0)
                    {
                        if(!uses.ContainsKey(key))throw new InvalidOperationException("Add Divisions requires an authored edge around the circle. Select a closed edge loop or face region.");
                        var samples=new Vector3[divisions];for(int j=0;j<divisions;j++)samples[j]=Destination(i+(j+1f)/(divisions+1),n);
                        if(g>next)Array.Reverse(samples);splitPositions.Add(key,samples);
                    }
                }
                foreach(int g in region.Interior)
                {
                    var d=Pos(g)-center;var planar=Vector3.ProjectOnPlane(d,normal);var rotated=Quaternion.AngleAxis(p.y,normal)*planar*(outputRadius/radius);
                    moved[g]=center+rotated+(alignment==0?Vector3.zero:normal*Vector3.Dot(d,normal));
                }
                insideFaces.UnionWith(region.Faces);
                if(region.Faces.Count==0)
                {
                    // Classify the existing faces by centroid in the original projected perimeter.
                    foreach(int f in relevant)
                    {
                        var centroid=Faces[f].Vertices.Aggregate(Vector3.zero,(s,v)=>s+Positions[v])/Faces[f].Vertices.Count-center;
                        float qx=Vector3.Dot(centroid,x),qy=Vector3.Dot(centroid,y);bool inside=false;
                        for(int a=0,b=count-1;a<count;b=a++)
                        {var va=Pos(ring[a])-center;var vb=Pos(ring[b])-center;float ax=Vector3.Dot(va,x),ay=Vector3.Dot(va,y),bx=Vector3.Dot(vb,x),by=Vector3.Dot(vb,y);if((ay>qy)!=(by>qy)&&qx<(bx-ax)*(qy-ay)/(by-ay)+ax)inside=!inside;}
                        if(inside)insideFaces.Add(f);
                    }
                }
            }
            for(int i=0;i<points.Count;i++)if(moved.TryGetValue(welded[i],out var destination))points[i]=destination;
            int nextGeo=sourceGeometry.Length;var splitIds=new Dictionary<(int,int),int[]>();
            foreach(var pair in splitPositions){splitIds[pair.Key]=Enumerable.Range(nextGeo,pair.Value.Length).ToArray();nextGeo+=pair.Value.Length;}
            var created=new Dictionary<(int,int,int),int>();
            int Insert(int a,int b,int step)
            {
                var key=Key(a,b);int canonicalStep=welded[a]<welded[b]?step:divisions-1-step;
                var renderKey=a<b?(a,b,step):(b,a,divisions-1-step);if(created.TryGetValue(renderKey,out int id))return id;
                float t=(step+1f)/(divisions+1);id=points.Count;created[renderKey]=id;points.Add(splitPositions[key][canonicalStep]);geometry.Add(splitIds[key][canonicalStep]);
                if(normals.Count>0)normals.Add(Vector3.Lerp(normals[a],normals[b],t).normalized);
                if(tangents.Count>0)tangents.Add(Vector4.Lerp(tangents[a],tangents[b],t));if(colors.Count>0)colors.Add(Color.Lerp(colors[a],colors[b],t));
                for(int c=0;c<8;c++)if(uv[c].Count>0)uv[c].Add(Vector4.Lerp(uv[c][a],uv[c][b],t));
                if(normalGroups!=null)normalGroups.Add(normalGroups[a]==normalGroups[b]?normalGroups[a]:normalGroups.Max()+1);return id;
            }
            var output=new List<Face>();var indices=Enumerable.Range(0,mesh.subMeshCount).Select(_=>new List<int>()).ToArray();var captured=BevelSourceTriangles(mesh);
            for(int f=0;f<Faces.Count;f++)
            {
                var face=Faces[f];var expanded=new List<int>();bool split=false;
                for(int i=0;i<face.Vertices.Count;i++)
                {
                    int a=face.Vertices[i],b=face.Vertices[(i+1)%face.Vertices.Count];expanded.Add(a);
                    if(splitPositions.ContainsKey(Key(a,b))){split=true;for(int j=0;j<divisions;j++)expanded.Add(Insert(a,b,j));}
                }
                if(!split){output.Add(face);indices[face.Submesh].AddRange(captured[f]);continue;}
                var triangles=new List<int>();Triangulate(expanded,points,triangles);indices[face.Submesh].AddRange(triangles);
                bool supporting=insideFaces.Contains(f)?(support==1||support==3):(support==2||support==3);
                if(supporting)for(int i=0;i<triangles.Count;i+=3)output.Add(new Face{Submesh=face.Submesh,Vertices=triangles.Skip(i).Take(3).ToList()});
                else output.Add(new Face{Submesh=face.Submesh,Vertices=expanded});
            }
            foreach(var point in points)if(!BevelFinite(point))throw new InvalidOperationException("Circularize produced non-finite geometry.");
            // Build everything before mutating the captured working mesh. Publication is owned by StudioMeshToolPreview.
            mesh.Clear();mesh.indexFormat=points.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;mesh.SetVertices(points);
            if(normals.Count>0)mesh.SetNormals(normals);if(tangents.Count>0)mesh.SetTangents(tangents);if(colors.Count>0)mesh.SetColors(colors);
            for(int c=0;c<8;c++)if(uv[c].Count>0)
            {int dim=source.Dimensions[c];if(dim==1)StudioSubdivisionData.SetUV1(mesh,c,uv[c]);else if(dim==2)mesh.SetUVs(c,uv[c].Select(v=>new Vector2(v.x,v.y)).ToList());else if(dim==3)mesh.SetUVs(c,uv[c].Select(v=>new Vector3(v.x,v.y,v.z)).ToList());else mesh.SetUVs(c,uv[c]);}
            mesh.subMeshCount=indices.Length;for(int sub=0;sub<indices.Length;sub++)mesh.SetTriangles(indices[sub],sub,false);
            node.cutPolygons=output.SelectMany(f=>new[]{f.Submesh,f.Vertices.Count}.Concat(f.Vertices)).ToArray();node.cutTriangles=mesh.triangles;node.geometryVertices=GeometricIds(geometry);node.normalGroups=normalGroups?.ToArray();
            StudioModifierEvaluator.UpdateSurfacePositions(node,points.ToArray());
        }
    }
}
