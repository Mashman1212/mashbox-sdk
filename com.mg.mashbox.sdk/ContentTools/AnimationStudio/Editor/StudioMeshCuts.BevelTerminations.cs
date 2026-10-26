using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        // A flat strip ending in a manifold vertex fan has one supporting plane.
        // Intersect the fan's stationary edges with that plane; an arbitrary
        // distance along those edges creates a separate tilted corner patch.
        private void PrepareFlatBevelTerminations(BevelWavefrontPlan plan)
        {
            if (plan.FlatTerminalCuts != null) return;
            plan.FlatTerminalCuts = new Dictionary<(int,int),Vector3>();
            plan.FlatTerminalOwners = new HashSet<int>();
            var pending = new Dictionary<int,Vector3>();
            foreach (int owner in plan.Distances.Keys)
            {
                BevelEdge selected = null; int degree = 0; bool closed = true;
                foreach (var edge in bevelIncident[owner])
                {
                    closed &= edge.Uses.Count == 2;
                    if (plan.Targets.Contains(BevelKey(edge.A,edge.B))) { selected = edge; degree++; }
                }
                if (!closed || degree != 1) continue;
                int f = selected.Uses[0].face, g = selected.Uses[1].face;
                Vector3 Velocity(int face, int corner)
                {
                    var polygon = Faces[face].Vertices;
                    if (welded[polygon[corner]] != owner) corner = (corner+1)%polygon.Count;
                    return plan.Velocities[face][corner];
                }
                var a = Velocity(f,selected.Uses[0].corner); var b = Velocity(g,selected.Uses[1].corner);
                var axis = plan.Originals[selected.A == owner ? selected.B : selected.A]-plan.Originals[owner];
                var normal = BevelUnit(Vector3.Cross(axis,b-a));
                if (normal.sqrMagnitude < .5f) continue;
                double offset = Vector3.Dot(normal,a); pending.Clear(); bool valid = true;
                foreach (var edge in bevelIncident[owner])
                {
                    if (edge == selected) continue;
                    bool boundedByMovingFace = false;
                    foreach (var use in edge.Uses) boundedByMovingFace |= use.face == f || use.face == g;
                    if (boundedByMovingFace) continue; // Existing wavefront intersections own these cuts.
                    int other = edge.A == owner ? edge.B : edge.A;
                    var direction = plan.Originals[other]-plan.Originals[owner];
                    double denominator = Vector3.Dot(normal,direction);
                    double rate = Math.Abs(denominator) > 1e-12 ? offset/denominator : double.NaN;
                    if (double.IsNaN(rate) || double.IsInfinity(rate) || rate <= 0) { valid = false; break; }
                    pending.Add(other,direction*(float)rate);
                }
                // A stationary fan that does not meet the forward strip plane needs
                // the existing general corner construction, rather than a forced cut.
                if (!valid || pending.Count == 0) continue;
                plan.FlatTerminalOwners.Add(owner);
                foreach (var cut in pending) plan.FlatTerminalCuts.Add((owner,cut.Key),cut.Value);
            }
            // Include approaching fan cuts in the first-event bound. The opposite
            // endpoint may be another terminal or an existing moving face corner.
            var rates = new Dictionary<(int,int),float>();
            void Rate(int owner, int other, Vector3 velocity)
            {
                var span = plan.Originals[other]-plan.Originals[owner];
                float value = Vector3.Dot(velocity,span)/span.sqrMagnitude;
                var key = (owner,other);
                if (!rates.TryGetValue(key,out var previous) || value > previous) rates[key] = Mathf.Max(0,value);
            }
            foreach (int face in plan.SelectedFaces)
            {
                var polygon = Faces[face].Vertices;
                for (int c = 0; c < polygon.Count; c++)
                {
                    int owner = welded[polygon[c]], previous = welded[polygon[(c+polygon.Count-1)%polygon.Count]], next = welded[polygon[(c+1)%polygon.Count]];
                    bool incoming = plan.Targets.Contains(BevelKey(previous,owner)), outgoing = plan.Targets.Contains(BevelKey(owner,next));
                    if (outgoing && !incoming) Rate(owner,previous,plan.Velocities[face][c]);
                    if (incoming && !outgoing) Rate(owner,next,plan.Velocities[face][c]);
                }
            }
            foreach (var cut in plan.FlatTerminalCuts) Rate(cut.Key.Item1,cut.Key.Item2,cut.Value);
            foreach (var cut in plan.FlatTerminalCuts)
            {
                int owner = cut.Key.Item1, other = cut.Key.Item2;
                float opposite = rates.TryGetValue((other,owner),out var known) ? known :
                    plan.Distances.TryGetValue(other,out var distance) ? distance/Vector3.Distance(plan.Originals[owner],plan.Originals[other]) : 0;
                float sum = rates[(owner,other)]+opposite;
                if (sum > 0) plan.FlatTerminalLimit = Mathf.Min(plan.FlatTerminalLimit,Mathf.Max(0,1/sum-1e-5f));
            }
        }
    }
}
