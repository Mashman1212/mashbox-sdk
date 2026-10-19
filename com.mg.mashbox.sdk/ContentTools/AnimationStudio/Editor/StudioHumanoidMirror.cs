using System;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed class StudioHumanoidMirror
    {
        private readonly AuthoringRig rig;
        private readonly int[] opposite;
        private readonly Quaternion[] parents, joints;
        private static Vector3 Reflect(Vector3 v) => new Vector3(-v.x, v.y, v.z);
        private static Quaternion Reflect(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);
        internal StudioHumanoidMirror(AuthoringRig rig)
        {
            if (!rig.Animator || !rig.Animator.isHuman) throw new InvalidOperationException("Mirroring requires a Humanoid character.");
            this.rig = rig;
            int count = rig.Bones.Length;
            opposite = Enumerable.Repeat(-1, count).ToArray(); parents = new Quaternion[count]; joints = new Quaternion[count];
            opposite[0] = 0;
            for (int h = 0; h < (int)HumanBodyBones.LastBone; h++)
            {
                var human = (HumanBodyBones)h; var bone = rig.Animator.GetBoneTransform(human);
                if (!bone) continue;
                string name = human.ToString();
                string partner = name.StartsWith("Left", StringComparison.Ordinal) ? "Right" + name.Substring(4)
                    : name.StartsWith("Right", StringComparison.Ordinal) ? "Left" + name.Substring(5) : name;
                var other = rig.Animator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), partner));
                opposite[Array.IndexOf(rig.Bones, bone)] = Array.IndexOf(rig.Bones, other);
            }
            // Bones are in hierarchy order. Also pair helper/end bones by their mapped parent.
            for (int i = 0; i < count; i++)
            {
                int p = Array.IndexOf(rig.Bones, rig.Bones[i].parent);
                parents[i] = p < 0 ? Quaternion.Inverse(rig.Rest.bones[0].rotation) : joints[p];
                joints[i] = parents[i] * rig.Rest.bones[i].rotation;
                if (opposite[i] >= 0 || p < 0 || opposite[p] < 0) continue;
                string name = SwapName(rig.Bones[i].name);
                int parent = opposite[p];
                opposite[i] = Array.FindIndex(rig.Bones, b => b.parent == rig.Bones[parent] && b.name == name);
            }
            for (int i = 0; i < count; i++)
                if (opposite[i] >= 0 && opposite[opposite[i]] != i) opposite[i] = -1;
        }
        private static string SwapName(string name)
        {
            foreach (var pair in new[] { new[] { "Left", "Right" }, new[] { "left", "right" }, new[] { "_L_", "_R_" } })
            {
                if (name.Contains(pair[0])) return name.Replace(pair[0], pair[1]);
                if (name.Contains(pair[1])) return name.Replace(pair[1], pair[0]);
            }
            foreach (var pair in new[] { new[] { "_L", "_R" }, new[] { ".L", ".R" } })
            {
                if (name.EndsWith(pair[0], StringComparison.Ordinal)) return name.Substring(0, name.Length - 2) + pair[1];
                if (name.EndsWith(pair[1], StringComparison.Ordinal)) return name.Substring(0, name.Length - 2) + pair[0];
            }
            if (name.StartsWith("L_", StringComparison.Ordinal)) return "R_" + name.Substring(2);
            if (name.StartsWith("R_", StringComparison.Ordinal)) return "L_" + name.Substring(2);
            return name;
        }
        internal void IncludeOpposite(int[] bones, int[] effectors)
        {
            var original = (int[])bones.Clone();
            for (int i = 0; i < bones.Length; i++) if (original[i] != 0 && opposite[i] >= 0) bones[opposite[i]] |= original[i];
            var originalIK = (int[])effectors.Clone();
            for (int i = 0; i < effectors.Length; i++) effectors[i ^ 1] |= originalIK[i];
        }
        internal PoseKey Apply(PoseKey source, bool additive)
        {
            var result = source.Copy(source.frame);
            Array.Clear(result.boneChannels, 0, result.boneChannels.Length);
            Array.Clear(result.effectorChannels, 0, result.effectorChannels.Length);
            for (int s = 0; s < source.bones.Length; s++)
            {
                int mask = source.boneChannels[s];
                int d = opposite[s];
                if (d < 0)
                {
                    if (mask != 0) throw new InvalidOperationException("Cannot find a mirror partner for " + rig.Paths[s] + ". Select mapped Humanoid joints.");
                    continue;
                }
                var value = source.bones[s]; var from = rig.Rest.bones[s]; var to = rig.Rest.bones[d];
                var delta = additive ? value.rotation : Quaternion.Inverse(from.rotation) * value.rotation;
                var mirrored = Quaternion.Inverse(joints[d]) * Reflect(joints[s] * delta * Quaternion.Inverse(joints[s])) * joints[d];
                result.bones[d] = new BonePose {
                    position = (additive ? Vector3.zero : to.position) + Quaternion.Inverse(parents[d]) * Reflect(parents[s] * (additive ? value.position : value.position - from.position)),
                    rotation = (additive ? mirrored : to.rotation * mirrored).normalized,
                    scale = additive ? value.scale : to.scale + value.scale - from.scale
                };
                result.boneChannels[d] = mask;
            }
            for (int s = 0; s < source.effectors.Length; s++)
            {
                int d = s ^ 1; var value = source.effectors[s]; var from = rig.Rest.effectors[s]; var to = rig.Rest.effectors[d];
                // IK targets and poles are already in character-root coordinates.
                var delta = additive ? from.rotation * value.rotation * Quaternion.Inverse(from.rotation) : value.rotation * Quaternion.Inverse(from.rotation);
                result.effectors[d] = new EffectorPose {
                    position = (additive ? Vector3.zero : to.position) + Reflect(additive ? value.position : value.position - from.position),
                    pole = (additive ? Vector3.zero : to.pole) + Reflect(additive ? value.pole : value.pole - from.pole),
                    rotation = (additive ? Quaternion.Inverse(to.rotation) * Reflect(delta) * to.rotation : Reflect(delta) * to.rotation).normalized,
                    weight = value.weight
                };
                result.effectorChannels[d] = source.effectorChannels[s];
            }
            return result;
        }
    }
}
