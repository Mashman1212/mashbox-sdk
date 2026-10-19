using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal bool CanDuplicateObjects => Document && mode == ComponentMode.Object && objects.Count > 0;
        internal void DuplicateSelectedObjects()
        {
            if (!CanDuplicateObjects) return;
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Duplicate model objects");
            var copies = Document.DuplicateObjects(objects);
            Undo.CollapseUndoOperations(group);
            CancelMarkingMenu(); EndPivotSnap(); StopCutTool(); pivot = false; marquee = false; gestureUndo = -1;
            objects.Clear(); Components.Clear();
            foreach (int index in copies) objects.Add(index);
            Active = copies.Length > 0 ? copies[copies.Length - 1] : -1;
            search = ""; GUIUtility.hotControl = 0;
            Rebuild(); Changed();
        }
        internal bool HandleObjectDuplicate()
        {
            var evt = Event.current;
            if (evt == null || EditorGUIUtility.editingTextField) return false;
            bool key = StudioShortcuts.Pressed(StudioShortcut.Duplicate,evt);
            bool command = (evt.type == EventType.ValidateCommand || evt.type == EventType.ExecuteCommand) && evt.commandName == "Duplicate";
            if (!key && !command) return false;
            if (evt.type != EventType.ValidateCommand && GUIUtility.hotControl == 0 && cutTool == CutTool.None)
            {
                if (CanDuplicateObjects) Run(DuplicateSelectedObjects);
                else if (mode != ComponentMode.Object) { error = "Switch to Object mode to duplicate objects."; repaint(); }
            }
            // Claim Unity's command even with no model selection; do not duplicate main-scene objects.
            evt.Use(); return true;
        }
    }
}
