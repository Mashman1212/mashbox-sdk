using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;
namespace MashBoxSDK.AnimationStudio {
internal static class StudioRetopologyExtrude {
 internal static string Issue(Part p,HashSet<(int,int)> selection) {
  if(selection.Count==0)return "Select an open border edge or loop to extrude.";
  var edges=StudioRetopologyOperations.Edges(p);var degree=new Dictionary<int,int>();
  foreach(var e in selection){if(!edges.TryGetValue(e,out var faces)||faces.Count!=1)return "Extrude requires open border edges. Interior edges cannot be extruded.";
   foreach(int v in new[]{e.Item1,e.Item2}){degree.TryGetValue(v,out int count);degree[v]=count+1;if(count>=2)return "Select non-branching border chains or loops.";}}
  return null;
 }
 internal static HashSet<(int,int)> Apply(Part p,HashSet<(int,int)> selection,Vector3 delta,StudioRetopologySurface surface,float offset) {
  string issue=Issue(p,selection);if(issue!=null)throw new InvalidOperationException(issue);
  if(float.IsNaN(delta.sqrMagnitude)||float.IsInfinity(delta.sqrMagnitude)||delta.sqrMagnitude<1e-12f)throw new InvalidOperationException("Choose a nonzero extrusion distance.");
  var edges=StudioRetopologyOperations.Edges(p);var points=new List<Vector3>(p.vertices);var map=new Dictionary<int,int>();
  foreach(int v in selection.SelectMany(e=>new[]{e.Item1,e.Item2}).Distinct().OrderBy(v=>v)){
   map[v]=points.Count;var point=p.vertices[v]+delta;points.Add(surface==null?point:surface.Project(point,offset));}
  var faces=new List<Face>();var result=new HashSet<(int,int)>();
  foreach(var edge in selection.OrderBy(e=>e.Item1).ThenBy(e=>e.Item2)){
   var face=p.faces[edges[edge][0]];int a=-1,b=-1;
   for(int i=0;i<face.vertices.Length;i++){int x=face.vertices[i],y=face.vertices[(i+1)%face.vertices.Length];if(StudioRetopologyOperations.Edge(x,y)==edge){a=x;b=y;break;}}
   int[] q={b,a,map[a],map[b]};
   var n1=Vector3.Cross(points[q[1]]-points[q[0]],points[q[2]]-points[q[0]]);
   var n2=Vector3.Cross(points[q[2]]-points[q[0]],points[q[3]]-points[q[0]]);
   if(n1.sqrMagnitude<1e-16f||n2.sqrMagnitude<1e-16f||Vector3.Dot(n1,n2)<=0)throw new InvalidOperationException("Extrusion collapsed on the surface. Adjust the direction or distance, or turn off surface snapping.");
   faces.Add(new Face(q,face.material));result.Add(StudioRetopologyOperations.Edge(map[a],map[b]));
  }
  while(p.uv.Count<p.vertices.Count)p.uv.Add(Vector2.zero);
  foreach(var v in map.OrderBy(e=>e.Value))p.uv.Add(p.uv[v.Key]);
  p.vertices=points;p.faces.AddRange(faces);p.needsUV=true;return result;
 }
}
}
