using MashBoxSDK.EditorResources;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.Maps
{
    [CustomEditor(typeof(MBSideHit))]
    public class MBSideHitInspector : Editor
    {
        private const float ToggleHandleSize = 0.16f;
        private const float AddFlagSetHandleSize = 0.14f;
        private const float RemoveFlagSetHandleSize = 0.11f;
        private const float MinimumAxisSize = 0.01f;

        private static readonly Color OrangeFlagWireColor = new Color(1f, 0.72f, 0.24f, 1f);
        private static readonly Color BlueFlagWireColor = new Color(0.35f, 0.82f, 1f, 1f);

        public override void OnInspectorGUI()
        {
            MashBoxInspectorHeaderUtility.DrawScriptHeader();
            DrawDefaultInspector();
        }

        private void OnSceneGUI()
        {
            if (!MBGameplayGizmoVisibility.Visible)
                return;

            if (target is MBSideHit sideHit)
                DrawSceneHandles(sideHit);
        }

        private void DrawSceneHandles(MBSideHit sideHit)
        {
            if (!sideHit || Selection.activeTransform != sideHit.transform)
                return;

            serializedObject.Update();

            DrawFlagColorToggle(sideHit);
            DrawFlagSetControls(sideHit);
            DrawFlagSetTransformHandles(sideHit);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawFlagColorToggle(MBSideHit sideHit)
        {
            var transform = sideHit.transform;
            var previousColor = Handles.color;
            Vector3 togglePosition = transform.position
                                     + (transform.up * Mathf.Max(sideHit.BoxSize.y + 0.75f, 1.1f))
                                     + (transform.right * 0.45f);
            float handleSize = HandleUtility.GetHandleSize(togglePosition) * ToggleHandleSize;

            Handles.color = GetFlagWireColor(sideHit);
            if (Handles.Button(togglePosition, Quaternion.identity, handleSize, handleSize * 1.2f, Handles.SphereHandleCap))
            {
                Undo.RecordObject(sideHit, "Toggle Side Hit Flag Color");
                sideHit.ToggleFlagVisualColor();
                EditorUtility.SetDirty(sideHit);
                SceneView.RepaintAll();
            }

            var labelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };
            labelStyle.normal.textColor = GetFlagWireColor(sideHit);
            Handles.Label(togglePosition + (Vector3.up * handleSize * 1.4f), $"Flag: {sideHit.FlagColor}", labelStyle);
            Handles.color = previousColor;
        }

        private void DrawFlagSetControls(MBSideHit sideHit)
        {
            var transform = sideHit.transform;
            var previousColor = Handles.color;
            Vector3 controlPosition = transform.position
                                      + (transform.up * Mathf.Max(sideHit.BoxSize.y + 0.75f, 1.1f))
                                      - (transform.right * 0.45f);
            float addHandleSize = HandleUtility.GetHandleSize(controlPosition) * AddFlagSetHandleSize;
            float removeHandleSize = HandleUtility.GetHandleSize(controlPosition) * RemoveFlagSetHandleSize;

            Handles.color = GetFlagWireColor(sideHit);
            if (Handles.Button(controlPosition, Quaternion.identity, addHandleSize, addHandleSize * 1.2f, Handles.CubeHandleCap))
            {
                Undo.RecordObject(sideHit, "Add Side Hit Flag Set");
                sideHit.AddFlagSet();
                EditorUtility.SetDirty(sideHit);
                serializedObject.Update();
                SceneView.RepaintAll();
            }

            if (sideHit.FlagSetCount > 1)
            {
                Vector3 removePosition = controlPosition - (Vector3.up * addHandleSize * 1.8f);
                Handles.color = new Color(1f, 0.35f, 0.25f, 1f);
                if (Handles.Button(removePosition, Quaternion.identity, removeHandleSize, removeHandleSize * 1.2f, Handles.CubeHandleCap))
                {
                    Undo.RecordObject(sideHit, "Remove Side Hit Flag Set");
                    sideHit.RemoveLastFlagSet();
                    EditorUtility.SetDirty(sideHit);
                    serializedObject.Update();
                    SceneView.RepaintAll();
                }
            }

            var labelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleRight
            };
            labelStyle.normal.textColor = GetFlagWireColor(sideHit);
            Handles.Label(controlPosition + (Vector3.up * addHandleSize * 1.4f), $"Sets: {sideHit.FlagSetCount}", labelStyle);
            Handles.color = previousColor;
        }

        private void DrawFlagSetTransformHandles(MBSideHit sideHit)
        {
            SerializedProperty flagSetsProperty = serializedObject.FindProperty("flagSets");
            if (flagSetsProperty == null || !flagSetsProperty.isArray)
                return;

            var transform = sideHit.transform;
            for (int i = 0; i < flagSetsProperty.arraySize; i++)
            {
                SerializedProperty flagSetProperty = flagSetsProperty.GetArrayElementAtIndex(i);
                SerializedProperty centerProperty = flagSetProperty.FindPropertyRelative("localCenter");
                SerializedProperty eulerProperty = flagSetProperty.FindPropertyRelative("localEulerAngles");
                SerializedProperty scaleProperty = flagSetProperty.FindPropertyRelative("localScale");
                if (centerProperty == null || eulerProperty == null || scaleProperty == null)
                    continue;

                Vector3 worldCenter = transform.TransformPoint(centerProperty.vector3Value);
                Quaternion worldRotation = transform.rotation * Quaternion.Euler(eulerProperty.vector3Value);
                float handleSize = HandleUtility.GetHandleSize(worldCenter);
                Vector3 localScale = SanitizeScale(scaleProperty.vector3Value);

                Handles.color = GetFlagWireColor(sideHit);
                Handles.Label(worldCenter + (Vector3.up * handleSize * 0.18f), $"{i + 1}", EditorStyles.boldLabel);

                EditorGUI.BeginChangeCheck();
                Vector3 newWorldCenter = Handles.PositionHandle(worldCenter, worldRotation);
                Quaternion newWorldRotation = Handles.RotationHandle(worldRotation, newWorldCenter);
                Vector3 newLocalScale = Handles.ScaleHandle(localScale, newWorldCenter, newWorldRotation, handleSize * 0.8f);
                if (!EditorGUI.EndChangeCheck())
                    continue;

                Undo.RecordObject(sideHit, "Move Side Hit Flag Set");
                centerProperty.vector3Value = transform.InverseTransformPoint(newWorldCenter);
                eulerProperty.vector3Value = (Quaternion.Inverse(transform.rotation) * newWorldRotation).eulerAngles;
                scaleProperty.vector3Value = SanitizeScale(newLocalScale);
                EditorUtility.SetDirty(sideHit);
                SceneView.RepaintAll();
            }
        }

        private static Color GetFlagWireColor(MBSideHit sideHit)
        {
            return sideHit.FlagColor == MBSideHit.FlagVisualColor.Blue ? BlueFlagWireColor : OrangeFlagWireColor;
        }

        private static Vector3 SanitizeScale(Vector3 value)
        {
            if (value == Vector3.zero)
                return Vector3.one;

            return new Vector3(
                Mathf.Max(Mathf.Abs(value.x), MinimumAxisSize),
                Mathf.Max(Mathf.Abs(value.y), MinimumAxisSize),
                Mathf.Max(Mathf.Abs(value.z), MinimumAxisSize));
        }
    }
}
