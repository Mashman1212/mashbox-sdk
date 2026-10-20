using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal bool HandleModelKeyboard(SceneView view)
        {
            var evt = Event.current;
            if (evt == null || !evt.isKey || EditorGUIUtility.editingTextField || hierarchyRenameNode != null) return false;
            if (markingOpen)
            {
                if (StudioShortcuts.Pressed(StudioShortcut.Cancel,evt)) CancelMarkingMenu();
                evt.Use(); return true;
            }
            if (HandleSaveShortcut() || HandleGridSnapKey(view) || HandleSizeShortcut(evt) || HandlePivotSnap(view)) return true;
            if (evt.type != EventType.KeyDown) return false;
            bool Is(StudioShortcut action) => StudioShortcuts.Pressed(action,evt);
            if (Is(StudioShortcut.Cancel))
            {
                if (cutTool != CutTool.None)
                {
                    if (pendingCuts.Count > 0 || cutStart.HasValue || cutDragging) ResetCutGesture(); else StopCutTool();
                }
                else
                {
                    var extrusionSelection = handleExtrudeSourceSelection;
                    if (gestureUndo >= 0) { int group = gestureUndo; gestureUndo = -1; Undo.RevertAllDownToGroup(group); Rebuild(); }
                    if (extrusionSelection != null) { Components.Clear(); foreach (int component in extrusionSelection) Components.Add(component); }
                    marquee = false; GUIUtility.hotControl = 0; pivot = false;
                }
                evt.Use(); repaint(); return true;
            }
            if (GUIUtility.hotControl == 0 && (Is(StudioShortcut.Undo) || Is(StudioShortcut.Redo)))
            { PerformWorkspaceHistory(Is(StudioShortcut.Redo)); evt.Use(); return true; }
            if (Is(StudioShortcut.Preferences)) { StudioControlsPreferences.Open(); evt.Use(); return true; }
            if (Is(StudioShortcut.XRay)) { SetXRayMode(!displayXray); evt.Use(); return true; }
            if (cutTool != CutTool.None)
            {
                if (Is(StudioShortcut.CommitCut))
                { Run(() => CommitCuts(new List<StudioMeshCuts.Cut>(pendingCuts))); ResetCutGesture(); evt.Use(); return true; }
                if (Is(StudioShortcut.RemoveCut))
                {
                    if (pendingCuts.Count > 0) { var last = pendingCuts[pendingCuts.Count-1]; pendingCuts.RemoveAt(pendingCuts.Count-1); cutStart = last.A; }
                    else cutStart = null;
                    evt.Use(); repaint(); return true;
                }
            }
            if (HandleExtrudeShortcut() || HandleObjectDuplicate() || HandleObjectDelete()) return true;
            if (cutTool != CutTool.None && !Is(StudioShortcut.Select) && !Is(StudioShortcut.Move) &&
                !Is(StudioShortcut.Rotate) && !Is(StudioShortcut.Scale) && !Is(StudioShortcut.Frame)) return false;
            if (Is(StudioShortcut.Select)) ActivateTool(Tool.None);
            else if (Is(StudioShortcut.Move)) ActivateTool(Tool.Move);
            else if (Is(StudioShortcut.Rotate)) ActivateTool(Tool.Rotate);
            else if (Is(StudioShortcut.Scale)) ActivateTool(Tool.Scale);
            else if (Is(StudioShortcut.Pivot)) { if (CanAdjustPivot) pivot = !pivot; }
            else if (Is(StudioShortcut.Frame)) Frame(view);
            else if (Is(StudioShortcut.SelectAll)) { if (Document) ExecuteMarkingAction(MarkingAction.SelectAll,view); }
            else if (Is(StudioShortcut.Object)) { mode = ComponentMode.Object; Components.Clear(); }
            else if (Is(StudioShortcut.Vertex)) { if (CanEditComponents()) mode = ComponentMode.Vertex; pivot = false; Components.Clear(); }
            else if (Is(StudioShortcut.Edge)) { if (CanEditComponents()) mode = ComponentMode.Edge; pivot = false; Components.Clear(); }
            else if (Is(StudioShortcut.Face)) { if (CanEditComponents()) mode = ComponentMode.Face; pivot = false; Components.Clear(); }
            else return false;
            evt.Use(); repaint(); return true;
        }
        internal void ReleaseShortcutHolds()
        {
            if (pivotSnapUndo >= 0) Undo.CollapseUndoOperations(pivotSnapUndo);
            EndPivotSnap(); ClearGridSnap();
        }
    }
}
