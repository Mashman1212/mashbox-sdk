#if UNITY_6000_0_OR_NEWER
using System.Collections.Generic;
using MashBoxSDK.Maps;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    // Editor-only labels also cover existing proxy instances; no prefab migration is needed.
    internal static class MBChairliftLabels
    {
        private static readonly List<MBChairliftPath> Paths = new List<MBChairliftPath>();
        private static readonly List<MBChairliftTower> Towers = new List<MBChairliftTower>();
        private static readonly List<MBChairliftTower> OrderedTowers = new List<MBChairliftTower>();
        private static readonly List<MBChairliftStation> Stations = new List<MBChairliftStation>();
        private static readonly Color CableColor = new Color(1f, .78f, .18f);
        private static GUIStyle labelStyle;

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        private static void DrawLabels(MBChairlift lift, GizmoType gizmoType)
        {
            if (!MBGameplayGizmoVisibility.ChairliftsVisible || !lift.gameObject.activeInHierarchy) return;

            lift.GetComponentsInChildren(false, Paths);
            lift.GetComponentsInChildren(false, Towers);
            lift.GetComponentsInChildren(false, Stations);
            OrderedTowers.Clear();
            var bounds = new Bounds(lift.transform.position, Vector3.zero);
            bool hasBounds = false;

            foreach (var path in Paths)
            {
                if (path.GetComponentInParent<MBChairlift>() != lift || path.points == null) continue;
                foreach (var point in path.points)
                {
                    if (point == null || point.GetComponentInParent<MBChairlift>() != lift) continue;
                    Encapsulate(ref bounds, ref hasBounds, point.position);
                    if (path.loadingBay) continue;
                    // Follow the outgoing cable, rather than sorting tower names or world height.
                    var tower = point.GetComponentInParent<MBChairliftTower>();
                    if (tower != null && tower.gameObject.activeInHierarchy && !OrderedTowers.Contains(tower))
                        OrderedTowers.Add(tower);
                }
            }
            // Newly added towers still get a label before they are connected to the cable.
            foreach (var tower in Towers)
            {
                if (tower.GetComponentInParent<MBChairlift>() != lift) continue;
                if (!OrderedTowers.Contains(tower)) OrderedTowers.Add(tower);
                Encapsulate(ref bounds, ref hasBounds, tower.transform.position);
            }
            for (int i = 0; i < OrderedTowers.Count; i++)
                DrawLabel(OrderedTowers[i].LabelPosition, "Tower " + (i + 1), Color.yellow);

            foreach (var station in Stations)
            {
                if (station.GetComponentInParent<MBChairlift>() != lift) continue;
                // World-space bounds keep labels above the station even when it is rotated/scaled.
                var stationBounds = new Bounds(station.transform.TransformPoint(station.previewCenter), Vector3.zero);
                for (int corner = 0; corner < 8; corner++)
                {
                    var sign = new Vector3((corner & 1) == 0 ? -1 : 1,
                        (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                    stationBounds.Encapsulate(station.transform.TransformPoint(station.previewCenter +
                        Vector3.Scale(station.previewSize * .5f, sign)));
                }
                Encapsulate(ref bounds, ref hasBounds, stationBounds.min);
                Encapsulate(ref bounds, ref hasBounds, stationBounds.max);
                var anchor = new Vector3(stationBounds.center.x, stationBounds.max.y, stationBounds.center.z);
                DrawLabel(anchor, station.kind == MBChairliftStationKind.Bottom ? "Bottom Station" : "Top Station", Color.cyan);
            }

            var titlePosition = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            string title = string.IsNullOrWhiteSpace(lift.chairliftName) ? lift.name : lift.chairliftName;
            DrawLabel(titlePosition, title, CableColor, 1f);
        }

        private static void Encapsulate(ref Bounds bounds, ref bool initialized, Vector3 point)
        {
            if (!initialized) { bounds = new Bounds(point, Vector3.zero); initialized = true; }
            else bounds.Encapsulate(point);
        }

        private static void DrawLabel(Vector3 anchor, string text, Color color, float extraHeight = 0f)
        {
            if (labelStyle == null)
                labelStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            labelStyle.normal.textColor = color;
            // A small camera-relative gap keeps text clear of the gizmo at different zoom levels.
            var position = anchor + Vector3.up * (Mathf.Max(.75f, HandleUtility.GetHandleSize(anchor) * .2f) + extraHeight);
            Handles.Label(position, text, labelStyle);
        }
    }
}
#endif
