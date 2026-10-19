using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private bool gridSnapHeld;
        private KeyCode gridSnapKey;
        private SceneView gridSnapView;

        private void ClearGridSnap()
        {
            gridSnapHeld = false; gridSnapView = null;
        }

        private bool HandleGridSnapKey(SceneView view)
        {
            var evt = Event.current;
            if (evt.type == EventType.KeyUp && evt.keyCode == gridSnapKey)
            {
                bool held = gridSnapHeld; ClearGridSnap();
                if (held) { evt.Use(); repaint(); return true; }
            }
            if (StudioShortcuts.Pressed(StudioShortcut.Cancel,evt)) ClearGridSnap();
            if (!StudioShortcuts.Pressed(StudioShortcut.GridSnap,evt) || EditorGUIUtility.editingTextField) return false;
            // Navigation can retain keyboard focus. Also allow X to be pressed mid-drag.
            gridSnapHeld = true; gridSnapKey = evt.keyCode; gridSnapView = view;
            evt.Use(); repaint(); return true;
        }

        internal static Vector3 SnapMoveToGrid(Vector3 from, Vector3 to, float spacing)
        {
            if (spacing <= 0 || float.IsNaN(spacing) || float.IsInfinity(spacing)) return to;
            // Snap to absolute world-grid coordinates, not increments from an off-grid
            // starting point. Keep coordinates locked by an axis/plane handle unchanged.
            for (int axis = 0; axis < 3; axis++)
                if (Mathf.Abs(to[axis] - from[axis]) > .000001f)
                    to[axis] = Mathf.Round(to[axis] / spacing) * spacing;
            return to;
        }
    }
}
