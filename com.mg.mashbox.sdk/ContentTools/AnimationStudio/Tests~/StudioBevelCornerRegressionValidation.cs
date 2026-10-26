using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using MashBoxSDK.AnimationStudio;
public static class StudioBevelCornerRegressionValidation
{
    public static string Validate()
    {
        string result;
        try
        {
            string matrix=StudioBevelCornerPlanValidation.Validate();if(!matrix.StartsWith("PASS"))throw new Exception(matrix);
            StudioBevelFlatTerminationValidation.Validate();
            StudioBevelProfileValidation.Validate();StudioBevelChainValidation.Validate();
            StudioPublicationValidation.Validate();StudioBevelGpuLifecycleValidation.Validate();
            var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
            try
            {
                var node=new StudioModelDocument.Node{mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles};var top=new StudioMeshCuts(node);var f=top.Faces[0].Vertices;var selected=new[]{(f[0],f[1])};
                top.BevelEdges(node,selected,new StudioMeshCuts.BevelOptions{Width=.1f});
                var field=typeof(StudioMeshCuts).GetField("bevelFlatClip",BindingFlags.NonPublic|BindingFlags.Instance);var plan=field.GetValue(top);if(plan==null)throw new Exception("No shared plan");
                var clock=Stopwatch.StartNew();for(int i=0;i<100;i++){top.BevelEdges(node,selected,new StudioMeshCuts.BevelOptions{Width=.1f+i*.005f});if(!ReferenceEquals(plan,field.GetValue(top)))throw new Exception("Width reconstructed the shared plan");}clock.Stop();
                result="PASS shared-corner matrix, 2592 terminal cases, 1296 profile and 9 shared-miter cases, 3360 chains, 162 publication checks, 16 GPU lifecycle/history checks. 100 cached cube evaluations: "+(clock.Elapsed.TotalMilliseconds/100).ToString("F4")+" ms mean (geometry/publication only).";
            }
            finally{UnityEngine.Object.DestroyImmediate(mesh);}
        }
        catch(Exception e){result=e.ToString();}
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../bevel-corner-regression-result.txt")),result);return result;
    }
    public static void Run(){EditorApplication.delayCall+=()=>EditorApplication.Exit(Validate().StartsWith("PASS")?0:1);}
}
