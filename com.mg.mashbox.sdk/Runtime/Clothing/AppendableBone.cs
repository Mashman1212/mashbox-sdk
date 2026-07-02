using UnityEngine;

namespace MashBoxSDK.Clothing
{
    public class AppendableBone : MonoBehaviour
    {
        public enum BoneType
        {
            Head,
            Spine,
            Chest,
            UpperArm_L,
            UpperArm_R,
            Forearm_L,
            Forearm_R,
            Hand_L,
            Hand_R,
            Thigh_L,
            Thigh_R,
            Calf_L,
            Calf_R,
            Foot_L,
            Foot_R
        }

        [Header("Bone Settings")]
        [SerializeField] private BoneType _targetBone;

        public BoneType GetTargetBone()
        {
            return _targetBone;
        }
    }
}