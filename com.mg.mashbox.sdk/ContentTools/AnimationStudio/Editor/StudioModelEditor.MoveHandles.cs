using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        [System.Flags] internal enum MoveAxes { X = 1, Y = 2, Z = 4, XY = 3, XZ = 5, YZ = 6, Free = 7 }
        // Older Unity C# compilers recreate method-group delegates on every draw.
        // Keep native handle callbacks stable across Layout, drag and Repaint.
        private static readonly Handles.CapFunction MoveArrowCap = Handles.ArrowHandleCap;
        private static readonly Handles.CapFunction MoveRectangleCap = Handles.RectangleHandleCap;
        private static readonly Handles.CapFunction PivotTriangleCap = DrawPivotTriangleCap;
        private static readonly Vector3[] PivotTrianglePoints = new Vector3[3];
        private readonly int[] moveHandleIds = new int[7];
        private int moveDragControl;
        private MoveAxes moveDragAxes = MoveAxes.Free;
        private Vector3 moveDragOrigin;
        private Quaternion moveDragOrientation;

        private bool IsMoveHandle(int id)
        {
            if (id == 0 || (!pivot && tool != Tool.Move)) return false;
            foreach (int handle in moveHandleIds) if (id == handle) return true;
            return false;
        }
        private void TrackMoveControl(int id, MoveAxes axes, Vector3 origin, Quaternion orientation)
        {
            if (GUIUtility.hotControl != id || moveDragControl == id) return;
            moveDragControl = id; moveDragAxes = axes;
            moveDragOrigin = Handles.matrix.MultiplyPoint3x4(origin);
            moveDragOrientation = orientation;
        }
#if MASHBOX_STUDIO_TESTS
        internal static System.Action<Vector3,float> MoveHandleProbe;
#endif
        private Vector3 DrawMoveHandle(Vector3 center, Quaternion orientation, float sizeMultiplier = 1)
        {
            if (GUIUtility.hotControl == 0) moveDragControl = 0;
            float size = HandleUtility.GetHandleSize(center) * sizeMultiplier;
#if MASHBOX_STUDIO_TESTS
            MoveHandleProbe?.Invoke(center,size);
#endif
            var result = center;
            using (new Handles.DrawingScope())
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    int id = moveHandleIds[axis] = GUIUtility.GetControlID(0x4d5300 + axis, FocusType.Passive);
                    Handles.color = axis == 0 ? Handles.xAxisColor : axis == 1 ? Handles.yAxisColor : Handles.zAxisColor;
                    var direction = orientation * (axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward);
                    var next = Handles.Slider(id, center, direction, size, MoveArrowCap, 0);
                    TrackMoveControl(id, (MoveAxes)(1 << axis), center, orientation);
                    if (GUIUtility.hotControl == id) result = next;
                }
                for (int plane = 0; plane < 3; plane++)
                {
                    int a = plane == 2 ? 1 : 0, b = plane == 0 ? 1 : 2;
                    var first = orientation * (a == 0 ? Vector3.right : Vector3.up);
                    var second = orientation * (b == 1 ? Vector3.up : Vector3.forward);
                    var normal = Vector3.Cross(first, second);
                    var offset = (first + second) * size * .23f;
                    int id = moveHandleIds[3 + plane] = GUIUtility.GetControlID(0x4d5310 + plane, FocusType.Passive);
                    Handles.color = plane == 0 ? Handles.zAxisColor : plane == 1 ? Handles.yAxisColor : Handles.xAxisColor;
                    var next = Handles.Slider2D(id, center + offset, normal, first, second, size * .065f, MoveRectangleCap, Vector2.zero);
                    TrackMoveControl(id, (MoveAxes)((1 << a) | (1 << b)), center, orientation);
                    if (GUIUtility.hotControl == id) result = next - offset;
                }
                int free = moveHandleIds[6] = GUIUtility.GetControlID(0x4d5320, FocusType.Passive);
                Handles.color = Color.yellow;
                var freePosition = Handles.FreeMoveHandle(free, center, size * (pivot ? .1125f : .075f), Vector3.zero, pivot ? PivotTriangleCap : MoveRectangleCap);
                TrackMoveControl(free, MoveAxes.Free, center, orientation);
                if (GUIUtility.hotControl == free) result = freePosition;
            }
            return result;
        }
        private static void DrawPivotTriangleCap(int control, Vector3 position, Quaternion rotation, float size, EventType type)
        {
            if (type == EventType.Layout || type == EventType.MouseMove)
                HandleUtility.AddControl(control, HandleUtility.DistanceToRectangle(position, rotation, size));
            if (type != EventType.Repaint) return;
            // Keep the native center control's rectangular picking shape at the requested size.
            PivotTrianglePoints[0] = position + rotation * (Vector3.up * size);
            PivotTrianglePoints[1] = position + rotation * (new Vector3(-.8660254f, -.5f, 0) * size);
            PivotTrianglePoints[2] = position + rotation * (new Vector3(.8660254f, -.5f, 0) * size);

            Handles.DrawAAConvexPolygon(PivotTrianglePoints);
        }

        internal static Vector3 ConstrainMove(Vector3 origin, Vector3 target, Quaternion orientation, MoveAxes axes)
        {
            var delta = Quaternion.Inverse(orientation) * (target - origin);
            for (int axis = 0; axis < 3; axis++) if (((int)axes & (1 << axis)) == 0) delta[axis] = 0;
            return origin + orientation * delta;
        }
        internal static Vector3 SnapConstrainedGrid(Vector3 origin, Vector3 target, Quaternion orientation, MoveAxes axes, float spacing)
        {
            if (spacing <= 0 || float.IsNaN(spacing) || float.IsInfinity(spacing)) return ConstrainMove(origin, target, orientation, axes);
            var point = Quaternion.Inverse(orientation) * target;
            for (int axis = 0; axis < 3; axis++) if (((int)axes & (1 << axis)) != 0) point[axis] = Mathf.Round(point[axis] / spacing) * spacing;
            return ConstrainMove(origin, orientation * point, orientation, axes);
        }
        internal static bool TrySnapFreeMoveToGrid(Ray ray, float spacing, out Vector3 target)
        {
            target = default;
            if (spacing <= 0 || float.IsNaN(spacing) || float.IsInfinity(spacing) ||
                !new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance)) return false;
            var point = ray.GetPoint(distance);
            target = new Vector3(Mathf.Round(point.x / spacing) * spacing, 0,
                Mathf.Round(point.z / spacing) * spacing);
            return true;
        }
        private bool TryMoveSnapTarget(SceneView view, Vector2 mouse, out Vector3 target)
        {
            var point = PivotCameraPoint(view, mouse);
            if (pivot) return TryPivotSnapVertex(view.camera, point, EditorGUIUtility.pixelsPerPoint, out target);
            if (mode == ComponentMode.Object) return TryObjectSnapTarget(view.camera, point, EditorGUIUtility.pixelsPerPoint, out target);
            var selected = SelectedVertices();
            return TryPivotSnapVertex(view.camera, point, EditorGUIUtility.pixelsPerPoint, out target, null,
                (node, vertex) => node != Active || !selected.Contains(vertex));
        }
        internal string SnapStatusText
        {
            get
            {
                if (!pivotSnapHeld && !gridSnapHeld) return null;
                string label = pivotSnapHeld ? "Vertex Snap · "+StudioShortcuts.Label(StudioShortcut.VertexSnap) : "Grid Snap · "+StudioShortcuts.Label(StudioShortcut.GridSnap);
                if (pivotSnapHeld && gridSnapHeld) label += " (overrides grid)";
                if (moveDragControl != 0 && GUIUtility.hotControl == moveDragControl)
                    label += moveDragAxes == MoveAxes.Free ? " · Free" : " · " + moveDragAxes + (((int)moveDragAxes & ((int)moveDragAxes - 1)) == 0 ? " axis" : " plane");
                return label;
            }
        }
        private void DrawSnapStatus(SceneView view)
        {
            if (Event.current.type != EventType.Repaint) return;
            var text = SnapStatusText; if (text == null) return;
            var canvas = view is MashBoxStudioViewport studio ? studio.ModelCanvasRect : new Rect(Vector2.zero, view.position.size);
            Handles.BeginGUI();
            var content = new GUIContent(text);
            var style = EditorStyles.whiteLabel;
            float width = Mathf.Min(canvas.width - 16, style.CalcSize(content).x + 24);
            var rect = new Rect(canvas.center.x - width * .5f, canvas.yMax - 38, width, 26);
            EditorGUI.DrawRect(rect, new Color(.08f, .09f, .1f, .92f));
            GUI.Label(new Rect(rect.x + 12, rect.y + 4, rect.width - 24, 20), content, style);
            Handles.EndGUI();
        }
    }
}
