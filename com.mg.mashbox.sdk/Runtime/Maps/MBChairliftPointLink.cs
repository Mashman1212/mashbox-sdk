using UnityEngine;

namespace MashBoxSDK.Maps
{
    [ExecuteAlways]
    [AddComponentMenu("MashBox/Maps/Chairlift Point Link")]
    public sealed class MBChairliftPointLink : MonoBehaviour
    {
        public Transform linkedPoint;
        public bool followLinkedPoint = true;
        public bool maintainOffset = true;
        public Vector3 positionOffset;
        public bool followInEditMode = true;
        // Authoring links keep bay endpoints attached as towers/stations move.
        // Runtime paths are prepared once before physics/network registration.
        private void Update()
        {
            if (Application.isPlaying || !followInEditMode || !followLinkedPoint || linkedPoint == null || linkedPoint == transform) return;
            var target = linkedPoint.position + (maintainOffset ? positionOffset : Vector3.zero);
            if ((transform.position - target).sqrMagnitude > .000001f) transform.position = target;
        }
    }
}
