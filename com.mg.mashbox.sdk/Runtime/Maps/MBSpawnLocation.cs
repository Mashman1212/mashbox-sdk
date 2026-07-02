using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("MashBox/Maps/Spawn Location")]
    public class MBSpawnLocation : MonoBehaviour
    {
        private const float SnapStartHeight = 1f;
        private const float SnapDistance = 10f;
        private Vector3 lastSnapCheckPosition;
        private Quaternion lastSnapCheckRotation;
        private bool hasSnapCheckState;

        public enum PlayerID
        {
            Any,
            zero,
            one,
            two,
            three,
            four,
            five,
            six,
            seven,
            eight
        }
        
        public enum TeamID
        {
            Any = -1,
            RedTeam = 0,
            BlueTeam = 1
        }

        public PlayerID Player;
        public TeamID Team;

        public bool IsGrounded()
        {
            return TryGetGroundHit(out _);
        }

        void OnDrawGizmos()//
        {
            SnapToGround();

            if (Team == TeamID.Any)
            {
                Gizmos.color = Color.black;
            }
            else if (Team == TeamID.RedTeam)
            {
                Gizmos.color = Color.red;
            }
            else if (Team == TeamID.BlueTeam)
            {
                Gizmos.color = Color.blue;
            }
            
            var wireColor = Gizmos.color;
            var fillColor = new Color(wireColor.r, wireColor.g, wireColor.b, 0.18f);

            Gizmos.color = fillColor;
            Gizmos.DrawSphere(transform.position, 1.85f);

            Gizmos.color = wireColor;
            Gizmos.DrawWireSphere(transform.position, 2.0f);
        }

        private void SnapToGround()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
                return;

            if (Selection.activeTransform == transform && GUIUtility.hotControl != 0)
                return;

            if (hasSnapCheckState &&
                Vector3.SqrMagnitude(transform.position - lastSnapCheckPosition) < 0.0001f &&
                Quaternion.Angle(transform.rotation, lastSnapCheckRotation) < 0.01f)
            {
                return;
            }

            hasSnapCheckState = true;
            lastSnapCheckPosition = transform.position;
            lastSnapCheckRotation = transform.rotation;

            if (!TryGetGroundHit(out var hit))
                return;

            if (Mathf.Approximately(transform.position.y, hit.point.y))
                return;

            Undo.RecordObject(transform, "Snap Spawn Location To Ground");
            transform.position = new Vector3(transform.position.x, hit.point.y, transform.position.z);
            EditorUtility.SetDirty(transform);
            lastSnapCheckPosition = transform.position;
#endif
        }

        private bool TryGetGroundHit(out RaycastHit hit)
        {
            var rayOrigin = transform.position + Vector3.up * SnapStartHeight;
            return Physics.Raycast(rayOrigin, Vector3.down, out hit, SnapDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }
    }
}
