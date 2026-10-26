using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;
namespace MashBoxSDK.AnimationStudio {
public static class CrotchValidation {
public static void Run(){var log=new List<string>();int exit=1;
void Check(bool yes,string text){if(!yes)throw new Exception(text);log.Add("PASS "+text);}
try {
foreach(int hipCount in new[]{16,20,12}){
var p=new Part();var selected=new HashSet<int>();
void Tube(int count,float x,float rx,float rz,float low,float high,bool selectTop){
int start=p.vertices.Count;
for(int row=0;row<2;row++)for(int i=0;i<count;i++){float a=2*Mathf.PI*(i+.5f)/count;p.vertices.Add(new Vector3(x+Mathf.Cos(a)*rx,row==0?low:high,Mathf.Sin(a)*rz));p.uv.Add(new Vector2((float)i/count,row));if((row==1)==selectTop)selected.Add(start+row*count+i);}
for(int i=0;i<count;i++){int next=(i+1)%count;p.faces.Add(new Face(new[]{start+i,start+count+i,start+count+next,start+next},0));}}
Tube(hipCount,0,1.1f,.5f,2,3,false);Tube(8,-.57f,.46f,.4f,0,1.3f,true);Tube(8,.57f,.46f,.4f,0,1.3f,true);
var original=p.vertices.ToArray();var loops=StudioRetopologyLoops.Boundaries(p).Where(r=>r.All(selected.Contains)).ToArray();
var edges=new HashSet<(int,int)>(loops.SelectMany(r=>Enumerable.Range(0,r.Length).Select(i=>StudioRetopologyOperations.Edge(r[i],r[(i+1)%r.Length]))));
Check(StudioRetopologyCrotch.Validate(p,selected,edges,out _) == null,"Accept three complete borders "+hipCount);
var invalid=new HashSet<(int,int)>(edges);invalid.Remove(invalid.First());Check(StudioRetopologyCrotch.Validate(p,selected,invalid,out _)!=null,"Reject incomplete edge selection "+hipCount);
int oldFaces=p.faces.Count;StudioRetopologyCrotch.Build(p,selected,edges,3,null,0);
Check(original.SequenceEqual(p.vertices.Take(original.Length)),"Existing vertices unchanged "+hipCount);
Check(StudioRetopologyLoops.Boundaries(p).Count==3,"Only waist and ankle openings remain "+hipCount);
var topology=StudioRetopologyOperations.Edges(p);Check(topology.All(e=>e.Value.Count<=2)&&edges.All(e=>topology[e].Count==2),"All three seams connected without non-manifold edges "+hipCount);
foreach(var edge in topology.Where(e=>e.Value.Count==2)){
int Dir(Face f){var ids=f.vertices;for(int i=0;i<ids.Length;i++)if(ids[i]==edge.Key.Item1&&ids[(i+1)%ids.Length]==edge.Key.Item2)return 1;return -1;}
Check(Dir(p.faces[edge.Value[0]])!=Dir(p.faces[edge.Value[1]]),"Consistent shared edge winding");}
Check(p.faces.Skip(oldFaces).All(f=>f.vertices.Distinct().Count()==f.vertices.Length),"No repeated face vertices "+hipCount);
if(hipCount==16)Check(p.faces.Skip(oldFaces).All(f=>f.vertices.Length==4),"Matching counts produce only quads");
else Check(p.faces.Skip(oldFaces).Count(f=>f.vertices.Length==3)==Math.Abs(hipCount-16),"Only necessary count transitions use triangles "+hipCount);
}
log.Add("SUCCESS");exit=0;
}catch(Exception e){log.Add("FAIL "+e);}finally{File.WriteAllLines("crotch-results.txt",log);EditorApplication.Exit(exit);}
}
}}
