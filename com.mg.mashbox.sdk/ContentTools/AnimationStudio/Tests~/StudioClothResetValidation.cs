using System;
using System.IO;
using System.Collections.Generic;
using MashBoxSDK.Cloth;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio {
public static class StudioClothResetValidation {
 public static void Run(){var log=new List<string>();MashBoxClothProfile profile=null;
 void Check(bool ok,string label){if(!ok)throw new Exception(label);log.Add("PASS "+label);}
 try{
 Check(!AnimationStudioWindow.ClothMotionDiscontinuity(.9f,1f,2),"Ordinary playback keeps simulation");
 Check(AnimationStudioWindow.ClothMotionDiscontinuity(1.99f,2.01f,2),"Animation wrap resets");
 Check(AnimationStudioWindow.ClothMotionDiscontinuity(1.99f,2f,2),"Exact endpoint resets");
 Check(!AnimationStudioWindow.ClothMotionDiscontinuity(2f,2.01f,2),"Frame after wrap does not reset again");
 Check(AnimationStudioWindow.ClothMotionDiscontinuity(1f,.5f,2),"Rewind resets");
 Check(AnimationStudioWindow.ClothMotionDiscontinuity(1f,5f,2),"Multiple skipped loops reset");
 var topology=new MashBoxClothTopology(new[]{Vector3.zero},new int[0],32);
 profile=ScriptableObject.CreateInstance<MashBoxClothProfile>();profile.gravity=profile.wind=Vector3.zero;profile.animationFollow=1;profile.animationInertia=1;profile.thickness=0;
 using(var solver=new MashBoxClothSolver(topology)){
 solver.SetTargets(new List<Vector3>{Vector3.zero},Matrix4x4.identity,new[]{10f});solver.Reset(true);
 var cap=new ClothCapsule(new Vector3(-1,-1,0),new Vector3(-1,1,0),.2f);solver.Step(1f/60,0,profile,new[]{cap},1);
 solver.BeginGrab(Vector3.zero,1);
 solver.SetTargets(new List<Vector3>{Vector3.up*3},Matrix4x4.identity,new[]{10f});
 Check(solver.TargetJumpExceeds(2),"Bone target jump detected without root motion");
 solver.Reset();Check(solver.GrabbedCount==0&&!solver.TargetJumpExceeds(2),"Reset clears grab and target velocity history");
 solver.SetTargets(new List<Vector3>{Vector3.zero},Matrix4x4.identity,new[]{10f});solver.Reset();
 cap=new ClothCapsule(new Vector3(1,-1,0),new Vector3(1,1,0),.2f);solver.Step(1f/60,0,profile,new[]{cap},1);
 Check(solver.Position(0).magnitude<.0001f,"Reset does not sweep collider across animation discontinuity");
 Check(solver.Displacement(0).magnitude<.0001f,"No inherited impulse after reset");
 }
 log.Add("SUCCESS");
 }catch(Exception e){log.Add("FAIL "+e);}finally{if(profile)UnityEngine.Object.DestroyImmediate(profile);File.WriteAllLines("Library/cloth-reset-validation.txt",log);}
 }
}
}
