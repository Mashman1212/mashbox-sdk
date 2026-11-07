using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        internal int[] PunchHole(StudioModelDocument.Node node,int entry,Vector3 center,Matrix4x4 world,float radius,int sides,float depth,bool through,bool clean,float rotation=0,Vector3 worldNormal=default,bool limitThroughDepth=false)
        {
            if(entry<0||entry>=Faces.Count||radius<=0||depth<=0||sides<3||sides>128||!BevelFinite(new Vector3(radius,depth,rotation))||Mathf.Abs(world.determinant)<1e-12f)throw new InvalidOperationException("Use finite positive Radius/Depth, 3–128 radial faces and a valid mesh transform.");
            var source=StudioMeshFracture.Capture(node);var inverse=world.inverse;source.Positions=source.Positions.Select(world.MultiplyPoint3x4).ToArray();source.Normals=source.Normals.Select(n=>inverse.transpose.MultiplyVector(n).normalized).ToArray();
            source.Tangents=source.Tangents.Select(t=>{var d=world.MultiplyVector(new Vector3(t.x,t.y,t.z)).normalized;return new Vector4(d.x,d.y,d.z,t.w);}).ToArray();
            var normal=worldNormal.sqrMagnitude>.5f?worldNormal.normalized:BevelPolygonNormal(Faces[entry].Vertices,source.Positions);var axis=Vector3.Cross(Mathf.Abs(normal.y)<.9f?Vector3.up:Vector3.right,normal).normalized;
            var result=StudioMeshFracture.Punch(source,center,normal,axis,radius,sides,depth,through,rotation%360,clean,out var affected,out var geometry,limitThroughDepth);
            var output=StudioMeshFracture.Publish(result,result.Pieces[0]);output.geometryVertices=result.Pieces[0].Faces.SelectMany(f=>f.Vertices.Select(v=>geometry[v])).ToArray();
            try{
                var mesh=output.mesh;mesh.vertices=mesh.vertices.Select(inverse.MultiplyPoint3x4).ToArray();mesh.normals=mesh.normals.Select(n=>world.transpose.MultiplyVector(n).normalized).ToArray();mesh.tangents=mesh.tangents.Select(t=>{var d=inverse.MultiplyVector(new Vector3(t.x,t.y,t.z)).normalized;return new Vector4(d.x,d.y,d.z,t.w);}).ToArray();mesh.RecalculateBounds();
                int material=Faces[entry].Submesh,last=mesh.subMeshCount-1;var walls=mesh.GetTriangles(last);mesh.SetTriangles(mesh.GetTriangles(material).Concat(walls).ToArray(),material);mesh.subMeshCount=last;
                for(int i=0;i<output.cutPolygons.Length;){if(output.cutPolygons[i]==last)output.cutPolygons[i]=material;i+=2+output.cutPolygons[i+1];}output.cutTriangles=mesh.triangles;
                StudioMeshToolPreview.CopyMesh(mesh,node.mesh);node.cutPolygons=output.cutPolygons;node.cutTriangles=output.cutTriangles;node.geometryVertices=output.geometryVertices;node.normalGroups=output.normalGroups;
            }finally{UnityEngine.Object.DestroyImmediate(output.mesh);}return affected;
        }
    }
}

