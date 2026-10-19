using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        private void PasteCachedKeys(AnimationTake actor, List<PoseKey> keys, int destination, bool mirror)
        {
            var clipboard = StudioKeyClipboard.Current;
            if (clipboard == null) return;
            FlushEdits(); playing = false;
            var document = take; int sharedFps = take.frameRate;
            int end = destination;
            InSelectedActor(actor, () =>
            {
                var samples = clipboard.Remap(actor, keys, rig.Rest, destination / (float)sharedFps);
                if (mirror)
                {
                    var reflection = new StudioHumanoidMirror(rig);
                    samples = samples.Select(k => reflection.Apply(k, clipboard.additive)).ToList();
                }
                // Validate and prepare every sample before recording or changing the destination.
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(mirror ? "Paste mirrored keys" : "Paste timeline keys");
                Undo.RegisterCompleteObjectUndo(actor, "Paste timeline keys");
                if (actor != document) Undo.RegisterCompleteObjectUndo(document, "Extend timeline");
                StudioKeyClipboard.Merge(actor, keys, samples, rig.Rest);
                actor.lastFrame = Mathf.Max(actor.lastFrame, samples.Max(k => k.frame));
                end = Mathf.RoundToInt(samples.Max(k => k.frame) / (float)actor.frameRate * sharedFps);
                document.lastFrame = Mathf.Max(document.lastFrame, Mathf.CeilToInt(actor.lastFrame / (float)actor.frameRate * sharedFps));
                Save(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
            });
            timelineRangeStart = destination; timelineRangeEnd = end;
            Seek(frame); Repaint(); viewport?.Repaint();
        }

        private void MirrorTimelineKeys(AnimationTake actor, List<PoseKey> keys, bool wholeTrack)
        {
            if (!wholeTrack && timelineRangeStart < 0) return;
            FlushEdits(); playing = false;
            var affected = keys.Where(k => wholeTrack || TimelineInRange(actor, k)).ToArray();
            InSelectedActor(actor, () =>
            {
                var reflection = new StudioHumanoidMirror(rig);
                SelectedControlMasks(out var bones, out var effectors);
                if (wholeTrack) { bones = Enumerable.Repeat(7, rig.Bones.Length).ToArray(); effectors = Enumerable.Repeat(15, 4).ToArray(); }
                // Mirroring is a swap: include counterparts so selecting either side is reversible.
                reflection.IncludeOpposite(bones, effectors);
                var copied = StudioKeyClipboard.Capture(actor, keys, affected, bones, effectors, 0, true);
                if (copied.keys.Length == 0) return;
                var mirrored = copied.keys.Select(k => reflection.Apply(k, copied.additive)).ToArray();
                var reflectedFrames = mirrored.ToDictionary(k => k.frame);
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Mirror Humanoid keys");
                Undo.RegisterCompleteObjectUndo(actor, "Mirror Humanoid keys");
                // Remove only the channels being swapped; preserve unrelated channels at each key.
                foreach (var key in affected)
                {
                    StudioKeyClipboard.ExplicitMasks(key, keys == actor.keys);
                    var reflected = reflectedFrames[key.frame];
                    // Sparse base channels with no keys fall back to the first stored pose.
                    // Swap those fallback values too, without creating extra authored channels.
                    for (int i = 0; i < bones.Length; i++)
                    {
                        key.boneChannels[i] &= ~bones[i];
                        if ((bones[i] & 1) != 0) key.bones[i].position = reflected.bones[i].position;
                        if ((bones[i] & 2) != 0) key.bones[i].rotation = reflected.bones[i].rotation;
                        if ((bones[i] & 4) != 0) key.bones[i].scale = reflected.bones[i].scale;
                    }
                    for (int i = 0; i < effectors.Length; i++)
                    {
                        key.effectorChannels[i] &= ~effectors[i];
                        if ((effectors[i] & 1) != 0) key.effectors[i].position = reflected.effectors[i].position;
                        if ((effectors[i] & 2) != 0) key.effectors[i].rotation = reflected.effectors[i].rotation;
                        if ((effectors[i] & 4) != 0) key.effectors[i].pole = reflected.effectors[i].pole;
                        if ((effectors[i] & 8) != 0) key.effectors[i].weight = reflected.effectors[i].weight;
                    }
                }
                StudioKeyClipboard.Merge(actor, keys, mirrored, rig.Rest);
                Save(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
            });
            Seek(frame); Repaint(); viewport?.Repaint();
        }

        private void AddClipboardMenuItems(AnimationTake actor, List<PoseKey> keys, int atFrame, Action<string, bool, Action> add)
        {
            var clipboard = StudioKeyClipboard.Current;
            string reason = clipboard == null ? "Copy selected keys first" : clipboard.Incompatibility(actor, keys);
            bool humanoid = false;
            InSelectedActor(actor, () => humanoid = rig != null && rig.Animator && rig.Animator.isHuman);
            add(reason == null ? "Paste keys at frame " + atFrame : "Paste keys (" + reason + ")", reason == null,
                () => PasteTimelineKeys(actor, keys, atFrame));
            add("Paste mirrored keys (Humanoid)", reason == null && humanoid,
                () => PasteCachedKeys(actor, keys, atFrame, true));
            bool selected = timelineRangeStart >= 0 && SelectedTimelineKeys(actor, keys).Any(k => TimelineInRange(actor, k));
            add("Mirror selected keys (Humanoid)", humanoid && selected, () => MirrorTimelineKeys(actor, keys, false));
            add("Mirror entire track (Humanoid)", humanoid && keys.Count > 0, () => MirrorTimelineKeys(actor, keys, true));
        }

        private void DrawAnimationEditMenu()
        {
            var rect = new Rect(48, 0, 48, 21);
            if (!EditorGUI.DropdownButton(rect, new GUIContent("Edit"), FocusType.Passive, StudioTheme.Styles.menuDropdown)) return;
            var menu = new StudioMenu();
            if (take && rig != null)
            {
                var actor = OutfitActor; var keys = TimelineLayerKeys(actor);
                if (SelectedTimelineKeys(actor, keys).Any(k => TimelineInRange(actor, k)))
                    menu.AddItem(new GUIContent("Copy selected keys"), false, () => Guard(() => CopyTimelineKeys(actor, keys)));
                else menu.AddDisabledItem(new GUIContent("Copy selected keys"));
                AddClipboardMenuItems(actor, keys, frame, (label, enabled, action) =>
                {
                    if (enabled) menu.AddItem(new GUIContent(label), false, () => Guard(action));
                    else menu.AddDisabledItem(new GUIContent(label));
                });
            }
            else menu.AddDisabledItem(new GUIContent("Open an animation to edit keys"));
            menu.DropDown(rect);
        }
    }
}
