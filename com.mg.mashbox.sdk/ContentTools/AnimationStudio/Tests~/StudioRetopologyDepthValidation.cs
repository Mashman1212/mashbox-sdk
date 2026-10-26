using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
namespace MashBoxSDK.AnimationStudio {
public static class DepthValidation {
public static void Run(){var results=new List<string>();int exit=1;GameObject go=null;Mesh quad=null;Material solid=null,xray=null;RenderTexture rt=null;Texture2D pixels=null;
try{
GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;
var shader=Shader.Find("Hidden/MashBox/RetopologyXray");if(!shader)throw new Exception("Shader missing");
go=new GameObject("X-ray depth test");var camera=go.AddComponent<Camera>();camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.nearClipPlane=.1f;camera.farClipPlane=20;camera.orthographicSize=1;camera.cullingMask=0;
rt=new RenderTexture(64,64,24,RenderTextureFormat.ARGB32);rt.Create();camera.targetTexture=rt;pixels=new Texture2D(64,64,TextureFormat.RGBA32,false);
quad=new Mesh();quad.vertices=new[]{new Vector3(-.6f,-.6f,0),new Vector3(.6f,-.6f,0),new Vector3(.6f,.6f,0),new Vector3(-.6f,.6f,0)};quad.triangles=new[]{0,1,2,0,2,3};
solid=new Material(Shader.Find("Hidden/Internal-Colored"));solid.SetColor("_Color",new Color(.2f,.2f,.2f,1));solid.SetInt("_ZWrite",1);solid.SetInt("_ZTest",(int)CompareFunction.LessEqual);solid.SetInt("_Cull",0);solid.SetInt("_SrcBlend",1);solid.SetInt("_DstBlend",0);
xray=new Material(shader);xray.SetColor("_Color",Color.red);xray.SetFloat("_Near",.1f);
foreach(bool ortho in new[]{false,true})foreach(bool near in new[]{true,false}){
camera.orthographic=ortho;xray.SetFloat("_Depth",.05f);
using(var cb=new CommandBuffer()){
cb.DrawMesh(quad,Matrix4x4.Translate(new Vector3(0,0,2)),solid);
cb.DrawMesh(quad,Matrix4x4.Translate(new Vector3(0,0,near?2.02f:2.3f)),xray);
camera.AddCommandBuffer(CameraEvent.AfterEverything,cb);camera.Render();camera.RemoveCommandBuffer(CameraEvent.AfterEverything,cb);
}
var old=RenderTexture.active;RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,64,64),0,0);pixels.Apply();RenderTexture.active=old;
var color=pixels.GetPixel(32,32);bool red=color.r>.8f&&color.g<.1f;
if(red!=near)throw new Exception($"Depth test failed ortho={ortho} near={near} pixel={color}");
results.Add($"PASS {(ortho?"Orthographic":"Perspective")} {(near?"near patch visible":"far patch hidden")}");
}
foreach(var message in ShaderUtil.GetShaderMessages(shader))if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)throw new Exception(message.message);
results.Add("PASS shader compilation");results.Add("SUCCESS");exit=0;
}catch(Exception e){results.Add("FAIL "+e);}finally{if(go)UnityEngine.Object.DestroyImmediate(go);if(quad)UnityEngine.Object.DestroyImmediate(quad);if(solid)UnityEngine.Object.DestroyImmediate(solid);if(xray)UnityEngine.Object.DestroyImmediate(xray);if(rt)UnityEngine.Object.DestroyImmediate(rt);if(pixels)UnityEngine.Object.DestroyImmediate(pixels);File.WriteAllLines("depth-results.txt",results);EditorApplication.Exit(exit);}
}
}}
