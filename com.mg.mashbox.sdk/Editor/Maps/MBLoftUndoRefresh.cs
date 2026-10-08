using System.Collections.Generic;
using MashBoxSDK.Maps.Spline;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace MashBoxSDK.MapTools
{
    [InitializeOnLoad]
    internal static class MBLoftUndoRefresh
    {
        private static bool undoPending;
        private static bool indexDirty = true;
        private static MultiSplineLoft[] lofts;
        private static readonly HashSet<MultiSplineLoft> affected = new HashSet<MultiSplineLoft>();

        static MBLoftUndoRefresh()
        {
            Undo.undoRedoPerformed += () => undoPending = true;
            EditorApplication.hierarchyChanged += () => indexDirty = true;
            ObjectChangeEvents.changesPublished += OnChanges;
        }

        // Unity publishes the restored objects after Undo/Redo. Selection and the
        // last-used toolbar mode do not tell us which objects were restored.
        private static void OnChanges(ref ObjectChangeEventStream stream)
        {
            if (!undoPending || EditorApplication.isPlayingOrWillChangePlaymode) return;
            undoPending = false;
            affected.Clear();
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) == ObjectChangeKind.ChangeGameObjectOrComponentProperties)
                {
                    stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var change);
                    Collect(EditorUtility.InstanceIDToObject(change.instanceId));
                }
                else if (stream.GetEventType(i) == ObjectChangeKind.ChangeGameObjectParent)
                {
                    stream.GetChangeGameObjectParentEvent(i, out var change);
                    var obj = EditorUtility.InstanceIDToObject(change.instanceId) as GameObject;
                    if (obj != null) Collect(obj.transform);
                }
            }
            // Existing OnValidate and spline-change handlers cover component data.
            // Queue, rather than force, so notifications for the same loft coalesce.
            foreach (var loft in affected) loft.QueueRegenerate();
            affected.Clear();
        }

        private static void Collect(Object changed)
        {
            if (!(changed is Transform) && !(changed is SplineContainer)) return;
            if (indexDirty || lofts == null)
            {
                lofts = Object.FindObjectsByType<MultiSplineLoft>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                indexDirty = false;
            }
            foreach (var loft in lofts)
                if (loft != null && loft.isActiveAndEnabled && loft.AutoRegenerate &&
                    !EditorUtility.IsPersistent(loft) && DependsOn(loft, changed)) affected.Add(loft);
        }

        internal static bool DependsOn(MultiSplineLoft loft, Object changed)
        {
            var moved = changed as Transform;
            if (moved != null && loft.transform.IsChildOf(moved)) return true;
            foreach (var source in loft.Sources)
            {
                if (source == null || source.container == null) continue;
                if (source.container == changed ||
                    (moved != null && source.container.transform.IsChildOf(moved))) return true;
            }
            return false;
        }
    }
}
