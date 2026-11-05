using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioFillHoleValidation
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static string Out(string n)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../FillHole/"+n));
        static readonly Vector3[] Border={new Vector3(-1,1,-1),new Vector3(-.5f,1,-1),new Vector3(.3f,1,-1),new Vector3(1,1,-1),new Vector3(1,1,-.4f),new Vector3(1,1,.2f),new Vector3(1,1,1),new Vector3(.4f,1,1),new Vector3(-1,1,1),new Vector3(-1,1,.5f)};
        static StudioModelDocument.Node Make(Vector3[] border,float scale,out (int a,int b)[] selected)
        {
            var points=new List<Vector3>();var geometry=new List<int>();var polys=new List<int>();var indices=new[]{new List<int>(),new List<int>()};var normals=new List<Vector3>();var uv=new List<Vector4>();var edges=new List<(int,int)>();int n=border.Length;
            for(int i=0;i<n;i++)
            {
                int next=(i+1)%n,start=points.Count;var p=new[]{border[next],new Vector3(border[next].x,-1,border[next].z),new Vector3(border[i].x,-1,border[i].z),border[i]};var normal=Vector3.Cross(p[1]-p[0],p[2]-p[0]).normalized;
                points.AddRange(p.Select(v=>v*scale));geometry.AddRange(new[]{next,next+n,i+n,i});normals.AddRange(Enumerable.Repeat(normal,4));uv.AddRange(p.Select(v=>new Vector4(v.x,v.z,.25f,.75f)));polys.AddRange(new[]{i%2,4,start,start+1,start+2,start+3});indices[i%2].AddRange(new[]{start,start+1,start+2,start,start+2,start+3});edges.Add((start+3,start));
            }
            var mesh=new Mesh();mesh.SetVertices(points);mesh.SetNormals(normals);mesh.SetUVs(0,uv.Select(v=>new Vector2(v.x,v.y)).ToList());mesh.SetUVs(7,uv);mesh.subMeshCount=2;for(int s=0;s<2;s++)mesh.SetTriangles(indices[s],s);mesh.RecalculateBounds();selected=edges.ToArray();return new StudioModelDocument.Node{name="Segmented open box",mesh=mesh,cutPolygons=polys.ToArray(),cutTriangles=mesh.triangles,geometryVertices=geometry.ToArray()};
        }
        static int BoundaryCount(StudioModelDocument.Node node)
        {
            var uses=new Dictionary<(int,int),int>();var t=new StudioMeshCuts(node);var g=node.geometryVertices;
            foreach(var f in t.Faces)for(int i=0;i<f.Vertices.Count;i++){int a=g[f.Vertices[i]],b=g[f.Vertices[(i+1)%f.Vertices.Count]];var k=a<b?(a,b):(b,a);uses.TryGetValue(k,out int count);uses[k]=count+1;}return uses.Values.Count(n=>n==1);
        }
        [InitializeOnLoadMethod]static void Boot(){if(Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets")&&File.Exists(Out("request.txt")))EditorApplication.update+=Next;}
        static void Next(){EditorApplication.update-=Next;Run();}
        public static void Run()
        {
            if(!Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets"))return;File.Delete(Out("request.txt"));var log=new List<string>();void Check(bool v,string text){if(!v)throw new Exception(text);log.Add("PASS "+text);}
            try
            {
                foreach(float scale in new[]{1f,.001f,1000f})
                {
                    var node=Make(Border,scale,out var selection);var original=node.mesh.vertices;var old0=node.mesh.GetTriangles(0);var old1=node.mesh.GetTriangles(1);var uv=new List<Vector4>();node.mesh.GetUVs(7,uv);
                    try
                    {
                        int cap=new StudioMeshCuts(node).FillHoles(node,selection.Reverse()).Single();var topology=new StudioMeshCuts(node);var face=topology.Faces[cap];
                        Check(topology.Faces.Count==Border.Length+1&&face.Vertices.Count==Border.Length,"One authored n-gon retains all uneven collinear border points scale="+scale);
                        Check(face.Vertices.Select(v=>node.geometryVertices[v]).OrderBy(v=>v).SequenceEqual(Enumerable.Range(0,Border.Length)),"Cap reuses every source endpoint geometric identity scale="+scale);
                        Check(BoundaryCount(node)==Border.Length&&node.geometryVertices.Distinct().Count()==Border.Length*2,"Top closes without geometric T-junctions or new geometric vertices scale="+scale);
                        var outputUV=new List<Vector4>();node.mesh.GetUVs(7,outputUV);Check(node.mesh.vertices.Take(original.Length).SequenceEqual(original)&&node.mesh.GetTriangles(0).Take(old0.Length).SequenceEqual(old0)&&node.mesh.GetTriangles(1).Take(old1.Length).SequenceEqual(old1)&&outputUV.Take(uv.Count).SequenceEqual(uv)&&node.mesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord7)==4,"Original geometry/render buffers/UV7 seams preserved scale="+scale);
                        if(scale==1)typeof(StudioFillHoleValidation).Assembly.GetType("BevelHoleProbe").GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{node,"FillHole/fill-hole.png"});
                    }
                    finally{UnityEngine.Object.DestroyImmediate(node.mesh);}
                }
                var invalid=Make(Border,1,out var borderEdges);var originalTriangles=invalid.mesh.triangles;bool rejected=false;
                try{new StudioMeshCuts(invalid).FillHoles(invalid,borderEdges.Take(borderEdges.Length-1));}catch(InvalidOperationException){rejected=true;}
                Check(rejected&&invalid.mesh.triangles.SequenceEqual(originalTriangles),"Open edge chain rejects before publication");rejected=false;
                try{new StudioMeshCuts(invalid).FillHoles(invalid,borderEdges.Concat(new[]{(0,1)}));}catch(InvalidOperationException){rejected=true;}
                Check(rejected&&invalid.mesh.triangles.SequenceEqual(originalTriangles),"Interior edge rejects before publication");UnityEngine.Object.DestroyImmediate(invalid.mesh);
                var d=ScriptableObject.CreateInstance<StudioModelDocument>();var n=Make(Border,1,out var selected);d.nodes.Add(n);
                try
                {
                    using(var editor=new StudioModelEditor(()=>{}))
                    {
                        editor.Load(d);editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);var edges=(List<(int a,int b)>)typeof(StudioModelEditor).GetMethod("Edges",F).Invoke(editor,null);var geometry=n.geometryVertices;
                        for(int i=0;i<edges.Count;i++)if(geometry[edges[i].a]<Border.Length&&geometry[edges[i].b]<Border.Length)editor.Components.Add(i);
                        editor.ToolbarCommands(null).Single(c=>c.Key=="Edit Mesh/Fill Hole").Execute();
                        Check(editor.ToolbarComponentMode==StudioModelEditor.ComponentMode.Face&&editor.Components.SetEquals(new[]{Border.Length}),"Shared command selects the new n-gon for immediate Poke");
                        Check(n.modifiers.operations.Last().kind=="FillHole","Compact Fill Hole modifier recorded");
                        editor.ToolbarCommands(null).Single(c=>c.Key=="Edit Mesh/Poke").Execute();
                        Check(new StudioMeshCuts(n).Faces.Count==Border.Length*2&&n.geometryVertices.Distinct().Count()==Border.Length*2+1,"Fill Hole then Poke connects the center to every segmented cap boundary vertex");
                        var replay=StudioModifierEvaluator.Evaluate(n.modifiers);try{Check(replay.mesh.triangles.SequenceEqual(n.mesh.triangles)&&replay.mesh.vertices.SequenceEqual(n.mesh.vertices),"Fill Hole plus Poke replay exact geometry in strict history order");}finally{UnityEngine.Object.DestroyImmediate(replay.mesh);}
                        typeof(StudioFillHoleValidation).Assembly.GetType("BevelHoleProbe").GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{n,"FillHole/fill-hole-poke.png"});
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(n.mesh);UnityEngine.Object.DestroyImmediate(d);}
            }
            catch(Exception e){log.Add("FAIL "+e);}
            File.WriteAllLines(Out("result.txt"),log);
        }
    }
}


