using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioRetopologyQuadCut
    {
        internal static float Tolerance(Part p)=>Mathf.Max(1e-7f,(p.reference?p.reference.bounds.size.magnitude:1f)*1e-6f);
        private static bool OnEdge(Part p,int a,int b,Vector3 point,out float t)
        {
            var d=p.vertices[b]-p.vertices[a];t=d.sqrMagnitude>0?Vector3.Dot(point-p.vertices[a],d)/d.sqrMagnitude:0;
            return t>=0&&t<=1&&(Vector3.Lerp(p.vertices[a],p.vertices[b],t)-point).sqrMagnitude<=Mathf.Pow(Tolerance(p),2);
        }
        internal static int FindFace(Part p,Vector3 start,Vector3 end)
        {
            float epsilon=Tolerance(p);
            if((start-end).sqrMagnitude<=epsilon*epsilon)return -1;
            for(int f=0;f<p.faces.Count;f++)
            {
                var ids=p.faces[f].vertices;bool a=false,b=false,same=false;
                for(int i=0;i<ids.Length;i++)
                {
                    bool x=OnEdge(p,ids[i],ids[(i+1)%ids.Length],start,out _);
                    bool y=OnEdge(p,ids[i],ids[(i+1)%ids.Length],end,out _);
                    a|=x;b|=y;same|=x&&y;
                }
                if(!a||!b||same)continue;
                // Only split through the interior of a convex face. Never cut
                // across a concave outline's exterior or bridge unrelated faces.
                var n=Vector3.zero;var origin=p.vertices[ids[0]];
                for(int i=1;i+1<ids.Length;i++)n+=Vector3.Cross(p.vertices[ids[i]]-origin,p.vertices[ids[i+1]]-origin);
                if(n.sqrMagnitude<1e-16f)continue;n.Normalize();bool inside=true;
                var middle=(start+end)*.5f;
                for(int i=0;i<ids.Length;i++)
                {
                    var v=p.vertices[ids[i]];var edge=p.vertices[ids[(i+1)%ids.Length]]-v;
                    if(Vector3.Dot(Vector3.Cross(edge,middle-v),n)<=epsilon*edge.magnitude){inside=false;break;}
                }
                if(inside)return f;
            }
            return -1;
        }
        private static int Insert(Part p,Vector3 point,int target)
        {
            var boundary=p.faces[target].vertices;
            float epsilon=Tolerance(p);
            foreach(int id in boundary)if((p.vertices[id]-point).sqrMagnitude<=epsilon*epsilon)return id;
            int a=-1,b=-1;float t=0;
            for(int i=0;i<boundary.Length;i++)if(OnEdge(p,boundary[i],boundary[(i+1)%boundary.Length],point,out t)){a=boundary[i];b=boundary[(i+1)%boundary.Length];break;}
            if(a<0)throw new InvalidOperationException("Cut endpoint is no longer on the face boundary.");
            int added=p.vertices.Count;bool uv=p.uv.Count==added;
            p.vertices.Add(Vector3.Lerp(p.vertices[a],p.vertices[b],t));
            if(uv)p.uv.Add(Vector2.Lerp(p.uv[a],p.uv[b],t));
            // Every incident face receives the same vertex, with its own UV
            // interpolation so existing UV seams and material boundaries survive.
            foreach(var face in p.faces)
            {
                var ids=face.vertices;var next=new List<int>();var values=new List<Vector2>();
                for(int i=0;i<ids.Length;i++)
                {
                    int x=ids[i],y=ids[(i+1)%ids.Length];next.Add(x);
                    values.Add(StudioRetopologyDocument.FaceUV(p,face,i));
                    if((x==a&&y==b)||(x==b&&y==a))
                    {next.Add(added);values.Add(Vector2.Lerp(StudioRetopologyDocument.FaceUV(p,face,i),StudioRetopologyDocument.FaceUV(p,face,(i+1)%ids.Length),x==a?t:1-t));}
                }
                if(next.Count!=ids.Length){face.vertices=next.ToArray();face.cornerUV=values.ToArray();}
            }
            return added;
        }
        internal static void InsertBoundaryPoints(Part p,Vector3[] points)
        {
            foreach(var point in points)
            {
                int target=-1;
                for(int f=0;f<p.faces.Count&&target<0;f++)
                {var ids=p.faces[f].vertices;for(int i=0;i<ids.Length;i++)if(OnEdge(p,ids[i],ids[(i+1)%ids.Length],point,out _)){target=f;break;}}
                if(target>=0)Insert(p,point,target);
            }
        }
        internal static void Apply(Part p,Vector3 start,Vector3 end)
        {
            int f=FindFace(p,start,end);if(f<0)throw new InvalidOperationException("Choose two distinct boundaries across one face.");
            int a=Insert(p,start,f),b=Insert(p,end,f);var face=p.faces[f];var ids=face.vertices;
            int ia=Array.IndexOf(ids,a),ib=Array.IndexOf(ids,b);
            Face Walk(int first,int last)
            {
                var v=new List<int>();var uv=new List<Vector2>();
                for(int i=first;;i=(i+1)%ids.Length){v.Add(ids[i]);uv.Add(StudioRetopologyDocument.FaceUV(p,face,i));if(i==last)break;}
                return new Face(v.ToArray(),face.material){cornerUV=uv.ToArray()};
            }
            p.faces[f]=Walk(ia,ib);p.faces.Add(Walk(ib,ia));
        }
    }
}
