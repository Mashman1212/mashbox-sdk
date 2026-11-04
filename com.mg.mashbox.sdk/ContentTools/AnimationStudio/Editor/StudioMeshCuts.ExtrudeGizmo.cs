using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        // Match Extrude's normalized, per-fan source normals, including authored
        // geometric identities at render seams and independent-face mode.
        internal Vector3 ExtrusionHandleDirection(int[] faces, (int a,int b)[] edges, bool together, bool joinSeams)
        {
            int Canonical(int v) => joinSeams ? welded[v] : v;
            var selected = faces != null ? new HashSet<int>(faces) : new HashSet<int>();
            var edgeVertices = new HashSet<int>();
            if (faces == null && edges != null)
            {
                var keys = new HashSet<(int,int)>();
                foreach (var e in edges) { int a=Canonical(e.a),b=Canonical(e.b);keys.Add(FlowKey(a,b));edgeVertices.Add(a);edgeVertices.Add(b); }
                for(int f=0;f<Faces.Count;f++)
                {var v=Faces[f].Vertices;for(int i=0;i<v.Count;i++)if(keys.Contains(FlowKey(Canonical(v[i]),Canonical(v[(i+1)%v.Count])))){selected.Add(f);break;}}
            }
            var sums = new Dictionary<(int,int),Vector3>();
            foreach(int f in selected.OrderBy(i=>i))
            {
                var v=Faces[f].Vertices;var normal=Vector3.zero;
                for(int i=0;i<v.Count;i++)normal+=Vector3.Cross(Positions[v[i]]-Positions[v[0]],Positions[v[(i+1)%v.Count]]-Positions[v[0]]);
                normal.Normalize();
                foreach(int g in v.Select(Canonical).Distinct())
                {if(faces==null&&!edgeVertices.Contains(g))continue;var key=(faces!=null&&!together?f+1:0,g);sums.TryGetValue(key,out var sum);sums[key]=sum+normal;}
            }
            if(sums.Count==0)return Vector3.zero;
            var directions=sums.Values.Select(v=>v.normalized).ToArray();var result=directions.Aggregate(Vector3.zero,(sum,v)=>sum+v)/directions.Length;
            // Symmetric/opposing regions still need a usable distance control.
            return result.sqrMagnitude>1e-12f?result:directions.FirstOrDefault(v=>v.sqrMagnitude>.5f);
        }
    }
}
