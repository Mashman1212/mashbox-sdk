using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal static class AnimationClipBaker
    {
        // The finger curve names differ from HumanTrait's display names.
        internal static string MuscleProperty(string name)
        {
            foreach (string side in new[] { "Left", "Right" })
                foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                {
                    string prefix = side + " " + finger + " ";
                    if (name.StartsWith(prefix, StringComparison.Ordinal))
                        return side + "Hand." + finger + "." + name.Substring(prefix.Length);
                }
            return name;
        }

        public static AnimationClip Bake(AnimationTake take, AuthoringRig rig, bool humanoid)
        {
            if (take.keys.Count == 0) throw new InvalidOperationException("Add at least one pose key before baking.");
            if (humanoid && !rig.Animator) throw new InvalidOperationException("Humanoid baking requires a valid Avatar.");
            var clip = new AnimationClip { name = take.name, frameRate = take.frameRate };
            var curves = new Dictionary<EditorCurveBinding, AnimationCurve>();
            var restore = rig.Capture();
            HumanPoseHandler handler = null;
            try
            {
                if (humanoid) handler = new HumanPoseHandler(rig.Animator.avatar, rig.Root.transform);
                var humanPose = new HumanPose();
                for (int frame = 0; frame <= take.lastFrame; frame++)
                {
                    if (frame % 30 == 0 && EditorUtility.DisplayCancelableProgressBar("Bake animation", "Sampling frame " + frame, (float)frame / Mathf.Max(1, take.lastFrame)))
                        throw new OperationCanceledException("Bake cancelled. No clip was saved.");
                    rig.Apply(take.Evaluate(frame));
                    float time = (float)frame / take.frameRate;
                    if (humanoid)
                    {
                        handler.GetHumanPose(ref humanPose);
                        for (int m = 0; m < HumanTrait.MuscleCount; m++)
                            Add(curves, "", typeof(Animator), MuscleProperty(HumanTrait.MuscleName[m]), time, humanPose.muscles[m]);
                        Vector(curves, "", typeof(Animator), "RootT", time, humanPose.bodyPosition);
                        Rotation(curves, "", typeof(Animator), "RootQ", time, humanPose.bodyRotation);
                    }
                    else
                        for (int i = 0; i < rig.Bones.Length; i++)
                        {
                            var bone = rig.Bones[i];
                            Vector(curves, rig.Paths[i], typeof(Transform), "m_LocalPosition", time, bone.localPosition);
                            Rotation(curves, rig.Paths[i], typeof(Transform), "m_LocalRotation", time, bone.localRotation);
                            Vector(curves, rig.Paths[i], typeof(Transform), "m_LocalScale", time, bone.localScale);
                        }
                }
                foreach (var pair in curves)
                {
                    var mode = take.interpolation == PoseInterpolation.Stepped
                        ? AnimationUtility.TangentMode.Constant : AnimationUtility.TangentMode.Linear;
                    for (int i = 0; i < pair.Value.length; i++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(pair.Value, i, mode);
                        AnimationUtility.SetKeyRightTangentMode(pair.Value, i, mode);
                    }
                    AnimationUtility.SetEditorCurve(clip, pair.Key, pair.Value);
                }
                clip.EnsureQuaternionContinuity();
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = take.loop;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                return clip;
            }
            catch { UnityEngine.Object.DestroyImmediate(clip); throw; }
            finally
            {
                handler?.Dispose();
                rig.Apply(restore);
                EditorUtility.ClearProgressBar();
            }
        }

        private static void Add(Dictionary<EditorCurveBinding, AnimationCurve> curves, string path, Type type, string property, float time, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidOperationException("Non-finite pose value at " + property + ". Check the rig before baking.");
            var binding = EditorCurveBinding.FloatCurve(path, type, property);
            if (!curves.TryGetValue(binding, out var curve)) curves[binding] = curve = new AnimationCurve();
            curve.AddKey(time, value);
        }
        private static void Vector(Dictionary<EditorCurveBinding, AnimationCurve> curves, string path, Type type, string property, float time, Vector3 value)
        {
            Add(curves, path, type, property + ".x", time, value.x);
            Add(curves, path, type, property + ".y", time, value.y);
            Add(curves, path, type, property + ".z", time, value.z);
        }
        private static void Rotation(Dictionary<EditorCurveBinding, AnimationCurve> curves, string path, Type type, string property, float time, Quaternion value)
        {
            Add(curves, path, type, property + ".x", time, value.x);
            Add(curves, path, type, property + ".y", time, value.y);
            Add(curves, path, type, property + ".z", time, value.z);
            Add(curves, path, type, property + ".w", time, value.w);
        }
    }
}
