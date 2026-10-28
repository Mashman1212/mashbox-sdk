#if MASHBOX_STUDIO_TESTS
using System;using System.IO;using System.Linq;using System.Reflection;using UnityEditor;using UnityEngine;using UnityEngine.Rendering;using MashBoxSDK.AnimationStudio;
public static class StudioShaderSplitValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;static AnimationStudioWindow host;static MashBoxStudioViewport view;static StudioModelEditor editor;static StudioModelDocument doc;static Material source;static Texture2D blue;static int phase,checks,warmup;static double ready,timeout;
 static void Check(bool pass,string text){if(!pass)throw new Exception(text);checks++;}
 static Shader Graph(string guid){var shader=AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));Check(shader&&!ShaderUtil.ShaderHasError(shader),"Graph import/compile failed: "+guid);return shader;}
 public static void Run(){EditorApplication.delayCall+=Start;}
 static void Start()
 {
  try{
   ShaderUtil.allowAsyncCompilation=false;
   var modeling=Graph("409bba92b9fbb904cbfd7cf58b9531a6");var checker=Graph("f495d7739232e7b4b96ac1ee1dbbceb2");
   Check(StudioMeshMaterialPreview.DefaultMaterial.shader==modeling,"Default material shader assignment incorrect");Check(StudioMeshMaterialPreview.CheckerMaterial.shader==checker,"Checker material shader assignment incorrect");
   source=new Material(StudioMeshMaterialPreview.DefaultMaterial);Check(!source.HasProperty("_Checkerboard")&&!source.HasProperty("_CheckerboardMap"),"Modeling shader still exposes checker properties");Check(source.HasColor("_BaseColor")&&source.HasTexture("_BaseMap")&&source.HasFloat("_Smoothness")&&source.HasFloat("_Metallic"),"Modeling shader lost ordinary surface inputs");
   Check(StudioMeshMaterialPreview.CheckerMaterial.HasTexture("_CheckerboardMap")&&StudioMeshMaterialPreview.CheckerMaterial.GetTexture("_CheckerboardMap"),"Dedicated checker texture missing");
   var global=Type.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings, Unity.RenderPipelines.HighDefinition.Runtime");global.GetMethod("Ensure",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{true});
   var pipeline=(RenderPipelineAsset)ScriptableObject.CreateInstance(Type.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset, Unity.RenderPipelines.HighDefinition.Runtime"));AssetDatabase.CreateAsset(pipeline,AssetDatabase.GenerateUniqueAssetPath("Assets/ShaderSplitPipeline.asset"));GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
   blue=new Texture2D(2,2,TextureFormat.RGBA32,false,true);blue.SetPixels(Enumerable.Repeat(Color.blue,4).ToArray());blue.Apply();source.SetColor("_BaseColor",Color.red);source.SetTexture("_BaseMap",Texture2D.whiteTexture);
   doc=ScriptableObject.CreateInstance<StudioModelDocument>();var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);doc.nodes.Add(new StudioModelDocument.Node{mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles,materials=new[]{source}});doc.lighting.studioLights=false;doc.lighting.ground=false;
   AnimationStudioWindow.Open();host=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(host,new object[]{true});typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",F).Invoke(host,new object[]{doc});editor=host.ToolbarModelEditor;
   view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio==host);view.position=new Rect(60,60,850,650);view.modelGridVisible=false;view.LookAtDirect(Vector3.zero,Quaternion.Euler(20,35,0),2);
   editor.SetModelDrawMode(view,DrawCameraMode.Textured);editor.SetXRayMode(false);editor.SetCheckerMap(false);editor.SetDisplayMode(false,true);
   ready=EditorApplication.timeSinceStartup+2;timeout=ready+60;EditorApplication.update+=Tick;
  }catch(Exception e){Finish(e.ToString());}
 }
 static Color32[] Pixels()
 {
  var rt=typeof(MashBoxStudioViewport).GetField("cachedScene",F).GetValue(view) as RenderTexture;if(!rt||!rt.IsCreated())return null;
  var old=RenderTexture.active;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);
  try{RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../shader-split-"+phase+".png")),image.EncodeToPNG());return image.GetPixels32();}
  finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}
 }
 static void Tick()
 {
  if(EditorApplication.timeSinceStartup<ready)return;
  try{
   if(EditorApplication.timeSinceStartup>timeout)throw new Exception("Timed out waiting for native shader preview");
   if(warmup++<2){view.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);view.Repaint();ready=EditorApplication.timeSinceStartup+.6;return;}
   var pixels=Pixels();if(pixels==null){view.Repaint();ready=EditorApplication.timeSinceStartup+.4;return;}
   var material=editor.Transforms[0].GetComponent<Renderer>().sharedMaterial;
   if(phase==0){Check(pixels.Count(p=>p.r>100&&p.r>p.g*1.5&&p.r>p.b*1.5)>1000,"BaseColor not visible in camera preview");source.SetColor("_BaseColor",Color.white);source.SetTexture("_BaseMap",blue);editor.SetDisplayMode(false,true);phase++;}
   else if(phase==1){Check(material.GetTexture("_MainTex")==blue,"BaseMap not mapped to camera preview");Check(pixels.Count(p=>p.b>100&&p.b>p.r*1.5&&p.b>p.g*1.5)>1000,"BaseMap not visible in camera preview");editor.SetDisplayMode(false,false);phase++;}
   else if(phase==2){Check(pixels.Count(p=>p.b>100&&p.b>p.r*1.5&&p.b>p.g*1.5)<100,"Neutral override retained assigned BaseMap");editor.SetCheckerMap(true);phase++;}
   else if(phase==3){Check(material.GetTexture("_MainTex")==StudioMeshMaterialPreview.CheckerMaterial.GetTexture("_CheckerboardMap"),"Dedicated checker map not bound in camera preview");Check(pixels.Select(p=>(p.r/16)*256+(p.g/16)*16+p.b/16).Distinct().Count()>25,"Checker absent from native camera preview");Check(doc.nodes[0].materials[0]==source&&!editor.ToolbarTextures,"Checker override changed original assignment/texture toggle");editor.EditLighting(s=>s.studioLights=true);editor.SetDisplayMode(true,false);phase++;}
   else if(phase==4){Check(material.shader==StudioMeshMaterialPreview.CheckerMaterial.shader,"Studio lighting did not use the dedicated checker graph");Check(pixels.Select(p=>(p.r/16)*256+(p.g/16)*16+p.b/16).Distinct().Count()>25,"Checker absent from Studio lighting output");editor.SetCheckerMap(false);editor.SetDisplayMode(true,true);phase++;}
   else{Check(material.shader==source.shader,"Studio lighting did not use modeling graph");Check(material.GetTexture("_BaseMap")==blue,"Studio material lost its BaseMap override");Check(pixels.Count(p=>p.b>35&&p.b>p.r*1.3&&p.b>p.g*1.3)>500,"BaseMap not visible in native Studio output");Check(doc.nodes[0].materials[0]==source,"Diagnostic modes changed saved source material");Finish("PASS "+checks+" split shader imports/properties/material bindings and native camera/Studio/default/neutral/checker rendering checks");return;}
   warmup=0;ready=EditorApplication.timeSinceStartup+1;view.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
  }catch(Exception e){Finish(e.ToString());}
 }
 static void Finish(string result)
 {
  EditorApplication.update-=Tick;File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../shader-split-result.txt")),result);
  EditorApplication.delayCall+=()=>{if(view)view.Close();if(host)UnityEngine.Object.DestroyImmediate(host);if(source)UnityEngine.Object.DestroyImmediate(source);if(blue)UnityEngine.Object.DestroyImmediate(blue);EditorApplication.delayCall+=()=>EditorApplication.Exit(result.StartsWith("PASS")?0:1);};
 }
}
#endif
