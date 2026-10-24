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
            limbBlend.style.bottom = 148;
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
            float next = GUI.HorizontalSlider(new Rect(area.x + 105, area.y + 6, 98, 18), first, 0, 1);
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = previousMixed;
            GUI.Label(new Rect(area.x + 210, area.y + 3, 38, 20),
                new GUIContent(mixed ? "Mixed" : Mathf.RoundToInt(first * 100) + "%",
                    "0% = FK, 100% = IK. Preserves the visible pose for all selected limbs. Hold Alt while dragging to blend between the stored poses instead."), StudioTheme.Styles.miniLabel);
            if (changed) Guard(() => ApplySelectedBlend(selected, next, true, false));
            DrawIKPinButtons(area, SelectedBlendLimbs(false));
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
