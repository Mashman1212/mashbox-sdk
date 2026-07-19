using MashBoxSDK.EditorResources;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.Maps
{
    [CustomEditor(typeof(MBPhotoSpot))]
    public class MBPhotoSpotInspector : Editor
    {
        private static readonly Color TriggerZoneHandleColor = new Color(0.65f, 1f, 0.55f, 0.95f);

        public override void OnInspectorGUI()
        {
            MashBoxInspectorHeaderUtility.DrawScriptHeader();
            EditorGUILayout.HelpBox("Photo Spot defines a camera challenge marker. Move and scale the trigger zone sphere in Scene view to control where the player activates the photo challenge.", MessageType.None);
            DrawDefaultInspector();
        }

        private void OnSceneGUI()
        {
            if (!MBGameplayGizmoVisibility.Visible)
                return;

            var photoSpot = (MBPhotoSpot)target;
            if (photoSpot == null)
                return;

            var transform = photoSpot.transform;
            var worldPosition = photoSpot.TriggerZoneWorldPosition;
            var handleRotation = Tools.pivotRotation == PivotRotation.Local ? transform.rotation : Quaternion.identity;

            using (new Handles.DrawingScope(TriggerZoneHandleColor))
            {
                EditorGUI.BeginChangeCheck();
                var newWorldPosition = Handles.PositionHandle(worldPosition, handleRotation);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(photoSpot, "Move Photo Spot Trigger Zone");
                    photoSpot.SetTriggerZoneLocalPosition(transform.InverseTransformPoint(newWorldPosition));
                    EditorUtility.SetDirty(photoSpot);
                }

                EditorGUI.BeginChangeCheck();
                var newRadius = Handles.RadiusHandle(handleRotation, worldPosition, photoSpot.TriggerZoneRadius);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(photoSpot, "Resize Photo Spot Trigger Zone");
                    photoSpot.SetTriggerZoneRadius(newRadius);
                    EditorUtility.SetDirty(photoSpot);
                }
            }
        }
    }
}
