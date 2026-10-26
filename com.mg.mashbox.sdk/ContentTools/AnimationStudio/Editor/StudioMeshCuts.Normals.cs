using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        internal (int, int) NormalEdgeKey(int a, int b) => Key(a, b);
        private struct NormalEdgeUse { internal int Face, Edge; internal NormalEdgeUse(int face, int edge) { Face = face; Edge = edge; } }
        internal void SetEdgeNormals(StudioModelDocument.Node node, HashSet<(int,int)> selection, float angle, bool harden, Action beforeWrite = null)
        {
            var mesh = node.mesh;
            if (node.skinned || node.blendShape != null || mesh.blendShapeCount > 0 || mesh.bindposes.Length > 0)
                throw new InvalidOperationException("Bake skinning and blend shapes before editing edge normals.");
            if (float.IsNaN(angle) || float.IsInfinity(angle) || angle < 0 || angle > 180)
                throw new InvalidOperationException("Smoothing angle must be between 0 and 180 degrees.");
            var sourceNormals = mesh.normals; var sourceTangents = mesh.tangents; var colors = mesh.colors;
            var offset = new int[Faces.Count+1]; var faceNormals = new Vector3[Faces.Count];
            var adjacency = new Dictionary<(int,int),List<NormalEdgeUse>>();
            var membership = new Dictionary<int,List<int>>();
            for (int f = 0; f < Faces.Count; f++)
            {
                var vertices = Faces[f].Vertices; offset[f+1] = offset[f]+vertices.Count;
                var origin = Positions[vertices[0]]; var normal = Vector3.zero;
                for (int i = 1; i+1 < vertices.Count; i++) normal += Vector3.Cross(Positions[vertices[i]]-origin, Positions[vertices[i+1]]-origin);
                faceNormals[f] = normal.sqrMagnitude > 1e-20f ? normal.normalized : sourceNormals.Length == Positions.Length ? sourceNormals[vertices[0]].normalized : Vector3.up;
                for (int e = 0; e < vertices.Count; e++)
                {
                    int v = vertices[e];
                    if (!membership.TryGetValue(v, out var faces)) membership[v] = faces = new List<int>(); faces.Add(f);
                    var key = Key(v,vertices[(e+1)%vertices.Count]);
                    if (!adjacency.TryGetValue(key,out var uses)) adjacency[key] = uses = new List<NormalEdgeUse>(); uses.Add(new NormalEdgeUse(f,e));
                }
            }
            int total = offset[Faces.Count];
            var parent = Enumerable.Range(0,total).ToArray(); var next = Enumerable.Repeat(-1,total).ToArray();
            var tail = Enumerable.Range(0,total).ToArray(); var sizes = Enumerable.Repeat(1,total).ToArray();
            var forbidden = new List<int>[total]; var smooth = new List<(int a,int b)>(); var affected = new bool[total];
            int Root(int i) { while(parent[i]!=i) { parent[i]=parent[parent[i]]; i=parent[i]; } return i; }
            void Block(int a,int b)
            {
                if(forbidden[a]==null)forbidden[a]=new List<int>(); forbidden[a].Add(b);
                if(forbidden[b]==null)forbidden[b]=new List<int>(); forbidden[b].Add(a);
            }
            void Join(int a,int b)
            {
                a=Root(a);b=Root(b);if(a==b)return;
                if(sizes[a]>sizes[b]) { int swap=a;a=b;b=swap; }
                // A selected hard edge must remain split even if other soft edges
                // provide another path around a closed vertex fan.
                for(int i=a;i>=0;i=next[i])
                    if(forbidden[i]!=null)foreach(int j in forbidden[i])if(Root(j)==b)return;
                parent[a]=b;next[tail[b]]=a;tail[b]=tail[a];sizes[b]+=sizes[a];
            }
            float cosine = Mathf.Cos(angle*Mathf.Deg2Rad);
            foreach(var pair in adjacency)
            {
                var uses=pair.Value;if(uses.Count!=2)continue;
                var a=uses[0];var b=uses[1];var av=Faces[a.Face].Vertices;var bv=Faces[b.Face].Vertices;
                int ai=a.Edge,aj=(a.Edge+1)%av.Count,bi=b.Edge,bj=(b.Edge+1)%bv.Count;
                if(welded[av[ai]]!=welded[bv[bi]]) { int swap=bi;bi=bj;bj=swap; }
                bool targeted=selection==null||selection.Contains(pair.Key);
                bool soft;
                if(targeted)soft=!harden&&Vector3.Dot(faceNormals[a.Face],faceNormals[b.Face])>=cosine-1e-6f;
                else if(node.normalGroups!=null&&node.normalGroups.Length==Positions.Length)
                    soft=node.normalGroups[av[ai]]==node.normalGroups[bv[bi]]&&node.normalGroups[av[aj]]==node.normalGroups[bv[bj]];
                else if(sourceNormals.Length==Positions.Length)
                    soft=Vector3.Dot(sourceNormals[av[ai]].normalized,sourceNormals[bv[bi]].normalized)>.9999f&&
                         Vector3.Dot(sourceNormals[av[aj]].normalized,sourceNormals[bv[bj]].normalized)>.9999f;
                else soft=Vector3.Dot(faceNormals[a.Face],faceNormals[b.Face])>.9999f;
                int ca=offset[a.Face]+ai,cb=offset[b.Face]+bi,da=offset[a.Face]+aj,db=offset[b.Face]+bj;
                if(targeted){affected[ca]=affected[cb]=affected[da]=affected[db]=true;}
                if(soft){smooth.Add((ca,cb));smooth.Add((da,db));}else{Block(ca,cb);Block(da,db);}
            }
            foreach(var pair in smooth)Join(pair.a,pair.b);
            var affectedRoots=new bool[total];for(int i=0;i<total;i++)if(affected[i])affectedRoots[Root(i)]=true;
            var sums=new Vector3[total];var fallback=new Vector3[total];
            for(int f=0;f<Faces.Count;f++)
            {
                var v=Faces[f].Vertices;
                for(int c=0;c<v.Count;c++)
                {
                    var center=Positions[v[c]];
                    var a=(Positions[v[(c+v.Count-1)%v.Count]]-center).normalized;
                    var b=(Positions[v[(c+1)%v.Count]]-center).normalized;
                    float weight=Mathf.Acos(Mathf.Clamp(Vector3.Dot(a,b),-1,1));
                    int root=Root(offset[f]+c);sums[root]+=faceNormals[f]*Mathf.Max(weight,.00001f);fallback[root]=faceNormals[f];
                }
            }
            for(int i=0;i<total;i++)if(parent[i]==i)sums[i]=sums[i].sqrMagnitude>1e-20f?sums[i].normalized:fallback[i];
            var uv=new List<Vector4>[8];var dimensions=new int[8];
            for(int c=0;c<8;c++){uv[c]=new List<Vector4>();mesh.GetUVs(c,uv[c]);if(uv[c].Count>0)dimensions[c]=mesh.GetVertexAttributeDimension((VertexAttribute)((int)VertexAttribute.TexCoord0+c));}
            int Attributes(int i)
            {
                unchecked{int hash=17;if(colors.Length>0)hash=hash*31+colors[i].GetHashCode();for(int c=0;c<8;c++)if(uv[c].Count>0)hash=hash*31+uv[c][i].GetHashCode();return hash;}
            }
            bool Same(int a,int b)
            {
                if(colors.Length>0&&!colors[a].Equals(colors[b]))return false;
                for(int c=0;c<8;c++)if(uv[c].Count>0&&!uv[c][a].Equals(uv[c][b]))return false;return true;
            }
            var variants=new Dictionary<(int root,int attributes),List<int>>();
            var sources=new List<int>();var outputNormals=new List<Vector3>();var outputGroups=new List<int>();var groupIds=new Dictionary<(int,Vector3),int>();var corners=new int[Faces.Count][];
            for(int f=0;f<Faces.Count;f++)
            {
                var v=Faces[f].Vertices;corners[f]=new int[v.Count];
                for(int c=0;c<v.Count;c++)
                {
                    int source=v[c],root=Root(offset[f]+c);
                    var normal=selection!=null&&!affectedRoots[root]&&sourceNormals.Length==Positions.Length?sourceNormals[source]:sums[root];
                    var normalKey=(root,normal);if(!groupIds.TryGetValue(normalKey,out int group))groupIds[normalKey]=group=groupIds.Count;
                    var key=(group,Attributes(source));
                    if(!variants.TryGetValue(key,out var matches))variants[key]=matches=new List<int>();
                    int existing=matches.FindIndex(i=>Same(source,sources[i]));
                    if(existing>=0)corners[f][c]=matches[existing];
                    else{int index=sources.Count;sources.Add(source);outputNormals.Add(normal);outputGroups.Add(group);matches.Add(index);corners[f][c]=index;}
                }
            }
            // Retain the original triangle diagonal, including on warped polygons.
            var triangles=new int[mesh.subMeshCount][];
            for(int sub=0;sub<triangles.Length;sub++)
            {
                var old=mesh.GetTriangles(sub);triangles[sub]=new int[old.Length];
                for(int i=0;i<old.Length;i+=3)
                {
                    int face=-1;
                    if(membership.TryGetValue(old[i],out var candidates))
                        foreach(int f in candidates)if(Faces[f].Submesh==sub&&Faces[f].Vertices.Contains(old[i+1])&&Faces[f].Vertices.Contains(old[i+2])){face=f;break;}
                    if(face<0)throw new InvalidOperationException("Could not retain the mesh's polygon triangulation; normals were not changed.");
                    for(int c=0;c<3;c++)triangles[sub][i+c]=corners[face][Faces[face].Vertices.IndexOf(old[i+c])];
                }
            }
            var polygons=Faces.SelectMany((face,f)=>new[]{face.Submesh,face.Vertices.Count}.Concat(corners[f])).ToArray();
            beforeWrite?.Invoke();
            mesh.Clear();mesh.indexFormat=sources.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;
            mesh.SetVertices(sources.Select(i=>Positions[i]).ToList());mesh.SetNormals(outputNormals);
            if(colors.Length>0)mesh.SetColors(sources.Select(i=>colors[i]).ToList());
            for(int c=0;c<8;c++)if(uv[c].Count>0)
            {
                if(dimensions[c]==2)mesh.SetUVs(c,sources.Select(i=>new Vector2(uv[c][i].x,uv[c][i].y)).ToList());
                else if(dimensions[c]==3)mesh.SetUVs(c,sources.Select(i=>new Vector3(uv[c][i].x,uv[c][i].y,uv[c][i].z)).ToList());
                else mesh.SetUVs(c,sources.Select(i=>uv[c][i]).ToList());
            }
            mesh.subMeshCount=triangles.Length;for(int s=0;s<triangles.Length;s++)mesh.SetTriangles(triangles[s],s);
            if(uv[0].Count>0)mesh.RecalculateTangents();else if(sourceTangents.Length==Positions.Length)mesh.SetTangents(sources.Select(i=>sourceTangents[i]).ToList());
            mesh.RecalculateBounds();mesh.UploadMeshData(false);node.cutPolygons=polygons;node.cutTriangles=mesh.triangles;node.normalGroups=outputGroups.ToArray();
        }
    }
}
