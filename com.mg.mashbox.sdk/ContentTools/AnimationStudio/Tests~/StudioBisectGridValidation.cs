using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object=UnityEngine.Object;
internal static class StudioBisectGridValidation
{
 static int checks;static string evidence="";
 static string Out(string n)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+n));
 static void Check(bool p,string m){checks++;if(!p)throw new Exception(m);}
 static StudioModelDocument.Node Prism(Vector2[] points)
 {
  int count=points.Length;var vertices=points.Select(p=>new Vector3(p.x,p.y,-.5f)).Concat(points.Select(p=>new Vector3(p.x,p.y,.5f))).ToList();
  var top=Enumerable.Range(count,count).ToList();var tris=new List<int>();
  typeof(StudioMeshCuts).GetMethod("TriangulateSliceCap",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{top,vertices,Vector3.forward,tris});
  var triangles=tris.Concat(tris.AsEnumerable().Reverse().Select(v=>v-count)).ToList();
  var faces=new List<int[]>{top.ToArray(),Enumerable.Range(0,count).Reverse().ToArray()};
  for(int i=0;i<count;i++){int j=(i+1)%count;var f=new[]{i,j,j+count,i+count};faces.Add(f);triangles.AddRange(new[]{f[0],f[1],f[2],f[0],f[2],f[3]});}
  var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();
  return new StudioModelDocument.Node{name="Grid fixture",mesh=mesh,cutPolygons=faces.SelectMany(f=>new[]{0,f.Length}.Concat(f)).ToArray(),cutTriangles=mesh.triangles,geometryVertices=Enumerable.Range(0,vertices.Count).ToArray()};
 }
 static Vector2[] Ring(int count,float aspect=1)=>Enumerable.Range(0,count).Select(i=>{float a=2*Mathf.PI*i/count;return new Vector2(Mathf.Cos(a)*aspect,Mathf.Sin(a));}).ToArray();
 static void Validate(StudioModelDocument.Node node,Plane plane,string label,bool regular)
 {
  var faces=new StudioMeshCuts(node).Faces;var points=node.mesh.vertices;var geometry=node.geometryVertices;
  var caps=faces.Where(f=>f.Vertices.All(v=>Mathf.Abs(plane.GetDistanceToPoint(points[v])+.005f)<2e-5f)).ToArray();
  Check(caps.Length>0,label+" caps");Check(caps.All(f=>f.Vertices.Count==4),label+" all caps quads");
  Check(node.normalGroups.All(g=>g>=0&&g<points.Length),label+" bounded normal groups");
  var edges=new Dictionary<(int,int),List<(int,int)>>();
  foreach(var f in faces)for(int i=0;i<f.Vertices.Count;i++){
   int a=geometry[f.Vertices[i]],b=geometry[f.Vertices[(i+1)%f.Vertices.Count]];var k=(Math.Min(a,b),Math.Max(a,b));if(!edges.TryGetValue(k,out var e))edges[k]=e=new List<(int,int)>();e.Add((a,b));}
  Check(edges.Values.All(e=>e.Count==2&&e[0].Item1==e[1].Item2&&e[0].Item2==e[1].Item1),label+" manifold closed wound result");
  var interior=new Dictionary<int,int>();var capEdges=new Dictionary<(int,int),int>();double min=1,mean=0;
  foreach(var f in caps){double q=1;for(int i=0;i<4;i++){
   int id=geometry[f.Vertices[i]];interior.TryGetValue(id,out int count);interior[id]=count+1;
   int next=geometry[f.Vertices[(i+1)%4]];var k=(Math.Min(id,next),Math.Max(id,next));capEdges.TryGetValue(k,out int uses);capEdges[k]=uses+1;
   var a=points[f.Vertices[(i+1)%4]]-points[f.Vertices[i]];var b=points[f.Vertices[(i+3)%4]]-points[f.Vertices[i]];
   q=Math.Min(q,2*Vector3.Dot(Vector3.Cross(a,b),plane.normal)/(a.sqrMagnitude+b.sqrMagnitude));
  }Check(q>0,label+" convex cap");min=Math.Min(min,q);mean+=q;}
  var boundary=new HashSet<int>(capEdges.Where(e=>e.Value==1).SelectMany(e=>new[]{e.Key.Item1,e.Key.Item2}));
  if(regular){Check(interior.Any(p=>!boundary.Contains(p.Key)),label+" grid interior");Check(interior.Where(p=>!boundary.Contains(p.Key)).All(p=>p.Value==4),label+" regular interior valence");Check(mean/caps.Length>.45,label+" average quad quality");}
  Check(node.mesh.uv.All(uv=>!float.IsNaN(uv.x)&&!float.IsInfinity(uv.x)),label+" finite planar UV");
  evidence+=$"{label}: {caps.Length} cap quads, min/mean scaled quality {min:F3}/{mean/caps.Length:F3}. ";
  Capture(node,plane,caps.SelectMany(f=>f.Vertices).Distinct().ToArray(),label);
 }
 [InitializeOnLoadMethod] static void Request(){if(Application.dataPath.EndsWith("SelectionUndoProject/Assets")&&File.Exists(Out("bisect-grid-request.txt"))){File.Delete(Out("bisect-grid-request.txt"));EditorApplication.delayCall+=Run;}}
 public static void Run()
 {
  if(!Application.dataPath.EndsWith("SelectionUndoProject/Assets"))throw new Exception("Disposable project required");
  try {
   var shapes=new[]{("circle32",Ring(32),true),("ellipse32",Ring(32,3),true),("odd17",Ring(17),true),("concave",new[]{new Vector2(0,0),new Vector2(2,0),new Vector2(2,1),new Vector2(1,1),new Vector2(1,2),new Vector2(0,2)},false),("rectangle",new[]{new Vector2(-2,-1),new Vector2(-1,-1),new Vector2(0,-1),new Vector2(1,-1),new Vector2(2,-1),new Vector2(2,0),new Vector2(2,1),new Vector2(1,1),new Vector2(0,1),new Vector2(-1,1),new Vector2(-2,1),new Vector2(-2,0)},true)};
   foreach(var shape in shapes){var n=Prism(shape.Item2);try{var p=new Plane(Vector3.forward,Vector3.zero);StudioMeshCuts.Bisect(n,p,Matrix4x4.identity,.01f,true);Validate(n,p,shape.Item1,shape.Item3);}finally{Object.DestroyImmediate(n.mesh);}}
   var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/JetSlice/fighterJetCutTest.fbx");Check(asset,"jet asset");
   var nodes=(List<StudioModelDocument.Node>)typeof(StudioModelDocument).GetMethod("CaptureNodes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{asset});
   var node=nodes.Where(n=>n.mesh).OrderByDescending(n=>n.mesh.vertexCount).First();var world=Matrix4x4.identity;for(int at=nodes.IndexOf(node);at>=0;at=nodes[at].parent){var n=nodes[at];world=Matrix4x4.TRS(n.position+n.positionOffset,n.rotation,n.scale)*world;}
   node.mesh.vertices=node.mesh.vertices.Select(world.MultiplyPoint3x4).ToArray();node.mesh.normals=node.mesh.normals.Select(n=>world.inverse.transpose.MultiplyVector(n).normalized).ToArray();node.mesh.RecalculateBounds();var source=StudioModifierSource.Capture(node);
   foreach(var direction in new[]{Vector3.forward,new Vector3(.21f,.17f,1).normalized}){
    var n=source.Restore();try{var p=new Plane(direction,n.mesh.bounds.center+direction*n.mesh.bounds.size.magnitude*.01237f);StudioMeshCuts.Bisect(n,p,Matrix4x4.identity,.01f,true);Validate(n,p,direction==Vector3.forward?"jet":"jet-angle",false);}finally{Object.DestroyImmediate(n.mesh);}}
   foreach(var n in nodes.Where(n=>n.mesh))Object.DestroyImmediate(n.mesh);
   typeof(StudioBisectSplitValidation).GetMethod("Run",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
   evidence+=" Separation regression: "+File.ReadAllText(Out("bisect-split-result.txt"));
   File.WriteAllText(Out("bisect-grid-result.txt"),"PASS "+checks+" checks. "+evidence);
  }catch(Exception e){File.WriteAllText(Out("bisect-grid-result.txt"),"FAIL after "+checks+" checks. "+evidence+"\n"+e);}
 }
 static void Capture(StudioModelDocument.Node n,Plane plane,int[] cap,string label)
 {
  var doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.nodes.Add(n);var editor=new StudioModelEditor(()=>{});editor.Load(doc);
  var renderer=editor.Transforms[0].GetComponent<MeshRenderer>();var material=new Material(Shader.Find("Hidden/FrameSubdivisionCheck"));var lineMaterial=new Material(Shader.Find("Hidden/Internal-Colored"));
  var cameraObject=new GameObject("Cap check camera");var camera=cameraObject.AddComponent<Camera>();var linesObject=new GameObject("Authored cap edges");var lines=new Mesh();
  var target=new RenderTexture(700,700,24);Texture2D image=null;var active=RenderTexture.active;
  var pipeline=UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
  try{
   UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;
   var positions=n.mesh.vertices;var center=cap.Aggregate(Vector3.zero,(a,v)=>a+positions[v])/cap.Length;float radius=cap.Max(v=>(positions[v]-center).magnitude);
   var topology=new StudioMeshCuts(n);var capSet=new HashSet<int>(cap);var indices=new List<int>();foreach(var f in topology.Faces.Where(f=>f.Vertices.All(capSet.Contains)))for(int i=0;i<f.Vertices.Count;i++){indices.Add(f.Vertices[i]);indices.Add(f.Vertices[(i+1)%f.Vertices.Count]);}
   // Render from the removed band toward the negative half; clip away the far positive shell.
   camera.scene=editor.Scene;camera.enabled=false;camera.transform.position=center+plane.normal*.004f;camera.transform.rotation=Quaternion.LookRotation(-plane.normal,Mathf.Abs(Vector3.Dot(plane.normal,Vector3.up))>.95f?Vector3.right:Vector3.up);
   camera.orthographic=true;camera.orthographicSize=radius*1.08f;camera.nearClipPlane=.0001f;camera.farClipPlane=radius*5;camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.08f,.1f);
   renderer.gameObject.layer=30;renderer.sharedMaterials=Enumerable.Repeat(material,n.mesh.subMeshCount).ToArray();
   UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(linesObject,editor.Scene);linesObject.layer=30;lines.vertices=positions.Select(p=>p+plane.normal*.00005f).ToArray();lines.SetIndices(indices.ToArray(),MeshTopology.Lines,0);lines.colors=Enumerable.Repeat(new Color(.05f,.2f,.25f),positions.Length).ToArray();linesObject.AddComponent<MeshFilter>().sharedMesh=lines;linesObject.AddComponent<MeshRenderer>().sharedMaterial=lineMaterial;
   target.Create();camera.targetTexture=target;camera.Render();RenderTexture.active=target;image=new Texture2D(700,700,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,700,700),0,0);image.Apply();File.WriteAllBytes(Out("bisect-grid-"+label+".png"),image.EncodeToPNG());
   Check(image.GetPixels32().Count(c=>c.r>80||c.g>80||c.b>80)>100,label+" rendered cap pixels");
  }finally{RenderTexture.active=active;UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(linesObject);Object.DestroyImmediate(lines);Object.DestroyImmediate(material);Object.DestroyImmediate(lineMaterial);editor.Dispose();Object.DestroyImmediate(doc);}
 }
}


