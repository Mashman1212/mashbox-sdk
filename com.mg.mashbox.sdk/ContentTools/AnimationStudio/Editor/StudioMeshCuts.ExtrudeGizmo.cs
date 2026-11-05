using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        // Match Extrude's per-vertex travel directions. Edge distance extends
        // the boundary in the source surface rather than along its face normal.
        internal Vector3 ExtrusionHandleDirection(int[] faces, (int a,int b)[] edges, bool together, bool joinSeams)
        {
            int Canonical(int v) => joinSeams ? welded[v] : v;
            var selected = faces != null ? new HashSet<int>(faces) : new HashSet<int>();
            var edgeVertices = new HashSet<int>();
            var edgeKeys = new HashSet<(int,int)>();
            if (faces == null && edges != null)
            {
                foreach (var e in edges) { int a=Canonical(e.a),b=Canonical(e.b);edgeKeys.Add(FlowKey(a,b));edgeVertices.Add(a);edgeVertices.Add(b); }
                for(int f=0;f<Faces.Count;f++)
                {var v=Faces[f].Vertices;for(int i=0;i<v.Count;i++)if(edgeKeys.Contains(FlowKey(Canonical(v[i]),Canonical(v[(i+1)%v.Count])))){selected.Add(f);break;}}
            }
            var sums = new Dictionary<(int,int),Vector3>();
            var normals = new Vector3[Faces.Count];
            var boundary = new List<(int face,int a,int b)>();
            foreach(int f in selected.OrderBy(i=>i))
            {
                var v=Faces[f].Vertices;var normal=Vector3.zero;
                for(int i=0;i<v.Count;i++)normal+=Vector3.Cross(Positions[v[i]]-Positions[v[0]],Positions[v[(i+1)%v.Count]]-Positions[v[0]]);
                normal.Normalize();normals[f]=normal;
                if(faces==null)for(int i=0;i<v.Count;i++)if(edgeKeys.Contains(FlowKey(Canonical(v[i]),Canonical(v[(i+1)%v.Count]))))boundary.Add((f,v[i],v[(i+1)%v.Count]));
                foreach(int g in v.Select(Canonical).Distinct())
                {if(faces==null&&!edgeVertices.Contains(g))continue;var key=(faces!=null&&!together?f+1:0,g);sums.TryGetValue(key,out var sum);sums[key]=sum+normal;}
            }
            if(sums.Count==0)return Vector3.zero;
            var directions=faces==null
                ? PrepareExtrusionInset(boundary,normals,_=>0,Canonical,false).Directions.Values.ToArray()
                : sums.Values.Select(v=>v.normalized).ToArray();
            var result=directions.Aggregate(Vector3.zero,(sum,v)=>sum+v)/directions.Length;
            // Symmetric/opposing regions still need a usable distance control.
            return result.sqrMagnitude>1e-12f?result:directions.FirstOrDefault(v=>v.sqrMagnitude>.5f);
        }
    }
}
