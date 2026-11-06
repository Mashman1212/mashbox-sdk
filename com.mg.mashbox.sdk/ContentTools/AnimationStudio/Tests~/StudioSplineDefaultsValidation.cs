using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    public static class StudioSplineDefaultsValidation
    {
        static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../../SplineDefaults/"+name));
        [InitializeOnLoadMethod] static void Boot(){if(Application.dataPath.Replace('\\','/').EndsWith("/Splines/SplineProject/Assets")&&File.Exists(Out("request.txt")))EditorApplication.delayCall+=Run;}
        public static void Run()
        {
            if(!Application.dataPath.Replace('\\','/').EndsWith("/Splines/SplineProject/Assets"))throw new Exception("Disposable spline project only.");
            File.Delete(Out("request.txt"));var log=new List<string>();void Check(bool b,string label){if(!b)throw new Exception(label);log.Add("PASS "+label);}
            try {
                var s=new StudioSpline{kind=StudioSplineKind.Bezier,knots=new List<StudioSpline.Knot>{new StudioSpline.Knot{position=Vector3.left,automatic=true},new StudioSpline.Knot{position=Vector3.up,automatic=true},new StudioSpline.Knot{position=Vector3.right,automatic=true}}};s.RefreshAutomaticTangents();
                Check(s.knots[1].outgoing.sqrMagnitude>.01f && (s.knots[1].incoming+s.knots[1].outgoing).sqrMagnitude<1e-10f,"Click-created Bezier knots have smooth nonzero tangent pairs");
                var old=s.knots[1].outgoing;s.knots[2].position+=Vector3.up;s.RefreshAutomaticTangents();Check(s.knots[1].outgoing!=old,"Automatic tangents update after neighboring knot movement");
                s.SetHandle(1,false,new Vector3(2,3,0));old=s.knots[1].outgoing;s.knots[2].position+=Vector3.up;s.RefreshAutomaticTangents();Check(!s.knots[1].automatic&&s.knots[1].outgoing==old,"Manual handle edits survive subsequent automatic smoothing");
                var before=s.Copy();s.Insert(0,.4f);s.RefreshAutomaticTangents();Check(Enumerable.Range(0,41).All(i=>{float t=i/40f;var p=t<.4f?s.Evaluate(0,t/.4f):s.Evaluate(1,(t-.4f)/.6f);return (p-before.Evaluate(0,t)).sqrMagnitude<1e-9f;}),"Automatic curve insertion preserves exact Bezier shape");
                var linear=before.Copy();linear.SetKind(StudioSplineKind.Linear);linear.SetKind(StudioSplineKind.Bezier);Check(linear.knots.All(k=>k.automatic)&&linear.knots[1].tangent==StudioSplineTangent.Aligned,"Bezier conversion creates automatic smooth tangents");
                Check(!MashBoxStudioViewport.FactoryToolbarVisible(StudioCurvesToolbar.Id)&&MashBoxStudioViewport.FactoryToolbarVisible(StudioMeshToolsToolbar.Id),"Curves factory visibility is off without changing Mesh Tools");
                var editor=new StudioModelEditor(()=>{});try{var commands=editor.ToolbarCommands(null);Check(!commands.Any(c=>c.Key=="Curves/Object Mode")&&commands.Any(c=>c.Key=="Select/Object Mode"),"Curves has no duplicate Object Mode; standard Object Mode remains");}finally{editor.Dispose();}
                var icon=StudioCommandIcons.Get("Hierarchy/Spline");Check(icon&&icon==StudioCommandIcons.Get("Hierarchy/Spline"),"Hierarchy S-curve icon uses the shared cache");
                var rt=RenderTexture.GetTemporary(96,96,0);var previous=RenderTexture.active;Graphics.Blit(icon,rt);RenderTexture.active=rt;var image=new Texture2D(96,96,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,96,96),0,0);image.Apply();File.WriteAllBytes(Out("hierarchy-spline.png"),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);
                var copy=JsonUtility.FromJson<StudioSpline>(JsonUtility.ToJson(linear));Check(copy.knots.All(k=>k.automatic),"Automatic tangent state survives serialization");
            }catch(Exception e){log.Add("FAIL "+e);}File.WriteAllLines(Out("result.txt"),log);EditorApplication.Exit(log.Any(x=>x.StartsWith("FAIL"))?1:0);
        }
    }
}
