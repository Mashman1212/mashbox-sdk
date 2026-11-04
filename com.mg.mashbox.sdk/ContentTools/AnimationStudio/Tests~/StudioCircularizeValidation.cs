using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioCircularizeValidation
    {
        static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Circularize/"+name));
        [InitializeOnLoadMethod] static void Request()
        {
            if(!Application.dataPath.Replace('\\','/').EndsWith("ThemeIsolationProject/Assets"))return;
            var request=Out("request.txt");if(File.Exists(request))EditorApplication.delayCall+=()=>{File.Delete(request);Run();};
        }
        static StudioModelDocument.Node Make(bool seams=false)
        {
            var points=new List<Vector3>();var geometry=new List<int>();var polys=new List<int>();var triangles=new List<int>();var uv=new List<Vector2>();
            if(!seams)for(int y=0;y<5;y++)for(int x=0;x<5;x++){points.Add(new Vector3(x-2,0,y-2));geometry.Add(y*5+x);uv.Add(new Vector2(x,y));}
            for(int y=0;y<4;y++)for(int x=0;x<4;x++)
            {
                int[] ids={y*5+x,(y+1)*5+x,(y+1)*5+x+1,y*5+x+1};
                if(seams)ids=ids.Select(g=>{int i=points.Count;points.Add(new Vector3(g%5-2,0,g/5-2));geometry.Add(g);uv.Add(new Vector2(g%5+x*10,g/5+y*10));return i;}).ToArray();
                polys.AddRange(new[]{0,4}.Concat(ids));triangles.AddRange(new[]{ids[0],ids[1],ids[2],ids[0],ids[2],ids[3]});
            }
            var mesh=new Mesh();mesh.SetVertices(points);mesh.SetTriangles(triangles,0);mesh.SetUVs(0,uv);mesh.RecalculateNormals();mesh.RecalculateBounds();
            return new StudioModelDocument.Node{mesh=mesh,cutPolygons=polys.ToArray(),cutTriangles=mesh.triangles,geometryVertices=geometry.ToArray()};
        }
        static readonly int[] Region={5,6,9,10};
        static readonly int[] Ring={6,7,8,13,18,17,16,11};
        public static void Run()
        {
            if(!Application.dataPath.Replace('\\','/').EndsWith("ThemeIsolationProject/Assets"))return;
            var log=new List<string>();void Check(bool valid,string name){if(!valid)throw new Exception(name);log.Add("PASS "+name);}
            try
            {
                foreach(bool seam in new[]{false,true})
                {
                    var node=Make(seam);var source=node.mesh.vertices;var originalUV=node.mesh.uv;var topo=new StudioMeshCuts(node);
                    topo.Circularize(node,null,null,Region,new Vector4(0,0,0,1),Vector3.zero);
                    var points=node.mesh.vertices;var selected=new HashSet<int>(Ring);float radius=(1+Mathf.Sqrt(2))*.5f;
                    Check(Enumerable.Range(0,points.Length).Where(i=>selected.Contains(node.geometryVertices[i])).All(i=>Mathf.Abs(points[i].magnitude-radius)<1e-5f),"Face perimeter fits one radius, seams="+seam);
                    Check(Enumerable.Range(0,points.Length).Where(i=>!selected.Contains(node.geometryVertices[i])).All(i=>points[i]==source[i]),"Unselected geometry and interior center fixed, seams="+seam);
                    Check(originalUV.SequenceEqual(node.mesh.uv)&&new StudioMeshCuts(node).Faces.All(f=>f.Vertices.Count==4),"UVs and authored quads retained, seams="+seam);
                    UnityEngine.Object.DestroyImmediate(node.mesh);
                }
                foreach(int mode in new[]{0,1,2})
                {
                    var node=Make();var topology=new StudioMeshCuts(node);
                    var edges=Ring.Select((v,i)=>(v,Ring[(i+1)%Ring.Length])).ToArray();
                    topology.Circularize(node,mode==0?Ring:null,mode==1?edges:null,mode==2?Region:null,new Vector4(.1f,15,1,0),Vector3.zero);
                    Check(Ring.All(v=>Mathf.Abs(node.mesh.vertices[v].magnitude-((1+Mathf.Sqrt(2))*.5f+.1f))<1e-5f),"Vertex/edge/face radius offset and loop orientation mode="+mode);
                    UnityEngine.Object.DestroyImmediate(node.mesh);
                }
                for(int supporting=0;supporting<4;supporting++)
                {
                    var node=Make(true);new StudioMeshCuts(node).Circularize(node,null,null,Region,new Vector4(0,0,0,1),new Vector3(1,supporting,0));
                    var topology=new StudioMeshCuts(node);var uses=new Dictionary<(int,int),int>();var geo=node.geometryVertices;
                    foreach(var face in topology.Faces)for(int j=0;j<face.Vertices.Count;j++){int a=geo[face.Vertices[j]],b=geo[face.Vertices[(j+1)%face.Vertices.Count]];var key=a<b?(a,b):(b,a);uses.TryGetValue(key,out int count);uses[key]=count+1;}
                    Check(uses.Values.All(n=>n<=2)&&uses.Values.Count(n=>n==1)==16,"Conforming divided seam boundaries supporting="+supporting);
                    Check(node.mesh.vertices.All(v=>!float.IsNaN(v.x))&&node.mesh.uv.Length==node.mesh.vertexCount,"Interpolated seam attributes supporting="+supporting);
                    Check(supporting==0?topology.Faces.Count==16:topology.Faces.Count>16,"Supporting edges publish real triangles supporting="+supporting);
                    if(supporting==3)
                    {
                        var probe=typeof(StudioCircularizeValidation).Assembly.GetType("BevelHoleProbe");
                        probe.GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{node,"Circularize/circularize-render.png"});
                        Check(File.Exists(Out("circularize-render.png")),"Native shaded surface and authored wire rendered");
                    }
                    UnityEngine.Object.DestroyImmediate(node.mesh);
                }
                var document=ScriptableObject.CreateInstance<StudioModelDocument>();var n=Make();document.nodes.Add(n);var initial=n.mesh.vertices;
                using(var preview=new StudioMeshToolPreview(document,new[]{0}))
                {
                    var target=preview.Targets[0];
                    target.Topology.Circularize(target.Work,null,null,Region,new Vector4(0,30,0,0),new Vector3(2,3,0));preview.Publish(true);
                    Check(n.mesh.vertexCount>25,"Captured preview publishes divisions");preview.Restore();Check(n.mesh.vertices.SequenceEqual(initial)&&new StudioMeshCuts(n).Faces.Count==16,"Cancel restores geometry and topology");
                }
                var operation=new StudioModifierOperation{kind="Circularize",faces=Region,parameters=new Vector4(0,0,0,0),flow=new Vector3(1,3,0)};operation.Bind(n);StudioModifierEvaluator.Apply(n,operation);
                Check(n.mesh.vertexCount>25&&new StudioMeshCuts(n).Faces.Count>16,"Saved modifier replays all geometry options");UnityEngine.Object.DestroyImmediate(n.mesh);UnityEngine.Object.DestroyImmediate(document);
                var invalid=Make();var before=invalid.mesh.vertices;bool rejected=false;
                try{new StudioMeshCuts(invalid).Circularize(invalid,null,new[]{(6,7),(7,8)},null,Vector4.zero,Vector3.zero);}catch(InvalidOperationException){rejected=true;}
                Check(rejected&&before.SequenceEqual(invalid.mesh.vertices),"Open edge chain rejected before mutation");UnityEngine.Object.DestroyImmediate(invalid.mesh);
            }
            catch(Exception ex){log.Add("FAIL "+ex);}
            File.WriteAllLines(Out("result.txt"),log);
        }
    }
}
