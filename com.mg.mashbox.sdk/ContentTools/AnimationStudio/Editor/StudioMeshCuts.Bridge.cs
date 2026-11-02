using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        private sealed class BridgeBorder
        {
            internal int Face,A,B;
            internal int Start,End;
        }
        private sealed class BridgeChain
        {
            internal readonly List<BridgeBorder> Edges=new List<BridgeBorder>();
            internal readonly List<int> Vertices=new List<int>();
            internal bool Closed;
        }
        private static double BridgeSegmentDistance(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            var p=new ClipVector(a);var q=new ClipVector(c);var u=new ClipVector(b)-p;var v=new ClipVector(d)-q;var r=p-q;
            double aa=u.Squared,ee=v.Squared,bb=u.Dot(v),cc=u.Dot(r),ff=v.Dot(r),denominator=aa*ee-bb*bb;
            double s=denominator>aa*ee*1e-15?Math.Max(0,Math.Min(1,(bb*ff-cc*ee)/denominator)):0;
            double t=(bb*s+ff)/ee;
            if(t<0){t=0;s=Math.Max(0,Math.Min(1,-cc/aa));}
            else if(t>1){t=1;s=Math.Max(0,Math.Min(1,(bb-cc)/aa));}
            return (r+u*s-v*t).Squared;
        }
        private BridgeChain OrderBridgeChain(List<BridgeBorder> edges)
        {
            var incoming=new Dictionary<int,BridgeBorder>();var outgoing=new Dictionary<int,BridgeBorder>();
            foreach(var edge in edges)
            {
                if(incoming.ContainsKey(edge.End)||outgoing.ContainsKey(edge.Start))
                    throw new InvalidOperationException("Bridge Edges requires unbranched borders with consistent face winding.");
                incoming.Add(edge.End,edge);outgoing.Add(edge.Start,edge);
            }
            var ends=outgoing.Keys.Where(v=>!incoming.ContainsKey(v)).ToArray();
            bool closed=ends.Length==0;
            if(!closed&&ends.Length!=1)throw new InvalidOperationException("Each bridge group must be one continuous chain or loop.");
            int start=closed?outgoing.Keys.Min():ends[0];int current=start;
            var result=new BridgeChain{Closed=closed};result.Vertices.Add(current);
            var visited=new HashSet<int>();
            while(outgoing.TryGetValue(current,out var edge)&&visited.Add(current))
            {
                result.Edges.Add(edge);current=edge.End;
                if(current==start)break;result.Vertices.Add(current);
            }
            if(result.Edges.Count!=edges.Count||closed&&current!=start||!closed&&outgoing.ContainsKey(current))
                throw new InvalidOperationException("Each bridge group must be one continuous chain or loop.");
            return result;
        }
        internal int BridgeEdges(StudioModelDocument.Node node,IEnumerable<(int a,int b)> selection,int divisions=0,float curvature=0)
        {
            if(divisions<0||divisions>64)throw new ArgumentOutOfRangeException(nameof(divisions),"Bridge divisions must be between 0 and 64.");
            if(float.IsNaN(curvature)||float.IsInfinity(curvature)||curvature < -1||curvature > 1)throw new ArgumentOutOfRangeException(nameof(curvature));
            LastBridgeProfile=null;
            var mesh=node.mesh;
            if(!mesh||!mesh.isReadable||node.skinned||node.blendShape!=null||mesh.blendShapeCount>0||mesh.bindposes.Length>0||mesh.boneWeights.Length>0)
                throw new InvalidOperationException("Bridge Edges requires a readable static mesh without skinning or blend shapes.");
            for(int sub=0;sub<mesh.subMeshCount;sub++)if(mesh.GetTopology(sub)!=MeshTopology.Triangles)
                throw new InvalidOperationException("Bridge Edges requires Studio polygon surfaces.");
            var uses=new Dictionary<(int,int),List<BridgeBorder>>();
            for(int f=0;f<Faces.Count;f++)for(int c=0;c<Faces[f].Vertices.Count;c++)
            {
                var face=Faces[f].Vertices;int a=face[c],b=face[(c+1)%face.Count];var key=Key(a,b);
                if(!uses.TryGetValue(key,out var list))uses.Add(key,list=new List<BridgeBorder>());
                list.Add(new BridgeBorder{Face=f,A=a,B=b,Start=welded[a],End=welded[b]});
            }
            var chosen=new Dictionary<(int,int),BridgeBorder>();
            foreach(var pair in selection)
            {
                if(pair.a<0||pair.b<0||pair.a>=Positions.Length||pair.b>=Positions.Length)
                    throw new InvalidOperationException("The edge selection changed. Select the bridge borders again.");
                var key=Key(pair.a,pair.b);
                if(key.Item1==key.Item2)throw new InvalidOperationException("Bridge Edges cannot use a zero-length edge.");
                if(!uses.TryGetValue(key,out var list)||list.Count!=1)
                    throw new InvalidOperationException("Bridge Edges requires open boundary edges; interior or non-manifold edges cannot receive another face.");
                chosen[key]=list[0];
            }
            if(chosen.Count<2)throw new InvalidOperationException("Select exactly two non-touching edge groups with equal edge counts.");
            var incidence=new Dictionary<int,List<BridgeBorder>>();
            foreach(var edge in chosen.Values)foreach(int v in new[]{edge.Start,edge.End})
            {
                if(!incidence.TryGetValue(v,out var list))incidence.Add(v,list=new List<BridgeBorder>());list.Add(edge);
                if(list.Count>2)throw new InvalidOperationException("Bridge Edges cannot bridge a branched edge selection.");
            }
            var groups=new List<List<BridgeBorder>>();var remaining=new HashSet<BridgeBorder>(chosen.Values);
            foreach(var seed in chosen.OrderBy(p=>p.Key).Select(p=>p.Value))
            {
                if(!remaining.Remove(seed))continue;var group=new List<BridgeBorder>();var pending=new Queue<BridgeBorder>();pending.Enqueue(seed);
                while(pending.Count>0)
                {
                    var edge=pending.Dequeue();group.Add(edge);
                    foreach(int v in new[]{edge.Start,edge.End})foreach(var next in incidence[v])if(remaining.Remove(next))pending.Enqueue(next);
                }
                groups.Add(group);
            }
            if(groups.Count!=2)throw new InvalidOperationException("Select exactly two separate edge groups. Touching groups, or more than two groups, cannot be bridged.");
            if(groups[0].Count!=groups[1].Count)throw new InvalidOperationException("The two bridge edge groups must have the same edge count.");
            var first=OrderBridgeChain(groups[0]);var second=OrderBridgeChain(groups[1]);
            if(first.Closed!=second.Closed)throw new InvalidOperationException("Bridge two open chains or two closed loops; a chain cannot be paired with a loop.");
            var originals=new Dictionary<int,Vector3>();foreach(var edge in chosen.Values){originals[edge.Start]=Positions[edge.A];originals[edge.End]=Positions[edge.B];}
            var bounds=new Bounds(originals.Values.First(),Vector3.zero);double coordinate=0;
            foreach(var point in originals.Values){if(!BevelFinite(point))throw new InvalidOperationException("Bridge coordinates must be finite.");bounds.Encapsulate(point);coordinate=Math.Max(coordinate,Math.Max(Math.Abs(point.x),Math.Max(Math.Abs(point.y),Math.Abs(point.z))));}
            double tolerance=Math.Max(1e-20,Math.Max(bounds.size.magnitude*1e-9,coordinate*1e-14));double squared=tolerance*tolerance;
            foreach(var a in first.Edges)foreach(var b in second.Edges)
                if(BridgeSegmentDistance(Positions[a.A],Positions[a.B],Positions[b.A],Positions[b.B])<=squared)
                    throw new InvalidOperationException("The two bridge groups touch or intersect. Choose non-touching groups.");
            int count=first.Edges.Count;int[] paired;
            if(first.Closed)
            {
                int best=0;double score=double.PositiveInfinity;
                for(int shift=0;shift<count;shift++)
                {
                    double candidate=0;for(int i=0;i<count;i++)candidate+=(new ClipVector(originals[first.Vertices[i]])-new ClipVector(originals[second.Vertices[(shift-i+count)%count]])).Squared;
                    if(candidate<score){score=candidate;best=shift;}
                }
                paired=Enumerable.Range(0,count).Select(i=>second.Vertices[(best-i+count)%count]).ToArray();
            }
            else paired=second.Vertices.AsEnumerable().Reverse().ToArray();
            for(int i=0;i<paired.Length;i++)for(int j=i+1;j<paired.Length;j++)
                if(BridgeSegmentDistance(originals[first.Vertices[i]],originals[paired[i]],originals[first.Vertices[j]],originals[paired[j]])<=squared)
                    throw new InvalidOperationException("The selected borders would create a crossing bridge. Check their winding and correspondence.");

            var source=BevelSource(mesh);var positions=Positions.ToList();var normals=source.RetainedNormals.ToList();var tangents=source.Tangents.ToList();var colors=source.Colors.ToList();
            var uv=new List<Vector4>[8];var dimensions=(int[])source.Dimensions.Clone();
            for(int c=0;c<8;c++)uv[c]=source.UV[c]?.ToList()??new List<Vector4>();
            if(uv[0].Count==0){uv[0].AddRange(Enumerable.Repeat(Vector4.zero,positions.Count));dimensions[0]=2;}
            var geometry=welded.ToList();
            var normalGroups=node.normalGroups!=null&&node.normalGroups.Length==Positions.Length?node.normalGroups.ToList():Enumerable.Range(0,Positions.Length).ToList();
            int nextGroup=normalGroups.Count>0?normalGroups.Max()+1:0;
            var indices=Enumerable.Range(0,mesh.subMeshCount).Select(sub=>mesh.GetTriangles(sub).ToList()).ToArray();
            var polygons=Faces.SelectMany(f=>new[]{f.Submesh,f.Vertices.Count}.Concat(f.Vertices)).ToList();
            var secondByKey=second.Edges.ToDictionary(e=>e.Start<e.End?(e.Start,e.End):(e.End,e.Start));
            var bridgeUses=new Dictionary<(int,int),(int count,int direction)>();
            var borderPlanes=new Dictionary<int,(Vector3 normal,Vector3 origin,bool planar)>();
            void CheckBorderPlane(int face,Vector3 bridgeNormal,int[] corners)
            {
                if(!borderPlanes.TryGetValue(face,out var plane))
                {
                    var ring=Faces[face].Vertices;var faceNormal=BevelPolygonNormal(ring,Positions);var origin=Positions[ring[0]];
                    if(faceNormal.sqrMagnitude<.5f)throw new InvalidOperationException("A selected bridge border belongs to a degenerate face.");
                    plane=(faceNormal,origin,ring.All(v=>Math.Abs(ClipDot(Positions[v]-origin,faceNormal))<=tolerance));borderPlanes.Add(face,plane);
                }
                if(plane.planar&&Vector3.Dot(bridgeNormal,plane.normal)<-.999999f&&corners.All(v=>Math.Abs(ClipDot(Positions[v]-plane.origin,plane.normal))<=tolerance))
                    throw new InvalidOperationException("The gap is already filled by a coplanar face; the bridge would overlap it.");
            }
            var lengths=new double[count+1];for(int i=0;i<count;i++){int next=first.Closed?(i+1)%count:i+1;lengths[i+1]=lengths[i]+Math.Sqrt((new ClipVector(originals[first.Vertices[i]])-new ClipVector(originals[first.Vertices[next]])).Squared);}
            var patchSides=new List<(int first,int second)>();
            for(int i=0;i<count;i++)
            {
                int next=first.Closed?(i+1)%count:i+1;var edge=first.Edges[i];int b0=paired[i],b1=paired[next];
                var other=secondByKey[b0<b1?(b0,b1):(b1,b0)];
                int raw0=other.Start==b0?other.A:other.B,raw1=other.Start==b1?other.A:other.B;
                var from=new[]{edge.B,edge.A,raw0,raw1};var loop=from.ToList();
                if(BridgeSegmentDistance(Positions[from[1]],Positions[from[2]],Positions[from[3]],Positions[from[0]])<=squared)
                    throw new InvalidOperationException("Bridge would create a crossing polygon.");
                List<int> triangles;
                try{triangles=BevelTriangulate(loop,Positions,0);}catch(BevelOverlap){throw new InvalidOperationException("Bridge would create an invalid or collapsed polygon.");}
                var normal=BevelPolygonNormal(loop,Positions);
                if(triangles.Count!=6||normal.sqrMagnitude<.5f)throw new InvalidOperationException("Bridge would create a collapsed polygon.");
                CheckBorderPlane(edge.Face,normal,from);CheckBorderPlane(other.Face,normal,from);
                for(int c=0;c<4;c++)
                {
                    int a=welded[from[c]],b=welded[from[(c+1)%4]];var key=a<b?(a,b):(b,a);
                    if(!bridgeUses.TryGetValue(key,out var use))
                    {
                        if(uses.TryGetValue(key,out var original))use=(original.Count,original.Sum(e=>e.Start<e.End?1:-1));
                    }
                    use=(use.count+1,use.direction+(a<b?1:-1));
                    if(use.count>2||use.count==2&&use.direction!=0)
                        throw new InvalidOperationException("Bridge would make a connecting edge non-manifold or inconsistently wound.");
                    bridgeUses[key]=use;
                }
                for(int t=0;t<triangles.Count;t+=3)
                    if(ClipDot(Vector3.Cross(Positions[triangles[t+1]]-Positions[triangles[t]],Positions[triangles[t+2]]-Positions[triangles[t]]),normal)<=0)
                        throw new InvalidOperationException("Bridge would create a folded polygon. Check the border winding.");
                int start=positions.Count;var tangent=Positions[edge.B]-Positions[edge.A];tangent=BevelUnit(tangent-normal*Vector3.Dot(tangent,normal));
                float handedness=Vector3.Dot(Vector3.Cross(normal,tangent),(Positions[raw0]+Positions[raw1]-Positions[edge.A]-Positions[edge.B]))<0?-1:1;
                var tex=new[]{new Vector2((float)(lengths[i+1]/lengths[count]),0),new Vector2((float)(lengths[i]/lengths[count]),0),new Vector2((float)(lengths[i]/lengths[count]),1),new Vector2((float)(lengths[i+1]/lengths[count]),1)};
                for(int c=0;c<4;c++)
                {
                    int v=from[c];positions.Add(Positions[v]);geometry.Add(welded[v]);normals.Add(normal);normalGroups.Add(nextGroup);
                    if(tangents.Count>0)tangents.Add(new Vector4(tangent.x,tangent.y,tangent.z,handedness));if(colors.Count>0)colors.Add(source.Colors[v]);
                    for(int channel=0;channel<8;channel++)if(uv[channel].Count>0){var value=source.UV[channel]!=null?source.UV[channel][v]:Vector4.zero;if(channel==0){value.x=tex[c].x;value.y=tex[c].y;}uv[channel].Add(value);}
                }
                patchSides.Add((edge.Face,other.Face));
                nextGroup++;int material=Faces[edge.Face].Submesh;var newLoop=Enumerable.Range(start,4).ToArray();
                polygons.Add(material);polygons.Add(4);polygons.AddRange(newLoop);
                foreach(int v in triangles)indices[material].Add(start+Array.IndexOf(from,v));
            }
            if(divisions>0)
            {
                LastBridgeProfile=SubdivideBridge(node,divisions,patchSides,positions,normals,tangents,colors,uv,normalGroups,indices,polygons,geometry);
                LastBridgeProfile.ApplyToBuffers(curvature,positions,normals,tangents);
            }
            // The caller evaluates this on its captured transaction mesh. Everything
            // above validates before publishing any live mesh or Undo registration.
            var format=positions.Count>65535||mesh.indexFormat==IndexFormat.UInt32?IndexFormat.UInt32:IndexFormat.UInt16;
            mesh.Clear();mesh.indexFormat=format;mesh.SetVertices(positions);mesh.SetNormals(normals);
            if(tangents.Count>0)mesh.SetTangents(tangents);if(colors.Count>0)mesh.SetColors(colors);
            for(int c=0;c<8;c++)if(uv[c].Count>0)
            {
                if(dimensions[c]==1)StudioSubdivisionData.SetUV1(mesh,c,uv[c]);else if(dimensions[c]==2)mesh.SetUVs(c,uv[c].Select(v=>new Vector2(v.x,v.y)).ToList());
                else if(dimensions[c]==3)mesh.SetUVs(c,uv[c].Select(v=>new Vector3(v.x,v.y,v.z)).ToList());
                else mesh.SetUVs(c,uv[c]);
            }
            mesh.subMeshCount=indices.Length;for(int sub=0;sub<indices.Length;sub++)mesh.SetTriangles(indices[sub],sub,false);mesh.RecalculateBounds();
            node.cutPolygons=polygons.ToArray();node.cutTriangles=mesh.triangles;node.normalGroups=normalGroups.ToArray();node.geometryVertices=GeometricIds(geometry);return count*(divisions+1);
        }
    }
}
