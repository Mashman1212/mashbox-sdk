using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        private readonly HashSet<HumanBodyBones> expandedCorrectionJoints = new HashSet<HumanBodyBones> { HumanBodyBones.Hips };
        private static readonly HumanBodyBones[] CorrectionPanelJoints = {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest,
            HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot
        };
        private bool TryDrawFocusedCorrectionPanel()
        {
            if (!inspectorExpanded || rig == null || !take) return false;
            EnsureActors();
            bool drawn = false;
            System.Action draw = () =>
            {
                if (take.singleFramePose || take.activeLayer < 0 || take.activeLayer >= take.layers.Count) return;
                var layer = take.layers[take.activeLayer];
                if (!layer.correctionPanel) return;
                drawn = true;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(layer.name, StudioTheme.Styles.boldLabel);
                    if (GUILayout.Button(new GUIContent("Close", "Hide this panel; keep the correction layer applied."), GUILayout.Width(64)))
                    {
                        EndCorrectionGizmo(false);
                        inspectorExpanded = false;
                        viewport?.SetInspectorExpanded(false);
                        viewport?.Repaint();
                        return;
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    float weight = EditorGUILayout.Slider("Strength", layer.weight, 0, 1);
                    bool enabled = GUILayout.Toggle(!layer.muted, "Enabled", StudioTheme.Styles.miniButton, GUILayout.Width(68));
                    if (EditorGUI.EndChangeCheck())
                    { Undo.RecordObject(take, "Adjust correction layer"); layer.weight = weight; layer.muted = !enabled; Save(); RefreshLayers(); }
                }
                inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
                DrawCorrectionLayerPanel(layer);
                EditorGUILayout.EndScrollView();
                if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Error);
            };
            if (activeActor >= 0 && activeActor < actorPreviews.Count) InActor(actorPreviews[activeActor], draw); else draw();
            return drawn;
        }

        private void DrawDualArmCorrection(StudioAnimationLayer layer)
        {
            var joints = new[] { HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
                HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm };
            bool mapped = joints.All(j => rig.Animator.GetBoneTransform(j));
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            using (new EditorGUI.DisabledScope(!mapped))
            {
                EditorGUILayout.LabelField("BOTH ARMS + CLAVICLES", StudioTheme.Styles.boldLabel);
                EditorGUI.BeginChangeCheck();
                float next = EditorGUILayout.Slider(new GUIContent("Clavicle / arm balance", "Positive raises both clavicles and lowers both upper arms. Negative reverses both."), layer.dualArmCorrection, -45, 45);
                bool changed = EditorGUI.EndChangeCheck();
                if (GUILayout.Button("Reset arm balance")) { next = 0; changed = true; }
                if (changed)
                {
                    float delta = next - layer.dualArmCorrection;
                    Undo.RecordObject(take, "Balance clavicles and arms");
                    foreach (var joint in joints)
                    {
                        var template = CorrectionFor(joint);
                        var offset = layer.offsets.FirstOrDefault(o => o.path == template.path);
                        if (offset == null)
                        {
                            offset = new StudioJointOffset { path = template.path, axisX = template.axisX,
                                axisY = template.axisY, axisZ = template.axisZ };
                            layer.offsets.Add(offset);
                        }
                        offset.angles.x += delta * (joint == HumanBodyBones.LeftShoulder || joint == HumanBodyBones.RightShoulder ? 1 : -1);
                        if (!layer.bones.Contains(offset.path)) layer.bones.Add(offset.path);
                    }
                    layer.dualArmCorrection = next;
                    Save(); RefreshLayers();
                }
            }
            if (!mapped) EditorGUILayout.HelpBox("Arm balance needs both shoulders and upper arms mapped.", MessageType.None);
        }
        private void CreateCorrectionLayer()
        {
            FinishSceneEdit(); EndCorrectionGizmo(false);
            quickCorrectionMode = QuickCorrectionMode.None;
            Undo.RecordObject(take, "Create correction layer");
            take.layers.Add(new StudioAnimationLayer { name = "Correction " + (take.layers.Count + 1), correctionPanel = true });
            take.activeLayer = take.layers.Count - 1;
            Save(); RefreshLayers(); OpenLayerSettings();
        }
        private void DrawCorrectionLayerPanel(StudioAnimationLayer layer)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("CORRECTION SLIDERS", StudioTheme.Styles.boldLabel);

            if (!rig.Animator)
            {
                EditorGUILayout.HelpBox("Joint sliders require a Humanoid character.", MessageType.Info);
                return;
            }
            DrawDualArmCorrection(layer);
            foreach (var joint in CorrectionPanelJoints)
            {
                if (!rig.Animator.GetBoneTransform(joint)) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    bool expanded = EditorGUILayout.Foldout(expandedCorrectionJoints.Contains(joint), ObjectNames.NicifyVariableName(joint.ToString()), true);
                    if (expanded) expandedCorrectionJoints.Add(joint); else expandedCorrectionJoints.Remove(joint);
                    if (expanded) DrawJointCorrectionSliders(layer, joint);
                }
            }
            if (GUILayout.Button("Reset all correction sliders"))
            {
                Undo.RecordObject(take, "Reset correction layer offsets");
                layer.dualArmCorrection = 0; layer.offsets.Clear(); Save(); RefreshLayers();
            }
        }
    }
}
