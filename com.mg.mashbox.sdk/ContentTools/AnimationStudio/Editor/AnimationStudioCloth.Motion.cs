using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        private const string ClothMotionFolder = "Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Animations/";
        private static readonly string[] ClothMotionNames = { "Rest Pose", "A-Pose", "Party Kid", "Locking Hip Hop Dance", "Gangnam Style", "Custom…" };
        [NonSerialized] private AnimationClip[] clothBuiltinClips;
        [SerializeField] private bool clothCustomMotion;
        [SerializeField] private bool clothAPoseMotion;

        private bool HasClothMotion => !clothAPoseMotion && clothClip && !clothClip.legacy && clothClip.length > 0;
        private bool CanSimulateCloth => ClothVisible && clothProfile && clothProfile.Matches(MashBoxSDK.Cloth.MashBoxClothSession.GetMesh(ClothRenderer));

        private static AnimationClip LoadClothBuiltinMotion(string name)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClothMotionFolder + name + ".anim");
            if (clip) return clip;
            return AssetDatabase.LoadAllAssetsAtPath(ClothMotionFolder + name + ".fbx")
                .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
        }

        private void DrawClothMotionSelector(ref float x, float y, float right)
        {
            if (clothBuiltinClips == null || clothBuiltinClips.Length != 3 || clothBuiltinClips.Any(c => !c))
                clothBuiltinClips = new[] {
                    LoadClothBuiltinMotion("Party Kid"),
                    LoadClothBuiltinMotion("Locking Hip Hop Dance"),
                    LoadClothBuiltinMotion("Gangnam Style")
                };
            int current = clothCustomMotion ? 5 : clothAPoseMotion ? 1 : !clothClip ? 0 : clothClip == clothBuiltinClips[0] ? 2 : clothClip == clothBuiltinClips[1] ? 3 : clothClip == clothBuiltinClips[2] ? 4 : 5;
            float width = Mathf.Min(180, Mathf.Max(80, (right - x - 8) * .45f));
            var selector = new Rect(x, y + 3, width, 20);
            if (EditorGUI.DropdownButton(selector, new GUIContent(ClothMotionNames[current], "Preview animation"), FocusType.Passive, EditorStyles.popup))
            {
                var menu = new StudioMenu();
                for (int i = 0; i < ClothMotionNames.Length; i++)
                {
                    int chosen = i;
                    menu.AddItem(new GUIContent(ClothMotionNames[i]), current == i, () => ClothAction(() =>
                    {
                        if (chosen == 5) { clothCustomMotion = true; return; }
                        var clip = chosen <= 1 ? null : LoadClothBuiltinMotion(ClothMotionNames[chosen]);
                        if (chosen > 1 && !clip) throw new InvalidOperationException("Could not load " + ClothMotionNames[chosen] + " from " + ClothMotionFolder + ". Expected a .anim clip or an FBX containing animation.");
                        SetClothMotion(clip, chosen == 1); clothCustomMotion = false;
                    }));
                }
                menu.DropDown(selector);
            }
            x += width + 8;
            if (current != 5) return;
            width = Mathf.Min(170, Mathf.Max(60, right - x - 90));
            var custom = (AnimationClip)EditorGUI.ObjectField(new Rect(x, y + 3, width, 20), clothClip, typeof(AnimationClip), false);
            if (custom != clothClip) ClothAction(() => { SetClothMotion(custom); clothCustomMotion = true; });
            x += width + 8;
        }

        [NonSerialized] private AnimationClip clothPoseClip;
        [NonSerialized] private bool clothPoseOverride;

        private void DrawClothPoseButtons(ref float x, float y)
        {
            using (new EditorGUI.DisabledScope(clothRig == null))
            {
                if (StudioSkinningEditor.DrawPosePresetButton(new Rect(x, y, 28, 26), false))
                    ClothAction(() => SetClothPose(0));
                x += 34;
                if (StudioSkinningEditor.DrawPosePresetButton(new Rect(x, y, 28, 26), true))
                    ClothAction(() => SetClothPose(2));
                x += 34;
                if (StudioSkinningEditor.DrawPosePresetButton(new Rect(x, y, 28, 26), true, true))
                    ClothAction(() => SetClothPose(3));
                x += 38;
            }
        }

        private void SetClothPose(int frame)
        {
            if (clothRig == null) return;
            EndClothStroke(); StopClothSession(); clothSimulationTime = 0;
            ApplyClothPose(frame);
            // Preview buttons temporarily override the selected simulation motion.
            clothPoseOverride = true;
            RefreshClothPickMesh();
        }

        private void ApplyClothPose(int frame)
        {
            if (clothRig == null) return;
            if (!clothPoseClip)
                clothPoseClip = AssetDatabase.LoadAllAssetsAtPath("Packages/com.mg.mashbox.sdk/Runtime/Characters/Human/Skeleton.fbx")
                    .OfType<AnimationClip>().FirstOrDefault(c => string.Equals(c.name.Trim(), "Skinning Test", StringComparison.OrdinalIgnoreCase));
            if (!clothPoseClip) throw new InvalidOperationException("The SDK Skeleton's Skinning Test poses could not be loaded.");
            for (int i = 0; i < clothRig.Bones.Length; i++) clothRig.Rest.bones[i].Apply(clothRig.Bones[i]);
            var hips = clothRig.Bones.FirstOrDefault(b => StudioSkinningDocument.BoneName(b.name) == "Hips");
            var restHips = hips ? hips.localPosition : Vector3.zero;
            var animator = clothRig.Animator;
            bool enabled = animator.enabled;
            var graph = PlayableGraph.Create("Cloth pose snapshot");
            try
            {
                animator.enabled = true;
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clothPoseClip);
                playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
                AnimationPlayableOutput.Create(graph, "Pose", animator).SetSourcePlayable(playable);
                graph.Play(); playable.SetTime(0); graph.Evaluate(0);
                var offset = hips ? restHips - hips.localPosition : Vector3.zero;
                playable.SetTime(frame / Mathf.Max(1, clothPoseClip.frameRate)); graph.Evaluate(0);
                if (hips) hips.localPosition += offset;
            }
            finally { graph.Destroy(); animator.enabled = enabled; }
        }

        private void ResumeClothMotionFromPose()
        {
            if (clothPoseOverride) SampleClothMotion(clothAnimationTime);
        }

        private void SetClothMotion(AnimationClip clip, bool aPose = false)
        {
            if (clip)
            {
                if (clip.legacy || clip.length <= 0) throw new InvalidOperationException("Choose a non-Legacy animation clip with a positive duration.");
                if (clip.isHumanMotion)
                {
                    if (!clothRig.Animator || !clothRig.Animator.avatar || !clothRig.Animator.avatar.isHuman)
                        throw new InvalidOperationException("This animation needs a valid Humanoid character avatar.");
                }
                else if (!AnimationUtility.GetCurveBindings(clip).Any(b => b.type == typeof(Transform) && Array.IndexOf(clothRig.Paths, b.path) >= 0))
                    throw new InvalidOperationException("This animation has no transform curves matching the character skeleton.");
            }
            EndClothStroke(); StopClothSession(); clothPainting = false;
            clothClip = clip; clothAPoseMotion = aPose; clothSimulationTime = 0; clothGrabParticle = -1;
            SampleClothMotion(0); RefreshClothPickMesh();
        }
    }
}
