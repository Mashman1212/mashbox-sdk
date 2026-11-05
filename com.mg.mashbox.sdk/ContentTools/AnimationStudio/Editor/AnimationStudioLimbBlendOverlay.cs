using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class MashBoxStudioViewport
    {
        private IMGUIContainer limbBlend;

        internal void BindLimbBlend(System.Action draw)
        {
            BindViewportControl(ref limbBlend, "mashbox-limb-blend", draw);
            limbBlend.style.left = 8;
            limbBlend.style.bottom = AnimationTimelineOverlayBottom;
            limbBlend.style.width = 310;
            limbBlend.style.display = panelsModeling ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }

    public sealed partial class AnimationStudioWindow
    {
        private void DrawViewportLimbBlend()
        {
            if (!this || !take || rig == null || pose == null) return;
            EnsureActors();
            var activePreview = activeActor >= 0 && activeActor < actorPreviews.Count ? actorPreviews[activeActor] : null;
            bool bikeSelected = false;
            if (activePreview == null) bikeSelected = IsBike;
            else InActor(activePreview, () => bikeSelected = IsBike);
            if (bikeSelected) return;
            var selected = SelectedBlendLimbs();
            if (selected.Count == 0) return;
            float first = 0;
            bool mixed = false, hasFirst = false;
            foreach (var item in selected)
                WithBlendActor(item.Actor, () =>
                {
                    float weight = pose.effectors[item.Index].weight;
                    if (!hasFirst) { first = weight; hasFirst = true; }
                    else mixed |= !Mathf.Approximately(first, weight);
                });

            var area = GUILayoutUtility.GetRect(310, 26, GUILayout.Width(310), GUILayout.Height(26));
            EditorGUI.DrawRect(area, StudioTheme.Current.Header);
            string label = selected.Count == 1
                ? LimbNames[selected[0].Index]
                : selected.Count + " limbs";
            GUI.Label(new Rect(area.x + 7, area.y + 3, 98, 20), label + " IK", StudioTheme.Styles.miniLabel);
            bool previousMixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = mixed;
            EditorGUI.BeginChangeCheck();
            var blendSlider = new Rect(area.x + 105, area.y + 6, 98, 18);
            DrawAnimationHover(blendSlider);
            float next = GUI.HorizontalSlider(blendSlider, first, 0, 1);
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = previousMixed;
            GUI.Label(new Rect(area.x + 210, area.y + 3, 38, 20),
                new GUIContent(mixed ? "Mixed" : Mathf.RoundToInt(first * 100) + "%",
                    "0% = FK, 100% = IK. Preserves the visible pose for all selected limbs. Hold Alt while dragging to blend between the stored poses instead."), StudioTheme.Styles.miniLabel);
            if (changed) Guard(() => ApplySelectedBlend(selected, next, true, false));
            DrawIKPinButtons(area, SelectedBlendLimbs(false));
        }

        private AnimationTake selectedRigChannelActor;
        private int selectedRigChannelBone = -1;
        private int selectedRigChannelAxis = -1;
        private readonly List<(AnimationTake actor, int index, int axis, float value, bool key)> pendingRigChannels
            = new List<(AnimationTake, int, int, float, bool)>();
        private bool RigChannelSelected(int boneIndex, int axis = 1) =>
            boneIndex >= 0 && selectedRigChannelActor == take && selectedRigChannelBone == boneIndex && selectedRigChannelAxis == axis;
        private void SelectRigChannel(int boneIndex, int axis = 1)
        {
            if (boneIndex < 0) return;
            if (RigChannelSelected(boneIndex, axis)) return;
            selectedRigChannelActor = take;
            selectedRigChannelBone = boneIndex;
            selectedRigChannelAxis = axis;
            timelineRangeStart = timelineRangeEnd = -1;
            viewport?.Repaint(); Repaint();
        }
        private void DrawRigChannelKeyMenu(Rect area, int boneIndex, int axis)
        {
            var evt = Event.current;
            if (!area.Contains(evt.mousePosition) ||
                !((evt.type == EventType.MouseDown && evt.button == 1) || evt.type == EventType.ContextClick)) return;
            SelectRigChannel(boneIndex, axis);
            var actor = take;
            var menu = new GenericMenu();
            if (boneIndex < 0 || playing) menu.AddDisabledItem(new GUIContent("Key Selected"));
            else menu.AddItem(new GUIContent("Key Selected"), false, () =>
                WithBlendActor(actor, () => Guard(() =>
                    EditRigChannel(boneIndex, axis, pose.bones[boneIndex].position[axis], true))));
            menu.ShowAsContext();
            evt.Use();
        }
        private void ApplyPendingRigChannels()
        {
            if (pendingRigChannels.Count == 0) return;
            bool unkeyed = !autoKey && pendingRigChannels.Any(edit => !edit.key);
            if (unkeyed) CaptureUnkeyedPoseUndo();
            foreach (var edit in pendingRigChannels)
                WithBlendActor(edit.actor, () => Guard(() => EditRigChannel(edit.index, edit.axis, edit.value, edit.key)));
            if (unkeyed) RecordUnkeyedPoseUndo();
            pendingRigChannels.Clear();
        }
        private void DrawViewportRigChannels()
        {
            using (new GUILayout.VerticalScope(StudioTheme.Styles.helpBox))
            {
                GUILayout.Label("Hand / foot targets", StudioTheme.Styles.boldLabel);
                for (int limb = 0; limb < 4; limb++) DrawCompactLimbTargetChannels(limb);
            }
        }

        private void DrawCompactBikeBalance()
        {
            using (new GUILayout.VerticalScope(StudioTheme.Styles.helpBox))
            {
                GUILayout.Label("BMX balance", StudioTheme.Styles.boldLabel);
                DrawRigChannel("PID X", "PIDBalanceControl", 1);
                DrawRigChannel("PID Z", "PIDBalanceControl", 2);
            }
        }

        private void DrawCompactLimbTargetChannels(int limb)
        {
            int weightIndex = Array.FindIndex(rig.Bones, b => StudioRigChannels.MatchesName(b.name, StudioRigChannels.WeightNames[limb]));
            int targetIndex = Array.FindIndex(rig.Bones, b => StudioRigChannels.MatchesName(b.name, StudioRigChannels.TargetNames[limb]));
            string shortName = limb == 0 ? "L hand" : limb == 1 ? "R hand" : limb == 2 ? "L foot" : "R foot";
            var area = GUILayoutUtility.GetRect(250, 24, GUILayout.Width(250), GUILayout.Height(24));
            DrawRigChannelKeyMenu(area, Event.current.mousePosition.x < area.x + 158 ? weightIndex : targetIndex, 1);
            EditorGUI.DrawRect(area, StudioTheme.Current.Header);
            if (RigChannelSelected(weightIndex))
                EditorGUI.DrawRect(new Rect(area.x, area.y, 158, area.height), new Color(.5f, .7f, .3f, .18f));
            if (RigChannelSelected(targetIndex))
                EditorGUI.DrawRect(new Rect(area.x + 158, area.y, 92, area.height), new Color(.5f, .7f, .3f, .18f));
            if (weightIndex >= 0)
            {
                DrawAnimationHover(new Rect(area.x + 7, area.y + 3, 45, 18));
                if (!playing) DrawAnimationHover(new Rect(area.x + 55, area.y + 4, 58, 18));
            }
            if (targetIndex >= 0)
            {
                DrawAnimationHover(new Rect(area.x + 161, area.y + 3, 16, 18));
                if (!playing) DrawAnimationHover(new Rect(area.x + 181, area.y + 2, 60, 20));
            }
            if (GUI.Button(new Rect(area.x + 7, area.y + 3, 45, 18),
                new GUIContent(shortName, "Show " + LimbNames[limb] + " weight keys on the timeline"), StudioTheme.Styles.miniLabel))
                SelectRigChannel(weightIndex);
            using (new EditorGUI.DisabledScope(weightIndex < 0 || playing))
            {
                float weight = weightIndex < 0 ? 0 : pose.bones[weightIndex].position.y;
                EditorGUI.BeginChangeCheck();
                float next = GUI.HorizontalSlider(new Rect(area.x + 55, area.y + 4, 58, 18), weight, 0, 1);
                bool changed = EditorGUI.EndChangeCheck();
                GUI.Label(new Rect(area.x + 119, area.y + 3, 38, 18), Mathf.RoundToInt(next * 100) + "%", StudioTheme.Styles.miniLabel);
                if (weightIndex >= 0 && changed)
                {
                    SelectRigChannel(weightIndex);
                    pendingRigChannels.Add((take, weightIndex, 1, next, true));
                }
            }
            if (GUI.Button(new Rect(area.x + 161, area.y + 3, 16, 18),
                new GUIContent("ID", "Show " + LimbNames[limb] + " target ID keys on the timeline"), StudioTheme.Styles.miniLabel))
                SelectRigChannel(targetIndex);
            using (new EditorGUI.DisabledScope(targetIndex < 0 || playing))
            {
                int targetId = targetIndex < 0 ? 0 : StudioRigChannels.DecodeTargetId(pose.bones[targetIndex].position.y);
                EditorGUI.BeginChangeCheck();
                int next = Mathf.Max(0, EditorGUI.DelayedIntField(new Rect(area.x + 181, area.y + 2, 60, 20), targetId));
                bool changed = EditorGUI.EndChangeCheck();
                if (targetIndex >= 0 && changed)
                {
                    SelectRigChannel(targetIndex);
                    pendingRigChannels.Add((take, targetIndex, 1, next * .1f, true));
                }
            }
        }

        private void DrawRigChannel(string label, string boneName, int axis)
        {
            int index = Array.FindIndex(rig.Bones, b => StudioRigChannels.MatchesName(b.name, boneName));
            var area = GUILayoutUtility.GetRect(1, 22, GUILayout.ExpandWidth(true));
            DrawRigChannelKeyMenu(area, index, axis);
            using (new EditorGUI.DisabledScope(index < 0 || playing))
            {
                if (index < 0) { GUI.Label(area, label + ": missing " + boneName, StudioTheme.Styles.miniLabel); return; }
                DrawAnimationHover(new Rect(area.x + 55, area.y + 4, area.width - 100, 18));
                if (GUI.Button(new Rect(area.x + 4, area.y + 2, 45, 18),
                    new GUIContent(label, "Show " + label + " keys on the timeline"),
                    RigChannelSelected(index, axis) ? StudioTheme.Styles.overlayButton : StudioTheme.Styles.miniLabel))
                    SelectRigChannel(index, axis);
                float value = pose.bones[index].position[axis];
                EditorGUI.BeginChangeCheck();
                float next = GUI.HorizontalSlider(new Rect(area.x + 55, area.y + 4, area.width - 100, 18), value, 0, 1);
                bool changed = EditorGUI.EndChangeCheck();
                GUI.Label(new Rect(area.xMax - 40, area.y + 2, 36, 18), Mathf.RoundToInt(next * 100) + "%", StudioTheme.Styles.miniLabel);
                if (changed)
                {
                    SelectRigChannel(index, axis);
                    pendingRigChannels.Add((take, index, axis, next, true));
                }
            }
        }

        private void EditRigChannel(int index, int axis, float value, bool forceKey)
        {
            FinishSceneEdit();
            pose.bones[index].position[axis] = value;
            playing = false;
            if (autoKey || forceKey)
            {
                Undo.RecordObject(take, "Edit rig channel");
                var mask = new int[pose.bones.Length]; mask[index] = 1;
                if (!WriteAuthoringKey(mask, new int[pose.effectors.Length])) return;
                Save();
                viewport?.ShowStatusMessage("Keyed position");
            }
            else unkeyedPose = true;
            rig.Apply(pose); EvaluateConstraints(); viewport?.Repaint(); Repaint();
        }

        private List<RigSelectionItem> SelectedBlendLimbs(bool mergePoles = true)
        {
            var result = new List<RigSelectionItem>();
            Action<RigSelectionItem> add = item =>
            {
                if (!item.IK || item.Index < 0 || item.Index >= 4 ||
                    result.Any(x => x.Actor == item.Actor && x.Index == item.Index && (mergePoles || x.Pole == item.Pole))) return;
                WithBlendActor(item.Actor, () =>
                {
                    if (rig != null && pose != null && rig.HasIK && !IsBike && rig.Ends[item.Index]) result.Add(item);
                });
            };
            if (rigSelectionExplicit) foreach (var item in rigSelection) add(item);
            else
            {
                var actor = activeActor >= 0 && activeActor < actorPreviews.Count ? actorPreviews[activeActor] : null;
                add(new RigSelectionItem { Actor = actor != null ? actor.Take : take,
                    Index = actor != null ? actor.Control : control, IK = actor != null ? actor.IK : ikMode,
                    Pole = actor != null ? actor.Pole : poleMode });
            }
            return result;
        }

        private void WithBlendActor(AnimationTake target, Action action)
        {
            if (target == take) action();
            else
            {
                var actor = actorPreviews.Find(a => a.Take == target);
                if (actor != null) InActor(actor, action);
            }
        }

        private void ApplySelectedBlend(List<RigSelectionItem> selected, float weight, bool change, bool key)
        {
            FinishSceneEdit(); FinishActorEdits();
            bool matchPose = Event.current == null || !Event.current.alt;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Change selected IK/FK blends");
            try
            {
                foreach (var actor in selected.GroupBy(item => item.Actor))
                    WithBlendActor(actor.Key, () =>
                    {
                        if (change)
                            foreach (var item in actor)
                            {
                                int limb = item.Index;
                                if (matchPose) rig.MatchLimbBlend(pose, limb, weight);
                                else pose.effectors[limb].weight = Mathf.Clamp01(weight);
                            }
                        Commit("Change selected IK/FK blends", key);
                    });
            }
            finally { Undo.CollapseUndoOperations(undoGroup); }
            viewport?.Repaint(); Repaint();
        }
    }
}
