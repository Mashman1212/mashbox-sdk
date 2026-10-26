using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
namespace MashBoxSDK.AnimationStudio {
public static class PatchSpikeValidation {
public static void Run(){var log=new List<string>();int exit=1;
void Check(bool yes,string text){if(!yes)throw new Exception(text);log.Add("PASS "+text);}
try{
for(int side=0;side<4;side++){
var doc=ScriptableObject.CreateInstance<StudioRetopologyDocument>();var mesh=new Mesh();mesh.vertices=new[]{Vector3.zero,new Vector3(3,0,0),new Vector3(3,3,0),new Vector3(0,3,0)};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateBounds();
doc.project=false;doc.selected=0;var p=new StudioRetopologyDocument.Part{reference=mesh};doc.parts.Add(p);
try{using(var editor=new StudioRetopologyEditor(doc,()=>{})){
const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;var type=typeof(StudioRetopologyEditor);
var tool=type.GetField("activeTool",flags);tool.SetValue(editor,Enum.Parse(tool.FieldType,"Patch"));type.GetField("cornerNormal",flags).SetValue(editor,Vector3.forward);
var add=type.GetMethod("AddPatch",flags);add.Invoke(editor,new object[]{new[]{Vector3.zero,Vector3.right,new Vector3(1,3,0),new Vector3(0,3,0)},2,6,false,null});
var seam=Enumerable.Range(0,p.vertices.Count).Where(i=>Mathf.Abs(p.vertices[i].x-1)<.001f).OrderBy(i=>p.vertices[i].y).ToArray();
p.vertices[seam[3]]+=Vector3.right*.08f;var points=new[]{Vector3.right,new Vector3(2,0,0),new Vector3(2,3,0),new Vector3(1,3,0)};points=Enumerable.Range(0,4).Select(i=>points[(i+side)%4]).ToArray();
add.Invoke(editor,new object[]{points,2,2,false,null});var edges=StudioRetopologyOperations.Edges(p);
Check(p.faces.Count==24,"Coarse neighboring patch inherits dense border, side "+side);
Check(Enumerable.Range(0,seam.Length-1).All(i=>edges[StudioRetopologyOperations.Edge(seam[i],seam[i+1])].Count==2),"Entire curved seam reused, side "+side);
Check(StudioRetopologyLoops.Boundaries(p).Count==1&&edges.All(e=>e.Value.Count<=2),"One connected manifold patch, side "+side);
}}finally{UnityEngine.Object.DestroyImmediate(doc);UnityEngine.Object.DestroyImmediate(mesh);}
}
var slope=new Mesh();slope.vertices=new[]{new Vector3(-2,-2,-2),new Vector3(2,-2,2),new Vector3(2,2,2),new Vector3(-2,2,-2)};slope.triangles=new[]{0,1,2,0,2,3};slope.RecalculateBounds();
try{var surface=new StudioRetopologySurface(slope);var point=new Vector3(.5f,0,0);var projected=surface.ProjectAlong(point,Vector3.forward,0);Check((projected-new Vector3(.5f,0,.5f)).magnitude<.00001f,"Directional projection preserves grid coordinates on a sloped surface");Check((surface.Project(point,0)-projected).magnitude>.1f,"Regression: closest point projection would shift the grid sideways");Check((surface.ProjectAlong(new Vector3(.5f,0,1),Vector3.forward,0)-projected).magnitude<.00001f,"Projection works from both sides of the surface");}finally{UnityEngine.Object.DestroyImmediate(slope);}
var trap=new Mesh();trap.vertices=new[]{new Vector3(.1f,-2,-2),new Vector3(.1f,2,-2),new Vector3(.1f,2,2),new Vector3(.1f,-2,2),new Vector3(-2,-2,5),new Vector3(2,-2,5),new Vector3(2,2,5),new Vector3(-2,2,5)};trap.triangles=new[]{0,1,2,0,2,3,4,5,6,4,6,7};trap.RecalculateBounds();
try{var surface=new StudioRetopologySurface(trap);Check(surface.ProjectAlong(Vector3.zero,Vector3.forward,0).z>4,"Reproduced grazing-ray spike toward distant surface");Check((surface.ProjectAlong(Vector3.zero,Vector3.forward,0,.2f)-new Vector3(.1f,0,0)).magnitude<.00001f,"Bounded projection stays on the nearby fold instead");}finally{UnityEngine.Object.DestroyImmediate(trap);}
log.Add("SUCCESS");exit=0;
}catch(Exception e){log.Add("FAIL "+e);}finally{File.WriteAllLines("patch-spike-results.txt",log);EditorApplication.Exit(exit);}
}
}}


