#if UNITY_6000_0_OR_NEWER
using UnityEngine;

namespace MashBoxSDK.Maps
{
    public enum MBChairliftStationKind { Bottom, Top }

    // Placement and route data only; the game definition supplies the physical station.
    [AddComponentMenu("MashBox/Maps/Chairlift Loading Bay")]
    [DisallowMultipleComponent]
    public sealed class MBChairliftStation : MonoBehaviour
    {
        public MBChairliftStationKind kind;
        public MBChairliftPath loadingBayPath;
        public Vector3 previewCenter;
        public Vector3 previewSize = new Vector3(12, 8, 16);

        [HideInInspector] public Bounds[] previewParts;
        [HideInInspector] public Vector3 placementOffset;
        public Vector3 PlacementPosition => transform.TransformPoint(placementOffset);

        private void OnDrawGizmos()
        {
            if (!MBGameplayGizmoVisibility.ChairliftsVisible) return;
            var matrix = Gizmos.matrix;
            var color = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.cyan;
            if (previewParts != null && previewParts.Length > 0)
            {
                foreach (var part in previewParts) Gizmos.DrawWireCube(part.center, part.size);
            }
            else Gizmos.DrawWireCube(previewCenter, previewSize);
            Gizmos.matrix = matrix;
            Gizmos.color = color;
        }
    }
}
#endif
