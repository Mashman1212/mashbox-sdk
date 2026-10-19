// Copy into a Unity Editor test assembly to invoke StudioVertexAOValidation.Run().
// Kept outside imported SDK code; creates no persistent assets.
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using MashBoxSDK.MapTools;
using Object=UnityEngine.Object;
public static class StudioVertexAOValidation
{
 static void Check(bool b,string m){if(!b)throw new Exception(m);}
 static Mesh Plane(){var m=new Mesh();m.vertices=new[]{new Vector3(-1,0,-1),new Vector3(-1,0,1),new Vector3(1,0,1),new Vector3(1,0,-1)};m.triangles=new[]{0,1,2,0,2,3};m.uv=new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right};m.uv2=Enumerable.Range(1,4).Select(i=>new Vector2(i,4)).ToArray();m.colors=Enumerable.Repeat(new Color(.5f,.2f,.3f,.7f),4).ToArray();m.RecalculateNormals();m.RecalculateBounds();return m;}
 public static void Run()
 {
  var m=Plane();var other=Object.Instantiate(m);Texture2DArray array=null;
  try{
   var settings=new StudioVertexAO.Settings{samples=64,relativeDistance=1};var values=StudioVertexAO.Sample(m,settings);Check(values.All(v=>v>.99f),"Open plane must be unoccluded");
   var colors=StudioVertexAO.RedColors(m,new[]{0f,.25f,.5f,1f});Check(colors.All(c=>c.g==.2f&&c.b==.3f&&c.a==.7f),"GBA changed");
   bool cancelled=false;try{StudioVertexAO.Sample(m,settings,p=>true);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"Cancellation failed");
   m.colors=colors;other.colors=Enumerable.Repeat(new Color(1,.8f,.9f,.6f),4).ToArray();
   var blend=StudioBlendShape.Capture(new[]{m,other},new[]{"A","B"},new[]{Matrix4x4.identity,Matrix4x4.identity});blend.SetMaster(.5f);blend.Apply(m);Check(Mathf.Abs(m.colors[0].r-.5f)<1e-6f&&m.colors[0].g==.2f,"AO interpolation or GBA preservation failed");blend.SetMaster(1);blend.Apply(m);Check(m.colors.All(c=>c.r==1),"Blend endpoint failed");
   var shapes=Enumerable.Range(0,8).Select(i=>MGGrassShapeBaker.Read(i==0?m:other,Matrix4x4.identity)).ToArray();shapes[3].colors=null;array=MGGrassShapeBaker.BuildAO(shapes);Check(array.format==TextureFormat.R8&&array.depth==8&&array.mipmapCount==1&&!array.isDataSRGB,"AO layout wrong");Check(array.GetPixelData<byte>(0,3).All(v=>v==255),"Missing AO must be white");
   var covered=Object.Instantiate(m);
   try {
    var points=m.vertices.Concat(m.vertices.Select(v=>new Vector3(v.x*10,.2f,v.z*10))).ToArray();covered.vertices=points;covered.triangles=m.triangles.Concat(m.triangles.Select(i=>i+4)).ToArray();covered.RecalculateNormals();covered.RecalculateBounds();
    var occ=VDPrecisionBaker.SampleIsolated(covered,2,.001f,64,0,true);Check(occ.Take(4).Average()<.2f,"Cover must occlude lower plane");
   }finally{Object.DestroyImmediate(covered);}
  }finally{Object.DestroyImmediate(m);Object.DestroyImmediate(other);if(array)Object.DestroyImmediate(array);}
 }
}
