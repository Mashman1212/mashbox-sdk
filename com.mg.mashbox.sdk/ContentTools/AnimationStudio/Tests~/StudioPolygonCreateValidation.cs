using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEngine;using UnityEngine.Rendering;
namespace MashBoxSDK.AnimationStudio {
 internal static class StudioPolygonCreateValidation {
  const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
  static string Out(string n)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../PolygonCreate/"+n));
  [InitializeOnLoadMethod]static void Boot(){if(Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets")&&File.Exists(Out("request.txt")))EditorApplication.update+=Next;}
  static void Next(){EditorApplication.update-=Next;Run();}
  static void Run(){File.Delete(Out("request.txt"));var log=new List<string>();void Check(bool v,string m){if(!v)throw new Exception(m);log.Add("PASS "+m);}
   try{
    var points=new[]{new Vector3(-1,-1,0),new Vector3(-1,1,0),new Vector3(0,.25f,0),new Vector3(1,1,0),new Vector3(1,-1,0)};
    var mesh=StudioModelDocument.BuildPolygon(points,out var polygons,out var center);
    try{
     Check(mesh.vertexCount==5&&mesh.triangles.Length==9,"Concave outline creates n-2 render triangles without a center vertex");
     Check(polygons.SequenceEqual(new[]{0,5,0,1,2,3,4}),"Authored n-gon retains every click in order");
     Check(mesh.vertices.Select(p=>p+center).Zip(points,(a,b)=>(a-b).sqrMagnitude<1e-10f).All(x=>x),"Centered pivot preserves all world vertex positions");
     Check(mesh.normals.All(n=>n.z<-.99f),"Clockwise screen outline faces the placing camera");
     float area=0;var v=mesh.vertices;var t=mesh.triangles;for(int i=0;i<t.Length;i+=3)area+=Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]).magnitude*.5f;
     Check(Mathf.Abs(area-3.25f)<1e-5f,"Concave triangulation covers polygon area without overlap");
     Check(Capture(mesh,-1,"front.png")>1000&&Capture(mesh,1,"back.png")<100,"Native backface culling displays clockwise front only");
    }finally{UnityEngine.Object.DestroyImmediate(mesh);}
    bool rejected=false;try{StudioModelDocument.BuildPolygon(points.Take(2).ToArray(),out _,out _);}catch(InvalidOperationException){rejected=true;}Check(rejected,"W rejects fewer than three vertices without creating geometry");
    var doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.SetWorkspacePath(Path.GetFullPath(Path.Combine(Application.dataPath,"../MashBoxStudio/Workspaces/polygon-test.workspace")));var editor=new StudioModelEditor(()=>{});editor.Load(doc);
    try{
     int index=doc.AddPolygon(points);var node=doc.nodes[index];Check(index==0&&new StudioMeshCuts(node).Faces.Single().Vertices.Count==5,"New standalone object exposes one authored face to modeling");
     UnityEditor.Undo.FlushUndoRecordObjects();StudioUndo.IncrementCurrentGroup();StudioUndo.PerformUndo();Check(doc.nodes.Count==0,"StudioUndo removes new polygon object");StudioUndo.PerformRedo();Check(doc.nodes.Count==1&&doc.nodes[0].mesh==node.mesh,"StudioRedo restores the owned mesh");
     var pending=(List<Vector3>)typeof(StudioModelEditor).GetField("polygonPoints",F).GetValue(editor);pending.AddRange(points);typeof(StudioModelEditor).GetField("cutTool",F).SetValue(editor,StudioModelEditor.CutTool.Polygon);
     var key=typeof(StudioModelEditor).GetMethod("PolygonKey",F);key.Invoke(editor,new object[]{new Event{type=EventType.KeyDown,keyCode=KeyCode.Backspace}});Check(pending.Count==4,"Backspace removes only the last placed vertex");
     key.Invoke(editor,new object[]{new Event{type=EventType.KeyDown,keyCode=KeyCode.Escape}});Check(pending.Count==0&&doc.nodes.Count==1,"Escape discards pending outline without changing existing objects");
     Check(new StudioPolygonPrimitivesToolbar().CreatePanelContent().childCount>=6,"Polygon primitive toolbar includes the new command");
    }finally{editor.Dispose();foreach(var n in doc.nodes)if(n.mesh)UnityEngine.Object.DestroyImmediate(n.mesh);UnityEngine.Object.DestroyImmediate(doc);}
   }catch(Exception e){log.Add("FAIL "+e);}File.WriteAllLines(Out("result.txt"),log);
  }
  static int Capture(Mesh mesh,int side,string file){
   var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var root=new GameObject("Polygon culling capture");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);root.layer=31;root.AddComponent<MeshFilter>().sharedMesh=mesh;var material=new Material(Shader.Find("Hidden/StudioFlipNormalsProbe"));root.AddComponent<MeshRenderer>().sharedMaterial=material;var cameraObject=new GameObject("Polygon camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,scene);var camera=cameraObject.AddComponent<Camera>();camera.scene=scene;camera.enabled=false;camera.cullingMask=1<<31;camera.transform.position=new Vector3(0,0,side*4);camera.transform.LookAt(Vector3.zero);camera.fieldOfView=45;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.nearClipPlane=.01f;camera.farClipPlane=100;var rt=new RenderTexture(256,256,24);rt.Create();camera.targetTexture=rt;var active=RenderTexture.active;var pipeline=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
   try{GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;camera.Render();RenderTexture.active=rt;var image=new Texture2D(256,256,TextureFormat.RGBA32,false);try{image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();File.WriteAllBytes(Out(file),image.EncodeToPNG());return image.GetPixels32().Count(c=>c.r>128);}finally{UnityEngine.Object.DestroyImmediate(image);}}
   finally{RenderTexture.active=active;GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(material);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
  }
 }
}
