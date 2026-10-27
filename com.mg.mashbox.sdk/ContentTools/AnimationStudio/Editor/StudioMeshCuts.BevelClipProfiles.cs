using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        private sealed class ClipProfileLayout
        {
            internal List<FlatClipPolygon> Source;
            internal readonly Dictionary<(int,int),int> Controls=new Dictionary<(int,int),int>();
            internal readonly Dictionary<FlatClipPolygon,int> Starts=new Dictionary<FlatClipPolygon,int>();
        }
        private ClipProfileLayout clipProfileLayout;
        private bool bevelUseClipping;

        private static Vector3 ClipProfileSample(Vector3 a,Vector3 b,Vector3 control,int sample,int steps,float tension)
        {
            // Integer complementary weights make reversing a shared cross-section
            // bit-identical. Round each coordinate only once at publication precision.
            var left=new ClipVector(a);var right=new ClipVector(b);
            return BevelConicProfile(left,right,new ClipVector(control),(steps-sample)/(double)steps,sample/(double)steps,tension).Float;
        }

        // Rational quadratic conic: the middle weight is sin(half the control
        // angle). Equal tangent lengths therefore give an exact circular fillet.
        // See the Circular Arcs reference in BEVEL_ENGINE.md.
        private static double BevelConicWeight(ClipVector a,ClipVector b,ClipVector control)
        {
            var x=a-control;var y=b-control;double length=Math.Sqrt(x.Squared*y.Squared);
            if(length<=1e-30)return 1;
            double dot=Math.Max(-1,Math.Min(1,x.Dot(y)/length));
            return Math.Sqrt(Math.Max(1e-12,(1-dot)*.5));
        }
        private static ClipVector BevelConicProfile(ClipVector a,ClipVector b,ClipVector control,double u,double t,float tension)
        {
            var linear=a*u+b*t;if(tension==0)return linear;
            double w=BevelConicWeight(a,b,control),middle=2*w*u*t,denominator=u*u+middle+t*t;
            var curve=(a*(u*u)+b*(t*t)+control*middle)*(1/denominator);
            return linear+(curve-linear)*tension;
        }

        private bool PrepareClipProfileLayout(FlatClipPlan plan)
        {
            if(clipProfileLayout!=null&&ReferenceEquals(clipProfileLayout.Source,plan.Output))return true;
            var layout=new ClipProfileLayout{Source=plan.Output};
            var uses=new Dictionary<(int,int),List<FlatClipPolygon>>();
            foreach(var polygon in plan.Output)for(int i=0;i<polygon.Points.Count;i++)
            {
                var key=BevelKey(polygon.Points[i],polygon.Points[(i+1)%polygon.Points.Count]);
                if(!uses.TryGetValue(key,out var list)){list=new List<FlatClipPolygon>();uses.Add(key,list);}list.Add(polygon);
            }
            foreach(var polygon in plan.Output)
            {
                if(polygon.Plane<0)continue;
                if(polygon.Points.Count!=4)return false;
                var selected=plan.Planes[polygon.Plane].Edge;var sections=new List<int>();
                var edge=bevelEdges[selected];
                for(int i=0;i<4;i++)
                {
                    var key=BevelKey(polygon.Points[i],polygon.Points[(i+1)%4]);
                    if(!uses.TryGetValue(key,out var adjacent)||adjacent.Count!=2)return false;
                    var other=ReferenceEquals(adjacent[0],polygon)?adjacent[1]:adjacent[0];int owner=-1;
                    if(other.Plane>=0)
                    {
                        var target=plan.Planes[other.Plane].Edge;
                        if(selected.Item1==target.Item1||selected.Item1==target.Item2)owner=selected.Item1;
                        else if(selected.Item2==target.Item1||selected.Item2==target.Item2)owner=selected.Item2;
                        else return false;
                    }
                    else
                    {
                        bool rail=false;foreach(var use in edge.Uses)rail|=use.face==other.Source;
                        if(rail)continue;
                        bool a=false,b=false;foreach(int render in Faces[other.Source].Vertices){a|=welded[render]==selected.Item1;b|=welded[render]==selected.Item2;}
                        if(a==b)return false;owner=a?selected.Item1:selected.Item2;
                    }
                    if(layout.Controls.TryGetValue(key,out int existing)&&existing!=owner)return false;
                    layout.Controls[key]=owner;sections.Add(i);
                }
                if(sections.Count!=2||(sections[1]-sections[0])!=2)return false;
                layout.Starts.Add(polygon,sections[0]);
            }
            clipProfileLayout=layout;return true;
        }

        private void EmitClipProfiles(BevelDraft draft,FlatClipPlan plan,int[] points,BevelOptions options,
            Action<List<int>,int,int,List<Vector3>,bool> add)
        {
            int steps=options.Subdivisions+1;
            var profiles=new Dictionary<(int,int),int[]>();
            foreach(var pair in clipProfileLayout.Controls)
            {
                var ids=new int[steps+1];ids[0]=points[pair.Key.Item1];ids[steps]=points[pair.Key.Item2];
                for(int k=1;k<steps;k++)ids[k]=draft.AddPoint(ClipProfileSample(plan.FloatPoints[pair.Key.Item1],plan.FloatPoints[pair.Key.Item2],plan.Wave.Originals[pair.Value],k,steps,options.Tension),pair.Value);
                profiles.Add(pair.Key,ids);
            }
            int[] Path(int a,int b)
            {
                var key=BevelKey(a,b);var ids=profiles[key];if(a==key.Item1)return ids;
                var reverse=(int[])ids.Clone();Array.Reverse(reverse);return reverse;
            }
            foreach(var polygon in plan.Output)
            {
                if(polygon.Plane<0)
                {
                    var loop=new List<int>();for(int i=0;i<polygon.Points.Count;i++)
                    {
                        int a=polygon.Points[i],b=polygon.Points[(i+1)%polygon.Points.Count];loop.Add(points[a]);
                        if(profiles.ContainsKey(BevelKey(a,b))){var path=Path(a,b);for(int k=1;k<steps;k++)loop.Add(path[k]);}
                    }
                    add(loop,polygon.Source,polygon.Material,null,true);continue;
                }
                int start=clipProfileLayout.Starts[polygon];int a0=polygon.Points[start],a1=polygon.Points[(start+1)%4],b0=polygon.Points[(start+3)%4],b1=polygon.Points[(start+2)%4];
                var left=Path(a0,a1);var right=Path(b0,b1);var controlLeft=plan.Wave.Originals[clipProfileLayout.Controls[BevelKey(a0,a1)]];var controlRight=plan.Wave.Originals[clipProfileLayout.Controls[BevelKey(b0,b1)]];
                var normal=plan.Planes[polygon.Plane].CapNormal;
                for(int k=0;k<steps;k++)
                {
                    var loop=new List<int>{left[k],left[k+1],right[k+1],right[k]};var normals=new List<Vector3>();
                    foreach(var sample in new[]{(true,k),(true,k+1),(false,k+1),(false,k)})
                    {
                        int j=sample.Item2;var along=draft.Positions[right[j]]-draft.Positions[left[j]];
                        normals.Add(BevelProfileNormal(draft.Positions[sample.Item1?left[0]:right[0]],draft.Positions[sample.Item1?left[steps]:right[steps]],sample.Item1?controlLeft:controlRight,along,normal,j/(float)steps,options.Tension));
                    }
                    add(loop,polygon.Source,polygon.Material,normals,false);
                }
                // Keep the existing outer rail selection, not each new profile loop.
                draft.Selection.Add(BevelKey(left[0],right[0]));
            }
        }
    }
}
