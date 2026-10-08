#if UNITY_6000_0_OR_NEWER
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("MashBox/Maps/Chairlift Cable Path")]
    [DisallowMultipleComponent]
    public sealed class MBChairliftPath : MonoBehaviour
    {
        public bool loadingBay;
        public bool closedLoop = true;
        public bool catmullRom;
        [Min(2)] public int samplesPerSegment = 16;
        public List<Transform> points = new List<Transform>();
        public bool simulateCableSag = true;
        [Min(0)] public int sagPointsPerSpan = 2;
        [Min(0)] public float cableSagAmount = 1.5f;
        [Min(.01f)] public float sagReferenceSpanLength = 30;
        public bool spawnChairs = true;
        public bool useSpacing;
        [Min(1)] public int chairCount = 8;
        [Min(.1f)] public float spacing = 12;
        public float startOffset;
        public bool alignToPath = true;
        public bool loopChairs = true;

        public bool Validate(out string error)
        {
            if (points == null || points.Count < (closedLoop ? 3 : 2))
            { error = name + ": not enough cable points."; return false; }
            var owner = GetComponentInParent<MBChairlift>();
            bool hasLength = false;
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i] == null || points[i].GetComponentInParent<MBChairlift>() != owner)
                { error = name + ": cable points must belong to this lift."; return false; }
                if (i > 0 && Vector3.SqrMagnitude(points[i].position - points[i - 1].position) > .000001f) hasLength = true;
            }
            if (!hasLength) { error = name + ": the cable has zero length."; return false; }
            error = null;
            return true;
        }

        // The same sag/curve construction as the game, without renderers or simulation.
        public List<Vector3> GetPreviewPoints()
        {
            var result = new List<Vector3>();
            if (points == null) return result;
            for (int i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                if (a == null) continue;
                result.Add(a.position);
                if (b == null || (!closedLoop && i == points.Count - 1) || loadingBay ||
                    !simulateCableSag || sagPointsPerSpan <= 0 || IsTowerArm(a, b)) continue;
                float length = Vector3.Distance(a.position, b.position);
                float sag = cableSagAmount * Mathf.Clamp01(length / Mathf.Max(.01f, sagReferenceSpanLength));
                for (int j = 1; j <= Mathf.Min(64, sagPointsPerSpan); j++)
                {
                    float t = j / (float)(Mathf.Min(64, sagPointsPerSpan) + 1);
                    result.Add(Vector3.Lerp(a.position, b.position, t) + Vector3.down * (Mathf.Sin(t * Mathf.PI) * sag));
                }
            }
            return result;
        }
        private static bool IsTowerArm(Transform a, Transform b)
        {
            if (a.parent == null || a.parent != b.parent) return false;
            return Pair(a.name, b.name, "Cable Point Up Entry", "Cable Point Up Exit") ||
                   Pair(a.name, b.name, "Cable Point Down Entry", "Cable Point Down Exit");
        }
        private static bool Pair(string a, string b, string x, string y) => (a == x && b == y) || (a == y && b == x);
        private Vector3 Point(List<Vector3> p, int i) => p[closedLoop ? (i + p.Count) % p.Count : Mathf.Clamp(i, 0, p.Count - 1)];
        private void OnDrawGizmos()
        {
            if (!MBGameplayGizmoVisibility.ChairliftsVisible) return;
            var p = GetPreviewPoints();
            if (p.Count < 2) return;
            Color old = Gizmos.color;
            Gizmos.color = loadingBay ? Color.cyan : new Color(1, .78f, .18f);
            int count = closedLoop ? p.Count : p.Count - 1;
            for (int i = 0; i < count; i++)
            {
                Vector3 previous = p[i];
                int samples = catmullRom ? Mathf.Clamp(samplesPerSegment, 2, 64) : 1;
                for (int j = 1; j <= samples; j++)
                {
                    float t = j / (float)samples;
                    Vector3 a = Point(p, i - 1), b = Point(p, i), c = Point(p, i + 1), d = Point(p, i + 2);
                    Vector3 next = catmullRom ? .5f * ((2*b) + (-a+c)*t + (2*a-5*b+4*c-d)*t*t + (-a+3*b-3*c+d)*t*t*t) : c;
                    Gizmos.DrawLine(previous, next);
                    previous = next;
                }
            }
            foreach (var point in points) if (point != null) Gizmos.DrawWireSphere(point.position, .2f);
            Gizmos.color = old;
        }
    }
}
#endif
