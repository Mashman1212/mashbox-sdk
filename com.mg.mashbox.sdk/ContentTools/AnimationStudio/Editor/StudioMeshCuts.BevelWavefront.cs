using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using MashBoxSDK.AnimationStudio.ThirdParty.StraightSkeleton;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        // Immutable analysis of the captured source. Parameter scrubbing evaluates
        // these trajectories; it never rebuilds the skeleton or changes its source.
        private sealed class BevelWavefrontPlan
        {
            internal HashSet<(int,int)> Targets;
            internal Vector3[] Originals;
            internal Dictionary<int,float> Distances;
            internal Dictionary<(int,int),float> EdgeSpeeds;
            internal readonly List<int> SelectedFaces = new List<int>(), AffectedFaces = new List<int>(), StaticFaces = new List<int>();
            internal Vector3[][] Velocities;
            internal float Limit = float.PositiveInfinity;
            internal Dictionary<(int,int),Vector3> FlatTerminalCuts;
            internal HashSet<int> FlatTerminalOwners;
            internal float FlatTerminalLimit = float.PositiveInfinity;
        }
        private sealed class BevelEventListener : IEventTimeChangeListener
        {
            public void AddOrUpdateEdgeEvent(int edgeIndex) { }
            public void AddOrUpdateVertexEvent(int vertexIndex) { }
        }
        private BevelWavefrontPlan bevelWavefront;

        // At a three-face vertex with two beveled edges, their outer rails
        // must meet at one point on the stationary edge. For face angle theta,
        // travel = offset / sin(theta). Solve these exact offset ratios over
        // the connected edges before constructing the face wavefronts. Each
        // edge keeps one constant speed at both ends, preserving parallel rails
        // and the original supporting planes instead of sealing a mismatch
        // with an artificial sliver. Inconsistent cycles retain separate fans.
        private Dictionary<(int,int),float> BalancedBevelEdgeSpeeds(BevelWavefrontPlan plan,
            HashSet<(int,int)> targets, HashSet<int> affected)
        {
            var links = new Dictionary<(int,int),List<((int,int) edge,double ratio)>>();
            foreach (var key in targets) links[key] = new List<((int,int),double)>();
            foreach (int vertex in affected)
            {
                var incident = bevelIncident[vertex];
                if (incident.Count != 3) continue;
                BevelEdge first = null, second = null, stationary = null;
                foreach (var edge in incident)
                {
                    if (!targets.Contains(BevelKey(edge.A,edge.B))) stationary = edge;
                    else if (first == null) first = edge;
                    else second = edge;
                }
                if (first == null || second == null || stationary == null ||
                    first.Uses.Count != 2 || second.Uses.Count != 2 || stationary.Uses.Count != 2) continue;
                Vector3 Direction(BevelEdge edge) => BevelUnit(plan.Originals[edge.A == vertex ? edge.B : edge.A] - plan.Originals[vertex]);
                var fixedDirection = Direction(stationary);
                double a = Vector3.Cross(Direction(first),fixedDirection).magnitude;
                double b = Vector3.Cross(Direction(second),fixedDirection).magnitude;
                if (a < 1e-6 || b < 1e-6) continue;
                var x = BevelKey(first.A,first.B); var y = BevelKey(second.A,second.B);
                double ratio = Math.Log(b/a);
                links[x].Add((y,ratio)); links[y].Add((x,-ratio));
            }
            var result = new Dictionary<(int,int),float>();
            var values = new Dictionary<(int,int),double>(); var region = new List<(int,int)>();
            foreach (var seed in targets)
            {
                if (values.ContainsKey(seed)) continue;
                region.Clear(); region.Add(seed); values.Add(seed,0); bool consistent = true; double maximum = 0;
                for (int cursor=0;cursor<region.Count;cursor++)
                {
                    var edge = region[cursor]; maximum = Math.Max(maximum,values[edge]);
                    foreach (var link in links[edge])
                    {
                        double expected = values[edge] + link.ratio;
                        if (values.TryGetValue(link.edge,out double previous)) consistent &= Math.Abs(previous-expected) < 1e-4;
                        else { values.Add(link.edge,expected); region.Add(link.edge); }
                    }
                }
                foreach (var edge in region)
                    result.Add(edge,plan.Distances[edge.Item1] * (consistent ? (float)Math.Exp(values[edge]-maximum) : 1));
            }
            return result;
        }

        private BevelWavefrontPlan PrepareBevelWavefront(HashSet<(int,int)> targets, HashSet<int> affected)
        {
            if(bevelWavefront != null && bevelWavefront.Targets.SetEquals(targets)) return bevelWavefront;
            int count=0;foreach(int id in welded)count=Math.Max(count,id+1);
            var plan=new BevelWavefrontPlan { Targets=new HashSet<(int,int)>(targets), Originals=new Vector3[count],
                Distances=new Dictionary<int,float>(), Velocities=new Vector3[Faces.Count][] };
            for(int i=0;i<Positions.Length;i++)plan.Originals[welded[i]]=Positions[i];
            foreach(int vertex in affected)
            {
                plan.Distances.Add(vertex,1);
            }
            var visited=new HashSet<int>();var region=new List<int>();
            foreach(int seed in affected)
            {
                if(!visited.Add(seed))continue;
                region.Clear();region.Add(seed);float distance=plan.Distances[seed];
                for(int cursor=0;cursor<region.Count;cursor++)
                {
                    int vertex=region[cursor];distance=Mathf.Min(distance,plan.Distances[vertex]);
                    foreach(var edge in bevelIncident[vertex])
                    {
                        if(!targets.Contains(BevelKey(edge.A,edge.B)))continue;
                        int other=edge.A==vertex?edge.B:edge.A;if(visited.Add(other))region.Add(other);
                    }
                }
                foreach(int vertex in region)plan.Distances[vertex]=distance;
            }
            plan.EdgeSpeeds = BalancedBevelEdgeSpeeds(plan, targets, affected);
            for(int f=0;f<Faces.Count;f++)
            {
                var face=Faces[f].Vertices;int n=face.Count;bool selected=false;
                bool touches=false; foreach (int v in face) touches|=affected.Contains(welded[v]);
                if (touches) plan.AffectedFaces.Add(f); else plan.StaticFaces.Add(f);
                for(int c=0;c<n;c++)selected|=targets.Contains(BevelKey(welded[face[c]],welded[face[(c+1)%n]]));
                if(!selected)continue;
                plan.SelectedFaces.Add(f);
                Vector3 origin=Positions[face[0]],x=BevelUnit(Positions[face[1]]-origin),y=Vector3.Cross(bevelNormals[f],x);
                float scale=0;foreach(int v in face)scale=Mathf.Max(scale,Vector3.Distance(origin,Positions[v]));
                if(scale<=bevelEpsilon)throw new InvalidOperationException("A bevel face has no stable plane.");
                float normalization=scale/100f; // Upstream predicates are calibrated for contours around 0..100.
                var contour=new float2[n];var speeds=new float[n];
                // Original face winding is CCW in its own basis; upstream is CW.
                for(int j=0;j<n;j++)
                {
                    int c=n-1-j,vertex=welded[face[c]],previous=welded[face[(c+n-1)%n]];
                    var p=Positions[face[c]]-origin;
                    if(Math.Abs(Vector3.Dot(p,bevelNormals[f]))>Math.Max(bevelEpsilon*8,scale*1e-6))
                        throw new InvalidOperationException("Bevel requires planar incident polygons. Split the non-planar polygon before beveling its edge.");
                    contour[j]=new float2(Vector3.Dot(p,x)/normalization,Vector3.Dot(p,y)/normalization);
                    speeds[j]=targets.Contains(BevelKey(vertex,previous))?plan.EdgeSpeeds[BevelKey(vertex,previous)]/normalization:0;
                }
                var graph=new WavefrontGraph(new PolygonWithHoles(contour),new BevelEventListener(),false,speeds);
                graph.GenerateInitialEvents();
                var velocities=plan.Velocities[f]=new Vector3[n];
                for(int j=0;j<n;j++)
                {
                    var velocity=graph.vertexDatas[j].velocity;
                    velocities[n-1-j]=(x*velocity.x+y*velocity.y)*normalization;
                }
                for(int j=0;j<graph.nEdges;j++)
                {
                    var e=graph.edgeEvents[j];
                    if(e.eventType.IsValid()&&e.eventTime>1e-6f)plan.Limit=Mathf.Min(plan.Limit,e.eventTime);
                }
                for(int j=0;j<graph.nVertices;j++)
                {
                    var v=graph.vertexDatas[j];
                    if(v.partOfSplitEvent&&v.splitTime>1e-6f)plan.Limit=Mathf.Min(plan.Limit,v.splitTime);
                }
            }
            // Preserve the last nondegenerate interval. Continuing through an event
            // requires a 3D topology transaction, not merely welding close positions.
            if(!float.IsPositiveInfinity(plan.Limit))plan.Limit=Mathf.Max(0,plan.Limit-1e-5f);
            bevelWavefront=plan;return plan;
        }
    }
}
