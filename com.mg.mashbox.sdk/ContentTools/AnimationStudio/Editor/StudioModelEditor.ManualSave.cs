using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal bool HandleSaveShortcut()
        {
            var evt = Event.current;
            if (evt == null || evt.type != EventType.KeyDown || GUIUtility.hotControl != 0) return false;
            bool saveAs = StudioShortcuts.Pressed(StudioShortcut.SaveAs,evt);
            if (!saveAs && !StudioShortcuts.Pressed(StudioShortcut.Save,evt)) return false;
            if (saveAs) Run(SaveAs); else Run(Save);
            evt.Use(); return true;
        }
    }
}
