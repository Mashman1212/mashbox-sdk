using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private float[] grabSurfaceDistances, grabMirrorDistances;
        private void PrepareGrabFalloff(Vector3 center)
        {
            grabSurfaceDistances = grabMirrorDistances = null;
            if (Tool != Brush.Grab || Document.grabVolume) return;
            grabSurfaceDistances = SurfaceDistances(center);
            if (SymmetryActive) grabMirrorDistances = SurfaceDistances(ReflectSymmetry(center,true));
        }
        private float[] SurfaceDistances(Vector3 center)
        {
            var distances = new float[initialPoints.Length];
            int seed = -1; float closest = float.PositiveInfinity;
            for (int i=0;i<distances.Length;i++)
            {
                distances[i] = float.PositiveInfinity;
                float d = (initialPoints[i]-center).sqrMagnitude;
                if (d < closest) { closest=d; seed=i; }
            }
            if (seed < 0) return distances;
            var pending = new SortedSet<(float distance, int vertex)>();
            void Visit(int vertex,float distance)
            {
                if (distance >= radius || distance >= distances[vertex]) return;
                foreach (int peer in sculptSeams[vertex])
                {
                    if (distance >= distances[peer]) continue;
                    pending.Remove((distances[peer],peer)); distances[peer]=distance;
                    pending.Add((distance,peer));
                }
            }
            Visit(seed,Mathf.Sqrt(closest));
            while (pending.Count > 0)
            {
                var item=pending.Min; pending.Remove(item);
                foreach (int next in neighbours[item.vertex])
                    Visit(next,item.distance+Vector3.Distance(initialPoints[item.vertex],initialPoints[next]));
            }
            return distances;
        }
    }
}
