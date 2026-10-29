using System;
using System.Collections.Generic;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio {
internal sealed partial class StudioRetopologyEditor {
 private void OpenRetopologyUVEditor(){
 var part=Active;int revision=part.Revision;var copy=StudioRetopologyDocument.BuildMesh(part);
 StudioUVEditor.OpenRetopology(copy,edited=>{
  if(!Document||Active!=part||part.Revision!=revision)throw new InvalidOperationException("Retopology changed. Reopen the UV editor to edit the current mesh.");
  var corners=StudioRetopologyUVEditing.ReadCorners(part,edited);
  ChangeCore("Edit retopology UVs",()=>{for(int i=0;i<part.faces.Count;i++)part.faces[i].cornerUV=corners[i];part.needsUV=false;},true);
  revision=part.Revision;status="UV0 updated. Editable topology remains welded.";
 });
 }
}
internal static class StudioRetopologyUVEditing {
 internal static Vector2[][] ReadCorners(StudioRetopologyDocument.Part part,Mesh mesh){
 var positions=mesh.vertices;var uv=mesh.uv;
 if(uv.Length!=positions.Length)throw new InvalidOperationException("UV0 is empty. Create or unwrap UV0 before applying.");
 var result=new Vector2[part.faces.Count][];var assigned=new bool[part.faces.Count][];
 for(int i=0;i<part.faces.Count;i++){result[i]=new Vector2[part.faces[i].vertices.Length];assigned[i]=new bool[result[i].Length];}
 for(int sub=0;sub<mesh.subMeshCount;sub++){
 var triangles=mesh.GetTriangles(sub);int cursor=0;
 for(int f=0;f<part.faces.Count;f++){
 var face=part.faces[f];if(face.material!=sub)continue;
 for(int t=1;t<face.vertices.Length-1;t++)foreach(int corner in new[]{0,t,t+1}){
 if(cursor>=triangles.Length)throw new InvalidOperationException("UV mesh topology changed. Reopen the editor.");
 int v=triangles[cursor++];if((positions[v]-part.vertices[face.vertices[corner]]).sqrMagnitude>1e-12f)throw new InvalidOperationException("UV mesh geometry changed. Reopen the editor.");
 var value=uv[v];if(float.IsNaN(value.x)||float.IsInfinity(value.x)||float.IsNaN(value.y)||float.IsInfinity(value.y))throw new InvalidOperationException("UV coordinates are invalid.");
 if(assigned[f][corner]&&(result[f][corner]-value).sqrMagnitude>1e-10f)throw new InvalidOperationException("An unwrap seam crosses inside a quad. Increase the unwrap angle or use reference UV transfer, then retry.");
 result[f][corner]=value;assigned[f][corner]=true;
 }
 }
 if(cursor!=triangles.Length)throw new InvalidOperationException("UV triangle count changed. Reopen the editor.");
 }
 for(int f=0;f<assigned.Length;f++)foreach(bool valid in assigned[f])if(!valid)throw new InvalidOperationException("UV mesh is missing faces.");
 return result;
 }
}
}
