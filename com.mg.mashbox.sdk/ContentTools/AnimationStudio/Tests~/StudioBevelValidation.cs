using System;using System.IO;using System.Collections.Generic;using System.Linq;using System.Reflection;using MashBoxSDK.AnimationStudio;using UnityEditor;using UnityEngine;using UnityEngine.Rendering;using Object=UnityEngine.Object;
public static class StudioBevelValidation
{
 static int checks,cases;static string current;static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool p,string s){if(!p)throw new Exception(current+": "+s);checks++;}
 static StudioModelDocument.Node Cube(){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);var mesh=Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);Object.DestroyImmediate(go);return new StudioModelDocument.Node{name="Bevel cube",mesh=mesh};}
 static (int,int) K(int a,int b)=>a<b?(a,b):(b,a);
 static List<(int a,int b)> Edges(StudioMeshCuts cuts){var ids=new Dictionary<Vector3,int>();var seen=new HashSet<(int,int)>();var edges=new List<(int,int)>();var v=cuts.Positions;foreach(var f in cuts.Faces)for(int i=0;i<f.Vertices.Count;i++){int a=f.Vertices[i],b=f.Vertices[(i+1)%f.Vertices.Count];if(!ids.ContainsKey(v[a]))ids.Add(v[a],ids.Count);if(!ids.ContainsKey(v[b]))ids.Add(v[b],ids.Count);if(seen.Add(K(ids[v[a]],ids[v[b]])))edges.Add((a,b));}return edges;}
 static void Valid(StudioModelDocument.Node node,bool closed=true)
 {
  var m=node.mesh;var v=m.vertices;var tr=m.triangles;var normals=m.normals;Check(tr.Length>0&&tr.Length%3==0,"Triangle buffer valid");Check(normals.Length==v.Length&&normals.All(n=>!float.IsNaN(n.x)&&n.sqrMagnitude>.98f&&n.sqrMagnitude<1.02f),"Normals finite and normalized");Check(v.All(p=>!float.IsNaN(p.x)&&!float.IsInfinity(p.x)),"Positions finite");Check(node.normalGroups!=null&&node.normalGroups.Length==v.Length&&node.normalGroups.All(g=>g>=0&&g<v.Length),"Smoothing groups valid");
  var ids=new Dictionary<Vector3,int>();var uses=new Dictionary<(int,int),(int count,int a,int b)>();double volume=0;
  for(int i=0;i<tr.Length;i+=3){var a=v[tr[i]];var b=v[tr[i+1]];var c=v[tr[i+2]];Check(Vector3.Cross(b-a,c-a).sqrMagnitude>1e-26f,"No degenerate render triangles");volume+=Vector3.Dot(a,Vector3.Cross(b,c))/6d;for(int j=0;j<3;j++){var p=v[tr[i+j]];var q=v[tr[i+(j+1)%3]];if(!ids.ContainsKey(p))ids.Add(p,ids.Count);if(!ids.ContainsKey(q))ids.Add(q,ids.Count);int x=ids[p],y=ids[q];var k=K(x,y);if(uses.TryGetValue(k,out var u)){Check(u.count==1&&u.a==y,"Triangle seams share opposite oriented half-edges");uses[k]=(2,u.a,u.b);}else uses.Add(k,(1,x,y));}}
  if(closed){Check(uses.Values.All(u=>u.count==2),"Closed mesh has no holes or T junctions");Check(ids.Count-uses.Count+tr.Length/3==2,"Closed cube retains Euler characteristic 2");Check(volume>0,"Outward winding has positive volume");}
  var topology=new StudioMeshCuts(node);Check(topology.Faces.Count>6,"Polygon topology retained");Check(node.cutTriangles.SequenceEqual(tr),"Polygon cache matches render triangles");
 }
 static void Case(int mask,int divisions,float tension,StudioMeshCuts.BevelJunction junction,float width=.2f)
 {
  current="cube mask="+mask+" divisions="+divisions+" tension="+tension+" junction="+junction+" width="+width;var n=Cube();try{var topology=new StudioMeshCuts(n);var all=Edges(topology);var selected=all.Where((e,i)=>(mask&(1<<i))!=0).ToList();var r=topology.BevelEdges(n,selected,new StudioMeshCuts.BevelOptions{Width=width,Subdivisions=divisions,Tension=tension,Junction=junction});Check(r!=null&&r.Edges==selected.Count,"Every selected edge handled");Check(r.Selection.Count>0,"Result has edge selection");Valid(n);cases++;}catch(Exception e){throw new Exception(current,e);}finally{Object.DestroyImmediate(n.mesh);}}
 public static void Run(){try{
  for(int mask=1;mask<4096;mask++)Case(mask,0,0,StudioMeshCuts.BevelJunction.Patch);
  foreach(int mask in new[]{1,3,7,15,31,63,255,1365,2730,4095})foreach(int divisions in new[]{1,3,8})foreach(float tension in new[]{-1f,-.5f,0,.5f,1})foreach(var j in new[]{StudioMeshCuts.BevelJunction.Uniform,StudioMeshCuts.BevelJunction.Patch})Case(mask,divisions,tension,j);
  foreach(float width in new[]{.001f,.5f,1f})Case(4095,4,1,StudioMeshCuts.BevelJunction.Uniform,width);
  current="zero-width no-op";var n=Cube();try{var t=new StudioMeshCuts(n);var old=n.mesh.vertices;bool wrote=false;var r=t.BevelEdges(n,new[]{Edges(t)[0]},new StudioMeshCuts.BevelOptions{Width=0,Subdivisions=0,Junction=StudioMeshCuts.BevelJunction.Patch},()=>wrote=true);Check(r==null&&!wrote&&n.mesh.vertices.SequenceEqual(old),"Zero width does not mutate mesh or register Undo");}finally{Object.DestroyImmediate(n.mesh);}
  File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../bevel-result.txt")),"PASS "+checks+" checks across "+cases+" geometric cases");
 }catch(Exception e){File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../bevel-result.txt")),e.ToString());}finally{EditorApplication.Exit(0);}}
}


