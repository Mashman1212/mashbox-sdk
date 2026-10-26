using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
namespace MashBoxSDK.AnimationStudio {
public static class WorkflowValidation {
public static void Run(){int exit=1;var log=new List<string>();Mesh reference=null;StudioRetopologyDocument doc=null;
void Check(bool yes,string text){if(!yes)throw new Exception(text);log.Add("PASS "+text);}
StudioRetopologyDocument.Part Grid(int n){var p=new StudioRetopologyDocument.Part{name="Grid"};for(int y=0;y<=n;y++)for(int x=0;x<=n;x++){p.vertices.Add(new Vector3(x,y,0));p.uv.Add(new Vector2(x,y));}for(int y=0;y<n;y++)for(int x=0;x<n;x++){int i=y*(n+1)+x;p.faces.Add(new StudioRetopologyDocument.Face(new[]{i,i+1,i+n+2,i+n+1}));}return p;}
try{
var p=Grid(3);StudioRetopologyResolution.Increase(p,new HashSet<int>{4},null,0);
Check(p.faces.Count==16&&p.faces.All(f=>f.vertices.Length==4),"Local refinement propagates conforming all-quad strips");
Check(StudioRetopologyLoops.Boundaries(p).Count==1&&StudioRetopologyOperations.Edges(p).All(e=>e.Value.Count<=2),"Refinement has no cracks or non-manifold edges");
p=Grid(2);int changed=StudioRetopologyResolution.Decrease(p,new HashSet<int>(Enumerable.Range(0,4)));
Check(changed==1&&p.faces.Count==1&&p.vertices.Count==4,"Four-quad block reduces to one quad");
p=Grid(4);changed=StudioRetopologyResolution.Decrease(p,new HashSet<int>{5,6,9,10});
Check(changed==1&&p.faces.All(f=>f.vertices.Length>=3)&&StudioRetopologyLoops.Boundaries(p).Count==1,"Partial reduction keeps connected border transitions");
p=Grid(2);Check(StudioRetopologyResolution.Decrease(p,new HashSet<int>{0})==0&&p.faces.Count==4,"Incomplete reduction block is left intact");
doc=ScriptableObject.CreateInstance<StudioRetopologyDocument>();p=Grid(3);reference=StudioRetopologyDocument.BuildMesh(p);p.reference=reference;doc.parts.Add(p);doc.selected=0;
using(var editor=new StudioRetopologyEditor(doc,()=>{})){
const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;var type=typeof(StudioRetopologyEditor);
object Call(string name,params object[] args)=>type.GetMethod(name,flags).Invoke(editor,args);
type.GetField("contextSelection",flags).SetValue(editor,true);
foreach(int mode in new[]{0,1,2}){Call("ApplyWheel",0,mode);Check((int)type.GetField("selectionMode",flags).GetValue(editor)==mode,"Selection wheel routes component mode "+mode);}
Check((int)Call("WheelBranch",3)==1&&(int)Call("WheelBranch",4)==2&&(int)Call("WheelBranch",5)==3,"Selection wheel opens Topology Display and Draw branches");
Call("ApplyWheel",3,2);Check(type.GetField("activeTool",flags).GetValue(editor).ToString()=="Tube","Draw wheel activates Tube");
Call("ApplyWheel",3,3);Check(type.GetField("activeTool",flags).GetValue(editor).ToString()=="Resolution","Draw wheel activates Resolution");
Call("Change","Resolution test",(Action)(()=>StudioRetopologyResolution.Increase(p,new HashSet<int>{4},null,0)));
Undo.FlushUndoRecordObjects();Undo.PerformUndo();Check(doc.parts[0].faces.Count==9&&doc.parts[0].reference==reference,"Resolution refinement Undo preserves reference");
}
log.Add("SUCCESS");exit=0;
}catch(Exception e){log.Add("FAIL "+e);}finally{if(doc)UnityEngine.Object.DestroyImmediate(doc);if(reference)UnityEngine.Object.DestroyImmediate(reference);File.WriteAllLines("workflow-results.txt",log);EditorApplication.Exit(exit);}
}
}}
