using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
namespace MashBoxSDK.AnimationStudio {
public static class GapPatchValidation {
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
p.vertices[seam[3]]+=new Vector3(-.4f,.8f,.1f);var points=new[]{Vector3.right,new Vector3(2,0,0),new Vector3(2,3,0),new Vector3(1,3,0)};points=Enumerable.Range(0,4).Select(i=>points[(i+side)%4]).ToArray();
add.Invoke(editor,new object[]{points,2,2,false,null});var edges=StudioRetopologyOperations.Edges(p);
Check(p.faces.Count==24,"Coarse neighboring patch inherits dense border, side "+side);
Check(Enumerable.Range(0,seam.Length-1).All(i=>edges[StudioRetopologyOperations.Edge(seam[i],seam[i+1])].Count==2),"Entire curved seam reused, side "+side);
Check(StudioRetopologyLoops.Boundaries(p).Count==1&&edges.All(e=>e.Value.Count<=2),"One connected manifold patch, side "+side);
}}finally{UnityEngine.Object.DestroyImmediate(doc);UnityEngine.Object.DestroyImmediate(mesh);}
}
var ring=new StudioRetopologyDocument.Part();
int steps=18;
for(int row=0;row<2;row++)for(int i=0;i<=steps;i++){
 float angle=Mathf.Lerp(45,315,(float)i/steps)*Mathf.Deg2Rad;
 ring.vertices.Add(new Vector3(Mathf.Cos(angle),row,Mathf.Sin(angle)));
}
for(int i=0;i<steps;i++)ring.faces.Add(new StudioRetopologyDocument.Face(new[]{i,i+1,i+1+steps+1,i+steps+1}));
var corner=new[]{ring.vertices[steps],ring.vertices[0],ring.vertices[steps+1],ring.vertices[2*steps+1]};
var sides=StudioRetopologyPatchBorders.Find(ring,corner,.00001f);
Check(sides[0]==null&&sides[2]==null,"Gap ignores remote top and bottom perimeter arcs");
Check(sides[1]?.Length==2&&sides[3]?.Length==2,"Gap reuses both local side seams");
int columns=4,rows=4;bool reverse=false;StudioRetopologyPatchBorders.Match(ring,sides,ref columns,ref rows,ref reverse);
Check(rows==1&&columns==4,"Gap preserves seam resolution without wrapping around garment");
log.Add("SUCCESS");exit=0;
}catch(Exception e){log.Add("FAIL "+e);}finally{File.WriteAllLines("gap-patch-results.txt",log);EditorApplication.Exit(exit);}
}
}}

