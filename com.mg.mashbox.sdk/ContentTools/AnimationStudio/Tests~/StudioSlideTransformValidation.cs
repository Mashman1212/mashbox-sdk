using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace MashBoxSDK.AnimationStudio
{
    public sealed class StudioSlideTransformValidation : EditorWindow
    {
        static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static string Root=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../../ComponentSlide/GizmoFix"));
        static readonly List<string> log=new List<string>();
        static StudioSlideTransformValidation window;
        StudioModelEditor editor;StudioModelDocument document;Mesh mesh;Camera cameraView;Scene scene;
        SceneView testView;bool shown,rendered;double captureAt;Vector3 raw=new Vector3(1.6f,.6f,0),actual=new Vector3(0,.6f,0);
        static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,F).Invoke(o,a);
        static void Set(object o,string n,object v)=>o.GetType().GetField(n,F).SetValue(o,v);
        static T Get<T>(object o,string n)=>(T)o.GetType().GetField(n,F).GetValue(o);
        static void Check(bool pass,string name){if(!pass)throw new Exception(name);if(!log.Contains("PASS "+name))log.Add("PASS "+name);}
        static bool Near(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<1e-8f;
        [InitializeOnLoadMethod] static void Boot()
        {
            if(!Application.dataPath.Replace('\\','/').EndsWith("/Splines/SplineProject/Assets"))return;
            EditorApplication.delayCall+=()=>{string request=Path.Combine(Root,"request.txt");if(File.Exists(request)){File.Delete(request);Begin();}};
        }
        static void Begin()
        {
            try
            {
                window=CreateInstance<StudioSlideTransformValidation>();
                window.mesh=new Mesh();window.mesh.vertices=Source();window.mesh.triangles=new[]{0,1,2,0,2,3};window.mesh.RecalculateNormals();
                window.document=CreateInstance<StudioModelDocument>();window.document.nodes.Add(new StudioModelDocument.Node{name="Slide",mesh=window.mesh,cutPolygons=new[]{0,4,0,1,2,3},cutTriangles=window.mesh.triangles,geometryVertices=new[]{0,1,2,3}});
                window.editor=new StudioModelEditor(()=>{});window.editor.Load(window.document);window.editor.SelectObject(0);Set(window.editor,"mode",StudioModelEditor.ComponentMode.Vertex);window.editor.Components.Add(0);window.editor.Components.Add(1);
                Core();
                window.testView=CreateInstance<SceneView>();window.testView.position=new Rect(90,90,640,440);window.testView.Show();window.testView.orthographic=true;window.testView.LookAtDirect(Vector3.zero,Quaternion.identity,3);window.cameraView=window.testView.camera;
                SceneView.duringSceneGui+=window.Draw;
                StudioModelEditor.MoveHandleProbe=(center,size)=>{if(Event.current.type==EventType.Repaint){Check(Near(center,window.actual),"Native Repaint anchors gizmo at constrained center");window.rendered=true;StudioModelEditor.MoveHandleProbe=null;}};
                window.testView.Repaint();window.captureAt=EditorApplication.timeSinceStartup+1;EditorApplication.update+=Capture;
            }
            catch(Exception e){Finish("FAIL "+e);}
        }
        static Vector3[] Source()=>new[]{new Vector3(-1,1,0),new Vector3(1,1,0),new Vector3(1,3,0),new Vector3(-1,3,0)};
        static void Core()
        {
            var e=window.editor;var mesh=window.mesh;var source=Source();var selected=new HashSet<int>{0,1};
            Set(e,"tool",Tool.Move);
            var drag=Call(e,"CaptureMoveDrag",1,new Vector3(0,1,0),Quaternion.identity,selected,source,true);Set(e,"moveDrag",drag);
            Call(e,"ApplyMoveDrag",new Vector3(1,2,0));
            Check(Near(Get<Vector3>(drag,"position"),new Vector3(1,2,0))&&Near(Get<Vector3>(drag,"displayPosition"),new Vector3(0,2,0)),"Move retains raw input while display follows constrained edge");
            Call(e,"ApplyMoveDrag",new Vector3(5,10,0));Check(Near(Get<Vector3>(drag,"displayPosition"),new Vector3(0,3,0)),"Displayed Move center stops at clamped endpoint");
            Call(e,"ApplyMoveDrag",new Vector3(.5f,1.5f,0));Check(Near(mesh.vertices[0],new Vector3(-1,1.5f,0)),"Move reverses continuously after endpoint clamp");
            foreach(var tool in new[]{Tool.Scale,Tool.Rotate})
            {
                mesh.vertices=Source();Call(e,"PositionsChanged",false,null,null);Set(e,"tool",tool);Set(e,"local",false);Call(e,"SyncComponentTarget");
                var gesture=Call(e,"CaptureMultiComponentGesture",new List<int>{0},Vector3.zero,Quaternion.identity,1,true);
                var rotation=tool==Tool.Rotate?Quaternion.Euler(0,0,30):Quaternion.identity;var scale=tool==Tool.Scale?Vector3.one*2:Vector3.one;
                Call(e,"ApplyMultiComponentGesture",gesture,Vector3.zero,rotation,scale);
                var values=e.ActiveMesh.vertices;log.Add(tool+" values "+string.Join(";",values.Select(v=>v.ToString())));
                Check(Mathf.Abs(values[0].x+1)<1e-5f&&Mathf.Abs(values[1].x-1)<1e-5f&&values[1].y>1.2f,tool+" projects transformed positions onto captured authored rails");
                if(tool==Tool.Scale)Check(Mathf.Abs(values[0].y-2)<1e-5f&&Mathf.Abs(values[1].y-2)<1e-5f,"Scale uses captured total factor");
                Check(Near(Get<Vector3>(gesture,"displayPosition"),(values[0]+values[1]-source[0]-source[1])*.5f),tool+" displayed center follows actual selected displacement");
            }
            Set(e,"tool",Tool.Move);Set(e,"moveDrag",null);
            Check(!Get<bool>(e,"slideComponents"),"Slide toggle initially off without preference writes");
            Set(e,"slideComponents",true);foreach(var t in new[]{Tool.Move,Tool.Rotate,Tool.Scale}){Set(e,"tool",t);Check((bool)typeof(StudioModelEditor).GetProperty("SlideComponentsEnabled",F).GetValue(e),t+" shares Slide toggle");}
            Set(e,"tool",Tool.Move);
        }
        void Draw(SceneView drawingView)
        {
            if(drawingView!=testView||!cameraView)return;
            try
            {
                cameraView.pixelRect=new Rect(0,0,640,440);
                Handles.SetCamera(new Rect(0,0,640,440),cameraView);
                if(Event.current.type==EventType.Repaint)
                {
                    var oldTarget=cameraView.targetTexture;var oldActive=RenderTexture.active;
                    var rt=new RenderTexture(640,440,24,RenderTextureFormat.ARGB32);rt.Create();
                    cameraView.targetTexture=rt;RenderTexture.active=rt;GL.PushMatrix();
                    try
                    {
                        GL.Clear(true,true,new Color(.15f,.15f,.15f,1));GL.LoadProjectionMatrix(cameraView.projectionMatrix);GL.modelview=cameraView.worldToCameraMatrix;
                        Handles.color=Color.white;Handles.DrawLine(new Vector3(0,-1.8f,0),new Vector3(0,1.8f,0));
                        Handles.color=Color.magenta;Handles.DrawWireDisc(raw,Vector3.forward,.09f);
                        Call(editor,"DrawSlidingMoveHandle",raw,Quaternion.identity,1f,(Vector3?)actual);
                        var texture=new Texture2D(640,440,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,640,440),0,0);texture.Apply();
                        Check(texture.GetPixels32().Count(c=>c.r>180||c.g>180||c.b>180)>50,"Native gizmo render contains visible strokes");
                        File.WriteAllBytes(Path.Combine(Root,"gizmo.png"),texture.EncodeToPNG());DestroyImmediate(texture);
                    }
                    finally{GL.PopMatrix();cameraView.targetTexture=oldTarget;RenderTexture.active=oldActive;DestroyImmediate(rt);}
                }
                if(Event.current.type==EventType.Layout)
                {
                    // The Layout query must remain at the unconstrained input frame.
                    StudioModelEditor.MoveHandleProbe=(p,s)=>{Check(Near(p,raw),"Native Layout retains original input frame");StudioModelEditor.MoveHandleProbe=null;};
                    Call(editor,"DrawSlidingMoveHandle",raw,Quaternion.identity,1f,(Vector3?)actual);
                    StudioModelEditor.MoveHandleProbe=(p,s)=>{if(Event.current.type==EventType.Repaint){Check(Near(p,actual),"Native Repaint follows constrained center");rendered=true;StudioModelEditor.MoveHandleProbe=null;}};
                }
            }
            catch(Exception e){Finish("FAIL "+e);}
        }
        static void Capture()
        {
            if(!window||!window.rendered||EditorApplication.timeSinceStartup<window.captureAt)return;
            try
            {
                Finish("PASS "+log.Count+" checks");
            }
            catch(Exception e){Finish("FAIL "+e);}
        }
        static void Finish(string result)
        {
            EditorApplication.update-=Capture;StudioModelEditor.MoveHandleProbe=null;log.Add(result);File.WriteAllLines(Path.Combine(Root,"result.txt"),log);
            var old=window;window=null;if(!old)return;SceneView.duringSceneGui-=old.Draw;
            EditorApplication.delayCall+=()=>{old.editor?.Dispose();if(old.document)DestroyImmediate(old.document);if(old.mesh)DestroyImmediate(old.mesh);if(old.scene.IsValid())EditorSceneManager.ClosePreviewScene(old.scene);if(old.testView)old.testView.Close();DestroyImmediate(old);};
        }
    }
}






