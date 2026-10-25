using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace MashBoxSDK.AnimationStudio {
public static class PatchDensityValidation {
public static void Run(){int exit=1;var results=new List<string>();var doc=ScriptableObject.CreateInstance<StudioRetopologyDocument>();Mesh reference=null;
void Check(bool yes,string label){if(!yes)throw new Exception(label);results.Add("PASS "+label);}
try{
reference=new Mesh();reference.vertices=new[]{new Vector3(-4,-4,0),new Vector3(4,-4,0),new Vector3(4,4,0),new Vector3(-4,4,0)};reference.triangles=new[]{0,1,2,0,2,3};reference.RecalculateBounds();
doc.project=false;doc.selected=0;var p=new StudioRetopologyDocument.Part{name="Patch",reference=reference};doc.parts.Add(p);
using(var editor=new StudioRetopologyEditor(doc,()=>{})){
const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;var type=typeof(StudioRetopologyEditor);
object Get(string field)=>type.GetField(field,flags).GetValue(editor);
void Set(string field,object value)=>type.GetField(field,flags).SetValue(editor,value);
void Call(string method)=>type.GetMethod(method,flags).Invoke(editor,null);
var tool=type.GetField("activeTool",flags);tool.SetValue(editor,Enum.Parse(tool.FieldType,"Patch"));Set("continuousPatch",true);Set("patchSegments",2);Set("patchQuadSize",.5f);Set("cornerNormal",Vector3.forward);
var corners=(List<Vector3>)Get("corners");corners.AddRange(new[]{Vector3.zero,Vector3.right,new Vector3(1,1,0),Vector3.up});Call("CreatePatch");
Check(p.faces.Count==4&&p.vertices.Count==9&&corners.Count==2,"First patch creates grid and seeds two continuation corners");
var seam=((int[])Get("patchContinuation")).ToArray();p.vertices[seam[1]]+=Vector3.forward*.1f;
corners.Add(new Vector3(1,2,0));corners.Add(new Vector3(0,2,0));Call("CreatePatch");
Check(p.faces.Count==8&&p.vertices.Count==15,"Second patch reuses all three shared vertices");
var edges=StudioRetopologyOperations.Edges(p);Check(Enumerable.Range(0,2).All(i=>edges[StudioRetopologyOperations.Edge(seam[i],seam[i+1])].Count==2),"Curved shared edge belongs to both patches");
Check(StudioRetopologyLoops.Boundaries(p).Count==1&&edges.All(e=>e.Value.Count<=2),"Continued patches form one manifold boundary");
Call("ClearPatchCorners");Check(corners.Count==0&&Get("patchContinuation")==null&&p.faces.Count==8,"Ending chain preserves committed patches");
Undo.FlushUndoRecordObjects();Undo.PerformUndo();p=doc.parts[0];Check(p.faces.Count==4&&Get("patchContinuation")==null,"Undo removes last patch and safely clears continuation");
}
doc.parts.Clear();doc.symmetry=true;p=new StudioRetopologyDocument.Part{name="Mirror patch",reference=reference};doc.parts.Add(p);
using(var editor=new StudioRetopologyEditor(doc,()=>{})){
const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;var type=typeof(StudioRetopologyEditor);var tool=type.GetField("activeTool",flags);tool.SetValue(editor,Enum.Parse(tool.FieldType,"Patch"));
type.GetField("continuousPatch",flags).SetValue(editor,true);type.GetField("patchSegments",flags).SetValue(editor,2);type.GetField("patchQuadSize",flags).SetValue(editor,.5f);type.GetField("cornerNormal",flags).SetValue(editor,Vector3.forward);
var corners=(List<Vector3>)type.GetField("corners",flags).GetValue(editor);corners.AddRange(new[]{new Vector3(1,0,0),new Vector3(2,0,0),new Vector3(2,1,0),new Vector3(1,1,0)});
type.GetMethod("CreatePatch",flags).Invoke(editor,null);corners.Add(new Vector3(2,2,0));corners.Add(new Vector3(1,2,0));type.GetMethod("CreatePatch",flags).Invoke(editor,null);
Check(p.faces.Count==16&&p.vertices.Count==30&&StudioRetopologyLoops.Boundaries(p).Count==2,"Mirrored chains share their own full seams with consistent winding");
}
var points=new[]{Vector3.zero,Vector3.right,new Vector3(1,4,0),new Vector3(0,4,0)}; Check(StudioRetopologyEditor.PatchGridSize(points,.5f,null)==new Vector2Int(2,8),"Long patch gets independent width and length divisions");Check(StudioRetopologyEditor.PatchGridSize(points,.5f,new[]{0,1,2,3,4})==new Vector2Int(4,8),"Continuous seam fixes columns without fixing length divisions");results.Add("SUCCESS");exit=0;
}catch(Exception e){results.Add("FAIL "+e);}finally{UnityEngine.Object.DestroyImmediate(doc);if(reference)UnityEngine.Object.DestroyImmediate(reference);File.WriteAllLines("patch-density-results.txt",results);EditorApplication.Exit(exit);}
}
}}

