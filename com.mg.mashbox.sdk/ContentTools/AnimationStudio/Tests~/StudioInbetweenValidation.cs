using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
 public static class StudioInbetweenValidation
 {
  const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
  const string Job="D:/ProjectX - U6/Tools/MotionGeneration/jobs/inbetween-validation";
  static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,Flags).Invoke(o,a);
  static void Check(bool v,string m) { if(!v) throw new Exception(m); Debug.Log("PASS: "+m); }
  public static void Export()
  {
   try
   {
    var take=ScriptableObject.CreateInstance<AnimationTake>(); take.name="AI Inbetween Test"; take.character=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Skeleton.fbx"); take.frameRate=30; take.lastFrame=60;
    using(var rig=new AuthoringRig(take.character,null))
    {
     take.bonePaths=rig.Paths;
     foreach(int f in new[]{0,10,50,60})
     {
      rig.Apply(rig.Rest);
      var left=rig.Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm); var right=rig.Animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
      left.rotation=Quaternion.AngleAxis(f<30?65:45,Vector3.forward)*left.rotation;
      right.rotation=Quaternion.AngleAxis(f<30?-65:-45,Vector3.forward)*right.rotation;
      take.keys.Add(rig.Capture().Copy(f));
     }
    }
    AssetDatabase.CreateAsset(take,AssetDatabase.GenerateUniqueAssetPath("Assets/AI Inbetween Test.asset"));
    AnimationStudioWindow.OpenInStudio(take); var window=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
    var request=Call(window,"CaptureInbetween",take,10,50); Directory.CreateDirectory(Job);
    File.WriteAllText(Job+"/request.json",JsonUtility.ToJson(request)); File.WriteAllText(Job+"/take-path.txt",AssetDatabase.GetAssetPath(take));
    Call(window,"FlushEdits"); foreach(var v in Resources.FindObjectsOfTypeAll<AnimationStudioViewport>()) v.Close(); UnityEngine.Object.DestroyImmediate(window);
    Debug.Log("PASS: actual Humanoid poses exported for local Kimodo"); EditorApplication.Exit(0);
   }
   catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
  }
  public static void Import()
  {
   try
   {
    var take=AssetDatabase.LoadAssetAtPath<AnimationTake>(File.ReadAllText(Job+"/take-path.txt"));
    var before=Enumerable.Range(0,61).Select(f=>take.Evaluate(f)).ToArray(); string keys=string.Join("|",take.keys.Select(k=>JsonUtility.ToJson(k)));
    AnimationStudioWindow.OpenInStudio(take); var window=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
    var type=typeof(AnimationStudioWindow);
    var request=JsonUtility.FromJson(File.ReadAllText(Job+"/request.json"),type.GetNestedType("InbetweenRequest",BindingFlags.NonPublic));
    var result=JsonUtility.FromJson(File.ReadAllText(Job+"/inbetween.json"),type.GetNestedType("InbetweenResult",BindingFlags.NonPublic));
    Call(window,"ApplyInbetween",take,request,result);
    Check(take.layers.Count==1 && take.layers[0].mode==StudioLayerMode.Override,"Result imports into an independent override layer");
    Check(keys==string.Join("|",take.keys.Select(k=>JsonUtility.ToJson(k))),"Original animation keys remain byte-for-byte unchanged");
    Check(take.layers[0].keys.Count==41 && take.lastFrame==60,"Exact interval length without added transition frames");
    foreach(int f in new[]{0,9,10,50,51,60})
    {
     var after=take.Evaluate(f);
     Check(after.bones.Select((b,i)=>Quaternion.Angle(b.rotation,before[f].bones[i].rotation)).Max()<.05f && after.bones.Select((b,i)=>Vector3.Distance(b.position,before[f].bones[i].position)).Max()<.00001f,"Endpoint or outside frame preserved: "+f);
    }
    Check(take.layers[0].keys.All(k=>k.bones.All(b=>float.IsFinite(b.position.x)&&float.IsFinite(b.rotation.w))),"Generated poses contain finite transforms");
    Check(take.layers[0].keys.Skip(1).Take(39).Any(k=>k.bones.Select((b,i)=>Quaternion.Angle(b.rotation,before[k.frame].bones[i].rotation)).Max()>1),"Interior frames contain generated motion");
    take.layers[0].muted=true;
    Check(Quaternion.Angle(take.Evaluate(25).bones[10].rotation,before[25].bones[10].rotation)<.01f,"Muting restores source animation");
    Call(window,"FlushEdits"); foreach(var v in Resources.FindObjectsOfTypeAll<AnimationStudioViewport>()) v.Close(); UnityEngine.Object.DestroyImmediate(window);
    File.WriteAllText(Job+"/unity-result.txt","PASS"); EditorApplication.Exit(0);
   }
   catch(Exception e) { Debug.LogException(e); File.WriteAllText(Job+"/unity-result.txt",e.ToString()); EditorApplication.Exit(1); }
  }
 }
}
