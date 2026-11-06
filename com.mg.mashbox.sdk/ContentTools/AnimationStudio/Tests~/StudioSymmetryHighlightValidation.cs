using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioSymmetryHighlightValidation
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static readonly List<string> report=new List<string>();static int failures;
        static string Root=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../SymmetryHighlight"));
        static object Call(object target,string name,params object[] args)
        {try{return target.GetType().GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(target,args);}catch(TargetInvocationException e){throw e.InnerException;}}
        static void Set(object target,string field,object value)=>target.GetType().GetField(field,F).SetValue(target,value);
        static object Get(object target,string field)=>target.GetType().GetField(field,F)?.GetValue(target) ?? target.GetType().GetProperty(field,F)?.GetValue(target);
        static void Check(bool condition,string label){if(!condition)throw new Exception(label);report.Add("PASS "+label);}
        static void Case(string label,Action action){try{action();}catch(Exception e){failures++;report.Add("FAIL "+label+": "+e);}}
        static void Cube(Action<StudioModelEditor,StudioModelDocument,StudioModelDocument.Node> action)
        {
            var doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.SetWorkspacePath(Path.GetFullPath(Path.Combine(Application.dataPath,"../MashBoxStudio/Workspaces/highlight-"+Guid.NewGuid().ToString("N")+".workspace")));doc.AddPrimitive(PrimitiveType.Cube);
            try{using(var editor=new StudioModelEditor(()=>{})){editor.Load(doc);editor.SelectObject(0);Set(editor,"symmetryEnabled",true);action(editor,doc,doc.nodes[0]);}}
            finally{foreach(var mesh in doc.nodes.Select(n=>n.mesh).Where(m=>m).Distinct())UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(doc);}
        }
        static int PositiveFace(StudioModelDocument.Node node){var cuts=new StudioMeshCuts(node);return Enumerable.Range(0,cuts.Faces.Count).OrderByDescending(i=>cuts.Faces[i].Vertices.Average(v=>cuts.Positions[v].x)).First();}
        static void Render(StudioModelEditor editor,StudioModelDocument.Node node)
        {
            var old=node.mesh;var points=new[]{new Vector3(1,-.5f,0),new Vector3(2,-.5f,0),new Vector3(2,.5f,0),new Vector3(1,.5f,0),new Vector3(-1,-.5f,0),new Vector3(-1,.5f,0),new Vector3(-2,.5f,0),new Vector3(-2,-.5f,0)};
            var mesh=new Mesh{vertices=points,triangles=new[]{0,1,2,0,2,3,4,5,6,4,6,7}};mesh.RecalculateNormals();mesh.RecalculateBounds();node.mesh=mesh;node.cutPolygons=new[]{0,4,0,1,2,3,0,4,4,5,6,7};node.cutTriangles=mesh.triangles;node.geometryVertices=Enumerable.Range(0,8).ToArray();node.modifiers=null;node.normalGroups=null;
            Call(editor,"Rebuild");UnityEngine.Object.DestroyImmediate(old);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);editor.Components.Add(0);
            var scene=(UnityEngine.SceneManagement.Scene)typeof(StudioModelEditor).GetProperty("Scene",F).GetValue(editor);
            var transforms=(Transform[])typeof(StudioModelEditor).GetProperty("Transforms",F).GetValue(editor);
            var material=new Material(Shader.Find("Unlit/Color"));material.color=new Color(.4f,.4f,.4f,1);
            foreach(var t in transforms){t.gameObject.layer=31;var renderer=t.GetComponent<Renderer>();if(renderer)renderer.sharedMaterial=material;}
            var view=ScriptableObject.CreateInstance<SceneView>();var camera=view.camera;
            if(!camera)throw new Exception("Disposable native SceneView camera unavailable");
            camera.scene=scene;camera.cullingMask=1<<31;camera.transform.position=new Vector3(0,0,5);camera.transform.LookAt(Vector3.zero);camera.orthographic=true;camera.orthographicSize=1.3f;camera.aspect=2;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.08f,.08f,1);camera.nearClipPlane=.01f;camera.farClipPlane=100;
            var rt=new RenderTexture(512,256,24);rt.Create();camera.targetTexture=rt;var prior=RenderTexture.active;var pipeline=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
            Camera.CameraCallback overlay=c=>{if(c==camera)Call(editor,"DrawComponentBatches",view);};Camera.onPostRender+=overlay;
            Color[] off=null,on=null;
            try
            {
                GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;
                foreach(bool enabled in new[]{false,true})
                {
                    Set(editor,"symmetryEnabled",enabled);camera.Render();RenderTexture.active=rt;var image=new Texture2D(512,256,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,512,256),0,0);image.Apply();
                    File.WriteAllBytes(Path.Combine(Root,enabled?"highlight-on.png":"highlight-off.png"),image.EncodeToPNG());if(enabled)on=image.GetPixels();else off=image.GetPixels();UnityEngine.Object.DestroyImmediate(image);
                }
                float Difference(Vector3 point)
                {
                    var screen=camera.WorldToScreenPoint(point);float result=0;
                    for(int y=(int)screen.y-5;y<(int)screen.y+5;y++)for(int x=(int)screen.x-5;x<(int)screen.x+5;x++){var a=off[y*512+x];var b=on[y*512+x];result+=Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b);}return result/100;
                }
                Check(Difference(new Vector3(-1.5f,0,0))>.05f,"Native component face renderer adds visible highlight on the mirrored quad");
                Check(Difference(new Vector3(1.5f,0,0))<.01f,"Native render retains the primary face highlight");
                Check(editor.Components.SetEquals(new[]{0}),"Rendering both highlights leaves primary selection untouched");
            }
            finally{Camera.onPostRender-=overlay;GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;RenderTexture.active=prior;camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(view);UnityEngine.Object.DestroyImmediate(material);}
        }
        public static void Run()
        {
            if(!Application.dataPath.Replace('\\','/').EndsWith("/SymmetryProject/Assets"))throw new Exception("Use the disposable SymmetryProject only.");Directory.CreateDirectory(Root);
            foreach(var mode in new[]{StudioModelEditor.ComponentMode.Vertex,StudioModelEditor.ComponentMode.Edge,StudioModelEditor.ComponentMode.Face})
                Case(mode.ToString(),()=>Cube((editor,doc,node)=>{
                    editor.SetToolbarComponentMode(mode);var positions=node.mesh.vertices;int selected;
                    if(mode==StudioModelEditor.ComponentMode.Face)selected=PositiveFace(node);
                    else if(mode==StudioModelEditor.ComponentMode.Vertex)selected=Array.FindIndex(positions,p=>p.x>0);
                    else {var edges=(List<(int a,int b)>)Call(editor,"Edges");selected=edges.FindIndex(e=>positions[e.a].x>0&&positions[e.b].x>0);}
                    editor.Components.Add(selected);var before=editor.Components.ToArray();var pivot=(Vector3)Call(editor,"ComponentSelectionCenter");
                    var displayed=(HashSet<int>)Call(editor,"SymmetryDisplayComponents");
                    Check(displayed.Contains(selected)&&displayed.Count>before.Length,mode+" display includes the matched counterpart");
                    Check(editor.Components.SetEquals(before)&&(Vector3)Call(editor,"ComponentSelectionCenter")==pivot,mode+" primary selection and gizmo center stay unchanged");
                    int builds=editor.SymmetryDisplayBuilds;Call(editor,"SymmetryDisplayComponents");Check(editor.SymmetryDisplayBuilds==builds,mode+" unchanged selection reuses correspondence cache");
                    Call(editor,"EnsureComponentDrawing");Check(((HashSet<int>)Get(editor,"drawnComponents")).SetEquals(displayed),mode+" existing selection renderer receives both sides");
                    if(mode==StudioModelEditor.ComponentMode.Vertex)
                    {
                        Check((int)Get(editor,"markerSelectionCount")==displayed.Count,"GPU vertex marker selection buffer includes mirrored aliases");
                        int uploads=editor.VertexMarkerSelectionUploads;Call(editor,"EnsureComponentDrawing");Check(editor.VertexMarkerSelectionUploads==uploads,"Unchanged marker selection does not upload again");
                    }
                    if(mode==StudioModelEditor.ComponentMode.Face)Check(((Mesh)Get(editor,"componentHighlight")).GetIndexCount(0)==12,"Face highlight contains both authored quad tessellations");
                    Set(editor,"symmetryEnabled",false);Call(editor,"EnsureComponentDrawing");Check(((HashSet<int>)Get(editor,"drawnComponents")).SetEquals(before),mode+" disabling symmetry removes only counterpart display");
                    Set(editor,"symmetryEnabled",true);editor.Components.Clear();Call(editor,"EnsureComponentDrawing");Check(((HashSet<int>)Get(editor,"drawnComponents")).Count==0,mode+" deselect clears both highlights");
                }));
            Case("invalid targets and changes",()=>Cube((editor,doc,node)=>{
                editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Vertex);editor.Components.Add(0);Call(editor,"SymmetryDisplayComponents");int builds=editor.SymmetryDisplayBuilds;
                Set(editor,"symmetryAxis",1);Call(editor,"SymmetryDisplayComponents");Check(editor.SymmetryDisplayBuilds==builds+1,"Axis changes invalidate correspondence once");
                Set(editor,"symmetryWorld",true);Call(editor,"SymmetryDisplayComponents");Check(editor.SymmetryDisplayBuilds==builds+2,"Position-space changes invalidate correspondence once");
                doc.nodes.Add(new StudioModelDocument.Node{mesh=node.mesh});var actual=(HashSet<int>)Call(editor,"SymmetryDisplayComponents");Check(actual.SetEquals(editor.Components)&&Get(editor,"error")==null,"Unsupported linked meshes keep primary display without repaint errors");doc.nodes.RemoveAt(1);
                ((HashSet<int>)Get(editor,"objects")).Add(1);actual=(HashSet<int>)Call(editor,"SymmetryDisplayComponents");Check(ReferenceEquals(actual,editor.Components),"Multiple object selection receives no mirrored display");((HashSet<int>)Get(editor,"objects")).Remove(1);
            }));
            Case("native rendered face highlights",()=>Cube((editor,doc,node)=>Render(editor,node)));
            File.WriteAllLines(Path.Combine(Root,"result.txt"),report.Concat(new[]{"Failures: "+failures}));EditorApplication.Exit(failures==0?0:1);
        }
    }
}
