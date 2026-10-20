using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class MashBoxStudioViewport
    {
        [System.Flags]
        internal enum DirtyState { None = 0, Scene = 1, Camera = 2, Overlay = 4, UI = 8, All = Scene | Camera | Overlay | UI }
        private static readonly FieldInfo WindowParent = typeof(EditorWindow).GetField("m_Parent", WindowMembers);
        private DirtyState viewportDirty = DirtyState.All;
        private object vSyncParent;
        private bool originalViewportVSync, appliedViewportVSync;
        internal int ViewportRenderCount { get; private set; }

        // Unity owns event delivery, layout and repaint coalescing. Only unchanged
        // modelling Repaints may reuse the last image; all input/layout stays native.
        public new void Repaint() => MarkDirty(DirtyState.Scene);
        internal void RepaintUI() => MarkDirty(DirtyState.UI);
        internal void MarkDirty(DirtyState state)
        {
            if (state == DirtyState.None) return;
            if ((state & (DirtyState.Scene | DirtyState.Camera)) != 0) state |= DirtyState.Overlay;
            if ((state & DirtyState.Overlay) != 0) state |= DirtyState.UI;
            viewportDirty |= state;
            base.Repaint();
        }

        protected override void OnSceneGUI()
        {
            ApplyViewportVSync();
            bool repaint = Event.current != null && Event.current.type == EventType.Repaint;
            if (repaint && PresentUnchangedViewport()) return;
            if (repaint) viewportDirty = DirtyState.None;
            if (repaint) BeginViewportImageCapture();
            try
            {
                base.OnSceneGUI();
                if (repaint) { ViewportRenderCount++; FinishViewportImageCapture(); }
            }
            finally { capturingViewport = false; }
        }

        private void ReleaseViewportRedraw() { ReleaseViewportImageCache(); RestoreViewportVSync(); }
        private void ApplyViewportVSync()
        {
            var parent = WindowParent?.GetValue(this);
            if (parent == null) return;
            if (!ReferenceEquals(parent, vSyncParent))
            {
                RestoreViewportVSync();
                var property = parent.GetType().GetProperty("vSyncEnabled", WindowMembers);
                if (property == null) return;
                vSyncParent = parent;
                originalViewportVSync = appliedViewportVSync = (bool)property.GetValue(parent);
            }
            bool requested = StudioSettings.Current.viewportVSync;
            if (requested == appliedViewportVSync) return;
            parent.GetType().GetMethod("EnableVSync", WindowMembers)?.Invoke(parent, new object[] { requested });
            appliedViewportVSync = requested;
        }
        private void RestoreViewportVSync()
        {
            if (vSyncParent == null) return;
            if (originalViewportVSync != appliedViewportVSync)
                vSyncParent.GetType().GetMethod("EnableVSync", WindowMembers)?.Invoke(vSyncParent, new object[] { originalViewportVSync });
            vSyncParent = null;
        }
    }
}
