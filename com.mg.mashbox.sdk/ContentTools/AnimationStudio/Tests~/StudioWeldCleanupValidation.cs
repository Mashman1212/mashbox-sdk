using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;
namespace MashBoxSDK.AnimationStudio {
public static class StudioWeldCleanupValidation {
 public static void Run(){var log=new List<string>();
 void Check(bool ok,string label){if(!ok)throw new Exception(label);log.Add("PASS "+label);}
 Part Make()=>new Part{vertices=new List<Vector3>{Vector3.zero,Vector3.right,Vector3.one-Vector3.forward,Vector3.up,new Vector3(1.02f,0,0),new Vector3(2,0,0),new Vector3(2,1,0),new Vector3(1.02f,1,0)},uv=Enumerable.Range(0,8).Select(i=>new Vector2(i,0)).ToList(),faces=new List<Face>{new Face(new[]{0,1,2,3}),new Face(new[]{4,5,6,7},1)}};
 Dictionary<int,float> Weights(Part p)=>Enumerable.Range(0,p.vertices.Count).ToDictionary(i=>i,i=>1f);
 try{
 var p=Make();int n=StudioRetopologyWeldCleanup.Apply(p,Weights(p),.1f);
 Check(n==2&&p.vertices.Count==6,"Nearby disconnected seam welds two pairs");
 Check(p.faces[0].vertices.Intersect(p.faces[1].vertices).Count()==2,"Adjacent patches now share a complete edge");
 Check(p.faces[1].cornerUV[0]==new Vector2(4,0)&&p.faces[0].cornerUV[1]==new Vector2(1,0),"UV seam values retained independently");
 Check(p.faces.Count==2&&p.faces[1].material==1,"Faces and materials retained");
 Check(StudioRetopologyWeldCleanup.Apply(p,Weights(p),.1f)==0,"Repeated dab leaves already welded mesh unchanged");
 p=Make();Check(StudioRetopologyWeldCleanup.Apply(p,Weights(p),.001f)==0,"Outside tolerance does not weld");
 Check(StudioRetopologyWeldCleanup.Apply(p,new Dictionary<int,float>{{1,1},{2,1}},.1f)==0,"Vertices outside brush remain untouched");
 p=Make();p.faces.Add(new Face(new[]{0,1,2,3}));Check(StudioRetopologyWeldCleanup.Apply(p,Weights(p),.1f)==0,"Duplicate faces prevent an unsafe weld");
 log.Add("SUCCESS");
 }catch(Exception e){log.Add("FAIL "+e);}finally{File.WriteAllLines("Library/weld-cleanup-validation.txt",log);}
 }
}
}