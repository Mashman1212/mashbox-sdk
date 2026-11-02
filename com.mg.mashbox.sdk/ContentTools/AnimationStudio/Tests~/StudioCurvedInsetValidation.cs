using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEditor;using UnityEngine;using Object=UnityEngine.Object;using MashBoxSDK.AnimationStudio;
internal static class StudioCurvedInsetValidation
{
 static int checks;static readonly List<Mesh> meshes=new List<Mesh>();
 static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+name));
 static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;}
 [InitializeOnLoadMethod]static void Request(){if(Application.dataPath.EndsWith("SelectionUndoProject/Assets")&&File.Exists(Out("curved-inset-request.txt")))EditorApplication.delayCall+=Run;}
 static StudioModelDocument.Node Band(bool seam)
 {
  const int segments=16;int stride=seam?17:16;var positions=new List<Vector3>();var uv=new List<Vector2>();var faces=new List<int>();var triangles=new List<int>();
  for(int row=0;row<4;row++)for(int i=0;i<stride;i++){float a=(i%segments)*Mathf.PI*2/segments;positions.Add(new Vector3(Mathf.Cos(a)*(1+row*.1f),Mathf.Sin(a)*(1+row*.1f),row));uv.Add(new Vector2((float)i/segments,row));}
  for(int row=0;row<3;row++)for(int i=0;i<segments;i++){int next=seam?i+1:(i+1)%segments;int a=row*stride+i,b=row*stride+next,c=(row+1)*stride+next,d=(row+1)*stride+i;faces.AddRange(new[]{0,4,a,b,c,d});triangles.AddRange(new[]{a,b,c,a,c,d});}
  var mesh=new Mesh{name="Curved inset band"};meshes.Add(mesh);mesh.vertices=positions.ToArray();mesh.triangles=triangles.ToArray();mesh.uv=uv.ToArray();mesh.colors=Enumerable.Repeat(Color.cyan,positions.Count).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
  return new StudioModelDocument.Node{name="Curved band",mesh=mesh,cutPolygons=faces.ToArray(),cutTriangles=mesh.triangles};
 }
 static void BandCase(bool seam,float height,float inset)
 {
  var n=Band(seam);var original=n.mesh.vertices;var cuts=new StudioMeshCuts(n);var result=cuts.Extrude(n,Enumerable.Range(16,16).ToArray(),null,height,true,true,inset:inset);
  Check(result.Faces.Length==16,"cap remains one connected sixteen-quad strip");Check(n.mesh.vertices.All(v=>!float.IsNaN(v.sqrMagnitude)&&!float.IsInfinity(v.sqrMagnitude)),"finite positions");
  foreach(int i in result.MovingVertices){var p=result.SourceVertices[i];var direction=result.InsetDirections[i];Check(Mathf.Abs(direction.z)>.9f,"boundary moves along the strip");Check(p.z<1.5f?direction.z<0:direction.z>0,"positive inset remains outward");Check(Mathf.Abs(direction.z)<1.1f,"surface offset is bounded");var expected=p+direction*inset+result.NormalDirections[i]*height;Check((n.mesh.vertices[i]-expected).sqrMagnitude<1e-9f,"captured source displacement");}
  if(seam){var byPosition=result.MovingVertices.GroupBy(i=>result.SourceVertices[i]);foreach(var group in byPosition){var first=n.mesh.vertices[group.First()];foreach(int i in group)Check((n.mesh.vertices[i]-first).sqrMagnitude<1e-10f,"render seam stays joined");}}
  foreach(var face in new StudioMeshCuts(n).Faces)Check(face.Vertices.Count==4,"authored quad preserved");
  Check(n.mesh.colors.All(c=>c==Color.cyan),"colors retained");
 }
 static void OverInset()
 {
  var n=Band(true);var vertices=n.mesh.vertices;var triangles=n.mesh.triangles;var faces=n.cutPolygons.ToArray();bool rejected=false;
  try{new StudioMeshCuts(n).Extrude(n,Enumerable.Range(16,16).ToArray(),null,0,true,true,inset:-.7f);}catch(InvalidOperationException){rejected=true;}
  Check(rejected,"strip collapse rejected");Check(n.mesh.vertices.SequenceEqual(vertices)&&n.mesh.triangles.SequenceEqual(triangles)&&n.cutPolygons.SequenceEqual(faces),"failed inset is atomic");
 }
 static void Planar()
 {
  var m=new Mesh();meshes.Add(m);m.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};m.triangles=new[]{0,1,2,0,2,3};m.RecalculateNormals();var n=new StudioModelDocument.Node{mesh=m,cutPolygons=new[]{0,4,0,1,2,3},cutTriangles=m.triangles};var r=new StudioMeshCuts(n).Extrude(n,new[]{0},null,0,true,true,inset:-.1f);foreach(int i in r.MovingVertices)Check(Mathf.Abs(Mathf.Abs(m.vertices[i].x)-.9f)<1e-4f&&Mathf.Abs(Mathf.Abs(m.vertices[i].y)-.9f)<1e-4f,"planar kernel unchanged");
 }
 internal static void Run()
 {
  if(!Application.dataPath.EndsWith("SelectionUndoProject/Assets"))throw new InvalidOperationException("Disposable project only");
  File.Delete(Out("curved-inset-request.txt"));checks=0;try{BandCase(false,0,-.1f);BandCase(true,0,-.1f);BandCase(true,.2f,-.1f);BandCase(true,0,.1f);OverInset();Planar();File.WriteAllText(Out("curved-inset-result.txt"),"PASS "+checks+" focused curved inset checks");}catch(Exception e){File.WriteAllText(Out("curved-inset-result.txt"),e.ToString());}finally{foreach(var mesh in meshes)if(mesh)Object.DestroyImmediate(mesh);meshes.Clear();}
 }
}
