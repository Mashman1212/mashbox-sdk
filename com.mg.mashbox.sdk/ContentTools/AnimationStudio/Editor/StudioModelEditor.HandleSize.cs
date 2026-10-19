using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal static bool HandleSizeShortcut(Event evt)
        {
            // SceneView navigation retains keyboardControl after orbit/pan ends.
            // Only text editing or an active mouse gesture should block these keys.
            if (evt.type != EventType.KeyDown || EditorGUIUtility.editingTextField ||
                GUIUtility.hotControl != 0) return false;
            bool increase = StudioShortcuts.Pressed(StudioShortcut.LargerHandles,evt);
            bool decrease = StudioShortcuts.Pressed(StudioShortcut.SmallerHandles,evt);
            if (!increase && !decrease) return false;
            var settings = StudioSettings.Current;
            float next = Mathf.Clamp(settings.handleSize * (increase ? 1.2f : 1 / 1.2f), .25f, 3);
            if (!Mathf.Approximately(next, settings.handleSize)) { settings.handleSize = next; StudioSettings.Save(); }
            evt.Use(); return true;
        }
    }
}
