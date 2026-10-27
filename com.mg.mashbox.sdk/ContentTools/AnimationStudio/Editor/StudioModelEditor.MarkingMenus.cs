using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal enum MarkingLevel { Components, Transform, Pivot, Selection, UV, UVSets, AxisSpace }
        internal enum MarkingAction
        {
            None, Object, Vertex, Edge, Face, TransformMenu, PivotMenu, SelectionMenu, UVMenu, UVSetsMenu,
            Move, Rotate, Scale, Axes, EditPivot, CenterPivot, Freeze, SelectAll, Clear, Frame, Through,
            OpenUV, UV0, UV1, UV2, UV3, UV4, UV5, UV6, UV7, ObjectAxes, WorldAxes, ComponentAxes
        }
        // Clockwise, starting north. Empty sectors do not perform an action.
        private static readonly MarkingAction[][] MarkingItems =
        {
            new[] { MarkingAction.Edge, MarkingAction.Object, MarkingAction.UVMenu, MarkingAction.None,
                MarkingAction.Face, MarkingAction.None, MarkingAction.Vertex, MarkingAction.PivotMenu },
            new[] { MarkingAction.Rotate, MarkingAction.None, MarkingAction.Scale, MarkingAction.None,
                MarkingAction.Axes, MarkingAction.None, MarkingAction.Move, MarkingAction.None },
            new[] { MarkingAction.EditPivot, MarkingAction.None, MarkingAction.CenterPivot, MarkingAction.None,
                MarkingAction.Freeze, MarkingAction.None, MarkingAction.None, MarkingAction.None },
            new[] { MarkingAction.SelectAll, MarkingAction.None, MarkingAction.Frame, MarkingAction.None,
                MarkingAction.Clear, MarkingAction.None, MarkingAction.Through, MarkingAction.None },
            new[] { MarkingAction.OpenUV, MarkingAction.None, MarkingAction.UVSetsMenu, MarkingAction.None,
                MarkingAction.None, MarkingAction.None, MarkingAction.None, MarkingAction.None },
            new[] { MarkingAction.UV0, MarkingAction.UV1, MarkingAction.UV2, MarkingAction.UV3,
                MarkingAction.UV4, MarkingAction.UV5, MarkingAction.UV6, MarkingAction.UV7 },
            new[] { MarkingAction.ComponentAxes, MarkingAction.None, MarkingAction.WorldAxes, MarkingAction.None,
                MarkingAction.None, MarkingAction.None, MarkingAction.ObjectAxes, MarkingAction.None }
        };
        private static readonly Vector2[] MarkingOffsets =
        {
            new Vector2(0, -54), new Vector2(92, -28), new Vector2(106, 0), new Vector2(92, 28),
            new Vector2(0, 54), new Vector2(-92, 28), new Vector2(-106, 0), new Vector2(-92, -28)
        };
        private struct MarkingParent { internal MarkingLevel Level; internal Vector2 Origin; }
        private readonly List<MarkingParent> markingParents = new List<MarkingParent>(4);
        private int markingButton;
        private bool markingOpen, suppressModelContext;
        private SceneView markingView;
        private MarkingLevel markingLevel;
        private Vector2 markingOrigin, markingPointer, markingAnchor;
        private int markingControl, markingHover = -1;
        private double markingHoverSince;
        private GUIStyle markingLabel;
        private readonly bool[] markingEnabled = new bool[8];

        internal static int MarkingSector(Vector2 delta)
        {
            if (delta.sqrMagnitude < 24 * 24) return -1;
            float angle = Mathf.Atan2(delta.x, -delta.y) * Mathf.Rad2Deg;
            return (Mathf.FloorToInt((angle + 22.5f + 360) / 45)) % 8;
        }
        private static bool IsMarkingBranch(MarkingAction action) => action >= MarkingAction.TransformMenu && action <= MarkingAction.UVSetsMenu;
        private static MarkingLevel BranchLevel(MarkingAction action)
        {
            switch (action)
            {
                case MarkingAction.TransformMenu: return MarkingLevel.Transform;
                case MarkingAction.PivotMenu: return MarkingLevel.Pivot;
                case MarkingAction.SelectionMenu: return MarkingLevel.Selection;
                case MarkingAction.UVMenu: return MarkingLevel.UV;
                default: return MarkingLevel.UVSets;
            }
        }
        private bool MarkingActionEnabled(MarkingAction action)
        {
            if (action == MarkingAction.None) return false;
            if (action == MarkingAction.ObjectAxes || action == MarkingAction.WorldAxes || action == MarkingAction.ComponentAxes) return true;
            if (action == MarkingAction.Object || action == MarkingAction.TransformMenu || action == MarkingAction.SelectionMenu ||
                action == MarkingAction.Move || action == MarkingAction.Rotate || action == MarkingAction.Scale || action == MarkingAction.Axes || action == MarkingAction.Through) return true;
            if (action == MarkingAction.Vertex || action == MarkingAction.Edge || action == MarkingAction.Face)
                return true;
            if (action == MarkingAction.UVMenu || action == MarkingAction.UVSetsMenu || action == MarkingAction.OpenUV || (action >= MarkingAction.UV0 && action <= MarkingAction.UV7))
                return true;
            if (action == MarkingAction.PivotMenu || action == MarkingAction.EditPivot || action == MarkingAction.CenterPivot)
                return CanAdjustPivot;
            if (action == MarkingAction.Freeze)
                return Document && Active >= 0 && mode == ComponentMode.Object && !Document.IsBone(Active);
            return Document && (mode == ComponentMode.Object || ActiveMesh);
        }
        private string MarkingText(MarkingAction action)
        {
            switch (action)
            {
                case MarkingAction.Object: return "Object Mode";
                case MarkingAction.ObjectAxes: return "Object";
                case MarkingAction.ComponentAxes: return "Component";
                case MarkingAction.WorldAxes: return "World";
                case MarkingAction.TransformMenu: return "Transform ›";
                case MarkingAction.PivotMenu: return "Pivot ›";
                case MarkingAction.SelectionMenu: return "Selection ›";
                case MarkingAction.UVMenu: return "UV ›";
                case MarkingAction.UVSetsMenu: return "UV Sets ›";
                case MarkingAction.EditPivot: return pivot ? "Finish Pivot Edit" : "Edit Pivot";
                case MarkingAction.CenterPivot: return "Center Pivot";
                case MarkingAction.Freeze: return "Freeze Transforms";
                case MarkingAction.SelectAll: return "Select All";
                case MarkingAction.Clear: return "Deselect";
                case MarkingAction.Axes: return local ? "World Axes" : "Object Axes";
                case MarkingAction.Through: return xray ? "Visible Only" : "Through Selection";
                case MarkingAction.OpenUV: return "UV Editor";
                default: return action >= MarkingAction.UV0 && action <= MarkingAction.UV7 ? "UV " + (action - MarkingAction.UV0) : action.ToString();
            }
        }
        private bool MarkingIsCurrent(MarkingAction action) =>
            (action == MarkingAction.Object && mode == ComponentMode.Object) ||
            (action == MarkingAction.Vertex && mode == ComponentMode.Vertex) ||
            (action == MarkingAction.Edge && mode == ComponentMode.Edge) ||
            (action == MarkingAction.Face && mode == ComponentMode.Face) ||
            (action == MarkingAction.Move && tool == Tool.Move) ||
            (action == MarkingAction.Rotate && tool == Tool.Rotate) ||
            (action == MarkingAction.Scale && tool == Tool.Scale) ||
            (action == MarkingAction.ObjectAxes && local && !componentAxes) ||
            (action == MarkingAction.ComponentAxes && ToolbarComponentAxes) || (action == MarkingAction.WorldAxes && !local);

        // Called before SceneView gets RMB, so its fly camera and native context menu
        // cannot claim the gesture. Alt navigation and existing handle drags pass through.
        internal void BeforeModelSceneGUI(SceneView view)
        {
            view.wantsMouseEnterLeaveWindow = true;
            if (HandleLoopCountWheel(view, Event.current)) return;
            if (BeforeSelectionPaintInput(view)) return;
            if (CancelMarqueeOutside(view)) return;
            // Some native handles consume MouseUp before our normal scene callback.
            // End its Undo group here so the next pivot/transform drag starts fresh.
            if (Event.current.rawType == EventType.MouseUp && Event.current.button == 0 && gestureUndo >= 0)
            { Undo.CollapseUndoOperations(gestureUndo); gestureUndo = -1; }
            if (HandleModelKeyboard(view)) return;
            int id = GUIUtility.GetControlID("MashBoxStudioMarkingMenu".GetHashCode(), FocusType.Passive);
            // Space selection remains available in a cut tool without applying its pending path.
            if (markingOpen || (Event.current.type == EventType.MouseDown && StudioShortcuts.Matches(StudioShortcut.AxisMenu,Event.current)))
            {
                HandleModelMarkingEvent(view,Event.current,id);
                if (markingOpen || Event.current.type == EventType.Used) return;
            }
            if (cutTool != CutTool.None)
            {
                var evt = Event.current;
                if (evt.type == EventType.MouseDown && StudioShortcuts.Matches(StudioShortcut.MarkingMenu,evt) &&
                    (!(view is MashBoxStudioViewport studio) || studio.ModelCanvasRect.Contains(evt.mousePosition)))
                {
                    Run(() => CommitCuts(new List<StudioMeshCuts.Cut>(pendingCuts)));
                    ResetCutGesture(); suppressModelContext = true; evt.Use(); repaint();
                }
                else if (evt.type == EventType.ContextClick) evt.Use();
                return;
            }
            HandleModelMarkingEvent(view, Event.current, id);
        }
        internal void HandleModelMarkingEvent(SceneView view, Event evt, int id)
        {
            if (evt.type == EventType.ContextClick && (markingOpen || suppressModelContext))
            { suppressModelContext = false; evt.Use(); return; }
            if (!markingOpen)
            {
                if (evt.type != EventType.MouseDown) return;
                suppressModelContext = false;
                bool axes = StudioShortcuts.Matches(StudioShortcut.AxisMenu,evt);
                if ((!axes && !StudioShortcuts.Matches(StudioShortcut.MarkingMenu,evt)) || GUIUtility.hotControl != 0) return;
                // Leave floating viewport controls available for their own input.
                if (view is MashBoxStudioViewport overlayView && overlayView.IsOverViewportControls(evt.mousePosition)) return;
                var canvas = view is MashBoxStudioViewport studio ? studio.ModelCanvasRect : new Rect(276, 68, Mathf.Max(0, view.position.width - 674), Mathf.Max(0, view.position.height - 76));
                if (!canvas.Contains(evt.mousePosition)) return;
                markingModeTarget = axes ? -1 : MeshAtModePointer(view,evt.mousePosition);
                markingView = view; markingButton = evt.button; markingControl = id; GUIUtility.hotControl = id;
                GUIUtility.keyboardControl = 0; EditorGUIUtility.editingTextField = false;
                markingOpen = true; suppressModelContext = true; markingParents.Clear();
                markingPointer = GUIUtility.GUIToScreenPoint(evt.mousePosition);
                SetMarkingLevel(axes ? MarkingLevel.AxisSpace : MarkingLevel.Components, markingPointer);
                evt.Use(); repaint(); return;
            }
            if (view != markingView) return;
            if (StudioShortcuts.Pressed(StudioShortcut.Cancel,evt))
            { CancelMarkingMenu(); evt.Use(); return; }
            if (evt.type == EventType.MouseLeaveWindow || GUIUtility.hotControl != markingControl)
            { CancelMarkingMenu(); return; }
            if (evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove || (evt.type == EventType.MouseUp && evt.button == markingButton))
            {
                markingPointer = GUIUtility.GUIToScreenPoint(evt.mousePosition);
                UpdateMarkingHover();
                if (evt.type == EventType.MouseUp)
                {
                    var action = markingHover >= 0 ? MarkingItems[(int)markingLevel][markingHover] : MarkingAction.None;
                    bool execute = markingHover >= 0 && markingEnabled[markingHover] && !IsMarkingBranch(action);
                    CancelMarkingMenu(); evt.Use();
                    if (execute) { int target=markingModeTarget;Run(() => ExecuteModeTargetAction(action, view,target)); }
                }
                else
                {
                    if (markingParents.Count > 0 && MarkingBackRect(markingOrigin).Contains(markingPointer)) ReturnMarkingParent();
                    else if (markingHover >= 0 && markingEnabled[markingHover] && IsMarkingBranch(MarkingItems[(int)markingLevel][markingHover]) &&
                        MarkingItemRect(markingOrigin, markingHover).Contains(markingPointer)) EnterMarkingBranch();
                    evt.Use();
                }
                repaint(); return;
            }
            if (evt.isKey || evt.type == EventType.ScrollWheel || evt.type == EventType.MouseDown || evt.type == EventType.MouseUp) evt.Use();
        }
        private void SetMarkingLevel(MarkingLevel level, Vector2 origin)
        {
            markingLevel = level;
            markingAnchor = markingPointer;
            // Keep all branches visible even when the gesture starts near a window edge.
            var bounds = markingView.position;
            markingOrigin = new Vector2(Mathf.Clamp(origin.x, bounds.xMin + 170, bounds.xMax - 170), Mathf.Clamp(origin.y, bounds.yMin + 112, bounds.yMax - 74));
            markingHover = -1; markingHoverSince = EditorApplication.timeSinceStartup;
            for (int i = 0; i < 8; i++) markingEnabled[i] = MarkingActionEnabled(MarkingItems[(int)level][i]);
        }
        private Rect MarkingItemRect(Vector2 origin, int sector)
        {
            var action = MarkingItems[(int)markingLevel][sector];
            float width = action == MarkingAction.Freeze || action == MarkingAction.Through ? 112 : 94;
            return new Rect(origin + MarkingOffsets[sector] - new Vector2(width * .5f, 10), new Vector2(width, 20));
        }
        private static Rect MarkingBackRect(Vector2 origin) => new Rect(origin + new Vector2(-36, -94), new Vector2(72, 20));
        private void UpdateMarkingHover()
        {
            int next = -1;
            bool neutral = (markingPointer - markingAnchor).sqrMagnitude < 24 * 24 ||
                (markingPointer - markingOrigin).sqrMagnitude < 24 * 24 ||
                (markingParents.Count > 0 && MarkingBackRect(markingOrigin).Contains(markingPointer));
            if (!neutral)
            {
                for (int i = 0; i < 8; i++) if (MarkingItems[(int)markingLevel][i] != MarkingAction.None && MarkingItemRect(markingOrigin, i).Contains(markingPointer)) { next = i; break; }
                if (next < 0) next = MarkingSector(markingPointer - markingAnchor);
            }
            if (next >= 0 && MarkingItems[(int)markingLevel][next] == MarkingAction.None) next = -1;
            if (next != markingHover) { markingHover = next; markingHoverSince = EditorApplication.timeSinceStartup; }
        }
        private void EnterMarkingBranch()
        {
            if (markingHover < 0 || !markingEnabled[markingHover]) return;
            var action = MarkingItems[(int)markingLevel][markingHover]; if (!IsMarkingBranch(action)) return;
            markingParents.Add(new MarkingParent { Level = markingLevel, Origin = markingOrigin });
            SetMarkingLevel(BranchLevel(action), markingPointer); repaint();
        }
        private void ReturnMarkingParent()
        {
            var previous = markingParents[markingParents.Count - 1]; markingParents.RemoveAt(markingParents.Count - 1);
            SetMarkingLevel(previous.Level, previous.Origin); repaint();
        }
        private void TickMarkingMenu()
        {
            if (markingOpen && markingHover >= 0 && markingEnabled[markingHover] &&
                IsMarkingBranch(MarkingItems[(int)markingLevel][markingHover]) && EditorApplication.timeSinceStartup - markingHoverSince >= .22)
                EnterMarkingBranch();
        }
        internal void CancelMarkingMenu()
        {
            if (!markingOpen) return;
            if (GUIUtility.hotControl == markingControl) GUIUtility.hotControl = 0;
            markingOpen = false; markingView = null; markingHover = -1; markingParents.Clear(); repaint();
        }
        internal void DrawMarkingMenu()
        {
            if (!markingOpen || Event.current.type != EventType.Repaint) return;
            if (markingLabel == null)
            {
                markingLabel = new GUIStyle(StudioTheme.Styles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };
            }
            DrawMarkingDragLine();
            var origin = GUIUtility.ScreenToGUIPoint(markingOrigin);
            var palette = StudioTheme.Current;
            for (int i = 0; i < 8; i++)
            {
                var action = MarkingItems[(int)markingLevel][i]; if (action == MarkingAction.None) continue;
                var rect = MarkingItemRect(origin, i); bool enabled = markingEnabled[i];
                EditorGUI.DrawRect(new Rect(rect.x - 1, rect.y - 1, rect.width + 2, rect.height + 2), palette.Header);
                EditorGUI.DrawRect(rect, markingHover == i && enabled ? palette.Hover : palette.Button);
                markingLabel.normal.textColor = enabled ? palette.Text : palette.Muted;
                if (action == MarkingAction.ObjectAxes || action == MarkingAction.WorldAxes || action == MarkingAction.ComponentAxes)
                {
                    GUI.Toggle(new Rect(rect.x+6,rect.center.y-8,16,16),MarkingIsCurrent(action),GUIContent.none,EditorStyles.toggle);
                    GUI.Label(new Rect(rect.x+22,rect.y,rect.width-24,rect.height),MarkingText(action),markingLabel);
                }
                else GUI.Label(rect, MarkingText(action), markingLabel);
            }
            if (markingParents.Count > 0)
            {
                var back = MarkingBackRect(origin);
                EditorGUI.DrawRect(new Rect(back.x - 1, back.y - 1, back.width + 2, back.height + 2), palette.Header);
                EditorGUI.DrawRect(back, palette.Button);
                markingLabel.normal.textColor = palette.Text;
                GUI.Label(back, "‹ Back", markingLabel);
            }
        }
        private void DrawMarkingDragLine()
        {
            // Input is captured in the SceneView; this overlay has its own GUI origin.
            var start = GUIUtility.ScreenToGUIPoint(markingAnchor);
            var end = GUIUtility.ScreenToGUIPoint(markingPointer);
            var delta = end - start;
            if (delta.sqrMagnitude < 1) return;
            var previousMatrix = GUI.matrix;
            try
            {
                GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
                // A dark edge keeps the light gesture line visible over both meshes and empty space.
                EditorGUI.DrawRect(new Rect(start.x, start.y - 2, delta.magnitude, 4), new Color(.08f, .08f, .08f, .9f));
                EditorGUI.DrawRect(new Rect(start.x, start.y - 1, delta.magnitude, 2), StudioTheme.Current.Text);
            }
            finally { GUI.matrix = previousMatrix; }
            EditorGUI.DrawRect(new Rect(start.x - 2, start.y - 2, 4, 4), StudioTheme.Current.Text);
        }
        internal void ExecuteMarkingAction(MarkingAction action, SceneView view)
        {
            if (!MarkingActionEnabled(action)) return;
            var before = action == MarkingAction.Clear || action == MarkingAction.SelectAll ? CaptureSelection() : null;
            switch (action)
            {
                case MarkingAction.Object: SetComponentMode(ComponentMode.Object,view); break;
                case MarkingAction.Vertex: SetComponentMode(ComponentMode.Vertex,view); break;
                case MarkingAction.Edge: SetComponentMode(ComponentMode.Edge,view); break;
                case MarkingAction.Face: SetComponentMode(ComponentMode.Face,view); break;
                case MarkingAction.Move: ActivateTool(Tool.Move); break;
                case MarkingAction.Rotate: ActivateTool(Tool.Rotate); break;
                case MarkingAction.Scale: ActivateTool(Tool.Scale); break;
                case MarkingAction.Axes: SetToolbarLocalAxes(!local); break;
                case MarkingAction.ObjectAxes: SetToolbarLocalAxes(true); break;
                case MarkingAction.ComponentAxes: SetToolbarComponentAxes(); break;
                case MarkingAction.WorldAxes: SetToolbarLocalAxes(false); break;
                case MarkingAction.EditPivot: pivot = !pivot; break;
                case MarkingAction.CenterPivot: CenterEditingPivot(); break;
                case MarkingAction.Freeze: Document.Freeze(Active, Transforms); Changed(); break;
                case MarkingAction.OpenUV: StudioUVEditor.Open(this); break;
                case MarkingAction.Frame: Frame(view); break;
                case MarkingAction.Through: xray = !xray; SaveDisplayConfiguration(); break;
                case MarkingAction.Clear: ClearAllComponentSelections(); if (mode == ComponentMode.Object) { objects.Clear(); Active = -1; } break;
                case MarkingAction.SelectAll:
                    if (mode == ComponentMode.Object) { for (int i = 0; i < Transforms.Length; i++) objects.Add(i); if (Active < 0 && Transforms.Length > 0) Active = 0; }
                    else { foreach(int node in ComponentTargetNodes()) using(new ComponentTargetScope(this,node)) for (int i = 0; i < ComponentCount(); i++) if(IsolationAllowsComponent(i)) Components.Add(i); }
                    break;
                default: if (action >= MarkingAction.UV0 && action <= MarkingAction.UV7) StudioUVEditor.Open(this, action - MarkingAction.UV0); break;
            }
            RecordSelectionChange(before);
            repaint(); toolOptions?.Repaint();
        }
        private void SetComponentMode(ComponentMode next, SceneView view = null) => ChangeComponentMode(next,view);
    }
}
