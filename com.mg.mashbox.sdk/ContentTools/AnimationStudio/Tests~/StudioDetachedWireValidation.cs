using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioDetachedWireValidation
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static StudioModelEditor editor;static AnimationStudioWindow host;static MashBoxStudioViewport view;static StudioModelDocument doc;
        static int phase,checks;static double ready;static readonly List<StudioModelDocument> docs=new List<StudioModelDocument>();
        static string Out(string n)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../detached-wire-"+n));
        static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
        static object Call(string name,params object[] args)=>typeof(StudioModelEditor).GetMethod(name,F).Invoke(editor,args);
        [InitializeOnLoadMethod]static void Boot(){if(Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets")&&File.Exists(Out("request.txt")))EditorApplication.delayCall+=Run;}
        static StudioModelDocument.Node Grid()
        {
            var mesh=new Mesh{name="Shared grid"};mesh.vertices=Enumerable.Range(0,9).Select(i=>new Vector3(i%3,i/3,0)).ToArray();var triangles=new List<int>();var faces=new List<int>();
            for(int y=0;y<2;y++)for(int x=0;x<2;x++){int a=y*3+x,b=a+3,c=b+1,d=a+1;faces.AddRange(new[]{0,4,a,b,c,d});triangles.AddRange(new[]{a,b,c,a,c,d});}
            mesh.triangles=triangles.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
            return new StudioModelDocument.Node{name="Grid",mesh=mesh,geometryVertices=Enumerable.Range(0,9).ToArray(),cutPolygons=faces.ToArray(),cutTriangles=mesh.triangles,materials=new[]{StudioMeshMaterialPreview.DefaultMaterial}};
        }
        static void Setup()
        {
            doc=ScriptableObject.CreateInstance<StudioModelDocument>();docs.Add(doc);doc.nodes.Add(Grid());
            typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",F).Invoke(host,new object[]{doc});editor=host.ToolbarModelEditor;
            editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);editor.Components.Add(0);Call("EstablishSelectionBaseline");
        }
        static void VerifyWires(int index)
        {
            var node=doc.nodes[index];var topo=new StudioMeshCuts(node);var points=node.mesh.vertices;var lines=editor.SelectionWireframeLines(index);
            foreach(var face in topo.Faces)for(int e=0;e<face.Vertices.Count;e++)
            {
                var a=points[face.Vertices[e]];var b=points[face.Vertices[(e+1)%face.Vertices.Count]];
                Check(Enumerable.Range(0,lines.Length/2).Any(i=>lines[i*2]==a&&lines[i*2+1]==b||lines[i*2]==b&&lines[i*2+1]==a),"Missing authored boundary on mesh "+index);
            }
        }
        public static void Run()
        {
            if(!Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets"))return;File.Delete(Out("request.txt"));
            try{
                AnimationStudioWindow.Open();host=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(host,new object[]{true});
                view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio==host);view.position=new Rect(50,50,1200,800);view.Show();
                Setup();Call("Run",(Action)(()=>Call("EditSelectedFaces",false,false)),true);Check(editor.StatusText=="Ready",editor.StatusText);
                Check(editor.SelectionWireframeLines(0).Length==28,"Detached coincident boundaries were merged");
                var gesture=Call("CaptureMultiComponentGesture",new List<int>{0},Vector3.zero,Quaternion.identity,0);
                Call("ApplyMultiComponentGesture",gesture,new Vector3(3,0,0),Quaternion.identity,Vector3.one);Call("FlushModifierEdits");VerifyWires(0);
                editor.SetModelDrawMode(view,DrawCameraMode.TexturedWire);view.modelGridVisible=false;view.LookAtDirect(new Vector3(2,1,0),Quaternion.identity,3);view.Repaint();
                phase=0;ready=EditorApplication.timeSinceStartup+1.5;EditorApplication.update+=Tick;
            }catch(Exception e){Finish(e);}
        }
        static void Capture(string name)
        {
            var parent=typeof(EditorWindow).GetField("m_Parent",F).GetValue(view);var rect=new Rect(Vector2.zero,view.position.size);var rt=new RenderTexture((int)rect.width,(int)rect.height,0);rt.Create();var old=RenderTexture.active;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);
            try{parent.GetType().GetMethod("GrabPixels",F).Invoke(parent,new object[]{rt,rect});RenderTexture.active=rt;image.ReadPixels(rect,0,0);image.Apply();var raw=image.GetPixels32();var flipped=new Color32[raw.Length];for(int y=0;y<image.height;y++)Array.Copy(raw,y*image.width,flipped,(image.height-1-y)*image.width,image.width);image.SetPixels32(flipped);image.Apply();Check(flipped.Select(p=>p.r).Distinct().Count()>20,"Viewport did not render");File.WriteAllBytes(Out(name),image.EncodeToPNG());}
            finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
        }
        static void Tick()
        {
            if(EditorApplication.timeSinceStartup<ready)return;
            try{
                if(phase==0){Capture("detach.png");Setup();var source=doc.nodes[0].mesh;var triangles=source.triangles;
                    Call("Run",(Action)(()=>Call("EditSelectedFaces",true,true)),true);Check(editor.StatusText=="Ready",editor.StatusText);Check(doc.nodes.Count==2,"New-object duplicate did not create one object");
                    Check(source.triangles.SequenceEqual(triangles),"Duplicate changed source");doc.nodes[1].position+=Vector3.right*3;Call("Rebuild");editor.Changed();VerifyWires(0);VerifyWires(1);
                    Check(editor.SelectionWireframeLines(0).Length==24&&editor.SelectionWireframeLines(1).Length==8,"Duplicate produced ghost or missing edges");
                    editor.SetModelDrawMode(view,DrawCameraMode.TexturedWire);view.LookAtDirect(new Vector3(2,1,0),Quaternion.identity,3);view.Repaint();phase=1;ready=EditorApplication.timeSinceStartup+1.5;return;}
                Capture("duplicate-object.png");Finish(null);
            }catch(Exception e){Finish(e);}
        }
        static void Finish(Exception e){EditorApplication.update-=Tick;File.WriteAllText(Out("result.txt"),e==null?"PASS "+checks+" checks: detached boundaries cached before movement, every authored edge after movement, new-object source/output boundaries and native viewport captures. No exit or personal-profile writes.":"FAIL "+checks+": "+e);if(e!=null)Debug.LogException(e);}
    }
}
