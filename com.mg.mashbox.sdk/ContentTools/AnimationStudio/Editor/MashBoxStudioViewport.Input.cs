using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class MashBoxStudioViewport
    {
        private sealed class InputHandler
        {
            internal Action original, wrapped;
        }
        private readonly Dictionary<IMGUIContainer,InputHandler> studioInputHandlers = new Dictionary<IMGUIContainer,InputHandler>();
        private Func<bool> studioInputActive;
        private Func<bool> studioKeyboard;
        private Action releaseStudioHolds;
        internal Vector2 CanvasInputOrigin { get; private set; }
        internal Vector2? CanvasMousePosition { get; private set; }
        private bool navigating;
        private StudioShortcut navigation;
        private int navigationControl, navigationButton;

        internal void BindModelInput(Func<bool> active, Func<bool> keyboard, Action releaseHolds)
        {
            studioInputActive = active; studioKeyboard = keyboard; releaseStudioHolds = releaseHolds;
            EditorApplication.update -= TickStudioInput;
            StudioShortcuts.changed -= ReleaseStudioInput;
            UnbindSelectionPaintInput();
            if (active == null) { RestoreStudioInput(); return; }
            BindSelectionPaintInput();
            EditorApplication.update += TickStudioInput;
            StudioShortcuts.changed += ReleaseStudioInput;
            foreach (var container in StudioChromeRoot.Query<IMGUIContainer>().ToList())
            {
                if (container.onGUIHandler == null) continue;
                if (studioInputHandlers.TryGetValue(container,out var previous) && container.onGUIHandler == previous.wrapped) continue;
                var handler = new InputHandler { original = container.onGUIHandler };
                // SceneView's native camera IMGUI container owns its main OnGUI callback.
                bool canvas = ReferenceEquals(handler.original.Target,this) &&
                    (handler.original.Method.DeclaringType == typeof(SceneView) || handler.original.Method.Name == nameof(OnSceneGUI));
                if(!canvas)BindStudioHoverRedraw(container);
                handler.wrapped = () => {
                    var inputEvent = Event.current;
                    if (canvas)
                    {
                        // Capture before SceneView changes its GUI clip/matrix for callbacks.
                        CanvasInputOrigin = rootVisualElement.WorldToLocal(container.LocalToWorld(Vector2.zero));
                        if (inputEvent.isMouse) CanvasMousePosition = inputEvent.mousePosition;
                    }
                    bool menuBlocked = container != modelMenuBar && container != modeSelector && MenuInputBlocked;
                    Vector2 mousePosition = inputEvent.mousePosition;
                    if (menuBlocked)
                    {
                        // IMGUI derives button hover from mousePosition during Repaint,
                        // even when its mouse events have already been consumed.
                        inputEvent.mousePosition = new Vector2(-100000,-100000);
                        if (inputEvent.isMouse || inputEvent.isKey || inputEvent.type == EventType.ScrollWheel || inputEvent.type == EventType.ContextClick || inputEvent.type == EventType.DragUpdated || inputEvent.type == EventType.DragPerform)
                            inputEvent.Use();
                    }
                    if (!menuBlocked && studioInputActive != null && studioInputActive())
                    {
                        if (canvas)
                        {
                            var evt = Event.current;
                            bool pointer = evt.isMouse || evt.type == EventType.ScrollWheel || evt.type == EventType.ContextClick || evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform;
                            bool toolbar = IsOverViewportControls(evt.mousePosition);
                            if (pointer && toolbar) { releaseStudioHolds?.Invoke(); evt.Use(); }
                            else RouteStudioCanvasInput();
                        }
                        else if (Event.current.isKey && !EditorGUIUtility.editingTextField && !(resizingTools && container == toolOptionsDivider)) studioKeyboard?.Invoke();
                    }
                    try { handler.original(); }
                    finally { if (menuBlocked) inputEvent.mousePosition = mousePosition; }
                    // Panels get their text-field and rename handling first. Everything else
                    // stays in Studio instead of falling through to Unity's global shortcuts.
                    if (studioInputActive != null && studioInputActive() && Event.current.isKey)
                        Event.current.Use();
                };
                studioInputHandlers[container] = handler;
                container.onGUIHandler = handler.wrapped;
            }
        }
        private KeyCode selectionPaintHeldKey;
        private bool selectionPaintHoldCancelled;
        internal bool SelectionPaintHoldActive => selectionPaintHeldKey != KeyCode.None;
        internal void RearmSelectionPaintHold()
        {
            if (SelectionPaintHoldActive && !selectionPaintHoldCancelled)
                DispatchSelectionPaintKey(EventType.KeyDown, selectionPaintHeldKey, Event.current.modifiers);
        }
        private Vector2 selectionPaintInputPointer;
        private bool selectionPaintInputInside;
        private void BindSelectionPaintInput()
        {
            rootVisualElement.RegisterCallback<KeyDownEvent>(SelectionPaintKeyDown, TrickleDown.TrickleDown);
            rootVisualElement.RegisterCallback<KeyUpEvent>(SelectionPaintKeyUp, TrickleDown.TrickleDown);
            rootVisualElement.RegisterCallback<PointerMoveEvent>(SelectionPaintPointerMove, TrickleDown.TrickleDown);
            rootVisualElement.RegisterCallback<PointerLeaveEvent>(SelectionPaintPointerLeave);
        }
        private void UnbindSelectionPaintInput()
        {
            rootVisualElement.UnregisterCallback<KeyDownEvent>(SelectionPaintKeyDown, TrickleDown.TrickleDown);
            rootVisualElement.UnregisterCallback<KeyUpEvent>(SelectionPaintKeyUp, TrickleDown.TrickleDown);
            rootVisualElement.UnregisterCallback<PointerMoveEvent>(SelectionPaintPointerMove, TrickleDown.TrickleDown);
            rootVisualElement.UnregisterCallback<PointerLeaveEvent>(SelectionPaintPointerLeave);
            selectionPaintInputInside = false;
        }
        private void SelectionPaintPointerMove(PointerMoveEvent evt)
        {
            selectionPaintInputPointer = rootVisualElement.WorldToLocal(evt.position);
            selectionPaintInputInside = ModelCanvasRect.Contains(selectionPaintInputPointer) && !IsOverViewportControls(selectionPaintInputPointer);
            if (!selectionPaintInputInside) releaseStudioHolds?.Invoke();
        }
        private void SelectionPaintPointerLeave(PointerLeaveEvent evt)
        {
            if (evt.target != rootVisualElement) return;
            // IMGUI mouse capture can retarget pointer events without leaving the window.
            // Only a pointer outside the root bounds represents an actual leave.
            if (rootVisualElement.contentRect.Contains(rootVisualElement.WorldToLocal(evt.position))) return;
            selectionPaintInputInside = false; releaseStudioHolds?.Invoke();
        }
        private bool DispatchSelectionPaintKey(EventType type, KeyCode keyCode, EventModifiers modifiers)
        {
            if (type == EventType.KeyUp && keyCode == selectionPaintHeldKey)
            { selectionPaintHeldKey = KeyCode.None; selectionPaintHoldCancelled = false; }
            if (studioInputActive == null || !studioInputActive() || MenuInputBlocked || EditorGUIUtility.editingTextField) return false;
            var editor = Studio?.ToolbarModelEditor;
            if (editor == null) return false;
            var key = new Event { type = type, keyCode = keyCode, modifiers = modifiers, mousePosition = selectionPaintInputPointer };
            if (type == EventType.KeyDown)
            {
                if (SelectionPaintHoldActive && StudioShortcuts.Pressed(StudioShortcut.Cancel, key)) selectionPaintHoldCancelled = true;
                if (editor.ToolbarComponentMode != StudioModelEditor.ComponentMode.Object &&
                    (StudioShortcuts.Pressed(StudioShortcut.PaintSelection, key) || StudioShortcuts.Pressed(StudioShortcut.UnpaintSelection, key)))
                {
                    if (selectionPaintHeldKey != keyCode) selectionPaintHoldCancelled = false;
                    selectionPaintHeldKey = keyCode;
                }
            }
            bool handled = editor.HandleSelectionPaintKey(this, key, selectionPaintInputInside);
            return handled || (type == EventType.KeyDown && selectionPaintHeldKey == keyCode);
        }
        private void SelectionPaintKeyDown(KeyDownEvent evt)
        {
            if (!DispatchSelectionPaintKey(EventType.KeyDown, evt.keyCode, evt.modifiers)) return;
            evt.imguiEvent?.Use(); evt.StopImmediatePropagation();
        }
        private void SelectionPaintKeyUp(KeyUpEvent evt)
        {
            if (!DispatchSelectionPaintKey(EventType.KeyUp, evt.keyCode, evt.modifiers)) return;
            evt.imguiEvent?.Use(); evt.StopImmediatePropagation();
        }
        private void RestoreStudioInput()
        {
            UnbindSelectionPaintInput();
            ReleaseStudioInput();
            foreach (var pair in studioInputHandlers)
            {
                UnbindStudioHoverRedraw(pair.Key);
                if (pair.Key.onGUIHandler == pair.Value.wrapped) pair.Key.onGUIHandler = pair.Value.original;
            }
            studioInputHandlers.Clear();
        }
        private static void DirtyStudioHover<T>(T evt) where T:EventBase<T>,new()
        {
            // Moving between controls inside one IMGUI container doesn't change
            // the retained element under the pointer. Explicitly invalidate its
            // hover paint; entry/exit alone only updates the first/last item.
            (evt.currentTarget as IMGUIContainer)?.MarkDirtyRepaint();
        }
        private static void BindStudioHoverRedraw(IMGUIContainer control)
        {
            UnbindStudioHoverRedraw(control);
            control.RegisterCallback<PointerMoveEvent>(DirtyStudioHover,TrickleDown.TrickleDown);
            control.RegisterCallback<PointerEnterEvent>(DirtyStudioHover,TrickleDown.TrickleDown);
            control.RegisterCallback<PointerLeaveEvent>(DirtyStudioHover,TrickleDown.TrickleDown);
        }
        private static void UnbindStudioHoverRedraw(IMGUIContainer control)
        {
            control.UnregisterCallback<PointerMoveEvent>(DirtyStudioHover,TrickleDown.TrickleDown);
            control.UnregisterCallback<PointerEnterEvent>(DirtyStudioHover,TrickleDown.TrickleDown);
            control.UnregisterCallback<PointerLeaveEvent>(DirtyStudioHover,TrickleDown.TrickleDown);
        }
        private void ReleaseStudioInput()
        {
            if (navigating && GUIUtility.hotControl == navigationControl) GUIUtility.hotControl = 0;
            selectionPaintHeldKey = KeyCode.None; selectionPaintHoldCancelled = false;
            navigating = false; releaseStudioHolds?.Invoke();
        }
        private void TickStudioInput()
        {
            if (focusedWindow != this || studioInputActive == null || !studioInputActive()) ReleaseStudioInput();
        }
        public override void OnDisable()
        {
            SaveStoredConfiguration();
            StudioPreferenceStore.Flush();
            ClearStatusMessages();
            ReleaseStudioToolbars();
            ReleaseViewportRedraw();
            ClearMenuInputShield();
            StopWorkspaceStatistics();
            EditorApplication.update -= TickStudioInput;
            StudioShortcuts.changed -= ReleaseStudioInput;
            RestoreStudioInput();
            base.OnDisable();
        }
        private void RouteStudioCanvasInput()
        {
            var evt = Event.current;
            int control = GUIUtility.GetControlID("MashBoxStudioNavigation".GetHashCode(),FocusType.Passive);
            if (evt.isKey && !EditorGUIUtility.editingTextField)
            {
                if (navigating && StudioShortcuts.Pressed(StudioShortcut.Cancel,evt)) ReleaseStudioInput();
                else studioKeyboard?.Invoke();
                if (evt.isKey) evt.Use();
                return;
            }
            if (navigating)
            {
                if (GUIUtility.hotControl != navigationControl) { ReleaseStudioInput(); return; }
                if (evt.type == EventType.MouseDrag)
                { ApplyStudioNavigation(navigation,navigation == StudioShortcut.Dolly ? -evt.delta : evt.delta); evt.Use(); MarkDirty(DirtyState.Camera); return; }
                if (evt.type == EventType.MouseUp && evt.button == navigationButton)
                { ReleaseStudioInput(); evt.Use(); MarkDirty(DirtyState.Overlay); return; }
            }
            // Defer plain middle mouse to the modeling tools. Their SceneGUI runs
            // with the camera configured and consumes unused middle-button events.
            // An Alt-pan already in progress retains its gesture until mouse-up.
            if (!navigating && evt.isMouse && evt.button == 2 && !evt.alt) return;
            bool canvas = ModelCanvasRect.Contains(evt.mousePosition) && !IsOverViewportControls(evt.mousePosition);
            if (!canvas) return;
            if (Studio?.ToolbarModelEditor?.WantsLoopCountWheel(evt) == true) return;
            if (evt.type == EventType.ScrollWheel && GUIUtility.hotControl == 0)
            { ApplyStudioNavigation(StudioShortcut.Dolly,new Vector2(0,evt.delta.y*8)); evt.Use(); MarkDirty(DirtyState.Camera); return; }
            if (evt.type == EventType.MouseDown && GUIUtility.hotControl == 0)
            {
                foreach (var action in new[] { StudioShortcut.Orbit, StudioShortcut.Pan, StudioShortcut.Dolly })
                    if (StudioShortcuts.Matches(action,evt))
                    {
                        navigating = true; navigation = action; navigationButton = evt.button;
                        navigationControl = control; GUIUtility.hotControl = control; Focus(); evt.Use(); return;
                    }
            }
            // Mouse gestures are owned here too; changing Unity's navigation profile
            // cannot reintroduce the old bindings after a Studio gesture is remapped.
            if (evt.isMouse && (evt.alt || evt.button != 0) &&
                !StudioShortcuts.Matches(StudioShortcut.MarkingMenu,evt) &&
                !StudioShortcuts.Matches(StudioShortcut.AxisMenu,evt) && GUIUtility.hotControl == 0) evt.Use();
        }
        internal void ApplyStudioNavigation(StudioShortcut action, Vector2 delta)
        {
            delta *= NavigationSensitivity;
            if (action == StudioShortcut.Orbit)
            {
                if (in2DMode || isRotationLocked) return;
                rotation = Quaternion.AngleAxis(delta.x*.25f,Vector3.up)*rotation*Quaternion.AngleAxis(delta.y*.25f,Vector3.right);
            }
            else if (action == StudioShortcut.Pan)
            {
                float units = 2*Mathf.Max(.0001f,size)/Mathf.Max(1,camera.pixelHeight/EditorGUIUtility.pixelsPerPoint);
                pivot += rotation*new Vector3(-delta.x*units,delta.y*units,0);
            }
            else size = Mathf.Clamp(size*Mathf.Exp((delta.x+delta.y)*.01f),.0001f,1000000);
        }
    }
}
