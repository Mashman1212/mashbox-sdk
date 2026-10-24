using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal static class SingleFramePoseChecks
    {
        [MenuItem("MashBox/Animation/Validate Single Frame Pose")]
        public static void Run()
        {
            var take = ScriptableObject.CreateInstance<AnimationTake>();
            GameObject source = null;
            AnimationClip baked = null;
            var path = "Assets/__SinglePoseCheck_" + Guid.NewGuid().ToString("N") + ".anim";
            try
            {
                var pose = new PoseKey { bones = new[] { new BonePose { position = Vector3.one, rotation = Quaternion.identity, scale = Vector3.one } },
                    effectors = new[] { new EffectorPose { rotation = Quaternion.identity, pole = Vector3.up } } };
                take.SetKey(pose, 0);
                pose.bones[0].position = Vector3.right * 8;
                take.SetKey(pose, 30);
                Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(take, "Test single pose");
                take.SetSingleFramePose(true);
                Check(take.keys.Count == 2 && take.keys[1].frame == 60, "two endpoints");
                Check(take.Evaluate(30).bones[0].position == Vector3.one, "motion replaced with first pose");
                Undo.PerformUndo();
                Check(!take.singleFramePose && take.keys.Any(k => k.frame == 30), "Undo restores motion");
                take.SetSingleFramePose(true);
                var edit = take.Evaluate(60);
                edit.bones[0].rotation = Quaternion.Euler(10, 25, 40);
                edit.bones[0].position = Vector3.one * 99;
                edit.effectors[0].pole = Vector3.left * 3;
                take.SetChannelKey(edit, 60, new[] { 2 }, new[] { 4 });
                Check(take.keys[0].bones[0].position == Vector3.one, "unselected channel preserved");
                Check(take.keys[1].bones[0].rotation == edit.bones[0].rotation && take.keys[1].effectors[0].pole == edit.effectors[0].pole, "rotation and pole mirrored");
                Check(!ReferenceEquals(take.keys[0].bones, take.keys[1].bones), "independent endpoint arrays");
                take.lastFrame = 90; take.SyncSingleFramePose();
                Check(take.keys.Count == 2 && take.keys[1].frame == 90, "duration resize");
                for (int f = 0; f <= 90; f++) Check(take.Evaluate(f).bones[0].rotation == edit.bones[0].rotation, "constant evaluation");
                source = new GameObject("Single pose test rig");
                using (var rig = new AuthoringRig(source, null))
                {
                    var captured = rig.Capture(); captured.bones[0].position = Vector3.one;
                    take.SetKey(captured, 45);
                    baked = AnimationClipBaker.Bake(take, rig, false);
                }
                foreach (var binding in AnimationUtility.GetCurveBindings(baked))
                {
                    var curve = AnimationUtility.GetEditorCurve(baked, binding);
                    Check(curve.keys.All(k => Mathf.Approximately(k.value, curve.keys[0].value)), "static exported curve");
                }
                Check(Mathf.Approximately(baked.length, 3), "export preserves duration");
                AssetDatabase.CreateAsset(baked, path);
                var saveLabel = typeof(AnimationStudioWindow).GetMethod("SaveSinglePoseLabel", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                AssetDatabase.SetLabels(baked, new[] { "ExistingLabel" });
                saveLabel.Invoke(null, new object[] { baked, true });
                AssetDatabase.SaveAssetIfDirty(baked);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var labels = AssetDatabase.GetLabels(AssetDatabase.LoadAssetAtPath<AnimationClip>(path));
                Check(labels.Contains("MashBoxSingleFramePose") && labels.Contains("ExistingLabel"), "mode and other labels persist");
                saveLabel.Invoke(null, new object[] { baked, false });
                Check(!AssetDatabase.GetLabels(baked).Contains("MashBoxSingleFramePose"), "disable removes mode label");
                take.SetSingleFramePose(false);
                take.SetKey(edit, 45);
                Check(take.keys.Any(k => k.frame == 45), "normal animation restored");
                Debug.Log("SingleFramePoseChecks: PASS — linked edits, pole targets, Undo, duration, static bake, and saved mode.");
            }
            finally
            {
                Undo.ClearUndo(take);
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path)) AssetDatabase.DeleteAsset(path);
                else if (baked) UnityEngine.Object.DestroyImmediate(baked);
                if (source) UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(take);
            }
        }
        private static void Check(bool condition, string label)
        { if (!condition) throw new InvalidOperationException("SingleFramePoseChecks: " + label); }
    }
}
