using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    // Same camera-local GUI coordinates used by the finite-edge snap picker.
    internal static class StudioSplineSnap
    {
        internal static bool Closest(Camera camera, Vector2 pointer, float dpi, StudioSpline spline, Matrix4x4 matrix,
            out Vector3 point, out float distance, out float depth)
        {
            Vector3 bestPoint=default;float best=float.PositiveInfinity,bestDepth=float.PositiveInfinity;
            Vector3 World(int segment,float t)=>matrix.MultiplyPoint3x4(spline.Evaluate(segment,t));
            float Measure(Vector3 world,out float z)
            {
                var screen=camera.WorldToScreenPoint(world);z=screen.z;
                if(!StudioSpline.Finite(world)||!StudioSpline.Finite(screen)||z<=camera.nearClipPlane)return float.PositiveInfinity;
                return (pointer-new Vector2(screen.x/dpi,(camera.pixelHeight-screen.y)/dpi)).sqrMagnitude;
            }
            void Consider(Vector3 world)
            {
                float d=Measure(world,out float z);
                if(d<best || Mathf.Abs(d-best)<.00001f&&z<bestDepth){best=d;bestDepth=z;bestPoint=world;}
            }
            for(int segment=0;segment<spline.SegmentCount;segment++)
            {
                var first=World(segment,0);var last=World(segment,1);Consider(first);Consider(last);
                if(spline.kind==StudioSplineKind.Linear)
                {
                    var a=camera.WorldToScreenPoint(first);var b=camera.WorldToScreenPoint(last);
                    // Clip a crossing span to the near plane before projection.
                    float near=camera.nearClipPlane+Mathf.Max(.000001f,camera.nearClipPlane*.0001f);
                    if(a.z<near&&b.z<near)continue;
                    if(a.z<near)first=Vector3.Lerp(first,last,(near-a.z)/(b.z-a.z));
                    else if(b.z<near)last=Vector3.Lerp(first,last,(near-a.z)/(b.z-a.z));
                    a=camera.WorldToScreenPoint(first);b=camera.WorldToScreenPoint(last);
                    var start=new Vector2(a.x/dpi,(camera.pixelHeight-a.y)/dpi);var span=new Vector2((b.x-a.x)/dpi,(a.y-b.y)/dpi);
                    float t=span.sqrMagnitude>1e-12f?Mathf.Clamp01(Vector2.Dot(pointer-start,span)/span.sqrMagnitude):0;
                    if(!camera.orthographic)t=t*a.z/((1-t)*b.z+t*a.z);
                    Consider(Vector3.Lerp(first,last,t));continue;
                }
                // Find every sampled local minimum, then refine on the actual cubic,
                // never on its display chords. Endpoints remain explicit candidates.
                const int samples=48;
                float left=Measure(first,out _),middle=Measure(World(segment,1f/samples),out _);
                for(int j=1;j<samples;j++)
                {
                    float right=Measure(World(segment,(j+1f)/samples),out _);
                    if((j==1||j==samples-1||middle<=left&&middle<=right)&&!float.IsInfinity(middle))
                    {
                        float lo=(j-1f)/samples,hi=(j+1f)/samples;
                        for(int iteration=0;iteration<22;iteration++)
                        {
                            float a=lo+(hi-lo)*.381966f,b=lo+(hi-lo)*.618034f;
                            if(Measure(World(segment,a),out _)<=Measure(World(segment,b),out _))hi=b;else lo=a;
                        }
                        Consider(World(segment,(lo+hi)*.5f));
                    }
                    left=middle;middle=right;
                }
            }
            point=bestPoint;distance=best;depth=bestDepth;return !float.IsInfinity(best);
        }
    }
}
