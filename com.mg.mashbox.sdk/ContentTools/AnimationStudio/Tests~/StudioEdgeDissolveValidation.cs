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
 public static class StudioEdgeDissolveValidation
 {
  static int checks;static readonly List<string> log=new List<string>();
  static void Check(bool v,string s){if(!v)throw new Exception(s);checks++;log.Add("PASS "+s);}
  static StudioModelDocument.Node Strip(int count,bool warped=false)
  {
   var mesh=new Mesh();var p=new List<Vector3>();for(int x=0;x<=count;x++){p.Add(new Vector3(x,0,0));p.Add(new Vector3(x,1,warped&&x==1?.3f:0));}mesh.SetVertices(p);
   var t=new List<int>();var poly=new List<int>();for(int x=0;x<count;x++){int a=x*2;t.AddRange(new[]{a,a+2,a+3,a,a+3,a+1});poly.AddRange(new[]{0,4,a,a+2,a+3,a+1});}
   mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.colors=p.Select((v,i)=>new Color(i*.1f,.2f,.3f,1)).ToArray();
   for(int c=0;c<8;c++)mesh.SetUVs(c,p.Select((v,i)=>new Vector4(i,c,.3f,.4f)).ToList());
   return new StudioModelDocument.Node{name="Strip",mesh=mesh,cutPolygons=poly.ToArray(),cutTriangles=mesh.triangles,geometryVertices=Enumerable.Range(0,p.Count).ToArray()};
  }
  static StudioModelDocument.Node Grid()
  {
   var mesh=new Mesh();mesh.vertices=Enumerable.Range(0,9).Select(i=>new Vector3(i%3,i/3,0)).ToArray();
   var poly=new List<int>();var tri=new List<int>();
   for(int y=0;y<2;y++)for(int x=0;x<2;x++){int a=y*3+x;poly.AddRange(new[]{0,4,a,a+1,a+4,a+3});tri.AddRange(new[]{a,a+1,a+4,a,a+4,a+3});}
   mesh.SetTriangles(tri,0);mesh.RecalculateNormals();return new StudioModelDocument.Node{mesh=mesh,cutPolygons=poly.ToArray(),cutTriangles=mesh.triangles,geometryVertices=Enumerable.Range(0,9).ToArray()};
  }
  static void Reject(StudioModelDocument.Node n,(int,int)[] edges,bool weld,string reason)
  {
   var poly=n.cutPolygons.ToArray();var t=n.mesh.triangles;int writes=0;bool rejected=false;
   try{new StudioMeshCuts(n).DeleteComponents(n,new HashSet<int>(),new HashSet<int>(),edges,weld,()=>writes++);}catch(InvalidOperationException){rejected=true;}
   Check(rejected&&writes==0&&n.cutPolygons.SequenceEqual(poly)&&n.mesh.triangles.SequenceEqual(t),reason);
  }
  public static void Begin()
  {
   if(!Application.dataPath.Replace('\\','/').EndsWith("/EdgeDissolve/Project/Assets"))throw new Exception("Disposable project only");EditorApplication.delayCall+=Run;
  }
  static void Run()
  {
   int exit=0;
   try
   {
    foreach(bool warped in new[]{false,true})foreach(bool weld in new[]{false,true})
    {
     var n=Strip(3,warped);var original=Object.Instantiate(n.mesh);int writes=0;
     var used=new StudioMeshCuts(n).DeleteComponents(n,new HashSet<int>(),new HashSet<int>(),new[]{(2,3),(4,5)},weld,()=>writes++);
     var cuts=new StudioMeshCuts(n);
     Check(writes==1&&cuts.Faces.Count==1&&cuts.Faces[0].Vertices.Count==4,"Strip merges to one quad with automatic endpoint cleanup "+warped+"/"+weld);
     Check(n.mesh.vertexCount==4&&used.SequenceEqual(new[]{0,1,6,7}),"Redundant endpoints compacted out of mesh");
     Check(n.mesh.vertices.SequenceEqual(used.Select(i=>original.vertices[i]))&&n.mesh.triangles.Length==6,"Retained positions unchanged and merged face rebuilt");
     Check(n.mesh.normals.SequenceEqual(used.Select(i=>original.normals[i]))&&n.mesh.colors.SequenceEqual(used.Select(i=>original.colors[i])),"Shading and colors retained");
     bool uvs=true;for(int c=0;c<8;c++){var a=new List<Vector4>();var b=new List<Vector4>();n.mesh.GetUVs(c,a);original.GetUVs(c,b);uvs&=a.SequenceEqual(used.Select(i=>b[i]));}Check(uvs,"All eight UV channels retained");
     Check(cuts.BevelSourceTriangles(n.mesh).Single().Count==6&&n.geometryVertices.SequenceEqual(used),"Downstream triangle ownership and normal-group identity remap valid");
     Object.DestroyImmediate(n.mesh);Object.DestroyImmediate(original);
    }
    var diagonal=Strip(1);diagonal.cutPolygons=new[]{0,3,0,2,3,0,3,0,3,1};new StudioMeshCuts(diagonal).DeleteComponents(diagonal,new HashSet<int>(),new HashSet<int>(),new[]{(0,3)},true,null);
    Check(new StudioMeshCuts(diagonal).Faces.Single().Vertices.Count==4&&diagonal.mesh.vertexCount==4,"Triangle diagonal dissolves while protecting corners needed by the surviving quad");Object.DestroyImmediate(diagonal.mesh);
    var grid=Grid();new StudioMeshCuts(grid).DeleteComponents(grid,new HashSet<int>(),new HashSet<int>(),new[]{(1,4)},true,null);
    Check(new StudioMeshCuts(grid).Faces.Count==3&&grid.mesh.vertices.Contains(new Vector3(1,1,0)),"Endpoint supporting three remaining edges is retained");
    Check(!grid.mesh.vertices.Contains(new Vector3(1,0,0)),"Endpoint supporting only two boundary edges is removed");Object.DestroyImmediate(grid.mesh);
    grid=Grid();new StudioMeshCuts(grid).DeleteComponents(grid,new HashSet<int>(),new HashSet<int>(),new[]{(1,4),(3,4),(4,5),(4,7)},true,null);
    Check(new StudioMeshCuts(grid).Faces.Count==1&&grid.mesh.vertexCount==4,"Dissolving complete grid removes interior junction and redundant border points");Object.DestroyImmediate(grid.mesh);
    var bad=Strip(2);bad.cutPolygons=bad.cutPolygons.Concat(bad.cutPolygons.Take(6)).ToArray();Reject(bad,new[]{(2,3)},true,"Nonmanifold edge rejected atomically");Object.DestroyImmediate(bad.mesh);
    var cubeMesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var cubePolys);
    var cube=new StudioModelDocument.Node{mesh=cubeMesh,cutPolygons=cubePolys,cutTriangles=cubeMesh.triangles,geometryVertices=StudioSubdivisionData.Primitive(cubeMesh)};
    var cubeCuts=new StudioMeshCuts(cube);var cv=cubeCuts.Faces[0].Vertices;
    cubeCuts.DeleteComponents(cube,new HashSet<int>(),new HashSet<int>(),new[]{(cv[0],cv[1])},true,null);
    var mergedCube=new StudioMeshCuts(cube);
    Check(mergedCube.Faces.Count==5,"Cube edge with split normal render corners dissolves into five faces");
    Check(mergedCube.Faces.Count(f=>f.Vertices.Count==3)==2,"Endpoint cleanup propagates to adjacent cube faces");
    Check(mergedCube.BevelSourceTriangles(cubeMesh).Sum(t=>t.Count)==cubeMesh.triangles.Length,"All rebuilt cube triangles have valid authored ownership");Object.DestroyImmediate(cubeMesh);
    var border=Strip(2);Reject(border,new[]{(0,1)},true,"Boundary edge rejected atomically");Reject(border,new[]{(2,3),(0,1)},true,"Mixed valid/border selection rejected atomically");
    border.cutPolygons[6]=1;border.mesh.subMeshCount=2;border.mesh.SetTriangles(new[]{0,2,3,0,3,1},0);border.mesh.SetTriangles(new[]{2,4,5,2,5,3},1);border.cutTriangles=border.mesh.triangles;Reject(border,new[]{(2,3)},true,"Material boundary rejected atomically");Object.DestroyImmediate(border.mesh);
    var n2=Strip(2);var doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.nodes.Add(n2);doc.nodes.Add(new StudioModelDocument.Node{name="Linked",mesh=n2.mesh,cutPolygons=n2.cutPolygons.ToArray(),cutTriangles=n2.cutTriangles.ToArray(),geometryVertices=n2.geometryVertices.ToArray()});
    using(var editor=new StudioModelEditor(()=>{}))
    {
     editor.Load(doc);editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);
     var edges=(List<(int a,int b)>)typeof(StudioModelEditor).GetMethod("Edges",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(editor,null);
     int id=edges.FindIndex(e=>(e.a==2&&e.b==3)||(e.a==3&&e.b==2));Check(id>=0,"Shared edge available for selection");editor.Components.Add(id);
     var source=n2.cutPolygons.ToArray();editor.DeleteSelection();Check(new StudioMeshCuts(doc.nodes[0]).Faces.Count==1&&editor.Components.Count==0&&(StudioModelEditor.ComponentMode)typeof(StudioModelEditor).GetField("mode",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor)==StudioModelEditor.ComponentMode.Edge,"Production Delete clears edge selection and retains Edge mode");
     Check(doc.nodes[1].cutPolygons.SequenceEqual(doc.nodes[0].cutPolygons),"Linked instance receives dissolved authored topology");
     UnityEditor.Undo.FlushUndoRecordObjects();editor.PerformWorkspaceHistory(false);Check(doc.nodes[0].cutPolygons.SequenceEqual(source)&&editor.Components.Contains(id),"One native Undo restores topology and selection");
     editor.PerformWorkspaceHistory(true);Check(new StudioMeshCuts(doc.nodes[0]).Faces.Count==1&&editor.Components.Count==0,"Native Redo restores dissolve");
    }
    Object.DestroyImmediate(n2.mesh);Object.DestroyImmediate(doc);
    var face=Strip(2);new StudioMeshCuts(face).DeleteComponents(face,new HashSet<int>{0},new HashSet<int>(),Array.Empty<(int,int)>(),true,null);Check(new StudioMeshCuts(face).Faces.Count==1&&face.mesh.triangles.Length==6,"Face Delete still opens hole");Object.DestroyImmediate(face.mesh);
    var vertex=Strip(2);new StudioMeshCuts(vertex).DeleteComponents(vertex,new HashSet<int>(),new HashSet<int>{0},Array.Empty<(int,int)>(),true,null);Check(new StudioMeshCuts(vertex).Faces.Count==2&&new StudioMeshCuts(vertex).Faces.Any(f=>f.Vertices.Count==3),"Vertex Delete retains corner clipping");Object.DestroyImmediate(vertex.mesh);
   }catch(Exception e){log.Add("FAIL "+e);exit=1;}
   log.Add("Checks: "+checks);File.WriteAllLines(Path.GetFullPath(Path.Combine(Application.dataPath,"../../result.txt")),log);EditorApplication.Exit(exit);
  }
 }
}


