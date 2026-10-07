using UnityEngine;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("MashBox/Maps/Chairlift Transfer")]
    public sealed class MBChairliftTransfer : MonoBehaviour
    {
        public MBChairliftPath targetPath;
        public bool sphere;
        public Vector3 center;
        public Vector3 size = Vector3.one;
        [Min(0)] public float radius = .5f;
        public bool useDriveManagerSpeed = true;
        public float speedOverride = 2;
        public bool loopOnTargetPath = true;
        public bool alignToTargetPath = true;
        public bool useContactRigidbodyAsTarget = true;
        public bool ignoreFollowersAlreadyOnTargetPath = true;
        [Min(0)] public float transferBlendDuration = .5f;
        public int speedHandoff;
        [Min(0)] public float transferCooldown = .75f;
        public bool transferOnTriggerStay = true;
        private void OnDrawGizmos()
        {
            if (!MBGameplayGizmoVisibility.ChairliftsVisible) return;
            var old = Gizmos.matrix;
            var color = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.green;
            if (sphere) Gizmos.DrawWireSphere(center, radius);
            else Gizmos.DrawWireCube(center, size);
            Gizmos.matrix = old;
            // The target is a path root, not a cable attachment; drawing to it misrepresents the route.
            Gizmos.color = color;
        }
    }
}
