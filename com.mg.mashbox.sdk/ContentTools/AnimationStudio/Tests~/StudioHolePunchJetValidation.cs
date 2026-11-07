using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace MashBoxSDK.AnimationStudio
{
 public static class StudioHolePunchJetValidation
 {
  static readonly List<string> log=new List<string>();
  static void RingQuality(StudioModelDocument.Node node,StudioMeshFracture.Input original,Vector3 center,Vector3 normal,float radius,float depth,bool through)
  {
   normal.Normalize();var points=node.mesh.vertices;var topology=new StudioMeshCuts(node);
   float Radial(Vector3 p){var q=p-center;return (q-normal*Vector3.Dot(q,normal)).magnitude;}
   bool At(float value,float target)=>Mathf.Abs(value-target)<radius*.0001f;
   var band=topology.Faces.Where(f=>f.Vertices.Count==4&&f.Vertices.Count(v=>At(Radial(points[v]),radius))==2&&f.Vertices.Count(v=>At(Radial(points[v]),radius*1.45f))==2).ToArray();
   if(band.Length!=(through?32:16))throw new Exception("Missing regular quad support band: "+band.Length);
   var wall=topology.Faces.Where(f=>f.Vertices.All(v=>At(Radial(points[v]),radius))&&f.Vertices.Max(v=>Vector3.Dot(points[v]-center,normal))-f.Vertices.Min(v=>Vector3.Dot(points[v]-center,normal))>depth*.1f).ToArray();
   if(wall.Length!=16||wall.Any(f=>f.Vertices.Count!=4))throw new Exception("The circular wall is not 16 genuine quads");
   if(!through){var floor=topology.Faces.Where(f=>f.Vertices.All(v=>At(Vector3.Dot(points[v]-center,normal),-depth))).ToArray();if(floor.Length!=16||floor.Any(f=>f.Vertices.Count!=4))throw new Exception("The partial floor is not a 4 by 4 quad grid");}
   double deviation=0;int samples=0;var indices=node.mesh.triangles;
   for(int i=0;i<indices.Length;i+=3){var p=(points[indices[i]]+points[indices[i+1]]+points[indices[i+2]])/3;float radial=Radial(p);if(radial<radius*1.01f||radial>radius*2)continue;double nearest=double.PositiveInfinity;
    foreach(var t in original.Triangles){var a=original.Positions[t[0]];var e=original.Positions[t[1]]-a;var f=original.Positions[t[2]]-a;float denominator=Vector3.Dot(Vector3.Cross(e,f),normal);if(Mathf.Abs(denominator)<1e-10)continue;float u=Vector3.Dot(Vector3.Cross(p-a,f),normal)/denominator,v=Vector3.Dot(Vector3.Cross(e,p-a),normal)/denominator;if(u< -1e-5||v< -1e-5||u+v>1.00001f)continue;nearest=Math.Min(nearest,Vector3.Distance(p,a+e*u+f*v));}
    if(double.IsInfinity(nearest))throw new Exception("Rebuilt surface left the source sheet");deviation=Math.Max(deviation,nearest);samples++;
   }
   log.Add("QUALITY support quads="+band.Length+" wall quads="+wall.Length+" floor="+(through?"open":"4x4 quads")+" maximum sampled axial deviation="+deviation+" (radius fraction="+(deviation/radius)+") samples="+samples);
   if(deviation>radius*.075)throw new Exception("Clean patch surface deviation exceeds 7.5 percent of stamp radius");
   log.Add("PASS regular quad ring, quad walls, floor grid and local shape bound");
  }
  static void Overlaps(Mesh mesh,Vector3 center,float radius)
  {
   var p=mesh.vertices;var t=mesh.triangles;var candidates=Enumerable.Range(0,t.Length/3).Where(i=>Enumerable.Range(0,3).Any(k=>(p[t[i*3+k]]-center).magnitude<radius*3)).ToArray();
   double Cross(Vector2 a,Vector2 b)=> (double)a.x*b.y-(double)a.y*b.x;
   bool Pierces(Vector3 start,Vector3 end,Vector3[] face){var plane=Vector3.Cross(face[1]-face[0],face[2]-face[0]).normalized;double ds=(double)(start.x-face[0].x)*plane.x+(double)(start.y-face[0].y)*plane.y+(double)(start.z-face[0].z)*plane.z;double de=(double)(end.x-face[0].x)*plane.x+(double)(end.y-face[0].y)*plane.y+(double)(end.z-face[0].z)*plane.z;if(!((ds>1e-7&&de< -1e-7)||(de>1e-7&&ds< -1e-7)))return false;var direction=end-start;var e1=face[1]-face[0];var e2=face[2]-face[0];var h=Vector3.Cross(direction,e2);float determinant=Vector3.Dot(e1,h);if(Mathf.Abs(determinant)<1e-10f)return false;var offset=start-face[0];float u=Vector3.Dot(offset,h)/determinant;var q=Vector3.Cross(offset,e1);float v=Vector3.Dot(direction,q)/determinant;float distance=Vector3.Dot(e2,q)/determinant;return u>1e-5f&&v>1e-5f&&u+v<1-1e-5f&&distance>1e-5f&&distance<1-1e-5f;}
   for(int i=0;i<candidates.Length;i++)for(int j=i+1;j<candidates.Length;j++){
    var a=Enumerable.Range(0,3).Select(k=>p[t[candidates[i]*3+k]]).ToArray();var b=Enumerable.Range(0,3).Select(k=>p[t[candidates[j]*3+k]]).ToArray();var n=Vector3.Cross(a[1]-a[0],a[2]-a[0]).normalized;var nb=Vector3.Cross(b[1]-b[0],b[2]-b[0]).normalized;
    if(Mathf.Abs(Vector3.Dot(n,nb))<.99999f||b.Any(v=>Mathf.Abs(Vector3.Dot(v-a[0],n))>1e-6f)){for(int k=0;k<3;k++)if(Pierces(a[k],a[(k+1)%3],b)||Pierces(b[k],b[(k+1)%3],a))throw new Exception("Intersecting noncoplanar triangles near stamp "+candidates[i]+" / "+candidates[j]+" A="+string.Join(";",a.Select(v=>v.ToString("R")))+" B="+string.Join(";",b.Select(v=>v.ToString("R"))));continue;}
    var x=(a[1]-a[0]).normalized;var y=Vector3.Cross(n,x);Vector2 P(Vector3 v){v-=a[0];return new Vector2(Vector3.Dot(v,x),Vector3.Dot(v,y));}var clip=a.Select(P).ToArray();var polygon=b.Select(P).ToList();
    for(int e=0;e<3&&polygon.Count>0;e++){var output=new List<Vector2>();var u=clip[e];var d=clip[(e+1)%3]-u;for(int k=0;k<polygon.Count;k++){var v=polygon[k];var w=polygon[(k+1)%polygon.Count];double dv=Cross(d,v-u),dw=Cross(d,w-u);if(dv>=0)output.Add(v);if((dv>=0)!=(dw>=0))output.Add(Vector2.LerpUnclamped(v,w,(float)(dv/(dv-dw))));}polygon=output;}
    double area=0;for(int k=0;k<polygon.Count;k++)area+=Cross(polygon[k],polygon[(k+1)%polygon.Count]);if(Math.Abs(area)>.0000001)throw new Exception("Overlapping coplanar render triangles near the stamp: "+candidates[i]+" / "+candidates[j]);
   }log.Add("PASS no coplanar overlap or noncoplanar triangle piercing near stamp");
  }
  public static void Begin(){if(!Application.dataPath.Replace('\\','/').EndsWith("/HolePuncher/Project/Assets"))throw new Exception("Disposable project only");EditorApplication.delayCall+=Run;}
  static void Run(){int code=0;try{
   StudioHolePunchValidation.KeepRunning=true;typeof(StudioHolePunchValidation).GetMethod("Run",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);StudioHolePunchValidation.KeepRunning=false;
   string path="Assets/Catalog/tests/fighterJetCutTest.fbx";var importer=(ModelImporter)AssetImporter.GetAtPath(path);importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.SaveAndReimport();var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
   foreach(bool clean in new[]{true,false})foreach(bool through in new[]{false,true}){
    var nodes=(List<StudioModelDocument.Node>)typeof(StudioModelDocument).GetMethod("CaptureNodes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{asset});var node=nodes.Where(n=>n.mesh).OrderByDescending(n=>n.mesh.vertexCount).First();
    var original=StudioMeshFracture.Capture(node);var topology=new StudioMeshCuts(node);var points=node.mesh.vertices;var bounds=node.mesh.bounds;
    var candidates=topology.Faces.Select((f,i)=>new{f,i,c=f.Vertices.Aggregate(Vector3.zero,(p,v)=>p+points[v])/f.Vertices.Count,n=Vector3.Cross(points[f.Vertices[1]]-points[f.Vertices[0]],points[f.Vertices[2]]-points[f.Vertices[0]]).normalized}).Where(f=>f.n.y>.5f).OrderBy(f=>Mathf.Abs(f.c.x-bounds.center.x)+Mathf.Abs(f.c.z-bounds.center.z)).ToArray();
    if(candidates.Length==0)throw new Exception("No upper face");var chosen=candidates[0];float radius=chosen.f.Vertices.Max(v=>(points[v]-chosen.c).magnitude)*1.5f;float depth=bounds.size.y*.1f;
    log.Add("Input "+points.Length+" vertices, "+topology.Faces.Count+" faces; bounds "+bounds+"; face "+chosen.i+" center "+chosen.c+" radius "+radius+" depth "+depth+" through="+through+" clean="+clean);
    try{Overlaps(node.mesh,chosen.c,radius);log.Add("BASELINE intersections clear");}catch(Exception baseline){log.Add("BASELINE "+baseline.Message);}var affected=topology.PunchHole(node,chosen.i,chosen.c,Matrix4x4.identity,radius,16,through?bounds.size.y+radius:depth,through,clean,11.25f,chosen.n,through);if(affected.Length<2)throw new Exception("Stamp did not cross multiple authored faces");
    typeof(StudioHolePunchValidation).GetMethod("Manifold",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{node});Overlaps(node.mesh,chosen.c,radius);if(clean)RingQuality(node,original,chosen.c,chosen.n,radius,depth,through);log.Add("PASS exact jet multiple-face "+(through?"through":"partial")+" affected="+affected.Length+" clean="+clean+" output="+new StudioMeshCuts(node).Faces.Count);
    // Render authored wire with ordinary depth testing on both opening sides.
    StudioHolePunchValidation.Focus=chosen.c;StudioHolePunchValidation.Direction=chosen.n;StudioHolePunchValidation.FocusRadius=radius;typeof(StudioHolePunchValidation).GetMethod("Render",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{node,(through?"jet-through":"jet-partial")+(clean?"":"-raw")});
    if(clean&&through){var p=node.mesh.vertices;var rim=p.Where(v=>{var q=v-chosen.c;return Mathf.Abs((q-chosen.n*Vector3.Dot(q,chosen.n)).magnitude-radius)<radius*.0001f;}).ToArray();float height=rim.Min(v=>Vector3.Dot(v-chosen.c,chosen.n));StudioHolePunchValidation.Focus=chosen.c+chosen.n*height;StudioHolePunchValidation.Direction=-chosen.n;typeof(StudioHolePunchValidation).GetMethod("Render",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{node,"jet-through-exit"});}
    foreach(var mesh in nodes.Where(n=>n.mesh).Select(n=>n.mesh).Distinct())Object.DestroyImmediate(mesh);
   }
  }catch(Exception e){log.Add("FAIL "+e);code=1;}File.WriteAllLines(Path.GetFullPath(Path.Combine(Application.dataPath,"../../jet-result.txt")),log);EditorApplication.Exit(code);}
 }
}






