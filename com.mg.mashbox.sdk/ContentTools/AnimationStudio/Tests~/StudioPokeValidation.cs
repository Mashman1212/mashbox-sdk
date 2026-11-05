using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioPokeValidation
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static string Out(string n)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Poke/"+n));
        static StudioModelDocument.Node Make(Vector3[] boundary)
        {
            int n=boundary.Length;var p=boundary.Concat(new[]{new Vector3(5,0,0),new Vector3(6,0,0),new Vector3(5,1,0)}).ToArray();var m=new Mesh();m.vertices=p;m.subMeshCount=2;m.SetTriangles(Enumerable.Range(1,n-2).SelectMany(i=>new[]{0,i,i+1}).ToArray(),0);m.SetTriangles(new[]{n,n+1,n+2},1);
            m.normals=Enumerable.Repeat(Vector3.forward,p.Length).ToArray();m.tangents=Enumerable.Repeat(new Vector4(1,0,0,1),p.Length).ToArray();m.colors=p.Select((v,i)=>new Color((float)i/p.Length,.2f,.7f,1)).ToArray();m.SetUVs(0,p.Select(v=>new Vector2(v.x,v.y)).ToList());m.SetUVs(7,p.Select(v=>new Vector4(v.x,v.y,.3f,.8f)).ToList());m.RecalculateBounds();
            return new StudioModelDocument.Node{name="Poke validation",mesh=m,cutPolygons=new[]{0,n}.Concat(Enumerable.Range(0,n)).Concat(new[]{1,3,n,n+1,n+2}).ToArray(),cutTriangles=m.triangles,geometryVertices=Enumerable.Range(0,p.Length).ToArray()};
        }
        [InitializeOnLoadMethod]static void Boot(){if(Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets")&&File.Exists(Out("request.txt")))EditorApplication.update+=Next;}
        static void Next(){EditorApplication.update-=Next;Run();}
        public static void Run()
        {
            if(!Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets"))return;File.Delete(Out("request.txt"));var log=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);log.Add("PASS "+name);}
            try
            {
                var convex=Enumerable.Range(0,12).Select(i=>new Vector3(Mathf.Cos(i*Mathf.PI/6),Mathf.Sin(i*Mathf.PI/6),0)).ToArray();
                var concave=new[]{new Vector3(-2,-2,0),new Vector3(2,-2,0),new Vector3(2,-1,0),new Vector3(-1,-1,0),new Vector3(-1,1,0),new Vector3(2,1,0),new Vector3(2,2,0),new Vector3(-2,2,0)};
                foreach(var boundary in new[]{convex,concave,convex.Select((v,i)=>v+Vector3.forward*(i%3)*.15f).ToArray()})
                {
                    var node=Make(boundary);var original=node.mesh.vertices;var untouched=node.mesh.GetTriangles(1);var uv=new List<Vector4>();node.mesh.GetUVs(7,uv);var mean=boundary.Aggregate(Vector3.zero,(s,v)=>s+v)/boundary.Length;
                    try
                    {
                        var centers=new StudioMeshCuts(node).PokeFaces(node,new[]{0});var after=new StudioMeshCuts(node);int center=centers.Single();
                        Check(node.mesh.vertexCount==original.Length+1&&(node.mesh.vertices[center]-mean).magnitude<1e-5f,"Exactly one arithmetic center; boundary count="+boundary.Length+" warped="+(boundary[1].z!=0));
                        Check(after.Faces.Count==boundary.Length+1&&after.Faces.Take(boundary.Length).All(f=>f.Vertices.Count==3&&f.Vertices.Contains(center)),"One genuine triangle for every source perimeter edge");
                        Check(node.mesh.vertices.Take(original.Length).SequenceEqual(original)&&node.mesh.GetTriangles(1).SequenceEqual(untouched),"Original vertices and unrelated material tessellation unchanged");
                        var outputUV=new List<Vector4>();node.mesh.GetUVs(7,outputUV);Check(outputUV.Take(original.Length).SequenceEqual(uv)&&(outputUV[center]-uv.Take(boundary.Length).Aggregate(Vector4.zero,(s,v)=>s+v)/boundary.Length).sqrMagnitude<1e-8f&&node.mesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord7)==4,"UV7 interpolation/dimension and source attributes retained");
                        Check(after.Faces.Take(boundary.Length).SelectMany(f=>f.Vertices.Where(v=>v!=center)).Distinct().Count()==boundary.Length,"Every perimeter vertex connects to its shared center");
                        if(ReferenceEquals(boundary,convex)||ReferenceEquals(boundary,concave))
                        {typeof(StudioPokeValidation).Assembly.GetType("BevelHoleProbe").GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{node,ReferenceEquals(boundary,convex)?"Poke/poke-convex.png":"Poke/poke-concave.png"});}
                    }
                    finally{UnityEngine.Object.DestroyImmediate(node.mesh);}
                }
                var doc=ScriptableObject.CreateInstance<StudioModelDocument>();var node1=Make(convex);var node2=Make(concave);doc.nodes.Add(node1);doc.nodes.Add(node2);
                try
                {
                    using(var editor=new StudioModelEditor(()=>{}))
                    {
                        editor.Load(doc);editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);editor.Components.Add(0);
                        ((HashSet<int>)typeof(StudioModelEditor).GetField("objects",F).GetValue(editor)).Add(1);
                        var target=typeof(StudioModelEditor).GetMethod("ComponentTargetAt",F).Invoke(editor,new object[]{1,true});((HashSet<int>)target.GetType().GetField("selection",F).GetValue(target)).Add(0);
                        editor.ToolbarCommands(null).Single(c=>c.Key=="Edit Mesh/Poke").Execute();
                        Check(node1.mesh.vertexCount==16&&node2.mesh.vertexCount==12,"Shared menu command pokes both selected meshes");
                        Check(editor.ToolbarComponentMode==StudioModelEditor.ComponentMode.Vertex&&editor.Components.Count==1&&editor.ToolbarToolSelected(Tool.Move),"New center selected in Vertex/Move mode for artist adjustment");
                        Check(node1.modifiers.operations.Last().kind=="Poke"&&node2.modifiers.operations.Last().kind=="Poke","Strict Poke history recorded per mesh");
                        var evaluated=StudioModifierEvaluator.Evaluate(node1.modifiers);
                        try{Check(evaluated.mesh.triangles.SequenceEqual(node1.mesh.triangles)&&evaluated.mesh.vertices.SequenceEqual(node1.mesh.vertices),"Poke modifier replays exact fan geometry");}finally{UnityEngine.Object.DestroyImmediate(evaluated.mesh);}
                        editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);editor.Components.Add(0);typeof(StudioModelEditor).GetMethod("RepeatLastAction",F).Invoke(editor,null);
                        Check(node1.modifiers.operations.Count(o=>o.kind=="Poke")==2,"Repeat Last uses current face selection");
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(node1.mesh);UnityEngine.Object.DestroyImmediate(node2.mesh);UnityEngine.Object.DestroyImmediate(doc);}
            }
            catch(Exception e){log.Add("FAIL "+e);}
            File.WriteAllLines(Out("result.txt"),log);
        }
    }
}
