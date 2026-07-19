using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MashBoxSDK.Maps
{
    public class MBExpertLineSignGizmo : MonoBehaviour
    {
        private const float GroundProbeStartHeight = 2.5f;
        private const float GroundProbeDistance = 20f;

        private static readonly Color PostColor = new Color(0.04f, 0.04f, 0.04f, 0.92f);
        private static readonly Color SignFillColor = new Color(0.0f, 0.0f, 0.0f, 0.22f);
        private static readonly Color SignWireColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
        private static readonly Color FaceFillColor = new Color(0.92f, 0.92f, 0.86f, 0.78f);
        private static readonly Color FaceWireColor = new Color(0.04f, 0.04f, 0.04f, 1.0f);
        private static readonly Color GuideColor = new Color(0.0f, 0.0f, 0.0f, 0.55f);

        [SerializeField] private Vector3 signSize = new Vector3(0.82f, 0.54f, 0.12f);
        [SerializeField] private float postHeight = 0.9f;
        [SerializeField] private float postWidth = 0.12f;
        [SerializeField] private bool snapPreviewToGround = true;

        public Vector3 SignSize => SanitizeVector(signSize);
        public float PostHeight => Mathf.Max(0.05f, postHeight);
        public float PostWidth => Mathf.Max(0.01f, postWidth);

        private void OnValidate()
        {
            signSize = SanitizeVector(signSize);
            postHeight = Mathf.Max(0.05f, postHeight);
            postWidth = Mathf.Max(0.01f, postWidth);
        }

        private void OnDrawGizmos()
        {
            if (!MBGameplayGizmoVisibility.Visible)
                return;

            Vector3 basePosition = snapPreviewToGround ? GetGroundedBasePosition(transform.position) : transform.position;
            Quaternion rotation = transform.rotation;
            Vector3 scale = SanitizeVector(transform.lossyScale);
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;

            if (snapPreviewToGround)
            {
                Gizmos.color = GuideColor;
                Gizmos.DrawLine(transform.position, basePosition);
            }

            Gizmos.matrix = Matrix4x4.TRS(basePosition, rotation, scale);

            Vector3 postSize = new Vector3(PostWidth, PostHeight, PostWidth);
            Vector3 postCenter = Vector3.up * (PostHeight * 0.5f);
            Vector3 signCenter = Vector3.up * (PostHeight + (SignSize.y * 0.5f));

            Gizmos.color = PostColor;
            Gizmos.DrawCube(postCenter, postSize);

            Gizmos.color = SignFillColor;
            Gizmos.DrawCube(signCenter, SignSize);
            Gizmos.color = SignWireColor;
            Gizmos.DrawWireCube(signCenter, SignSize);

            Vector3 faceOffset = Vector3.back * ((SignSize.z * 0.5f) + 0.004f);
            Vector3 faceSize = new Vector3(SignSize.x * 0.72f, SignSize.y * 0.72f, 0.01f);
            Gizmos.color = FaceFillColor;
            Gizmos.DrawCube(signCenter + faceOffset, faceSize);
            Gizmos.color = FaceWireColor;
            Gizmos.DrawWireCube(signCenter + faceOffset, faceSize);

            Gizmos.matrix = Matrix4x4.TRS(basePosition + (rotation * (signCenter + faceOffset)), rotation * Quaternion.Euler(0f, 0f, 45f), scale);
            Vector3 diamondSize = new Vector3(SignSize.y * 0.32f, SignSize.y * 0.32f, 0.012f);
            Gizmos.color = SignFillColor;
            Gizmos.DrawCube(Vector3.zero, diamondSize);
            Gizmos.color = SignWireColor;
            Gizmos.DrawWireCube(Vector3.zero, diamondSize);

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;

#if UNITY_EDITOR
            if (Selection.activeTransform == transform)
            {
                Handles.color = FaceWireColor;
                Handles.Label(basePosition + Vector3.up * (PostHeight + SignSize.y + 0.18f), "Expert Line Sign");
            }
#endif
        }

        public static Vector3 GetGroundedBasePosition(Vector3 worldPosition)
        {
            Vector3 rayOrigin = worldPosition + (Vector3.up * GroundProbeStartHeight);
            float rayDistance = GroundProbeStartHeight + GroundProbeDistance;

            return Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, rayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? hit.point
                : worldPosition;
        }

        private static Vector3 SanitizeVector(Vector3 value)
        {
            return new Vector3(
                Mathf.Max(Mathf.Abs(value.x), 0.01f),
                Mathf.Max(Mathf.Abs(value.y), 0.01f),
                Mathf.Max(Mathf.Abs(value.z), 0.01f));
        }
    }
}
