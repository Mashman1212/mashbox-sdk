using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private bool pivotSnapHeld, edgeSnapHeld, edgeSnapLatched;
        private Vector3 edgeSnapStart, edgeSnapEnd;
        private KeyCode pivotSnapKey;
        private int pivotSnapControl, pivotSnapUndo = -1;
        private int pivotSnapButton;
        private bool quickPivotSnap;
        private bool snapMovesObjects, snapMovesComponents;
        private Vector3 objectSnapOffset;
        private SceneView vertexSnapView;

        private Quaternion DrawPivotRotationRings(Vector3 center, Quaternion orientation, float sizeMultiplier)
        {
            // Use the established native rotation control set, including its stable
            // ring identities and drag handling, alongside the translation controls.
            rotationHandleIds = Handles.RotationHandleIds.@default;
            var evt = Event.current; var modifiers = evt.modifiers;
            if (evt.control || evt.command) evt.modifiers = modifiers & ~EventModifiers.Shift;
            try { return DrawSizedRotationHandle(rotationHandleIds, orientation, center, sizeMultiplier * .8f); }
            finally { evt.modifiers = modifiers; }
        }

        private Vector3 DrawPivotHandle(Vector3 center, Quaternion orientation, float sizeMultiplier = 1)
        {
            var next = DrawMoveHandle(center, orientation, sizeMultiplier);
            // The interactive rotation rings provide the pivot orientation marker.
            return next;
        }
        private static void DrawPivotMarker(Vector3 center, Quaternion orientation, float sizeMultiplier = 1)
        {
            if (Event.current.type != EventType.Repaint) return;
            float radius = HandleUtility.GetHandleSize(center) * .25f * sizeMultiplier;
            using (new Handles.DrawingScope())
            {
                Handles.color = Handles.xAxisColor;
                Handles.DrawWireDisc(center, orientation * Vector3.right, radius);
                Handles.color = Handles.yAxisColor;
                Handles.DrawWireDisc(center, orientation * Vector3.up, radius);
                Handles.color = Handles.zAxisColor;
                Handles.DrawWireDisc(center, orientation * Vector3.forward, radius);
            }
        }

        // Unity's native vertex snap uses the main editor selection, not our preview scene.
        private bool HandlePivotSnap(SceneView view)
        {
            if (LiveObjectTransformLocked) { EndPivotSnap(); return false; }
            var evt = Event.current;
            if (evt.type == EventType.KeyUp && evt.keyCode == pivotSnapKey)
            {
                bool held = pivotSnapHeld; pivotSnapHeld = false; edgeSnapHeld = false; edgeSnapLatched = false;
                if (held) { evt.Use(); repaint(); return true; }
            }
            bool objectMove = !pivot && mode == ComponentMode.Object && tool == Tool.Move && Document && Active >= 0;
            bool componentMove = !pivot && mode != ComponentMode.Object && tool == Tool.Move && CanAdjustPivot;
            if (!SplineInputActive && cutTool != CutTool.Polygon && !(pivot && CanAdjustPivot) && !objectMove && !componentMove)
            { EndPivotSnap(); return false; }
            int id = GUIUtility.GetControlID("MashBoxStudioPivotSnap".GetHashCode(), FocusType.Passive);
            if ((StudioShortcuts.Pressed(StudioShortcut.VertexSnap,evt) || StudioShortcuts.Pressed(StudioShortcut.EdgeSnap,evt)) && !EditorGUIUtility.editingTextField)
            { bool edge = StudioShortcuts.Pressed(StudioShortcut.EdgeSnap,evt); if (!pivotSnapHeld || edgeSnapHeld != edge) edgeSnapLatched = false; edgeSnapHeld = edge; pivotSnapHeld = true; pivotSnapKey = evt.keyCode; vertexSnapView = view; evt.Use(); repaint(); return true; }
            // Polygon placement consumes the shared hold state without moving existing geometry.
            if (SplineInputActive || cutTool == CutTool.Polygon) return false;
            if (pivotSnapControl != 0)
            {
                if (StudioShortcuts.Pressed(StudioShortcut.Cancel,evt))
                {
                    Undo.RevertAllDownToGroup(pivotSnapUndo); EndPivotSnap(); evt.Use(); repaint(); return true;
                }
                if (evt.type == EventType.MouseDrag && evt.button == pivotSnapButton)
                { if (pivotSnapHeld || (quickPivotSnap && gridSnapHeld)) SnapPivotAt(view, evt.mousePosition); evt.Use(); return true; }
                if (evt.type == EventType.MouseUp && evt.button == pivotSnapButton)
                { Undo.CollapseUndoOperations(pivotSnapUndo); EndPivotSnap(false); evt.Use(); repaint(); return true; }
            }
            bool quickSnap = evt.button == 2 && (pivotSnapHeld || gridSnapHeld);
            if ((!pivotSnapHeld && !quickSnap) || evt.type != EventType.MouseDown ||
                (evt.button != 0 && !quickSnap) || evt.alt || GUIUtility.hotControl != 0) return false;
            // Left-button axis/plane handles own their constrained drag. Middle-click
            // snaps from the current move pivot, independently of the hovered handle.
            if (!quickSnap && (IsMoveHandle(HandleUtility.nearestControl) || componentMove)) return false;
            if (view is MashBoxStudioViewport studio && (!studio.ModelCanvasRect.Contains(evt.mousePosition) || studio.IsOverViewportControls(evt.mousePosition))) return false;
            if (pivot && Document.IsBone(Active)) return false;
            snapMovesObjects = objectMove; snapMovesComponents = componentMove;
            pivotSnapButton = evt.button; quickPivotSnap = quickSnap;
            if (objectMove)
            {
                // Pick a source vertex once. Never retarget it as the object moves.
                // Starting from the move handle instead uses the object's pivot.
                var source = Transforms[Active].position;
                if (!quickSnap && TryPivotSnapVertex(view.camera, PivotCameraPoint(view, evt.mousePosition), EditorGUIUtility.pixelsPerPoint,
                    out var vertex, IsSnapMovingNode)) source = vertex;
                objectSnapOffset = source - Transforms[Active].position;
            }
            Undo.IncrementCurrentGroup(); pivotSnapUndo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(objectMove ? "Snap objects" : componentMove ? "Snap mesh components" : "Snap pivot");
            pivotSnapControl = id; GUIUtility.hotControl = id;
            SnapPivotAt(view, evt.mousePosition); evt.Use(); return true;
        }
        private void EndPivotSnap(bool releaseKey = true)
        {
            if (pivotSnapControl != 0 && GUIUtility.hotControl == pivotSnapControl) GUIUtility.hotControl = 0;
            pivotSnapControl = 0; pivotSnapUndo = -1; edgeSnapLatched = false;
            snapMovesObjects = snapMovesComponents = false; quickPivotSnap = false;
            if (releaseKey) { pivotSnapHeld = false; edgeSnapHeld = false; edgeSnapLatched = false; vertexSnapView = null; }
        }
        private void SnapPivotAt(SceneView view, Vector2 mouse)
        {
            if (snapMovesObjects || snapMovesComponents)
            {
                Vector3 destination = default;
                bool found = pivotSnapHeld ? TryMoveSnapTarget(view, mouse, out destination) :
                    quickPivotSnap && gridSnapHeld && TrySnapFreeMoveToGrid(HandleUtility.GUIPointToWorldRay(mouse), StudioSceneUnits.GridSpacing, out destination);
                if (found) Run(() => { if (snapMovesObjects) SnapObjectsTo(destination); else SnapComponentsTo(destination); });
                return;
            }
            Vector3 target;
            if (quickPivotSnap)
            {
                if (!TryQuickPivotSnapTarget(view, mouse, out target)) return;
            }
            else if (!(edgeSnapHeld ? TryEdgeSnapTarget(view, mouse, out target) : TryPivotSnapVertex(view.camera, PivotCameraPoint(view, mouse), EditorGUIUtility.pixelsPerPoint, out target))) return;
            if (mode != ComponentMode.Object && (EditingPivotPosition - target).sqrMagnitude < 1e-12f) return;
            Run(() => MoveEditingPivot(target));
        }
        private bool TryQuickPivotSnapTarget(SceneView view, Vector2 mouse, out Vector3 target)
        {
            // V is strictly vertex snapping, including when grid snap is also held.
            // No eligible vertex means no movement; never fall back to an edge or grid.
            if (edgeSnapHeld) return TryEdgeSnapTarget(view, mouse, out target);
            if (pivotSnapHeld)
                return TryPivotSnapVertex(view.camera, PivotCameraPoint(view, mouse), EditorGUIUtility.pixelsPerPoint, out target);
            target = default;
            return gridSnapHeld && TrySnapFreeMoveToGrid(HandleUtility.GUIPointToWorldRay(mouse), StudioSceneUnits.GridSpacing, out target);
        }
        private void SnapComponentsTo(Vector3 target)
        {
            var center = EditingPivotPosition;
            var delta = target - center;
            if (delta.sqrMagnitude < 1e-12f) return;
            var indices = SelectedVertices();
            if (indices.Count == 0) return;
            PreserveFaceTopology();
            Undo.RegisterCompleteObjectUndo(ActiveMesh, "Snap mesh components");
            var vertices = ActiveMesh.vertices;
            var transform = Transforms[Active];
            foreach (int i in indices)
                vertices[i] = transform.InverseTransformPoint(transform.TransformPoint(vertices[i]) + delta);
            ActiveMesh.vertices = vertices;
            RecalculateEditableNormals(ActiveMesh); ActiveMesh.RecalculateBounds();
            if (ActiveMesh.uv.Length == ActiveMesh.vertexCount) ActiveMesh.RecalculateTangents();
            EditorUtility.SetDirty(ActiveMesh);
            UpdateComponentPivotAfterTransform(target, center, Quaternion.identity, Quaternion.identity);
            PositionsChanged();
        }
        private bool IsSnapMovingNode(int node) => objects.Contains(node) || HasSelectedParent(node);
        internal bool TryObjectSnapTarget(Camera camera, Vector2 mouse, float pixelsPerPoint, out Vector3 target) =>
            TryPivotSnapVertex(camera, mouse, pixelsPerPoint, out target, node => !IsSnapMovingNode(node));

        private void SnapObjectsTo(Vector3 target)
        {
            var delta = target - (Transforms[Active].position + objectSnapOffset);
            if (delta.sqrMagnitude < 1e-12f) return;
            Undo.RegisterCompleteObjectUndo(Document, "Snap objects to vertex");
            foreach (int node in objects)
            {
                if (HasSelectedParent(node)) continue;
                Transforms[node].position += delta;
                Document.ReadTransform(node, Transforms[node]);
            }
            Changed(false);
        }
        private static Vector2 PivotCameraPoint(SceneView view, Vector2 guiPoint)
        {
            // Derive camera pixels from the actual SceneView ray. Screen/window
            // offsets include title bars and overlay margins and can shift the target.
            var ray = HandleUtility.GUIPointToWorldRay(guiPoint);
            var screen = view.camera.WorldToScreenPoint(ray.GetPoint(view.camera.nearClipPlane + 1));
            float dpi = EditorGUIUtility.pixelsPerPoint;
            return new Vector2(screen.x / dpi, (view.camera.pixelHeight - screen.y) / dpi);
        }
        internal bool TryPivotSnapVertex(Camera camera, Vector2 mouse, float pixelsPerPoint, out Vector3 target, System.Predicate<int> includeNode = null, System.Func<int, int, bool> includeVertex = null, System.Action<int, int> snapped = null)
        {
            target = default;
            if (!camera || !Document) return false;
            float bestDistance = 18 * 18, bestDepth = float.PositiveInfinity;
            bool found = false;
            for (int n = 0; n < Transforms.Length; n++)
            {
                if (includeNode != null && !includeNode(n)) continue;
                var mesh = Document.nodes[n].mesh;
                if (!mesh || !Transforms[n].gameObject.activeInHierarchy) continue;
                var renderer = Transforms[n].GetComponent<Renderer>();
                if (!renderer || !renderer.enabled) continue;
                var vertices = PickVertices(n);
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (includeVertex != null && !includeVertex(n, i)) continue;
                    var world = Transforms[n].TransformPoint(vertices[i]);
                    var screen = camera.WorldToScreenPoint(world);
                    if (screen.z <= camera.nearClipPlane) continue;
                    var gui = new Vector2(screen.x / pixelsPerPoint, (camera.pixelHeight - screen.y) / pixelsPerPoint);
                    float distance = (gui - mouse).sqrMagnitude;
                    if (distance > bestDistance || (Mathf.Abs(distance - bestDistance) < .01f && screen.z >= bestDepth)) continue;
                    bestDistance = distance; bestDepth = screen.z; target = world; found = true; snapped?.Invoke(n, i);
                }
            }
            return found;
        }
    }
}
