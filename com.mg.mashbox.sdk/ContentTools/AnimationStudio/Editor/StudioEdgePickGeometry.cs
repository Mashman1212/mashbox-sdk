using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    // Shared pointer geometry; no editor or native graphics state is needed.
    internal static class StudioEdgePickGeometry
    {
        internal static float ClosestFraction(Vector2 pointer,Vector2 a,Vector2 b)
        {
            var delta=b-a;float length=delta.sqrMagnitude;
            return length>0?Mathf.Clamp01(Vector2.Dot(pointer-a,delta)/length):0;
        }
        internal static float PerspectiveFraction(float screenFraction,float depthA,float depthB)
        {
            float denominator=(1-screenFraction)*depthB+screenFraction*depthA;
            return denominator>0?Mathf.Clamp01(screenFraction*depthA/denominator):screenFraction;
        }
    }
}
