using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace MashBoxSDK.AnimationStudio
{
 public static class StudioHolePunchValidation
 {
  internal static bool KeepRunning; internal static Vector3 Focus,Direction; internal static float FocusRadius;
  static readonly List<string> log=new List<string>();
  static void Check(bool value,string label){if(!value)throw new Exception(label);log.Add("PASS "+label);}
  static StudioModelDocument.Node Cube(){var m=new Mesh();m.vertices=new[]{new Vector3(-1,-1,-1),new Vector3(1,-1,-1),new Vector3(1,1,-1),new Vector3(-1,1,-1),new Vector3(-1,-1,1),new Vector3(1,-1,1),new Vector3(1,1,1),new Vector3(-1,1,1)};var rings=new[]{new[]{0,3,2,1},new[]{4,5,6,7},new[]{0,1,5,4},new[]{3,7,6,2},new[]{0,4,7,3},new[]{1,2,6,5}};m.triangles=rings.SelectMany(r=>new[]{r[0],r[1],r[2],r[0],r[2],r[3]}).ToArray();m.RecalculateNormals();for(int c=0;c<8;c++)m.SetUVs(c,m.vertices.Select(v=>new Vector4(v.x,v.y,c,1)).ToList());return new StudioModelDocument.Node{mesh=m,cutPolygons=rings.SelectMany(r=>new[]{0,4}.Concat(r)).ToArray(),cutTriangles=m.triangles,geometryVertices=Enumerable.Range(0,8).ToArray()};}
  static void Manifold(StudioModelDocument.Node n){var t=new StudioMeshCuts(n);var edges=new Dictionary<(int,int),List<(int,int)>>();foreach(var f in t.Faces)for(int i=0;i<f.Vertices.Count;i++){int a=n.geometryVertices[f.Vertices[i]],b=n.geometryVertices[f.Vertices[(i+1)%f.Vertices.Count]];var k=(Math.Min(a,b),Math.Max(a,b));if(!edges.ContainsKey(k))edges[k]=new List<(int,int)>();edges[k].Add((a,b));}Check(edges.Values.All(e=>e.Count==2&&e[0].Item1==e[1].Item2&&e[0].Item2==e[1].Item1),"Closed oriented manifold");Check(n.mesh.triangles.All(v=>v>=0&&v<n.mesh.vertexCount),"Valid render indices");}
  static void LimitedVolume()
  {
   foreach(bool clean in new[]{false,true})foreach(float depth in new[]{.4f,2.5f,6.5f})
   {
    var n=Cube();var p=n.mesh.vertices;var t=n.mesh.triangles;var faces=new StudioMeshCuts(n).Faces.Select(f=>f.Vertices.ToArray()).ToArray();var offsets=new[]{0f,4f,-4f};
    n.mesh.vertices=offsets.SelectMany(z=>p.Select(v=>v+Vector3.forward*z)).ToArray();n.mesh.triangles=Enumerable.Range(0,3).SelectMany(i=>t.Select(v=>v+8*i)).ToArray();n.mesh.RecalculateNormals();
    n.geometryVertices=Enumerable.Range(0,24).ToArray();n.cutPolygons=Enumerable.Range(0,3).SelectMany(i=>faces.SelectMany(f=>new[]{0,f.Length}.Concat(f.Select(v=>v+8*i)))).ToArray();n.cutTriangles=n.mesh.triangles;
    for(int c=0;c<8;c++)n.mesh.SetUVs(c,n.mesh.vertices.Select(v=>new Vector4(v.x,v.y,c,1)).ToList());
    string[] OriginalShell(int shell)=>new StudioMeshCuts(n).Faces.Where(f=>f.Vertices.All(v=>n.geometryVertices[v]>=shell*8&&n.geometryVertices[v]<(shell+1)*8)).Select(f=>string.Join(",",f.Vertices.Select(v=>n.geometryVertices[v]))).OrderBy(s=>s).ToArray();
    var ahead=OriginalShell(2);var beyond=OriginalShell(1);
    new StudioMeshCuts(n).PunchHole(n,0,new Vector3(0,0,-1),Matrix4x4.identity,.3f,16,depth,true,clean,11.25f,Vector3.back,true);Manifold(n);
    var points=n.mesh.vertices;var indices=n.mesh.triangles;double volume=0;for(int k=0;k<indices.Length;k+=3)volume+=Vector3.Dot(points[indices[k]],Vector3.Cross(points[indices[k+1]],points[indices[k+2]]))/6.0;
    double cutLength=depth<2?depth:depth<6?2:4,expected=24-8*.3*.3*Math.Sin(Math.PI/8)*cutLength;
    Check(Math.Abs(volume-expected)<.0002,"Bounded Through exact volume depth="+depth+" clean="+clean);
    Check(ahead.SequenceEqual(OriginalShell(2)),"Mesh piece ahead of volume remains unchanged");
    if(depth<6)Check(beyond.SequenceEqual(OriginalShell(1)),"Mesh piece beyond depth remains unchanged");
    Object.DestroyImmediate(n.mesh);
   }
  }
  static void Render(StudioModelDocument.Node n,string name){var go=new GameObject("Hole preview");var wireObject=new GameObject("Authored wire");var wireMesh=new Mesh();var wireMaterial=new Material(Shader.Find("Hidden/HolePunchWireValidation"));var topology=new StudioMeshCuts(n);wireMesh.vertices=n.mesh.vertices;wireMesh.SetIndices(topology.Faces.SelectMany(f=>Enumerable.Range(0,f.Vertices.Count).SelectMany(i=>new[]{f.Vertices[i],f.Vertices[(i+1)%f.Vertices.Count]})).ToArray(),MeshTopology.Lines,0);wireObject.AddComponent<MeshFilter>().sharedMesh=wireMesh;wireObject.AddComponent<MeshRenderer>().sharedMaterial=wireMaterial;var cameraObject=new GameObject("Preview camera");var lightObject=new GameObject("Preview light");var material=new Material(Shader.Find("Standard"));var rt=new RenderTexture(640,480,24);var image=new Texture2D(640,480,TextureFormat.RGB24,false);var previous=RenderTexture.active;try{go.AddComponent<MeshFilter>().sharedMesh=n.mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;material.color=new Color(.65f,.7f,.78f);var camera=cameraObject.AddComponent<Camera>();camera.transform.position=FocusRadius>0?Focus+Direction*FocusRadius*5+Vector3.right*FocusRadius:new Vector3(-2.5f,2,-4.5f);camera.transform.LookAt(FocusRadius>0?Focus:Vector3.zero);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.13f,.15f);camera.targetTexture=rt;var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(25,-30,0);camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,640,480),0,0);image.Apply();File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+name+".png")),image.EncodeToPNG());}finally{RenderTexture.active=previous;Object.DestroyImmediate(go);Object.DestroyImmediate(wireObject);Object.DestroyImmediate(wireMesh);Object.DestroyImmediate(wireMaterial);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(lightObject);Object.DestroyImmediate(material);Object.DestroyImmediate(rt);Object.DestroyImmediate(image);}}
  public static void Begin(){if(!Application.dataPath.Replace('\\','/').EndsWith("/HolePuncher/Project/Assets"))throw new Exception("Disposable project only");EditorApplication.delayCall+=Run;}
  static void Run(){int code=0;try{
   LimitedVolume();
   foreach(bool through in new[]{false,true})foreach(bool clean in new[]{false,true})foreach(int count in new[]{3,5,6,8,10,16,32,128}){
    var n=Cube();new StudioMeshCuts(n).PunchHole(n,0,new Vector3(.1f,.05f,-1),Matrix4x4.identity,.3f,count,.4f,through,clean,11.25f);Manifold(n);var points=n.mesh.vertices;var indices=n.mesh.triangles;double volume=0;for(int k=0;k<indices.Length;k+=3)volume+=Vector3.Dot(points[indices[k]],Vector3.Cross(points[indices[k+1]],points[indices[k+2]]))/6.0;double expected=8-count*.5*.3*.3*Math.Sin(Math.PI*2/count)*(through?2:.4);Check(Math.Abs(volume-expected)<.0001,"Exact removed polygonal-cylinder volume");if(count==16&&clean)Render(n,through?"through":"recess");var t=new StudioMeshCuts(n);Check(t.Faces.Count>=count+4,"Radial wall count "+count+" through="+through+" clean="+clean);for(int c=0;c<8;c++){var uv=new List<Vector4>();n.mesh.GetUVs(c,uv);Check(uv.Count==n.mesh.vertexCount,"UV channel "+c);}Object.DestroyImmediate(n.mesh);
   }
   var doc=ScriptableObject.CreateInstance<StudioModelDocument>();var transaction=Cube();doc.nodes.Add(transaction);var original=transaction.mesh.triangles;
   using(var preview=new StudioMeshToolPreview(doc,new[]{0})){var target=preview.Targets[0];target.Topology.PunchHole(target.Work,0,new Vector3(0,0,-1),Matrix4x4.identity,.3f,16,.4f,false,true);preview.Publish(true);Check(transaction.mesh.vertexCount>8,"Captured preview publishes");preview.Restore();Check(transaction.mesh.triangles.SequenceEqual(original),"Cancel restores exact indices");preview.Commit("Hole Puncher fixture");UnityEditor.Undo.FlushUndoRecordObjects();Check(transaction.mesh.vertexCount>8,"Commit publishes preview");UnityEditor.Undo.PerformUndo();Check(transaction.mesh.triangles.SequenceEqual(original),"Native Undo restores source");UnityEditor.Undo.PerformRedo();Check(transaction.mesh.vertexCount>8,"Native Redo restores hole");}
   Object.DestroyImmediate(transaction.mesh);Object.DestroyImmediate(doc);
   var scaled=Cube();var world=Matrix4x4.TRS(new Vector3(3,2,1),Quaternion.Euler(12,24,7),new Vector3(2,1,3));new StudioMeshCuts(scaled).PunchHole(scaled,0,world.MultiplyPoint3x4(new Vector3(0,0,-1)),world,.3f,16,.4f,true,true);Manifold(scaled);Object.DestroyImmediate(scaled.mesh);
   var invalid=Cube();var before=invalid.mesh.triangles;bool rejected=false;try{new StudioMeshCuts(invalid).PunchHole(invalid,0,new Vector3(0,0,-1),Matrix4x4.identity,2,16,.2f,true,true);}catch(InvalidOperationException){rejected=true;}Check(rejected&&before.SequenceEqual(invalid.mesh.triangles),"Whole-mesh removal rejects without mutation");Object.DestroyImmediate(invalid.mesh);
  }catch(Exception e){log.Add("FAIL "+e);code=1;}File.WriteAllLines(Path.GetFullPath(Path.Combine(Application.dataPath,"../../result.txt")),log);if(!KeepRunning)EditorApplication.Exit(code);else if(code!=0)throw new Exception("Base Hole Puncher regression failed; see result.txt");}
 }
}

