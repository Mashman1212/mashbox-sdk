using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioGenerationValidation
    {
        private static void Check(bool value, string name) { if (!value) throw new Exception(name); Debug.Log("PASS: " + name); }
        public static void RunSteps()
        {
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Skeleton.fbx");
                using (var rig = new AuthoringRig(source,null))
                {
                    var solve = typeof(AnimationMotionImporter).GetMethod("SolveLeg",BindingFlags.NonPublic|BindingFlags.Static);
                    var feet = new[] { rig.Ends[2].position,rig.Ends[3].position };
                    rig.Animator.GetBoneTransform(HumanBodyBones.Hips).position -= Vector3.up*.10f;
                    for (int s = 0; s < 2; s++) solve.Invoke(null,new object[]{rig.Starts[s+2],rig.Middles[s+2],rig.Ends[s+2],feet[s]});
                    var narrow = rig.Capture();
                    for (int s = 0; s < 2; s++) solve.Invoke(null,new object[]{rig.Starts[s+2],rig.Middles[s+2],rig.Ends[s+2],feet[s]+Vector3.right*(s == 0 ? -.10f : .10f)});
                    var wide = rig.Capture();
                    var take = ScriptableObject.CreateInstance<AnimationTake>(); take.bonePaths = rig.Paths; take.lastFrame = 30;
                    take.keys.Add(wide.Copy(0)); take.keys.Add(wide.Copy(30));
                    AnimationMotionImporter.ConnectWithSteps(take,rig,narrow,narrow,1,.09f);
                    Check(take.lastFrame == 90,"Step bridges add duration without shortening dance");
                    rig.Apply(narrow);
                    var hipBone = rig.Animator.GetBoneTransform(HumanBodyBones.Hips);
                    Vector3 originalHips = hipBone.position;
                    rig.Apply(take.keys[2]);
                    Check(Vector3.ProjectOnPlane(hipBone.position-originalHips,Vector3.up).magnitude > .005f,"Pelvis anticipates the step before lift-off");
                    Check(Vector3.Distance(rig.Ends[2].position,feet[0]) < .003f && Vector3.Distance(rig.Ends[3].position,feet[1]) < .003f,"Both feet stay planted during anticipation");
                    rig.Apply(take.keys[7]);
                    float lift0 = rig.Ends[2].position.y-feet[0].y, lift1 = rig.Ends[3].position.y-feet[1].y;
                    int support = lift0 > lift1 ? 1 : 0;
                    Check(Vector3.Dot(Vector3.ProjectOnPlane(hipBone.position-originalHips,Vector3.up),Vector3.ProjectOnPlane(feet[support]-originalHips,Vector3.up)) > 0,"Pelvis transfers toward the support foot");
                    Check(Mathf.Max(lift0,lift1) > .025f,"Swing foot visibly lifts during stance widening");
                    Check(Vector3.Distance(rig.Ends[support+2].position,feet[support]) < .003f,"Opposite support foot stays planted within 3mm");
                    Check(take.keys[0].bones.Select((b,i)=>Quaternion.Angle(b.rotation,narrow.bones[i].rotation)).Max()<.01f,"Exact narrow start pose retained");
                    Check(take.keys[90].bones.Select((b,i)=>Quaternion.Angle(b.rotation,narrow.bones[i].rotation)).Max()<.01f,"Exact narrow end pose retained");
                    Check(take.keys[45].bones.Select((b,i)=>Quaternion.Angle(b.rotation,wide.bones[i].rotation)).Max()<.01f,"Original dance section preserved");
                    rig.Apply(take.keys[67]);
                    Check(Mathf.Max(rig.Ends[2].position.y-feet[0].y,rig.Ends[3].position.y-feet[1].y)>.025f,"Return transition lifts a foot too");
                    var clip = AnimationClipBaker.Bake(take,rig,true); Check(clip.isHumanMotion,"Step transitions bake to Humanoid");
                    UnityEngine.Object.DestroyImmediate(clip); UnityEngine.Object.DestroyImmediate(take);
                    var still = ScriptableObject.CreateInstance<AnimationTake>(); still.bonePaths = rig.Paths; still.lastFrame = 30;
                    still.keys.Add(narrow.Copy(0)); still.keys.Add(narrow.Copy(30));
                    AnimationMotionImporter.ConnectWithSteps(still,rig,narrow,narrow,1,.09f);
                    rig.Apply(still.keys[7]);
                    Check(Vector3.Distance(hipBone.position,originalHips) < .00001f,"Matching stances do not add artificial body swaying");
                    UnityEngine.Object.DestroyImmediate(still);
                    var repeated = ScriptableObject.CreateInstance<AnimationTake>(); repeated.bonePaths = rig.Paths; repeated.lastFrame = 30;
                    repeated.keys.Add(wide.Copy(0)); repeated.keys.Add(wide.Copy(30));
                    for (int pass = 0; pass < 4; pass++) AnimationMotionImporter.ConnectWithSteps(repeated,rig,narrow,narrow,.6f,.09f);
                    Check(repeated.lastFrame == 66 && repeated.endpointLeadFrames == 18 && repeated.endpointTailFrames == 18,"Repeated apply retains exactly 0.6 seconds per end without stacking");
                    UnityEngine.Object.DestroyImmediate(repeated);
                }
                File.WriteAllText("steps-result.txt","PASS: swing clearance, planted support, exact idle endpoints, preserved dance and Humanoid export."); EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText("steps-result.txt",e.ToString()); EditorApplication.Exit(1); }
        }
        public static void RunEndpoints()
        {
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Skeleton.fbx");
                var body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/SM_Body_01.fbx");
                using (var rig = new AuthoringRig(source, body))
                {
                    var data = JsonUtility.FromJson<GeneratedMotion>(File.ReadAllText("../Tools/MotionGeneration/jobs/20260913-031850-ad2f5ba0/motion.json"));
                    var take = AnimationMotionImporter.Create(data, rig, source, body, null, null, false);
                    AnimationMotionImporter.PlantFeet(take, rig);
                    var mid = take.keys[take.lastFrame/2].Copy(take.lastFrame/2);
                    var start = rig.Rest.Copy(0); var end = rig.Rest.Copy(0);
                    int hips = Array.IndexOf(rig.Bones, rig.Animator.GetBoneTransform(HumanBodyBones.Hips));
                    end.bones[hips].position += Vector3.right * .1f;
                    AnimationMotionImporter.MatchEndpoints(take, start, end, .4f);
                    Check(take.keys[0].bones.Select((b,i) => Quaternion.Angle(b.rotation,start.bones[i].rotation)).Max() < .01f, "Start matches captured idle rotations");
                    Check(take.keys[take.lastFrame].bones.Select((b,i) => Vector3.Distance(b.position,end.bones[i].position)).Max() < .000001f, "End matches exact captured positions");
                    Check(take.keys[take.lastFrame/2].bones.Select((b,i) => Quaternion.Angle(b.rotation,mid.bones[i].rotation)).Max() < .01f, "Middle dance motion remains unchanged");
                    Check(start.frame == 0 && end.frame == 0, "Captured poses not mutated");
                    var clip = AnimationClipBaker.Bake(take, rig, true);
                    Check(clip.isHumanMotion, "Matched dance bakes to Humanoid");
                    var sampledIdle = AnimationMotionImporter.SampleEndpoint(clip, 0, source, rig.Paths);
                    Check(sampledIdle.bones.Length == start.bones.Length && sampledIdle.effectors.All(e => e.weight == 0), "Humanoid clip endpoint samples as editable FK pose");
                    var savedWorking = rig.Capture();
                    rig.Apply(rig.Rest); clip.SampleAnimation(rig.Root,0);
                    var expectedSample = rig.Capture(); rig.Apply(savedWorking);
                    Check(sampledIdle.bones.Select((b,i) => Quaternion.Angle(b.rotation,expectedSample.bones[i].rotation)).Max() < .1f, "Endpoint equals Unity Humanoid clip sampling on the target character");
                    var sampledMiddle = AnimationMotionImporter.SampleEndpoint(clip,clip.length/2,source,rig.Paths);
                    Check(sampledMiddle.bones.Select((b,i) => Quaternion.Angle(b.rotation,sampledIdle.bones[i].rotation)).Max() > 5, "Different clip time produces a different sampled pose");
                    Check(rig.Capture().bones.Select((b,i) => Vector3.Distance(b.position,savedWorking.bones[i].position)).Max() < .00001f, "Sampling leaves working rig unchanged");
                    var generic = new AnimationClip();
                    generic.SetCurve(rig.Paths[hips], typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0,.1f,1,.3f));
                    var genericPose = AnimationMotionImporter.SampleEndpoint(generic,.75f,source,rig.Paths);
                    Check(Mathf.Abs(genericPose.bones[hips].position.x-.25f) < .0001f, "Generic clip samples requested time on matching skeleton");
                    var clampedPose = AnimationMotionImporter.SampleEndpoint(generic,5,source,rig.Paths);
                    Check(Mathf.Abs(clampedPose.bones[hips].position.x-.3f) < .0001f, "Clip sample time is clamped");
                    UnityEngine.Object.DestroyImmediate(generic);
                    var incompatible = new AnimationClip();
                    incompatible.SetCurve("MissingBone", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0,0,1,1));
                    bool rejected = false;
                    try { AnimationMotionImporter.SampleEndpoint(incompatible,0,source,rig.Paths); }
                    catch (InvalidOperationException) { rejected = true; }
                    Check(rejected,"Incompatible generic clip is rejected");
                    UnityEngine.Object.DestroyImmediate(incompatible);
                    UnityEngine.Object.DestroyImmediate(clip);
                    take.lastFrame = 1;
                    AnimationMotionImporter.MatchEndpoints(take, start, end, 2);
                    Check(take.keys.Count == 2 && take.keys[1].bones[hips].position == end.bones[hips].position, "Short take retains both exact endpoints");
                    UnityEngine.Object.DestroyImmediate(take);
                }
                File.WriteAllText("endpoints-result.txt", "PASS: exact idle endpoints, preserved middle dance, immutable captures, short take and Humanoid bake.");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText("endpoints-result.txt", e.ToString()); EditorApplication.Exit(1); }
        }
        public static void RunFootCleanup()
        {
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Skeleton.fbx");
                var body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/SM_Body_01.fbx");
                using (var rig = new AuthoringRig(source, body))
                {
                    var take = ScriptableObject.CreateInstance<AnimationTake>();
                    take.bonePaths = (string[])rig.Paths.Clone(); take.frameRate = 30; take.lastFrame = 60;
                    int hips = Array.IndexOf(rig.Bones, rig.Animator.GetBoneTransform(HumanBodyBones.Hips));
                    for (int f = 0; f <= 60; f++)
                    {
                        var key = rig.Rest.Copy(f);
                        key.bones[hips].position += new Vector3(f*.001f, f < 30 ? 0 : .2f, 0);
                        take.keys.Add(key);
                    }
                    var original = take.keys.Select(k => k.Copy(k.frame)).ToArray();
                    var before = rig.Capture();
                    int contacts = AnimationMotionImporter.PlantFeet(take, rig);
                    Check(contacts == 2, "Detects one ground contact per foot and releases raised feet");
                    rig.Apply(take.keys[5]); var planted = rig.Ends[2].position;
                    rig.Apply(take.keys[20]);
                    Check(Vector3.Distance(planted, rig.Ends[2].position) < .002f, "Ground-contact drift reduced below 2mm");
                    Check(take.keys.Select((k,i) => Vector3.Distance(k.bones[hips].position, original[i].bones[hips].position)).Max() < .00001f, "Preserves root motion");
                    Check(take.keys[50].bones.Select((b,i) => Quaternion.Angle(b.rotation, original[50].bones[i].rotation)).Max() < .01f, "Airborne pose unchanged");
                    Check(take.keys.All(k => k.effectors.All(e => e.weight == 0)), "Cleanup baked to editable FK");
                    var clip = AnimationClipBaker.Bake(take, rig, true);
                    Check(clip.isHumanMotion, "Cleaned motion bakes as Humanoid");
                    UnityEngine.Object.DestroyImmediate(clip); UnityEngine.Object.DestroyImmediate(take);
                    foreach (var file in Directory.GetFiles("../Tools/MotionGeneration/jobs", "motion.json", SearchOption.AllDirectories))
                    {
                        if (!Path.GetFileName(Path.GetDirectoryName(file)).StartsWith("2026")) continue;
                        var data = JsonUtility.FromJson<GeneratedMotion>(File.ReadAllText(file));
                        var real = AnimationMotionImporter.Create(data, rig, source, body, null, null, false);
                        int found = AnimationMotionImporter.PlantFeet(real, rig);
                        Check(real.keys.All(k => k.bones.All(b => !float.IsNaN(b.rotation.x))), "Actual generated motion cleanup finite: " + file + " contacts=" + found);
                        UnityEngine.Object.DestroyImmediate(real);
                    }
                }
                File.WriteAllText("foot-cleanup-result.txt", "PASS: contact drift <2mm, lift release, root preserved, FK keys, Humanoid bake and actual generated jobs.");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText("foot-cleanup-result.txt", e.ToString()); EditorApplication.Exit(1); }
        }
        public static void RunMachineGate()
        {
            try
            {
                var type = typeof(AnimationStudioWindow);
                var flags = BindingFlags.NonPublic | BindingFlags.Static;
                Check((bool)type.GetField("MotionGenerationAvailable", flags).GetValue(null), "Generation is enabled on the intended workstation");
                var match = type.GetMethod("MatchesMotionWorkstation", flags);
                Check(!(bool)match.Invoke(null, new object[] { "another-workstation" }), "Other workstation identities are rejected");
                Check(!(bool)match.Invoke(null, new object[] { null }), "Missing machine identity fails closed");
                var tabs = (string[])type.GetField("InspectorTabs", flags).GetValue(null);
                Check(!tabs.Contains("Generate") && tabs.Contains("Actors"), "Default inspector has no Generate tab");
                File.WriteAllText("machine-gate-result.txt", "PASS: current workstation allowed; other/missing identities denied; public inspector excludes Generate.");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText("machine-gate-result.txt", e.ToString()); EditorApplication.Exit(1); }
        }
        public static void RunReal()
        {
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Skeleton.fbx");
                var body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/SM_Body_01.fbx");
                foreach (string kind in new[] { "text", "video" })
                using (var rig = new AuthoringRig(source, body))
                {
                    var data = JsonUtility.FromJson<GeneratedMotion>(File.ReadAllText("../Tools/MotionGeneration/jobs/" + kind + "-test/motion.json"));
                    var take = AnimationMotionImporter.Create(data,rig,source,body,null,null,false);
                    try
                    {
                        Check(take.keys.Count >= 60, kind + " actual inference imports at least 2 seconds");
                        Check(take.keys.Skip(1).Any(k => k.bones.Select((b,i) => Quaternion.Angle(b.rotation,take.keys[0].bones[i].rotation)).Max() > 10),kind + " contains changing body poses");
                        var clip = AnimationClipBaker.Bake(take,rig,true);
                        Check(clip.isHumanMotion && clip.length > 1.9,kind + " actual output bakes to Humanoid");
                        UnityEngine.Object.DestroyImmediate(clip);
                        rig.SetNeutralShading(true);
                        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(rig.Root,UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                        try
                        {
                            for (int shot = 0; shot < 3; shot++)
                            {
                                rig.Apply(take.keys[shot * (take.keys.Count-1)/2]);
                                var go = new GameObject("Validation camera");
                                var camera = go.AddComponent<Camera>();
                                var target = rig.Animator.GetBoneTransform(HumanBodyBones.Hips).position + Vector3.up * .05f;
                                camera.transform.position = target + new Vector3(2,1,4);
                                camera.transform.LookAt(target); camera.orthographic = true; camera.orthographicSize = 1.25f;
                                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f,.14f,.16f);
                                var rt = new RenderTexture(600,600,24); camera.targetTexture = rt;
                                camera.Render();
                                var previous = RenderTexture.active; RenderTexture.active = rt;
                                var image = new Texture2D(600,600,TextureFormat.RGB24,false);
                                image.ReadPixels(new Rect(0,0,600,600),0,0); image.Apply();
                                File.WriteAllBytes(kind + "-motion-" + shot + ".png",image.EncodeToPNG());
                                RenderTexture.active = previous; camera.targetTexture = null;
                                UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(go);
                            }
                        }
                        finally { UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(rig.Root,rig.Scene); }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(take); }
                }
                File.WriteAllText("generation-real-result.txt","PASS: actual local text/video inference, retargeting and Humanoid baking. Preview snapshots saved.");
                EditorApplication.Exit(0);
            }
            catch(Exception e) { Debug.LogException(e); File.WriteAllText("generation-real-result.txt",e.ToString()); EditorApplication.Exit(1); }
        }
        public static void Run()
        {
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Skeleton.fbx");
                var body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/SM_Body_01.fbx");
                using (var rig = new AuthoringRig(source, body))
                {
                    var before = rig.Capture();
                    var fixture = JsonUtility.FromJson<GeneratedMotion>(File.ReadAllText("../Tools/MotionGeneration/jobs/fixture/motion.json"));
                    var take = AnimationMotionImporter.Create(fixture, rig, source, body, null, null, false);
                    try
                    {
                        Check(take.keys.Count == 31 && take.lastFrame == 30 && take.frameRate == 30, "Frame count and timing retained");
                        Check(rig.Capture().bones.Select((b,i) => Quaternion.Angle(b.rotation,before.bones[i].rotation)).Max() < .01, "Import restores original working pose");
                        int arm = Array.IndexOf(rig.Bones,rig.Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm));
                        int hips = Array.IndexOf(rig.Bones,rig.Animator.GetBoneTransform(HumanBodyBones.Hips));
                        Check(Quaternion.Angle(take.keys[0].bones[arm].rotation,take.keys[30].bones[arm].rotation) > 30, "Retargeted left arm retains movement");
                        Check(Vector3.Distance(take.keys[0].bones[hips].position,take.keys[30].bones[hips].position) > .5, "Root travel retained");
                        Check(take.keys.All(k => k.effectors.All(e => e.weight == 0)), "Generated keys are editable FK poses with matched IK targets");
                        var clip = AnimationClipBaker.Bake(take,rig,true);
                        Check(clip.isHumanMotion && clip.length > .9, "Generated take bakes to a Humanoid animation clip");
                        UnityEngine.Object.DestroyImmediate(clip);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(take); }
                    fixture.parents[1] = 8;
                    bool rejected = false;
                    try { AnimationMotionImporter.Validate(fixture); } catch(InvalidOperationException) { rejected = true; }
                    Check(rejected,"Invalid hierarchy rejected before import");
                }
                string previousTools = EditorPrefs.GetString("MashBox.MotionToolPath", "");
                var window = ScriptableObject.CreateInstance<AnimationStudioWindow>();
                try
                {
                    EditorPrefs.SetString("MashBox.MotionToolPath",Path.GetFullPath("../Tools/MotionGeneration"));
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var type = typeof(AnimationStudioWindow);
                    type.GetField("motionPid",flags).SetValue(window,0);
                    type.GetMethod("StartMotionJob",flags).Invoke(window,null);
                    var process = (System.Diagnostics.Process)type.GetMethod("GetMotionProcess",flags).Invoke(window,null);
                    Check(process != null,"Unity launches the local Python worker");
                    string start = (string)type.GetField("motionStartTicks",flags).GetValue(window);
                    type.GetField("motionStartTicks",flags).SetValue(window,"incorrect");
                    Check(type.GetMethod("GetMotionProcess",flags).Invoke(window,null) == null,"Cancellation refuses a mismatched process identity");
                    type.GetField("motionStartTicks",flags).SetValue(window,start);
                    type.GetMethod("CancelMotionJob",flags).Invoke(window,null);
                    Check(process.WaitForExit(5000),"Cancel stops the owned worker");
                    process.Dispose();
                }
                finally
                {
                    if (string.IsNullOrEmpty(previousTools)) EditorPrefs.DeleteKey("MashBox.MotionToolPath");
                    else EditorPrefs.SetString("MashBox.MotionToolPath",previousTools);
                    UnityEngine.Object.DestroyImmediate(window);
                }
                File.WriteAllText("generation-validation-result.txt","PASS: retargeting, timing, root motion, pose preservation, FK/IK editing, Humanoid baking, malformed data, worker launch and cancellation.");
                EditorApplication.Exit(0);
            }
            catch(Exception e) { Debug.LogException(e); File.WriteAllText("generation-validation-result.txt",e.ToString()); EditorApplication.Exit(1); }
        }
    }
}
