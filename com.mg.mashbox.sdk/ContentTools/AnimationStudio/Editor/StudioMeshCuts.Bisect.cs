using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        // Called only on an owned transaction mesh. Both parallel sections are cut
        // before the middle band is removed; outside geometry is never translated.
        internal static HashSet<int> Bisect(StudioModelDocument.Node node, Plane worldPlane, Matrix4x4 world, float distance, bool fill, Action<Vector3,Vector3> cutCenters = null)
        {
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0)
                throw new InvalidOperationException("Bisect Distance must be a finite nonnegative distance.");
            if (node.skinned || node.blendShape != null || node.mesh.blendShapeCount > 0 || node.mesh.boneWeights.Length > 0)
                throw new InvalidOperationException("Bisect requires an editable static mesh.");
            var normal = world.transpose.MultiplyVector(worldPlane.normal);
            float length = normal.magnitude;
            if (length < 1e-12f || Mathf.Abs(world.determinant) < 1e-12f)
                throw new InvalidOperationException("Bisect requires a nonsingular object transform.");
            var center = new Plane(normal / length, (Vector3.Dot(worldPlane.normal, world.MultiplyPoint3x4(Vector3.zero)) + worldPlane.distance) / length);
            float half = distance * .5f / length;
            var low = new Plane(center.normal, center.distance + half);
            var high = new Plane(center.normal, center.distance - half);
            var topology = new StudioMeshCuts(node);
            float epsilon = topology.CutTolerance;
            if (distance > 0 && half < epsilon * 2)
                throw new InvalidOperationException("Bisect Distance is too small for this mesh's precision. Use zero or a larger gap.");
            bool cut = false;
            foreach (var plane in distance == 0 ? new[] { center } : new[] { low, high })
            {
                if (topology.ApplyPlaneSlice(node,plane)) { topology = new StudioMeshCuts(node); cut = true; }
            }
            if (!cut) throw new InvalidOperationException("Draw a cut through the mesh, away from tangent or coplanar surfaces.");
            var buffer = new BisectBuffer(node);
            var sourceFaces = topology.Faces;
            var triangles = topology.BevelSourceTriangles(node.mesh);
            var faces = new List<Face>(); var render = new List<List<int>>(); var positiveFaces = new HashSet<int>();
            var sourceGeometry = StudioSubdivisionData.Geometry(node);
            var sideGeometry = new Dictionary<(int, int), int>();
            var rawSides = new Dictionary<(int, int), int>();
            var sideCounts = new int[2];
            int SideVertex(int raw, int side)
            {
                if (rawSides.TryGetValue((raw, side), out int index)) return index;
                if (!sideGeometry.TryGetValue((sourceGeometry[raw], side), out int geo))
                    sideGeometry[(sourceGeometry[raw], side)] = geo = buffer.NextGeometry++;
                index = buffer.Copy(raw, geo); rawSides.Add((raw, side), index); return index;
            }
            var capEdges = new List<(int a, int b, int sub)>[2] { new List<(int,int,int)>(), new List<(int,int,int)>() };
            for (int f = 0; f < sourceFaces.Count; f++)
            {
                var source = sourceFaces[f];
                float min = source.Vertices.Min(v => center.GetDistanceToPoint(topology.Positions[v]));
                float max = source.Vertices.Max(v => center.GetDistanceToPoint(topology.Positions[v]));
                int side = max <= -half + epsilon && min < -half - epsilon ? 0 : min >= half - epsilon && max > half + epsilon ? 1 : -1;
                if (side < 0) continue;
                sideCounts[side]++;
                var face = new Face { Submesh = source.Submesh, Vertices = source.Vertices.Select(v => SideVertex(v, side)).ToList() };
                if (side == 1) positiveFaces.Add(faces.Count);
                faces.Add(face); render.Add(triangles[f].Select(v => SideVertex(v, side)).ToList());
                var plane = side == 0 ? low : high;
                for (int i = 0; i < face.Vertices.Count; i++)
                {
                    int a = face.Vertices[i], b = face.Vertices[(i + 1) % face.Vertices.Count];
                    if (Mathf.Abs(plane.GetDistanceToPoint(buffer.Positions[a])) <= epsilon * 2 && Mathf.Abs(plane.GetDistanceToPoint(buffer.Positions[b])) <= epsilon * 2)
                        capEdges[side].Add((b, a, face.Submesh)); // opposite the retained surface
                }
            }
            if (sideCounts.Any(n => n == 0)) throw new InvalidOperationException("The cut must leave faces on both sides of the gap.");
            if (fill)
                for (int side = 0; side < 2; side++)
                {
                    int first = faces.Count;
                    BuildBisectCaps(buffer, capEdges[side], side == 0 ? center.normal : -center.normal, faces, render, epsilon);
                    if (side == 1) for (int f = first; f < faces.Count; f++) positiveFaces.Add(f);
                }
            buffer.Publish(node, faces, render);
            if(cutCenters!=null)
            {
                Vector3 Center(int side)
                {
                    var rim=capEdges[side].SelectMany(e=>new[]{e.a,e.b}).Distinct().ToArray();
                    var chart=new CutChart(buffer.Positions[rim[0]],center.normal);
                    var min=new Vector2(float.PositiveInfinity,float.PositiveInfinity);var max=-min;
                    foreach(int id in rim){var p=chart.Project(buffer.Positions[id]);min=Vector2.Min(min,p);max=Vector2.Max(max,p);}
                    var middle=(min+max)*.5f;return chart.Origin+chart.X*middle.x+chart.Y*middle.y;
                }
                cutCenters(Center(0),Center(1));
            }
            return positiveFaces;
        }

        private static void BuildBisectCaps(BisectBuffer buffer, List<(int a, int b, int sub)> edges, Vector3 normal,
            List<Face> faces, List<List<int>> render, float epsilon)
        {
            var byEdge = edges.GroupBy(e => (Math.Min(buffer.Geometry[e.a], buffer.Geometry[e.b]), Math.Max(buffer.Geometry[e.a], buffer.Geometry[e.b]))).ToArray();
            if (byEdge.Any(g => g.Count() > 2)) throw new InvalidOperationException("Fill requires manifold section boundaries.");
            var boundary = byEdge.Where(g => g.Count() == 1).Select(g => g.First()).ToList();
            if (boundary.Count == 0) throw new InvalidOperationException("No closed section boundary to fill.");
            var outgoing = boundary.GroupBy(e => buffer.Geometry[e.a]).ToDictionary(g => g.Key, g => g.ToArray());
            if (outgoing.Any(p => p.Value.Length != 1) || boundary.GroupBy(e => buffer.Geometry[e.b]).Any(g => g.Count() != 1))
                throw new InvalidOperationException("Fill requires closed, non-branching section loops.");
            var loops = new List<(List<int> vertices, int sub)>(); var visited = new HashSet<int>();
            foreach (var edge in boundary)
            {
                int start = buffer.Geometry[edge.a], current = start;
                if (visited.Contains(start)) continue;
                var loop = new List<int>();
                do
                {
                    if (!visited.Add(current) || !outgoing.TryGetValue(current, out var next))
                        throw new InvalidOperationException("Fill cannot cap an open or touching section.");
                    loop.Add(next[0].a); current = buffer.Geometry[next[0].b];
                } while (current != start);
                if (loop.Count < 3) throw new InvalidOperationException("The section is degenerate.");
                loops.Add((loop, edge.sub));
            }
            var chart = new CutChart(buffer.Positions[loops[0].vertices[0]], normal);
            foreach (var loop in loops)
                foreach (var other in loops)
                    if (!ReferenceEquals(loop.vertices, other.vertices) && CutInside(chart.Project(buffer.Positions[loop.vertices[0]]), other.vertices.Select(v => chart.Project(buffer.Positions[v])).ToArray()))
                        throw new InvalidOperationException("Fill does not yet support nested section holes. Turn Fill off for this cut.");
            foreach (var loop in loops)
            {
                var indices = new List<int>(); TriangulateSliceCap(loop.vertices, buffer.Positions, normal, indices);
                double area = 0, triangleArea = 0;
                for (int i = 0; i < loop.vertices.Count; i++) area += CutCross(chart.Project(buffer.Positions[loop.vertices[i]]), chart.Project(buffer.Positions[loop.vertices[(i + 1) % loop.vertices.Count]]));
                for (int i = 0; i < indices.Count; i += 3)
                { var a = chart.Project(buffer.Positions[indices[i]]); triangleArea += CutCross(chart.Project(buffer.Positions[indices[i + 1]]) - a, chart.Project(buffer.Positions[indices[i + 2]]) - a); }
                if (area <= epsilon * epsilon || Math.Abs(area - triangleArea) > Math.Max(epsilon * epsilon * 16, area * 1e-4))
                    throw new InvalidOperationException("The section cannot be filled safely; check source winding and topology.");
                var quads = TryBisectGrid(buffer,loop.vertices,normal,faces,render,epsilon);
                if (quads == null)
                {
                // Concave/irregular loops that cannot form a valid lattice retain
                // conforming all-quad refinement with guarded interior relaxation.
                ImproveSliceTriangulation(indices,buffer.Positions,normal);
                var patches = new List<List<int>>();
                for (int i = 0; i < indices.Count; i += 3) patches.Add(indices.Skip(i).Take(3).ToList());
                // Remove triangulation diagonals in quality order before any necessary
                // all-quad refinement. A four-sided section becomes exactly one quad.
                while (true)
                {
                    var candidates = new List<(float score,int a,int b,List<int> contour)>();
                    for (int a = 0; a < patches.Count; a++) for (int b = a + 1; b < patches.Count; b++)
                    {
                        if (patches[a].Count != 3 || patches[b].Count != 3) continue;
                        var contour = BisectMergeTriangles(patches[a], patches[b]); if (contour == null) continue;
                        float score = 0; bool valid = true;
                        for (int k = 0; k < 4; k++)
                        {
                            var x = buffer.Positions[contour[(k + 1) % 4]] - buffer.Positions[contour[k]];
                            var y = buffer.Positions[contour[(k + 2) % 4]] - buffer.Positions[contour[(k + 1) % 4]];
                            if (Vector3.Dot(Vector3.Cross(x, y), normal) <= epsilon * epsilon) valid = false;
                            score += Mathf.Abs(Vector3.Dot(x.normalized, y.normalized));
                        }
                        if (valid) candidates.Add((score,a,b,contour));
                    }
                    if(candidates.Count==0)break;
                    var degrees=new int[patches.Count];foreach(var candidate in candidates){degrees[candidate.a]++;degrees[candidate.b]++;}
                    var leaves=candidates.Where(c=>degrees[c.a]==1||degrees[c.b]==1).ToList();
                    var best=(leaves.Count>0?leaves:candidates).OrderBy(c=>c.score).ThenBy(c=>c.a).ThenBy(c=>c.b).First();
                    patches[best.a]=best.contour;patches.RemoveAt(best.b);
                }
                bool refine = patches.Any(p => p.Count != 4);
                var mids = new Dictionary<(int, int), int>();
                int Mid(int a, int b)
                {
                    var key = (Math.Min(buffer.Geometry[a], buffer.Geometry[b]), Math.Max(buffer.Geometry[a], buffer.Geometry[b]));
                    if (!mids.TryGetValue(key, out int id)) mids[key] = id = buffer.Interpolate(a, b, buffer.NextGeometry++);
                    return id;
                }
                quads = new List<List<int>>();
                foreach (var patch in patches)
                {
                    if (!refine) { quads.Add(patch); continue; }
                    int center = buffer.Average(patch);
                    for (int i = 0; i < patch.Count; i++)
                        quads.Add(new List<int> { patch[i], Mid(patch[i], patch[(i + 1) % patch.Count]), center, Mid(patch[(i + patch.Count - 1) % patch.Count], patch[i]) });
                }
                if (refine) PropagateBisectMidpoints(buffer,mids,faces,render);
                RelaxBisectPatch(buffer,quads,normal,epsilon);
                }
                var capVertices = new Dictionary<int, int>();
                int Cap(int v)
                {
                    if (capVertices.TryGetValue(v, out int id)) return id;
                    id = buffer.Copy(v, buffer.Geometry[v]); buffer.SetCap(id, normal, chart); capVertices[v] = id; return id;
                }
                foreach (var quad in quads)
                {
                    var vertices = quad.Select(Cap).ToList(); var tris = new List<int>(); TriangulateSliceCap(vertices, buffer.Positions, normal, tris);
                    if (tris.Count != 6) throw new InvalidOperationException("A generated quad is degenerate.");
                    faces.Add(new Face { Submesh = loop.sub, Vertices = vertices }); render.Add(tris);
                }
            }
        }
        private static void ImproveSliceTriangulation(List<int> indices,List<Vector3> positions,Vector3 normal)
        {
            var chart=new CutChart(positions[indices[0]],normal);var points=new Dictionary<int,(double x,double y)>();
            foreach(int v in indices.Distinct()){var p=chart.Project(positions[v]);points[v]=(p.x,p.y);}
            double Cross(int a,int b,int c){var p=points[a];var q=points[b];var r=points[c];return(q.x-p.x)*(r.y-p.y)-(q.y-p.y)*(r.x-p.x);}
            double Edge(int a,int b){var p=points[a];var q=points[b];return(p.x-q.x)*(p.x-q.x)+(p.y-q.y)*(p.y-q.y);}
            double Quality(int a,int b,int c)=>Cross(a,b,c)/(Edge(a,b)+Edge(b,c)+Edge(c,a));
            for(int pass=0;pass<indices.Count*4;pass++)
            {
                var edges=new Dictionary<(int,int),(int triangle,int a,int b,int c)>();
                double best=0;int first=-1,second=-1,aa=0,bb=0,cc=0,dd=0;
                for(int i=0;i<indices.Count;i+=3)for(int k=0;k<3;k++)
                {
                    int a=indices[i+k],b=indices[i+(k+1)%3],d=indices[i+(k+2)%3];var key=(Math.Min(a,b),Math.Max(a,b));
                    if(!edges.TryGetValue(key,out var prior)){edges.Add(key,(i,a,b,d));continue;}
                    if(prior.a!=b||prior.b!=a)continue;
                    a=prior.a;b=prior.b;int c=prior.c;
                    if(Cross(c,a,d)<=0||Cross(a,d,b)<=0||Cross(d,b,c)<=0||Cross(b,c,a)<=0)continue;
                    double old=Math.Min(Quality(a,b,c),Quality(b,a,d));double next=Math.Min(Quality(c,d,b),Quality(d,c,a));
                    double improvement=next-old;if(next<=old*1.00001+1e-15||improvement<=best)continue;
                    best=improvement;first=prior.triangle;second=i;aa=a;bb=b;cc=c;dd=d;
                }
                if(first<0)break;
                indices[first]=cc;indices[first+1]=dd;indices[first+2]=bb;
                indices[second]=dd;indices[second+1]=cc;indices[second+2]=aa;
            }
        }

        private static void TriangulateSliceCap(List<int> polygon,List<Vector3> positions,Vector3 normal,List<int> indices)
        {
            var chart=new CutChart(positions[polygon[0]],normal);
            var points=new Dictionary<int,(double x,double y)>();double scale=0;
            foreach(int id in polygon)
            {
                var v=positions[id];double x=(double)v.x-chart.Origin.x,y=(double)v.y-chart.Origin.y,z=(double)v.z-chart.Origin.z;
                var p=(x*chart.X.x+y*chart.X.y+z*chart.X.z,x*chart.Y.x+y*chart.Y.y+z*chart.Y.z);
                points[id]=p;scale=Math.Max(scale,Math.Max(Math.Abs(p.Item1),Math.Abs(p.Item2)));
            }
            double tolerance=Math.Max(1e-30,scale*scale*1e-15);
            double Cross(int a,int b,int c){var p=points[a];var q=points[b];var r=points[c];return(q.x-p.x)*(r.y-p.y)-(q.y-p.y)*(r.x-p.x);}
            var remaining=polygon.ToList();
            while(remaining.Count>3)
            {
                int best=-1;double bestQuality=-1;
                for(int i=0;i<remaining.Count;i++)
                {
                    int a=remaining[(i+remaining.Count-1)%remaining.Count],b=remaining[i],c=remaining[(i+1)%remaining.Count];
                    if(Cross(a,b,c)<=tolerance)continue;
                    if(remaining.Any(v=>v!=a&&v!=b&&v!=c&&Cross(a,b,v)>=-tolerance&&Cross(b,c,v)>=-tolerance&&Cross(c,a,v)>=-tolerance))continue;
                    var pa=points[a];var pb=points[b];var pc=points[c];double Edge((double x,double y) p,(double x,double y) q)=>Math.Pow(p.x-q.x,2)+Math.Pow(p.y-q.y,2);double quality=Cross(a,b,c)/(Edge(pa,pb)+Edge(pb,pc)+Edge(pc,pa));if(quality>bestQuality){bestQuality=quality;best=i;}
                }
                if(best<0)throw new InvalidOperationException("The section boundary is self-intersecting or degenerate; fill was not applied.");
                indices.AddRange(new[]{remaining[(best+remaining.Count-1)%remaining.Count],remaining[best],remaining[(best+1)%remaining.Count]});remaining.RemoveAt(best);
            }
            if(remaining.Count==3)
            {
                if(Cross(remaining[0],remaining[1],remaining[2])<=tolerance)throw new InvalidOperationException("The section contains a collapsed patch; fill was not applied.");
                indices.AddRange(remaining);
            }
        }

        private static List<int> BisectMergeTriangles(List<int> a, List<int> b)
        {
            if (a.Intersect(b).Count() != 2) return null;
            var edges = new List<(int a,int b)>();
            foreach (var p in new[] { a,b }) for (int i = 0; i < 3; i++)
            { var e = (p[i],p[(i+1)%3]); int reverse = edges.FindIndex(x => x.a == e.Item2 && x.b == e.Item1); if (reverse >= 0) edges.RemoveAt(reverse); else edges.Add(e); }
            if (edges.Count != 4) return null;
            var result = new List<int>(); int current = edges[0].a;
            for (int i = 0; i < 4; i++) { result.Add(current); var next = edges.Where(e => e.a == current).ToArray(); if (next.Length != 1) return null; current = next[0].b; }
            return current == result[0] && result.Distinct().Count() == 4 ? result : null;
        }

        private sealed class BisectBuffer
        {
            internal readonly List<Vector3> Positions, Normals;
            internal readonly List<Vector4> Tangents;
            internal readonly List<Color> Colors;
            internal readonly List<int> Geometry, Groups;
            internal int NextGeometry;
            private readonly List<Vector4>[] uv = new List<Vector4>[8];
            private readonly int[] dimensions = new int[8];
            private readonly int submeshes;
            internal BisectBuffer(StudioModelDocument.Node node, bool capAttributes=true)
            {
                var m = node.mesh; Positions = m.vertices.ToList(); Normals = m.normals.ToList(); Tangents = m.tangents.ToList(); Colors = m.colors.ToList();
                Geometry = StudioSubdivisionData.Geometry(node).ToList(); NextGeometry = Geometry.Max() + 1;
                Groups = node.normalGroups != null && node.normalGroups.Length == Positions.Count ? node.normalGroups.ToList() : Enumerable.Range(0,Positions.Count).ToList();
                var groupIds=new Dictionary<int,int>();
                for(int i=0;i<Groups.Count;i++)
                {int label=Groups[i];if(!groupIds.TryGetValue(label,out int dense))groupIds.Add(label,dense=groupIds.Count);Groups[i]=dense;}
                if (Normals.Count == 0 && capAttributes)
                {var values=new Vector3[Positions.Count];var tris=m.triangles;for(int i=0;i<tris.Length;i+=3){int a=tris[i],b=tris[i+1],c=tris[i+2];var normal=Vector3.Cross(Positions[b]-Positions[a],Positions[c]-Positions[a]);values[a]+=normal;values[b]+=normal;values[c]+=normal;}Normals.AddRange(values.Select(n=>n.normalized));}
                submeshes = m.subMeshCount;
                for (int c = 0; c < 8; c++) { uv[c] = new List<Vector4>(); m.GetUVs(c,uv[c]); dimensions[c] = m.GetVertexAttributeDimension(VertexAttribute.TexCoord0+c); }
                if (uv[0].Count == 0 && capAttributes) { uv[0].AddRange(Enumerable.Repeat(Vector4.zero,Positions.Count)); dimensions[0] = 2; }
            }
            internal int Copy(int a, int geometry)
            {
                int id = Positions.Count; Positions.Add(Positions[a]); if(Normals.Count>0)Normals.Add(Normals[a]); Geometry.Add(geometry); Groups.Add(Groups[a]);
                if (Tangents.Count > 0) Tangents.Add(Tangents[a]); if (Colors.Count > 0) Colors.Add(Colors[a]);
                foreach (var channel in uv) if (channel.Count > 0) channel.Add(channel[a]); return id;
            }
            internal int Interpolate(int a, int b, int geometry, float t=.5f)
            {
                int id = Copy(a,geometry); Positions[id] = Vector3.LerpUnclamped(Positions[a],Positions[b],t); if(Normals.Count>0)Normals[id] = Vector3.LerpUnclamped(Normals[a],Normals[b],t).normalized;
                if (Tangents.Count > 0) Tangents[id] = Vector4.LerpUnclamped(Tangents[a],Tangents[b],t);
                if (Colors.Count > 0) Colors[id] = Color.LerpUnclamped(Colors[a],Colors[b],t);
                foreach (var channel in uv) if (channel.Count > 0) channel[id] = Vector4.LerpUnclamped(channel[a],channel[b],t); return id;
            }
            internal int Average(List<int> vertices)
            {
                int id = Copy(vertices[0],NextGeometry++); Positions[id] = vertices.Aggregate(Vector3.zero,(p,v)=>p+Positions[v])/vertices.Count;
                if (Colors.Count>0) Colors[id]=vertices.Aggregate(new Color(0,0,0,0),(p,v)=>p+Colors[v])/vertices.Count;
                foreach(var channel in uv)if(channel.Count>0)channel[id]=vertices.Aggregate(Vector4.zero,(p,v)=>p+channel[v])/vertices.Count;
                return id;
            }
            internal void SetCap(int id, Vector3 normal, CutChart chart)
            {
                Normals[id]=normal; Groups[id]=id;
                var p=chart.Project(Positions[id]); var old=uv[0][id]; uv[0][id]=new Vector4(p.x,p.y,old.z,old.w); dimensions[0]=Math.Max(2,dimensions[0]);
                if(Tangents.Count>0)Tangents[id]=new Vector4(chart.X.x,chart.X.y,chart.X.z,1);
            }
            internal void Publish(StudioModelDocument.Node node,List<Face> faces,List<List<int>> render)
            {
                var used=faces.SelectMany(f=>f.Vertices).Distinct().OrderBy(v=>v).ToArray();
                var map=used.Select((v,i)=>(v,i)).ToDictionary(p=>p.v,p=>p.i); var m=node.mesh;
                m.Clear();m.indexFormat=used.Length>65535?IndexFormat.UInt32:IndexFormat.UInt16;
                m.SetVertices(used.Select(v=>Positions[v]).ToList());if(Normals.Count>0)m.SetNormals(used.Select(v=>Normals[v]).ToList());
                if(Tangents.Count>0)m.SetTangents(used.Select(v=>Tangents[v]).ToList());if(Colors.Count>0)m.SetColors(used.Select(v=>Colors[v]).ToList());
                for(int c=0;c<8;c++)if(uv[c].Count>0)
                {var values=used.Select(v=>uv[c][v]).ToList();if(dimensions[c]==1)StudioSubdivisionData.SetUV1(m,c,values);else if(dimensions[c]==2)m.SetUVs(c,values.Select(v=>new Vector2(v.x,v.y)).ToList());else if(dimensions[c]==3)m.SetUVs(c,values.Select(v=>new Vector3(v.x,v.y,v.z)).ToList());else m.SetUVs(c,values);}
                m.subMeshCount=submeshes;for(int s=0;s<submeshes;s++)m.SetTriangles(faces.Select((f,i)=>(f,i)).Where(p=>p.f.Submesh==s).SelectMany(p=>render[p.i]).Select(v=>map[v]).ToArray(),s);
                m.RecalculateBounds();m.UploadMeshData(false);node.cutTriangles=m.triangles;
                node.cutPolygons=faces.SelectMany(f=>new[]{f.Submesh,f.Vertices.Count}.Concat(f.Vertices.Select(v=>map[v]))).ToArray();
                node.geometryVertices=used.Select(v=>Geometry[v]).ToArray();node.normalGroups=used.Select(v=>Groups[v]).ToArray();
                var normalIds=new Dictionary<int,int>();
                for(int i=0;i<node.normalGroups.Length;i++)
                {int label=node.normalGroups[i];if(!normalIds.TryGetValue(label,out int dense))normalIds.Add(label,dense=normalIds.Count);node.normalGroups[i]=dense;}
            }
        }
    }
}
