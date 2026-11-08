using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    public sealed class StudioSplineScaleValidation:EditorWindow
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static readonly List<string> log=new List<string>();bool ran;
        static string Out(string file)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../SplineScale/"+file));
        public static void Begin(){if(!Application.dataPath.Replace('\\','/').EndsWith("/SymmetryProject/Assets"))throw new Exception("Disposable project only.");var w=GetWindow<StudioSplineScaleValidation>(true,"Sweep validation",false);w.position=new Rect(20,20,620,650);w.Repaint();}
        static void Check(bool value,string name){if(!value)throw new Exception(name);log.Add("PASS "+name);}
        static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,F).Invoke(o,args);
        static void Set(object o,string name,object value)=>o.GetType().GetField(name,F).SetValue(o,value);
        static T Get<T>(object o,string name)=>(T)o.GetType().GetField(name,F).GetValue(o);
        static StudioSpline Curve()=>new StudioSpline{kind=StudioSplineKind.Bezier,knots=new List<StudioSpline.Knot>{new StudioSpline.Knot{position=new Vector3(-2,0,0),outgoing=new Vector3(1,2,0)},new StudioSpline.Knot{position=new Vector3(2,0,0),incoming=new Vector3(-1,2,0)}}};
        static bool Same(Vector3[] a,Vector3[] b)=>a.Length==b.Length&&a.Zip(b,(x,y)=>(x-y).sqrMagnitude<1e-9f).All(x=>x);
        static byte[] RenderMesh(Mesh mesh,string file)
        {
            var preview=new PreviewRenderUtility();var material=new Material(Shader.Find("Standard"));material.color=new Color(.8f,.55f,.18f);
            try
            {
                preview.camera.transform.SetPositionAndRotation(new Vector3(0,1,-8),Quaternion.identity);preview.camera.fieldOfView=45;preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=100;
                preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.12f,.13f,.15f);
                preview.lights[0].intensity=1.4f;preview.lights[0].transform.rotation=Quaternion.Euler(35,30,0);preview.lights[1].intensity=.8f;
                preview.BeginPreview(new Rect(0,0,640,480),GUIStyle.none);preview.DrawMesh(mesh,Matrix4x4.identity,material,0);preview.Render();var texture=preview.EndPreview();
                var previous=RenderTexture.active;RenderTexture.active=texture as RenderTexture;var image=new Texture2D(640,480,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,640,480),0,0);image.Apply();var bytes=image.EncodeToPNG();File.WriteAllBytes(Out(file),bytes);DestroyImmediate(image);RenderTexture.active=previous;return bytes;
            }
            finally{DestroyImmediate(material);preview.Cleanup();}
        }


        void OnGUI()
        {
            if(ran||Event.current.type!=EventType.Repaint)return;ran=true;
            var doc=CreateInstance<StudioModelDocument>();StudioModelEditor editor=null;
            try
            {
                var source=new StudioModelDocument.Node{name="Path",spline=Curve()};doc.nodes.Add(source);
                editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(0);Call(editor,"EditSpline");
                var evt=new Event{type=EventType.KeyDown,keyCode=KeyCode.A,control=true};
                Check((bool)Call(editor,"SplineKey",evt),"Select All handled by spline editing");
                var selected=Get<HashSet<int>>(editor,"splineKnots");Check(selected.Count==source.spline.knots.Count,"Select All selects every spline knot");
                foreach(var pair in new[]{(KeyCode.R,Tool.Scale),(KeyCode.E,Tool.Rotate),(KeyCode.W,Tool.Move)})
                {
                    Event.current=new Event{type=EventType.KeyDown,keyCode=pair.Item1};
                    Check(editor.HandleModelKeyboard(null)&&Get<Tool>(editor,"tool")==pair.Item2&&selected.Count==source.spline.knots.Count&&Get<bool>(editor,"splineEditing"),"Native keyboard switches spline tool to "+pair.Item2+" without losing knots");
                }
                Event.current=new Event{type=EventType.Repaint};
                Call(editor,"ActivateTool",Tool.Scale);Check(Get<Tool>(editor,"tool")==Tool.Scale&&Get<bool>(editor,"splineEditing")&&selected.Count==source.spline.knots.Count,"Scale activation preserves all-knot editing selection");
                Check(editor.ToolOptionsTitle=="Scale Tool","Spline editing uses normal Scale Tool Options");
                Set(editor,"local",false);Set(editor,"preventNegativeScale",true);
                var before=source.spline.Copy();var center=(Vector3)Call(editor,"SplineSelectionCenter");Call(editor,"BeginSplineGesture",center);
                Call(editor,"ApplySplineGizmoTransform",Quaternion.identity,new Vector3(2,3,1));
                Check(source.spline.knots.Select((k,i)=>(k.position-(center+Vector3.Scale(before.knots[i].position-center,new Vector3(2,3,1)))).sqrMagnitude<1e-9f).All(x=>x),"All knots scale about their shared selection center");
                Check(source.spline.knots.Select((k,i)=>(k.outgoing-Vector3.Scale(before.knots[i].outgoing,new Vector3(2,3,1))).sqrMagnitude<1e-9f).All(x=>x),"Bezier tangent offsets scale with their knots");
                Call(editor,"ApplySplineGizmoTransform",Quaternion.identity,new Vector3(-2,0,2));
                var factor=Get<Vector3>(editor,"splineScale");Check(factor.x==1e-6f&&factor.y==1e-6f&&factor.z==2,"Negative and zero factors clamp to the shared positive minimum");
                Call(editor,"ApplySplineGizmoTransform",Quaternion.identity,Vector3.one);
                Check(source.spline.knots.Select((k,i)=>(k.position-before.knots[i].position).sqrMagnitude<1e-9f).All(x=>x),"Reversing the drag restores captured positions without accumulated error");
                Call(editor,"ApplySplineGizmoTransform",Quaternion.identity,new Vector3(2,2,2));Call(editor,"EndSplineGesture",false);
                UnityEditor.Undo.FlushUndoRecordObjects();StudioUndo.IncrementCurrentGroup();StudioUndo.PerformUndo();
                Check(doc.nodes[0].spline.knots.Select((k,i)=>(k.position-before.knots[i].position).sqrMagnitude<1e-9f).All(x=>x),"One Undo restores the entire scale gesture");
                StudioUndo.PerformRedo();Check((doc.nodes[0].spline.knots[0].position-before.knots[0].position).sqrMagnitude>1e-3,"Redo restores spline scaling");
                editor.SelectObject(0);Call(editor,"EditSpline");selected=Get<HashSet<int>>(editor,"splineKnots");selected.UnionWith(Enumerable.Range(0,doc.nodes[0].spline.knots.Count));Call(editor,"ActivateTool",Tool.Scale);
                before=doc.nodes[0].spline.Copy();center=(Vector3)Call(editor,"SplineSelectionCenter");Set(editor,"preventNegativeScale",false);Call(editor,"BeginSplineGesture",center);Call(editor,"ApplySplineGizmoTransform",Quaternion.identity,new Vector3(-1,1,1));
                Check(Get<Vector3>(editor,"splineScale").x==-1,"Disabling the shared setting permits intentional mirroring");Call(editor,"EndSplineGesture",true);
                Check(doc.nodes[0].spline.knots.Select((k,i)=>(k.position-before.knots[i].position).sqrMagnitude<1e-9f).All(x=>x),"Cancel restores the captured spline");
                editor.SelectObject(0);Call(editor,"OpenSweepTool",null,null);var output=doc.nodes.Last();var meshBefore=output.mesh.vertices;
                editor.SelectObject(0);Call(editor,"EditSpline");selected=Get<HashSet<int>>(editor,"splineKnots");selected.UnionWith(Enumerable.Range(0,doc.nodes[0].spline.knots.Count));Call(editor,"ActivateTool",Tool.Scale);center=(Vector3)Call(editor,"SplineSelectionCenter");Call(editor,"BeginSplineGesture",center);Call(editor,"ApplySplineGizmoTransform",Quaternion.identity,new Vector3(1.5f,1.5f,1.5f));
                Check(!Same(meshBefore,output.mesh.vertices)&&output.modifiers.operations.All(o=>string.IsNullOrEmpty(o.failure)),"Linked sweep updates during spline scale before release");Call(editor,"EndSplineGesture",false);
                editor.SelectObject(0);Call(editor,"EditSpline");selected=Get<HashSet<int>>(editor,"splineKnots");selected.Add(0);Call(editor,"ActivateTool",Tool.Scale);var untouched=doc.nodes[0].spline.knots[1].position;center=(Vector3)Call(editor,"SplineSelectionCenter");Call(editor,"BeginSplineGesture",center);Call(editor,"ApplySplineGizmoTransform",Quaternion.identity,Vector3.one*2);
                Check(doc.nodes[0].spline.knots[1].position==untouched,"Subset scaling leaves unselected knots unchanged");Call(editor,"EndSplineGesture",false);
            }
            catch(Exception ex){log.Add("FAIL "+ex);}
            finally{editor?.Dispose();DestroyImmediate(doc);File.WriteAllLines(Out("result.txt"),log);EditorApplication.delayCall+=()=>EditorApplication.Exit(log.Any(x=>x.StartsWith("FAIL"))?1:0);}
        }
    }
}
