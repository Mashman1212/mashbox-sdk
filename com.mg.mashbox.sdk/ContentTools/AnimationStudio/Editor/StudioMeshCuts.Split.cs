using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        // Partition by authored faces while reusing their exact render diagonals.
        // Each side owns its boundary render vertices; UV/normal seams stay split.
        internal void ValidateSplitSource(StudioModelDocument.Node node)
        {
            if (!node.mesh || !node.mesh.isReadable || node.skinned || node.blendShape != null || node.mesh.blendShapeCount > 0)
                throw new InvalidOperationException("Split Selection requires a readable static mesh without skinning or blend shapes.");
            for (int s = 0; s < node.mesh.subMeshCount; s++)
                if (node.mesh.GetTopology(s) != MeshTopology.Triangles)
                    throw new InvalidOperationException("Split Selection requires Studio polygon surfaces with a triangle render buffer.");
            var triangles = BevelSourceTriangles(node.mesh);
            for (int f = 0; f < Faces.Count; f++)
                if (triangles[f].Count != (Faces[f].Vertices.Count - 2) * 3)
                    throw new InvalidOperationException("A face has no matching render triangulation; splitting was not applied.");
            if (triangles.Sum(t => t.Count) != node.mesh.triangles.Length)
                throw new InvalidOperationException("The face boundaries do not cover the render mesh; splitting was not applied.");
        }

        internal StudioModelDocument.Node ExtractSplitFaces(StudioModelDocument.Node node, HashSet<int> included, bool keepLooseVertices)
        {
            var source = BevelSource(node.mesh); var originalTriangles = BevelSourceTriangles(node.mesh);
            var faces = Enumerable.Range(0, Faces.Count).Where(included.Contains).ToArray();
            var usedSet = new HashSet<int>(faces.SelectMany(f => Faces[f].Vertices));
            if (keepLooseVertices)
            {
                var surface = new HashSet<int>(Faces.SelectMany(f => f.Vertices));
                for (int v = 0; v < Positions.Length; v++) if (!surface.Contains(v)) usedSet.Add(v);
            }
            var used = usedSet.OrderBy(v => v).ToArray();
            var remap = new int[Positions.Length]; for (int v = 0; v < used.Length; v++) remap[used[v]] = v;
            var triangles = Enumerable.Range(0, node.mesh.subMeshCount).Select(_ => new List<int>()).ToArray();
            foreach (int f in faces) triangles[Faces[f].Submesh].AddRange(originalTriangles[f].Select(v => remap[v]));
            var mesh = new Mesh { name = node.mesh.name, indexFormat = used.Length > 65535 || node.mesh.indexFormat == IndexFormat.UInt32 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            try
            {
                mesh.SetVertices(used.Select(v => Positions[v]).ToList());
                if (source.Normals.Length == Positions.Length) mesh.SetNormals(used.Select(v => source.Normals[v]).ToList());
                if (source.Tangents.Length == Positions.Length) mesh.SetTangents(used.Select(v => source.Tangents[v]).ToList());
                if (source.Colors.Length == Positions.Length) mesh.SetColors(used.Select(v => source.Colors[v]).ToList());
                for (int c = 0; c < 8; c++) if (source.UV[c] != null)
                {
                    var values = used.Select(v => source.UV[c][v]);
                    if (source.Dimensions[c] == 1) StudioSubdivisionData.SetUV1(mesh,c,values); else if (source.Dimensions[c] == 2) mesh.SetUVs(c, values.Select(v => new Vector2(v.x,v.y)).ToList());
                    else if (source.Dimensions[c] == 3) mesh.SetUVs(c, values.Select(v => new Vector3(v.x,v.y,v.z)).ToList());
                    else mesh.SetUVs(c, values.ToList());
                }
                mesh.subMeshCount = triangles.Length;
                for (int s = 0; s < triangles.Length; s++) mesh.SetTriangles(triangles[s],s,false);
                mesh.RecalculateBounds();
                var part = new StudioModelDocument.Node { name = node.name, parent = node.parent, siblingOrder = node.siblingOrder,
                    position = node.position, positionOffset = node.positionOffset, rotation = node.rotation, scale = node.scale,
                    pivotRotation = node.pivotRotation, active = node.active, sourcePath = node.sourcePath, anchorType = node.anchorType,
                    materials = node.materials?.ToArray() ?? Array.Empty<Material>(), previewTextureSlice = node.previewTextureSlice, vertexAOBaked = node.vertexAOBaked,
                    mesh = mesh, geometryVertices = GeometricIds(used.Select(v=>welded[v])), subdivision=node.subdivision?.Copy(), cutPolygons = faces.SelectMany(f => new[] { Faces[f].Submesh, Faces[f].Vertices.Count }.Concat(Faces[f].Vertices.Select(v => remap[v]))).ToArray(),
                    cutTriangles = mesh.triangles, normalGroups = node.normalGroups != null && node.normalGroups.Length == Positions.Length ? used.Select(v => node.normalGroups[v]).ToArray() : null };
                part.RotationChannels = node.RotationChannels; part.rotation = node.rotation;
                return part;
            }
            catch { UnityEngine.Object.DestroyImmediate(mesh); throw; }
        }
    }
}

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        // Copy the selected region as one island, retaining its internal seams and
        // exact render triangles. New geometric identities prevent positional welds.
        internal int[] CopyFaceRegion(StudioModelDocument.Node node, HashSet<int> selected, bool duplicate)
        {
            ValidateSplitSource(node);
            if (selected.Count == 0 || selected.Any(f => f < 0 || f >= Faces.Count))
                throw new InvalidOperationException("Select valid faces to duplicate or detach.");
            var source = BevelSource(node.mesh); var render = BevelSourceTriangles(node.mesh);
            var used = selected.SelectMany(f => Faces[f].Vertices).Distinct().OrderBy(v => v).ToArray();
            var surface = new HashSet<int>(Faces.SelectMany(f=>f.Vertices));
            var retained = new HashSet<int>(Faces.Where((f,i)=>!selected.Contains(i)).SelectMany(f=>f.Vertices));
            // Detach removes newly unused originals, but retains preexisting loose vertices.
            var originals = Enumerable.Range(0,Positions.Length).Where(v=>duplicate||retained.Contains(v)||!surface.Contains(v)).ToArray();
            var originalMap = originals.Select((v,i)=>(v,i)).ToDictionary(p=>p.v,p=>p.i);
            var map = used.Select((v,i) => (v,i)).ToDictionary(p => p.v,p => originals.Length+p.i);
            var indices = originals.Concat(used).ToArray();
            var mesh = new Mesh { name=node.mesh.name, indexFormat=indices.Length>65535?IndexFormat.UInt32:node.mesh.indexFormat };
            try
            {
                mesh.SetVertices(indices.Select(v=>Positions[v]).ToList());
                if(source.Normals.Length==Positions.Length)mesh.SetNormals(indices.Select(v=>source.Normals[v]).ToList());
                if(source.Tangents.Length==Positions.Length)mesh.SetTangents(indices.Select(v=>source.Tangents[v]).ToList());
                if(source.Colors.Length==Positions.Length)mesh.SetColors(indices.Select(v=>source.Colors[v]).ToList());
                for(int c=0;c<8;c++)if(source.UV[c]!=null)
                {
                    var values=indices.Select(v=>source.UV[c][v]).ToList();
                    if(source.Dimensions[c]==1)StudioSubdivisionData.SetUV1(mesh,c,values);
                    else if(source.Dimensions[c]==2)mesh.SetUVs(c,values.Select(v=>new Vector2(v.x,v.y)).ToList());
                    else if(source.Dimensions[c]==3)mesh.SetUVs(c,values.Select(v=>new Vector3(v.x,v.y,v.z)).ToList());
                    else mesh.SetUVs(c,values);
                }
                var ordered=selected.OrderBy(f=>f).ToArray();
                var faceIds=Enumerable.Range(0,Faces.Count).Concat(duplicate?ordered:Array.Empty<int>()).ToArray();
                var polygons=new List<int>();var triangles=Enumerable.Range(0,node.mesh.subMeshCount).Select(_=>new List<int>()).ToArray();
                for(int i=0;i<faceIds.Length;i++)
                {
                    int f=faceIds[i];bool copy=duplicate?i>=Faces.Count:selected.Contains(f);
                    int Remap(int v)=>copy?map[v]:originalMap[v];
                    polygons.Add(Faces[f].Submesh);polygons.Add(Faces[f].Vertices.Count);
                    polygons.AddRange(Faces[f].Vertices.Select(Remap));
                    triangles[Faces[f].Submesh].AddRange(render[f].Select(Remap));
                }
                mesh.subMeshCount=triangles.Length;
                for(int s=0;s<triangles.Length;s++)mesh.SetTriangles(triangles[s],s,false);
                mesh.RecalculateBounds();
                var originalIds=GeometricIds(Enumerable.Range(0,Positions.Length).Select(v=>welded[v]));
                int next=originalIds.Length==0?0:originalIds.Max()+1;
                var newIds=new Dictionary<int,int>();
                var geometry=originals.Select(v=>originalIds[v]).Concat(used.Select(v=> {int id=originalIds[v];if(!newIds.TryGetValue(id,out int n))newIds.Add(id,n=next++);return n;})).ToArray();
                int[] groups=null;
                if(node.normalGroups?.Length==Positions.Length)
                {
                    // Separate continuity identities without changing authored normals.
                    var labels=new Dictionary<int,int>();int offset=node.normalGroups.Distinct().Count();
                    groups=node.normalGroups.Select(g=> {if(!labels.TryGetValue(g,out int n))labels.Add(g,n=labels.Count);return n;}).ToArray();
                    groups=originals.Select(v=>groups[v]).Concat(used.Select(v=>groups[v]+offset)).ToArray();
                }
                StudioMeshToolPreview.CopyMesh(mesh,node.mesh);
                node.cutPolygons=polygons.ToArray();node.cutTriangles=node.mesh.triangles;
                node.geometryVertices=geometry;node.normalGroups=groups;
                return duplicate?Enumerable.Range(Faces.Count,ordered.Length).ToArray():ordered;
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }
}
