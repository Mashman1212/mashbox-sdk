using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private Vector3[] PreparePolygonOutline(Vector3[] clicked,out bool[] fixedEdges,out (int,int)[] requiredSeams,out int[] layoutCorners)
        {
            float tolerance=Mathf.Max(.000001f,Active.reference.bounds.size.magnitude*.00001f);
            var routes=StudioRetopologyPatchBorders.Find(Active,clicked,tolerance);
            var outline=new List<Vector3>();var locked=new List<bool>();var seams=new HashSet<(int,int)>();var anchors=new List<int>();
            for(int side=0;side<clicked.Length;side++)
            {
                anchors.Add(outline.Count);
                var route=routes[side];
                if(route==null){outline.Add(clicked[side]);locked.Add(false);continue;}
                for(int i=0;i<route.Length-1;i++)
                {
                    outline.Add(Active.vertices[route[i]]);locked.Add(true);
                    seams.Add(StudioRetopologyOperations.Edge(route[i],route[i+1]));
                }
            }
            if(Document.symmetry)
            {
                var existing=StudioRetopologyOperations.Edges(Active);
                foreach(var edge in seams.ToArray())
                {
                    int Mirror(int id)
                    {
                        var point=Active.vertices[id];point.x=-point.x;
                        return Active.vertices.FindIndex(p=>(p-point).sqrMagnitude<=tolerance*tolerance);
                    }
                    int a=Mirror(edge.Item1),b=Mirror(edge.Item2);
                    if(a>=0&&b>=0&&existing.TryGetValue(StudioRetopologyOperations.Edge(a,b),out var faces)&&faces.Count==1)
                        seams.Add(StudioRetopologyOperations.Edge(a,b));
                }
            }
            requiredSeams=seams.ToArray();
            fixedEdges=locked.Any(value=>value)?locked.ToArray():null;
            layoutCorners=fixedEdges!=null&&clicked.Length==4?anchors.ToArray():null;
            return outline.ToArray();
        }
    }
}
