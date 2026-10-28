using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal const string PivotLockKey = "Model.LockPivot";
        private bool pivotLocked, lockedPivotValid;
        private StudioModelDocument lockedPivotDocument;
        private Vector3 lockedPivotPosition;
        private Quaternion lockedPivotOrientation;
        private int lockedObjectMoveControl;
        private Vector3 lockedObjectMovePosition;
        internal bool PivotLocked => pivotLocked;
        private bool HasLockedPivot => pivotLocked && lockedPivotValid && lockedPivotDocument == Document;
        private void EnsureLockedPivot()
        {
            if (!pivotLocked || HasLockedPivot || !Document || Active < 0 || Active >= Transforms.Length) return;
            var position = UnlockedEditingPivotPosition;
            if (mode != ComponentMode.Object && !HasComponentPivot)
            {
                var nodes = ComponentTargetNodes(true);
                if (nodes.Count > 1) position = MultiComponentCenter(nodes);
            }
            RememberLockedPivot(position, UnlockedPivotOrientation);
        }
        private void RememberLockedPivot(Vector3 position, Quaternion orientation)
        {
            if (!pivotLocked || !Document) return;
            lockedPivotDocument = Document; lockedPivotPosition = position;
            lockedPivotOrientation = orientation.normalized; lockedPivotValid = true;
        }
        private Vector3 ResolveLockedPivotCenter(Vector3 fallback)
        { EnsureLockedPivot(); return HasLockedPivot ? lockedPivotPosition : fallback; }
        private Vector3 LockedObjectMoveHandleCenter(Vector3 fallback, int control)
        {
            if (!HasLockedPivot || pivot || mode != ComponentMode.Object || tool != UnityEditor.Tool.Move) return fallback;
            if (control == 0 || control != lockedObjectMoveControl) { lockedObjectMoveControl = 0; return fallback; }
            return lockedObjectMovePosition;
        }
        private void TrackLockedObjectMove(Vector3 position, int control)
        {
            if (!HasLockedPivot || pivot || mode != ComponentMode.Object || tool != UnityEditor.Tool.Move || control == 0) return;
            lockedObjectMoveControl = control; lockedObjectMovePosition = position;
        }
        internal void SetPivotLocked(bool enabled)
        {
            if (pivotLocked == enabled) return;
            pivotLocked = enabled; lockedPivotValid = false; lockedPivotDocument = null; lockedObjectMoveControl = 0;
            EnsureLockedPivot(); SaveDisplayConfiguration();
            InvalidateHandleComponentFrame(); pivotHover = null; repaint(); toolOptions?.Repaint();
        }
        private void LoadPivotLock()
        {
            bool enabled = StudioPreferenceStore.GetFloat(PivotLockKey, 0) >= .5f;
            if (pivotLocked != enabled) { lockedPivotValid = false; lockedPivotDocument = null; }
            pivotLocked = enabled;
        }
    }
}
