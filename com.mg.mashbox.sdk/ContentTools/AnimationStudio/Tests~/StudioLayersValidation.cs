using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    public static class StudioLayersValidation
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
                var human=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Skeleton.fbx");
                var take=ScriptableObject.CreateInstance<AnimationTake>(); take.character=human; take.lastFrame=20;
                int shoulder;
                using(var rig=new AuthoringRig(human,null))
                { take.bonePaths=rig.Paths; take.keys.Add(rig.Rest.Copy(0)); shoulder=Array.IndexOf(rig.Bones,rig.Animator.GetBoneTransform(HumanBodyBones.LeftShoulder)); }
                var end=take.keys[0].Copy(20); end.bones[shoulder].rotation*=Quaternion.Euler(0,30,0); take.SetKey(end,20);
                string original=string.Join("|",take.keys.Select(JsonUtility.ToJson));
                var layer=new StudioAnimationLayer { name="Shoulder correction",weight=.5f }; layer.bones.Add(take.bonePaths[shoulder]); take.layers.Add(layer); take.activeLayer=0;
                var desired=take.Evaluate(7); desired.bones[shoulder].rotation*=Quaternion.Euler(20,0,0);
                var boneMask=new int[desired.bones.Length]; boneMask[shoulder]=2;
                take.SetLayerKey(0,desired,7,boneMask,new int[4]);
                Check(Quaternion.Angle(take.Evaluate(7).bones[shoulder].rotation,desired.bones[shoulder].rotation)<.08f,"Weighted additive key reproduces the authored shoulder pose");
                Check(original==string.Join("|",take.keys.Select(JsonUtility.ToJson)),"Layer keying leaves base motion bytes unchanged");
                for(int f=0;f<=20;f++)
                    Check(Vector3.Distance(take.Evaluate(f).bones[0].position,take.EvaluateBase(f).bones[0].position)<.00001f,"Unassigned root channel preserved at frame "+f);
                layer.muted=true;
                Check(Quaternion.Angle(take.Evaluate(7).bones[shoulder].rotation,take.EvaluateBase(7).bones[shoulder].rotation)<.08f,"Mute restores imported shoulder motion");
                layer.muted=false; layer.offsets.Add(new StudioJointOffset { path=take.bonePaths[shoulder],angles=new Vector3(0,0,8) });
                var samples=Enumerable.Range(0,21).Select(f=>take.Evaluate(f)).ToArray();
                take.ConvertLayerMode(0,StudioLayerMode.Override);
                Check(Enumerable.Range(0,21).All(f=>Quaternion.Angle(take.Evaluate(f).bones[shoulder].rotation,samples[f].bones[shoulder].rotation)<.1f),"Additive-to-override conversion preserves every sampled frame including constant offset");
                take.ConvertLayerMode(0,StudioLayerMode.Additive);
                Check(Enumerable.Range(0,21).All(f=>Quaternion.Angle(take.Evaluate(f).bones[shoulder].rotation,samples[f].bones[shoulder].rotation)<.1f),"Override-to-additive conversion preserves motion");
                var top=new StudioAnimationLayer { name="IK correction",solo=true }; top.effectors[0]=9; take.layers.Add(top);
                desired=take.Evaluate(5); desired.effectors[0].position+=Vector3.up*.1f; desired.effectors[0].weight=1;
                var effectors=new int[4]; effectors[0]=9; take.SetLayerKey(1,desired,5,new int[desired.bones.Length],effectors);
                Check(Vector3.Distance(take.Evaluate(5).effectors[0].position,desired.effectors[0].position)<.00001f && take.Evaluate(5).effectors[0].weight==1,"IK target and activation can be isolated on a solo layer");
                Check(Quaternion.Angle(take.Evaluate(5).bones[shoulder].rotation,take.EvaluateBase(5).bones[shoulder].rotation)<.08f,"Solo excludes other correction layers while retaining base motion");
                top.solo=false; bool blocked=false;
                try { take.SetLayerKey(0,desired,5,boneMask,effectors); } catch(InvalidOperationException) { blocked=true; }
                Check(blocked,"Keying beneath enabled layers cannot accidentally double their contribution");
                top.muted=true; take.activeLayer=0;
                AssetDatabase.CreateAsset(take,AssetDatabase.GenerateUniqueAssetPath("Assets/Layered Motion.asset"));
                AnimationStudioWindow.OpenInStudio(take); var window=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
                Set(window,"ikMode",false); Set(window,"control",shoulder); Call(window,"Seek",9);
                var pose=(PoseKey)Get(window,"pose"); pose.bones[shoulder].rotation*=Quaternion.Euler(0,4,0);
                Call(window,"HandleKeys",new Event { type=EventType.KeyDown,keyCode=KeyCode.E,modifiers=EventModifiers.Shift });
                Check(original==string.Join("|",take.keys.Select(JsonUtility.ToJson)),"Shift+E routes edits into the selected layer without replacing base keys");
                var expected=take.Evaluate(9); var baked=(AnimationTake)Call(window,"SampleConstrainedTake",take);
                Check(baked.layers.Count==0 && Quaternion.Angle(baked.Evaluate(9).bones[shoulder].rotation,expected.bones[shoulder].rotation)<.1f,"Export samples the correction stack once and removes layers from the baked copy");
                UnityEngine.Object.DestroyImmediate(baked);
                Call(window,"FlushEdits");
                Check(File.ReadAllText(AssetDatabase.GetAssetPath(take)).Contains("Shoulder correction"),"Layer names, memberships, offsets and keys persist in the asset");
                foreach(var v in Resources.FindObjectsOfTypeAll<AnimationStudioViewport>()) v.Close(); UnityEngine.Object.DestroyImmediate(window);
                File.WriteAllText("layers-result.txt","PASS: additive/override weighting, base protection, masks, mute/solo, mode conversion, constant offsets, IK layers, hotkeys, export and persistence."); EditorApplication.Exit(0);
            }
            catch(Exception e) { Debug.LogException(e); File.WriteAllText("layers-result.txt",e.ToString()); EditorApplication.Exit(1); }
        }
    }
}
