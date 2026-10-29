using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        internal sealed class EdgeFlowPlan
        {
            private readonly Vector3[] source;
            private readonly int[] canonical;
            private readonly Dictionary<int,Vector3> displacement = new Dictionary<int,Vector3>();
            private readonly List<int[]> affectedTriangles = new List<int[]>();
            internal EdgeFlowPlan(StudioMeshCuts topology, Mesh mesh, IEnumerable<(int a,int b)> selection, bool joinSeams, HashSet<Vector3> fixedPositions)
            {
                source=(Vector3[])topology.Positions.Clone();canonical=new int[source.Length];
                var positions=new Dictionary<int,Vector3>();
                for(int i=0;i<source.Length;i++){canonical[i]=joinSeams?topology.welded[i]:i;positions[canonical[i]]=source[i];}
                var all=new Dictionary<int,HashSet<int>>();var selected=new Dictionary<int,HashSet<int>>();
                var uses=new Dictionary<(int,int),List<int>>();
                var faces=topology.Faces.Select(f=>f.Vertices.Select(i=>canonical[i]).ToArray()).ToArray();
                void Link(Dictionary<int,HashSet<int>> graph,int a,int b)
                {if(!graph.TryGetValue(a,out var adjacent))graph[a]=adjacent=new HashSet<int>();adjacent.Add(b);}
                for(int f=0;f<faces.Length;f++)for(int e=0;e<faces[f].Length;e++)
                {
                    int a=faces[f][e],b=faces[f][(e+1)%faces[f].Length];if(a==b)continue;
                    var key=FlowKey(a,b);if(!uses.TryGetValue(key,out var list))uses[key]=list=new List<int>();list.Add(f);
                    Link(all,a,b);Link(all,b,a);
                }
                foreach(var edge in selection)
                {
                    if(edge.a<0||edge.b<0||edge.a>=source.Length||edge.b>=source.Length)throw new ArgumentException("An Edge Flow edge is outside the mesh.");
                    int a=canonical[edge.a],b=canonical[edge.b];
                    if(a==b||!uses.ContainsKey(FlowKey(a,b)))throw new ArgumentException("Edge Flow requires authored polygon edges.");
                    Link(selected,a,b);Link(selected,b,a);
                }
                // Fit transverse face fans, never average along the selected edge.
                // The latter shortens straight edges and pinches their boundary sides.
                foreach(int at in selected.Keys.OrderBy(i=>i))
                {
                    var along=selected[at];
                    if(along.Count>2 || (along.Count==1&&selected[along.First()].Count!=1) ||
                       (fixedPositions!=null&&fixedPositions.Contains(positions[at])))continue;
                    Vector3 axis=along.Count==1?positions[along.First()]-positions[at]:positions[along.Min()]-positions[along.Max()];
                    if(axis.sqrMagnitude<1e-20f)continue;axis.Normalize();
                    Vector3 Project(Vector3 delta)=>delta-axis*Vector3.Dot(delta,axis);
                    var pairs=new HashSet<(int,int)>();Vector3 sum=Vector3.zero;int count=0;
                    foreach(int other in along.OrderBy(i=>i))
                    {
                        var adjacent=uses[FlowKey(at,other)];if(adjacent.Count!=2)continue;
                        int Cross(int face)
                        {
                            var ids=faces[face];int i=Array.IndexOf(ids,at);
                            int prev=ids[(i+ids.Length-1)%ids.Length],next=ids[(i+1)%ids.Length];return prev==other?next:prev;
                        }
                        int left=Cross(adjacent[0]),right=Cross(adjacent[1]);
                        if(left==right||along.Contains(left)||along.Contains(right))continue;
                        var pair=FlowKey(left,right);if(!pairs.Add(pair))continue;left=pair.Item1;right=pair.Item2;
                        Vector3 center=positions[at],a=positions[left],b=positions[right];
                        float la=Project(center-a).magnitude,lb=Project(b-center).magnitude;
                        if(la<=1e-10f||lb<=1e-10f)continue;
                        Vector3? Continue(int near,int oppositeSide)
                        {
                            var candidates=all[near].Where(i=>i!=at&&i!=oppositeSide&&!selected.ContainsKey(i)).OrderBy(i=>i).ToArray();
                            if(candidates.Length==0)return null;
                            var incoming=uses[FlowKey(near,at)];
                            var opposite=candidates.Where(i=>!uses[FlowKey(near,i)].Any(incoming.Contains)).ToArray();
                            if(opposite.Length>0)candidates=opposite;
                            Vector3 direction=Project(positions[near]-center).normalized;
                            float best=candidates.Max(i=>Vector3.Dot(Project(positions[i]-positions[near]).normalized,direction));
                            if(best< -1e-5f)return null;
                            var tied=candidates.Where(i=>Vector3.Dot(Project(positions[i]-positions[near]).normalized,direction)>=best-1e-5f).ToArray();
                            Vector3 average=Vector3.zero;foreach(int i in tied)average+=positions[i];return average/tied.Length;
                        }
                        var before=Continue(left,right);var after=Continue(right,left);
                        // Cubic Hermite interpolation from FIXED surrounding tangents.
                        // A planar surrounding profile therefore gives a planar target,
                        // while a turning profile can push outward as well as inward.
                        Vector3 chord=Project(b-a);float span=chord.magnitude;
                        Vector3 m0=before.HasValue?Project(a-before.Value).normalized:chord.normalized;
                        Vector3 m1=after.HasValue?Project(after.Value-b).normalized:chord.normalized;
                        if(m0.sqrMagnitude<1e-10f)m0=chord.normalized;if(m1.sqrMagnitude<1e-10f)m1=chord.normalized;
                        float t=la/(la+lb);
                        Vector3 target=StudioEdgeFlow.Hermite(a,b,span*m0,span*m1,t);
                        sum+=Project(target-center);count++;
                    }
                    if(count>0)displacement[at]=sum/count;
                }
                foreach(var list in topology.BevelSourceTriangles(mesh))for(int i=0;i+2<list.Count;i+=3)
                {
                    int a=list[i],b=list[i+1],c=list[i+2];
                    if(displacement.ContainsKey(canonical[a])||displacement.ContainsKey(canonical[b])||displacement.ContainsKey(canonical[c]))affectedTriangles.Add(new[]{a,b,c});
                }
            }
            internal Vector3[] Evaluate(float influence,int iterations)
            {
                if(float.IsNaN(influence)||float.IsInfinity(influence))throw new ArgumentOutOfRangeException(nameof(influence));
                if(iterations<0)throw new ArgumentOutOfRangeException(nameof(iterations));
                var result=(Vector3[])source.Clone();
                float signed=Mathf.Clamp(influence,-1,1);
                float amount=Mathf.Sign(signed)*(1-(float)Math.Pow(1-Mathf.Abs(signed),iterations));
                if(amount==0)return result;
                for(int i=0;i<result.Length;i++)if(displacement.TryGetValue(canonical[i],out var delta))result[i]+=delta*amount;
                foreach(var point in result)if(!StudioEdgeFlow.Finite(point))throw new InvalidOperationException("Edge Flow produced invalid coordinates.");
                foreach(var triangle in affectedTriangles)
                {
                    int a=triangle[0],b=triangle[1],c=triangle[2];
                    var before=Vector3.Cross(source[b]-source[a],source[c]-source[a]);var after=Vector3.Cross(result[b]-result[a],result[c]-result[a]);
                    if(before.sqrMagnitude>1e-20f&&(after.sqrMagnitude<=before.sqrMagnitude*1e-12f||Vector3.Dot(before,after)<=0))
                        throw new InvalidOperationException("Edge Flow would collapse or invert a face. Reduce Influence or Iterations.");
                }
                return result;
            }
        }
        private static (int,int) FlowKey(int a,int b)=>a<b?(a,b):(b,a);
        internal EdgeFlowPlan CreateEdgeFlowPlan(Mesh mesh,IEnumerable<(int a,int b)> selection,bool joinSeams=true,HashSet<Vector3> fixedPositions=null)
            =>new EdgeFlowPlan(this,mesh,selection,joinSeams,fixedPositions);
    }
}
