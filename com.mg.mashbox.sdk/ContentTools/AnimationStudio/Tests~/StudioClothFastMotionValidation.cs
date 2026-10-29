using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Unity.Mathematics;
using MashBoxSDK.Cloth;
namespace MashBoxSDK.AnimationStudio {
public static class StudioClothFastMotionValidation {
 public static void Run(){var log=new List<string>();void Check(bool ok,string label){if(!ok)throw new Exception(label);log.Add("PASS "+label);}
 try{
 var verts=new List<Vector3>();var tris=new List<int>();const int w=6,h=21,n=w*h;
 for(int layer=0;layer<2;layer++)for(int y=0;y<h;y++)for(int x=0;x<w;x++)verts.Add(new Vector3(x*.04f,y*.05f,layer*.002f));
 for(int l=0;l<2;l++)for(int y=0;y<h-1;y++)for(int x=0;x<w-1;x++){int a=l*n+y*w+x;tris.AddRange(new[]{a,a+1,a+w,a+1,a+w+1,a+w});}
 for(int x=0;x<w-1;x++){int a=(h-1)*w+x;tris.AddRange(new[]{a,a+n,a+1,a+1,a+n,a+n+1});}
 var topology=new MashBoxClothTopology(verts.ToArray(),tris.ToArray(),32);
 Check(topology.ParticleCount<=32,"Connected reduction respects particle budget");
 Check(Enumerable.Range(0,w*12).All(i=>topology.vertexParticle[i]!=topology.vertexParticle[i+n]),"Nearby sheets in one island remain separate below their actual join");
 var left=Enumerable.Range(0,w*12).Select(i=>topology.vertexParticle[i]).ToHashSet();var right=Enumerable.Range(n,w*12).Select(i=>topology.vertexParticle[i]).ToHashSet();
 Check(!topology.edges.Any(e=>(left.Contains(e.x)&&right.Contains(e.y))||(right.Contains(e.x)&&left.Contains(e.y))),"No artificial stretch or bend links bridge nearby sheets");
 var cap=new ClothCapsule(new Vector3(0,-1,0),new Vector3(0,1,0),.2f);
 float3 end=new float3(1,0,0);
 Check(cap.Sweep(new float3(-1,0,0),ref end,cap,0,out _)&&end.x<-.199f,"Fast particle crossing stays on entry side");
 var old=new ClothCapsule(new Vector3(-1,-1,0),new Vector3(-1,1,0),.2f);var moved=new ClothCapsule(new Vector3(1,-1,0),new Vector3(1,1,0),.2f);end=float3.zero;
 Check(moved.Sweep(float3.zero,ref end,old,0,out _)&&end.x>1.199f,"Fast collider crossing carries stationary particle to correct side");
 end=new float3(-1,0,0);Check(!cap.Sweep(new float3(-.2f,0,0),ref end,cap,0,out _),"Departing contact does not stick");
 end=new float3(1,3,0);Check(!cap.Sweep(new float3(-1,3,0),ref end,cap,0,out _),"Clear path remains free");
 var taper=new ClothCapsule(new Vector3(0,-1,0),new Vector3(0,1,0),.3f,.1f);end=new float3(1,.5f,0);
 Check(taper.Sweep(new float3(-1,.5f,0),ref end,taper,.008f,out _)&&taper.Distance(end,.008f,out _)>=-.0001f&&end.x<0,"Taper sweep retains entry side and clearance");
 var profile=ScriptableObject.CreateInstance<MashBoxClothProfile>();
 try{
 profile.gravity=Vector3.zero;profile.wind=Vector3.zero;profile.animationFollow=0;profile.animationInertia=0;profile.thickness=0;profile.substeps=2;
 var one=new MashBoxClothTopology(new[]{Vector3.zero},new int[0],32);
 using(var solver=new MashBoxClothSolver(one)){
 solver.SetTargets(new List<Vector3>{Vector3.zero},Matrix4x4.identity,new[]{10f});solver.Reset(true);
 solver.Step(1f/60,0,profile,new[]{old},1);solver.Step(1f/60,0,profile,new[]{moved},1);
 Check(solver.Position(0).x>1.199f,"Full solver catches fast-moving collider");
 }
 }finally{UnityEngine.Object.DestroyImmediate(profile);}
 log.Add("SUCCESS");
 }catch(Exception e){log.Add("FAIL "+e);}File.WriteAllLines("Library/cloth-fast-motion-validation.txt",log);
 }
}
}
