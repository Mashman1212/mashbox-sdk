using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;
namespace MashBoxSDK.AnimationStudio {
public static class StudioQuadCleanupValidation {
 public static void Run() {
 var log=new List<string>();
 void Check(bool value,string label){if(!value)throw new Exception(label);log.Add("PASS "+label);}
 Part Hex()=>new Part {vertices=new List<Vector3>{new Vector3(0,0,0),new Vector3(1,-.1f,0),new Vector3(2,0,0),new Vector3(2,1,0),new Vector3(1,1.1f,0),new Vector3(0,1,0)},faces=new List<Face>{new Face(new[]{0,1,2,3}),new Face(new[]{3,4,5,0})}};
 Dictionary<int,float> Weights(Part p)=>Enumerable.Range(0,p.vertices.Count).ToDictionary(i=>i,i=>1f);
 HashSet<(int,int)> Border(Part p)=>StudioRetopologyOperations.Edges(p).Where(e=>e.Value.Count==1).Select(e=>e.Key).ToHashSet();
 try {
 var p=Hex();var old=p.vertices.ToArray();var border=Border(p);var w=Weights(p);
 Check(StudioRetopologyQuadCleanup.Apply(p,new HashSet<int>{0,1},w)>0,"Distorted quad pair improves connectivity");
 Check(StudioRetopologyOperations.Edges(p).ContainsKey((1,4)),"Quad edges align into two regular columns");
 Check(border.SetEquals(Border(p)),"Exact perimeter edges retained");
 Check(old.SequenceEqual(p.vertices)&&w.Count==0,"Boundary positions protected from relaxation");
 Check(p.faces.All(f=>f.vertices.Length==4)&&p.vertices.Count==6,"No extra vertices or triangles introduced");
 Check(StudioRetopologyQuadCleanup.Apply(p,new HashSet<int>{0,1},Weights(p))==0,"Regular pair does not keep flipping");
 p=Hex();p.faces=new List<Face>{new Face(new[]{0,1,4}),new Face(new[]{4,5,0})};
 Check(StudioRetopologyQuadCleanup.Apply(p,new HashSet<int>{0,1},Weights(p))==1&&p.faces.Count==1,"Suitable triangles merge into a quad");
 p=Hex();p.faces[0].cornerUV=new Vector2[4];
 Check(StudioRetopologyQuadCleanup.Apply(p,new HashSet<int>{0,1},Weights(p))==0,"Authored UV faces preserved");
 p=Hex();p.faces[1].material=1;
 Check(StudioRetopologyQuadCleanup.Apply(p,new HashSet<int>{0,1},Weights(p))==0,"Material boundaries preserved");
 p=Hex();w=Weights(p);w.Remove(5);
 Check(StudioRetopologyQuadCleanup.Apply(p,new HashSet<int>{0,1},w)==0,"Partial faces outside brush are untouched");
 p=Hex();p.vertices.Add(new Vector3(1,0,1));p.faces.Add(new Face(new[]{1,4,6}));
 StudioRetopologyQuadCleanup.Apply(p,new HashSet<int>{0,1},Weights(p));
 Check(StudioRetopologyOperations.Edges(p)[(1,4)].Count==1,"No new connection duplicates an outside edge");
 Check(StudioRetopologyOperations.Edges(p).All(e=>e.Value.Count<=2),"Cleanup stays manifold");
 log.Add("SUCCESS");
 } catch(Exception e){log.Add("FAIL "+e);} File.WriteAllLines("Library/quad-cleanup-validation.txt",log);
 }
}
}

