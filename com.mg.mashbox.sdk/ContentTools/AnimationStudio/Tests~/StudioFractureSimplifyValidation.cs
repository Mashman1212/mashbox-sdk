using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEditor;
namespace MashBoxSDK.AnimationStudio
{
 internal static partial class StudioMeshFracture
 {
  internal static int CheckSimpleDomain(float[] xy,float[] hole=null)
  {
   var work=new SurfaceWork();var outer=new List<int>();for(int i=0;i<xy.Length;i+=2)outer.Add(work.Vertex(new V(xy[i],xy[i+1],0)));
   var holes=new List<List<int>>();if(hole!=null){var ring=new List<int>();for(int i=0;i<hole.Length;i+=2)ring.Add(work.Vertex(new V(hole[i],hole[i+1],0)));holes.Add(ring);}
   var normal=new V(0,0,1);double expected=RingArea(work,outer,normal)+holes.Sum(h=>RingArea(work,h,normal));
   var faces=SimplifyInterior(work,outer,holes,normal,CancellationToken.None);double area=0;
   var edges=new Dictionary<(int,int),int>();
   foreach(var f in faces){double a=RingArea(work,f.ToList(),normal);if(a<=0)throw new Exception("Reversed/collapsed fan");area+=a;for(int i=0;i<f.Length;i++){var edge=(f[i],f[(i+1)%f.Length]);edges[edge]=edges.TryGetValue(edge,out int n)?n+1:1;}}
   if(Math.Abs(area-expected)>1e-7)throw new Exception("Fan area does not cover domain exactly");
   foreach(var ring in new[]{outer}.Concat(holes))for(int i=0;i<ring.Count;i++){var e=(ring[i],ring[(i+1)%ring.Count]);if(!edges.TryGetValue(e,out int count)||count!=1)throw new Exception("Boundary lost");edges.Remove(e);}
   if(edges.Any(e=>e.Value!=1||!edges.ContainsKey((e.Key.Item2,e.Key.Item1))))throw new Exception("Interior does not conform");
   return faces.Count;
  }
 }
 [InitializeOnLoad] public static class StudioFractureSimplifyValidation
 {
  static string Root=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../FractureSimplify"));
  static StudioFractureSimplifyValidation(){if(Application.dataPath.Replace('\\','/').EndsWith("/SelectionUndoProject/Assets"))EditorApplication.update+=Tick;}
  static void Tick(){if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;var p=Path.Combine(Root,"request.txt");if(File.Exists(p)){File.Delete(p);Run();}}
  public static void Run()
  {
   int checks=0;GameObject cube=null;
   try
   {
    StudioMeshFracture.CheckSimpleDomain(new float[]{0,0,2,0,4,0,4,4,0,4});checks++;
    StudioMeshFracture.CheckSimpleDomain(new float[]{0,0,5,0,5,5,4,5,4,1,1,1,1,5,0,5});checks++;
    StudioMeshFracture.CheckSimpleDomain(new float[]{0,0,5,0,5,5,0,5},new float[]{1,1,1,4,4,4,4,1});checks++;
    cube=GameObject.CreatePrimitive(PrimitiveType.Cube);var mesh=cube.GetComponent<MeshFilter>().sharedMesh;
    var input=StudioMeshFracture.Capture(new StudioModelDocument.Node{mesh=mesh});var original=mesh.vertices;
    var detailed=StudioMeshFracture.Compute(input,new StudioMeshFracture.Settings{SimplifyInterior=false},CancellationToken.None,null);
    var settings=new StudioMeshFracture.Settings{SimplifyInterior=true};
    var simple=StudioMeshFracture.Compute(input,settings,CancellationToken.None,null);
    int Tris(StudioMeshFracture.Result r)=>r.Pieces.SelectMany(p=>p.Faces).Sum(f=>f.Exterior<0?f.Vertices.Length-2:0);
    if(Tris(simple)>=Tris(detailed))throw new Exception("No interior render triangle reduction");checks++;
    if(simple.Pieces.Sum(p=>p.Faces.Count)>=detailed.Pieces.Sum(p=>p.Faces.Count))throw new Exception("No authored face reduction");checks++;
    if(!simple.Positions.SequenceEqual(simple.Original))throw new Exception("Roughness retained in simplified mode");checks++;
    if(!mesh.vertices.SequenceEqual(original))throw new Exception("Source mutated");checks++;
    var pairs=simple.Pieces.SelectMany(p=>p.Faces).Where(f=>f.Exterior<0).GroupBy(f=>string.Join(",",f.Vertices.OrderBy(v=>v)));
    if(pairs.Any(g=>g.Count()!=2))throw new Exception("Shared interfaces mismatch");checks++;
    foreach(var piece in simple.Pieces){var node=StudioMeshFracture.Publish(simple,piece);try{var v=node.mesh.vertices;var tri=node.mesh.triangles;for(int i=0;i<tri.Length;i+=3)if(Vector3.Cross(v[tri[i+1]]-v[tri[i]],v[tri[i+2]]-v[tri[i]]).sqrMagnitude<1e-20f)throw new Exception("Degenerate published render triangle");}finally{UnityEngine.Object.DestroyImmediate(node.mesh);}}checks++;
    settings.Gap=.001f;StudioMeshFracture.Compute(input,settings,CancellationToken.None,null);checks++;
    File.WriteAllText(Path.Combine(Root,"result.txt"),"PASS "+checks+" checks; cube interior render triangles "+Tris(detailed)+" -> "+Tris(simple)+"; authored faces "+detailed.Pieces.Sum(p=>p.Faces.Count)+" -> "+simple.Pieces.Sum(p=>p.Faces.Count));
   }catch(Exception e){File.WriteAllText(Path.Combine(Root,"result.txt"),"FAIL after "+checks+": "+e);}finally{if(cube)UnityEngine.Object.DestroyImmediate(cube);}
  }
 }
}
