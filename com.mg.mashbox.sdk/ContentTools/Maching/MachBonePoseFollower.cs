using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maching
{
    [ExecuteAlways]
    public class MachBonePoseFollower : MonoBehaviour
    {
        [SerializeField] private Transform _targetSkeletonRoot;
        [SerializeField] private bool _copyScale = true;

        private readonly List<BoneBinding> _bindings = new();

        private struct BoneBinding
        {
            public Transform Source;
            public Transform Target;
        }

        public void Configure(Transform targetSkeletonRoot)
        {
            _targetSkeletonRoot = targetSkeletonRoot;
            RebuildBindings();
            ApplyPose();
        }

        private void OnEnable()
        {
            RebuildBindings();
            ApplyPose();
        }

        private void LateUpdate()
        {
            ApplyPose();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            RebuildBindings();
            ApplyPose();
        }
#endif

        private void RebuildBindings()
        {
            _bindings.Clear();

            if (_targetSkeletonRoot == null)
                return;

            var targetBonesByName = new Dictionary<string, Transform>();
            foreach (Transform bone in _targetSkeletonRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!targetBonesByName.ContainsKey(bone.name))
                    targetBonesByName.Add(bone.name, bone);
            }

            var sourceBones = new HashSet<Transform>();
            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer == null)
                    continue;

                AddSourceBone(sourceBones, renderer.rootBone);

                foreach (var bone in renderer.bones)
                    AddSourceBone(sourceBones, bone);
            }

            foreach (var sourceBone in sourceBones)
            {
                if (!targetBonesByName.TryGetValue(sourceBone.name, out var targetBone))
                    continue;

                _bindings.Add(new BoneBinding
                {
                    Source = sourceBone,
                    Target = targetBone,
                });
            }
        }

        private void ApplyPose()
        {
            for (int i = 0; i < _bindings.Count; i++)
            {
                var binding = _bindings[i];
                if (binding.Source == null || binding.Target == null)
                    continue;

                binding.Source.localPosition = binding.Target.localPosition;
                binding.Source.localRotation = binding.Target.localRotation;

                if (_copyScale)
                    binding.Source.localScale = binding.Target.localScale;
            }
        }

        private void AddSourceBone(HashSet<Transform> sourceBones, Transform bone)
        {
            if (bone == null || !bone.IsChildOf(transform))
                return;

            sourceBones.Add(bone);
        }
    }
}
