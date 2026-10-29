using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;
namespace MashBoxSDK.AnimationStudio {
public static class StudioQuadCutValidation {
 public static void Run(){var log=new List<string>();
 void Check(bool ok,string label){if(!ok)throw new Exception(label);log.Add("PASS "+label);}
 Part Make()=>new Part{vertices=new List<Vector3>{Vector3.zero,Vector3.right,new Vector3(1,1,0),Vector3.up,new Vector3(2,0,0),new Vector3(2,1,0)},uv=new List<Vector2>{Vector2.zero,Vector2.right,Vector2.one,Vector2.up,Vector2.right*2,new Vector2(2,1)},faces=new List<Face>{new Face(new[]{0,1,2,3}),new Face(new[]{1,4,5,2},1){cornerUV=new[]{new Vector2(10,0),new Vector2(11,0),new Vector2(11,1),new Vector2(10,1)}}}};
 try{
 var p=Make();var a=new Vector3(0,.5f,0);var b=new Vector3(1,.5f,0);
 Check(StudioRetopologyQuadCut.FindFace(p,a,b)==0,"Opposite edges identify the existing face");
 StudioRetopologyQuadCut.Apply(p,a,b);
 Check(p.faces.Count==3&&p.faces[0].vertices.Length==4&&p.faces[2].vertices.Length==4,"Edge-to-edge cut creates two quads");
 int shared=p.vertices.FindIndex(v=>v==b);
 Check(p.faces.All(f=>f.vertices.Contains(shared)),"Both new quads and adjacent face share one cut vertex");
 Check(p.faces[1].vertices.Length==5&&!StudioRetopologyOperations.Edges(p).ContainsKey((1,2)),"Adjacent edge split removes the old unsplit edge");
 int corner=Array.IndexOf(p.faces[1].vertices,shared);
 Check((p.faces[1].cornerUV[corner]-new Vector2(10,.5f)).sqrMagnitude<1e-10f,"Neighbor UV seam interpolated independently");
 Check(p.faces[0].material==0&&p.faces[1].material==1&&p.faces[2].material==0,"Material assignments retained");
 p=Make();StudioRetopologyQuadCut.Apply(p,Vector3.zero,new Vector3(1,.5f,0));
 Check(p.faces[0].vertices.Length==3&&p.faces[2].vertices.Length==4,"Vertex-to-edge cut produces triangle and quad without duplicate endpoint");
 Check(p.vertices.Count==7,"Existing start vertex reused");
 p=Make();Check(StudioRetopologyQuadCut.FindFace(p,Vector3.zero,Vector3.right)<0,"Existing edge is not cut again");
 Check(StudioRetopologyQuadCut.FindFace(p,new Vector3(0,.5f,0),new Vector3(2,.5f,0))<0,"Cross-face path is not accepted as a single-face cut");
 StudioRetopologyQuadCut.InsertBoundaryPoints(p,new[]{new Vector3(1,.5f,0)});
 Check(p.vertices.Count==7&&p.faces.All(f=>f.vertices.Contains(6)),"New quad attachment splits both sides of a shared edge");
 log.Add("SUCCESS");
 }catch(Exception e){log.Add("FAIL "+e);}finally{File.WriteAllLines("Library/quad-cut-validation.txt",log);}
 }
}
}
