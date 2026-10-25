using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioRetopologyRelaxBrush
    {
        internal static bool Apply(Part part,Dictionary<int,float> weights,float strength,bool protectBorders,StudioRetopologySurface surface,float offset)
        {
            if(strength<=0||weights.Count==0)return false;
            var edges=StudioRetopologyOperations.Edges(part);
            var neighbors=new Dictionary<int,List<int>>();var border=new HashSet<int>();
            foreach(var entry in edges)
            {
                int a=entry.Key.Item1,b=entry.Key.Item2;
                if(!neighbors.ContainsKey(a))neighbors[a]=new List<int>();
                if(!neighbors.ContainsKey(b))neighbors[b]=new List<int>();
                neighbors[a].Add(b);neighbors[b].Add(a);
                if(entry.Value.Count!=2){border.Add(a);border.Add(b);}
            }
            var original=part.vertices.ToArray();var proposed=(Vector3[])original.Clone();bool changed=false;
            foreach(var entry in weights)
            {
                int i=entry.Key;
                if(i<0||i>=original.Length||(protectBorders&&border.Contains(i))||!neighbors.TryGetValue(i,out var adjacent)||adjacent.Count==0)continue;
                var average=Vector3.zero;foreach(int j in adjacent)average+=original[j];average/=adjacent.Count;
                var point=Vector3.Lerp(original[i],average,Mathf.Clamp01(strength)*Mathf.Clamp01(entry.Value)*.45f);
                proposed[i]=surface==null?point:surface.Project(point,offset);
                changed|=(proposed[i]-original[i]).sqrMagnitude>1e-14f;
            }
            if(!changed)return false;
            // Reject a dab that folds existing triangles; keep the last valid shape.
            foreach(var face in part.faces)
            {
                var ids=face.vertices;
                for(int j=1;j+1<ids.Length;j++)
                {
                    var before=Vector3.Cross(original[ids[j]]-original[ids[0]],original[ids[j+1]]-original[ids[0]]);
                    var after=Vector3.Cross(proposed[ids[j]]-proposed[ids[0]],proposed[ids[j+1]]-proposed[ids[0]]);
                    if(before.sqrMagnitude>1e-16f&&(after.sqrMagnitude<before.sqrMagnitude*.0001f||Vector3.Dot(before,after)<=0))return false;
                }
            }
            part.vertices=new List<Vector3>(proposed);return true;
        }
    }
}
