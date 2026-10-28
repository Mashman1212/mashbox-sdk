using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using MashBoxSDK.AnimationStudio.ThirdParty.StraightSkeleton;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        private sealed class ExtrusionInsetPlan
        {
            internal readonly Dictionary<(int,int),Vector3> Directions = new Dictionary<(int,int),Vector3>();
            internal float PositiveInsetLimit = float.PositiveInfinity, NegativeInsetLimit = float.PositiveInfinity;
        }

        // Capture offset-line trajectories once. The same vendored wavefront
        // kernel as bevel supplies miter velocities and first collision times.
        private ExtrusionInsetPlan PrepareExtrusionInset(List<(int face,int a,int b)> boundary,
            Vector3[] normals, Func<int,int> group, Func<int,int> canonical, bool faceMode)
        {
            var plan = new ExtrusionInsetPlan();
            if (boundary.Count == 0) throw new InvalidOperationException("Inset needs a selected region with a boundary.");
            if (!faceMode)
            {
                var constraints = new Dictionary<(int,int),List<Vector3>>();
                foreach (var edge in boundary)
                {
                    var inward = Vector3.Cross(normals[edge.face], (Positions[edge.b]-Positions[edge.a]).normalized).normalized;
                    foreach (int v in new[]{edge.a,edge.b})
                    {
                        var key=(group(edge.face),canonical(v));
                        if(!constraints.TryGetValue(key,out var list))constraints[key]=list=new List<Vector3>();
                        if(!list.Any(n=>Vector3.Dot(n,inward)>.99999f))list.Add(inward);
                    }
                }
                foreach(var pair in constraints)
                {
                    var n=pair.Value;
                    if(n.Count>2)throw new InvalidOperationException("Inset needs an unbranched boundary edge chain.");
                    float denominator=n.Count==1?1:1+Vector3.Dot(n[0],n[1]);
                    if(denominator<1e-5f)throw new InvalidOperationException("Inset boundary directions cancel at a corner.");
                    plan.Directions[pair.Key]=n.Count==1?-n[0]:-(n[0]+n[1])/denominator;
                }
                // Edge lengths must not collapse while extending an open chain.
                foreach(var edge in boundary)
                {
                    var delta=Positions[edge.b]-Positions[edge.a];
                    var velocity=plan.Directions[(group(edge.face),canonical(edge.b))]-plan.Directions[(group(edge.face),canonical(edge.a))];
                    float rate=Vector3.Dot(delta.normalized,velocity);
                    if(rate< -1e-6f)plan.PositiveInsetLimit=Mathf.Min(plan.PositiveInsetLimit,-delta.magnitude/rate);
                    if(rate>1e-6f)plan.NegativeInsetLimit=Mathf.Min(plan.NegativeInsetLimit,delta.magnitude/rate);
                }
                return plan;
            }

            var outgoing=new Dictionary<(int,int),int>();
            for(int i=0;i<boundary.Count;i++)
            {
                var edge=boundary[i];var key=(group(edge.face),canonical(edge.a));
                if(outgoing.ContainsKey(key))throw new InvalidOperationException("Inset needs non-branching region boundaries. Separate regions that meet only at one vertex.");
                outgoing.Add(key,i);
            }
            var used=new HashSet<int>();var loops=new List<List<int>>();
            for(int seed=0;seed<boundary.Count;seed++)
            {
                if(used.Contains(seed))continue;var loop=new List<int>();int current=seed;
                do
                {
                    if(!used.Add(current))throw new InvalidOperationException("The inset boundary does not form a simple loop.");
                    loop.Add(current);var edge=boundary[current];
                    if(!outgoing.TryGetValue((group(edge.face),canonical(edge.b)),out current))
                        throw new InvalidOperationException("The inset region has an open boundary.");
                }while(current!=seed);
                loops.Add(loop);
            }
            // Separate planar charts; holes and disjoint contours on one chart
            // enter one graph, preserving their winding and mutual collisions.
            while(loops.Count>0)
            {
                var seed=boundary[loops[0][0]];var normal=normals[seed.face];var origin=Positions[seed.a];
                var x=(Positions[seed.b]-origin).normalized;var y=Vector3.Cross(normal,x);
                var chart=new List<List<int>>();float extent=0;
                for(int i=loops.Count-1;i>=0;i--)
                {
                    var loop=loops[i];var first=boundary[loop[0]];
                    if(group(first.face)!=group(seed.face)||Vector3.Dot(normal,normals[first.face])<.99999f||Mathf.Abs(Vector3.Dot(Positions[first.a]-origin,normal))>1e-5f)continue;
                    foreach(int id in loop)
                    {
                        var edge=boundary[id];extent=Mathf.Max(extent,(Positions[edge.a]-origin).magnitude);
                        if(Vector3.Dot(normal,normals[edge.face])<.99999f||Mathf.Abs(Vector3.Dot(Positions[edge.a]-origin,normal))>Mathf.Max(1e-5f,extent*1e-5f))
                            throw new InvalidOperationException("Keep Faces Together inset requires planar regions. Extrude these faces individually instead.");
                    }
                    chart.Add(loop);loops.RemoveAt(i);
                }
                if(extent<1e-6f)throw new InvalidOperationException("Inset boundary has no stable plane.");
                float normalization=extent/100f;var contours=new List<float2[]>();var ids=new List<(int,int)>();
                foreach(var loop in chart)
                {
                    var contour=new float2[loop.Count];
                    for(int j=0;j<loop.Count;j++)
                    {
                        var edge=boundary[loop[loop.Count-1-j]];var point=Positions[edge.a]-origin;
                        contour[j]=new float2(Vector3.Dot(point,x)/normalization,Vector3.Dot(point,y)/normalization);
                        ids.Add((group(edge.face),canonical(edge.a)));
                    }
                    contours.Add(contour);
                }
                var polygon=new PolygonWithHoles(contours[0],contours.Skip(1).ToList());
                for(int sign=1;sign>=-1;sign-=2)
                {
                    var graph=new WavefrontGraph(polygon,new BevelEventListener(),false,Enumerable.Repeat(sign/normalization,ids.Count).ToArray());
                    graph.GenerateInitialEvents();float limit=float.PositiveInfinity;
                    for(int i=0;i<ids.Count;i++)
                    {
                        var v=graph.vertexDatas[i];
                        if(sign>0)
                        {
                            var velocity=(x*v.velocity.x+y*v.velocity.y)*normalization;
                            if(float.IsNaN(velocity.sqrMagnitude)||float.IsInfinity(velocity.sqrMagnitude))throw new InvalidOperationException("Inset produced an unstable corner.");
                            plan.Directions[ids[i]]=-velocity;
                        }
                        if(v.partOfSplitEvent&&v.splitTime>1e-6f)limit=Mathf.Min(limit,v.splitTime);
                    }
                    for(int i=0;i<graph.nEdges;i++){var e=graph.edgeEvents[i];if(e.eventType.IsValid()&&e.eventTime>1e-6f)limit=Mathf.Min(limit,e.eventTime);}
                    if(sign>0)plan.NegativeInsetLimit=Mathf.Min(plan.NegativeInsetLimit,limit);else plan.PositiveInsetLimit=Mathf.Min(plan.PositiveInsetLimit,limit);
                }
            }
            return plan;
        }
    }
}
