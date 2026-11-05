using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        internal int[] FillHoles(StudioModelDocument.Node node,IEnumerable<(int a,int b)> selection)
        {
            var mesh=node.mesh;
            if(!mesh||!mesh.isReadable||node.skinned||node.blendShape!=null||mesh.blendShapeCount>0||mesh.bindposes.Length>0||mesh.boneWeights.Length>0)
                throw new InvalidOperationException("Fill Hole requires a readable static mesh without skinning or blend shapes.");
            var chosen=new Dictionary<(int,int),BridgeBorder>();
            foreach(var edge in selection)
            {
                if(edge.a<0||edge.b<0||edge.a>=Positions.Length||edge.b>=Positions.Length)throw new InvalidOperationException("The Fill Hole edge selection changed. Select the border again.");
                var key=Key(edge.a,edge.b);
                if(key.Item1==key.Item2||!BevelFinite(Positions[edge.a])||!BevelFinite(Positions[edge.b])||(Positions[edge.a]-Positions[edge.b]).sqrMagnitude==0)
                    throw new InvalidOperationException("Fill Hole requires finite, nonzero boundary edges.");
                if(!neighbours.TryGetValue(key,out var uses)||uses.Count!=1)
                    throw new InvalidOperationException("Fill Hole requires open boundary edges, not interior or non-manifold edges.");
                var use=uses[0];var ring=Faces[use.face].Vertices;int a=ring[use.edge],b=ring[(use.edge+1)%ring.Count];
                chosen[key]=new BridgeBorder{Face=use.face,A=a,B=b,Start=welded[a],End=welded[b]};
            }
            if(chosen.Count<3)throw new InvalidOperationException("Select complete closed boundary loops with at least three edges to fill.");
            var incidence=new Dictionary<int,List<BridgeBorder>>();
            foreach(var edge in chosen.Values)foreach(int v in new[]{edge.Start,edge.End})
            {if(!incidence.TryGetValue(v,out var list))incidence[v]=list=new List<BridgeBorder>();list.Add(edge);}
            if(incidence.Values.Any(v=>v.Count!=2))throw new InvalidOperationException("Fill Hole requires complete closed loops without open ends, branches or touching loops.");
            var pending=new HashSet<BridgeBorder>(chosen.Values);var loops=new List<BridgeChain>();
            foreach(var seed in chosen.OrderBy(p=>p.Key).Select(p=>p.Value))
            {
                if(!pending.Remove(seed))continue;var group=new List<BridgeBorder>();var queue=new Queue<BridgeBorder>();queue.Enqueue(seed);
                while(queue.Count>0){var e=queue.Dequeue();group.Add(e);foreach(int v in new[]{e.Start,e.End})foreach(var next in incidence[v])if(pending.Remove(next))queue.Enqueue(next);}
                BridgeChain chain;
                try{chain=OrderBridgeChain(group);}catch(InvalidOperationException){throw new InvalidOperationException("The selected border has inconsistent winding or branches. Select a complete manifold hole loop.");}
                if(!chain.Closed)throw new InvalidOperationException("Fill Hole requires a closed boundary loop.");loops.Add(chain);
            }
            var source=BevelSource(mesh);var points=Positions.ToList();var normals=source.RetainedNormals.ToList();var tangents=source.Tangents.ToList();var colors=source.Colors.ToList();
            var uv=new List<Vector4>[8];var dims=(int[])source.Dimensions.Clone();for(int c=0;c<8;c++)uv[c]=source.UV[c]?.ToList()??new List<Vector4>();
            if(uv[0].Count==0){uv[0].AddRange(Enumerable.Repeat(Vector4.zero,points.Count));dims[0]=2;}
            var geometry=welded.ToList();var groups=node.normalGroups!=null&&node.normalGroups.Length==Positions.Length?node.normalGroups.ToList():new List<int>();
            if(groups.Count==0)
            {var ids=new Dictionary<(int,Vector3),int>();for(int i=0;i<Positions.Length;i++){var key=(welded[i],normals[i]);if(!ids.TryGetValue(key,out int g))ids[key]=g=ids.Count;groups.Add(g);}}
            int nextGroup=groups.Count==0?0:groups.Max()+1;
            var indices=Enumerable.Range(0,mesh.subMeshCount).Select(s=>mesh.GetTriangles(s).ToList()).ToArray();
            var polygons=Faces.SelectMany(f=>new[]{f.Submesh,f.Vertices.Count}.Concat(f.Vertices)).ToList();var caps=new List<int>();
            foreach(var chain in loops)
            {
                // Reverse the adjacent faces' winding. Every edge endpoint stays
                // in this ring, including collinear points along straight sides.
                var boundary=chain.Edges.Select(e=>e.A).Reverse().ToList();
                var normal=BevelPolygonNormal(boundary,Positions);
                if(normal.sqrMagnitude<.5f)throw new InvalidOperationException("The hole boundary has no usable surface area.");
                var render=new List<int>();
                try{TriangulateSliceCap(boundary,points,normal,render);}
                catch(InvalidOperationException){throw new InvalidOperationException("The hole boundary cannot form a valid n-gon. Check for crossings or collapsed edges.");}
                // The renderer must retain the very same segmented boundary as
                // authored topology: never silently simplify a collinear side.
                var edgeUse=new Dictionary<(int,int),int>();
                for(int t=0;t<render.Count;t+=3)for(int e=0;e<3;e++){var k=FlowKey(render[t+e],render[t+(e+1)%3]);edgeUse.TryGetValue(k,out int count);edgeUse[k]=count+1;}
                if(render.Count!=(boundary.Count-2)*3||Enumerable.Range(0,boundary.Count).Any(i=>!edgeUse.TryGetValue(FlowKey(boundary[i],boundary[(i+1)%boundary.Count]),out int count)||count!=1))
                    throw new InvalidOperationException("Fill Hole could not retain every boundary segment in its render tessellation.");
                var chart=new CutChart(Positions[boundary[0]],normal);var mapped=new Dictionary<int,int>();var cap=new List<int>();
                foreach(int raw in boundary)
                {
                    int id=points.Count;cap.Add(id);mapped[raw]=id;points.Add(Positions[raw]);geometry.Add(welded[raw]);normals.Add(normal);groups.Add(nextGroup);
                    if(tangents.Count>0)tangents.Add(new Vector4(chart.X.x,chart.X.y,chart.X.z,1));if(colors.Count>0)colors.Add(source.Colors[raw]);
                    for(int c=0;c<8;c++)if(uv[c].Count>0){var value=source.UV[c]!=null&&source.UV[c].Length>raw?source.UV[c][raw]:Vector4.zero;if(c==0){var d=Positions[raw]-chart.Origin;value.x=Vector3.Dot(d,chart.X);value.y=Vector3.Dot(d,chart.Y);}uv[c].Add(value);}
                }
                nextGroup++;int material=Faces[chain.Edges[0].Face].Submesh;caps.Add(Faces.Count+caps.Count);polygons.Add(material);polygons.Add(cap.Count);polygons.AddRange(cap);
                indices[material].AddRange(render.Select(v=>mapped[v]));
            }
            var format=mesh.indexFormat==IndexFormat.UInt32||points.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;
            mesh.Clear();mesh.indexFormat=format;mesh.SetVertices(points);mesh.SetNormals(normals);if(tangents.Count>0)mesh.SetTangents(tangents);if(colors.Count>0)mesh.SetColors(colors);
            for(int c=0;c<8;c++)if(uv[c].Count>0)
            {if(dims[c]==1)StudioSubdivisionData.SetUV1(mesh,c,uv[c]);else if(dims[c]==2)mesh.SetUVs(c,uv[c].Select(v=>new Vector2(v.x,v.y)).ToList());else if(dims[c]==3)mesh.SetUVs(c,uv[c].Select(v=>new Vector3(v.x,v.y,v.z)).ToList());else mesh.SetUVs(c,uv[c]);}
            mesh.subMeshCount=indices.Length;for(int s=0;s<indices.Length;s++)mesh.SetTriangles(indices[s],s,false);mesh.RecalculateBounds();
            node.cutPolygons=polygons.ToArray();node.cutTriangles=mesh.triangles;node.geometryVertices=GeometricIds(geometry);node.normalGroups=groups.ToArray();return caps.ToArray();
        }
    }
}
