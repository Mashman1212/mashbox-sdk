using System;using System.IO;using System.Linq;using System.Reflection;using MashBoxSDK.AnimationStudio;using UnityEditor;using UnityEngine;using UnityEngine.SceneManagement;using Object=UnityEngine.Object;
public static class StudioLiveSurfaceValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
 static int checks;static void Check(bool b,string m){if(!b)throw new Exception(m);checks++;}
 static object Get(object o,string n)=>o.GetType().GetField(n,F).GetValue(o);
 static object BevelKind()=>Kind("Bevel");
 static object Kind(string kind)=>Enum.Parse(typeof(StudioModelEditor).GetNestedType("LiveTool",F),kind);
 static void Evaluate(StudioModelEditor editor, string kind="Bevel")=>typeof(StudioModelEditor).GetMethod("EvaluateLiveTool",F).Invoke(editor,new[]{Kind(kind)});
 static void Cancel(StudioModelEditor editor)=>typeof(StudioModelEditor).GetMethod("CancelLiveTool",F,null,new[]{typeof(bool)},null).Invoke(editor,new object[]{false});
 static Color32[] Pixels(Camera camera,RenderTexture rt)
 {var old=RenderTexture.active;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);try{camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();return tex.GetPixels32();}finally{RenderTexture.active=old;Object.DestroyImmediate(tex);}}
 static bool Same(Color32[] a,Color32[] b)=>a.SequenceEqual(b);
 public static void Run()
 {
  StudioModelEditor editor=null;StudioModelDocument doc=null;Mesh mesh=null;Material material=null;RenderTexture rt=null;
  try{
   doc=ScriptableObject.CreateInstance<StudioModelDocument>();mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);doc.nodes.Add(new StudioModelDocument.Node{name="Live surface",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles});
   editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);var edges=(System.Collections.Generic.List<(int a,int b)>)typeof(StudioModelEditor).GetMethod("Edges",F).Invoke(editor,null);for(int i=0;i<edges.Count;i++)editor.Components.Add(i);
   var go=new GameObject("Surface check camera");var camera=go.AddComponent<Camera>();camera.scene=editor.Scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.orthographic=true;camera.orthographicSize=.85f;camera.transform.position=Vector3.one*2;camera.transform.LookAt(Vector3.zero);camera.cullingMask=1<<30;camera.enabled=false;camera.useOcclusionCulling=false;
   var filter=editor.Transforms[0].GetComponent<MeshFilter>();var renderer=filter.GetComponent<MeshRenderer>();filter.gameObject.layer=30;material=new Material(Shader.Find("Unlit/Color"));material.color=Color.white;renderer.sharedMaterial=material;rt=new RenderTexture(160,160,24);rt.Create();camera.targetTexture=rt;
   var source=Pixels(camera,rt);Check(source.Any(c=>c.r>200),"Renderer draws source before preview");
   ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.4f);ToolChannelPane.SetFloatDefault("Bevel Edges","Subdivisions",3);ToolChannelPane.SetFloatDefault("Bevel Edges","Tension",1);ToolChannelPane.SetFloatDefault("Bevel Edges","Junction",0);Evaluate(editor);
   var preview=(StudioMeshToolPreview)Get(editor,"livePreview");var shaded=Pixels(camera,rt);filter.sharedMesh=preview.Targets[0].Work.mesh;var expected=Pixels(camera,rt);filter.sharedMesh=mesh;
   Check(!Same(source,expected),"Bevel changes rendered silhouette");Check(Same(shaded,expected),"Existing renderer displays new shaded surface, not original source");
   ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.25f);Evaluate(editor);shaded=Pixels(camera,rt);filter.sharedMesh=preview.Targets[0].Work.mesh;expected=Pixels(camera,rt);filter.sharedMesh=mesh;Check(Same(shaded,expected),"Next parameter edit refreshes existing shaded buffers");
   Cancel(editor);Check(Same(source,Pixels(camera,rt)),"Cancel restores original rendered surface");Check(editor.Transforms[0].GetComponentsInChildren<Renderer>(true).Length==1,"Only one renderer exists for edited object");
   editor.Components.Clear();for(int i=0;i<edges.Count;i++)editor.Components.Add(i);
   ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.35f);Evaluate(editor);preview=(StudioMeshToolPreview)Get(editor,"livePreview");filter.sharedMesh=preview.Targets[0].Work.mesh;expected=Pixels(camera,rt);filter.sharedMesh=mesh;
   typeof(StudioModelEditor).GetMethod("ApplyLiveTool",F).Invoke(editor,new[]{BevelKind()});Check(Same(expected,Pixels(camera,rt)),"Committed bevel surface updates without a face drag");
   editor.PerformWorkspaceHistory(false);filter=editor.Transforms[0].GetComponent<MeshFilter>();filter.gameObject.layer=30;filter.GetComponent<MeshRenderer>().sharedMaterial=material;Check(Same(source,Pixels(camera,rt)),"Undo restores source shading");
   editor.PerformWorkspaceHistory(true);filter=editor.Transforms[0].GetComponent<MeshFilter>();filter.gameObject.layer=30;filter.GetComponent<MeshRenderer>().sharedMaterial=material;Check(Same(expected,Pixels(camera,rt)),"Redo restores committed bevel shading");
   var committed=Pixels(camera,rt);editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);editor.Components.Clear();editor.Components.Add(0);Evaluate(editor,"Extrude");preview=(StudioMeshToolPreview)Get(editor,"livePreview");
   shaded=Pixels(camera,rt);filter.sharedMesh=preview.Targets[0].Work.mesh;expected=Pixels(camera,rt);filter.sharedMesh=mesh;Check(Same(shaded,expected),"Extrusion topology publishes to shaded renderer immediately");
   foreach(float distance in new[]{.25f,.45f,-.2f})
   {
    typeof(StudioModelEditor).GetField("extrusionDistance",F).SetValue(editor,distance);Evaluate(editor,"Extrude");
    shaded=Pixels(camera,rt);filter.sharedMesh=preview.Targets[0].Work.mesh;expected=Pixels(camera,rt);filter.sharedMesh=mesh;
    Check(Same(shaded,expected),"Extrusion distance stream edits update shaded buffers at "+distance);
   }
   Cancel(editor);Check(Same(committed,Pixels(camera,rt)),"Extrusion cancel restores committed shaded bevel");
   File.WriteAllText(Path.Combine(Application.dataPath,"../live-surface-result.txt"),"PASS "+checks+" rendered surface assertions");editor.Dispose();Object.DestroyImmediate(mesh);Object.DestroyImmediate(doc);Object.DestroyImmediate(material);Object.DestroyImmediate(rt);Object.DestroyImmediate(go);EditorApplication.Exit(0);
  }catch(Exception e){File.WriteAllText(Path.Combine(Application.dataPath,"../live-surface-result.txt"),"FAIL "+e);editor?.Dispose();EditorApplication.Exit(1);}
 }
}
