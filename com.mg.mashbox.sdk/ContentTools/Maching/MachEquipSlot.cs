
using UnityEngine;

namespace MashBoxSDK.Maching
{
    public class MachEquipSlot : MonoBehaviour, IMachEquipSlot
    {
        private enum MachEquipBindingMode
        {
            Auto,
            PreserveOriginalBones,
            ReSkinToTargetBones,
        }

        public string SlotID => slotTag;
        [SerializeField] private Transform _skeletonRoot;
        [SerializeField] private string slotTag = "Pants";
        [SerializeField] private GameObject _equippedItem;
        [SerializeField] private MachEquipBindingMode _bindingMode = MachEquipBindingMode.Auto;

        //IEquipSlot

        public void Equip(GameObject go)
        {
            if (go == null)
                return;
            
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                SafeDestroy(transform.GetChild(i).gameObject);
            }
            
            if (_equippedItem != null)
            {
                SafeDestroy(_equippedItem);
                _equippedItem = null;
            }
            
            _equippedItem = InstantiateSafe(go, transform);

            _equippedItem.name = go.name; // removes (Clone)
            _equippedItem.transform.localPosition = Vector3.zero;
            _equippedItem.transform.localRotation = Quaternion.identity;

            ApplyBinding(_equippedItem);

            _equippedItem.SetActive(true);
        }
        public GameObject GetEquippedItem()
        {
            return _equippedItem;
        }
        
        private GameObject InstantiateSafe(GameObject prefab, Transform parent)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            }
#endif

            return Instantiate(prefab, parent);
        }
        private void SafeDestroy(GameObject go)
        {
            if (go == null)
                return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(go);
                return;
            }
#endif

            Destroy(go);
        }
        
        private void ApplySkinning(GameObject go)
        {
            if (_skeletonRoot == null)
            {
                Debug.LogWarning("MachEquipSlot: No skeleton root assigned");
                return;
            }

            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null)
                return;

            // Find matching root bone
            Transform targetRoot = FindDeepChild(_skeletonRoot, smr.rootBone.name);
            if (targetRoot == null)
            {
                Debug.LogWarning($"Root bone not found: {smr.rootBone.name}");
                return;
            }

            // Remap bones
            Transform[] newBones = new Transform[smr.bones.Length];
            for (int i = 0; i < smr.bones.Length; i++)
            {
                var bone = smr.bones[i];
                if (bone == null) continue;

                var match = FindDeepChild(_skeletonRoot, bone.name);
                newBones[i] = match != null ? match : bone;
            }

            smr.bones = newBones;
            smr.rootBone = targetRoot;
            smr.updateWhenOffscreen = true;
        }

        private void ApplyBinding(GameObject go)
        {
            if (go == null)
                return;

            if (ShouldPreserveOriginalBones(go))
            {
                ApplyPoseFollower(go);
                return;
            }

            RemovePoseFollower(go);
            ApplySkinning(go);
        }

        private bool ShouldPreserveOriginalBones(GameObject go)
        {
            if (_bindingMode == MachEquipBindingMode.PreserveOriginalBones)
                return true;

            if (_bindingMode == MachEquipBindingMode.ReSkinToTargetBones)
                return false;

            return CanPreserveOriginalBones(go);
        }

        private bool CanPreserveOriginalBones(GameObject go)
        {
            if (_skeletonRoot == null)
                return false;

            var renderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0)
                return false;

            int checkedBoneCount = 0;
            int matchedBoneCount = 0;

            foreach (var renderer in renderers)
            {
                if (renderer == null)
                    continue;

                if (renderer.rootBone != null)
                {
                    checkedBoneCount++;
                    if (FindDeepChild(_skeletonRoot, renderer.rootBone.name) != null)
                        matchedBoneCount++;
                }

                foreach (var bone in renderer.bones)
                {
                    if (bone == null || !bone.IsChildOf(go.transform))
                        continue;

                    checkedBoneCount++;
                    if (FindDeepChild(_skeletonRoot, bone.name) != null)
                        matchedBoneCount++;
                }
            }

            return checkedBoneCount > 0 && matchedBoneCount == checkedBoneCount;
        }

        private void ApplyPoseFollower(GameObject go)
        {
            if (_skeletonRoot == null)
            {
                Debug.LogWarning("MachEquipSlot: No skeleton root assigned");
                return;
            }

            var follower = go.GetComponent<MachBonePoseFollower>();
            if (follower == null)
                follower = go.AddComponent<MachBonePoseFollower>();

            follower.Configure(_skeletonRoot);

            foreach (var renderer in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                renderer.updateWhenOffscreen = true;
        }

        private void RemovePoseFollower(GameObject go)
        {
            if (go == null)
                return;

            var follower = go.GetComponent<MachBonePoseFollower>();
            if (follower == null)
                return;

            SafeDestroy(follower);
        }
        
        private Transform FindDeepChild(Transform parent, string name)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                    return child;
            }
            return null;
        }

        private void SafeDestroy(Component component)
        {
            if (component == null)
                return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(component);
                return;
            }
#endif

            Destroy(component);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_equippedItem != null)
                ApplyBinding(_equippedItem);
        }
#endif
    }
}
