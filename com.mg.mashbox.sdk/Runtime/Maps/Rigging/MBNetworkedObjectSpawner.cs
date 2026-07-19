using System;
using MashBoxSDK.Services;
using UnityEngine;
using UnityEngine.Events;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MashBoxSDK.Maps.Rigging
{
    [AddComponentMenu("MashBox/Maps/Networking/Networked Object Spawner")]
    [DisallowMultipleComponent]
    public class MBNetworkedObjectSpawner : MonoBehaviour
    {
        [Header("Spawn")]
        [Tooltip("Name of the MashBox-registered network prefab to spawn, for example Drift Car 2.")]
        [SerializeField] private string spawnKey = "Drift Car 2";
        [Tooltip("Transform used as the spawn position and rotation. Defaults to this object.")]
        [SerializeField] private Transform spawnPoint;
        [Tooltip("Local offset from the spawn transform.")]
        [SerializeField] private Vector3 localOffset;
        [Tooltip("Ask MashBox to snap the spawned object down to ground before spawning.")]
        [SerializeField] private bool snapToGround = true;
        [Tooltip("Log a warning when the current session cannot accept this spawn request.")]
        [SerializeField] private bool warnWhenUnavailable = true;

        [Header("Events")]
        [SerializeField] private UnityEvent onSpawnRequested;
        [SerializeField] private UnityEvent onSpawnUnavailable;

        [SerializeField, HideInInspector] private string requestKey;

        private const float GizmoGroundRayUp = 2.0f;
        private const float GizmoGroundRayDown = 8.0f;
        private static readonly Color GizmoColor = new Color(0.1f, 0.72f, 1.0f, 0.95f);
        private static readonly Color GizmoFillColor = new Color(0.1f, 0.72f, 1.0f, 0.12f);
        private static readonly Color GizmoSnapColor = new Color(1.0f, 0.82f, 0.2f, 0.9f);

        public string SpawnKey => NetworkedObjectSpawnService.NormalizeKey(spawnKey);
        public Transform SpawnPoint => ResolveSpawnTransform();

        private void Reset()
        {
            EnsureSpawnPoint();
            EnsureRequestKey();
        }

        private void OnValidate()
        {
            EnsureSpawnPoint();
            EnsureRequestKey();
        }

        public void SetSpawnKey(string value)
        {
            spawnKey = value;
        }

        public void Spawn()
        {
            TrySpawn(spawnKey);
        }

        public void Spawn(string overrideSpawnKey)
        {
            TrySpawn(overrideSpawnKey);
        }

        public bool TrySpawn()
        {
            return TrySpawn(spawnKey);
        }

        public bool TrySpawn(string overrideSpawnKey)
        {
            MBNetworkedObjectSpawnRequest request = BuildRequest(overrideSpawnKey);
            bool accepted = NetworkedObjectSpawnService.Spawn(request);

            if (accepted)
            {
                onSpawnRequested?.Invoke();
                return true;
            }

            if (warnWhenUnavailable)
                Debug.LogWarning($"[MBNetworkedObjectSpawner] Spawn request was not accepted for key '{request.SpawnKey}'.", this);

            onSpawnUnavailable?.Invoke();
            return false;
        }

        public MBNetworkedObjectSpawnRequest BuildRequest()
        {
            return BuildRequest(spawnKey);
        }

        public MBNetworkedObjectSpawnRequest BuildRequest(string overrideSpawnKey)
        {
            Transform basis = ResolveSpawnTransform();
            Vector3 position = basis.TransformPoint(localOffset);
            Quaternion rotation = basis.rotation;

            return MBNetworkedObjectSpawnRequest.Create(
                overrideSpawnKey,
                position,
                rotation,
                snapToGround,
                requestKey);
        }

        private Transform ResolveSpawnTransform()
        {
            return spawnPoint != null ? spawnPoint : transform;
        }

        private void EnsureSpawnPoint()
        {
            if (spawnPoint == null)
                spawnPoint = transform;
        }

        private void EnsureRequestKey()
        {
            if (!string.IsNullOrWhiteSpace(requestKey))
                return;

            requestKey = Guid.NewGuid().ToString("N");
        }

        private void OnDrawGizmos()
        {
            DrawSpawnGizmo(selected: false);
        }

        private void OnDrawGizmosSelected()
        {
            DrawSpawnGizmo(selected: true);
        }

        private void DrawSpawnGizmo(bool selected)
        {
            if (!MBGameplayGizmoVisibility.Visible)
                return;

            Transform basis = ResolveSpawnTransform();
            if (basis == null)
                return;

            Vector3 position = basis.TransformPoint(localOffset);
            Quaternion rotation = basis.rotation;
            float size = GetGizmoSize(position);
            float footprintSize = selected ? size * 0.8f : size * 0.55f;
            float arrowLength = selected ? size * 1.5f : size;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;

            Gizmos.matrix = Matrix4x4.TRS(position, rotation, Vector3.one);
            Gizmos.color = GizmoFillColor;
            Gizmos.DrawCube(Vector3.zero, new Vector3(footprintSize, 0.025f, footprintSize));
            Gizmos.color = GizmoColor;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(footprintSize, 0.05f, footprintSize));
            Gizmos.DrawWireSphere(Vector3.zero, footprintSize * 0.22f);
            DrawLocalArrow(arrowLength, footprintSize * 0.35f);

            Gizmos.matrix = previousMatrix;

            if (snapToGround && selected)
                DrawGroundSnapGizmo(position, size);

            Gizmos.color = previousColor;

#if UNITY_EDITOR
            if (selected)
                DrawSpawnLabel(position, size);
#endif
        }

        private static void DrawLocalArrow(float length, float headSize)
        {
            Vector3 tip = Vector3.forward * length;
            Gizmos.DrawLine(Vector3.zero, tip);
            Gizmos.DrawLine(tip, tip + (Vector3.back + Vector3.left * 0.55f) * headSize);
            Gizmos.DrawLine(tip, tip + (Vector3.back + Vector3.right * 0.55f) * headSize);
            Gizmos.DrawLine(tip, tip + (Vector3.back + Vector3.up * 0.35f) * headSize);
        }

        private static void DrawGroundSnapGizmo(Vector3 position, float size)
        {
            Vector3 rayStart = position + Vector3.up * GizmoGroundRayUp;
            Vector3 rayEnd = position - Vector3.up * GizmoGroundRayDown;

            Gizmos.color = GizmoSnapColor;
            Gizmos.DrawLine(rayStart, rayEnd);
            Gizmos.DrawWireSphere(rayStart, size * 0.08f);
            Gizmos.DrawWireSphere(rayEnd, size * 0.08f);
        }

        private static float GetGizmoSize(Vector3 position)
        {
#if UNITY_EDITOR
            return Mathf.Clamp(HandleUtility.GetHandleSize(position) * 0.28f, 0.35f, 2.0f);
#else
            return 1.0f;
#endif
        }

#if UNITY_EDITOR
        private void DrawSpawnLabel(Vector3 position, float size)
        {
            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal =
                {
                    textColor = GizmoColor
                }
            };

            Handles.Label(position + Vector3.up * size * 0.75f, $"Spawn: {SpawnKey}", labelStyle);
        }
#endif
    }
}
