using UnityEngine;

namespace MG.GodSurface
{
    public static class MGSurfaceMath
    {
        public static Vector2Int Cell(Vector3 position, float width) => new Vector2Int(
            Mathf.FloorToInt(position.x / Mathf.Max(1, width)), Mathf.FloorToInt(position.z / Mathf.Max(1, width)));

        // Matrix columns preserve parent shear and negative scale, unlike rebuilding TRS from lossyScale.
        public static Bounds TransformBounds(Bounds local, Matrix4x4 matrix)
        {
            Vector3 e = local.extents;
            Vector3 x = matrix.MultiplyVector(new Vector3(e.x, 0, 0));
            Vector3 y = matrix.MultiplyVector(new Vector3(0, e.y, 0));
            Vector3 z = matrix.MultiplyVector(new Vector3(0, 0, e.z));
            return new Bounds(matrix.MultiplyPoint3x4(local.center), 2 * new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)));
        }

        public static bool Inside(Bounds bounds, Plane plane) =>
            plane.GetDistanceToPoint(bounds.center) + Vector3.Dot(bounds.extents,
                new Vector3(Mathf.Abs(plane.normal.x), Mathf.Abs(plane.normal.y), Mathf.Abs(plane.normal.z))) >= 0;

        public static int SelectLOD(float relativeHeight, float[] thresholds)
        {
            for (int i = 0; i < thresholds.Length; i++) if (relativeHeight >= thresholds[i]) return i;
            return -1;
        }
    }
}
