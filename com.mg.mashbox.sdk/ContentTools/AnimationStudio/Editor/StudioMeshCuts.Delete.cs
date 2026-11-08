using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        // Dissolve authored boundaries and remove endpoints no longer supporting other edges.
        private int[] DissolveEdges(StudioModelDocument.Node node, HashSet<(int,int)> selected,
            Func<int,int,(int,int)> key, Action beforeWrite)
        {
            var uses=new Dictionary<(int,int),List<(int face,int a,int b)>>();
            for(int f=0;f<Faces.Count;f++)
            {
                var v=Faces[f].Vertices;
                for(int e=0;e<v.Count;e++)
                {
                    int a=v[e],b=v[(e+1)%v.Count];var k=key(a,b);
                    if(!uses.TryGetValue(k,out var list))uses[k]=list=new List<(int,int,int)>();
                    list.Add((f,a,b));
                }
            }
            var parents=Enumerable.Range(0,Faces.Count).ToArray();
            int Root(int f){while(parents[f]!=f)f=parents[f];return f;}
            foreach(var edge in selected)
            {
                if(!uses.TryGetValue(edge,out var list)||list.Count!=2||list[0].face==list[1].face)
                    throw new InvalidOperationException("Delete edges requires shared interior edges. To open a hole, select and delete faces instead.");
                var a=list[0];var b=list[1];
                if(welded[a.a]!=welded[b.b]||welded[a.b]!=welded[b.a])
                    throw new InvalidOperationException("Cannot dissolve edges with inconsistent face winding.");
                if(Faces[a.face].Submesh!=Faces[b.face].Submesh)
                    throw new InvalidOperationException("Cannot dissolve across material slots. Assign the faces to the same slot first.");
                parents[Root(b.face)]=Root(a.face);
            }
            var endpoints=new HashSet<int>();
            foreach(var edge in selected)foreach(var use in uses[edge]){endpoints.Add(welded[use.a]);endpoints.Add(welded[use.b]);}
            var remainingNeighbours=new Dictionary<int,HashSet<int>>();
            void Neighbour(int a,int b)
            {if(!remainingNeighbours.TryGetValue(a,out var set))remainingNeighbours[a]=set=new HashSet<int>();set.Add(b);}
            foreach(var pair in uses)if(!selected.Contains(pair.Key))foreach(var use in pair.Value)
            {Neighbour(welded[use.a],welded[use.b]);Neighbour(welded[use.b],welded[use.a]);}
            var redundant=new HashSet<int>(endpoints.Where(v=>!remainingNeighbours.TryGetValue(v,out var set)||set.Count<=2));
            var sourceTriangles=BevelSourceTriangles(node.mesh);
            var output=new List<Face>();var outputTriangles=new List<List<int>>();
            void Add(Face face, bool changed, List<int> original)
            {output.Add(face);outputTriangles.Add(changed?null:original);}
            foreach(var region in Enumerable.Range(0,Faces.Count).GroupBy(Root))
            {
                if(region.Count()==1){int f=region.First();Add(Faces[f],false,sourceTriangles[f]);continue;}
                var ids=new HashSet<int>(region);var boundary=new Dictionary<int,(int a,int b)>();
                foreach(int f in region)
                {
                    var v=Faces[f].Vertices;
                    for(int e=0;e<v.Count;e++)
                    {
                        int a=v[e],b=v[(e+1)%v.Count];var k=key(a,b);
                        if(selected.Contains(k))continue;
                        if(uses[k].Any(u=>u.face!=f&&ids.Contains(u.face)))
                            throw new InvalidOperationException("Select all interior edges of the region to dissolve it.");
                        if(boundary.ContainsKey(welded[a]))
                            throw new InvalidOperationException("Cannot dissolve a branched or pinched polygon boundary.");
                        boundary.Add(welded[a],(a,b));
                    }
                }
                if(boundary.Count<3)throw new InvalidOperationException("Cannot dissolve an entire closed surface.");
                int start=boundary.Keys.First(),current=start;var polygon=new List<int>();
                var visited=new HashSet<int>();
                while(visited.Add(current)&&boundary.TryGetValue(current,out var next))
                {polygon.Add(next.a);current=welded[next.b];}
                if(current!=start||polygon.Count!=boundary.Count)
                    throw new InvalidOperationException("Cannot dissolve a region with holes or disconnected boundaries.");
                Add(new Face{Submesh=Faces[region.First()].Submesh,Vertices=polygon},true,null);
            }
            // Cleanup must never turn a valid surviving polygon into an edge/point.
            // Protect such corners across every face, including render seam aliases.
            foreach(var face in output)
                if(face.Vertices.Count(v=>!redundant.Contains(welded[v]))<3)
                    foreach(int v in face.Vertices)redundant.Remove(welded[v]);
            var positions=Positions.ToList();
            for(int f=0;f<output.Count;f++)
            {
                var polygon=output[f].Vertices.Where(v=>!redundant.Contains(welded[v])).ToList();
                if(outputTriangles[f]==null||polygon.Count!=output[f].Vertices.Count)
                {
                    var triangles=new List<int>();Triangulate(polygon,positions,triangles);
                    if(triangles.Count!=(polygon.Count-2)*3)throw new InvalidOperationException("Cannot triangulate the dissolved polygon; the mesh was preserved.");
                    outputTriangles[f]=triangles;
                }
                output[f]=new Face{Submesh=output[f].Submesh,Vertices=polygon};
            }
            return PublishDeletedComponents(node,output,outputTriangles,beforeWrite);
        }
        internal int[] DeleteComponents(StudioModelDocument.Node node, HashSet<int> faces,
            HashSet<int> vertices, IEnumerable<(int a, int b)> edges, bool weldEdges, Action beforeWrite)
        {
            var mesh = node.mesh;
            if (!mesh.isReadable) throw new InvalidOperationException("Component deletion requires a readable Studio editable mesh.");
            if (node.skinned || mesh.blendShapeCount > 0)
                throw new InvalidOperationException("Component deletion is unavailable on skinned meshes or meshes with blend shapes.");
            (int, int) EdgeKey(int a, int b) => weldEdges ? Key(a, b) : (Math.Min(a, b), Math.Max(a, b));
            var edgeKeys = new HashSet<(int, int)>(edges.Select(e => EdgeKey(e.a, e.b)));
            if(edgeKeys.Count>0)
            {
                if(faces.Count>0||vertices.Count>0)throw new InvalidOperationException("Delete one component type at a time.");
                return DissolveEdges(node,edgeKeys,EdgeKey,beforeWrite);
            }
            var kept = new List<int>();
            var retainedPolygons = new Dictionary<int, List<int>>();
            for (int f = 0; f < Faces.Count; f++)
            {
                var polygon = Faces[f].Vertices;
                bool remove = faces.Contains(f);
                if (remove) continue;
                // Vertex deletion clips the selected corners from each polygon boundary.
                // A quad with one removed corner becomes the remaining triangle.
                var remaining = polygon.Where(v => !vertices.Contains(v)).ToList();
                if (remaining.Count < 3) continue;
                kept.Add(f); retainedPolygons.Add(f, remaining);
            }
            bool removedVertex = vertices.Any(v => v >= 0 && v < Positions.Length);
            if (kept.Count == Faces.Count && !removedVertex) return null;
            var originalTriangles = BevelSourceTriangles(mesh);
            var output=new List<Face>();var outputTriangles=new List<List<int>>();
            List<Vector3> triangulationPositions = null;
            foreach (int f in kept)
            {
                var remaining = retainedPolygons[f];
                var faceTriangles = originalTriangles[f];
                if (remaining.Count != Faces[f].Vertices.Count)
                {
                    if (triangulationPositions == null) triangulationPositions = Positions.ToList();
                    faceTriangles = new List<int>();
                    Triangulate(remaining, triangulationPositions, faceTriangles);
                }
                else if (faceTriangles.Count != (remaining.Count - 2) * 3)
                    throw new InvalidOperationException("A remaining face has no matching render triangulation; deletion was not applied.");
                output.Add(new Face{Submesh=Faces[f].Submesh,Vertices=remaining});outputTriangles.Add(faceTriangles);
            }
            return PublishDeletedComponents(node,output,outputTriangles,beforeWrite);
        }
        private int[] PublishDeletedComponents(StudioModelDocument.Node node,List<Face> output,List<List<int>> faceTriangles,Action beforeWrite)
        {
            var mesh=node.mesh;
            var used=output.SelectMany(f=>f.Vertices).Concat(faceTriangles.SelectMany(t=>t)).Distinct().OrderBy(v=>v).ToArray();
            var remap=new int[Positions.Length];for(int i=0;i<used.Length;i++)remap[used[i]]=i;
            var triangles=Enumerable.Range(0,mesh.subMeshCount).Select(_=>new List<int>()).ToArray();
            for(int f=0;f<output.Count;f++)triangles[output[f].Submesh].AddRange(faceTriangles[f].Select(v=>remap[v]));
            var source=BevelSource(mesh);
            var polygons=output.SelectMany(f=>new[]{f.Submesh,f.Vertices.Count}.Concat(f.Vertices.Select(v=>remap[v]))).ToArray();
            beforeWrite?.Invoke();
            mesh.Clear(); mesh.indexFormat = used.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(used.Select(v => Positions[v]).ToList());
            if (source.Normals.Length == Positions.Length) mesh.SetNormals(used.Select(v => source.Normals[v]).ToList());
            if (source.Tangents.Length == Positions.Length) mesh.SetTangents(used.Select(v => source.Tangents[v]).ToList());
            if (source.Colors.Length == Positions.Length) mesh.SetColors(used.Select(v => source.Colors[v]).ToList());
            for (int c = 0; c < 8; c++) if (source.UV[c] != null)
            {
                var values = used.Select(v => source.UV[c][v]);
                if (source.Dimensions[c] == 1) StudioSubdivisionData.SetUV1(mesh,c,values);
                else if (source.Dimensions[c] == 2) mesh.SetUVs(c, values.Select(v => new Vector2(v.x, v.y)).ToList());
                else if (source.Dimensions[c] == 3) mesh.SetUVs(c, values.Select(v => new Vector3(v.x, v.y, v.z)).ToList());
                else mesh.SetUVs(c, values.ToList());
            }
            mesh.subMeshCount = triangles.Length;
            for (int s = 0; s < triangles.Length; s++) mesh.SetTriangles(triangles[s], s);
            mesh.RecalculateBounds(); mesh.UploadMeshData(false);
            node.cutPolygons = polygons; node.cutTriangles = mesh.triangles;node.geometryVertices=GeometricIds(used.Select(i=>welded[i]));
            return used;
        }
    }
}
