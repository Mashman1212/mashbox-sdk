using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    public static class StudioChannelKeysValidation
    {
        const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
        static object Get(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);
        static void Set(object o,string n,object v)=>o.GetType().GetField(n,Flags).SetValue(o,v);
        static void Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,Flags).Invoke(o,a);
        static void Check(bool v,string m) { if(!v) throw new Exception(m); Debug.Log("PASS: "+m); }
        public static void Run()
        {
            try
            {
                var human=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Skeleton.fbx");
                var take=ScriptableObject.CreateInstance<AnimationTake>(); take.character=human; take.lastFrame=20;
                using(var r=new AuthoringRig(human,null)) { take.bonePaths=r.Paths; take.keys.Add(r.Rest.Copy(0)); }
                var end=take.keys[0].Copy(20); end.bones[1].rotation*=Quaternion.Euler(0,60,0); end.bones[2].position+=Vector3.up; take.SetKey(end,20);
                AssetDatabase.CreateAsset(take,AssetDatabase.GenerateUniqueAssetPath("Assets/Channel Keys.asset"));
                AnimationStudioWindow.OpenInStudio(take);
                var window=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
                Set(window,"ikMode",false); Set(window,"control",1); Set(window,"autoKey",false);
                Call(window,"Seek",7);
                var original=Enumerable.Range(0,21).Select(f=>take.Evaluate(f)).ToArray();
                var pose=(PoseKey)Get(window,"pose"); pose.bones[1].position+=Vector3.right; pose.bones[1].rotation*=Quaternion.Euler(40,0,0); pose.bones[2].position+=Vector3.left;
                var desired=pose.Copy(7);
                var key=new Event { type=EventType.KeyDown,keyCode=KeyCode.W,modifiers=EventModifiers.Shift };
                Call(window,"HandleKeys",key);
                Check(key.type==EventType.Used && take.keys.Count==3,"Shift+W inserts a key with Auto Key off");
                Check(Vector3.Distance(take.Evaluate(7).bones[1].position,desired.bones[1].position)<.00001f,"Shift+W records selected translation");
                Check((bool)Get(window,"unkeyedPose"),"Other posed channels remain marked unkeyed");
                for(int f=0;f<=20;f++)
                    Check(Quaternion.Angle(take.Evaluate(f).bones[1].rotation,original[f].bones[1].rotation)<.001f &&
                        Vector3.Distance(take.Evaluate(f).bones[2].position,original[f].bones[2].position)<.00001f,"Unkeyed rotation and other bone interpolation preserved at frame "+f);
                Call(window,"HandleKeys",new Event { type=EventType.KeyDown,keyCode=KeyCode.E,modifiers=EventModifiers.Shift });
                Check(take.keys.Count==3 && Quaternion.Angle(take.Evaluate(7).bones[1].rotation,desired.bones[1].rotation)<.001f,"Shift+E merges rotation at the same frame");
                Check(Vector3.Distance(take.Evaluate(7).bones[1].position,desired.bones[1].position)<.00001f,"Rotation key retains the translation key");
                Call(window,"Seek",8); int count=take.keys.Count;
                EditorGUIUtility.editingTextField=true;
                Call(window,"HandleKeys",new Event { type=EventType.KeyDown,keyCode=KeyCode.W,modifiers=EventModifiers.Shift });
                EditorGUIUtility.editingTextField=false;
                Call(window,"HandleKeys",new Event { type=EventType.KeyDown,keyCode=KeyCode.W,modifiers=EventModifiers.Shift|EventModifiers.Control });
                Call(window,"HandleKeys",new Event { type=EventType.KeyDown,keyCode=KeyCode.E });
                Check(take.keys.Count==count && (bool)Get(window,"rotate"),"Text editing and extra modifiers do not key; plain E selects rotate");
                Set(window,"ikMode",true); Set(window,"control",0); Set(window,"poleMode",false);
                pose=(PoseKey)Get(window,"pose"); var oldRotation=pose.effectors[0].rotation;
                pose.effectors[0].position+=Vector3.up*.05f; pose.effectors[0].rotation=Quaternion.Euler(35,0,0); pose.effectors[0].weight=1;
                Call(window,"HandleKeys",new Event { type=EventType.KeyDown,keyCode=KeyCode.W,modifiers=EventModifiers.Shift });
                Check(take.Evaluate(8).effectors[0].weight==1 && Quaternion.Angle(take.Evaluate(8).effectors[0].rotation,oldRotation)<.001f,"IK position key activates IK without keying target rotation");
                Call(window,"FlushEdits");
                Check(File.ReadAllText(AssetDatabase.GetAssetPath(take)).Contains("selective: 1"),"Channel masks serialize with the take");
                foreach(var view in Resources.FindObjectsOfTypeAll<AnimationStudioViewport>()) view.Close(); UnityEngine.Object.DestroyImmediate(window);
                File.WriteAllText("channel-keys-result.txt","PASS: selected position/rotation, unchanged other curves, merge, draft retention, modifier and text guards, plain tools, IK activation and persistence."); EditorApplication.Exit(0);
            }
            catch(Exception e) { Debug.LogException(e); File.WriteAllText("channel-keys-result.txt",e.ToString()); EditorApplication.Exit(1); }
        }
    }
}
