using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class AuthoringRig
    {
        internal Transform BodyControl(int kind)
        {
            if (!Animator || !Animator.avatar || !Animator.avatar.isHuman) return null;
            if (kind == 0) return Animator.GetBoneTransform(HumanBodyBones.Hips);
            if (kind == 1) return Animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? Animator.GetBoneTransform(HumanBodyBones.Chest);
            return kind == 2 ? Animator.GetBoneTransform(HumanBodyBones.Head) : null;
        }
        internal List<Transform> BodyControlChain(int kind)
        {
            var chain = new List<Transform>();
            var target = BodyControl(kind); var hips = BodyControl(0);
            if (!target) return chain;
            chain.Add(target);
            if (kind != 0)
                for (var bone = target.parent; bone && bone != hips && bone != Root.transform; bone = bone.parent) chain.Add(bone);
            return chain;
        }
        internal void PoseBodyControl(PoseKey pose, int kind, Vector3 position, Quaternion rotation, bool rotate)
        {
            var chain = BodyControlChain(kind);
            if (chain.Count == 0) return;
            var target = chain[0];
            if (rotate) target.rotation = rotation;
            else if (kind == 0) target.position = position;
            else
            {
                // Rotate only torso ancestors: no bone stretching or pelvis remapping.
                Quaternion orientation = target.rotation;
                for (int iteration = 0; iteration < 48; iteration++)
                {
                    if ((target.position - position).sqrMagnitude < 1e-8f) break;
                    for (int i = 1; i < chain.Count; i++)
                    {
                        var joint = chain[i];
                        Vector3 from = target.position - joint.position, to = position - joint.position;
                        if (from.sqrMagnitude < 1e-10f || to.sqrMagnitude < 1e-10f) continue;
                        joint.rotation = Quaternion.FromToRotation(from, to) * joint.rotation;
                    }
                }
                target.rotation = orientation;
            }
            foreach (var bone in chain)
            {
                int index = Array.IndexOf(Bones, bone);
                if (index >= 0) pose.bones[index] = BonePose.Read(bone);
            }
            Apply(pose); // Re-solve limbs to retain their targets and world pins.
        }
    }
    public sealed partial class AnimationStudioWindow
    {
        private static readonly string[] BodyIKNames = { "Hips IK", "Chest IK", "Head IK" };
        private int BodyIKKind(int boneIndex)
        {
            if (!showIKControls || IsBike || rig == null || boneIndex < 0 || boneIndex >= rig.Bones.Length) return -1;
            for (int kind = 0; kind < 3; kind++) if (rig.Bones[boneIndex] == rig.BodyControl(kind)) return kind;
            return -1;
        }
        private void DrawBodyIKPickers(SceneView view, bool canPick)
        {
            if (!showIKControls || IsBike) return;
            for (int kind = 0; kind < 3; kind++)
            {
                var bone = rig.BodyControl(kind); if (!bone) continue;
                int index = Array.IndexOf(rig.Bones, bone);
                RegisterRigPoint(index, false, false, bone.position);
                bool selected = RigControlSelected(index);
                Handles.color = selected ? Color.white : new Color(.3f, 1f, .7f);
                // Body-sized hoops, rather than camera-facing icons. Derive the
                // anatomical up axis from rest so imported bone axis conventions
                // do not turn the hoops sideways. Follow each body's posed rotation.
                Quaternion restRotation = Quaternion.identity;
                for (var parent = bone; parent && parent != rig.Root.transform; parent = parent.parent)
                {
                    int restIndex = Array.IndexOf(rig.Bones, parent);
                    restRotation = (restIndex >= 0 ? rig.Rest.bones[restIndex].rotation : parent.localRotation) * restRotation;
                }
                Vector3 normal = bone.rotation * (Quaternion.Inverse(restRotation) * Vector3.up);
                Quaternion facing = Quaternion.FromToRotation(Vector3.forward, normal);
                Vector3 scale = rig.Root.transform.lossyScale;
                float actorScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                float radius = (kind == 0 ? .13f : kind == 1 ? .16f : .09f)
                    * rig.Animator.humanScale * actorScale * controlSize;
                bool clicked = Handles.Button(bone.position, facing, radius, radius, Handles.CircleHandleCap);
                if (clicked && canPick) SelectBone(bone);
                if (selected && rigSelection.Count <= 1) Handles.Label(bone.position, BodyIKNames[kind]);
            }
        }
        private bool DrawBodyIKHandle(bool released)
        {
            if (ikMode) return false;
            int kind = BodyIKKind(control); if (kind < 0) return false;
            var bone = rig.BodyControl(kind);
            EditorGUI.BeginChangeCheck();
            Vector3 position = bone.position; Quaternion rotation = bone.rotation;
            if (rotate) rotation = StudioRotationHandle(rotation, position);
            else position = Handles.PositionHandle(position, HandleOrientation(rotation));
            if (EditorGUI.EndChangeCheck())
            {
                rig.PoseBodyControl(pose, kind, position, rotation, rotate);
                PreviewSceneEdit("Pose " + BodyIKNames[kind]);
            }
            if (released) FinishSceneEdit();
            return true;
        }
        private void AddBodyIKChannelMask(int[] bones, int index, int mask)
        {
            int kind = BodyIKKind(index);
            if (kind <= 0 || mask != 1) return;
            // Positioning a chest/head target keys the solved chain rotations.
            foreach (var bone in rig.BodyControlChain(kind))
            {
                int affected = Array.IndexOf(rig.Bones, bone);
                if (affected >= 0) bones[affected] |= 2;
            }
        }
    }
}
