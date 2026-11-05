using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;

// Disposable host extracts the production drop resolver, importer and constrained sampler.
public static class StudioActorClipImportValidation
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static AnimationClip Clip(string name, string path, int fps, float seconds)
    {
        var clip = new AnimationClip { name = name, frameRate = fps };
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.x"), AnimationCurve.Linear(0, 0, seconds, 1));
        return clip;
    }
    public static void Run()
    {
        if (!Application.dataPath.Replace('\\','/').EndsWith("/BmxAnimatorValidation/Assets")) throw new InvalidOperationException("Disposable host only.");
        try
        {
            var source = new GameObject("HumanFixture"); new GameObject("Hips").transform.SetParent(source.transform);
            var bike = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Bike Skeleton.fbx");
            using (var humanRig = new AuthoringRig(source, null))
            using (var bikeRig = new AuthoringRig(bike, null))
            {
                var human = ScriptableObject.CreateInstance<AnimationTake>(); human.bonePaths = humanRig.Paths; human.SetKey(humanRig.Rest,0);
                var vehicle = ScriptableObject.CreateInstance<AnimationTake>(); vehicle.bonePaths = bikeRig.Paths; vehicle.SetKey(bikeRig.Rest,0);
                var window = ScriptableObject.CreateInstance<AnimationStudioWindow>(); window.take=human; window.rig=humanRig; window.AddFixtureActor(vehicle,bikeRig);
                var humanClip=Clip("Player@Test", "Hips",24,1);
                var vehicleClip=Clip("BMX@Test",bikeRig.Paths.First(p=>p.EndsWith("Frame_Joint")),30,2);
                window.ImportFixture(humanClip,-1); string firstKeys=JsonUtility.ToJson(human.keys[12]);
                window.ImportFixture(vehicleClip,0);
                Check(human.frameRate==30 && vehicle.frameRate==30,"Both imports use the shared session frame rate");
                Check(human.lastFrame==60 && vehicle.lastFrame==60,"Shared playback range contains both clips");
                Check(firstKeys==JsonUtility.ToJson(human.keys[12]),"Vehicle import preserves human motion");
                Check(human.outputClip==humanClip && vehicle.outputClip==vehicleClip,"Separate source clip references");
                var humanSample=window.SampleFixture(human); var vehicleSample=window.SampleFixture(vehicle);
                Check(humanSample.lastFrame==60 && humanSample.frameRate==30 && vehicleSample.lastFrame==60 && vehicleSample.frameRate==30,"Separate clip exports share timeline timing");
                var bad=Clip("Unmatched","Missing/Bone",30,1); string previous=JsonUtility.ToJson(vehicle.keys[30]);
                bool rejected=false;try {window.ImportFixture(bad,0);}catch(InvalidOperationException){rejected=true;}
                Check(rejected && previous==JsonUtility.ToJson(vehicle.keys[30]) && vehicle.outputClip==vehicleClip,"Failed import leaves previous actor clip intact");
                Check(AnimationStudioWindow.DroppedClipRole(humanClip)==0 && AnimationStudioWindow.DroppedClipRole(vehicleClip)==1,"Automatic human/vehicle routing");
                var fbx=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/BMX@BMX_Barspin Left.fbx");
                var clips=AnimationStudioWindow.ResolveDroppedAnimationClips(new UnityEngine.Object[]{fbx});
                Check(clips.Length>1 && clips.All(c=>!c.name.StartsWith("__preview__")),"Real BMX FBX exposes its named animation clips");
                Check(AnimationStudioWindow.ResolveDroppedAnimationClips(new UnityEngine.Object[]{fbx,clips[0]}).SequenceEqual(new[]{clips[0]}),"Explicit FBX child takes precedence");
                Check(AnimationStudioWindow.ResolveDroppedAnimationClips(new UnityEngine.Object[]{humanClip,fbx}).Length==clips.Length+1,"Mixed clip and FBX drop retains both");
                Check(!window.WritableFixture(clips[0]),"Imported FBX clip cannot be overwritten");
                UnityEngine.Object.DestroyImmediate(humanSample);UnityEngine.Object.DestroyImmediate(vehicleSample);
                UnityEngine.Object.DestroyImmediate(window);UnityEngine.Object.DestroyImmediate(human);UnityEngine.Object.DestroyImmediate(vehicle);
            }
            UnityEngine.Object.DestroyImmediate(source);
            File.WriteAllText("actor-clip-result.txt","PASS: independent actor imports, shared timing, export sampling, source references, rollback on mismatch, role routing, real multi-clip FBX extraction, explicit-child precedence, mixed drops and FBX write protection.");
            EditorApplication.Exit(0);
        }
        catch(Exception e){File.WriteAllText("actor-clip-result.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
