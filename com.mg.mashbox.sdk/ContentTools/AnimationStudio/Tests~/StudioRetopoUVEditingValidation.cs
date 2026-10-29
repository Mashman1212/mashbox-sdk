using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;
namespace MashBoxSDK.AnimationStudio {
public static class StudioRetopoUVEditingValidation {
 public static void Run(){var log=new List<string>();Mesh mesh=null;
 void Check(bool ok,string label){if(!ok)throw new Exception(label);log.Add("PASS "+label);}
 try{
 var p=new Part{vertices=new List<Vector3>{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(2,0,0),new Vector3(0,1,0),new Vector3(1,1,0),new Vector3(2,1,0)},uv=new List<Vector2>{Vector2.zero,Vector2.right,Vector2.right*2,Vector2.up,Vector2.one,new Vector2(2,1)},faces=new List<Face>{new Face(new[]{0,1,4,3}),new Face(new[]{1,2,5,4})}};
 mesh=StudioRetopologyDocument.BuildMesh(p);var original=p.vertices.ToArray();var triangles=mesh.triangles;
 mesh.uv=mesh.uv.Select(v=>v*.25f+Vector2.one*.1f).ToArray();var corners=StudioRetopologyUVEditing.ReadCorners(p,mesh);
 Check(corners[0][0]==Vector2.one*.1f&&corners[1][1]==new Vector2(.6f,.1f),"UV edits transfer to correct polygon corners");
 for(int i=0;i<p.faces.Count;i++)p.faces[i].cornerUV=corners[i];
 Check(p.vertices.SequenceEqual(original)&&p.faces.All(f=>f.vertices.Length==4),"Applying UVs retains welded quad topology");
 UnwrapParam.SetDefaults(out var options);var generated=Unwrapping.GeneratePerTriangleUV(mesh,options);StudioUVTools.ApplyCornerUVs(mesh,0,generated);
 corners=StudioRetopologyUVEditing.ReadCorners(p,mesh);Check(corners.Length==2&&corners.All(c=>c.Length==4),"Automatic unwrap maps back to quads");
 var points=mesh.vertices;points[0]+=Vector3.forward;mesh.vertices=points;bool rejected=false;
 try{StudioRetopologyUVEditing.ReadCorners(p,mesh);}catch(InvalidOperationException){rejected=true;}
 Check(rejected,"Changed geometry rejected instead of overwriting retopology");
 var weights=Enumerable.Range(0,p.vertices.Count).ToDictionary(i=>i,i=>1f);StudioRetopologyRelaxBrush.Apply(p,weights,1,true,null,0);
 Check(p.vertices.SequenceEqual(original),"Brush leaves every open-border vertex fixed");
 StudioRetopologyOperations.Relax(p,null,true,0,new HashSet<int>());
 Check(p.vertices.SequenceEqual(original),"Whole-mesh Relax leaves open borders fixed");
 log.Add("SUCCESS");
 }catch(Exception e){log.Add("FAIL "+e);}finally{if(mesh)UnityEngine.Object.DestroyImmediate(mesh);File.WriteAllLines("Library/retopo-uv-editing-validation.txt",log);}
 }
}
}
