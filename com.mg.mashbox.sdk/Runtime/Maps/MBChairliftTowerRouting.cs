#if UNITY_6000_0_OR_NEWER
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps
{
    public static class MBChairliftTowerRouting
    {
        // Tower order is authored in the hierarchy, never inferred from height or
        // distance (both are ambiguous on lifts that turn or cross a valley).
        public static bool Refresh(MBChairliftPath path)
        {
            if (path.loadingBay || !path.autoConnectTowers) return false;
            var lift = path.GetComponentInParent<MBChairlift>();
            if (lift == null) return false;
            var endpoints = new Transform[4];
            string[] names = { "Bottom Transfer Point Up", "Top Transfer Point Up", "Top Transfer Point Down", "Bottom Transfer Point Down" };
            int nonTowerPoints = 0;
            foreach (var point in path.points)
            {
                if (point == null || point.GetComponentInParent<MBChairliftTower>() != null) continue;
                nonTowerPoints++;
                for (int i = 0; i < names.Length; i++) if (point.name == names[i]) endpoints[i] = point;
            }
            // Preserve custom routes. Auto wiring supports the SDK station loop
            // and the original tower-only starter, without discarding extra waypoints.
            bool hasStations = nonTowerPoints == 4;
            if (nonTowerPoints != 0 && (!hasStations || System.Array.Exists(endpoints, p => p == null))) return false;
            var up = new List<Transform>();
            var down = new List<Transform>();
            foreach (var tower in lift.GetComponentsInChildren<MBChairliftTower>(true))
            {
                if (tower.GetComponentInParent<MBChairlift>() != lift) continue;
                var t = tower.transform;
                var a = t.Find("Cable Point Up Entry") ?? t.Find("Cable Point Up");
                var b = t.Find("Cable Point Up Exit");
                var c = t.Find("Cable Point Down Entry") ?? t.Find("Cable Point Down");
                var d = t.Find("Cable Point Down Exit");
                // Do not replace a valid route with a half-connected tower.
                if (a == null || b == null || c == null || d == null) return false;
                up.Add(a); up.Add(b);
                down.Insert(0, d); down.Insert(0, c);
            }
            var result = new List<Transform>();
            if (hasStations) result.Add(endpoints[0]);
            result.AddRange(up);
            if (hasStations) { result.Add(endpoints[1]); result.Add(endpoints[2]); }
            result.AddRange(down);
            if (hasStations) result.Add(endpoints[3]);
            bool same = result.Count == path.points.Count;
            for (int i = 0; same && i < result.Count; i++) same = result[i] == path.points[i];
            if (same) return false;
            path.points = result;
            return true;
        }
    }
}
#endif
