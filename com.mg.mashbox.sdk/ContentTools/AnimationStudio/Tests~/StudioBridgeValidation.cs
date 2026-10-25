using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace MashBoxSDK.AnimationStudio {
public static class StudioBridgeValidation {
 public static void Run(){int exit=1;var results=new List<string>();var doc=ScriptableObject.CreateInstance<StudioRetopologyDocument>();Mesh reference=null;
 void Check(bool ok,string name){if(!ok)throw new Exception(name);results.Add("PASS "+name);}
 object Invoke(object target,string name,params object[] args)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);
 try{
 reference=new Mesh{hideFlags=HideFlags.HideAndDontSave};reference.vertices=new[]{Vector3.zero,Vector3.right,Vector3.forward};reference.triangles=new[]{0,2,1};reference.RecalculateBounds();
 var p=new StudioRetopologyDocument.Part{reference=reference,name="Transient reference"};StudioRetopologyDocument.CopyReference(p);doc.parts.Add(p);doc.selected=0;
 using(var editor=new StudioRetopologyEditor(doc,()=>{})){
 var before=p.vertices.ToArray();int rev=p.Revision;bool dirty=doc.dirty;
 Invoke(editor,"BridgeLoops");Check(p.reference==reference&&p.vertices.SequenceEqual(before)&&p.Revision==rev&&doc.dirty==dirty,"Accidental bridge with no selection is a no-op");
 bool threw=false;try{Invoke(editor,"Change","Injected failure",(Action)(()=>{p.vertices.Clear();p.faces.Clear();p.uv.Clear();p.needsUV=true;throw new InvalidOperationException("Injected failure");}));}catch(TargetInvocationException){threw=true;}
 Check(threw&&ReferenceEquals(doc.parts[0],p)&&p.reference==reference&&reference.vertexCount==3,"Rollback retains transient Mesh and Part identity");
 Check(p.vertices.SequenceEqual(before)&&p.faces.Count==1&&p.uv.Count==3&&p.Revision==rev&&doc.dirty==dirty,"Rollback restores geometry and dirty state");
 Invoke(editor,"BridgeLoops");Check(p.reference==reference,"Editor remains usable after rejected operation");
 }
 var tubes=new StudioRetopologyDocument.Part();for(int row=0;row<4;row++)for(int i=0;i<8;i++){float a=i*Mathf.PI/4;tubes.vertices.Add(new Vector3(Mathf.Cos(a),row,Mathf.Sin(a)));tubes.uv.Add(Vector2.zero);}
 foreach(int row in new[]{0,2})for(int i=0;i<8;i++){int j=(i+1)%8;tubes.faces.Add(new StudioRetopologyDocument.Face(new[]{row*8+i,(row+1)*8+i,(row+1)*8+j,row*8+j}));}
 Check(StudioRetopologyLoops.BridgeIssue(tubes,new HashSet<int>{8,9,10})!=null,"Partial loop rejected");
 var selection=new HashSet<int>(Enumerable.Range(8,16));Check(StudioRetopologyLoops.BridgeIssue(tubes,selection)==null,"Matching complete loops accepted");
 StudioRetopologyLoops.Bridge(tubes,selection,3,null,0);Check(StudioRetopologyLoops.Boundaries(tubes).Count==2&&StudioRetopologyOperations.Edges(tubes).All(e=>e.Value.Count<=2),"Valid bridge still creates manifold quads");
 p.reference=null;using(var missing=new StudioRetopologyEditor(doc,()=>{}))Check(missing.Scene.IsValid(),"Missing reference does not prevent working-mesh preview");
 reference.name=p.name+" Reference";
 p.reference=null;using(var recovered=new StudioRetopologyEditor(doc,()=>{}))Check(p.reference==reference,"Unique surviving transient reference is recovered");
 var duplicate=UnityEngine.Object.Instantiate(reference);duplicate.name=reference.name;duplicate.hideFlags=HideFlags.HideAndDontSave;
 p.reference=null;using(var ambiguous=new StudioRetopologyEditor(doc,()=>{}))Check(!p.reference,"Ambiguous reference names are not guessed");
 UnityEngine.Object.DestroyImmediate(duplicate);
 results.Add("SUCCESS");exit=0;
 }catch(Exception e){results.Add("FAIL "+e);}finally{UnityEngine.Object.DestroyImmediate(doc);if(reference)UnityEngine.Object.DestroyImmediate(reference);File.WriteAllLines("bridge-results.txt",results);EditorApplication.Exit(exit);}
 }
}}
