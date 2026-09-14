using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioActorsValidation
    {
        const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
        static void Check(bool value,string message) { if(!value) throw new Exception(message); Debug.Log("PASS: "+message); }
        static object Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Flags).Invoke(target,args);
        static object Get(object target,string field)=>target.GetType().GetField(field,Flags).GetValue(target);
        static void Set(object target,string field,object value)=>target.GetType().GetField(field,Flags).SetValue(target,value);
        public static void Run()
        {
            AnimationStudioWindow window=null;
            try
            {
                var human=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Skeleton.fbx");
                var bike=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Bike Skeleton.fbx");
                var take=ScriptableObject.CreateInstance<AnimationTake>(); take.character=human; take.frameRate=30; take.lastFrame=60;
                using(var r=new AuthoringRig(human,null)) { take.bonePaths=r.Paths; take.keys.Add(r.Rest.Copy(0)); }
                AssetDatabase.CreateAsset(take,AssetDatabase.GenerateUniqueAssetPath("Assets/Rider Bike Session.asset"));
                AnimationStudioWindow.OpenInStudio(take);
                window=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
                Call(window,"AddActor",bike);
                Check(take.actors.Count==1 && AssetDatabase.IsSubAsset(take.actors[0]),"Bike persists as a separate actor track in the take asset");
                var preview=((IList)Get(window,"actorPreviews"))[0];
                var bikeRig=(AuthoringRig)preview.GetType().GetField("Rig").GetValue(preview);
                var primaryRig=(AuthoringRig)Get(window,"rig");
                Check(bikeRig.Root.scene==primaryRig.Root.scene,"Rider and bike render in one preview scene");
                Check(bikeRig.Bones.Any(b=>b.name=="DriveTrain_Joint"),"Actual bike skeleton exposes Maya drivetrain mapping");
                var pedal=bikeRig.Bones.First(b=>b.name=="LeftPedal_Joint");
                var before=pedal.rotation;
                Call(window,"InActor",preview,(Action)(()=>Call(window,"SetBikeAngle",3,40f)));
                Check(Quaternion.Angle(before,pedal.rotation)<.1f,"Crank rotation preserves pedal world orientation via Maya counter-rotation");
                var clip=AssetDatabase.LoadAllAssetsAtPath("Assets/Character/BMX_Moto.fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
                Set(window,"actorClip",clip); Call(window,"ImportActorClip");
                var child=take.actors[0];
                Check(child.keys.Count>10 && child.frameRate==take.frameRate,"BMX clip imports at the shared clock rate");
                Check(child.keys.Skip(1).Any(k=>k.bones.Select((b,i)=>Quaternion.Angle(b.rotation,child.keys[0].bones[i].rotation)).Max()>5),"Actual BMX motion changes joint poses");
                Check(take.keys.Count==1,"Bike import preserves rider animation keys");
                Call(window,"Seek",Mathf.Min(20,take.lastFrame));
                var expected=child.Evaluate(20);
                Check(bikeRig.Capture().bones.Select((b,i)=>Quaternion.Angle(b.rotation,expected.bones[i].rotation)).Max()<.1f,"Shared scrub evaluates bike at the same timestamp");
                var baked=AnimationClipBaker.Bake(child,bikeRig,false);
                Check(!baked.isHumanMotion && AnimationUtility.GetCurveBindings(baked).Any(b=>b.path.Contains("Frame_Joint")),"Bike exports as independent generic joint animation");
                UnityEngine.Object.DestroyImmediate(baked);
                Call(window,"FlushEdits");
                var path=AssetDatabase.GetAssetPath(take);
                Check(File.ReadAllText(path).Contains("actors:"),"Actor references save with the session");
                foreach(var view in Resources.FindObjectsOfTypeAll<AnimationStudioViewport>()) view.Close();
                UnityEngine.Object.DestroyImmediate(window); window=null;
                File.WriteAllText("actors-result.txt","PASS: actual bike rig, shared scene/clock, separate persisted keys, pedal linkage, source FBX import and generic bake."); EditorApplication.Exit(0);
            }
            catch(Exception e) { if(window) UnityEngine.Object.DestroyImmediate(window); Debug.LogException(e); File.WriteAllText("actors-result.txt",e.ToString()); EditorApplication.Exit(1); }
        }
    }
}
