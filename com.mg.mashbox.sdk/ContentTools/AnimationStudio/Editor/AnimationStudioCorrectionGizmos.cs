using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        private int correctionGizmoUndo = -1;
        private AnimationTake correctionGizmoActor;
        private bool correctionGizmoWasDirty;

        private void SetQuickCorrectionMode(QuickCorrectionMode mode)
        {
            EndCorrectionGizmo(false);
            FinishSceneEdit();
            quickCorrectionMode = mode;
            if (mode != QuickCorrectionMode.None)
            {
                playing = false;
                if (mode != QuickCorrectionMode.Hips) rotate = true;
                FocusQuickCorrectionJoint();
                RefreshLayers();
            }
            viewport?.Repaint(); Repaint();
        }

        private void FocusQuickCorrectionJoint()
        {
            if (!rig?.Animator || quickCorrectionMode == QuickCorrectionMode.None) return;
            var joint = QuickCorrectionJoints().Select(rig.Animator.GetBoneTransform).FirstOrDefault(b => b);
            if (!joint) return;
            control = Array.IndexOf(rig.Bones, joint); ikMode = false; poleMode = false;
            rigSelection.Clear(); rigSelectionExplicit = false; selectingRig = false;
            viewport?.Repaint(); Repaint();
        }

        private bool HandleCorrectionGizmoKeys(Event evt)
        {
            if (quickCorrectionMode == QuickCorrectionMode.None || evt.type != EventType.KeyDown || EditorGUIUtility.editingTextField) return false;
            if (evt.keyCode == KeyCode.Escape && correctionGizmoUndo >= 0)
            {
                GUIUtility.hotControl = 0;
                EndCorrectionGizmo(true); evt.Use(); return true;
            }
            if (evt.alt || evt.control || evt.command) return false;
            // These keys belong to the correction tool, never the current-frame keying tools.
            if (evt.keyCode == KeyCode.W || evt.keyCode == KeyCode.E)
            {
                EndCorrectionGizmo(false);
                rotate = evt.keyCode == KeyCode.E || quickCorrectionMode != QuickCorrectionMode.Hips;
                evt.Use(); viewport?.Repaint(); return true;
            }
            if (evt.keyCode == KeyCode.S) { evt.Use(); return true; }
            return false;
        }

        private bool DrawCorrectionGizmos(SceneView view)
        {
            if (quickCorrectionMode == QuickCorrectionMode.None) return false;
            if (!rig.Animator || !take || take.keys.Count == 0) return true;
            if (playing) { EndCorrectionGizmo(false); return true; }
            var evt = Event.current;
            bool released = evt.rawType == EventType.MouseUp;
            bool rotateHandle = rotate || quickCorrectionMode != QuickCorrectionMode.Hips;
            foreach (var joint in QuickCorrectionJoints())
            {
                var bone = rig.Animator.GetBoneTransform(joint);
                if (!bone) continue;
                Handles.Label(bone.position, joint + " · Whole clip");
                EditorGUI.BeginChangeCheck();
                var position = bone.position;
                var rotation = bone.rotation;
                if (rotateHandle) rotation = StudioRotationHandle(rotation, position);
                else position = Handles.PositionHandle(position, HandleOrientation(rotation));
                if (EditorGUI.EndChangeCheck())
                    Guard(() => ApplyCorrectionGizmo(joint, position, rotation, rotateHandle));
            }
            if (released || (correctionGizmoUndo >= 0 && GUIUtility.hotControl == 0)) EndCorrectionGizmo(false);
            return true;
        }

        private void ApplyCorrectionGizmo(HumanBodyBones joint, Vector3 worldPosition, Quaternion worldRotation, bool rotation)
        {
            if (!rig?.Animator || quickCorrectionMode == QuickCorrectionMode.None || !QuickCorrectionJoints().Contains(joint)) return;
            var bone = rig.Animator.GetBoneTransform(joint);
            if (!bone || (!rotation && joint != HumanBodyBones.Hips)) return;
            if (correctionGizmoUndo >= 0 && correctionGizmoActor != take) EndCorrectionGizmo(false);
            if (correctionGizmoUndo < 0)
            {
                Undo.IncrementCurrentGroup(); correctionGizmoUndo = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Correct animation with gizmo");
                correctionGizmoActor = take; correctionGizmoWasDirty = clipSessionDirty;
            }
            var source = CorrectionFor(joint);
            var corrections = QuickCorrectionJoints().Where(j => rig.Animator.GetBoneTransform(j)).Select(CorrectionFor).ToArray();
            if (rotation)
            {
                Quaternion delta = Quaternion.Inverse(bone.rotation) * worldRotation;
                foreach (var correction in corrections)
                    correction.gizmoRotation = (correction.GizmoRotation * MirrorCorrectionDelta(delta, source, correction)).normalized;
            }
            else
            {
                var translation = bone.parent ? bone.parent.InverseTransformVector(worldPosition - bone.position) : worldPosition - bone.position;
                corrections[0].position += translation;
            }
            ApplyQuickCorrections(corrections);
        }

        private static Quaternion MirrorCorrectionDelta(Quaternion delta, StudioBoneCorrection source, StudioBoneCorrection target)
        {
            if (source.path == target.path) return delta;
            delta.ToAngleAxis(out float angle, out var axis);
            if (Mathf.Abs(angle) < .00001f || axis.sqrMagnitude < .000001f) return Quaternion.identity;
            Vector3 mirrored = target.axisX.normalized * Vector3.Dot(axis, source.axisX.normalized)
                + target.axisY.normalized * Vector3.Dot(axis, source.axisY.normalized)
                + target.axisZ.normalized * Vector3.Dot(axis, source.axisZ.normalized);
            return Quaternion.AngleAxis(angle, mirrored);
        }

        private void EndCorrectionGizmo(bool cancel)
        {
            if (correctionGizmoUndo < 0) return;
            int group = correctionGizmoUndo;
            var actor = correctionGizmoActor;
            correctionGizmoUndo = -1; correctionGizmoActor = null; globalRotationControl = 0;
            Undo.FlushUndoRecordObjects();
            if (cancel)
            {
                Undo.RevertAllDownToGroup(group);
                if (actor && rig != null) InSelectedActor(actor, RefreshLayers);
                clipSessionDirty = correctionGizmoWasDirty; UpdateClipSaveState();
            }
            else Undo.CollapseUndoOperations(group);
            Undo.IncrementCurrentGroup();
        }
    }
}
