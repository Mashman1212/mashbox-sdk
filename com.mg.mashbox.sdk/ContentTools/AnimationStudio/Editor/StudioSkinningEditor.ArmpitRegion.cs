using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private float[] BuildArmpitTorsoRegion(Vector3[] points, Vector3[] restPoints, Vector3 center, Vector3 up, Vector3 across, float halfWidth, float height)
        {
            // Weld only for analysis: UV splits must not interrupt surface paths.
            var representatives=new Dictionary<Vector3,int>();
            var rep=new int[points.Length];
            for(int i=0;i<rep.Length;i++)
            {
                if(!representatives.TryGetValue(restPoints[i],out rep[i])) { rep[i]=i; representatives.Add(restPoints[i],i); }
            }
            var edges=new HashSet<int>[points.Length];
            foreach(int r in representatives.Values) edges[r]=new HashSet<int>();
            var triangles=Part.mesh.triangles;
            void Link(int a,int b) { a=rep[a]; b=rep[b]; if(a!=b) { edges[a].Add(b); edges[b].Add(a); } }
            for(int i=0;i<triangles.Length;i+=3) { Link(triangles[i],triangles[i+1]); Link(triangles[i+1],triangles[i+2]); Link(triangles[i+2],triangles[i]); }
            var torsoSeeds=new List<int>(); var sleeveSeeds=new List<int>();
            foreach(int r in representatives.Values)
            {
                var offset=points[r]-center;
                float lateral=Mathf.Abs(Vector3.Dot(offset,across))/halfWidth;
                float below=-Vector3.Dot(offset,up)/height;
                // Reliable interior anchors, not the uncertain nearest-body label
                // around the armpit. The transition is measured along the garment.
                if(lateral<=.68f && below>=.18f && below<=.85f) torsoSeeds.Add(r);
                if(lateral>=1.65f) sleeveSeeds.Add(r);
            }
            if(torsoSeeds.Count==0 || sleeveSeeds.Count==0)
                throw new InvalidOperationException("Could not identify torso and sleeve regions. Fit the shirt in A-pose before running armpit correction.");
            float[] Distances(List<int> seeds)
            {
                var result=new float[points.Length]; for(int i=0;i<result.Length;i++) result[i]=float.PositiveInfinity;
                var queue=new SortedSet<(float distance,int vertex)>();
                foreach(int seed in seeds) { result[seed]=0; queue.Add((0,seed)); }
                while(queue.Count>0)
                {
                    var item=queue.Min; queue.Remove(item);
                    foreach(int next in edges[item.vertex])
                    {
                        float d=item.distance+Vector3.Distance(points[item.vertex],points[next]);
                        if(d>=result[next]) continue;
                        queue.Remove((result[next],next)); result[next]=d; queue.Add((d,next));
                    }
                }
                return result;
            }
            var torsoDistance=Distances(torsoSeeds); var sleeveDistance=Distances(sleeveSeeds);
            var region=new float[points.Length];
            for(int i=0;i<region.Length;i++)
            {
                int r=rep[i]; float t=torsoDistance[r],s=sleeveDistance[r];
                // Disconnected accessories are left alone unless attached to both regions.
                if(float.IsInfinity(t)||float.IsInfinity(s)) continue;
                float share=s/Mathf.Max(1e-6f,t+s);
                region[i]=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.3f,.7f,share));
            }
            return region;
        }
    }
}
