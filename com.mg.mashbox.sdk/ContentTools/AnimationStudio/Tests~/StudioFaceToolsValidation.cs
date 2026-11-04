using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace MashBoxSDK.AnimationStudio
{
    // Non-exiting, opt-in fixture; never executes in the working project.
    internal static class StudioFaceToolsValidation
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static string Out(string suffix)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../face-tools-"+suffix));
        static int checks;
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
        [InitializeOnLoadMethod]static void Boot()
        {
            if(Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets")&&File.Exists(Out("request.txt")))EditorApplication.delayCall+=Run;
        }
        static object Call(StudioModelEditor e,string name,params object[] args)=>typeof(StudioModelEditor).GetMethod(name,F).Invoke(e,args);
        static StudioModelDocument.Node Cube()
        {
            var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
            mesh.colors=Enumerable.Range(0,mesh.vertexCount).Select(i=>new Color(i/32f,.2f,.3f,1)).ToArray();
            mesh.SetUVs(7,Enumerable.Range(0,mesh.vertexCount).Select(i=>new Vector4(i,.3f,.7f,1)).ToList());
            return new StudioModelDocument.Node{name="Face source",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles,geometryVertices=null};
        }
        static void MoveFace(StudioModelEditor editor,StudioModelDocument.Node node)
        {
            var selected=editor.SelectedVertices();var source=node.mesh.vertices;
            Check(typeof(StudioModelEditor).GetField("tool",F).GetValue(editor).ToString()=="Move","Face command left the transform tool disabled");
            var gesture=Call(editor,"CaptureMultiComponentGesture",new List<int>{0},Vector3.zero,Quaternion.identity,0);
            Check((bool)Call(editor,"ApplyMultiComponentGesture",gesture,Vector3.up,Quaternion.identity,Vector3.one),"Move did not apply");
            var moved=node.mesh.vertices;
            Check(Enumerable.Range(0,moved.Length).All(i=>(moved[i]-(source[i]+(selected.Contains(i)?Vector3.up:Vector3.zero))).sqrMagnitude<1e-10f),"Face movement affected unselected geometry");
            Check(Enumerable.Range(0,moved.Length).Any(i=>!selected.Contains(i)&&moved[i]==source[i]),"No fixed original geometry remained");
            Call(editor,"FlushModifierEdits");
        }
        public static void Run()
        {
            if(!Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets"))return;
            File.Delete(Out("request.txt"));var cleanup=new List<Mesh>();StudioModelEditor editor=null;StudioModelDocument doc=null;
            try
            {
                foreach(bool duplicate in new[]{true,false})
                {
                    var node=Cube();cleanup.Add(node.mesh);var before=new StudioMeshCuts(node);var points=node.mesh.vertices;var normals=node.mesh.normals;
                    var chosen=new HashSet<int>{0,1};var result=before.CopyFaceRegion(node,chosen,duplicate);var after=new StudioMeshCuts(node);
                    Check(after.Faces.Count==(duplicate?8:6),"Face count changed incorrectly");
                    Check(result.Length==2,"Wrong resulting selection");
                    Check(after.Faces.All(f=>f.Vertices.Count==4),"Lost authored quads");
                    var copied=new HashSet<int>(result.SelectMany(i=>after.Faces[i].Vertices).Select(v=>node.geometryVertices[v]));
                    var rest=new HashSet<int>(after.Faces.Where((f,i)=>!result.Contains(i)).SelectMany(f=>f.Vertices).Select(v=>node.geometryVertices[v]));
                    Check(!copied.Overlaps(rest),"Copied/detached patch remains welded to source");
                    Check(node.mesh.vertices.All(p=>points.Contains(p)),"Operation moved geometry");
                    Check(node.mesh.normals.All(n=>normals.Contains(n)),"Operation changed normals");
                    var uv=new List<Vector4>();node.mesh.GetUVs(7,uv);Check(uv.Count==node.mesh.vertexCount&&uv.All(v=>v.y==.3f&&v.z==.7f),"UV7 lost");
                    Check(node.mesh.colors.Length==node.mesh.vertexCount,"Colors lost");
                    Check(after.Faces.SelectMany(f=>f.Vertices).Distinct().Count()==node.mesh.vertexCount,"Created orphan vertices");
                    Check(node.mesh.triangles.Length==(duplicate?48:36),"Lost source triangles");
                }
                doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.nodes.Add(Cube());cleanup.Add(doc.nodes[0].mesh);
                editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);editor.Components.Add(0);
                var mesh=doc.nodes[0].mesh;var triangles=mesh.triangles.ToArray();
                Call(editor,"Run",(Action)(()=>Call(editor,"EditSelectedFaces",true,false)),true);
                Check(editor.StatusText=="Ready","Duplicate command failed: "+editor.StatusText);
                Check(new StudioMeshCuts(doc.nodes[0]).Faces.Count==7,"Editor duplicate failed");
                Check(doc.nodes[0].modifiers?.operations.Last().name=="Duplicate Face","Missing modifier history");
                var evaluated=StudioModifierEvaluator.Evaluate(doc.nodes[0].modifiers);cleanup.Add(evaluated.mesh);
                Check(evaluated.mesh.triangles.SequenceEqual(mesh.triangles),"Duplicate history replay differs");
                Check(editor.Transforms[0].GetComponent<MeshFilter>().sharedMesh==mesh,"Renderer lost mesh binding");
                var copiedFace=new StudioMeshCuts(doc.nodes[0]).Faces[editor.Components.Single()];
                Check(editor.SelectedVertices().SetEquals(copiedFace.Vertices),"Welded transform selection recaptures the coincident original faces after Duplicate");
                MoveFace(editor,doc.nodes[0]);
                editor.Components.Clear();editor.Components.Add(0);
                Call(editor,"Run",(Action)(()=>Call(editor,"EditSelectedFaces",false,false)),true);
                Check(editor.StatusText=="Ready","Detach command failed: "+editor.StatusText);
                Check(new StudioMeshCuts(doc.nodes[0]).Faces.Count==7,"Detach changed face count");
                Check(doc.nodes[0].modifiers.operations.Last().name=="Detach Face","Detach history missing");
                var detachedFace=new StudioMeshCuts(doc.nodes[0]).Faces[0];
                Check(editor.SelectedVertices().SetEquals(detachedFace.Vertices),"Detached face selection still includes coincident neighbors");
                MoveFace(editor,doc.nodes[0]);
                var sourcePoints=mesh.vertices;var sourceTriangles=mesh.triangles;int count=doc.nodes.Count;
                var all=new HashSet<int>(Enumerable.Range(0,new StudioMeshCuts(doc.nodes[0]).Faces.Count));
                var added=doc.SplitMeshes(new Dictionary<int,HashSet<int>>{{0,all}},duplicate:true);cleanup.Add(doc.nodes[added[0]].mesh);
                Check(doc.nodes.Count==count+1&&added.Length==1,"Duplicate as object failed");
                Check(doc.nodes[0].mesh==mesh&&mesh.vertices.SequenceEqual(sourcePoints)&&mesh.triangles.SequenceEqual(sourceTriangles),"New-object duplicate mutated source");
                Check(doc.nodes[added[0]].mesh.bounds.center.sqrMagnitude<1e-9f,"New-object pivot not centered");
                Check(new StudioMeshCuts(doc.nodes[added[0]]).Faces.Count==all.Count,"New object lost faces");
                File.WriteAllText(Out("result.txt"),"PASS "+checks+" checks; kernel, editor commands, renderer binding, Direct history replay, and whole-object face duplication. Native Undo/Redo and physical UI not exercised.");
            }
            catch(Exception e){File.WriteAllText(Out("result.txt"),"FAIL "+checks+" checks: "+e);Debug.LogException(e);}
            finally {editor?.Dispose();if(doc)Object.DestroyImmediate(doc);foreach(var mesh in cleanup.Distinct())if(mesh)Object.DestroyImmediate(mesh);}
        }
    }
}
