using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.Cloth;
namespace MashBoxSDK.AnimationStudio {
public static class StudioCloth30HzValidation {
public static void Run(){var log=new List<string>();MashBoxClothProfile profile=null;
try{
var vertices=new List<Vector3>();var triangles=new List<int>();const int n=12;
for(int row=0;row<2;row++)for(int i=0;i<n;i++){float a=i*Mathf.PI*2/n;vertices.Add(new Vector3(Mathf.Cos(a)*.1f,.15f+row*.7f,Mathf.Sin(a)*.1f));}
for(int i=0;i<n;i++){int j=(i+1)%n;triangles.AddRange(new[]{i,j,n+i,j,n+j,n+i});}
var topology=new MashBoxClothTopology(vertices.ToArray(),triangles.ToArray(),32);var motion=new float[vertices.Count];for(int i=0;i<motion.Length;i++)motion[i]=.12f;
profile=ScriptableObject.CreateInstance<MashBoxClothProfile>();profile.frequency=30;profile.substeps=4;profile.iterations=4;profile.animationFollow=.75f;profile.animationInertia=.5f;profile.gravity=profile.wind=Vector3.zero;profile.thickness=.008f;
using(var solver=new MashBoxClothSolver(topology)){
solver.SetTargets(vertices,Matrix4x4.identity,motion);solver.Reset(true);
float minSide=1,minClearance=1,maxDisplacement=0;
for(int frame=0;frame<180;frame++){
float angle=70*Mathf.Sin(frame/30f*2*Mathf.PI*2);var rotation=Quaternion.Euler(0,0,angle);var matrix=Matrix4x4.Rotate(rotation);var cap=new ClothCapsule(Vector3.zero,rotation*Vector3.up,.08f);
solver.SetTargets(vertices,matrix,motion);solver.Step(1f/30,frame/30f,profile,new[]{cap},1);
for(int i=0;i<vertices.Count;i++){
Vector3 point=solver.Position(topology.vertexParticle[i]);var local=Quaternion.Inverse(rotation)*point;var radial=new Vector3(local.x,0,local.z);var original=new Vector3(vertices[i].x,0,vertices[i].z);
minSide=Mathf.Min(minSide,Vector3.Dot(radial.normalized,original.normalized));minClearance=Mathf.Min(minClearance,cap.Distance(point,profile.thickness,out _));maxDisplacement=Mathf.Max(maxDisplacement,(point-matrix.MultiplyPoint3x4(vertices[i])).magnitude);
if(float.IsNaN(point.x)||float.IsInfinity(point.x))throw new Exception("Nonfinite simulation");
}}
log.Add("30 Hz, four substeps, four iterations, 180 fast swing frames");log.Add("Minimum original-side alignment: "+minSide);log.Add("Minimum clearance: "+minClearance);log.Add("Maximum target displacement: "+maxDisplacement);
if(minSide<0)throw new Exception("Particle crossed to opposite side of leg");if(minClearance<-.001f)throw new Exception("Particle penetrated collider");log.Add("SUCCESS");}
}catch(Exception e){log.Add("FAIL "+e);}finally{if(profile)UnityEngine.Object.DestroyImmediate(profile);File.WriteAllLines("Library/cloth-30hz-validation.txt",log);}
}}
}
