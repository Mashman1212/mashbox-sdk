using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Culling
{
    // Construct once per mesh/reference pair. Animation never changes a bind-space map.
    public sealed class ClothingCullingMap
    {
        readonly int[] triangles, samples;
        readonly bool direct;
        public static string BoneName(string name) => name.Substring(name.LastIndexOf(':')+1);

        public ClothingCullingMap(ClothingCullingSurface surface, Mesh mesh, string[] boneNames)
        {
            if(!surface || !mesh || !mesh.isReadable) throw new InvalidOperationException("Culling requires a readable body mesh and a reference surface.");
            triangles=mesh.triangles;
            var points=mesh.vertices;
            var binds=mesh.bindposes;
            if(binds.Length>0)
            {
                var transforms=new Matrix4x4[binds.Length];
                var valid=new bool[binds.Length];
                var sourceBones=new Dictionary<string,int>(StringComparer.Ordinal);
                for(int b=0;b<surface.boneNames.Length;b++)
                {var name=BoneName(surface.boneNames[b]);if(!sourceBones.ContainsKey(name))sourceBones.Add(name,b);}
                for(int b=0;b<binds.Length && b<boneNames.Length;b++)
                {
                    if(!sourceBones.TryGetValue(BoneName(boneNames[b]),out int source) || source>=surface.bindposes.Length)continue;
                    transforms[b]=surface.bindposes[source].inverse*binds[b]; valid[b]=true;
                }
                using(var counts=mesh.GetBonesPerVertex())
                using(var weights=mesh.GetAllBoneWeights())
                {
                    int offset=0;
                    for(int v=0;v<points.Length;v++)
                    {
                        Vector3 p=Vector3.zero; float total=0;
                        for(int w=0;v<counts.Length && w<counts[v];w++)
                        {
                            var weight=weights[offset++];
                            if(weight.weight<=0)continue;
                            if(weight.boneIndex>=valid.Length || !valid[weight.boneIndex])
                                throw new InvalidOperationException("Culling reference is missing an influencing bone: " + mesh.name);
                            p+=transforms[weight.boneIndex].MultiplyPoint3x4(points[v])*weight.weight;
                            total+=weight.weight;
                        }
                        if(total>0)points[v]=p/total;
                    }
                }
            }
            direct=points.Length==surface.vertices.Length && triangles.Length==surface.triangles.Length;
            if(direct)for(int i=0;i<triangles.Length;i++)if(triangles[i]!=surface.triangles[i]){direct=false;break;}
            if(direct)for(int i=0;i<points.Length;i++)if((points[i]-surface.vertices[i]).sqrMagnitude>1e-8f){direct=false;break;}
            if(direct)return;
            samples=new int[points.Length];
            const float reach=.015f;
            var tree=new SurfaceTree(surface.vertices,surface.triangles);
            for(int v=0;v<points.Length;v++)
                samples[v]=tree.Nearest(points[v],reach*reach);
        }

        // Nearest-triangle queries visit tight bounds, rather than hundreds of
        // triangles in every expanded grid cell. The 15 mm matching tolerance is unchanged.
        sealed class SurfaceTree
        {
            struct Node { public Bounds bounds; public int start,count,left,right; }
            sealed class AxisComparer : IComparer<int>
            {
                readonly Vector3[] centers;readonly int axis;
                public AxisComparer(Vector3[] centers,int axis){this.centers=centers;this.axis=axis;}
                public int Compare(int a,int b){int result=centers[a][axis].CompareTo(centers[b][axis]);return result!=0?result:a.CompareTo(b);}
            }
            readonly Vector3[] vertices;
            readonly int[] triangles,order;
            readonly Bounds[] bounds;
            readonly AxisComparer[] comparers;
            readonly List<Node> nodes=new List<Node>();
            public SurfaceTree(Vector3[] vertices,int[] triangles)
            {
                this.vertices=vertices;this.triangles=triangles;
                int count=triangles.Length/3;
                order=new int[count];bounds=new Bounds[count];var centers=new Vector3[count];
                for(int t=0;t<count;t++)
                {
                    order[t]=t;var a=vertices[triangles[t*3]];
                    bounds[t]=new Bounds(a,Vector3.zero);
                    bounds[t].Encapsulate(vertices[triangles[t*3+1]]);bounds[t].Encapsulate(vertices[triangles[t*3+2]]);
                    centers[t]=bounds[t].center;
                }
                comparers=new[]{new AxisComparer(centers,0),new AxisComparer(centers,1),new AxisComparer(centers,2)};
                if(count>0)Build(0,count);
            }
            int Build(int start,int count)
            {
                var box=bounds[order[start]];
                for(int i=start+1;i<start+count;i++)box.Encapsulate(bounds[order[i]]);
                int index=nodes.Count;nodes.Add(default);
                var node=new Node{bounds=box,start=start,count=count};
                if(count>8)
                {
                    var size=box.size;int axis=size.x>=size.y && size.x>=size.z?0:size.y>=size.z?1:2;
                    Array.Sort(order,start,count,comparers[axis]);
                    int half=count/2;node.count=0;node.left=Build(start,half);node.right=Build(start+half,count-half);
                }
                nodes[index]=node;return index;
            }
            public int Nearest(Vector3 point,float maximumSquared)
            {
                int triangle=-1;float distance=maximumSquared;
                if(nodes.Count>0)Search(0,point,ref triangle,ref distance);
                return triangle;
            }
            void Search(int index,Vector3 point,ref int triangle,ref float distance)
            {
                var node=nodes[index];if(node.bounds.SqrDistance(point)>distance)return;
                if(node.count>0)
                {
                    for(int i=node.start;i<node.start+node.count;i++)
                    {
                        int t=order[i];if(bounds[t].SqrDistance(point)>distance)continue;
                        var q=Closest(point,vertices[triangles[t*3]],vertices[triangles[t*3+1]],vertices[triangles[t*3+2]]);
                        float d=(q-point).sqrMagnitude;
                        // Preserve the old ascending-triangle tie-break on shared edges.
                        if(d<distance || (triangle>=0 && d==distance && t<triangle)){distance=d;triangle=t;}
                    }
                }
                else
                {
                    int first=node.left,second=node.right;
                    if(nodes[second].bounds.SqrDistance(point)<nodes[first].bounds.SqrDistance(point)){first=node.right;second=node.left;}
                    Search(first,point,ref triangle,ref distance);Search(second,point,ref triangle,ref distance);
                }
            }
        }

        public bool[] Evaluate(CullingMask mask)
        {
            var hidden=new bool[triangles.Length/3];
            UnionInto(mask,hidden);
            return hidden;
        }

        public void UnionInto(CullingMask mask,bool[] hidden)
        {
            if(!mask.surface || mask.hidden==null || mask.hidden.Length!=mask.surface.TriangleCount)
                throw new InvalidOperationException("Culling mask no longer matches its reference topology.");
            if(hidden==null || hidden.Length!=triangles.Length/3)
                throw new ArgumentException("Culling destination does not match the mapped mesh.",nameof(hidden));
            bool Hidden(int v)=>samples[v]>=0 && mask.hidden[samples[v]]!=0;
            for(int t=0;t<hidden.Length;t++)hidden[t]|=direct ? mask.hidden[t]!=0 :
                Hidden(triangles[t*3]) && Hidden(triangles[t*3+1]) && Hidden(triangles[t*3+2]);
        }

        public static void Apply(Mesh destination, Mesh original, bool[] hidden)
        {
            int offset=0;
            for(int s=0;s<original.subMeshCount;s++)
            {
                var source=original.GetTriangles(s);var kept=new List<int>(source.Length);
                for(int i=0;i<source.Length;i+=3,offset++)
                    if(offset>=hidden.Length || !hidden[offset]){kept.Add(source[i]);kept.Add(source[i+1]);kept.Add(source[i+2]);}
                destination.SetTriangles(kept,s,false);
            }
            destination.bounds=original.bounds;
        }

        static Vector3 Closest(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
        {
            var ab=b-a;var ac=c-a;var ap=p-a;float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
            if(d1<=0 && d2<=0)return a;
            var bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);
            if(d3>=0 && d4<=d3)return b;
            float vc=d1*d4-d3*d2;if(vc<=0 && d1>=0 && d3<=0)return a+ab*(d1/(d1-d3));
            var cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);
            if(d6>=0 && d5<=d6)return c;
            float vb=d5*d2-d1*d6;if(vb<=0 && d2>=0 && d6<=0)return a+ac*(d2/(d2-d6));
            float va=d3*d6-d5*d4;if(va<=0 && d4-d3>=0 && d5-d6>=0)return b+(c-b)*((d4-d3)/((d4-d3)+(d5-d6)));
            float sum=va+vb+vc;if(Mathf.Abs(sum)<1e-20f)return a;
            return a+ab*(vb/sum)+ac*(vc/sum);
        }
    }
}
