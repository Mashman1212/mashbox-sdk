using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private float fitBodyGap = .008f, fitBodyReach = .15f;
        private FitBodySurface fitBodySurface;

        private bool PrepareFitBody()
        {
            var triangles = new List<FitBodyTriangle>();
            for (int i = 0; i < Document.parts.Count; i++)
            {
                var part = Document.parts[i];
                if (i == Document.selected || part.role != StudioSkinningDocument.MeshRole.Body || !part.mesh) continue;
                var baked = new Mesh();
                try
                {
                    var skin = objects[i].GetComponent<SkinnedMeshRenderer>();
                    Mesh mesh = part.mesh;
                    if (skin) { skin.BakeMesh(baked); mesh = baked; }
                    var matrix = objects[i].transform.localToWorldMatrix;
                    var normalMatrix = matrix.inverse.transpose;
                    var points = mesh.vertices; var ns = mesh.normals; var indices = mesh.triangles;
                    for (int k = 0; k < indices.Length; k += 3)
                    {
                        int a = indices[k], b = indices[k+1], c = indices[k+2];
                        var t = new FitBodyTriangle {
                            a = matrix.MultiplyPoint3x4(points[a]), b = matrix.MultiplyPoint3x4(points[b]), c = matrix.MultiplyPoint3x4(points[c])
                        };
                        var face = Vector3.Cross(t.b-t.a,t.c-t.a);
                        if (face.sqrMagnitude < 1e-16f) continue;
                        t.na = ns.Length == points.Length ? normalMatrix.MultiplyVector(ns[a]).normalized : face.normalized;
                        t.nb = ns.Length == points.Length ? normalMatrix.MultiplyVector(ns[b]).normalized : face.normalized;
                        t.nc = ns.Length == points.Length ? normalMatrix.MultiplyVector(ns[c]).normalized : face.normalized;
                        t.bounds = new Bounds(t.a,Vector3.zero); t.bounds.Encapsulate(t.b); t.bounds.Encapsulate(t.c);
                        triangles.Add(t);
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(baked); }
            }
            fitBodySurface = triangles.Count > 0 ? new FitBodySurface(triangles.ToArray()) : null;
            if (fitBodySurface != null) { error = null; return true; }
            error = "Fit to Body needs another mesh marked as Body. Add a body or configure the Default Body.";
            repaint(); return false;
        }

        private Vector3 FitBodyPoint(Vector3 point, float amount)
        {
            if (fitBodySurface == null || !fitBodySurface.Closest(point,fitBodyReach,out var surface,out var normal)) return point;
            return Vector3.Lerp(point,surface + normal * fitBodyGap,amount);
        }

        private struct FitBodyTriangle
        {
            public Vector3 a,b,c,na,nb,nc;
            public Bounds bounds;
            public List<BoneWeight1> wa, wb, wc;
            public Vector3 Closest(Vector3 p, out Vector3 normal)
                => Closest(p, out normal, out _);
            public Vector3 Closest(Vector3 p, out Vector3 normal, out Vector3 bary)
            {
                // Triangle Voronoi regions; barycentrics also interpolate surface normals.
                var ab=b-a; var ac=c-a; var ap=p-a;
                float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
                if(d1<=0 && d2<=0) bary=new Vector3(1,0,0);
                else
                {
                    var bp=p-b; float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);
                    if(d3>=0 && d4<=d3) bary=new Vector3(0,1,0);
                    else
                    {
                        float vc=d1*d4-d3*d2;
                        if(vc<=0 && d1>=0 && d3<=0) { float v=d1/(d1-d3); bary=new Vector3(1-v,v,0); }
                        else
                        {
                            var cp=p-c; float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);
                            if(d6>=0 && d5<=d6) bary=new Vector3(0,0,1);
                            else
                            {
                                float vb=d5*d2-d1*d6,va=d3*d6-d5*d4;
                                if(vb<=0 && d2>=0 && d6<=0) { float w=d2/(d2-d6); bary=new Vector3(1-w,0,w); }
                                else if(va<=0 && d4-d3>=0 && d5-d6>=0) { float w=(d4-d3)/((d4-d3)+(d5-d6)); bary=new Vector3(0,1-w,w); }
                                else { float inv=1/(va+vb+vc); bary=new Vector3(va*inv,vb*inv,vc*inv); }
                            }
                        }
                    }
                }
                normal=(na*bary.x+nb*bary.y+nc*bary.z).normalized;
                if(normal.sqrMagnitude<.5f) normal=Vector3.Cross(ab,ac).normalized;
                return a*bary.x+b*bary.y+c*bary.z;
            }
        }

        // Built once per stroke from the current posed body, with no per-vertex allocations.
        private sealed class FitBodySurface
        {
            private sealed class Node { public Bounds bounds; public int start,count; public Node left,right; }
            private readonly FitBodyTriangle[] triangles;
            private readonly Node root;
            public FitBodySurface(FitBodyTriangle[] triangles) { this.triangles=triangles; root=Build(0,triangles.Length); }
            private Node Build(int start,int count)
            {
                var n=new Node { start=start,count=count,bounds=triangles[start].bounds };
                for(int i=start+1;i<start+count;i++) n.bounds.Encapsulate(triangles[i].bounds);
                if(count<=8) return n;
                var size=n.bounds.size; int axis=size.x>size.y ? (size.x>size.z?0:2) : (size.y>size.z?1:2);
                Array.Sort(triangles,start,count,Comparer<FitBodyTriangle>.Create((a,b)=>a.bounds.center[axis].CompareTo(b.bounds.center[axis])));
                int half=count/2; n.left=Build(start,half); n.right=Build(start+half,count-half); return n;
            }
            public bool Closest(Vector3 p,float reach,out Vector3 point,out Vector3 normal)
            {
                float best=reach*reach; point=normal=Vector3.zero; bool found=false;
                Search(root,p,ref best,ref point,ref normal,ref found); return found;
            }
            public bool Raycast(Ray ray, out RaycastHit hit)
            {
                hit = new RaycastHit(); float distance = float.PositiveInfinity;
                RaySearch(root, ray, ref distance, ref hit);
                return !float.IsPositiveInfinity(distance);
            }
            public BoneWeight1[] InterpolatedWeights(Vector3 point)
            {
                float best=float.PositiveInfinity; FitBodyTriangle triangle=default; Vector3 bary=default;
                FindWeights(root,point,ref best,ref triangle,ref bary);
                if(triangle.wa==null) throw new InvalidOperationException("No valid body surface was found for weight transfer.");
                var merged=new Dictionary<int,float>();
                void Add(List<BoneWeight1> weights,float amount)
                {
                    float total=0; foreach(var w in weights) total+=w.weight;
                    if(total<=1e-8f) return;
                    foreach(var w in weights)
                        merged[w.boneIndex]=(merged.TryGetValue(w.boneIndex,out var existing)?existing:0)+w.weight/total*amount;
                }
                Add(triangle.wa,bary.x); Add(triangle.wb,bary.y); Add(triangle.wc,bary.z);
                var result=new List<BoneWeight1>(); float sum=0;
                foreach(var pair in merged) if(pair.Value>1e-8f) { result.Add(new BoneWeight1 { boneIndex=pair.Key,weight=pair.Value }); sum+=pair.Value; }
                if(sum<=1e-8f) throw new InvalidOperationException("The nearest body surface has no usable skin weights.");
                for(int i=0;i<result.Count;i++) { var w=result[i]; w.weight/=sum; result[i]=w; }
                result.Sort((a,b)=>b.weight.CompareTo(a.weight)); return result.ToArray();
            }
            private void FindWeights(Node node,Vector3 point,ref float best,ref FitBodyTriangle triangle,ref Vector3 bary)
            {
                if(node.bounds.SqrDistance(point)>best) return;
                if(node.left==null)
                {
                    for(int i=node.start;i<node.start+node.count;i++)
                    {
                        var nearest=triangles[i].Closest(point,out _,out var coordinates);
                        float d=(nearest-point).sqrMagnitude;
                        if(d>=best) continue; best=d; triangle=triangles[i]; bary=coordinates;
                    }
                    return;
                }
                var first=node.left; var second=node.right;
                if(first.bounds.SqrDistance(point)>second.bounds.SqrDistance(point)) { first=node.right; second=node.left; }
                FindWeights(first,point,ref best,ref triangle,ref bary); FindWeights(second,point,ref best,ref triangle,ref bary);
            }
            private void RaySearch(Node node, Ray ray, ref float nearest, ref RaycastHit hit)
            {
                if (!node.bounds.IntersectRay(ray, out float entry) || entry > nearest) return;
                if (node.left != null)
                {
                    RaySearch(node.left,ray,ref nearest,ref hit); RaySearch(node.right,ray,ref nearest,ref hit); return;
                }
                for (int i=node.start;i<node.start+node.count;i++)
                {
                    var t=triangles[i]; var ab=t.b-t.a; var ac=t.c-t.a;
                    var cross=Vector3.Cross(ray.direction,ac); float det=Vector3.Dot(ab,cross);
                    if (Mathf.Abs(det)<1e-8f) continue;
                    float inv=1/det; var offset=ray.origin-t.a; float u=Vector3.Dot(offset,cross)*inv;
                    if(u<0 || u>1) continue;
                    var q=Vector3.Cross(offset,ab); float v=Vector3.Dot(ray.direction,q)*inv;
                    if(v<0 || u+v>1) continue;
                    float d=Vector3.Dot(ac,q)*inv; if(d<0 || d>=nearest) continue;
                    nearest=d; hit.distance=d; hit.point=ray.GetPoint(d);
                    hit.normal=(t.na*(1-u-v)+t.nb*u+t.nc*v).normalized;
                }
            }
            private void Search(Node node,Vector3 p,ref float best,ref Vector3 point,ref Vector3 normal,ref bool found)
            {
                if(node.bounds.SqrDistance(p)>best) return;
                if(node.left==null)
                {
                    for(int i=node.start;i<node.start+node.count;i++)
                    {
                        var q=triangles[i].Closest(p,out var n); float d=(q-p).sqrMagnitude;
                        if(d>best) continue; best=d; point=q; normal=n; found=true;
                    }
                    return;
                }
                var first=node.left; var second=node.right;
                if(first.bounds.SqrDistance(p)>second.bounds.SqrDistance(p)) { first=node.right; second=node.left; }
                Search(first,p,ref best,ref point,ref normal,ref found);
                Search(second,p,ref best,ref point,ref normal,ref found);
            }
        }
    }
}
