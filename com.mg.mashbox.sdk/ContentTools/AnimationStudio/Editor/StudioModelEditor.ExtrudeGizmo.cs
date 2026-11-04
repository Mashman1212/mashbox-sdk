using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private int extrudeDistanceControl;
        private Vector3 extrudeDistanceOrigin, extrudeDistanceStep;
        private float extrudeDistanceStart;
        private StudioMeshToolPreview.Target extrudeDirectionTarget;
        private Vector3 extrudeLocalDirection;
        private bool extrudeDirectionTogether, extrudeDirectionWeld;
        private bool ExtrudeDistanceGizmoActive => extrudeToolPane && !pivot && tool == Tool.Move &&
            livePreview != null && liveTool == LiveTool.Extrude && ActiveMesh && Active >= 0;
        private void ResetExtrudeDistanceGizmo() { extrudeDistanceControl = 0; }
        private void ClearExtrudeDistanceGizmo() { ResetExtrudeDistanceGizmo(); extrudeDirectionTarget = null; }
        private bool TryExtrudeDistanceFrame(out Vector3 center, out Vector3 step)
        {
            center = step = Vector3.zero;
            if (!ExtrudeDistanceGizmoActive) return false;
            GetHandleComponentFrame(out var selected, out var positions, out center);
            if (selected.Count == 0) return false;
            var target = livePreview.Targets[0];
            if (extrudeDirectionTarget != target || extrudeDirectionTogether != extrudeFacesTogether || extrudeDirectionWeld != weld)
            {
                extrudeDirectionTarget = target; extrudeDirectionTogether = extrudeFacesTogether; extrudeDirectionWeld = weld;
                extrudeLocalDirection = target.Topology.ExtrusionHandleDirection(liveFaces, liveEdges, extrudeFacesTogether, weld);
            }
            step = Transforms[Active].TransformVector(extrudeLocalDirection);
            return step.sqrMagnitude > 1e-16f && !float.IsNaN(step.sqrMagnitude) && !float.IsInfinity(step.sqrMagnitude);
        }
        // Shared by native handle input and focused validation. One scalar drives
        // every selected target through the existing captured Extrude preview.
        private void SetExtrudeDistanceFromGizmo(Vector3 destination, Vector3 origin, Vector3 step, float initial)
        {
            if (step.sqrMagnitude <= 1e-16f) return;
            float value = initial + Vector3.Dot(destination-origin, step)/step.sqrMagnitude;
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            extrusionDistance = value; StoreExtrusionDefaults(); QueueLiveTool(LiveTool.Extrude);
            repaint(); toolOptions?.Repaint();
        }
        private bool DrawExtrudeDistanceGizmo()
        {
            if (!TryExtrudeDistanceFrame(out var center, out var step)) return false;
            if (extrudeDistanceControl != 0 && GUIUtility.hotControl != extrudeDistanceControl) ResetExtrudeDistanceGizmo();
            var orientation = local ? PivotOrientation : Quaternion.identity;
            if (extrudeDistanceControl != 0)
            {
                center = extrudeDistanceOrigin + extrudeDistanceStep * (extrusionDistance-extrudeDistanceStart);
                step = extrudeDistanceStep; orientation = moveDragOrientation;
            }
            int previous = GUIUtility.hotControl;
            var pointer = Event.current.mousePosition;
            bool dragging = Event.current.type == EventType.MouseDrag;
            EditorGUI.BeginChangeCheck();
            Vector3 position;
            float size = StudioSettings.Current.handleSize;
            using (new Handles.DrawingScope(Matrix4x4.identity))
            {
                position = DrawMoveHandle(center, orientation, size);
                AntialiasTransformGizmo(center, HandleUtility.GetHandleSize(center)*size*1.35f);
            }
            bool changed = EditorGUI.EndChangeCheck();
            if (GUIUtility.hotControl != 0 && GUIUtility.hotControl != previous && IsMoveHandle(GUIUtility.hotControl))
            {
                extrudeDistanceControl = GUIUtility.hotControl; extrudeDistanceOrigin = center;
                extrudeDistanceStep = step; extrudeDistanceStart = extrusionDistance;
            }
            if (changed && extrudeDistanceControl != 0)
            {
                if (dragging) position = ResolveMoveSnap(SceneView.currentDrawingSceneView, pointer, position, center);
                SetExtrudeDistanceFromGizmo(position, extrudeDistanceOrigin, extrudeDistanceStep, extrudeDistanceStart);
            }
            if (extrudeDistanceControl != 0 && GUIUtility.hotControl != extrudeDistanceControl) ResetExtrudeDistanceGizmo();
            return true;
        }
    }
}
