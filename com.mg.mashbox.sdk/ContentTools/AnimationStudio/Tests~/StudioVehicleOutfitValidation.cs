using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    public sealed class OutfitSlotFixture : MonoBehaviour { public string _slotPrefix="BMX_Frame"; public string _rootingPrefix=""; public string _directionID=""; }
    public static class StudioVehicleOutfitValidation
    {
        const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
        static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,Flags).Invoke(o,a);
        static object Get(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);
        static void Check(bool v,string m) { if(!v) throw new Exception(m); Debug.Log("PASS: "+m); }
        public static void Run()
        {
            try
            {
                var bike=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Bike Skeleton.fbx");
                var frame=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/BMX_Frame_67_Performance.prefab");
                Check(frame && frame.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="Headset_Anchor"),"Actual 67 Performance frame exposes its mounting anchors");
                var take=ScriptableObject.CreateInstance<AnimationTake>(); take.character=bike;
                using(var r=new AuthoringRig(bike,null)) { take.bonePaths=r.Paths; take.keys.Add(r.Rest.Copy(0)); }
                AssetDatabase.CreateAsset(take,AssetDatabase.GenerateUniqueAssetPath("Assets/Vehicle Outfit.asset"));
                AnimationStudioWindow.OpenInStudio(take); var window=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
                var rig=(AuthoringRig)Get(window,"rig");
                var cube=GameObject.CreatePrimitive(PrimitiveType.Cube); var source=new GameObject("Vehicle fixture");
                var frameSlot=new GameObject("Chassis Visuals"); frameSlot.transform.SetParent(source.transform,false); frameSlot.AddComponent<OutfitSlotFixture>();
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(frame); instance.transform.SetParent(frameSlot.transform,false);
                var barSlot=new GameObject("Headset Visuals"); barSlot.transform.SetParent(source.transform,false); var marker=barSlot.AddComponent<OutfitSlotFixture>(); marker._rootingPrefix="headset"; marker._slotPrefix="BMX_Bars";
                cube.transform.SetParent(barSlot.transform,false);
                Call(window,"ReadVehicleSlots",source);
                Check(take.vehicleParts.Count==2 && take.vehicleParts[1].anchorPart==0,"Equipped defaults resolve the frame-to-headset anchor");
                var visuals=(IList)typeof(AuthoringRig).GetField("vehicleVisuals",Flags).GetValue(rig);
                var visualFrame=((GameObject)visuals[0]).transform; var visualBars=((GameObject)visuals[1]).transform;
                var anchor=visualFrame.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Headset_Anchor");
                Check(Vector3.Distance(visualBars.position,anchor.position)<.0001f,"Part snaps to the actual frame anchor in rest pose");
                Check(visualBars.parent.name=="Bars_Joint","Steering part follows Bars_Joint independently of the frame");
                Check(!rig.Root.GetComponentsInChildren<MonoBehaviour>(true).Any(),"Outfit preview does not clone gameplay scripts");
                int count=rig.Bones.Length; Call(window,"ApplyVehicleOutfit");
                Check(rig.Bones.Length==count && rig.Root.GetComponentsInChildren<Renderer>(true).Length==frame.GetComponentsInChildren<Renderer>(true).Length+1,"Reapplying outfit preserves bone paths and does not duplicate renderers");
                var before=rig.Capture(); Call(window,"SaveBikeHandle",0,new Vector3(.1f,.2f,0),new Vector3(0,20,0));
                Check(take.keys.Count==1 && rig.Capture().bones.Select((b,i)=>Vector3.Distance(b.position,before.bones[i].position)).Max()<.00001f,"Handle layout edits leave animation transforms and keys unchanged");
                var preset=ScriptableObject.CreateInstance<StudioActorPreset>(); preset.character=bike; preset.vehicleParts=new List<StudioVehiclePart>{take.vehicleParts[0]}; preset.handleOffsets=take.handleOffsets;
                Call(window,"AddActorPreset",preset);
                Check(take.actors.Count==1 && take.actors[0].vehicleParts.Count==1 && take.actors[0].handleOffsets.Count==1,"Actor preset restores parts and handle layout into a new actor");
                take.actors[0].handleOffsets[0].position=Vector3.zero;
                Check(preset.handleOffsets[0].position!=Vector3.zero,"Loaded presets do not share mutable handle settings");
                var synthetic=new GameObject("Root alignment"); synthetic.transform.SetPositionAndRotation(new Vector3(1,2,3),Quaternion.Euler(0,35,0));
                using(var r=new AuthoringRig(synthetic,null)) Check(Vector3.Distance(r.Root.transform.position,synthetic.transform.position)<.00001f && Quaternion.Angle(r.Root.transform.rotation,synthetic.transform.rotation)<.001f,"Source root translation and orientation survive preview construction");
                Call(window,"FlushEdits");
                UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(preset); UnityEngine.Object.DestroyImmediate(synthetic);
                foreach(var v in Resources.FindObjectsOfTypeAll<AnimationStudioViewport>()) v.Close(); UnityEngine.Object.DestroyImmediate(window);
                File.WriteAllText("vehicle-outfit-result.txt","PASS: actual frame anchors, equipped defaults, joint attachment, isolated renderers, stable bone paths, handle offsets, reusable presets and source-root alignment."); EditorApplication.Exit(0);
            }
            catch(Exception e) { Debug.LogException(e); File.WriteAllText("vehicle-outfit-result.txt",e.ToString()); EditorApplication.Exit(1); }
        }
    }
}
