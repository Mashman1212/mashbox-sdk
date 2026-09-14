using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioConstraintsValidation
    {
        const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
        static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,Flags).Invoke(o,a);
        static object Get(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);
        static void Check(bool v,string m) { if(!v) throw new Exception(m); Debug.Log("PASS: "+m); }
        public static void Run()
        {
            AnimationStudioWindow window=null;
            try
            {
                var human=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Skeleton.fbx");
                var bike=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Bike Skeleton.fbx");
                var take=ScriptableObject.CreateInstance<AnimationTake>(); take.character=human; take.lastFrame=12;
                using(var r=new AuthoringRig(human,null)) { take.bonePaths=r.Paths; take.keys.Add(r.Rest.Copy(0)); }
                AssetDatabase.CreateAsset(take,AssetDatabase.GenerateUniqueAssetPath("Assets/Constraint Session.asset"));
                AnimationStudioWindow.OpenInStudio(take); window=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
                Call(window,"AddActor",bike);
                var preview=((IList)Get(window,"actorPreviews"))[0];
                var br=(AuthoringRig)preview.GetType().GetField("Rig").GetValue(preview);
                var hr=(AuthoringRig)Get(window,"rig");
                Check(hr.HasIK,"Real character has the IK solver for attachments");
                var child=take.actors[0]; int bar=Array.FindIndex(br.Bones,b=>b.name=="Bars_Joint");
                var k=child.keys[0].Copy(12); k.bones[bar].position+=new Vector3(0,.025f,0); child.SetKey(k,12);
                Call(window,"Seek",0);
                Vector3 start=hr.Ends[0].position;
                var c=new StudioConstraint { drivenActor=take,drivenIK=0,targetActor=child,targetBone=br.Paths[bar],lastFrame=12 };
                Call(window,"AddAttachment",c,true);
                Check(Vector3.Distance(start,hr.Ends[0].position)<.03f,"Maintain offset attaches without snapping the hand to the steering pivot");
                Call(window,"Seek",12);
                Vector3 goal=br.Bones[bar].position+br.Bones[bar].rotation*c.positionOffset;
                Check(Vector3.Distance(goal,hr.Ends[0].position)<.035f,"Hand IK follows moving steering joint within solver tolerance");
                Check(take.keys.Count==1 && take.keys[0].effectors[0].weight==0,"Constraint evaluation does not change authored keys");
                var baked=(AnimationTake)Call(window,"SampleConstrainedTake",take);
                Check(baked.keys.Count==13 && baked.keys.All(p=>p.effectors.All(e=>e.weight==0)),"Export samples attachments into independent FK poses");
                hr.Apply(baked.Evaluate(12)); Check(Vector3.Distance(hr.Ends[0].position,goal)<.035f,"Baked hand retains the attachment without a runtime constraint");
                UnityEngine.Object.DestroyImmediate(baked);
                c.enabled=false; Call(window,"Seek",12);
                Check(Vector3.Distance(hr.Ends[0].position,start)<.001f,"Disabling attachment restores original animation");
                c.enabled=true; c.firstFrame=4; c.lastFrame=8; Call(window,"Seek",0);
                Check(Vector3.Distance(hr.Ends[0].position,start)<.001f,"Attachment is inactive outside its frame range");
                c.firstFrame=0; c.lastFrame=12;
                bool rejected=false;
                try { Call(window,"AddAttachment",new StudioConstraint { drivenActor=child,drivenBone=br.Paths[bar],targetActor=take,targetIK=0,lastFrame=12 },true); }
                catch(TargetInvocationException e) { rejected=e.InnerException.Message.Contains("Circular"); }
                Check(rejected && take.constraints.Count==1,"Circular rider-to-bike-to-rider dependencies are rejected before saving");
                c.enabled=false;
                var boneConstraint=new StudioConstraint { drivenActor=child,drivenBone=br.Paths[bar],targetActor=take,targetBone=hr.Paths[Array.IndexOf(hr.Bones,hr.Ends[1])],lastFrame=12 };
                Call(window,"AddAttachment",boneConstraint,false); Call(window,"Seek",6);
                Check(Vector3.Distance(br.Bones[bar].position,hr.Ends[1].position)<.001f,"Generic bone constraint can snap to another actor bone");
                Call(window,"FlushEdits");
                Check(File.ReadAllText(AssetDatabase.GetAssetPath(take)).Contains("targetActor:"),"Attachment references and settings persist with the take");
                foreach(var view in Resources.FindObjectsOfTypeAll<AnimationStudioViewport>()) view.Close();
                UnityEngine.Object.DestroyImmediate(window); window=null;
                File.WriteAllText("constraints-result.txt","PASS: hand IK follows bike, maintain offset, independent keys, FK bake, disable, frame range, cycle rejection, bone attachment and persistence.");
                EditorApplication.Exit(0);
            }
            catch(Exception e) { Debug.LogException(e); File.WriteAllText("constraints-result.txt",e.ToString()); EditorApplication.Exit(1); }
        }
    }
}
