using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
 public static class StudioSelectionValidation
 {
  const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
  static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,Flags).Invoke(o,a);
  static object Get(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);
  static void Set(object o,string n,object v)=>o.GetType().GetField(n,Flags).SetValue(o,v);
  static void Check(bool v,string m) { if(!v) throw new Exception(m); Debug.Log("PASS: "+m); }
  public static void Run()
  {
   try
   {
    var take=ScriptableObject.CreateInstance<AnimationTake>(); take.character=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Skeleton.fbx");take.lastFrame=20;
    using(var rig=new AuthoringRig(take.character,null)) { take.bonePaths=rig.Paths;take.keys.Add(rig.Rest.Copy(0)); }
    AssetDatabase.CreateAsset(take,AssetDatabase.GenerateUniqueAssetPath("Assets/Selection Test.asset"));
    AnimationStudioWindow.OpenInStudio(take);var window=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
    var rig2=(AuthoringRig)Get(window,"rig");
    Check((bool)Call(window,"CanTranslateBone",0),"Top-level Root allows translation");
    Check(Enumerable.Range(1,rig2.Bones.Length-1).All(i=>!(bool)Call(window,"CanTranslateBone",i)),"Every non-root bone rejects translation");
    int left=Array.IndexOf(rig2.Bones,rig2.Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm));
    int right=Array.IndexOf(rig2.Bones,rig2.Animator.GetBoneTransform(HumanBodyBones.RightUpperArm));
    Call(window,"SetRigControlSelection",left,false,false,false);
    Call(window,"SetRigControlSelection",right,false,false,true);
    Check(((IList)Get(window,"rigSelection")).Count==2,"Shift-click adds a second bone");
    Call(window,"SetRigControlSelection",right,false,false,true);
    Check(((IList)Get(window,"rigSelection")).Count==1,"Shift-click toggles an existing selection off");
    Call(window,"SetRigControlSelection",right,false,false,true);
    var masks=new int[rig2.Bones.Length]; var effectors=new int[4];
    Call(window,"AddRigSelectionMasks",masks,effectors,2);
    Check(masks[left]==2 && masks[right]==2 && masks.Count(v=>v!=0)==2,"Multi-selection keys only selected rotation channels");
    var layer=new StudioAnimationLayer(); Call(window,"AddSelectedToLayer",layer);
    Check(layer.bones.Contains(rig2.Paths[left])&&layer.bones.Contains(rig2.Paths[right]),"Both selected bones can be assigned to one layer");
    var bike=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Bike Skeleton.fbx");Call(window,"AddActor",bike);
    var actors=(IList)Get(window,"actorPreviews");var preview=actors[0];
    Call(window,"InActor",preview,(Action)(()=>Call(window,"SetRigControlSelection",1,false,false,true)));
    Check(((IList)Get(window,"rigSelection")).Count==3,"Shift selection spans character and vehicle actors");
    masks=new int[rig2.Bones.Length];effectors=new int[4];Call(window,"AddRigSelectionMasks",masks,effectors,1);
    Check(masks.Count(v=>v!=0)==2,"Actor scoping excludes bike controls from character key masks");
    var itemType=window.GetType().GetNestedType("RigSelectionItem",BindingFlags.NonPublic);var pointType=window.GetType().GetNestedType("RigPickPoint",BindingFlags.NonPublic);
    var points=(IList)Get(window,"rigPickPoints");points.Clear();
    foreach(int index in new[]{left,right}) { var item=Activator.CreateInstance(itemType);itemType.GetField("Actor").SetValue(item,take);itemType.GetField("Index").SetValue(item,index);var point=Activator.CreateInstance(pointType);pointType.GetField("Item").SetValue(point,item);pointType.GetField("Point").SetValue(point,new Vector2(index==left?10:30,10));points.Add(point); }
    Call(window,"SelectRigRectangle",new Rect(0,0,20,20),false);
    Check(((IList)Get(window,"rigSelection")).Count==1,"Marquee replacement selects only enclosed controls");
    Call(window,"SelectRigRectangle",new Rect(20,0,20,20),true);
    Check(((IList)Get(window,"rigSelection")).Count==2,"Shift-marquee adds enclosed controls");
    Check((bool)Get(window,"lockActorMeshes"),"Mesh selection is locked by default");
    Event.current=null;Call(window,"FlushEdits");foreach(var v in Resources.FindObjectsOfTypeAll<AnimationStudioViewport>())v.Close();UnityEngine.Object.DestroyImmediate(window);
    File.WriteAllText("selection-result.txt","PASS");EditorApplication.Exit(0);
   }
   catch(Exception e) { Debug.LogException(e);File.WriteAllText("selection-result.txt",e.ToString());EditorApplication.Exit(1); }
  }
 }
}
