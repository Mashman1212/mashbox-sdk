#if UNITY_6000_0_OR_NEWER
using MashBoxSDK.Maps;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    [InitializeOnLoad]
    internal static class MBChairliftTowerRoutingEditor
    {
        static MBChairliftTowerRoutingEditor()
        {
            EditorApplication.hierarchyChanged += Queue;
            Undo.undoRedoPerformed += Queue;
            Queue();
        }
        private static void Queue()
        {
            EditorApplication.delayCall -= Refresh;
            EditorApplication.delayCall += Refresh;
        }
        private static void Refresh()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (var path in Object.FindObjectsByType<MBChairliftPath>(FindObjectsInactive.Include))
            {
                if (EditorUtility.IsPersistent(path) || !path.gameObject.scene.IsValid()) continue;
                if (!MBChairliftTowerRouting.Refresh(path)) continue;
                // Derived references follow duplicate/delete/reorder and Undo/Redo.
                // Recording another Undo operation here would split the user's action.
                EditorUtility.SetDirty(path);
                PrefabUtility.RecordPrefabInstancePropertyModifications(path);
            }
            SceneView.RepaintAll();
        }
    }
}
#endif
