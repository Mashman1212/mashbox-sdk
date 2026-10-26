using System;
using System.Linq;
using UnityEngine;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private void CreateDrawTriangle()
        {
            if(corners.Count!=3)return;
            var points=corners.ToArray();var normal=cornerNormal;
            Change("Draw triangle",()=>
            {
                AddDrawTriangle(points,normal);
                if(Document.symmetry&&!points.All(p=>Mathf.Abs(p.x)<.000001f))
                    AddDrawTriangle(points.Select(p=>new Vector3(-p.x,p.y,p.z)).ToArray(),new Vector3(-normal.x,normal.y,normal.z));
                Active.needsUV=true;
            });
            status="Triangle created. Snapped vertices are welded.";
        }
        private void AddDrawTriangle(Vector3[] points,Vector3 normal)
        {
            if(Vector3.Cross(points[1]-points[0],points[2]-points[0]).sqrMagnitude<1e-14f)
                throw new InvalidOperationException("Choose three distinct, non-collinear triangle corners.");
            float tolerance=Mathf.Max(.000001f,Active.reference!=null?Active.reference.bounds.size.magnitude*.00001f:.000001f);
            var ids=new int[3];
            for(int i=0;i<3;i++)
            {
                ids[i]=Active.vertices.FindIndex(p=>(p-points[i]).sqrMagnitude<=tolerance*tolerance);
                if(ids[i]<0){ids[i]=Active.vertices.Count;Active.vertices.Add(points[i]);Active.uv.Add(Vector2.zero);}
            }
            if(ids.Distinct().Count()!=3)throw new InvalidOperationException("Triangle corners snapped to the same vertex.");
            if(Active.faces.Any(f=>f.vertices.Length==3&&f.vertices.OrderBy(i=>i).SequenceEqual(ids.OrderBy(i=>i))))
                throw new InvalidOperationException("A triangle already exists here.");
            var edges=StudioRetopologyOperations.Edges(Active);bool? reverse=null;
            for(int i=0;i<3;i++)
            {
                int a=ids[i],b=ids[(i+1)%3];
                if(!edges.TryGetValue(StudioRetopologyOperations.Edge(a,b),out var neighbors))continue;
                if(neighbors.Count!=1)throw new InvalidOperationException("Draw from open edges; this edge already has two faces.");
                var face=Active.faces[neighbors[0]].vertices;
                bool forward=Enumerable.Range(0,face.Length).Any(j=>face[j]==a&&face[(j+1)%face.Length]==b);
                if(reverse.HasValue&&reverse.Value!=forward)throw new InvalidOperationException("These corners would produce inconsistent face winding.");
                reverse=forward;
            }
            if(reverse??Vector3.Dot(Vector3.Cross(points[1]-points[0],points[2]-points[0]),normal)<0)Array.Reverse(ids);
            Active.faces.Add(new Face(ids));
        }
    }
}
