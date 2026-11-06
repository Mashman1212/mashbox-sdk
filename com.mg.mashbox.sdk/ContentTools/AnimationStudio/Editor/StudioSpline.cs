using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public enum StudioSplineKind { Linear, Bezier }
    public enum StudioSplineTangent { Corner, Aligned, Mirrored }
    [Serializable] public sealed class StudioSpline
    {
        [Serializable] public sealed class Knot
        {
            public Vector3 position, incoming, outgoing; public bool automatic;
            public StudioSplineTangent tangent = StudioSplineTangent.Corner;
            public Knot Copy() => (Knot)MemberwiseClone();
        }
        public StudioSplineKind kind;
        public bool closed;
        public List<Knot> knots = new List<Knot>();
        public int SegmentCount => closed ? knots.Count : Math.Max(0, knots.Count - 1);
        public StudioSpline Copy() => new StudioSpline { kind = kind, closed = closed, knots = knots.Select(k => k.Copy()).ToList() };
        internal static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
        public void Validate()
        {
            if (!Enum.IsDefined(typeof(StudioSplineKind), kind) || knots == null || knots.Count < (closed ? 3 : 2) ||
                knots.Any(k => k == null || !Finite(k.position) || !Finite(k.incoming) || !Finite(k.outgoing) || !Enum.IsDefined(typeof(StudioSplineTangent), k.tangent)))
                throw new InvalidOperationException("A spline needs finite knots and handles, with at least two knots (three for a closed loop).");
        }
        public Vector3 Evaluate(int segment, float t)
        {
            var a = knots[segment]; var b = knots[(segment + 1) % knots.Count];
            if (kind == StudioSplineKind.Linear) return Vector3.LerpUnclamped(a.position, b.position, t);
            float u = 1 - t;
            return u*u*u*a.position + 3*u*u*t*(a.position+a.outgoing) + 3*u*t*t*(b.position+b.incoming) + t*t*t*b.position;
        }
        public int Insert(int segment, float t)
        {
            if (segment < 0 || segment >= SegmentCount || !float.IsFinite(t) || t <= 0 || t >= 1) throw new InvalidOperationException("Choose the interior of a spline segment.");
            var a = knots[segment]; var b = knots[(segment + 1) % knots.Count]; var k = new Knot { position = Evaluate(segment, t) };
            if (kind == StudioSplineKind.Bezier)
            {
                var ab = Vector3.Lerp(a.position, a.position+a.outgoing, t);
                var bc = Vector3.Lerp(a.position+a.outgoing, b.position+b.incoming, t);
                var cd = Vector3.Lerp(b.position+b.incoming, b.position, t);
                var abc = Vector3.Lerp(ab, bc, t); var bcd = Vector3.Lerp(bc, cd, t);
                a.outgoing = ab-a.position; b.incoming = cd-b.position; a.automatic = b.automatic = false;
                // Splitting changes only the adjoining handles. Preserve the opposite segments.
                if (a.tangent == StudioSplineTangent.Mirrored) a.tangent = StudioSplineTangent.Aligned;
                if (b.tangent == StudioSplineTangent.Mirrored) b.tangent = StudioSplineTangent.Aligned;
                k.incoming = abc-k.position; k.outgoing = bcd-k.position; k.tangent = StudioSplineTangent.Aligned;
            }
            knots.Insert(segment+1, k); return segment+1;
        }
        public void Remove(IEnumerable<int> indices)
        {
            var ids = indices.Distinct().OrderByDescending(i=>i).ToArray();
            if (ids.Length == 0 || ids.Any(i=>i<0 || i>=knots.Count)) throw new InvalidOperationException("Select knots to remove.");
            if (knots.Count-ids.Length < (closed ? 3 : 2)) throw new InvalidOperationException("Retain two knots, or three in a closed loop. Use Object mode to delete the whole spline.");
            foreach (int i in ids) knots.RemoveAt(i);
        }
        public void Reverse()
        {
            knots.Reverse(); foreach (var k in knots) { var old=k.incoming; k.incoming=k.outgoing; k.outgoing=old; }
        }
        public void SetHandle(int index, bool incoming, Vector3 offset)
        {
            if (!Finite(offset)) throw new InvalidOperationException("Spline handles must be finite.");
            var k=knots[index]; k.automatic=false; var opposite=incoming?k.outgoing:k.incoming;
            if (k.tangent == StudioSplineTangent.Mirrored) opposite=-offset;
            else if (k.tangent == StudioSplineTangent.Aligned && offset.sqrMagnitude>1e-20f) opposite=-offset.normalized*opposite.magnitude;
            if(incoming){k.incoming=offset;k.outgoing=opposite;}else{k.outgoing=offset;k.incoming=opposite;}
        }
        // Automatic knots derive cubic tangents from their neighbors. Manual handles
        // and shape-exact inserted spans are never overwritten by this refresh.
        public void RefreshAutomaticTangents()
        {
            if(kind!=StudioSplineKind.Bezier || knots.Count<2)return;
            for(int i=0;i<knots.Count;i++)
            {
                var k=knots[i];if(!k.automatic)continue;
                bool previous=i>0||closed,next=i+1<knots.Count||closed;
                var before=knots[(i+knots.Count-1)%knots.Count].position;
                var after=knots[(i+1)%knots.Count].position;
                var tangent=previous&&next?(after-before)/6:next?(after-k.position)/3:(k.position-before)/3;
                k.incoming=previous?-tangent:Vector3.zero;k.outgoing=next?tangent:Vector3.zero;k.tangent=StudioSplineTangent.Aligned;
            }
        }
        public void SetKind(StudioSplineKind value)
        {
            if (kind==value) return;
            kind=value;
            // Converting to Bezier creates smooth automatic tangents.
            if(value==StudioSplineKind.Bezier) for(int i=0;i<knots.Count;i++)
            {
                knots[i].incoming=i>0||closed?(knots[(i+knots.Count-1)%knots.Count].position-knots[i].position)/3:Vector3.zero;
                knots[i].outgoing=i+1<knots.Count||closed?(knots[(i+1)%knots.Count].position-knots[i].position)/3:Vector3.zero;
                knots[i].tangent=StudioSplineTangent.Aligned;knots[i].automatic=true;
            }
            RefreshAutomaticTangents();
        }
    }
}
