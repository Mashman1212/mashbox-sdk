using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        internal Dictionary<int,int> FlipNormals(StudioModelDocument.Node node,IEnumerable<int> selection)
        {
            var mesh=node.mesh;
            if(!mesh||!mesh.isReadable||node.skinned||node.blendShape!=null||mesh.blendShapeCount>0||mesh.bindposes.Length>0||mesh.boneWeights.Length>0)
                throw new InvalidOperationException("Flip Normals requires readable static meshes without skinning or blend shapes.");
            var chosen=new HashSet<int>(selection??Enumerable.Range(0,Faces.Count));
            if(chosen.Count==0||chosen.Any(f=>f<0||f>=Faces.Count))throw new InvalidOperationException("Select valid faces or incident edges or vertices to flip.");
            var captured=BevelSourceTriangles(mesh);var selected=new HashSet<int>();var retained=new HashSet<int>();
            var reverse=new Dictionary<(int sub,int a,int b,int c),int>();
            for(int f=0;f<Faces.Count;f++)
            {
                var used=chosen.Contains(f)?selected:retained;used.UnionWith(Faces[f].Vertices);used.UnionWith(captured[f]);
                if(!chosen.Contains(f))continue;
                for(int t=0;t<captured[f].Count;t+=3){var k=(Faces[f].Submesh,captured[f][t],captured[f][t+1],captured[f][t+2]);reverse.TryGetValue(k,out int n);reverse[k]=n+1;}
            }
            if(selected.Any(i=>i<0||i>=Positions.Length||!BevelFinite(Positions[i])))throw new InvalidOperationException("Flip Normals requires valid finite face geometry.");
            var source=BevelSource(mesh);var points=Positions.ToList();var normals=source.RetainedNormals.ToList();var tangents=source.Tangents.ToList();var colors=source.Colors.ToList();
            var uv=new List<Vector4>[8];for(int c=0;c<8;c++)uv[c]=source.UV[c]?.ToList()??new List<Vector4>();
            var geometry=StudioSubdivisionData.Geometry(node).ToList();var groups=node.normalGroups!=null&&node.normalGroups.Length==Positions.Length?node.normalGroups.ToList():new List<int>();
            if(groups.Count==0){var ids=new Dictionary<(int,Vector3),int>();for(int i=0;i<Positions.Length;i++){var key=(geometry[i],normals[i]);if(!ids.TryGetValue(key,out int g))ids[key]=g=ids.Count;groups.Add(g);}}
            var retainedGroups=new HashSet<int>(retained.Select(i=>groups[i]));var flippedGroups=new Dictionary<int,int>();int nextGroup=groups.Count==0?0:groups.Max()+1;
            var remap=new Dictionary<int,int>();
            foreach(int old in selected.OrderBy(i=>i))
            {
                int id=old;
                if(retained.Contains(old))
                {
                    id=points.Count;points.Add(points[old]);normals.Add(normals[old]);geometry.Add(geometry[old]);groups.Add(groups[old]);
                    if(tangents.Count>0)tangents.Add(tangents[old]);if(colors.Count>0)colors.Add(colors[old]);for(int c=0;c<8;c++)if(uv[c].Count>0)uv[c].Add(uv[c][old]);
                }
                int group=groups[old];
                if(retainedGroups.Contains(group)){if(!flippedGroups.TryGetValue(group,out int replacement))flippedGroups[group]=replacement=nextGroup++;groups[id]=replacement;}
                normals[id]=-normals[id];if(tangents.Count>0){var tangent=tangents[id];tangent.w=-tangent.w;tangents[id]=tangent;}remap.Add(old,id);
            }
            int Map(int v)=>remap.TryGetValue(v,out int mapped)?mapped:v;
            var polygons=new List<int>();
            for(int f=0;f<Faces.Count;f++){var face=Faces[f];polygons.Add(face.Submesh);polygons.Add(face.Vertices.Count);polygons.AddRange(chosen.Contains(f)?face.Vertices.Select(Map).Reverse():face.Vertices);}
            var indices=Enumerable.Range(0,mesh.subMeshCount).Select(s=>mesh.GetTriangles(s)).ToArray();
            for(int sub=0;sub<indices.Length;sub++)for(int t=0;t<indices[sub].Length;t+=3)
            {
                var data=indices[sub];var key=(sub,data[t],data[t+1],data[t+2]);if(!reverse.TryGetValue(key,out int count)||count==0)continue;
                reverse[key]=count-1;int a=Map(data[t]),b=Map(data[t+1]),c=Map(data[t+2]);data[t]=a;data[t+1]=c;data[t+2]=b;
            }
            if(reverse.Values.Any(n=>n!=0))throw new InvalidOperationException("The source face tessellation changed. Select the faces again.");
            var format=mesh.indexFormat==IndexFormat.UInt32||points.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;
            mesh.Clear();mesh.indexFormat=format;mesh.SetVertices(points);mesh.SetNormals(normals);if(tangents.Count>0)mesh.SetTangents(tangents);if(colors.Count>0)mesh.SetColors(colors);
            for(int c=0;c<8;c++)if(uv[c].Count>0){int dimension=source.Dimensions[c];if(dimension==1)StudioSubdivisionData.SetUV1(mesh,c,uv[c]);else if(dimension==2)mesh.SetUVs(c,uv[c].Select(v=>new Vector2(v.x,v.y)).ToList());else if(dimension==3)mesh.SetUVs(c,uv[c].Select(v=>new Vector3(v.x,v.y,v.z)).ToList());else mesh.SetUVs(c,uv[c]);}
            mesh.subMeshCount=indices.Length;for(int s=0;s<indices.Length;s++)mesh.SetTriangles(indices[s],s,false);mesh.RecalculateBounds();
            node.cutPolygons=polygons.ToArray();node.cutTriangles=mesh.triangles;node.geometryVertices=geometry.ToArray();node.normalGroups=groups.ToArray();return remap;
        }
    }
}
