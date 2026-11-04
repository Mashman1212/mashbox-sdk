using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // Track only a capture acquired by this IMGUI container. Never clear a
    // different container's active control or interrupt a held-button drag.
    internal sealed class StudioMouseCapture
    {
        private int control, button = -1;
        internal void Clear() { control = 0; button = -1; }
        internal void Observe(EventType type, int mouseButton, int before, int after)
        {
            if (after == 0) { Clear(); return; }
            if (type == EventType.MouseDown && before == 0)
            { control = after; button = mouseButton; }
        }
        internal bool ShouldRelease(EventType type, int mouseButton, int hotControl)
            => control != 0 && hotControl == control &&
                (type == EventType.MouseMove || ((type == EventType.MouseDown || type == EventType.MouseUp) && mouseButton == button));
    }
}
