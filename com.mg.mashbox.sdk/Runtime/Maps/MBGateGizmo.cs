using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MashBoxSDK.Maps
{
    public class MBGateGizmo : MonoBehaviour
    {
        private static readonly Color ExpertLineFillColor = new Color(0.0f, 0.0f, 0.0f, 0.18f);
        private static readonly Color ExpertLineWireColor = new Color(0.0f, 0.0f, 0.0f, 0.96f);
        private static readonly Color ExpertLineStartFillColor = new Color(1.0f, 0.92f, 0.28f, 0.65f);
        private static readonly Color ExpertLineStartWireColor = new Color(1.0f, 0.92f, 0.28f, 1.0f);

        [SerializeField] private Vector3 boxSize = new Vector3(2f, 2f, 0.5f);
        [SerializeField] private Color fillColor = new Color(0.2f, 0.8f, 1f, 0.12f);
        [SerializeField] private Color wireColor = new Color(0.2f, 0.8f, 1f, 0.95f);

        public Vector3 BoxSize
        {
            get => boxSize;
            set => boxSize = value;
        }

        public Color FillColor
        {
            get => fillColor;
            set => fillColor = value;
        }

        public Color WireColor
        {
            get => wireColor;
            set => wireColor = value;
        }

        private void OnDrawGizmos()
        {
            if (GetComponent<MBRaceGate>() != null || GetComponentInParent<MBRace>() != null)
                return;

            var previousMatrix = Gizmos.matrix;
            var previousColor = Gizmos.color;

            Gizmos.matrix = transform.localToWorldMatrix;
            bool isExpertLineGate = GetComponentInParent<MBExpertLine>() != null;
            bool isExpertLineStartGate = isExpertLineGate && IsFirstExpertLineGate();

            Gizmos.color = isExpertLineGate ? ExpertLineFillColor : fillColor;
            Gizmos.DrawCube(Vector3.zero, boxSize);

            Gizmos.color = isExpertLineGate ? ExpertLineWireColor : wireColor;
            Gizmos.DrawWireCube(Vector3.zero, boxSize);

            if (isExpertLineStartGate)
                DrawExpertLineStartMarker();

            Gizmos.color = previousColor;
            Gizmos.matrix = previousMatrix;

#if UNITY_EDITOR
            if (isExpertLineStartGate)
            {
                Handles.color = ExpertLineStartWireColor;
                Handles.Label(transform.position + (transform.up * (boxSize.y + 0.35f)), "START GATE\nExpert Line timer starts here");
            }
#endif
        }

        private void DrawExpertLineStartMarker()
        {
            Vector3 topBandCenter = new Vector3(0f, boxSize.y * 0.5f + 0.035f, 0f);
            Vector3 topBandSize = new Vector3(Mathf.Max(0.12f, boxSize.x * 0.74f), 0.07f, Mathf.Max(0.08f, boxSize.z * 1.08f));
            Gizmos.color = ExpertLineStartFillColor;
            Gizmos.DrawCube(topBandCenter, topBandSize);
            Gizmos.color = ExpertLineStartWireColor;
            Gizmos.DrawWireCube(topBandCenter, topBandSize);

            float arrowZ = Mathf.Max(0.08f, boxSize.z * 0.5f + 0.025f);
            float arrowWidth = Mathf.Max(0.3f, boxSize.x * 0.34f);
            float arrowY = boxSize.y * 0.1f;
            Vector3 arrowTip = new Vector3(0f, arrowY, -arrowZ);
            Vector3 arrowLeft = new Vector3(-arrowWidth, arrowY, arrowZ * 0.35f);
            Vector3 arrowRight = new Vector3(arrowWidth, arrowY, arrowZ * 0.35f);
            Gizmos.DrawLine(arrowLeft, arrowTip);
            Gizmos.DrawLine(arrowRight, arrowTip);
            Gizmos.DrawLine(arrowLeft, arrowRight);
        }

        private bool IsFirstExpertLineGate()
        {
            MBExpertLine expertLine = GetComponentInParent<MBExpertLine>();
            if (expertLine == null)
                return false;

            foreach (Transform child in expertLine.transform)
            {
                if (!child.name.StartsWith("Gate"))
                    continue;

                return child == transform;
            }

            return false;
        }
    }
}
