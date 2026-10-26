using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Undo = MashBoxSDK.AnimationStudio.StudioUndo;

namespace MashBoxSDK.AnimationStudio
{
    // Session-only selection data. Never snapshots mesh buffers or portable preferences.
    internal sealed class StudioSelectionUndoState : ScriptableObject
    {
        public int documentId, meshId;
        public int active = -1, mode;
        public int[] components = Array.Empty<int>(), objects = Array.Empty<int>();
        public StudioComponentSelectionRecord[] targets;
        internal static StudioSelectionUndoState Find(StudioModelDocument document, bool create)
        {
            if (!document) return null;
            string key = "MashBoxStudio.SelectionUndo." + document.GetInstanceID();
            var state = EditorUtility.InstanceIDToObject(SessionState.GetInt(key, 0)) as StudioSelectionUndoState;
            if (state && state.documentId == document.GetInstanceID()) return state;
            if (!create) return null;
            state = CreateInstance<StudioSelectionUndoState>();
            state.hideFlags = HideFlags.HideAndDontSave; state.documentId = document.GetInstanceID();
            SessionState.SetInt(key, state.GetInstanceID());
            return state;
        }
        internal static void ImportBaseline(StudioModelDocument document)
        {
            var state = Find(document, false);
            if (state) Undo.ClearUndo(state);
        }
    }

    internal sealed partial class StudioModelEditor
    {
        private sealed class SelectionSnapshot
        {
            internal StudioModelDocument document;
            internal Mesh mesh;
            internal int active;
            internal ComponentMode mode;
            internal int[] components, objects;
            internal StudioComponentSelectionRecord[] targets;
        }
        private int selectionTransactionDepth, selectionBaselineVersion;
        private void EstablishSelectionBaseline()
        {
            if (!Document) return;
            WriteSelection(StudioSelectionUndoState.Find(Document, true), CaptureSelection());
            selectionBaselineVersion++;
        }
        private SelectionSnapshot CaptureSelection() => new SelectionSnapshot {
            document = Document, mesh = ActiveMesh, active = Active, mode = mode,
            components = Components.ToArray(), objects = objects.ToArray(), targets = CaptureComponentTargets()
        };
        private bool SelectionMatches(SelectionSnapshot value) => value.document == Document &&
            value.active == Active && value.mode == mode && value.mesh == ActiveMesh &&
            Components.SetEquals(value.components) && objects.SetEquals(value.objects) && ComponentTargetsMatch(value.targets);
        private static void WriteSelection(StudioSelectionUndoState state, SelectionSnapshot value)
        {
            state.documentId = value.document.GetInstanceID(); state.meshId = value.mesh ? value.mesh.GetInstanceID() : 0; state.active = value.active;
            state.mode = (int)value.mode; state.components = value.components; state.objects = value.objects; state.targets=value.targets;
        }
        private void RecordSelectionChange(SelectionSnapshot before, bool separate = true, string label = "Select model components")
        {
            if (before == null || before.document != Document || !Document || SelectionMatches(before)) return;
            if (separate && selectionTransactionDepth != 0) return;
            var after = CaptureSelection();
            var state = StudioSelectionUndoState.Find(Document, true);
            if (separate) Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            WriteSelection(state, before);
            Undo.RegisterCompleteObjectUndo(state, label);
            WriteSelection(state, after);
            Undo.CollapseUndoOperations(group);
            if (separate) Undo.IncrementCurrentGroup();
            SelectionDisplayChanged();
        }
        private void SelectionDisplayChanged()
        {
            componentFrameValid = false;
            if (componentPivot) componentPivot.custom = false;
            cachedSelectedStatistics = null; InvalidateHandleComponentFrame();
            drawnMode = hoverMode = (ComponentMode)(-1);
            pivotTargetCache.Clear(); pivotHover = null;
            repaint(); toolOptions?.Repaint();
        }
        private void RestoreSelectionHistory()
        {
            var state = StudioSelectionUndoState.Find(Document, false);
            if (!state) return;
            // History restores selected IDs/modes, not ownership of GPU buffers
            // and topology caches. Mesh changes invalidate their affected caches.
            ClearAllComponentSelections(); objects.Clear();
            foreach (int i in state.objects) if (i >= 0 && i < Transforms.Length) objects.Add(i);
            RestoreComponentTargets(state.targets);
            Active = state.active >= 0 && state.active < Transforms.Length ? state.active : -1;
            mode = (ComponentMode)state.mode;
            Components.Clear();
            if (ActiveMesh && ActiveMesh.GetInstanceID() == state.meshId)
            {
                int count = ComponentCount();
                foreach (int i in state.components) if (i >= 0 && i < count) Components.Add(i);
            }
            SelectionDisplayChanged();
        }

        // One source selection and one history entry per held-key paint stroke.
        private SelectionSnapshot selectionPaintBefore;
        private SceneView selectionPaintView;
        private KeyCode selectionPaintKey;
        private Vector3[] selectionPaintVertices;
        private Vector2 selectionPaintPointer, selectionPaintPrevious, selectionPaintCanvasPointer;
        private bool selectionPaintErase, selectionPaintPending, selectionPaintHasPrevious;
        private bool selectionPaintArmed, selectionPaintArmErase, selectionPaintFinishPending;
        private SceneView selectionPaintArmView;
        private KeyCode selectionPaintArmKey;
        private int selectionPaintControl;
        private readonly System.Collections.Generic.List<Vector2> selectionPaintPath = new System.Collections.Generic.List<Vector2>(16);
        private void CancelMarqueeForSelectionPaint()
        {
            if (marquee)
            {
                marquee = false;
                if (GUIUtility.hotControl == marqueeControl) GUIUtility.hotControl = 0;
                marqueeControl = 0; additive = subtract = false;
                if (gestureUndo >= 0) { Undo.CollapseUndoOperations(gestureUndo); gestureUndo = -1; }
            }
        }
        private bool SelectionPaintInputHeld(SceneView view) =>
            (GUIUtility.hotControl == 0 || GUIUtility.hotControl == selectionPaintControl || GUIUtility.hotControl == marqueeControl) &&
            (selectionPaintArmed || selectionPaintBefore != null || (view is MashBoxStudioViewport studio && studio.SelectionPaintHoldActive));
        private static Vector2 PaintInputPosition(SceneView view) =>
            view is MashBoxStudioViewport studio && studio.CanvasMousePosition.HasValue
                ? studio.CanvasMousePosition.Value : Event.current.mousePosition;
        private bool PaintHoverVisible => selectionPaintArmed || selectionPaintBefore != null;
        private void UpdateSelectionPaintHover(SceneView view)
        {
            var evt = Event.current;
            if ((evt.type != EventType.Repaint && evt.type != EventType.MouseMove) || !PaintHoverVisible || !ActiveMesh) return;
            int previous = hoveredComponent;
            hoveredComponent = FindSelectionPaintComponent(view, PaintInputPosition(view));
            if (evt.type == EventType.MouseMove && previous != hoveredComponent)
            {
                if (view is MashBoxStudioViewport studio) studio.MarkDirty(MashBoxStudioViewport.DirtyState.Overlay);
                else view.Repaint();
            }
        }
        private bool BeginSelectionPaint(SceneView view, KeyCode key, bool erase)
        {
            if (selectionPaintBefore != null) return true;
            CancelMarqueeForSelectionPaint();
            if (mode == ComponentMode.Object || !ActiveMesh || Active < 0 || !Transforms[Active].gameObject.activeInHierarchy || !CanEditComponents() || pivot || pivotSnapHeld || markingOpen || cutTool != CutTool.None || marquee || GUIUtility.hotControl != 0) return false;
            FinishLiveTool();
            selectionPaintBefore = CaptureSelection(); selectionPaintView = view; selectionPaintKey = key;
            selectionPaintVertices = ActiveMesh.vertices;
            selectionPaintErase = erase; selectionPaintHasPrevious = false; selectionPaintPending = true;
            selectionPaintPointer = Event.current != null ? Event.current.mousePosition : Vector2.zero;
            selectionPaintCanvasPointer = PaintInputPosition(view);
            selectionPaintPath.Clear(); selectionPaintPath.Add(selectionPaintCanvasPointer);
            hoveredComponent = -1; repaint(); return true;
        }
        private void FinishSelectionPaint(bool cancel = false) => EndSelectionPaintStroke(cancel, false);
        private void EndSelectionPaintStroke(bool cancel, bool keepArmed)
        {
            if (!keepArmed) { selectionPaintArmed = false; selectionPaintArmView = null; selectionPaintArmKey = KeyCode.None; }
            if (selectionPaintControl != 0 && GUIUtility.hotControl == selectionPaintControl) GUIUtility.hotControl = 0;
            selectionPaintControl = 0; selectionPaintFinishPending = false; selectionPaintPath.Clear();
            var before = selectionPaintBefore;
            if (before == null) return;
            selectionPaintBefore = null; selectionPaintView = null; selectionPaintVertices = null;
            selectionPaintPending = selectionPaintHasPrevious = false; selectionPaintKey = KeyCode.None;
            if (cancel && before.document == Document && before.mesh == ActiveMesh && before.mode == mode && before.active == Active)
            {
                Components.Clear(); foreach (int component in before.components) Components.Add(component);
                SelectionDisplayChanged();
            }
            else RecordSelectionChange(before, label: selectionPaintErase ? "Unpaint selection" : "Paint selection");
        }
        private bool ApplySelectionPaintHit(int component)
        {
            if (selectionPaintBefore == null || component < 0 || component >= ComponentCount()) return false;
            return selectionPaintErase ? Components.Remove(component) : Components.Add(component);
        }
        private bool HandleSelectionPaintKey(SceneView view) => HandleSelectionPaintKey(view, Event.current, EditorWindow.mouseOverWindow == view);
        internal bool HandleSelectionPaintKey(SceneView view, Event evt, bool pointerInCanvas)
        {
            if (selectionPaintArmed || selectionPaintBefore != null)
            {
                var heldKey = selectionPaintArmed ? selectionPaintArmKey : selectionPaintKey;
                if (evt.type == EventType.KeyUp && evt.keyCode == heldKey)
                {
                    selectionPaintArmed = false; selectionPaintArmView = null; selectionPaintArmKey = KeyCode.None;
                    if (selectionPaintBefore != null && selectionPaintPending)
                    { selectionPaintFinishPending = true; repaint(); }
                    else FinishSelectionPaint();
                    evt.Use(); return true;
                }
                if (StudioShortcuts.Pressed(StudioShortcut.Cancel, evt))
                { FinishSelectionPaint(true); evt.Use(); return true; }
                bool modifier = evt.keyCode == KeyCode.LeftControl || evt.keyCode == KeyCode.RightControl || evt.keyCode == KeyCode.LeftCommand || evt.keyCode == KeyCode.RightCommand;
                if (modifier && heldKey == KeyCode.Tab)
                {
                    selectionPaintArmErase = selectionPaintErase = evt.type == EventType.KeyDown;
                    if (selectionPaintBefore != null) selectionPaintPending = true;
                    repaint(); evt.Use(); return true;
                }
                if (evt.type == EventType.KeyDown && evt.keyCode != heldKey) FinishSelectionPaint();
                else if (evt.keyCode == heldKey) { evt.Use(); return true; }
            }
            if (targetWeldActive) return false;
            bool erase = StudioShortcuts.Pressed(StudioShortcut.UnpaintSelection, evt);
            if (!erase && !StudioShortcuts.Pressed(StudioShortcut.PaintSelection, evt)) return false;
            if (!view || evt.alt || !pointerInCanvas) return false;
            var canvas = view is MashBoxStudioViewport studio ? studio.ModelCanvasRect : new Rect(Vector2.zero, view.position.size);
            if (!canvas.Contains(evt.mousePosition) || (view is MashBoxStudioViewport overlay && overlay.IsOverViewportControls(evt.mousePosition))) return false;
            if (mode == ComponentMode.Object || !ActiveMesh || !CanEditComponents() || pivot || pivotSnapHeld || markingOpen || cutTool != CutTool.None || (GUIUtility.hotControl != 0 && !marquee)) return false;
            selectionPaintArmed = true; selectionPaintArmView = view; selectionPaintArmKey = evt.keyCode; selectionPaintArmErase = erase;
            selectionPaintPointer = evt.mousePosition;
            evt.Use(); return true;
        }
        private void FinishSelectionPaintOutside(SceneView view)
        {
            if (!selectionPaintArmed && selectionPaintBefore == null) return;
            var evt = Event.current;
            bool pointer = evt.rawType == EventType.MouseMove || evt.rawType == EventType.MouseDrag || evt.rawType == EventType.MouseDown || evt.rawType == EventType.MouseUp;
            var canvas = view is MashBoxStudioViewport studio ? studio.ModelCanvasRect : new Rect(Vector2.zero, view.position.size);
            var canvasPointer = view is MashBoxStudioViewport viewport
                ? PaintInputPosition(view) + viewport.CanvasInputOrigin
                : evt.mousePosition;
            if (evt.rawType == EventType.MouseLeaveWindow || (pointer && (!canvas.Contains(canvasPointer) || (view is MashBoxStudioViewport overlay && overlay.IsOverViewportControls(canvasPointer)))))
                FinishSelectionPaint();
        }
        private bool BeforeSelectionPaintInput(SceneView view)
        {
            if (SelectionPaintInputHeld(view)) CancelMarqueeForSelectionPaint();
            FinishSelectionPaintOutside(view);
            if (!selectionPaintArmed && view is MashBoxStudioViewport heldView && heldView.SelectionPaintHoldActive)
                heldView.RearmSelectionPaintHold();
            if (!selectionPaintArmed)
            {
                var input = Event.current;
                if (SelectionPaintInputHeld(view) && input.isMouse && input.button == 0)
                { input.Use(); return true; }
                return false;
            }
            var evt = Event.current;
            int control = GUIUtility.GetControlID("StudioSelectionPaintDrag".GetHashCode(), FocusType.Passive);
            if (view != selectionPaintArmView || evt.alt) { FinishSelectionPaint(); return false; }
            if (evt.type == EventType.Layout && GUIUtility.hotControl == 0) HandleUtility.AddDefaultControl(control);
            bool down = evt.type == EventType.MouseDown && evt.button == 0;
            bool drag = evt.type == EventType.MouseDrag && evt.button == 0;
            bool up = evt.type == EventType.MouseUp && evt.button == 0;
            if (!down && !drag && !up) return false;
            if (selectionPaintBefore == null)
            {
                if (up || !BeginSelectionPaint(view, selectionPaintArmKey, selectionPaintArmErase)) { evt.Use(); return true; }
                selectionPaintControl = control; GUIUtility.hotControl = control;
            }
            if (GUIUtility.hotControl != selectionPaintControl) { FinishSelectionPaint(); evt.Use(); return true; }
            if (selectionPaintArmKey == KeyCode.Tab) selectionPaintArmErase = evt.control || evt.command;
            selectionPaintErase = selectionPaintArmErase;
            // Keep camera-canvas coordinates captured before SceneView changes GUI clips.
            // Projection and ray construction use the camera directly, not GUIClip.
            selectionPaintCanvasPointer = PaintInputPosition(view);
            if (selectionPaintPath.Count == 0 || (selectionPaintPath[selectionPaintPath.Count-1]-selectionPaintCanvasPointer).sqrMagnitude > .01f)
            {
                if (selectionPaintPath.Count < 16) selectionPaintPath.Add(selectionPaintCanvasPointer);
                else selectionPaintPath[15] = selectionPaintCanvasPointer;
            }
            selectionPaintPending = true;
            if (up) selectionPaintFinishPending = true;
            evt.Use(); repaint(); return true;
        }
        private bool SelectionPaintSceneGUI(SceneView view)
        {
            if (selectionPaintBefore == null) return false;
            if (view != selectionPaintView || selectionPaintBefore.document != Document || selectionPaintBefore.mesh != ActiveMesh || selectionPaintBefore.mode != mode || selectionPaintBefore.active != Active)
            { FinishSelectionPaint(); return false; }
            if (selectionPaintPending && Event.current.type == EventType.Repaint)
            {
                selectionPaintPending = false;
                if (selectionPaintPath.Count == 0) selectionPaintPath.Add(selectionPaintCanvasPointer);
                OcclusionMesh[] occluders = null; bool changed = false; int budget = 32;
                for (int p = 0; p < selectionPaintPath.Count; p++)
                {
                    selectionPaintPointer = selectionPaintPath[p];
                    var from = selectionPaintHasPrevious ? selectionPaintPrevious : selectionPaintPointer;
                    // Keep click/drag endpoints even when several events arrive before a frame.
                    // Bound interpolation work while preserving each queued pointer target.
                    int available = Mathf.Min(8, budget - (selectionPaintPath.Count-p-1));
                    int samples = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(from, selectionPaintPointer) / 6f), 1, available);
                    for (int i = 1; i <= samples; i++)
                        changed |= ApplySelectionPaintHit(FindSelectionPaintComponent(view, Vector2.Lerp(from, selectionPaintPointer, i / (float)samples), ref occluders));
                    budget -= samples; selectionPaintPrevious = selectionPaintPointer; selectionPaintHasPrevious = true;
                }
                selectionPaintPath.Clear();
                if (changed) SelectionDisplayChanged();
            }
            if (selectionPaintFinishPending && !selectionPaintPending) EndSelectionPaintStroke(false, selectionPaintArmed);
            return true;
        }
    }
}
