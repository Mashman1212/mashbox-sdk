using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
public static class StudioPickingValidation
{
 public static void Run(){int exit=0;try{Validate();}catch(Exception e){exit=1;Debug.LogException(e);}EditorApplication.Exit(exit);}

 static int checks;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
 public static void Validate(){
 string result=Path.GetFullPath(Path.Combine(Application.dataPath,"../../picking-result.txt"));
 var meshes=new List<Mesh>();int exit=0;
 try{
 var flags=BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 var type=typeof(StudioModelEditor).GetNestedType("OcclusionMesh",BindingFlags.NonPublic);
 var query=typeof(StudioModelEditor).GetMethods(flags).Single(m=>m.Name=="Occluded"&&m.IsStatic);
 var random=new System.Random(12419);
 float Next(float min,float max)=>(float)(min+random.NextDouble()*(max-min));
 var entries=Array.CreateInstance(type,12);var matrices=new Matrix4x4[12];
 for(int n=0;n<12;n++){
 var vertices=Enumerable.Range(0,600).Select(i=>new Vector3(Next(-1,1),Next(-1,1),Next(-1,1))).ToArray();
 var mesh=new Mesh();mesh.vertices=vertices;mesh.triangles=Enumerable.Range(0,vertices.Length).ToArray();mesh.RecalculateBounds();meshes.Add(mesh);
 matrices[n]=Matrix4x4.TRS(new Vector3(Next(-4,4),Next(-4,4),Next(-4,4)),Quaternion.Euler(Next(0,360),Next(0,360),Next(0,360)),new Vector3(n%2==0?-.5f:2.5f,.15f+n*.2f,1.7f));
 var entry=Activator.CreateInstance(type,true);type.GetField("Mesh",flags).SetValue(entry,mesh);type.GetField("Bounds",flags).SetValue(entry,mesh.bounds);type.GetField("WorldToLocal",flags).SetValue(entry,matrices[n].inverse);entries.SetValue(entry,n);
 }
 bool Query(Ray ray,float limit)=>(bool)query.Invoke(null,new object[]{ray,limit,entries});
 Check(!Query(new Ray(new Vector3(100,100,100),Vector3.right),30),"Bounds-miss rays are clear");
 foreach(var entry in entries)Check(type.GetField("Vertices",flags).GetValue(entry)==null,"Bounds misses do not copy mesh arrays");
 var batchRays=new List<Ray>();var batchLimits=new List<float>();var batchExpected=new List<bool>();var watch=Stopwatch.StartNew();
 for(int r=0;r<500;r++){
 var ray=new Ray(new Vector3(Next(-8,8),Next(-8,8),Next(-8,8)),new Vector3(Next(-1,1),Next(-1,1),Next(-1,1)).normalized);float limit=Next(0,20);bool expected=false;
 for(int n=0;n<meshes.Count&&!expected;n++){
 var v=meshes[n].vertices;var tr=meshes[n].triangles;var matrix=matrices[n];
 for(int i=0;i<tr.Length;i+=3)if(StudioModelEditor.RayTriangle(ray,matrix.MultiplyPoint3x4(v[tr[i]]),matrix.MultiplyPoint3x4(v[tr[i+1]]),matrix.MultiplyPoint3x4(v[tr[i+2]]),out float distance)&&distance<limit){expected=true;break;}
 }
 batchRays.Add(ray);batchLimits.Add(limit);batchExpected.Add(expected);Check(Query(ray,limit)==expected,"Occlusion matches world-space reference with rotated, mirrored and nonuniformly scaled meshes");
 }
 watch.Stop();
 var batch=typeof(StudioModelEditor).GetMethod("OccludedBatch",flags);
 var cold=Array.CreateInstance(type,12);for(int n=0;n<12;n++){var e=Activator.CreateInstance(type,true);type.GetField("Mesh",flags).SetValue(e,meshes[n]);type.GetField("Bounds",flags).SetValue(e,meshes[n].bounds);type.GetField("WorldToLocal",flags).SetValue(e,matrices[n].inverse);cold.SetValue(e,n);}
 foreach(var source in new[]{cold,entries}){var actual=(bool[])batch.Invoke(null,new object[]{batchRays.ToArray(),batchLimits.ToArray(),source});for(int i=0;i<actual.Length;i++)Check(actual[i]==batchExpected[i],"Cold/warm batched visibility matches reference");}
 Check(((bool[])batch.Invoke(null,new object[]{Array.Empty<Ray>(),Array.Empty<float>(),entries})).Length==0,"Empty marquee has no visibility work");

 foreach(var entry in entries){var data=type.GetField("Vertices",flags).GetValue(entry);Query(new Ray(Vector3.zero,Vector3.forward),20);if(data!=null)Check(ReferenceEquals(data,type.GetField("Vertices",flags).GetValue(entry)),"Mesh arrays reused within pick");}
 Check(!Query(new Ray(Vector3.zero,Vector3.forward),-1),"Behind-camera limits are clear");
 File.WriteAllText(result,"PASS: "+checks+" picking assertions; 500 accelerated/reference ray comparisons in "+watch.ElapsedMilliseconds+" ms. This is a geometry correctness run, not an interactive hover timing.");
 }catch(Exception e){exit=1;File.WriteAllText(result,e.ToString());}
 finally{foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);}
 if(exit != 0) throw new Exception("StudioPickingValidation failed; see result file");
 }
}
