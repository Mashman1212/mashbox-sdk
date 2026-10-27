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
        private static readonly string[] ClothMotionNames = { "Rest Pose", "Party Kid", "Locking Hip Hop Dance", "Custom…" };
        [NonSerialized] private AnimationClip[] clothBuiltinClips;
        [SerializeField] private bool clothCustomMotion;

        private bool HasClothMotion => clothClip && !clothClip.legacy && clothClip.length > 0;
        private bool CanSimulateCloth => ClothVisible && clothProfile && clothProfile.Matches(MashBoxSDK.Cloth.MashBoxClothSession.GetMesh(ClothRenderer));

        private void DrawClothMotionSelector(ref float x, float y, float right)
        {
            if (clothBuiltinClips == null || clothBuiltinClips.Length != 2)
                clothBuiltinClips = new[] {
                    AssetDatabase.LoadAssetAtPath<AnimationClip>(ClothMotionFolder + "Party Kid.anim"),
                    AssetDatabase.LoadAssetAtPath<AnimationClip>(ClothMotionFolder + "Locking Hip Hop Dance.anim")
                };
            int current = clothCustomMotion ? 3 : !clothClip ? 0 : clothClip == clothBuiltinClips[0] ? 1 : clothClip == clothBuiltinClips[1] ? 2 : 3;
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
                        if (chosen == 3) { clothCustomMotion = true; return; }
                        var clip = chosen == 0 ? null : clothBuiltinClips[chosen - 1];
                        if (chosen != 0 && !clip) throw new InvalidOperationException("Built-in animation could not be loaded. Refresh the SDK package.");
                        SetClothMotion(clip); clothCustomMotion = false;
                    }));
                }
                menu.DropDown(selector);
            }
            x += width + 8;
            if (current != 3) return;
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
            if (!clothPoseClip)
                clothPoseClip = AssetDatabase.LoadAllAssetsAtPath("Packages/com.mg.mashbox.sdk/Runtime/Characters/Human/Skeleton.fbx")
                    .OfType<AnimationClip>().FirstOrDefault(c => string.Equals(c.name.Trim(), "Skinning Test", StringComparison.OrdinalIgnoreCase));
            if (!clothPoseClip) throw new InvalidOperationException("The SDK Skeleton's Skinning Test poses could not be loaded.");
            EndClothStroke(); StopClothSession();
            clothSimulationTime = 0;
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
            // One-shot pose sample: keep the selected emote and its timeline position.
            clothPoseOverride = true;
            RefreshClothPickMesh();
        }

        private void ResumeClothMotionFromPose()
        {
            if (clothPoseOverride) SampleClothMotion(clothAnimationTime);
        }

        private void SetClothMotion(AnimationClip clip)
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
            clothClip = clip; clothSimulationTime = 0; clothGrabParticle = -1;
            SampleClothMotion(0); RefreshClothPickMesh();
        }
    }
}
