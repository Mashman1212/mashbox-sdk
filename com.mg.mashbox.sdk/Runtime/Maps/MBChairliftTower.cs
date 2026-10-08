#if UNITY_6000_0_OR_NEWER
using UnityEngine;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("MashBox/Maps/Chairlift Tower")]
    public sealed class MBChairliftTower : MonoBehaviour
    {
        // The tower pivot is at its base. Cable points are authored above it and
        // are also the points used by the runtime cable; never assume pivot = cable height.
        public bool TryGetCableSupports(out Vector3 upEntry, out Vector3 upExit,
            out Vector3 downEntry, out Vector3 downExit)
        {
            var a = transform.Find("Cable Point Up Entry") ?? transform.Find("Cable Point Up");
            var b = transform.Find("Cable Point Up Exit");
            var c = transform.Find("Cable Point Down Entry") ?? transform.Find("Cable Point Down");
            var d = transform.Find("Cable Point Down Exit");
            upEntry = a != null ? transform.InverseTransformPoint(a.position) : Vector3.zero;
            upExit = b != null ? transform.InverseTransformPoint(b.position) : Vector3.zero;
            downEntry = c != null ? transform.InverseTransformPoint(c.position) : Vector3.zero;
            downExit = d != null ? transform.InverseTransformPoint(d.position) : Vector3.zero;
            return a != null && b != null && c != null && d != null;
        }

        public Vector3 LabelPosition
        {
            get
            {
                if (!TryGetCableSupports(out var a, out var b, out var c, out var d)) return transform.position;
                var center = (a + b + c + d) * .25f;
                center.y = Mathf.Max(a.y, b.y, c.y, d.y);
                return transform.TransformPoint(center);
            }
        }

        private void OnDrawGizmos()
        {
            if (!MBGameplayGizmoVisibility.ChairliftsVisible) return;
            var old = Gizmos.matrix;
            var color = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.yellow;
            if (TryGetCableSupports(out var a, out var b, out var c, out var d))
            {
                var up = (a + b) * .5f;
                var down = (c + d) * .5f;
                var head = (up + down) * .5f;
                // Schematic mast and foundation; contact rails use exact authored coordinates.
                var mast = new Vector3(0, head.y, 0);
                Gizmos.DrawWireCube(mast * .5f, new Vector3(.77f, Mathf.Abs(head.y), .77f));
                Gizmos.DrawLine(mast, head);
                Gizmos.DrawLine(up, down);
                Gizmos.DrawLine(a, b);
                Gizmos.DrawLine(c, d);
                Gizmos.DrawWireSphere(a, .15f);
                Gizmos.DrawWireSphere(b, .15f);
                Gizmos.DrawWireSphere(c, .15f);
                Gizmos.DrawWireSphere(d, .15f);
            }
            // A disconnected/new tower only shows its base, rather than an invented cable height.
            Gizmos.DrawWireCube(Vector3.up * .5f, new Vector3(1.25f, 1f, 1.25f));
            Gizmos.matrix = old;
            Gizmos.color = color;
        }
    }
}
#endif
