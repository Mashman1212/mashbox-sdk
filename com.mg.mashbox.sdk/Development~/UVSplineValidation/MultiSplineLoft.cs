using UnityEngine;

namespace MashBoxSDK.Maps.Spline
{
    // Only the unrelated loft/mesh generator is stubbed. UVSpline and Unity's
    // serialization, transforms, meshes, and spline package are the real code.
    public sealed class MultiSplineLoft : MonoBehaviour
    {
        public int synchronizationCount;
        public int pointCount;
        public bool SynchronizeUvSplineSettings(UVSpline spline)
        {
            synchronizationCount++;
            pointCount = spline.GeneratedPointCount;
            return true;
        }
        public bool TryEvaluateSurfaceCenterline(float t, out Vector3 position)
        {
            position = Vector3.zero;
            return false;
        }
        public void InvalidateVisualChunks() { }
    }
}
