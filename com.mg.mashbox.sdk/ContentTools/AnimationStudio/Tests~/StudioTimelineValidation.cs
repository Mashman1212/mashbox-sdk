using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
 public static class StudioTimelineValidation
 {
  const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
  static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,Flags).Invoke(o,a);
  static void Set(object o,string n,object v)=>o.GetType().GetField(n,Flags).SetValue(o,v);
  static void Check(bool v,string m) { if(!v) throw new Exception(m); Debug.Log("PASS: "+m); }
  public static void Run()
  {
   try
   {
    var human=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Skeleton.fbx");
    var take=ScriptableObject.CreateInstance<AnimationTake>(); take.character=human; take.frameRate=30; take.lastFrame=20;
    using(var rig=new AuthoringRig(human,null)) { take.bonePaths=rig.Paths; take.keys.Add(rig.Rest.Copy(0)); }
    take.keys.Add(take.keys[0].Copy(10)); take.keys.Add(take.keys[0].Copy(20));
    take.keys[1].selective=true; take.keys[1].boneChannels=new int[take.bonePaths.Length]; take.keys[1].boneChannels[0]=2;
    string original=JsonUtility.ToJson(take.keys[1]);
    AssetDatabase.CreateAsset(take,AssetDatabase.GenerateUniqueAssetPath("Assets/Timeline Validation.asset"));
    AnimationStudioWindow.OpenInStudio(take);
    var window=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
    Set(window,"timelineRangeStart",20); Set(window,"timelineRangeEnd",10);
    Call(window,"CopyTimelineKeys",take,take.keys);
    Call(window,"PasteTimelineKeys",take,take.keys,30);
    Check(take.keys.Select(k=>k.frame).SequenceEqual(new[]{0,10,20,30,40}),"Reverse range copies and pastes with original key spacing");
    Check(take.keys[3].selective && take.keys[3].boneChannels[0]==2,"Sparse channel masks survive copy/paste");
    Check(original==JsonUtility.ToJson(take.keys[1]),"Copy and paste preserve source key bytes");
    Check(!ReferenceEquals(take.keys[1].bones,take.keys[3].bones),"Pasted pose arrays are independent");
    Check(take.lastFrame==40,"Paste extends take duration");
    Call(window,"PasteTimelineKeys",take,take.keys,30);
    Check(take.keys.Count==5,"Paste replaces matching frames without duplicate keys");
    Call(window,"DeleteTimelineKeys",take,take.keys);
    Check(take.keys.Select(k=>k.frame).SequenceEqual(new[]{0,10,20}),"Delete removes only selected interval");
    Set(window,"timelineRangeStart",0); Set(window,"timelineRangeEnd",40);
    Call(window,"DeleteTimelineKeys",take,take.keys);
    Check(take.keys.Count==3,"Base motion retains a required pose when full deletion is attempted");
    var layer=new StudioAnimationLayer(); layer.keys.Add(take.keys[1].Copy(10)); take.layers.Add(layer);
    Call(window,"PasteTimelineKeys",take,layer.keys,30);
    Check(layer.keys.Count==1,"Clipboard cannot overwrite a different layer");
    Call(window,"DeleteTimelineKeys",take,layer.keys);
    Check(layer.keys.Count==0 && take.keys.Count==3,"Layer may be emptied without changing base motion");
    Call(window,"FlushEdits");
    foreach(var view in Resources.FindObjectsOfTypeAll<AnimationStudioViewport>()) view.Close();
    UnityEngine.Object.DestroyImmediate(window);
    File.WriteAllText("timeline-result.txt","PASS"); EditorApplication.Exit(0);
   }
   catch(Exception e) { Debug.LogException(e); File.WriteAllText("timeline-result.txt",e.ToString()); EditorApplication.Exit(1); }
  }
 }
}
