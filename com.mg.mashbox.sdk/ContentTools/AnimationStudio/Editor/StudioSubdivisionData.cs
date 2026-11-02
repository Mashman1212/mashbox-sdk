using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    [Serializable]
    public sealed class StudioSubdivisionSettings
    {
        public bool preview;
        public int level=1, boundary=2;
        public StudioSubdivisionSettings Copy() => new StudioSubdivisionSettings { preview=preview,level=level,boundary=boundary };
    }
    internal static class StudioSubdivisionData
    {
        internal static void SetUV1(Mesh mesh,int channel,IEnumerable<Vector4> values)
        {using(var data=new Unity.Collections.NativeArray<float>(values.Select(v=>v.x).ToArray(),Unity.Collections.Allocator.Temp))mesh.SetUVs(channel,data);}
        internal static int[] Identity(int count) { var result=new int[count];for(int i=0;i<count;i++)result[i]=i;return result; }
        // Only call for a primitive constructed by Studio, whose single surface
        // connectivity is known. Never use this to recover imported mesh topology.
        internal static int[] Primitive(Mesh mesh)
        {
            var map=new Dictionary<Vector3,int>();var p=mesh.vertices;var result=new int[p.Length];
            for(int i=0;i<p.Length;i++){if(!map.TryGetValue(p[i],out int id))map.Add(p[i],id=map.Count);result[i]=id;}
            return result;
        }
        internal static int[] Geometry(StudioModelDocument.Node node)
        {
            if(node.geometryVertices!=null && node.geometryVertices.Length==node.mesh.vertexCount)return node.geometryVertices;
            // Legacy/imported indices are the only authoritative identity available.
            // Equal coordinates alone never establish a shared control vertex.
            return Identity(node.mesh.vertexCount);
        }
        internal sealed class Input
        {
            internal int[] Representatives, Geometry, Counts, Corners, Values, Materials;
            internal StudioSubdivisionPlan Plan;
            internal Input(StudioModelDocument.Node node)
            {
                var mesh=node.mesh;
                if(!mesh || !mesh.isReadable || node.skinned || mesh.blendShapeCount>0 || node.blendShape!=null)
                    throw new InvalidOperationException("Subdivision requires a readable static mesh without blend shapes.");
                if(node.cutPolygons==null || node.cutTriangles==null || !node.cutTriangles.SequenceEqual(mesh.triangles))
                    throw new InvalidOperationException("Subdivision requires current authored polygon boundaries.");
                Geometry=StudioSubdivisionData.Geometry(node);
                var unique=new Dictionary<int,int>();var representatives=new List<int>();
                var mapped=new int[Geometry.Length];
                for(int i=0;i<mapped.Length;i++)
                {
                    if(Geometry[i]<0)throw new InvalidOperationException("Invalid geometric vertex identity.");
                    if(!unique.TryGetValue(Geometry[i],out int id)){unique.Add(Geometry[i],id=unique.Count);representatives.Add(i);}mapped[i]=id;
                }
                // Face-varying identities depend on actual attributes, not normal splits.
                var sourceUV=new List<Vector4>[8];for(int c=0;c<8;c++){sourceUV[c]=new List<Vector4>();mesh.GetUVs(c,sourceUV[c]);}
                var sourceColors=mesh.colors;var attributeVertices=new int[mesh.vertexCount];
                var variants=new Dictionary<(int,int),List<int>>();
                for(int v=0;v<attributeVertices.Length;v++)
                {
                    int hash=17;unchecked{for(int c=0;c<8;c++)if(sourceUV[c].Count>0)hash=hash*31+sourceUV[c][v].GetHashCode();if(sourceColors.Length>0)hash=hash*31+sourceColors[v].GetHashCode();}
                    var key=(Geometry[v],hash);if(!variants.TryGetValue(key,out var candidates))variants.Add(key,candidates=new List<int>());
                    int match=-1;foreach(int candidate in candidates){bool same=sourceColors.Length==0||sourceColors[candidate].Equals(sourceColors[v]);for(int c=0;c<8&&same;c++)if(sourceUV[c].Count>0)same=sourceUV[c][candidate].Equals(sourceUV[c][v]);if(same){match=candidate;break;}}
                    if(match<0){match=v;candidates.Add(v);}attributeVertices[v]=match;
                }
                var counts=new List<int>();var corners=new List<int>();var values=new List<int>();var materials=new List<int>();
                var polygon=node.cutPolygons;
                for(int i=0;i<polygon.Length;)
                {
                    if(i+2>polygon.Length)throw new InvalidOperationException("Incomplete polygon metadata.");
                    int material=polygon[i++],count=polygon[i++];
                    if(count<3 || count>polygon.Length-i || material<0 || material>=mesh.subMeshCount)throw new InvalidOperationException("Invalid polygon metadata.");
                    counts.Add(count);materials.Add(material);
                    var distinct=new HashSet<int>();
                    for(int c=0;c<count;c++)
                    {
                        int v=polygon[i++];if(v<0||v>=mapped.Length)throw new InvalidOperationException("Invalid polygon vertex.");
                        if(!distinct.Add(mapped[v]))throw new InvalidOperationException("Subdivision cannot refine a face containing repeated geometric vertices.");
                        corners.Add(mapped[v]);values.Add(attributeVertices[v]);
                    }
                }
                var settings=node.subdivision??new StudioSubdivisionSettings();
                Representatives=representatives.ToArray();Counts=counts.ToArray();Corners=corners.ToArray();Values=values.ToArray();Materials=materials.ToArray();
                Plan=new StudioSubdivisionPlan(Representatives.Length,Counts,Corners,mesh.vertexCount,Values,settings.level,settings.boundary);
            }
            internal Vector3[] Positions(Mesh source)
            {var p=source.vertices;return Representatives.Select(i=>p[i]).ToArray();}
            internal Mesh CreateMesh(StudioModelDocument.Node node,bool evaluate,out int[] polygons)
            {
                var mesh=new Mesh {name=node.mesh.name+" Subdivision",hideFlags=HideFlags.HideAndDontSave,indexFormat=IndexFormat.UInt32};
                try
                {
                    Vector3[] p,n;
                    if(evaluate)Plan.Surface(Positions(node.mesh),out p,out n);
                    else {p=new Vector3[Plan.Corners.Length];n=new Vector3[p.Length];}
                    mesh.vertices=p;mesh.normals=n;
                    var uv=new List<Vector4>();
                    for(int channel=0;channel<8;channel++)
                    {
                        uv.Clear();node.mesh.GetUVs(channel,uv);if(uv.Count==0)continue;
                        var refined=Plan.EvaluateAttributes(uv);var output=Plan.Values.Select(i=>refined[i]).ToList();
                        int dimension=node.mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0+channel);
                        if(dimension==1)SetUV1(mesh,channel,output);
                        else if(dimension==2)mesh.SetUVs(channel,output.Select(v=>new Vector2(v.x,v.y)).ToList());
                        else if(dimension==3)mesh.SetUVs(channel,output.Select(v=>new Vector3(v.x,v.y,v.z)).ToList());
                        else mesh.SetUVs(channel,output);
                    }
                    var colors=node.mesh.colors;
                    if(colors.Length>0){var refined=Plan.EvaluateAttributes(colors.Select(c=>(Vector4)c).ToArray());mesh.colors=Plan.Values.Select(i=>(Color)refined[i]).ToArray();}
                    var indices=Enumerable.Range(0,node.mesh.subMeshCount).Select(_=>new List<int>()).ToArray();var metadata=new List<int>();
                    for(int f=0;f<Plan.FaceCount;f++)
                    {
                        int c=f*4,material=Materials[Plan.Parents[f]];
                        metadata.AddRange(new[]{material,4,c,c+1,c+2,c+3});
                        indices[material].AddRange(new[]{c,c+1,c+2,c,c+2,c+3});
                    }
                    mesh.subMeshCount=indices.Length;for(int i=0;i<indices.Length;i++)mesh.SetTriangles(indices[i],i,false);
                    mesh.bounds=node.mesh.bounds;
                    if(mesh.HasVertexAttribute(VertexAttribute.TexCoord0))
                    {
                        var tangents=new Vector4[p.Length];
                        if(evaluate)
                        {
                            var tex=mesh.uv;
                            for(int i=0;i<p.Length;i++)
                            {
                                int f=i/4*4;var ab=p[f+1]-p[f];var ac=p[f+2]-p[f];var u=tex[f+1]-tex[f];var v=tex[f+2]-tex[f];float determinant=u.x*v.y-u.y*v.x;
                                var tangent=Mathf.Abs(determinant)>1e-20f?(ab*v.y-ac*u.y)/determinant:Vector3.right;
                                var bitangent=Mathf.Abs(determinant)>1e-20f?(ac*u.x-ab*v.x)/determinant:Vector3.forward;
                                tangent-=n[i]*Vector3.Dot(n[i],tangent);float length=tangent.sqrMagnitude;
                                tangent=length>1e-24f?tangent/Mathf.Sqrt(length):(Mathf.Abs(n[i].y)<.9f?Vector3.Cross(Vector3.up,n[i]):Vector3.Cross(Vector3.right,n[i])).normalized;
                                tangents[i]=new Vector4(tangent.x,tangent.y,tangent.z,Vector3.Dot(Vector3.Cross(n[i],tangent),bitangent)<0?-1:1);
                            }
                        }
                        mesh.tangents=tangents;
                    }
                    polygons=metadata.ToArray();return mesh;
                }
                catch {UnityEngine.Object.DestroyImmediate(mesh);throw;}
            }
        }
    }
}
