using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal bool HandleSaveShortcut()
        {
            var evt = Event.current;
            if (evt == null || evt.type != EventType.KeyDown || evt.keyCode != KeyCode.S ||
                !(evt.control || evt.command) || evt.alt || GUIUtility.hotControl != 0) return false;
            if (evt.shift) Run(SaveAs); else Run(Save);
            evt.Use(); return true;
        }
    }
}
