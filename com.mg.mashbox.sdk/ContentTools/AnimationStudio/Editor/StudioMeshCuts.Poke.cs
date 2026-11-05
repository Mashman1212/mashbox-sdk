using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        internal int[] PokeFaces(StudioModelDocument.Node node,IEnumerable<int> selection)
        {
            var mesh=node.mesh;
            if(!mesh||!mesh.isReadable||node.skinned||node.blendShape!=null||mesh.blendShapeCount>0||mesh.bindposes.Length>0||mesh.boneWeights.Length>0)
                throw new InvalidOperationException("Poke requires a readable static mesh without skinning or blend shapes.");
            var chosen=new HashSet<int>(selection??Array.Empty<int>());
            if(chosen.Count==0||chosen.Any(f=>f<0||f>=Faces.Count))throw new InvalidOperationException("Select valid faces to poke.");
            foreach(int f in chosen)
                if(Faces[f].Vertices.Count<3||Faces[f].Vertices.Any(v=>v<0||v>=Positions.Length||!BevelFinite(Positions[v])))
                    throw new InvalidOperationException("Poke requires face boundaries with at least three finite vertices.");
            // Deliberately retain a simple arithmetic center and every boundary
            // connection, including concave/warped/overlapping fans requested by
            // the artist. Do not run cut triangulation or collision rejection.
            var source=BevelSource(mesh);var points=Positions.ToList();var normals=source.RetainedNormals.ToList();
            var tangents=source.Tangents.ToList();var colors=source.Colors.ToList();var geometry=StudioSubdivisionData.Geometry(node).ToList();
            var uv=new List<Vector4>[8];for(int c=0;c<8;c++)uv[c]=source.UV[c]?.ToList()??new List<Vector4>();
            var groups=node.normalGroups!=null&&node.normalGroups.Length==Positions.Length?node.normalGroups.ToList():new List<int>();
            if(groups.Count==0)
            {
                var ids=new Dictionary<(int,Vector3),int>();
                for(int i=0;i<Positions.Length;i++){var key=(geometry[i],normals.Count>i?normals[i]:Vector3.zero);if(!ids.TryGetValue(key,out int g))ids[key]=g=ids.Count;groups.Add(g);}
            }
            int nextGeometry=geometry.Count==0?0:geometry.Max()+1,nextGroup=groups.Count==0?0:groups.Max()+1;
            var centers=new List<int>();var polygons=new List<int>();var indices=Enumerable.Range(0,mesh.subMeshCount).Select(_=>new List<int>()).ToArray();var captured=BevelSourceTriangles(mesh);
            for(int f=0;f<Faces.Count;f++)
            {
                var face=Faces[f];var ring=face.Vertices;
                if(!chosen.Contains(f)){polygons.AddRange(new[]{face.Submesh,ring.Count}.Concat(ring));indices[face.Submesh].AddRange(captured[f]);continue;}
                int center=points.Count;centers.Add(center);float weight=1f/ring.Count;
                Vector3 position=Vector3.zero,normal=Vector3.zero;Vector4 tangent=Vector4.zero;Color color=Color.clear;
                foreach(int v in ring){position+=Positions[v]*weight;if(normals.Count>0)normal+=normals[v]*weight;if(tangents.Count>0)tangent+=tangents[v]*weight;if(colors.Count>0)color+=colors[v]*weight;}
                if(!BevelFinite(position))throw new InvalidOperationException("Poke produced a non-finite center.");
                points.Add(position);geometry.Add(nextGeometry++);groups.Add(nextGroup++);
                if(normals.Count>0)normals.Add(normal.normalized);
                if(tangents.Count>0){var xyz=new Vector3(tangent.x,tangent.y,tangent.z).normalized;tangents.Add(new Vector4(xyz.x,xyz.y,xyz.z,tangent.w<0?-1:1));}
                if(colors.Count>0)colors.Add(color);
                for(int c=0;c<8;c++)if(uv[c].Count>0){Vector4 value=Vector4.zero;foreach(int v in ring)value+=uv[c][v]*weight;uv[c].Add(value);}
                for(int i=0;i<ring.Count;i++)
                {int a=ring[i],b=ring[(i+1)%ring.Count];indices[face.Submesh].AddRange(new[]{a,b,center});polygons.AddRange(new[]{face.Submesh,3,a,b,center});}
            }
            var format=mesh.indexFormat==IndexFormat.UInt32||points.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;
            mesh.Clear();mesh.indexFormat=format;mesh.SetVertices(points);if(normals.Count>0)mesh.SetNormals(normals);if(tangents.Count>0)mesh.SetTangents(tangents);if(colors.Count>0)mesh.SetColors(colors);
            for(int c=0;c<8;c++)if(uv[c].Count>0)
            {int dim=source.Dimensions[c];if(dim==1)StudioSubdivisionData.SetUV1(mesh,c,uv[c]);else if(dim==2)mesh.SetUVs(c,uv[c].Select(v=>new Vector2(v.x,v.y)).ToList());else if(dim==3)mesh.SetUVs(c,uv[c].Select(v=>new Vector3(v.x,v.y,v.z)).ToList());else mesh.SetUVs(c,uv[c]);}
            mesh.subMeshCount=indices.Length;for(int s=0;s<indices.Length;s++)mesh.SetTriangles(indices[s],s,false);mesh.RecalculateBounds();
            node.cutPolygons=polygons.ToArray();node.cutTriangles=mesh.triangles;node.geometryVertices=geometry.ToArray();node.normalGroups=groups.ToArray();return centers.ToArray();
        }
    }
}
