using System;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // A spatial tree of reference triangles, shared by projection and relaxation.
    internal sealed class StudioRetopologySurface
    {
        private struct Triangle { public Vector3 a,b,c; public Bounds bounds; }
        private sealed class Node { public Bounds bounds; public int start,count; public Node left,right; }
        private readonly Triangle[] triangles;
        private readonly Node root;
        internal StudioRetopologySurface(Mesh mesh)
        {
            var points=mesh.vertices; var indices=mesh.triangles;
            triangles=new Triangle[indices.Length/3];
            for(int i=0;i<triangles.Length;i++)
            {
                var t=new Triangle { a=points[indices[i*3]],b=points[indices[i*3+1]],c=points[indices[i*3+2]] };
                t.bounds=new Bounds(t.a,Vector3.zero); t.bounds.Encapsulate(t.b); t.bounds.Encapsulate(t.c); triangles[i]=t;
            }
            if(triangles.Length==0) throw new InvalidOperationException("The reference has no triangles.");
            root=Build(0,triangles.Length);
        }
        private Node Build(int start,int count)
        {
            var node=new Node { start=start,count=count,bounds=triangles[start].bounds };
            for(int i=start+1;i<start+count;i++) node.bounds.Encapsulate(triangles[i].bounds);
            if(count<=12) return node;
            Vector3 size=node.bounds.size; int axis=size.x>size.y?(size.x>size.z?0:2):(size.y>size.z?1:2);
            Array.Sort(triangles,start,count,System.Collections.Generic.Comparer<Triangle>.Create((a,b)=>a.bounds.center[axis].CompareTo(b.bounds.center[axis])));
            int half=count/2; node.left=Build(start,half); node.right=Build(start+half,count-half); return node;
        }
        internal Vector3 Project(Vector3 point,float offset)
        {
            float best=float.PositiveInfinity; Vector3 result=point,normal=Vector3.up;
            Search(root,point,ref best,ref result,ref normal); return result+normal*offset;
        }
        // Project a grid along one shared axis rather than pulling each point
        // sideways toward an independently chosen nearest triangle.
        internal Vector3 ProjectAlong(Vector3 point,Vector3 direction,float offset,float localSpan=0)
        {
            if(direction.sqrMagnitude<1e-12f)return Project(point,offset);
            direction.Normalize();float best=float.PositiveInfinity;var result=point;var normal=direction;
            if(localSpan>0)
            {
                // A grazing ray can miss the nearby fold and hit the far side of
                // the garment. Keep the nearest surface as the fallback and only
                // accept directional hits within a local, cell-sized allowance.
                float nearest=float.PositiveInfinity;
                Search(root,point,ref nearest,ref result,ref normal);
                best=Mathf.Sqrt(nearest)*1.5f+Mathf.Max(.000001f,localSpan)*.5f;
            }
            Cast(root,new Ray(point,direction),ref best,ref result,ref normal);
            Cast(root,new Ray(point,-direction),ref best,ref result,ref normal);
            return float.IsInfinity(best)?Project(point,offset):result+normal*offset;
        }
        private void Cast(Node node,Ray ray,ref float best,ref Vector3 result,ref Vector3 normal)
        {
            var bounds=node.bounds;bounds.Expand(.000001f);
            if(!bounds.IntersectRay(ray,out float entry)||entry>best)return;
            if(node.left!=null){Cast(node.left,ray,ref best,ref result,ref normal);Cast(node.right,ray,ref best,ref result,ref normal);return;}
            for(int i=node.start;i<node.start+node.count;i++)
            {
                var tri=triangles[i];var e1=tri.b-tri.a;var e2=tri.c-tri.a;
                var p=Vector3.Cross(ray.direction,e2);float det=Vector3.Dot(e1,p);
                if(Mathf.Abs(det)<1e-12f)continue;
                var t=ray.origin-tri.a;float u=Vector3.Dot(t,p)/det;if(u<-.000001f||u>1.000001f)continue;
                var q=Vector3.Cross(t,e1);float v=Vector3.Dot(ray.direction,q)/det;if(v<-.000001f||u+v>1.000001f)continue;
                float d=Vector3.Dot(e2,q)/det;if(d<-.000001f||d>=best)continue;
                best=Mathf.Max(0,d);result=ray.GetPoint(best);normal=Vector3.Cross(e1,e2).normalized;
            }
        }
        private void Search(Node node,Vector3 point,ref float best,ref Vector3 result,ref Vector3 normal)
        {
            if(node.bounds.SqrDistance(point)>best) return;
            if(node.left==null)
            {
                for(int i=node.start;i<node.start+node.count;i++)
                {
                    var t=triangles[i]; var candidate=Closest(point,t.a,t.b,t.c); float d=(candidate-point).sqrMagnitude;
                    if(d>=best) continue; best=d; result=candidate; normal=Vector3.Cross(t.b-t.a,t.c-t.a).normalized;
                }
                return;
            }
            var first=node.left; var second=node.right;
            if(first.bounds.SqrDistance(point)>second.bounds.SqrDistance(point)) { first=node.right; second=node.left; }
            Search(first,point,ref best,ref result,ref normal); Search(second,point,ref best,ref result,ref normal);
        }
        internal static Vector3 Closest(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
        {
            var ab=b-a; var ac=c-a; var ap=p-a;
            float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
            if(d1<=0&&d2<=0)return a;
            var bp=p-b; float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);
            if(d3>=0&&d4<=d3)return b;
            float vc=d1*d4-d3*d2;
            if(vc<=0&&d1>=0&&d3<=0)return a+ab*(d1/(d1-d3));
            var cp=p-c; float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);
            if(d6>=0&&d5<=d6)return c;
            float vb=d5*d2-d1*d6;
            if(vb<=0&&d2>=0&&d6<=0)return a+ac*(d2/(d2-d6));
            float va=d3*d6-d5*d4;
            if(va<=0&&d4-d3>=0&&d5-d6>=0)return b+(c-b)*((d4-d3)/((d4-d3)+(d5-d6)));
            float sum=va+vb+vc;
            if(Mathf.Abs(sum)<1e-20f)return a;
            return a+ab*(vb/sum)+ac*(vc/sum);
        }
    }
}
