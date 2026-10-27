using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal enum PivotAction { Align, Orient, Position, Aim }
        internal enum PivotComponent { Vertex, Edge, Face }
        internal sealed class PivotTarget
        {
            internal PivotComponent kind;
            internal Vector3 position, aimPoint;
            internal Quaternion orientation;
            internal Vector3[] outline;
        }
        private readonly Dictionary<StudioModelDocument.Node, StudioMeshCuts> pivotTargetCache = new Dictionary<StudioModelDocument.Node, StudioMeshCuts>();
        private PivotTarget pivotHover;
        private Vector2 pivotPointer;
        private static readonly Vector2[] PivotPickOffsets = { new Vector2(0, 6), new Vector2(0, -6), new Vector2(6, 0), new Vector2(-6, 0), new Vector2(6, 6), new Vector2(-6, 6), new Vector2(6, -6), new Vector2(-6, -6) };
        internal Quaternion PivotOrientation
        {
            get { EnsureLockedPivot(); return HasLockedPivot ? lockedPivotOrientation : UnlockedPivotOrientation; }
        }
        private Quaternion UnlockedPivotOrientation
        {
            get
            {
                if (!componentAxes) return ObjectPivotOrientation;
                if (HasComponentPivot) return componentPivot.orientation;
                if (mode != ComponentMode.Object && ActiveMesh && Components.Count > 0)
                    return ComponentOrientation();
                return ObjectPivotOrientation;
            }
        }
        private Quaternion ObjectPivotOrientation
        {
            get
            {
                var q = Document.nodes[Active].pivotRotation;
                if (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w < .0001f) q = Quaternion.identity;
                return Transforms[Active].rotation * q.normalized;
            }
        }
        internal static PivotAction PivotActionFor(bool control, bool shift) => control
            ? (shift ? PivotAction.Aim : PivotAction.Orient) : (shift ? PivotAction.Position : PivotAction.Align);
        private bool PivotTargetSceneGUI(SceneView view, int selectionId)
        {
            var evt = Event.current;
            if (!pivot || !CanAdjustPivot ||
                evt.alt || pivotSnapHeld || GUIUtility.hotControl != 0 || evt.type == EventType.MouseLeaveWindow)
            { pivotHover = null; return false; }
            var canvas = view is MashBoxStudioViewport studio ? studio.ModelCanvasRect : new Rect(Vector2.zero, view.position.size);
            if (!canvas.Contains(evt.mousePosition) || (view is MashBoxStudioViewport overlay && overlay.IsOverViewportControls(evt.mousePosition)))
            { pivotHover = null; return false; }
            if (evt.type == EventType.MouseMove || evt.type == EventType.MouseDown)
            {
                pivotPointer = evt.mousePosition;
                pivotHover = PickPivotTarget(view.camera, PivotCameraPoint(view, pivotPointer), EditorGUIUtility.pixelsPerPoint);
                repaint();
            }
            if (evt.type == EventType.KeyDown || evt.type == EventType.KeyUp) repaint();
            if (pivotHover == null || HandleUtility.nearestControl != selectionId) return false;
            var action = PivotActionFor(evt.control || evt.command, evt.shift);
            if (evt.type == EventType.Repaint) DrawPivotTarget(canvas, action);
            if (evt.type != EventType.MouseDown || evt.button != 0) return false;
            var target = pivotHover;
            Run(() => ApplyPivotTarget(target, action));
            if (gestureUndo >= 0) { Undo.CollapseUndoOperations(gestureUndo); gestureUndo = -1; }
            evt.Use(); repaint(); return true;
        }
        internal void ApplyPivotTarget(PivotTarget target, PivotAction action)
        {
            if (target == null || !CanAdjustPivot) return;
            var transform = Transforms[Active];
            var orientation = PivotOrientation;
            if (action == PivotAction.Aim)
            {
                var direction = target.aimPoint - EditingPivotPosition;
                if (direction.sqrMagnitude < 1e-10f) return;
                orientation = Quaternion.FromToRotation(orientation * Vector3.right, direction.normalized) * orientation;
            }
            else if (action != PivotAction.Position) orientation = target.orientation;
            if (mode != ComponentMode.Object)
            {
                SetComponentPivot(action == PivotAction.Align || action == PivotAction.Position ? target.position : EditingPivotPosition,
                    orientation, action + " component pivot");
                return;
            }
            Undo.RegisterCompleteObjectUndo(Document, action + " pivot");
            if (action == PivotAction.Align || action == PivotAction.Position) Document.Repivot(Active, Transforms, target.position);
            if (action != PivotAction.Position) Document.nodes[Active].pivotRotation = Quaternion.Inverse(transform.rotation) * orientation;
            RememberLockedPivot(Transforms[Active].position, orientation);
            EditorUtility.SetDirty(Document); Changed();
        }
        private static Quaternion PivotFrame(Vector3 x, Vector3 up)
        {
            x = x.normalized;
            if (x.sqrMagnitude < .5f) x = Vector3.right;
            up = Vector3.ProjectOnPlane(up, x).normalized;
            if (up.sqrMagnitude < .5f) up = Vector3.ProjectOnPlane(Mathf.Abs(x.y) < .9f ? Vector3.up : Vector3.forward, x).normalized;
            return Quaternion.LookRotation(Vector3.Cross(x, up), up);
        }
        internal PivotTarget PickPivotTarget(Camera camera, Vector2 mouse, float dpi, Vector2? sample = null)
        {
            if (!camera || !Document) return null;
            var rayPoint = sample ?? mouse;
            var ray = camera.ScreenPointToRay(new Vector3(rayPoint.x * dpi, camera.pixelHeight - rayPoint.y * dpi));
            float nearest = float.PositiveInfinity;
            StudioMeshCuts hitTopology = null; StudioMeshCuts.Face hitFace = null;
            Vector3[] hitVertices = null; Transform hitTransform = null;
            for (int n = 0; n < Transforms.Length; n++)
            {
                var node = Document.nodes[n]; var mesh = node.mesh;
                if (!mesh || !Transforms[n].gameObject.activeInHierarchy) continue;
                var renderer = Transforms[n].GetComponent<Renderer>();
                if (!renderer || !renderer.enabled || !renderer.bounds.IntersectRay(ray)) continue;
                bool triangles = true;
                for (int sub = 0; sub < mesh.subMeshCount; sub++) if (mesh.GetTopology(sub) != MeshTopology.Triangles) triangles = false;
                if (!triangles) continue;
                if (!pivotTargetCache.TryGetValue(node, out var topology)) pivotTargetCache[node] = topology = new StudioMeshCuts(node);
                var vertices = node.skinned ? PickVertices(n) : topology.Positions;
                var t = Transforms[n];
                foreach (var face in topology.Faces)
                {
                    var v = face.Vertices;
                    for (int k = 1; k < v.Count - 1; k++)
                        if (RayTriangle(ray, t.TransformPoint(vertices[v[0]]), t.TransformPoint(vertices[v[k]]), t.TransformPoint(vertices[v[k + 1]]), out float distance) && distance < nearest)
                        { nearest = distance; hitTopology = topology; hitFace = face; hitVertices = vertices; hitTransform = t; }
                }
            }
            if (hitFace == null)
            {
                // Screen-space component tolerance also extends just outside silhouettes.
                if (sample.HasValue) return null;
                PivotTarget edge = null;
                foreach (var offset in PivotPickOffsets)
                {
                    var nearby = PickPivotTarget(camera, mouse, dpi, mouse + offset);
                    if (nearby == null) continue;
                    if (nearby.kind == PivotComponent.Vertex) return nearby;
                    if (nearby.kind == PivotComponent.Edge && edge == null) edge = nearby;
                }
                return edge;
            }
            var points = new Vector3[hitFace.Vertices.Count]; var center = Vector3.zero;
            for (int i = 0; i < points.Length; i++) { points[i] = hitTransform.TransformPoint(hitVertices[hitFace.Vertices[i]]); center += points[i]; }
            center /= points.Length;
            var normal = Vector3.Cross(points[1] - points[0], points[2] - points[0]).normalized;
            Vector2 Screen(Vector3 world) { var p = camera.WorldToScreenPoint(world); return new Vector2(p.x, camera.pixelHeight - p.y) / dpi; }
            int closest = -1; float best = 9 * 9;
            for (int i = 0; i < points.Length; i++)
            { float d = (Screen(points[i]) - mouse).sqrMagnitude; if (d <= best) { best = d; closest = i; } }
            if (closest >= 0)
            {
                // Average adjoining surface normals, including vertices split at UV/normal seams.
                var localPoint = hitVertices[hitFace.Vertices[closest]]; var vertexNormal = Vector3.zero;
                foreach (var face in hitTopology.Faces)
                {
                    bool touches = false; foreach (int index in face.Vertices) if ((hitVertices[index] - localPoint).sqrMagnitude < 1e-12f) { touches = true; break; }
                    if (!touches || face.Vertices.Count < 3) continue;
                    var a = hitTransform.TransformPoint(hitVertices[face.Vertices[0]]);
                    var b = hitTransform.TransformPoint(hitVertices[face.Vertices[1]]);
                    var c = hitTransform.TransformPoint(hitVertices[face.Vertices[2]]);
                    vertexNormal += Vector3.Cross(b - a, c - a).normalized;
                }
                return new PivotTarget { kind = PivotComponent.Vertex, position = points[closest], aimPoint = points[closest],
                    orientation = PivotFrame(vertexNormal, points[(closest + 1) % points.Length] - points[closest]), outline = new[] { points[closest] } };
            }
            closest = -1; best = 7 * 7;
            for (int i = 0; i < points.Length; i++)
            {
                var a = Screen(points[i]); var b = Screen(points[(i + 1) % points.Length]); var delta = b - a;
                float fraction = delta.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(mouse - a, delta) / delta.sqrMagnitude) : 0;
                float d = (mouse - Vector2.Lerp(a, b, fraction)).sqrMagnitude;
                if (d <= best) { best = d; closest = i; }
            }
            if (closest >= 0)
            {
                var a = points[closest]; var b = points[(closest + 1) % points.Length];
                return new PivotTarget { kind = PivotComponent.Edge, position = (a + b) * .5f, aimPoint = (a + b) * .5f,
                    orientation = PivotFrame(b - a, normal), outline = new[] { a, b } };
            }
            return new PivotTarget { kind = PivotComponent.Face, position = center, aimPoint = ray.GetPoint(nearest),
                orientation = PivotFrame(normal, points[1] - points[0]), outline = points };
        }
        private void DrawPivotTarget(Rect canvas, PivotAction action)
        {
            var target = pivotHover;
            using (new Handles.DrawingScope())
            {
                var depth = Handles.zTest;
                try
                {
                    Handles.zTest = CompareFunction.Always; Handles.color = new Color(1, .8f, .15f, 1);
                    if (target.kind == PivotComponent.Vertex)
                        Handles.DotHandleCap(0, target.position, Quaternion.identity, HandleUtility.GetHandleSize(target.position) * .06f, EventType.Repaint);
                    else if (target.kind == PivotComponent.Edge) Handles.DrawAAPolyLine(3, target.outline);
                    else
                    {
                        Handles.color = new Color(1, .8f, .15f, .18f);
                        for (int i = 1; i < target.outline.Length - 1; i++) Handles.DrawAAConvexPolygon(target.outline[0], target.outline[i], target.outline[i + 1]);
                        Handles.color = new Color(1, .8f, .15f, 1);
                        for (int i = 0; i < target.outline.Length; i++) Handles.DrawAAPolyLine(2, target.outline[i], target.outline[(i + 1) % target.outline.Length]);
                    }
                }
                finally { Handles.zTest = depth; }
            }
            Handles.BeginGUI();
            var label = (action == PivotAction.Aim ? "Aim X" : action.ToString()) + " · " + target.kind;
            var rect = new Rect(Mathf.Clamp(pivotPointer.x + 12, canvas.xMin, canvas.xMax - 150), Mathf.Clamp(pivotPointer.y + 20, canvas.yMin, canvas.yMax - 22), 150, 22);
            var color = GUI.contentColor; GUI.contentColor = Color.black;
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), label, StudioTheme.Styles.miniLabel);
            GUI.contentColor = Color.white; GUI.Label(rect, label, StudioTheme.Styles.miniLabel); GUI.contentColor = color;
            Handles.EndGUI();
        }
    }
}
