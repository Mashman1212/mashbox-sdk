using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private readonly List<Vector3> polygonPoints = new List<Vector3>();
        private Plane polygonPlane;
        private Vector3? polygonHover;
        private readonly List<int> polygonPreviewTriangles = new List<int>();
        private void UpdatePolygonPreview()
        {
            polygonPreviewTriangles.Clear();
            if (polygonPoints.Count < 3) return;
            var normal = Vector3.zero;
            for (int i = 0; i < polygonPoints.Count; i++) normal += Vector3.Cross(polygonPoints[i] - polygonPoints[0], polygonPoints[(i + 1) % polygonPoints.Count] - polygonPoints[0]);
            if (normal.sqrMagnitude < 1e-24f) return;
            var ring = new List<int>();
            for (int i = 0; i < polygonPoints.Count; i++) ring.Add(i);
            try { StudioMeshCuts.TriangulateSliceCap(ring, polygonPoints, normal.normalized, polygonPreviewTriangles); }
            catch (InvalidOperationException) { polygonPreviewTriangles.Clear(); }
        }
        private void BeginPolygon(SceneView view, Action<StudioModelDocument> loaded)
        {
            using var camera = new CreationCameraScope(view);
            FinishToolsForSwitch(); EndPivotSnap(); ClearGridSnap(); CancelMarkingMenu(); EnsureWorkspace(loaded);
            polygonPlane = new Plane(-view.camera.transform.forward, view.pivot);
            polygonPoints.Clear(); polygonPreviewTriangles.Clear(); polygonHover = null;
            cutTool = CutTool.Polygon; tool = Tool.None; pivot = false;
            CameraOptionsVisible = LightingOptionsVisible = false;
            error = null; repaint(); OpenToolOptions();
        }
        private bool PolygonKey(Event evt)
        {
            if (cutTool != CutTool.Polygon) return false;
            if (StudioShortcuts.Pressed(StudioShortcut.Cancel, evt))
            { StopCutTool(); evt.Use(); repaint(); return true; }
            if (evt.alt || evt.control || evt.command || evt.shift) return false;
            if (evt.keyCode == KeyCode.Backspace || evt.keyCode == KeyCode.Delete)
            {
                if (polygonPoints.Count > 0) polygonPoints.RemoveAt(polygonPoints.Count - 1);
                UpdatePolygonPreview();
                evt.Use(); repaint(); return true;
            }
            if (evt.keyCode != KeyCode.W) return false;
            Run(() => { CommitPolygon(); StopCutTool(); tool = Tool.Move; });
            evt.Use(); repaint(); return true;
        }
        private void CommitPolygon()
        {
            int index = Document.AddPolygon(polygonPoints);
            RememberAction(RepeatKind.Polygon);
            polygonPoints.Clear(); polygonHover = null;
            mode = ComponentMode.Object; pivot = false; search = "";
            Rebuild(); SelectObject(index); Changed();
        }
        private void UpdatePolygonHover(SceneView view)
        {
            polygonHover = null;
            var mouse = Event.current.mousePosition;
            if (view is MashBoxStudioViewport host && (!host.ModelCanvasRect.Contains(mouse) || host.IsOverViewportControls(mouse))) return;
            if (TryPolygonPoint(view, mouse, out var point)) polygonHover = point;
        }
        private bool TryPolygonPoint(SceneView view, Vector2 mouse, out Vector3 point)
        {
            // Preserve shared precedence: vertex/edge wins over grid, with no
            // fallback to unsnapped placement when a held snap finds no target.
            if (edgeSnapHeld) return TryEdgeSnapTarget(view, mouse, out point);
            if (pivotSnapHeld) return TryPivotSnapVertex(view.camera, PivotCameraPoint(view, mouse), EditorGUIUtility.pixelsPerPoint, out point);
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            if (gridSnapHeld)
            {
                var frame = view is MashBoxStudioViewport flat && flat.IsOrthographicView
                    ? flat.ViewKind == 0 ? Quaternion.Euler(90, 0, 0) : flat.ViewKind == 2 ? Quaternion.Euler(0, 0, 90) : Quaternion.identity
                    : Quaternion.identity;
                return TrySnapFreeMoveToGrid(ray, StudioSceneUnits.GridSpacing, frame, out point);
            }
            point = default;
            if (!polygonPlane.Raycast(ray, out float distance)) return false;
            point = ray.GetPoint(distance); return true;
        }
        private bool PolygonSceneGUI(SceneView view)
        {
            var evt = Event.current;
            if (HandleModelKeyboard(view))
            { if (cutTool == CutTool.Polygon) UpdatePolygonHover(view); return true; }
            if (evt.alt) return true;
            int control = GUIUtility.GetControlID("MashBoxPolygonCreate".GetHashCode(), FocusType.Passive);
            var canvas = view is MashBoxStudioViewport studio ? studio.ModelCanvasRect : new Rect(0, 0, view.position.width, view.position.height);
            bool over = canvas.Contains(evt.mousePosition) && (!(view is MashBoxStudioViewport host) || !host.IsOverViewportControls(evt.mousePosition));
            if (over && evt.type == EventType.Layout) HandleUtility.AddDefaultControl(control);
            if (evt.type == EventType.MouseMove || evt.type == EventType.MouseDown)
            {
                UpdatePolygonHover(view);
                if (over && evt.type == EventType.MouseDown && evt.button == 0 && polygonHover.HasValue)
                {
                    var point = polygonHover.Value;
                    if (!polygonPoints.Exists(p => (p - point).sqrMagnitude < 1e-12f)) polygonPoints.Add(point);
                    UpdatePolygonPreview();
                    edgeSnapLatched = false; // Each placed vertex is a new snap gesture.
                    evt.Use();
                }
                repaint();
            }
            if (evt.type == EventType.Repaint)
            {
                using (new Handles.DrawingScope(new Color(.35f, .85f, 1f)))
                {
                    Handles.color = new Color(.35f, .85f, 1f, .18f);
                    for (int i = 0; i < polygonPreviewTriangles.Count; i += 3)
                        Handles.DrawAAConvexPolygon(polygonPoints[polygonPreviewTriangles[i]], polygonPoints[polygonPreviewTriangles[i + 1]], polygonPoints[polygonPreviewTriangles[i + 2]]);
                    Handles.color = new Color(.35f, .85f, 1f);
                    for (int i = 1; i < polygonPoints.Count; i++) Handles.DrawAAPolyLine(2, polygonPoints[i - 1], polygonPoints[i]);
                    if (polygonPoints.Count > 2) Handles.DrawDottedLine(polygonPoints[polygonPoints.Count - 1], polygonPoints[0], 4);
                    if (polygonPoints.Count > 0 && polygonHover.HasValue) Handles.DrawAAPolyLine(2, polygonPoints[polygonPoints.Count - 1], polygonHover.Value);
                    if (polygonHover.HasValue) Handles.DotHandleCap(0, polygonHover.Value, Quaternion.identity, HandleUtility.GetHandleSize(polygonHover.Value) * .05f, EventType.Repaint);
                    foreach (var point in polygonPoints) Handles.DotHandleCap(0, point, Quaternion.identity, HandleUtility.GetHandleSize(point) * .04f, EventType.Repaint);
                }
            }
            return true;
        }
    }
}
