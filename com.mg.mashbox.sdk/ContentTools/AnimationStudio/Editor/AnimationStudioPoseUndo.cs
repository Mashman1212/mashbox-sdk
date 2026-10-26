using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // Transient pose history must not write animation keys when Auto Key is off.
    internal sealed class StudioPoseUndoState : ScriptableObject
    {
        [Serializable] internal sealed class Entry
        {
            public AnimationTake actor;
            public PoseKey pose;
        }
        public int revision, frame;
        public List<Entry> poses = new List<Entry>();
    }
    public sealed partial class AnimationStudioWindow
    {
        [SerializeField] private StudioPoseUndoState poseUndoState;
        [SerializeField] private int appliedPoseUndoRevision;
        private List<StudioPoseUndoState.Entry> poseUndoStart;
        private int poseUndoFrame;

        private List<StudioPoseUndoState.Entry> CapturePoseUndoEntries()
        {
            var entries = new List<StudioPoseUndoState.Entry>();
            // FinishSceneEdit may run inside the selected secondary actor scope.
            if (inActorScope) return entries;
            if (take && pose != null) entries.Add(new StudioPoseUndoState.Entry { actor = take, pose = pose.Copy(frame) });
            foreach (var actor in actorPreviews)
                if (actor.Pose != null) entries.Add(new StudioPoseUndoState.Entry { actor = actor.Take, pose = actor.Pose.Copy(actor.Pose.frame) });
            return entries;
        }
        private void CaptureUnkeyedPoseUndo()
        {
            if (!take || pose == null || autoKey) { poseUndoStart = null; return; }
            poseUndoStart = CapturePoseUndoEntries(); poseUndoFrame = frame;
        }
        private void RecordUnkeyedPoseUndo()
        {
            if (poseUndoStart == null || inActorScope) return;
            var after = CapturePoseUndoEntries();
            bool changed = poseUndoStart.Count != after.Count;
            for (int i = 0; !changed && i < after.Count; i++)
                changed = after[i].actor != poseUndoStart[i].actor || JsonUtility.ToJson(after[i].pose) != JsonUtility.ToJson(poseUndoStart[i].pose);
            if (!changed) { poseUndoStart = null; return; }
            if (!poseUndoState) poseUndoState = CreateInstance<StudioPoseUndoState>();
            poseUndoState.hideFlags = HideFlags.HideAndDontSave;
            poseUndoState.poses = poseUndoStart; poseUndoState.frame = poseUndoFrame;
            poseUndoStart = null;
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(poseUndoState, "Move unkeyed pose");
            poseUndoState.poses = after; poseUndoState.frame = frame;
            poseUndoState.revision++;
            appliedPoseUndoRevision = poseUndoState.revision;
            Undo.IncrementCurrentGroup();
        }
        private bool RestoreUnkeyedPoseUndo()
        {
            if (!poseUndoState || poseUndoState.revision == appliedPoseUndoRevision) return false;
            appliedPoseUndoRevision = poseUndoState.revision;
            if (!take || rig == null || !poseUndoState.poses.Exists(e => e.actor == take)) return false;
            playing = false; sceneDraft = false; poseUndoStart = null;
            frame = Mathf.Clamp(poseUndoState.frame, 0, take.lastFrame);
            EnsureActors();
            foreach (var entry in poseUndoState.poses)
            {
                if (entry.actor == take)
                { pose = entry.pose.Copy(frame); rig.Apply(pose); unkeyedPose = true; }
                else
                {
                    var actor = actorPreviews.Find(a => a.Take == entry.actor);
                    if (actor == null) continue;
                    actor.Pose = entry.pose.Copy(entry.pose.frame); actor.Rig.Apply(actor.Pose);
                    actor.Draft = false; actor.Unkeyed = true;
                }
            }
            EvaluateConstraints(); viewport?.Repaint(); Repaint();
            return true;
        }
    }
}
