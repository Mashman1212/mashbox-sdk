#if UNITY_6000_0_OR_NEWER
using MashBoxSDK.Maps;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    [CustomEditor(typeof(MBChairliftStation))]
    public sealed class MBChairliftStationEditor : Editor
    {
        private bool previousHidden;
        private void OnEnable() => previousHidden = Tools.hidden;
        private void OnDisable() => Tools.hidden = previousHidden;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("Station handles are at the hut floor. Place this point on your landing pad. The original transform origin is retained to preserve cable connections.", MessageType.Info);
        }

        private void OnSceneGUI()
        {
            var station = (MBChairliftStation)target;
            bool show = MBGameplayGizmoVisibility.ChairliftsVisible && !Application.isPlaying &&
                Selection.activeGameObject == station.gameObject && Selection.gameObjects.Length == 1;
            bool customTool = Tools.current == Tool.Move || Tools.current == Tool.Rotate || Tools.current == Tool.Scale;
            Tools.hidden = previousHidden || (show && customTool);
            if (!show || !customTool || previousHidden) return;
            var t = station.transform;
            Vector3 pivot = station.PlacementPosition;
            Quaternion orientation = Tools.pivotRotation == PivotRotation.Local ? t.rotation : Quaternion.identity;
            EditorGUI.BeginChangeCheck();
            Vector3 position = pivot;
            Quaternion rotation = t.rotation;
            Vector3 scale = t.localScale;
            if (Tools.current == Tool.Move) position = Handles.PositionHandle(pivot, orientation);
            else if (Tools.current == Tool.Rotate) rotation = Handles.RotationHandle(t.rotation, pivot);
            else scale = Handles.ScaleHandle(scale, pivot, t.rotation, HandleUtility.GetHandleSize(pivot));
            if (!EditorGUI.EndChangeCheck()) return;
            Undo.RecordObject(t, "Transform Chairlift Station");
            if (Tools.current == Tool.Move) t.position += position - pivot;
            else
            {
                t.rotation = rotation;
                t.localScale = scale;
                // Keep the placement point stationary while rotating/scaling the station.
                t.position += pivot - station.PlacementPosition;
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }
    }
}
#endif
