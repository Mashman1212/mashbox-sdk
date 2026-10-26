using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        // Local supporting-halfspace clipping (Sutherland-Hodgman, 1974). Shared edge cuts
        // and source-face provenance preserve editable boundaries. Cached cut steps are
        // internal preview data, not a product node graph. See BEVEL_ENGINE.md for scope.
        private struct ClipVector
        {
            internal double X,Y,Z;
            internal ClipVector(double x,double y,double z){X=x;Y=y;Z=z;}
            internal ClipVector(Vector3 v){X=v.x;Y=v.y;Z=v.z;}
            internal Vector3 Float=>new Vector3((float)X,(float)Y,(float)Z);
            internal double Squared=>X*X+Y*Y+Z*Z;
            internal double Dot(ClipVector b)=>X*b.X+Y*b.Y+Z*b.Z;
            internal ClipVector Cross(ClipVector b)=>new ClipVector(Y*b.Z-Z*b.Y,Z*b.X-X*b.Z,X*b.Y-Y*b.X);
            public static ClipVector operator +(ClipVector a,ClipVector b)=>new ClipVector(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
            public static ClipVector operator -(ClipVector a,ClipVector b)=>new ClipVector(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
            public static ClipVector operator *(ClipVector a,double b)=>new ClipVector(a.X*b,a.Y*b,a.Z*b);
        }
        private sealed class FlatClipPlane
        {
            internal ClipVector Normal;
            internal Vector3 CapNormal;
            internal double Origin,Speed;
            internal int Source,Material;
            internal (int,int) Edge;
            internal double Distance(ClipVector p,double width)=>Normal.Dot(p)-Origin-Speed*width;
        }
        private sealed class FlatClipPolygon
        {
            internal List<int> Points;
            internal int Source,Plane=-1,Material;
            internal FlatClipPolygon With(List<int> points)=>new FlatClipPolygon{Points=points,Source=Source,Plane=Plane,Material=Material};
        }
        private sealed class FlatClipPointInfo
        {
            internal readonly HashSet<int> Surfaces=new HashSet<int>();
            internal readonly HashSet<(int,int)> OriginalEdges=new HashSet<(int,int)>();
            internal bool Original;
            internal FlatClipPointInfo Copy(){var copy=new FlatClipPointInfo{Original=Original};copy.Surfaces.UnionWith(Surfaces);copy.OriginalEdges.UnionWith(OriginalEdges);return copy;}
        }
        private struct FlatClipStep {internal int A,B,Output,Plane;internal bool Alias;}
        private struct FlatClipCheck {internal int Point,Plane,Side;}
        private sealed class FlatClipGroup
        {
            internal readonly List<int> Faces=new List<int>(),Planes=new List<int>();
            internal readonly HashSet<int> FaceSet=new HashSet<int>();
            internal readonly List<FlatClipPolygon> Input=new List<FlatClipPolygon>();
        }
        private sealed class FlatClipPlan
        {
            internal BevelWavefrontPlan Wave;
            internal readonly List<FlatClipPlane> Planes=new List<FlatClipPlane>();
            internal readonly List<FlatClipGroup> Groups=new List<FlatClipGroup>();
            internal readonly List<int> OriginalIds=new List<int>();
            internal readonly Dictionary<int,int> LocalIds=new Dictionary<int,int>();
            internal readonly List<ClipVector> OriginalPoints=new List<ClipVector>(),Points=new List<ClipVector>();
            internal readonly List<Vector3> FloatPoints=new List<Vector3>();
            internal readonly List<FlatClipPointInfo> OriginalInfo=new List<FlatClipPointInfo>(),Info=new List<FlatClipPointInfo>();
            internal readonly Dictionary<Vector3,int> Identities=new Dictionary<Vector3,int>();
            internal readonly List<FlatClipStep> Steps=new List<FlatClipStep>();
            internal readonly List<FlatClipCheck> Checks=new List<FlatClipCheck>();
            internal readonly Dictionary<(int,int),(int count,int direction)> Interface=new Dictionary<(int,int),(int,int)>();
            internal List<FlatClipPolygon> Output;
            internal float Limit=float.PositiveInfinity;
            internal float LastWidth=float.NaN;
        }
        private FlatClipPlan bevelFlatClip;
        private BevelWavefrontPlan bevelFlatClipAttempt;
        private static double ClipDot(Vector3 a,Vector3 b)=>(double)a.x*b.x+(double)a.y*b.y+(double)a.z*b.z;

        // Plane gradients encode displacement per unit width. At a two-edge miter,
        // both strips must reach every intervening stationary edge at the same time:
        // (gradientA-gradientB).stationaryRay == 0. Solve these shared constraints
        // before clipping; independently chosen edge-bisector planes cannot do this
        // at a higher-valence fan and create extra little edges instead.
        private void BalanceFlatClipPlanes(FlatClipPlan plan)
        {
            var indices=new Dictionary<(int,int),int>();
            var gradients=new ClipVector[plan.Planes.Count];double magnitude=0;
            var parents=new int[plan.Planes.Count];for(int i=0;i<parents.Length;i++)parents[i]=i;
            int Root(int i){while(parents[i]!=i){parents[i]=parents[parents[i]];i=parents[i];}return i;}
            var constraints=new List<(int a,int b,ClipVector direction)>();
            for(int i=0;i<plan.Planes.Count;i++)
            {
                var plane=plan.Planes[i];indices.Add(plane.Edge,i);
                gradients[i]=plane.Normal*(-1/plane.Speed);magnitude=Math.Max(magnitude,Math.Sqrt(gradients[i].Squared));
                var tangent=new ClipVector(plan.Wave.Originals[plane.Edge.Item2])-new ClipVector(plan.Wave.Originals[plane.Edge.Item1]);
                constraints.Add((i,-1,tangent*(1/Math.Sqrt(tangent.Squared))));
            }
            int junctions=0;
            var owners=new List<int>(plan.Wave.Distances.Keys);owners.Sort();
            foreach(int owner in owners)
            {
                var selected=new List<int>();var stationary=new List<BevelEdge>();
                foreach(var edge in bevelIncident[owner])
                    if(indices.TryGetValue(BevelKey(edge.A,edge.B),out int id))selected.Add(id);else stationary.Add(edge);
                if(selected.Count!=2)continue;
                selected.Sort();stationary.Sort((a,b)=>BevelKey(a.A,a.B).CompareTo(BevelKey(b.A,b.B)));
                if(stationary.Count>0)parents[Root(selected[1])]=Root(selected[0]);
                foreach(var edge in stationary)
                {
                    var ray=new ClipVector(plan.Wave.Originals[edge.A==owner?edge.B:edge.A])-new ClipVector(plan.Wave.Originals[owner]);
                    constraints.Add((selected[0],selected[1],ray*(1/Math.Sqrt(ray.Squared))));junctions++;
                }
            }
            if(junctions==0)return;
            // Project onto the shared miter constraint nullspace with a matrix-free
            // conjugate-gradient solve. Local row sweeps converge too slowly on long,
            // finely subdivided chains and must not silently leave unmatched miters.
            ClipVector[] Apply(ClipVector[] input)
            {
                var output=new ClipVector[input.Length];
                foreach(var row in constraints)
                {
                    double value=(input[row.a]-(row.b<0?new ClipVector(0,0,0):input[row.b])).Dot(row.direction)/(row.b<0?1:2);
                    output[row.a]=output[row.a]+row.direction*value;
                    if(row.b>=0)output[row.b]=output[row.b]-row.direction*value;
                }
                return output;
            }
            double Inner(ClipVector[] a,ClipVector[] b){double result=0;for(int i=0;i<a.Length;i++)result+=a[i].Dot(b[i]);return result;}
            var correction=new ClipVector[gradients.Length];var residual=Apply(gradients);var direction=(ClipVector[])residual.Clone();
            double squared=Inner(residual,residual);bool converged=squared==0;
            int maximum=Math.Max(32,gradients.Length*12);
            for(int iteration=0;!converged&&iteration<maximum;iteration++)
            {
                var product=Apply(direction);double denominator=Inner(direction,product);if(denominator<=0)break;
                double alpha=squared/denominator;
                for(int i=0;i<gradients.Length;i++){correction[i]=correction[i]+direction[i]*alpha;residual[i]=residual[i]-product[i]*alpha;}
                double error=0;
                foreach(var row in constraints)
                {
                    var a=gradients[row.a]-correction[row.a];var b=row.b<0?new ClipVector(0,0,0):gradients[row.b]-correction[row.b];
                    error=Math.Max(error,Math.Abs((a-b).Dot(row.direction)));
                }
                if(error<=magnitude*1e-13){converged=true;break;}
                double next=Inner(residual,residual);if(next==0)break;double beta=next/squared;squared=next;
                for(int i=0;i<gradients.Length;i++)direction[i]=residual[i]+direction[i]*beta;
            }
            if(converged)for(int i=0;i<gradients.Length;i++)gradients[i]=gradients[i]-correction[i];
            if(!converged)return;
            var scales=new double[gradients.Length];var supported=new bool[gradients.Length];for(int i=0;i<scales.Length;i++){scales[i]=1;supported[i]=true;}
            for(int i=0;i<gradients.Length;i++)
            {
                if(gradients[i].Squared<magnitude*magnitude*1e-20)return;
                scales[Root(i)]=Math.Max(scales[Root(i)],1/(-plan.Planes[i].Speed*Math.Sqrt(gradients[i].Squared)));
                if(gradients[i].Dot(plan.Planes[i].Normal)<=0)return;
            }
            // Miter constraints are only admissible while these remain supporting
            // cuts of the captured patch. Reject a whole coupled component if its
            // zero-width plane cuts existing geometry or touches an unrelated edge.
            foreach(var group in plan.Groups)foreach(int i in group.Planes)
            {
                var plane=plan.Planes[i];var n=gradients[i]*(1/Math.Sqrt(gradients[i].Squared));
                var origin=new ClipVector(plan.Wave.Originals[plane.Edge.Item1]);
                var allowed=new HashSet<int>{plane.Edge.Item1,plane.Edge.Item2};
                foreach(int j in group.Planes)if((gradients[i]-gradients[j]).Squared<gradients[i].Squared*1e-22&&Math.Abs(n.Dot(new ClipVector(plan.Wave.Originals[plan.Planes[j].Edge.Item1])-origin))<1e-11){allowed.Add(plan.Planes[j].Edge.Item1);allowed.Add(plan.Planes[j].Edge.Item2);}
                foreach(int face in group.Faces)foreach(int render in Faces[face].Vertices)
                {
                    int vertex=welded[render];var point=new ClipVector(plan.Wave.Originals[vertex]);double distance=n.Dot(point-origin),error=(1+Math.Abs(point.X)+Math.Abs(point.Y)+Math.Abs(point.Z))*1e-11;
                    if(distance>error||Math.Abs(distance)<=error&&!allowed.Contains(vertex))supported[Root(i)]=false;
                }
            }
            for(int i=0;i<gradients.Length;i++)
            {
                if(!supported[Root(i)])continue;
                var plane=plan.Planes[i];double length=Math.Sqrt(gradients[i].Squared);
                double orientation=new ClipVector(plane.CapNormal).Dot(plane.Normal)>=0?1:-1;
                plane.Normal=gradients[i]*(1/length);plane.CapNormal=(plane.Normal*orientation).Float;
                plane.Origin=plane.Normal.Dot(new ClipVector(plan.Wave.Originals[plane.Edge.Item1]));
                plane.Speed=-1/(length*scales[Root(i)]);
            }
        }
        private FlatClipPlan PrepareFlatClip(BevelWavefrontPlan wave,Mesh mesh)
        {
            if(ReferenceEquals(bevelFlatClipAttempt,wave))return bevelFlatClip;
            bevelFlatClipAttempt=wave;bevelFlatClip=null;
            var plan=new FlatClipPlan{Wave=wave};var targets=new List<(int,int)>(wave.Targets);targets.Sort();
            var facePlane=new Dictionary<int,int>();var parent=new int[targets.Count];for(int i=0;i<parent.Length;i++)parent[i]=i;
            int Find(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
            void Join(int a,int b){a=Find(a);b=Find(b);if(a!=b)parent[b]=a;}
            var planeFaces=new List<HashSet<int>>();
            for(int i=0;i<targets.Count;i++)
            {
                var edge=bevelEdges[targets[i]];var faces=new HashSet<int>();
                foreach(int owner in new[]{edge.A,edge.B})foreach(var incident in bevelIncident[owner])
                {
                    if(incident.Uses.Count!=2)return null;
                    foreach(var use in incident.Uses)faces.Add(use.face);
                }
                planeFaces.Add(faces);
                foreach(int face in faces){if(facePlane.TryGetValue(face,out int previous))Join(i,previous);else facePlane.Add(face,i);}
                var first=edge.Uses[0];var second=edge.Uses[1];
                var direction=new ClipVector(wave.Originals[edge.B])-new ClipVector(wave.Originals[edge.A]);direction=direction*(1/Math.Sqrt(direction.Squared));
                var normal=new ClipVector(bevelNormals[first.face])+new ClipVector(bevelNormals[second.face]);normal=normal-direction*normal.Dot(direction);
                if(normal.Squared<1e-20)return null;normal=normal*(1/Math.Sqrt(normal.Squared));
                int corner=first.corner;var velocity=new ClipVector(wave.Velocities[first.face][corner]);
                double speed=normal.Dot(velocity);if(Math.Abs(speed)<1e-12)return null;
                double orientation=speed<0?1:-1;
                plan.Planes.Add(new FlatClipPlane{Normal=normal*orientation,CapNormal=normal.Float,Origin=normal.Dot(new ClipVector(wave.Originals[edge.A]))*orientation,Speed=speed*orientation,Source=first.face,Material=Faces[first.face].Submesh,Edge=targets[i]});
            }

            var groups=new Dictionary<int,FlatClipGroup>();
            for(int i=0;i<targets.Count;i++)
            {
                int root=Find(i);if(!groups.TryGetValue(root,out var group)){group=new FlatClipGroup();groups.Add(root,group);plan.Groups.Add(group);}
                group.Planes.Add(i);foreach(int f in planeFaces[i])if(group.FaceSet.Add(f))group.Faces.Add(f);
            }
            foreach(var group in plan.Groups)if((long)group.Planes.Count*(group.Faces.Count+group.Planes.Count)>8000000)return null;
            BalanceFlatClipPlanes(plan);
            List<int>[] triangles=null;
            int Point(int original)
            {
                if(plan.LocalIds.TryGetValue(original,out int id))return id;
                id=plan.OriginalIds.Count;plan.LocalIds.Add(original,id);plan.OriginalIds.Add(original);plan.OriginalPoints.Add(new ClipVector(wave.Originals[original]));plan.OriginalInfo.Add(new FlatClipPointInfo{Original=true});return id;
            }
            foreach(var group in plan.Groups)
            {
                group.Faces.Sort();var vertices=new HashSet<int>();var faceSet=new HashSet<int>(group.Faces);var boundary=new HashSet<int>();
                foreach(int f in group.Faces)
                {
                    foreach(int render in Faces[f].Vertices)vertices.Add(welded[render]);
                    var ring=new List<int>();foreach(int render in Faces[f].Vertices){int id=Point(welded[render]);ring.Add(id);plan.OriginalInfo[id].Surfaces.Add(f);}
                    if(FlatClipConvex(ring,id=>plan.OriginalPoints[id].Float)){group.Input.Add(new FlatClipPolygon{Source=f,Material=Faces[f].Submesh,Points=ring});continue;}
                    if(triangles==null)triangles=BevelSourceTriangles(mesh);
                    var t=new List<int>();foreach(int render in triangles[f])t.Add(Point(welded[render]));
                    t=PreserveFlatClipBoundary(ring,t);
                    for(int j=0;j<t.Count;j+=3)
                    {
                        var ids=new List<int>{t[j],t[j+1],t[j+2]};
                        foreach(int id in ids)plan.OriginalInfo[id].Surfaces.Add(f);
                        group.Input.Add(new FlatClipPolygon{Source=f,Material=Faces[f].Submesh,Points=ids});
                    }
                }
                foreach(int vertex in vertices)foreach(var edge in bevelIncident[vertex])
                {
                    bool inside=false,outside=false;foreach(var use in edge.Uses){inside|=faceSet.Contains(use.face);outside|=!faceSet.Contains(use.face);}
                    if(inside&&(outside||edge.Uses.Count==1)){boundary.Add(edge.A);boundary.Add(edge.B);}
                }
                foreach(int p in group.Planes)
                {
                    var plane=plan.Planes[p];var allowed=new HashSet<int>{plane.Edge.Item1,plane.Edge.Item2};
                    foreach(int other in group.Planes)
                    {
                        var same=plan.Planes[other];
                        if((same.Normal-plane.Normal).Squared<1e-20&&Math.Abs(same.Origin-plane.Origin)<1e-12&&Math.Abs(same.Speed-plane.Speed)<1e-12){if(same.Source!=plane.Source)return null;allowed.Add(same.Edge.Item1);allowed.Add(same.Edge.Item2);}
                    }
                    foreach(int vertex in vertices)
                    {
                        var position=new ClipVector(wave.Originals[vertex]);double distance=plane.Distance(position,0);double error=(1+Math.Abs(plane.Origin))*1e-12;
                        if(distance>error||(Math.Abs(distance)<=error&&!allowed.Contains(vertex)))return null;
                        if(boundary.Contains(vertex))
                        {
                            if(distance>=-error)return null;
                            double time=(distance+bevelEpsilon*8)/plane.Speed;
                            if(time<=0)return null;plan.Limit=Mathf.Min(plan.Limit,(float)time);
                        }
                    }
                    foreach(int f in group.Faces)
                    {
                        var polygon=Faces[f].Vertices;
                        for(int c=0;c<polygon.Count;c++)
                        {
                            int a=welded[polygon[c]],b=welded[polygon[(c+1)%polygon.Count]];
                            if(!wave.Targets.Contains(BevelKey(a,b))&&Math.Abs(plane.Distance(new ClipVector(wave.Originals[a]),0))<1e-12&&Math.Abs(plane.Distance(new ClipVector(wave.Originals[b]),0))<1e-12)return null;
                        }
                    }
                }
            }
            var changed=new HashSet<int>(wave.AffectedFaces);var originalEdges=new HashSet<(int,int)>();
            foreach(int f in wave.AffectedFaces)
            {
                var polygon=Faces[f].Vertices;for(int c=0;c<polygon.Count;c++)originalEdges.Add(BevelKey(welded[polygon[c]],welded[polygon[(c+1)%polygon.Count]]));
            }
            foreach(var key in originalEdges)
            {
                int count=0,direction=0;foreach(var use in bevelEdges[key].Uses)if(!changed.Contains(use.face)){count++;direction+=welded[Faces[use.face].Vertices[use.corner]]==key.Item1?1:-1;}
                if(count>0)plan.Interface.Add(key,(count,direction));
            }
            bevelFlatClip=plan;return plan;
        }

        private static bool FlatClipConvex(List<int> ring,Func<int,Vector3> position)
        {
            if(ring.Count==3)return true;var normal=Vector3.zero;var origin=position(ring[0]);
            for(int i=1;i+1<ring.Count;i++)normal+=Vector3.Cross(position(ring[i])-origin,position(ring[i+1])-origin);
            int axis=BevelAxis(normal);bool positive=false,negative=false;
            for(int i=0;i<ring.Count;i++){int side=BevelOrientation(BevelProject(position(ring[i]),axis),BevelProject(position(ring[(i+1)%ring.Count]),axis),BevelProject(position(ring[(i+2)%ring.Count]),axis));positive|=side>0;negative|=side<0;if(positive&&negative)return false;}
            return positive||negative;
        }

        private static int FlatClipSide(FlatClipPlane plane,ClipVector point,double width)
        {
            double d=plane.Distance(point,width),error=(1+Math.Abs(plane.Origin)+Math.Abs(point.X)+Math.Abs(point.Y)+Math.Abs(point.Z))*1e-12;
            return d>error?1:d< -error?-1:0;
        }
        private static void CleanFlatClipLoop(List<int> points)
        {
            bool changed=true;while(changed&&points.Count>1)
            {
                changed=false;
                for(int i=points.Count-1;i>=0;i--)if(points[i]==points[(i+1)%points.Count]){points.RemoveAt(i);changed=true;if(points.Count<2)break;}
                if(points.Count<3)break;
                for(int i=0;i<points.Count;i++)if(points[i]==points[(i+2)%points.Count]){points.RemoveAt((i+1)%points.Count);changed=true;break;}
            }
        }
        private static bool FlatClipHasArea(List<int> polygon,IList<Vector3> points)
        {
            if(polygon.Count<3)return false;var origin=new ClipVector(points[polygon[0]]);
            for(int i=1;i+1<polygon.Count;i++)if((new ClipVector(points[polygon[i]])-origin).Cross(new ClipVector(points[polygon[i+1]])-origin).Squared>0)return true;
            return false;
        }
        private static List<List<int>> FlatClipCycles(Dictionary<(int,int),(int a,int b)> edges)
        {
            var next=new SortedDictionary<int,int>();foreach(var edge in edges.Values){if(next.ContainsKey(edge.a))throw new BevelOverlap("The clipping boundary branches at a corner.");next.Add(edge.a,edge.b);}
            var result=new List<List<int>>();
            while(next.Count>0)
            {
                int start=-1;foreach(int id in next.Keys){start=id;break;}var ring=new List<int>();int current=start;
                do{ring.Add(current);if(!next.TryGetValue(current,out int following))throw new BevelOverlap("The clipping boundary is open.");next.Remove(current);current=following;}while(current!=start);
                CleanFlatClipLoop(ring);result.Add(ring);
            }
            return result;
        }
        private static void FlatClipBoundaryEdge(Dictionary<(int,int),(int a,int b)> edges,int a,int b)
        {
            if(a==b)return;var key=BevelKey(a,b);
            if(edges.TryGetValue(key,out var previous)){if(previous.a!=b)throw new BevelOverlap("The clipping faces have inconsistent winding.");edges.Remove(key);}else edges.Add(key,(a,b));
        }
        private static List<int> PreserveFlatClipBoundary(List<int> ring,List<int> triangles)
        {
            var used=new HashSet<int>(triangles);var order=new Dictionary<int,int>();for(int i=0;i<ring.Count;i++)order.Add(ring[i],i);
            var result=new List<int>();
            for(int i=0;i<triangles.Count;i+=3)
            {
                var pieces=new List<int[]>{new[]{triangles[i],triangles[i+1],triangles[i+2]}};
                for(int piece=0;piece<pieces.Count;piece++)
                {
                    var triangle=pieces[piece];bool split=false;
                    for(int edge=0;edge<3;edge++)
                    {
                        int a=triangle[edge],b=triangle[(edge+1)%3],c=triangle[(edge+2)%3];
                        if(!order.TryGetValue(a,out int start)||!order.TryGetValue(b,out int end))continue;
                        var chain=new List<int>{a};bool boundary=true;
                        for(int j=(start+1)%ring.Count;j!=end;j=(j+1)%ring.Count){int point=ring[j];if(used.Contains(point)){boundary=false;break;}chain.Add(point);}
                        if(!boundary||chain.Count<2)continue;chain.Add(b);pieces.RemoveAt(piece);
                        for(int j=1;j<chain.Count;j++)pieces.Add(new[]{chain[j-1],chain[j],c});piece--;split=true;break;
                    }
                    if(!split)result.AddRange(triangle);
                }
            }
            return result;
        }

        private void BuildFlatClip(FlatClipPlan plan,float width)
        {
            plan.Points.Clear();plan.Points.AddRange(plan.OriginalPoints);plan.FloatPoints.Clear();plan.Info.Clear();plan.Identities.Clear();plan.Steps.Clear();plan.Checks.Clear();
            for(int i=0;i<plan.OriginalPoints.Count;i++){var point=plan.OriginalPoints[i].Float;plan.FloatPoints.Add(point);plan.Info.Add(plan.OriginalInfo[i].Copy());plan.Identities.Add(point,i);}
            var output=new List<FlatClipPolygon>();
            int Intersection(int a,int b,int p,Dictionary<(int,int),int> cuts)
            {
                var key=BevelKey(a,b);if(cuts.TryGetValue(key,out int cached))return cached;
                var plane=plan.Planes[p];double da=plane.Distance(plan.Points[a],width),db=plane.Distance(plan.Points[b],width);
                double t=da/(da-db);var point=plan.Points[a]+(plan.Points[b]-plan.Points[a])*t;var published=point.Float;
                if(!BevelFinite(published))throw new BevelOverlap("A clipping intersection is not finite.");
                bool alias=plan.Identities.TryGetValue(published,out int id);
                if(!alias){id=plan.Points.Count;plan.Points.Add(point);plan.FloatPoints.Add(published);plan.Info.Add(new FlatClipPointInfo());plan.Identities.Add(published,id);}
                var info=plan.Info[id];foreach(int s in plan.Info[a].Surfaces)if(plan.Info[b].Surfaces.Contains(s))info.Surfaces.Add(s);info.Surfaces.Add(-p-1);
                if(a<plan.OriginalIds.Count&&b<plan.OriginalIds.Count)
                {
                    var original=BevelKey(plan.OriginalIds[a],plan.OriginalIds[b]);if(bevelEdges.ContainsKey(original))info.OriginalEdges.Add(original);
                }
                foreach(var edge in plan.Info[a].OriginalEdges)if(plan.Info[b].OriginalEdges.Contains(edge)||(b<plan.OriginalIds.Count&&(plan.OriginalIds[b]==edge.Item1||plan.OriginalIds[b]==edge.Item2)))info.OriginalEdges.Add(edge);
                foreach(var edge in plan.Info[b].OriginalEdges)if(a<plan.OriginalIds.Count&&(plan.OriginalIds[a]==edge.Item1||plan.OriginalIds[a]==edge.Item2))info.OriginalEdges.Add(edge);
                plan.Steps.Add(new FlatClipStep{A=a,B=b,Output=id,Plane=p,Alias=alias});cuts.Add(key,id);return id;
            }
            foreach(var group in plan.Groups)
            {
                var polygons=new List<FlatClipPolygon>();foreach(var polygon in group.Input)polygons.Add(polygon.With(new List<int>(polygon.Points)));
                foreach(int p in group.Planes)
                {
                    var plane=plan.Planes[p];var cuts=new Dictionary<(int,int),int>();var retained=new List<FlatClipPolygon>();bool outside=false;
                    foreach(var polygon in polygons)
                    {
                        var loop=new List<int>();int count=polygon.Points.Count;var sides=new int[count];
                        for(int c=0;c<count;c++){int id=polygon.Points[c];sides[c]=FlatClipSide(plane,plan.Points[id],width);plan.Checks.Add(new FlatClipCheck{Point=id,Plane=p,Side=sides[c]});outside|=sides[c]>0;if(sides[c]==0)plan.Info[id].Surfaces.Add(-p-1);}
                        for(int c=0;c<count;c++)
                        {
                            int a=polygon.Points[c],b=polygon.Points[(c+1)%count],sa=sides[c],sb=sides[(c+1)%count];
                            if(sa<=0)loop.Add(a);
                            if((sa<0&&sb>0)||(sa>0&&sb<0))loop.Add(Intersection(a,b,p,cuts));
                        }
                        CleanFlatClipLoop(loop);if(FlatClipHasArea(loop,plan.FloatPoints))retained.Add(polygon.With(loop));
                    }
                    if(outside)
                    {
                        var boundary=new Dictionary<(int,int),(int,int)>();
                        foreach(var polygon in retained)for(int c=0;c<polygon.Points.Count;c++){int a=polygon.Points[c],b=polygon.Points[(c+1)%polygon.Points.Count];if(plan.Info[a].Surfaces.Contains(-p-1)&&plan.Info[b].Surfaces.Contains(-p-1))FlatClipBoundaryEdge(boundary,b,a);}
                        foreach(var ring in FlatClipCycles(boundary))
                        {
                            if(!FlatClipHasArea(ring,plan.FloatPoints))continue;
                            if(FlatClipConvex(ring,id=>plan.FloatPoints[id])){retained.Add(new FlatClipPolygon{Source=plane.Source,Material=plane.Material,Plane=p,Points=ring});continue;}
                            var triangles=PreserveFlatClipBoundary(ring,BevelTriangulate(ring,plan.FloatPoints,0));
                            for(int c=0;c<triangles.Count;c+=3)retained.Add(new FlatClipPolygon{Source=plane.Source,Material=plane.Material,Plane=p,Points=new List<int>{triangles[c],triangles[c+1],triangles[c+2]}});
                        }
                    }
                    polygons=retained;
                }
                output.AddRange(polygons);
            }
            plan.Output=MergeFlatClipFaces(plan,output);
        }

        private static List<FlatClipPolygon> MergeFlatClipFaces(FlatClipPlan plan,List<FlatClipPolygon> polygons)
        {
            var groups=new Dictionary<(int,int),List<FlatClipPolygon>>();
            foreach(var polygon in polygons){var key=(polygon.Plane,polygon.Plane<0?polygon.Source:0);if(!groups.TryGetValue(key,out var list))groups.Add(key,list=new List<FlatClipPolygon>());list.Add(polygon);}
            var result=new List<FlatClipPolygon>();
            var activeSurfaces=new HashSet<int>();foreach(var polygon in polygons)activeSurfaces.Add(polygon.Plane<0?polygon.Source:-polygon.Plane-1);
            foreach(var list in groups.Values)
            {
                var parents=new int[list.Count];for(int i=0;i<parents.Length;i++)parents[i]=i;
                int Find(int i){while(parents[i]!=i){parents[i]=parents[parents[i]];i=parents[i];}return i;}
                var edgeOwners=new Dictionary<(int,int),(int polygon,int start)>();
                for(int i=0;i<list.Count;i++)for(int c=0;c<list[i].Points.Count;c++)
                {
                    int a=list[i].Points[c],b=list[i].Points[(c+1)%list[i].Points.Count];var key=BevelKey(a,b);
                    if(edgeOwners.TryGetValue(key,out var previous)){if(previous.start!=b)throw new BevelOverlap("Clipped face pieces overlap.");int x=Find(i),y=Find(previous.polygon);if(x!=y)parents[x]=y;}else edgeOwners.Add(key,(i,a));
                }
                var components=new Dictionary<int,Dictionary<(int,int),(int,int)>>();
                for(int i=0;i<list.Count;i++)
                {
                    int root=Find(i);if(!components.TryGetValue(root,out var boundary))components.Add(root,boundary=new Dictionary<(int,int),(int,int)>());
                    for(int c=0;c<list[i].Points.Count;c++)FlatClipBoundaryEdge(boundary,list[i].Points[c],list[i].Points[(c+1)%list[i].Points.Count]);
                }
                foreach(var boundary in components.Values)foreach(var ring in FlatClipCycles(boundary))
                {
                    for(int i=ring.Count-1;i>=0;i--){var info=plan.Info[ring[i]];int active=0;foreach(int surface in info.Surfaces)if(activeSurfaces.Contains(surface))active++;if(!info.Original&&info.OriginalEdges.Count==0&&active<3)ring.RemoveAt(i);}
                    CleanFlatClipLoop(ring);if(FlatClipHasArea(ring,plan.FloatPoints))result.Add(list[0].With(ring));
                }
            }
            return result;
        }
        private bool ReplayFlatClip(FlatClipPlan plan,float width)
        {
            foreach(var step in plan.Steps)
            {
                var plane=plan.Planes[step.Plane];var a=plan.Points[step.A];var b=plan.Points[step.B];double da=plane.Distance(a,width),db=plane.Distance(b,width);
                if(da==db)return false;var position=a+(b-a)*(da/(da-db));var published=position.Float;if(!BevelFinite(published))return false;
                if(step.Alias){if(!published.Equals(plan.FloatPoints[step.Output]))return false;}else{plan.Points[step.Output]=position;plan.FloatPoints[step.Output]=published;}
            }
            foreach(var check in plan.Checks)if(FlatClipSide(plan.Planes[check.Plane],plan.Points[check.Point],width)!=check.Side)return false;
            var seen=new Dictionary<Vector3,int>();
            foreach(var polygon in plan.Output)foreach(int id in polygon.Points){var position=plan.FloatPoints[id];if(seen.TryGetValue(position,out int previous)&&previous!=id)return false;seen[position]=id;}
            return true;
        }
        private void EvaluateFlatClip(FlatClipPlan plan,float width)
        {
            if(plan.LastWidth==width&&plan.Output!=null)return;
            if(plan.Output==null||!ReplayFlatClip(plan,width))BuildFlatClip(plan,width);
            plan.LastWidth=width;
        }
        private bool FlatClipChanged(FlatClipPlan plan)
        {
            var seen=new HashSet<int>();
            foreach(var polygon in plan.Output)
            {
                if(polygon.Plane>=0||!seen.Add(polygon.Source))return true;
                var original=Faces[polygon.Source].Vertices;if(original.Count!=polygon.Points.Count)return true;
                int offset=-1;
                for(int i=0;i<original.Count;i++)if(polygon.Points[0]<plan.OriginalIds.Count&&welded[original[i]]==plan.OriginalIds[polygon.Points[0]]){offset=i;break;}
                if(offset<0)return true;
                for(int i=0;i<original.Count;i++){int id=polygon.Points[i];if(id>=plan.OriginalIds.Count||plan.OriginalIds[id]!=welded[original[(i+offset)%original.Count]])return true;}
            }
            int count=0;foreach(var group in plan.Groups)count+=group.Faces.Count;return seen.Count!=count;
        }
        private void ValidateFlatClipDraft(BevelDraft draft)
        {
            var uses=new Dictionary<(int,int),(int count,int direction)>(bevelFlatClip.Interface);
            void Face(IList<int> loop)
            {
                if(loop.Count<3||new HashSet<int>(loop).Count!=loop.Count)throw new BevelOverlap("A clipped face has repeated or collapsed corners.");
                for(int i=0;i<loop.Count;i++)
                {
                    int a=loop[i],b=loop[(i+1)%loop.Count];if(draft.Positions[a].Equals(draft.Positions[b]))throw new BevelOverlap("The requested bevel is below coordinate precision.");
                    var key=BevelKey(a,b);uses.TryGetValue(key,out var use);uses[key]=(use.count+1,use.direction+(a==key.Item1?1:-1));
                }
            }
            foreach(var polygon in draft.Polygons){var loop=new List<int>();foreach(int v in polygon.Vertices)loop.Add(draft.Vertices[v].Geo);Face(loop);}
            foreach(var pair in uses)
            {
                if(pair.Value.count==2&&pair.Value.direction==0)continue;
                bool unchanged=bevelEdges.TryGetValue(pair.Key,out var original)&&original.Uses.Count==pair.Value.count;
                if(unchanged){int direction=0;foreach(var use in original.Uses)direction+=welded[Faces[use.face].Vertices[use.corner]]==pair.Key.Item1?1:-1;unchanged=direction==pair.Value.direction;}
                if(!unchanged)throw new BevelOverlap("The clipping transaction did not produce a closed, consistently wound surface.");
            }
        }
    }
}
