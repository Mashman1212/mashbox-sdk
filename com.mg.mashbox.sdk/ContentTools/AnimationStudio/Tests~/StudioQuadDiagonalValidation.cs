using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEngine;using UnityEngine.Rendering;
namespace MashBoxSDK.AnimationStudio {
 internal static class StudioQuadDiagonalValidation {
  const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
  static string Out(string n)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../QuadDiagonals/"+n));
  [InitializeOnLoadMethod]static void Boot(){if(Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets")&&File.Exists(Out("request.txt")))EditorApplication.update+=Next;}
  static void Next(){EditorApplication.update-=Next;Run();}
  static void Run(){File.Delete(Out("request.txt"));var log=new List<string>();var doc=ScriptableObject.CreateInstance<StudioModelDocument>();var mesh=new Mesh();
   void Check(bool v,string m){if(!v)throw new Exception(m);log.Add("PASS "+m);}
   try {
    var points=new List<Vector3>();var tris=new List<int>();var polygons=new List<int>();
    for(int q=0;q<5;q++){int k=points.Count;float x=(q-2)*2;float warp=q==0?0:q==1?.2f:q==2?1.2f:q==3?-.2f:-1.2f;points.AddRange(new[]{new Vector3(x-.7f,-.8f,0),new Vector3(x+.7f,-.8f,0),new Vector3(x+.7f,.8f,warp),new Vector3(x-.7f,.8f,0)});tris.AddRange(new[]{k,k+1,k+2,k,k+2,k+3});polygons.AddRange(new[]{0,4,k,k+1,k+2,k+3});}
    mesh.SetVertices(points);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();var node=new StudioModelDocument.Node{name="Planar, gentle and steep quads",mesh=mesh,cutPolygons=polygons.ToArray(),cutTriangles=mesh.triangles};doc.nodes.Add(node);
    float Opacity(int q){int k=q*4;return StudioModelEditor.QuadDiagonalOpacity(points[k],points[k+1],points[k+2],points[k],points[k+2],points[k+3]);}
    Check(Opacity(0)==0,"Planar quad has no overlay");Check(Opacity(1)>0&&Opacity(2)>Opacity(1),"Stronger bends have greater opacity");Check(Math.Abs(Opacity(1)-Opacity(3))<.0001f&&Math.Abs(Opacity(2)-Opacity(4))<.0001f,"Matched concave and convex bends have equal opacity");
    using(var editor=new StudioModelEditor(()=>{})){
     editor.Load(doc);var flag=typeof(StudioModelEditor).GetField("showNonPlanarQuadDiagonals",F);flag.SetValue(editor,false);
     Check(editor.ToolbarCommands(null).Any(c=>c.Key=="View/Non-planar Quad Diagonals"),"View toggle is available to shortcut and toolbar discovery");
     var originalModifiers=node.modifiers;var original=mesh.vertices;var triangles=mesh.triangles;var faces=node.cutPolygons.ToArray();var off=Capture(editor,node,false,"off.png");var on=Capture(editor,node,true,"on.png");
     int Orange(Color32 c)=>c.r>c.g*1.4f&&c.r>c.b*1.5f&&c.r>90?1:0;
     Check(on.Sum(Orange)>off.Sum(Orange)+40,"Actual renderer adds red-orange diagnostic diagonals");foreach(int hits in lastCoverage)Check(hits>=18,"Visible folded diagonal remains continuous across at least 18 of 20 samples");
     Check(original.SequenceEqual(mesh.vertices)&&triangles.SequenceEqual(mesh.triangles)&&faces.SequenceEqual(node.cutPolygons),"Display overlay preserves vertices, render tessellation and authored faces");
     Check(editor.Components.Count==0&&ReferenceEquals(node.modifiers,originalModifiers),"Display overlay creates no selection or modifier history");
     var cache=(System.Collections.IDictionary)typeof(StudioModelEditor).GetField("selectionWireframes",F).GetValue(editor);var wire=cache.Values.Cast<object>().Single();var overlay=wire.GetType().GetField("quadOverlay",F).GetValue(wire);var buffers=(Vector3[][])overlay.GetType().GetField("lines",F).GetValue(overlay);var savedBuffers=(Vector3[][])buffers.Clone();var again=Capture(editor,node,true,"cached.png");Check(again.SequenceEqual(on),"Cached redraw produces identical native pixels");
     Check(Enumerable.Range(0,16).All(i=>ReferenceEquals(savedBuffers[i],buffers[i])),"Cached redraw reuses line buffers without rebuilding");
    }
   } catch(Exception e){log.Add("FAIL "+e);}finally{UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(doc);File.WriteAllLines(Out("result.txt"),log);}
  }
  static readonly List<int> lastCoverage=new List<int>();
  static Color32[] Capture(StudioModelEditor editor,StudioModelDocument.Node node,bool enabled,string file){
   typeof(StudioModelEditor).GetField("showNonPlanarQuadDiagonals",F).SetValue(editor,enabled);
   var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var root=new GameObject("Quad diagnostics capture");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);root.layer=31;root.AddComponent<MeshFilter>().sharedMesh=node.mesh;
   var material=new Material(Shader.Find("Hidden/BevelHoleProbe"));material.SetColor("_Color",new Color(.55f,.55f,.55f,1));root.AddComponent<MeshRenderer>().sharedMaterial=material;
   var cameraObject=new GameObject("Capture camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,scene);var camera=cameraObject.AddComponent<Camera>();camera.scene=scene;camera.enabled=false;camera.cullingMask=1<<31;camera.transform.position=new Vector3(0,0,-8);camera.transform.rotation=Quaternion.identity;camera.fieldOfView=32;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.12f,.12f,1);camera.nearClipPlane=.01f;camera.farClipPlane=100;var rt=new RenderTexture(1280,480,24);rt.Create();camera.targetTexture=rt;
   var oldRT=RenderTexture.active;var pipeline=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
   Exception renderError=null;void Overlay(Camera rendered){if(rendered!=camera)return;var oldEvent=Event.current;try{Event.current=new Event{type=EventType.Repaint};Handles.SetCamera(camera);typeof(StudioModelEditor).GetMethod("DrawVisibleObjectWireframes",F).Invoke(editor,new object[]{camera});}catch(Exception e){renderError=e;}finally{Event.current=oldEvent;}}
   Camera.onPostRender+=Overlay;
   try{GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;camera.Render();if(renderError!=null)throw renderError;RenderTexture.active=rt;var image=new Texture2D(1280,480,TextureFormat.RGBA32,false);try{image.ReadPixels(new Rect(0,0,1280,480),0,0);image.Apply();File.WriteAllBytes(Out(file),image.EncodeToPNG());var pixels=image.GetPixels32();lastCoverage.Clear();if(enabled)for(int q=1;q<5;q++){var vertices=node.mesh.vertices;var a=camera.WorldToScreenPoint(vertices[q*4]);var b=camera.WorldToScreenPoint(vertices[q*4+2]);int hits=0;for(int sample=0;sample<20;sample++){var spot=Vector3.Lerp(a,b,.12f+.76f*sample/19);bool red=false;for(int dy=-3;dy<=3;dy++)for(int dx=-3;dx<=3;dx++){int x=Mathf.RoundToInt(spot.x)+dx,y=Mathf.RoundToInt(spot.y)+dy;if(x<0||x>=1280||y<0||y>=480)continue;var c=pixels[y*1280+x];red|=c.r>c.g*1.4f&&c.r>c.b*1.5f&&c.r>90;}if(red)hits++;}lastCoverage.Add(hits);}return pixels;}finally{UnityEngine.Object.DestroyImmediate(image);}}
   finally{Camera.onPostRender-=Overlay;RenderTexture.active=oldRT;GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(material);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
  }
 }
}
