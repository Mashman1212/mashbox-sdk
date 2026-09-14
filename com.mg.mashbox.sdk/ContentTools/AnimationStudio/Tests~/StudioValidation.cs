using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioValidation
    {
        private static void Check(bool value, string name)
        {
            if (!value) throw new Exception("FAIL: " + name);
            Debug.Log("PASS: " + name);
        }
        public static void Run()
        {
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Skeleton.fbx");
                var body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/SM_Body_01.fbx");
                using (var rig = new AuthoringRig(source, body))
                {
                    Check(rig.HasIK, "Final IK initialized on Skeleton: " + rig.SolverStatus);
                    Check(rig.Root.GetComponentsInChildren<MonoBehaviour>(true).Length == 0, "Preview contains no gameplay scripts");
                    Check(rig.Root.GetComponentsInChildren<SkinnedMeshRenderer>().All(s => s.bones.All(b => b)), "Body bones remapped");
                    var take = ScriptableObject.CreateInstance<AnimationTake>();
                    take.character = source; take.body = body; take.bonePaths = rig.Paths;
                    take.lastFrame = 15;
                    take.loop = false;
                    var first = rig.Capture();
                    take.SetKey(first, 0);
                    var moved = first.Copy(15);
                    moved.effectors[0] = rig.MatchEffector(0, 1);
                    moved.effectors[0].position += new Vector3(0, 0.10f, 0.12f);
                    moved.effectors[2] = rig.MatchEffector(2, 1);
                    moved.effectors[3] = rig.MatchEffector(3, 1);
                    rig.Apply(moved);
                    Check(Vector3.Distance(rig.Ends[0].position, rig.Root.transform.TransformPoint(moved.effectors[0].position)) < 0.025f, "Hand reaches IK target");
                    var expected = rig.Capture();
                    rig.Apply(first); rig.Apply(moved);
                    Check(rig.Bones.Select((b,i) => Quaternion.Angle(b.localRotation, expected.bones[i].rotation)).Max() < 0.1f, "Random-access IK evaluation is deterministic");
                    int hips = Array.IndexOf(rig.Bones, rig.Animator.GetBoneTransform(HumanBodyBones.Hips));
                    moved.bones[hips].position += Vector3.down * 0.08f;
                    rig.Apply(moved);
                    for (int i = 2; i < 4; i++)
                        Check(Vector3.Distance(rig.Ends[i].position, rig.Root.transform.TransformPoint(moved.effectors[i].position)) < 0.025f, "Foot " + i + " stays pinned under hip motion");
                    take.SetKey(moved, 15);
                    var generic = AnimationClipBaker.Bake(take, rig, false);
                    rig.Apply(take.Evaluate(15)); expected = rig.Capture();
                    var avatar = rig.Animator.avatar;
                    rig.Animator.avatar = null; rig.Animator.Rebind();
                    rig.Apply(first); generic.SampleAnimation(rig.Root, 0.5f);
                    foreach (var i in Enumerable.Range(0, rig.Bones.Length).OrderByDescending(i => Vector3.Distance(rig.Bones[i].localPosition, expected.bones[i].position)).Take(3))
                        Debug.Log("Generic position error " + rig.Paths[i] + ": " + Vector3.Distance(rig.Bones[i].localPosition, expected.bones[i].position));
                    Check(rig.Bones.Select((b,i) => Vector3.Distance(b.localPosition, expected.bones[i].position)).Max() < 0.001f, "Generic baked positions round-trip");
                    Check(rig.Bones.Select((b,i) => Quaternion.Angle(b.localRotation, expected.bones[i].rotation)).Max() < 0.1f, "Generic baked rotations round-trip");
                    rig.Animator.avatar = avatar; rig.Animator.Rebind(); rig.Apply(first);
                    var humanoid = AnimationClipBaker.Bake(take, rig, true);
                    Check(humanoid.isHumanMotion, "Humanoid clip recognized");
                    Check(AnimationUtility.GetCurveBindings(humanoid).Length == HumanTrait.MuscleCount + 7, "All muscles and body root channels baked");
                    rig.Apply(first); humanoid.SampleAnimation(rig.Root, 0.5f);
                    Check(rig.Bones.All(b => !float.IsNaN(b.position.x)), "Humanoid samples finite transforms");
                    Check(Vector3.Distance(rig.Ends[0].position, rig.Root.transform.TransformPoint(moved.effectors[0].position)) < 0.05f, "Humanoid hand round-trip within 5cm");
                    take.interpolation = PoseInterpolation.Stepped;
                    Check(take.Evaluate(14).effectors[0].weight == 0 && take.Evaluate(15).effectors[0].weight == 1, "Stepped interpolation changes exactly at key");
                    AssetDatabase.CreateAsset(take, "Assets/Validated Take.asset");
                    AssetDatabase.CreateAsset(generic, "Assets/Validated Generic.anim");
                    AssetDatabase.CreateAsset(humanoid, "Assets/Validated Humanoid.anim");
                    AssetDatabase.SaveAssets();
                    File.WriteAllText("validation-result.txt", "PASS: SM skeleton/body, FBIK targets, deterministic evaluation, pinned feet, generic and humanoid bake, stepped keys.");
                }
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex); File.WriteAllText("validation-result.txt", ex.ToString()); EditorApplication.Exit(1);
            }
        }
    }
}
