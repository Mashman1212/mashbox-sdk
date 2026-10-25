using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
namespace MashBoxSDK.AnimationStudio {
public static class ComponentValidation {
public static void Run(){int exit=1;var result=new List<string>();StudioRetopologyDocument doc=null;Mesh mesh=null;
void Check(bool ok,string label){if(!ok)throw new Exception(label);result.Add("PASS "+label);}
StudioRetopologyDocument.Part Make(){var p=new StudioRetopologyDocument.Part{name="Test"};foreach(float x in new[]{0f,2f}){int s=p.vertices.Count;p.vertices.AddRange(new[]{new Vector3(x,0,0),new Vector3(x+1,0,0),new Vector3(x+1,1,0),new Vector3(x,1,0)});p.uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});p.faces.Add(new StudioRetopologyDocument.Face(new[]{s,s+1,s+2,s+3}));}return p;}
try{
var p=Make();var edges=new HashSet<(int,int)>{(1,2),(4,7)};
Check(StudioRetopologyBridgeEdges.Validate(p,edges,out _,out bool closed)==null&&!closed,"Facing single boundary edges accepted");
StudioRetopologyBridgeEdges.Bridge(p,edges,3,null,0);
Check(p.faces.Count==5&&p.vertices.Count==12,"Three quad rows bridge gap");
Check(StudioRetopologyOperations.Edges(p).All(e=>e.Value.Count<=2)&&StudioRetopologyLoops.Boundaries(p).Count==1,"Bridge preserves winding and joins patches into one boundary");
Check(p.faces.All(f=>Vector3.Cross(p.vertices[f.vertices[1]]-p.vertices[f.vertices[0]],p.vertices[f.vertices[2]]-p.vertices[f.vertices[0]]).z>0),"Bridge faces match source orientation");
Check(StudioRetopologyBridgeEdges.Validate(p,edges,out _,out _)!=null,"Interior edges rejected after bridge");
var mismatch=Make();Check(StudioRetopologyBridgeEdges.Validate(mismatch,new HashSet<(int,int)>{(1,2),(4,7),(6,7)},out _,out _)!=null,"Mismatched chain lengths rejected");
var noSelection=Make();int before=noSelection.faces.Count;try{StudioRetopologyBridgeEdges.Bridge(noSelection,new HashSet<(int,int)>(),1,null,0);}catch(InvalidOperationException){}Check(noSelection.faces.Count==before,"Invalid bridge leaves mesh unchanged");
doc=ScriptableObject.CreateInstance<StudioRetopologyDocument>();p=Make();mesh=StudioRetopologyDocument.BuildMesh(p);p.reference=mesh;doc.parts.Add(p);doc.selected=0;
using(var editor=new StudioRetopologyEditor(doc,()=>{})){
const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
var field=typeof(StudioRetopologyEditor).GetField("selectionMode",flags);field.SetValue(editor,Enum.Parse(field.FieldType,"Faces"));
((HashSet<int>)typeof(StudioRetopologyEditor).GetField("selectedFaces",flags).GetValue(editor)).Add(0);
typeof(StudioRetopologyEditor).GetMethod("DeleteComponents",flags).Invoke(editor,null);
Check(p.faces.Count==1&&p.vertices.Count==4&&p.vertices.All(v=>v.x>=2),"Face delete removes only selected face and compacts unused vertices");
Undo.FlushUndoRecordObjects();Undo.PerformUndo();Check(doc.parts[0].faces.Count==2,"Face deletion is undoable");
}
result.Add("SUCCESS");exit=0;
}catch(Exception e){result.Add("FAIL "+e);}finally{if(doc)UnityEngine.Object.DestroyImmediate(doc);if(mesh)UnityEngine.Object.DestroyImmediate(mesh);File.WriteAllLines("component-results.txt",result);EditorApplication.Exit(exit);}
}
}}
