using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        private enum QuickCorrectionMode { None, Hips, Shoulders, Arms }
        [SerializeField] private QuickCorrectionMode quickCorrectionMode;
        [SerializeField] private int quickCorrectionSide;
        [SerializeField] private int quickCorrectionArmPart;
        private static readonly string[] CorrectionSides = { "Both", "Left", "Right" };
        private static readonly string[] CorrectionArmParts = { "Upper arms", "Elbows / forearms" };

        private HumanBodyBones[] QuickCorrectionJoints()
        {
            if (quickCorrectionMode == QuickCorrectionMode.Hips) return new[] { HumanBodyBones.Hips };
            var pair = quickCorrectionMode == QuickCorrectionMode.Shoulders
                ? new[] { HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder }
                : quickCorrectionArmPart == 0
                    ? new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm }
                    : new[] { HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm };
            return quickCorrectionSide == 0 ? pair : new[] { pair[Mathf.Clamp(quickCorrectionSide - 1, 0, 1)] };
        }

        private StudioBoneCorrection CorrectionFor(HumanBodyBones joint)
        {
            var bone = rig.Animator.GetBoneTransform(joint);
            int index = Array.IndexOf(rig.Bones, bone);
            if (index < 0) throw new InvalidOperationException("This joint is not in the preview skeleton.");
            var current = take.quickCorrections?.Find(c => c.path == rig.Paths[index]);
            if (current != null) return current.Copy();
            bool limb = joint != HumanBodyBones.Hips;
            var inverse = Quaternion.Inverse(RestWorldRotation(bone));
            var root = rig.Rest.bones[0].rotation;
            Vector3 right = root * Vector3.right, up = root * Vector3.up, forward = root * Vector3.forward;
            Vector3 direction = joint.ToString().StartsWith("Left", StringComparison.Ordinal) ? -right : right;
            return new StudioBoneCorrection
            {
                path = rig.Paths[index],
                axisX = inverse * (limb ? Vector3.Cross(direction, up) : right),
                axisY = inverse * (limb ? Vector3.Cross(direction, forward) : forward),
                axisZ = inverse * (limb ? direction : up)
            };
        }

        private void ApplyQuickCorrections(StudioBoneCorrection[] corrections)
        {
            FinishSceneEdit();
            Undo.RecordObject(take, "Correct " + quickCorrectionMode.ToString().ToLowerInvariant() + " across animation");
            foreach (var correction in corrections) take.SetQuickCorrection(correction);
            Save(); RefreshLayers();
        }

        private void DrawQuickCorrections()
        {
            EditorGUILayout.LabelField("WHOLE-CLIP CORRECTIONS", StudioTheme.Styles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(!rig.Animator || take.keys.Count == 0))
            {
                CorrectionButton(QuickCorrectionMode.Hips, "Hips correction");
                CorrectionButton(QuickCorrectionMode.Shoulders, "Shoulders");
                CorrectionButton(QuickCorrectionMode.Arms, "Arms");
            }
            if (!rig.Animator)
            {
                EditorGUILayout.HelpBox("Hips, shoulders and arms corrections need a character with a Humanoid bone mapping.", MessageType.None);
                if (quickCorrectionMode != QuickCorrectionMode.None && GUILayout.Button("Done")) SetQuickCorrectionMode(QuickCorrectionMode.None);
                return;
            }
            if (quickCorrectionMode == QuickCorrectionMode.None) return;
            EditorGUI.BeginChangeCheck();
            if (quickCorrectionMode != QuickCorrectionMode.Hips)
                quickCorrectionSide = EditorGUILayout.Popup("Side", quickCorrectionSide, CorrectionSides);
            if (quickCorrectionMode == QuickCorrectionMode.Arms)
                quickCorrectionArmPart = EditorGUILayout.Popup("Arm part", quickCorrectionArmPart, CorrectionArmParts);
            if (EditorGUI.EndChangeCheck()) { EndCorrectionGizmo(false); FocusQuickCorrectionJoint(); }
            if (quickCorrectionMode == QuickCorrectionMode.Hips)
                rotate = GUILayout.Toolbar(rotate ? 1 : 0, new[] { "Move [W]", "Rotate [E]" }) == 1;
            else EditorGUILayout.LabelField("Rotate gizmos [E]", StudioTheme.Styles.miniLabel);
            var requested = QuickCorrectionJoints();
            var mapped = requested.Where(j => rig.Animator.GetBoneTransform(j)).ToArray();
            if (mapped.Length != requested.Length)
                EditorGUILayout.HelpBox("Not mapped: " + string.Join(", ", requested.Except(mapped)) + ". Only mapped joints can be corrected.", MessageType.Info);
            if (mapped.Length == 0) return;
            var corrections = mapped.Select(CorrectionFor).ToArray();
            bool changed = false;
            if (quickCorrectionMode == QuickCorrectionMode.Hips)
            {
                EditorGUI.BeginChangeCheck();
                corrections[0].position = EditorGUILayout.Vector3Field(new GUIContent("Position offset (m)", "Offset in the hips parent's local axes, applied across the entire animation."), corrections[0].position);
                changed |= EditorGUI.EndChangeCheck();
            }
            bool hips = quickCorrectionMode == QuickCorrectionMode.Hips;
            changed |= DrawCorrectionAxis(corrections, 0, hips ? "Bend forward / back" : "Lift / lower");
            changed |= DrawCorrectionAxis(corrections, 1, hips ? "Side bend" : "Forward / back");
            changed |= DrawCorrectionAxis(corrections, 2, "Twist");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset correction"))
                {
                    EndCorrectionGizmo(false);
                    foreach (var correction in corrections) { correction.position = Vector3.zero; correction.angles = Vector3.zero; correction.gizmoRotation = Quaternion.identity; }
                    changed = true;
                }
                if (GUILayout.Button("Done")) SetQuickCorrectionMode(QuickCorrectionMode.None);
            }
            if (changed) Guard(() => ApplyQuickCorrections(corrections));
            EditorGUILayout.HelpBox("Drag the viewport gizmo to correct the whole clip. W moves hips; E rotates. Both mirrors a drag to the paired joint. Escape cancels a drag; Undo or Reset reverses it. IK pins and attachments still apply afterward.", MessageType.None);
            EditorGUILayout.Space(6);
        }

        private void CorrectionButton(QuickCorrectionMode mode, string label)
        {
            bool selected = quickCorrectionMode == mode;
            if (GUILayout.Toggle(selected, label, StudioTheme.Styles.miniButton) != selected)
            {
                SetQuickCorrectionMode(selected ? QuickCorrectionMode.None : mode);
            }
        }

        private static bool DrawCorrectionAxis(StudioBoneCorrection[] corrections, int axis, string label)
        {
            float first = corrections[0].angles[axis];
            bool previousMixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = corrections.Any(c => !Mathf.Approximately(c.angles[axis], first));
            EditorGUI.BeginChangeCheck();
            float next = EditorGUILayout.Slider(label, first, -90, 90);
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = previousMixed;
            if (changed) foreach (var correction in corrections) correction.angles[axis] = next;
            return changed;
        }
    }
}
