using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        internal BridgeProfilePlan LastBridgeProfile;
        private sealed class BridgeColumn
        { internal Vector3 A,B,Start,End,Normal; }
        internal sealed class BridgeProfilePlan
        {
            internal readonly int Divisions;
            internal readonly Vector3[] Flat,Delta;
            private readonly int first;
            private readonly int[][] quads;
            private readonly int[] groups;
            private readonly Vector3[] normals;
            private readonly Vector4[] tangents;
            private Vector3[] working;
            private List<Vector3> pointBuffer,normalBuffer;
            private List<Vector4> tangentBuffer;
            private readonly Dictionary<int,Vector3> normalSums=new Dictionary<int,Vector3>();
            internal BridgeProfilePlan(int divisions,int first,Vector3[] flat,Vector3[] delta,int[][] quads,int[] groups,Vector3[] normals,Vector4[] tangents)
            {Divisions=divisions;this.first=first;Flat=flat;Delta=delta;this.quads=quads;this.groups=groups;this.normals=normals;this.tangents=tangents;}
            internal void ApplyToBuffers(float curvature,List<Vector3> points,List<Vector3> outputNormals,List<Vector4> outputTangents)
            {
                var result=Evaluate(curvature);
                for(int i=first;i<result.Length;i++)points[i]=result[i];
                var sums=normalSums;sums.Clear();
                foreach(var q in quads)
                {
                    var n=Vector3.Cross(result[q[1]]-result[q[0]],result[q[2]]-result[q[0]])+Vector3.Cross(result[q[2]]-result[q[0]],result[q[3]]-result[q[0]]);
                    foreach(int i in q){sums.TryGetValue(groups[i],out var sum);sums[groups[i]]=sum+n;}
                }
                for(int i=first;i<result.Length;i++)outputNormals[i]=BevelUnit(sums[groups[i]]);
                if(outputTangents.Count>0)foreach(var q in quads)
                {
                    var along=(result[q[0]]-result[q[1]])+(result[q[3]]-result[q[2]]);
                    var across=(result[q[2]]+result[q[3]])-(result[q[0]]+result[q[1]]);
                    foreach(int i in q){var n=outputNormals[i];var t=BevelUnit(along-n*Vector3.Dot(along,n));outputTangents[i]=new Vector4(t.x,t.y,t.z,Vector3.Dot(Vector3.Cross(n,t),across)<0?-1:1);}
                }
            }
            internal Vector3[] Evaluate(float curvature)
            {
                if(float.IsNaN(curvature)||float.IsInfinity(curvature)||curvature < -1||curvature > 1)throw new ArgumentOutOfRangeException(nameof(curvature));
                var result=working??(working=(Vector3[])Flat.Clone());
                for(int i=first;i<result.Length;i++){result[i]=Flat[i]+Delta[i]*curvature;if(!StudioEdgeFlow.Finite(result[i]))throw new InvalidOperationException("Bridge produced a non-finite profile.");}
                foreach(var q in quads)for(int triangle=0;triangle<2;triangle++)
                {
                    int a=q[0],b=q[triangle+1],c=q[triangle+2];
                    var before=Vector3.Cross(Flat[b]-Flat[a],Flat[c]-Flat[a]);var after=Vector3.Cross(result[b]-result[a],result[c]-result[a]);
                    if(before.sqrMagnitude<=1e-30f||after.sqrMagnitude<=before.sqrMagnitude*1e-12f||Vector3.Dot(before,after)<=0)
                        throw new InvalidOperationException("Bridge curvature would collapse or fold a face. Reduce Curvature or change the border correspondence.");
                }
                return result;
            }
            internal Vector3[] Apply(Mesh mesh,float curvature)
            {
                var points=pointBuffer??(pointBuffer=Flat.ToList());var ns=normalBuffer??(normalBuffer=normals.ToList());var ts=tangentBuffer??(tangentBuffer=tangents.ToList());ApplyToBuffers(curvature,points,ns,ts);
                mesh.SetVertices(points);mesh.SetNormals(ns);if(ts.Count>0)mesh.SetTangents(ts);mesh.RecalculateBounds();return working;
            }
        }
        private BridgeProfilePlan SubdivideBridge(StudioModelDocument.Node node,int divisions,List<(int first,int second)> sides,
            List<Vector3> points,List<Vector3> normals,List<Vector4> tangents,List<Color> colors,List<Vector4>[] uv,List<int> groups,List<int>[] indices,List<int> polygons,List<int> geometry)
        {
            int first=Positions.Length,segments=divisions+1;var oldGeometry=geometry.ToArray();geometry.RemoveRange(first,geometry.Count-first);var gridGeometry=new Dictionary<(int,int,int),int>();int nextGeo=sourceGeometry.Length;
            var oldPoints=points.ToArray();var oldColors=colors.ToArray();var oldUV=uv.Select(x=>x.ToArray()).ToArray();
            var columns=new Dictionary<(Vector3,Vector3),BridgeColumn>();
            BridgeColumn Column(Vector3 a,Vector3 b)
            {if(!columns.TryGetValue((a,b),out var column))columns[(a,b)]=column=new BridgeColumn{A=a,B=b};return column;}
            for(int p=0;p<sides.Count;p++)
            {
                int start=first+p*4;var a1=oldPoints[start];var a0=oldPoints[start+1];var b0=oldPoints[start+2];var b1=oldPoints[start+3];
                var n0=BevelPolygonNormal(Faces[sides[p].first].Vertices,Positions);var n1=BevelPolygonNormal(Faces[sides[p].second].Vertices,Positions);
                var outgoing=BevelUnit(Vector3.Cross(a1-a0,n0));var incoming=BevelUnit(Vector3.Cross(n1,b0-b1));var n=BevelUnit(Vector3.Cross(a0-a1,b0-a1));
                foreach(var c in new[]{Column(a0,b0),Column(a1,b1)}){c.Start+=outgoing;c.End+=incoming;c.Normal+=n;}
            }
            var delta=new List<Vector3>(Enumerable.Repeat(Vector3.zero,first));var quads=new List<int[]>();
            points.RemoveRange(first,points.Count-first);normals.RemoveRange(first,normals.Count-first);groups.RemoveRange(first,groups.Count-first);
            if(tangents.Count>0)tangents.RemoveRange(first,tangents.Count-first);if(colors.Count>0)colors.RemoveRange(first,colors.Count-first);
            foreach(var channel in uv)if(channel.Count>0)channel.RemoveRange(first,channel.Count-first);
            int originalPolygons=Faces.Sum(f=>2+f.Vertices.Count);polygons.RemoveRange(originalPolygons,polygons.Count-originalPolygons);
            for(int sub=0;sub<indices.Length;sub++){int original=node.mesh.GetIndexCount(sub)>int.MaxValue?throw new InvalidOperationException("Bridge index buffer is too large."):(int)node.mesh.GetIndexCount(sub);indices[sub].RemoveRange(original,indices[sub].Count-original);}
            int nextGroup=groups.Count>0?groups.Max()+1:0;var gridGroups=new Dictionary<(Vector3,Vector3,int),int>();
            int Add(int from,int to,int row,float u)
            {
                float t=row/(float)segments;var a=oldPoints[from];var b=oldPoints[to];var column=columns[(a,b)];var chord=b-a;float span=chord.magnitude;
                var flat=row==0?a:row==segments?b:Vector3.LerpUnclamped(a,b,t);Vector3 displacement=Vector3.zero;
                if(row>0&&row<segments)
                {
                    var n=BevelUnit(column.Normal);var m0=BevelUnit(column.Start)*span;var m1=BevelUnit(column.End)*span;
                    var middle=StudioEdgeFlow.Hermite(a,b,m0,m1,.5f)-(a+b)*.5f;
                    float sign=Vector3.Dot(middle,n)<0?-1:1;
                    displacement=(StudioEdgeFlow.Hermite(a,b,m0,m1,t)-flat)*sign;
                    // Flat neighboring tangents cannot supply a bow. The same
                    // Hermite basis gives an explicit signed arch, with fixed ends.
                    if(Mathf.Abs(Vector3.Dot(middle,n))<span*1e-5f)displacement+=StudioEdgeFlow.Hermite(Vector3.zero,Vector3.zero,n*span,-n*span,t);
                }
                int ga=oldGeometry[from],gb=oldGeometry[to];var geoKey=(ga,gb,row);if(!gridGeometry.TryGetValue(geoKey,out int geo)){geo=row==0?ga:row==segments?gb:nextGeo++;gridGeometry.Add(geoKey,geo);}geometry.Add(geo);
                int index=points.Count;points.Add(flat);delta.Add(displacement);normals.Add(Vector3.zero);
                var key=(a,b,row);if(!gridGroups.TryGetValue(key,out int group)){group=nextGroup++;gridGroups.Add(key,group);}groups.Add(group);
                if(tangents.Count>0)tangents.Add(Vector4.zero);if(colors.Count>0)colors.Add(Color.LerpUnclamped(oldColors[from],oldColors[to],t));
                for(int channel=0;channel<8;channel++)if(uv[channel].Count>0){var value=Vector4.LerpUnclamped(oldUV[channel][from],oldUV[channel][to],t);if(channel==0){value.x=u;value.y=t;}uv[channel].Add(value);}
                return index;
            }
            for(int p=0;p<sides.Count;p++)
            {
                int start=first+p*4,material=Faces[sides[p].first].Submesh;float u1=oldUV[0][start].x,u0=oldUV[0][start+1].x;
                for(int row=0;row<segments;row++)
                {
                    var q=new[]{Add(start,start+3,row,u1),Add(start+1,start+2,row,u0),Add(start+1,start+2,row+1,u0),Add(start,start+3,row+1,u1)};
                    quads.Add(q);polygons.Add(material);polygons.Add(4);polygons.AddRange(q);indices[material].AddRange(new[]{q[0],q[1],q[2],q[0],q[2],q[3]});
                }
            }
            return new BridgeProfilePlan(divisions,first,points.ToArray(),delta.ToArray(),quads.ToArray(),groups.ToArray(),normals.ToArray(),tangents.ToArray());
        }
    }
}
