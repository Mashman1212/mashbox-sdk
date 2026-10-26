using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;using System.Reflection;using UnityEditor;using UnityEngine;using MashBoxSDK.AnimationStudio;using Object=UnityEngine.Object;
public static class StudioBevelSubdivisionValidation
{
 static List<(int a,int b)> Edges(StudioMeshCuts t){var seen=new HashSet<(Vector3,Vector3)>();var result=new List<(int,int)>();foreach(var f in t.Faces)for(int i=0;i<f.Vertices.Count;i++){int a=f.Vertices[i],b=f.Vertices[(i+1)%f.Vertices.Count];if(seen.Add(StudioMeshCuts.NormalEdgePositionKeyForBevel(t.Positions[a],t.Positions[b])))result.Add((a,b));}return result;}
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static void ValidateMesh(StudioModelDocument.Node n)
 {
  var t=new StudioMeshCuts(n);var uses=new Dictionary<(Vector3,Vector3),(int count,Vector3 start)>();double volume=0;var referenced=new HashSet<int>();
  foreach(var face in t.Faces){typeof(StudioBevelCornerPlanValidation).GetMethod("SimpleProjection",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{t,face});var a=t.Positions[face.Vertices[0]];Vector3 area=Vector3.zero;for(int i=1;i+1<face.Vertices.Count;i++){var b=t.Positions[face.Vertices[i]];var c=t.Positions[face.Vertices[i+1]];area+=Vector3.Cross(b-a,c-a);volume+=Vector3.Dot(a,Vector3.Cross(b,c))/6d;}Check(area.sqrMagnitude>0,"Collapsed face");for(int i=0;i<face.Vertices.Count;i++){int x=face.Vertices[i],y=face.Vertices[(i+1)%face.Vertices.Count];referenced.Add(x);var p=t.Positions[x];var q=t.Positions[y];Check(!p.Equals(q),"Zero-length edge");var key=StudioMeshCuts.NormalEdgePositionKeyForBevel(p,q);uses.TryGetValue(key,out var u);Check(u.count==0||u.start.Equals(q),"Inconsistent winding");uses[key]=(u.count+1,p);}}
  Check(uses.Values.All(v=>v.count==2),"Open/nonmanifold edge");Check(t.Positions.Distinct().Count()-uses.Count+t.Faces.Count==2,"Unexpected genus");Check(volume>0,"Empty/inverted volume");Check(referenced.Count==n.mesh.vertexCount,"Unused published vertex");
 }
 public static string Validate()
 {
  int cases=0;string phase="";var report=new StringBuilder();
  try{foreach(int firstSub in new[]{0,1,2,3,8,16,32})foreach(int secondSub in new[]{0,1,2,3,8,16,32})foreach(float firstWidth in new[]{.03f,.1f,.22f})foreach(float secondWidth in new[]{.03f,.1f,.22f})foreach(var style in new[]{StudioMeshCuts.BevelJunction.Uniform,StudioMeshCuts.BevelJunction.Patch})
  {
   phase=$"firstSub={firstSub} secondSub={secondSub} firstWidth={firstWidth} secondWidth={secondWidth} style={style}";
   var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
   try{
    var node=new StudioModelDocument.Node{mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles};var t=new StudioMeshCuts(node);
    var selected=Edges(t).Where(e=>{var a=t.Positions[e.a];var b=t.Positions[e.b];return a.z==-.5f&&b.z==-.5f&&((a.y==.5f&&b.y==.5f)||(a.x==.5f&&b.x==.5f));}).ToArray();Check(selected.Length==2,"First selection mismatch");
    t.BevelEdges(node,selected,new StudioMeshCuts.BevelOptions{Width=firstWidth,Subdivisions=firstSub,Tension=1,Junction=style});ValidateMesh(node);Check((bool)typeof(StudioMeshCuts).GetField("bevelUseClipping",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(t),"First pass fell back to old junction builder");
    t=new StudioMeshCuts(node);selected=Edges(t).Where(e=>{var a=t.Positions[e.a];var b=t.Positions[e.b];return Mathf.Abs(a.x-a.y)<1e-5&&Mathf.Abs(b.x-b.y)<1e-5&&a.x>0&&b.x>0;}).ToArray();Check(selected.Length==firstSub+2,"Second selection mismatch "+selected.Length);
    t.BevelEdges(node,selected,new StudioMeshCuts.BevelOptions{Width=secondWidth,Subdivisions=secondSub,Tension=1,Junction=style});ValidateMesh(node);Check((bool)typeof(StudioMeshCuts).GetField("bevelUseClipping",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(t),"Second pass fell back to old junction builder");cases++;
   }finally{Object.DestroyImmediate(mesh);}
  }
  foreach(float size in new[]{1f,10f,100f})foreach(float width in new[]{.025f,.1f,1.2f})
  {
   if(width>=size)continue;phase=$"model-unit width size={size} width={width}";
   var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polys);try{
    mesh.vertices=mesh.vertices.Select(p=>p*size).ToArray();mesh.RecalculateBounds();var n=new StudioModelDocument.Node{mesh=mesh,cutPolygons=polys,cutTriangles=mesh.triangles};var t=new StudioMeshCuts(n);
    var edge=Edges(t).Single(e=>{var a=t.Positions[e.a];var b=t.Positions[e.b];return a.y==size*.5f&&b.y==size*.5f&&a.z==-size*.5f&&b.z==-size*.5f;});
    var widthResult=t.BevelEdges(n,new[]{edge},new StudioMeshCuts.BevelOptions{Width=width});Check(Mathf.Abs(widthResult.AppliedWidth-width)<1e-6,"Width was capped");ValidateMesh(n);
    var points=new StudioMeshCuts(n).Positions;var top=points.Where(p=>p.y==size*.5f&&p.z>-size*.5f+1e-6f&&p.z<size*.5f-1e-6f).ToArray();Check(top.Length>0&&top.All(p=>Mathf.Abs((p.z+size*.5f)-width)<size*1e-6f),"Offset does not equal requested model distance");cases++;
   }finally{Object.DestroyImmediate(mesh);}
  }
  report.AppendLine("PASS "+cases+" subdivision sequence and model-unit width cases");
  }catch(Exception e){report.AppendLine("FAIL "+phase+" after "+cases+": "+e);}
  string result=report.ToString();File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../bevel-subdivision-result.txt")),result);return result;
 }
 public static void Run(){EditorApplication.delayCall+=()=>{var result=Validate();EditorApplication.Exit(result.StartsWith("PASS")?0:1);};}
}


