using MashBoxSDK.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    [InitializeOnLoad]
    internal static class MBChairliftSelection
    {
        private static MBChairliftTower[] towers;
        private static MBChairliftStation[] stations;
        private static bool dirty = true;

        static MBChairliftSelection()
        {
            EditorApplication.hierarchyChanged += () => dirty = true;
            SceneView.duringSceneGui += Draw;
        }

        private static void Draw(SceneView view)
        {
            if (!view.drawGizmos || !MBGameplayGizmoVisibility.ChairliftsVisible || Event.current.alt || Tools.current == Tool.View) return;
            if (dirty)
            {
                towers = Object.FindObjectsByType<MBChairliftTower>(FindObjectsInactive.Exclude);
                stations = Object.FindObjectsByType<MBChairliftStation>(FindObjectsInactive.Exclude);
                dirty = false;
            }
            foreach (var tower in towers)
            {
                if (!CanPick(tower)) continue;
                if (tower.TryGetCableSupports(out var a, out var b, out var c, out var d))
                {
                    var head = (a + b + c + d) * .25f;
                    PickBox(tower.gameObject, new Bounds(new Vector3(0, head.y * .5f, 0), new Vector3(.9f, Mathf.Max(.1f, Mathf.Abs(head.y)), .9f)), Color.yellow);
                    var supports = new Bounds(a, Vector3.one * .3f);
                    supports.Encapsulate(b); supports.Encapsulate(c); supports.Encapsulate(d);
                    PickBox(tower.gameObject, supports, Color.yellow);
                }
                PickMarker(tower.gameObject, tower.LabelPosition, Color.yellow);
            }
            foreach (var station in stations)
            {
                if (!CanPick(station)) continue;
                if (station.previewParts != null && station.previewParts.Length > 0)
                    foreach (var part in station.previewParts) PickBox(station.gameObject, part, Color.cyan);
                else PickBox(station.gameObject, new Bounds(station.previewCenter, station.previewSize), Color.cyan);
                PickMarker(station.gameObject, station.transform.TransformPoint(station.previewCenter), Color.cyan);
            }
        }

        private static bool CanPick(Component component) => component != null && component.gameObject.activeInHierarchy &&
            StageUtility.GetStageHandle(component.gameObject) == StageUtility.GetCurrentStageHandle() &&
            !SceneVisibilityManager.instance.IsHidden(component.gameObject) &&
            !SceneVisibilityManager.instance.IsPickingDisabled(component.gameObject);

        private static void PickBox(GameObject target, Bounds bounds, Color color)
        {
            // Let Unity's move/rotate/scale handles win once this element is selected.
            if (Selection.Contains(target)) return;
            color.a = .12f;
            var size = bounds.size;
            size = new Vector3(Mathf.Max(.15f, size.x), Mathf.Max(.15f, size.y), Mathf.Max(.15f, size.z));
            using (new Handles.DrawingScope(color, target.transform.localToWorldMatrix * Matrix4x4.TRS(bounds.center, Quaternion.identity, size)))
                if (Handles.Button(Vector3.zero, Quaternion.identity, 1f, 1f, Handles.CubeHandleCap)) Select(target);
        }

        private static void PickMarker(GameObject target, Vector3 position, Color color)
        {
            if (Selection.Contains(target)) return;
            using (new Handles.DrawingScope(color))
            {
                float size = HandleUtility.GetHandleSize(position) * .12f;
                if (Handles.Button(position, Quaternion.identity, size, size * 1.5f, Handles.SphereHandleCap)) Select(target);
            }
        }

        private static void Select(GameObject target)
        {
            if (Event.current.shift || Event.current.control || Event.current.command)
            {
                var selected = new System.Collections.Generic.List<Object>(Selection.objects);
                if (!selected.Remove(target)) selected.Add(target);
                Selection.objects = selected.ToArray();
            }
            else Selection.activeGameObject = target;
            SceneView.RepaintAll();
        }
    }
}
