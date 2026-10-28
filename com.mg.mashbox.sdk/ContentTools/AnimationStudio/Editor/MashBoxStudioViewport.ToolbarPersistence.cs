using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class MashBoxStudioViewport
    {
        // DockingCompleted includes same-container reorder, which need not resize
        // the overlay. Unity exposes this signal only through its native event.
        private static readonly EventInfo NativeToolbarDocked = typeof(Overlay).GetEvent("dockingCompleted", WindowMembers);
        private static readonly EventInfo NativeToolbarCanvasEnabled = typeof(OverlayCanvas).GetEvent("overlaysEnabledChanged", WindowMembers);
        private readonly Dictionary<Overlay, Delegate> toolbarDockingHandlers = new Dictionary<Overlay, Delegate>();
        private bool storedToolbarSaveQueued, toolbarCanvasPersistenceBound;

        private void BindStoredToolbarEvents(Overlay overlay)
        {
            overlay.displayedChanged += OnStoredToolbarBoolean;
            overlay.collapsedChanged += OnStoredToolbarBoolean;
            overlay.floatingChanged += OnStoredToolbarBoolean;
            overlay.layoutChanged += OnStoredToolbarLayout;
            overlay.floatingPositionChanged += OnStoredToolbarPosition;
            if (NativeToolbarDocked != null)
            {
                var handler = Delegate.CreateDelegate(NativeToolbarDocked.EventHandlerType, this,
                    typeof(MashBoxStudioViewport).GetMethod(nameof(OnStoredToolbarDocked), WindowMembers));
                NativeToolbarDocked.GetAddMethod(true).Invoke(overlay, new object[] { handler });
                toolbarDockingHandlers[overlay] = handler;
            }
            if (!toolbarCanvasPersistenceBound && NativeToolbarCanvasEnabled != null)
            {
                NativeToolbarCanvasEnabled.GetAddMethod(true).Invoke(overlayCanvas, new object[] { (Action<bool>)OnStoredToolbarBoolean });
                toolbarCanvasPersistenceBound = true;
            }
        }
        private void UnbindStoredToolbarEvents()
        {
            foreach (var overlay in studioToolbarOverlays)
            {
                overlay.displayedChanged -= OnStoredToolbarBoolean;
                overlay.collapsedChanged -= OnStoredToolbarBoolean;
                overlay.floatingChanged -= OnStoredToolbarBoolean;
                overlay.layoutChanged -= OnStoredToolbarLayout;
                overlay.floatingPositionChanged -= OnStoredToolbarPosition;
            }
            foreach (var pair in toolbarDockingHandlers)
                NativeToolbarDocked?.GetRemoveMethod(true).Invoke(pair.Key, new object[] { pair.Value });
            toolbarDockingHandlers.Clear();
            if (toolbarCanvasPersistenceBound)
                NativeToolbarCanvasEnabled?.GetRemoveMethod(true).Invoke(overlayCanvas, new object[] { (Action<bool>)OnStoredToolbarBoolean });
            toolbarCanvasPersistenceBound = false;
        }
        private void OnStoredToolbarBoolean(bool value) => QueueStoredToolbarSave();
        private void OnStoredToolbarLayout(Layout value) => QueueStoredToolbarSave();
        private void OnStoredToolbarPosition(Vector3 value) => QueueStoredToolbarSave();
        private void OnStoredToolbarDocked(object container) => QueueStoredToolbarSave();
        private void QueueStoredToolbarSave()
        {
            if (!this || restoringStoredToolbars || !restoredStoredToolbars || modeHiddenToolbars.Count > 0 || storedToolbarSaveQueued) return;
            storedToolbarSaveQueued = true;
            EditorApplication.delayCall += FlushStoredToolbarSave;
        }
        private void FlushStoredToolbarSave()
        {
            CancelStoredToolbarSave();
            if (this) SaveStoredToolbars();
        }
        private void CancelStoredToolbarSave()
        {
            EditorApplication.delayCall -= FlushStoredToolbarSave;
            storedToolbarSaveQueued = false;
        }
    }
}
