using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MashBoxSDK.Cloth;
using UnityEditor;
using UnityEngine;
public static class StudioClothWindValidation
{
    public static readonly string Report=Path.Combine(Path.GetTempPath(),"mashbox-cloth-wind-validation.txt");
    [MenuItem("MashBox/Diagnostics/Validate cloth wind and flutter")]
    public static void Run()
    {
        var log=new List<string>();Mesh mesh=null, painted=null;MashBoxClothProfile profile=null;
        void Check(bool ok,string label){if(!ok)throw new Exception(label);log.Add("PASS "+label);}
        try
        {
            var wind=new MashBoxWindSettings{velocity=new Vector3(0,0,4),gust=.5f,frequency=1};
            Check((wind.Sample(0)-new Vector3(0,0,4)).sqrMagnitude<1e-8f,"Wind baseline");
            Check(Mathf.Abs(wind.Sample(.25f).z-6)<1e-5f,"Shared gust peak");
            Check(Mathf.Abs(wind.Sample(.75f).z-2)<1e-5f,"Shared gust trough");
            Check(default(MashBoxWindSettings).Sample(10)==Vector3.zero,"Wind off is zero");
            mesh=new Mesh();mesh.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up};mesh.triangles=new[]{0,1,2};mesh.RecalculateNormals();
            mesh.colors=new[]{new Color(.2f,.3f,.4f,.5f),new Color(.6f,.7f,.8f,.9f),Color.white};
            profile=ScriptableObject.CreateInstance<MashBoxClothProfile>();profile.Initialize(mesh);
            Check(Mathf.Abs(profile.flutter[0]-.2f)<1e-5f,"Existing vertex red imports into flutter");
            profile.flutter=new[]{0f,.5f,1f};painted=UnityEngine.Object.Instantiate(mesh);profile.ApplyFlutter(painted);
            var before=mesh.colors;var after=painted.colors;
            Check(after[0].r==0 && after[1].r==.5f && after[2].r==1,"Flutter paints red channel");
            Check(Enumerable.Range(0,3).All(i=>after[i].g==before[i].g&&after[i].b==before[i].b&&after[i].a==before[i].a),"Green blue alpha preserved");
            Check(Mathf.Abs(mesh.colors[0].r-.2f)<1e-5f,"Original mesh unchanged");
            profile.gravity=Vector3.zero;profile.bodyCollisions=false;profile.animationFollow=0;profile.animationInertia=0;profile.damping=0;
            float Move(float response,Vector3 velocity)
            {
                profile.windResponse=response;
                using(var solver=new MashBoxClothSolver(new MashBoxClothTopology(new[]{Vector3.zero},Array.Empty<int>(),32)))
                {
                    solver.SetTargets(new List<Vector3>{Vector3.zero},Matrix4x4.identity,new[]{10f});solver.Reset(true);
                    for(int i=0;i<10;i++)solver.Step(1f/30,i/30f,profile,null,0,null,velocity);
                    return solver.Position(0).z;
                }
            }
            Check(Mathf.Abs(Move(0,Vector3.forward*4))<1e-6f,"Zero garment response blocks wind");
            float forward=Move(1,Vector3.forward*4);
            Check(forward>.01f,"Global wind drives cloth");
            Check(Move(1,Vector3.back*4)<-.01f,"Global wind direction reverses cloth response");
            Check(Mathf.Abs(Move(1,Vector3.zero))<1e-6f,"Wind-off leaves stationary cloth still");
            foreach(var path in new[]{"Packages/com.mg.mashbox.sdk/Shaders/HDRP/Lit/Clothing/MG_Clothing.shadergraph","Packages/com.mg.mashbox.sdk/Shaders/HDRP/Lit/MashBoxStudio/AnimationStudioPreview.shader"})
            {
                var shader=AssetDatabase.LoadAssetAtPath<Shader>(path);Check(shader,"Shader imported: "+path);
                var material=new Material(shader);
                try {Check(material.HasProperty("_MGFlutterAmplitude")&&material.HasProperty("_MGWindPreview"),"Shader exposes flutter and preview override");material.SetPass(0);}
                finally{UnityEngine.Object.DestroyImmediate(material);}
                var errors=ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity.ToString()=="Error").ToArray();
                Check(errors.Length==0,"No shader errors: "+string.Join("; ",errors.Select(m=>m.message)));
            }
            log.Add("SUCCESS");
        }
        catch(Exception e){log.Add("FAIL "+e);}
        finally
        {
            if(profile)UnityEngine.Object.DestroyImmediate(profile);if(painted)UnityEngine.Object.DestroyImmediate(painted);if(mesh)UnityEngine.Object.DestroyImmediate(mesh);
            File.WriteAllLines(Report,log);
        }
    }
}
