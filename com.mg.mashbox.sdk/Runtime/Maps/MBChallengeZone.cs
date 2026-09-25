using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("MashBox/Maps/Challenge Zone")]
    [RequireComponent(typeof(BoxCollider)), DisallowMultipleComponent]
    public sealed class MBChallengeZone : MonoBehaviour
    {
        public bool Contains(Vector3 worldPosition)
        {
            var box = GetComponent<BoxCollider>();
            if (!box.enabled) return false;
            Vector3 point = transform.InverseTransformPoint(worldPosition) - box.center;
            Vector3 half = box.size * 0.5f;
            return Mathf.Abs(point.x) <= half.x && Mathf.Abs(point.y) <= half.y && Mathf.Abs(point.z) <= half.z;
        }

        private void Reset() { GetComponent<BoxCollider>().isTrigger = true; }

        private void OnDrawGizmos()
        {
            if (!MBGameplayGizmoVisibility.Visible) return;
            var box = GetComponent<BoxCollider>();
            var owner = GetComponentInParent<MBTrickChallenge>();
            int index = owner != null ? owner.Zones.IndexOf(this) : -1;
            Color color = owner != null && owner.IsLine ? new Color(.15f, .75f, 1f) : new Color(1f, .65f, .15f);
            Matrix4x4 previous = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(color.r, color.g, color.b, .1f);
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = color;
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = previous;
            Gizmos.color = previousColor;
#if UNITY_EDITOR
            Handles.Label(transform.TransformPoint(box.center + Vector3.up * (box.size.y * .5f)),
                owner != null && owner.IsLine ? $"Step {index + 1}: {name}" : name);
            if (owner != null && owner.IsLine && index > 0 && owner.Zones[index - 1] != null)
            {
                Color previousHandleColor = Handles.color;
                Handles.color = color;
                Handles.DrawDottedLine(owner.Zones[index - 1].transform.position, transform.position, 5f);
                Handles.color = previousHandleColor;
            }
#endif
        }
    }
}
