using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;
namespace MashBoxSDK.AnimationStudio {
public static class StudioExtrudeValidation {
 public static void Run(){var log=new List<string>();StudioRetopologyDocument doc=null;
 void Check(bool value,string label){if(!value)throw new Exception(label);log.Add("PASS "+label);}
 try{
 var p=new Part {vertices=new List<Vector3>{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(0,1,0)},faces=new List<Face>{new Face(new[]{0,1,2,3},2)}};
 var border=new HashSet<(int,int)>{(2,3)};
 border=StudioRetopologyExtrude.Apply(p,border,Vector3.up,null,0);
 Check(p.faces.Count==2&&p.vertices.Count==6,"Single edge adds exactly one welded quad");
 Check(StudioRetopologyOperations.Edges(p)[(2,3)].Count==2,"Original border becomes a shared interior edge");
 border=StudioRetopologyExtrude.Apply(p,border,Vector3.up,null,0);
 Check(p.faces.Count==3&&p.vertices.Count==8&&border.All(e=>p.vertices[e.Item1].y==3&&p.vertices[e.Item2].y==3),"Repeat extrudes the new border");
 Check(p.faces.All(f=>f.material==2)&&p.uv.Count==p.vertices.Count,"Materials and vertex UV counts retained");
 Check(p.faces.All(f=>Vector3.Cross(p.vertices[f.vertices[1]]-p.vertices[f.vertices[0]],p.vertices[f.vertices[2]]-p.vertices[f.vertices[0]]).z>0),"Face winding remains consistent");
 Check(StudioRetopologyExtrude.Issue(p,new HashSet<(int,int)>{(2,3)})!=null,"Interior edge extrusion rejected");
 int count=p.vertices.Count;try{StudioRetopologyExtrude.Apply(p,border,Vector3.zero,null,0);}catch(InvalidOperationException){}
 Check(p.vertices.Count==count,"Invalid extrusion leaves geometry intact");
 var ring=new Part();for(int y=0;y<2;y++)for(int i=0;i<8;i++){float t=i*Mathf.PI/4;ring.vertices.Add(new Vector3(Mathf.Cos(t),y,Mathf.Sin(t)));}
 for(int i=0;i<8;i++)ring.faces.Add(new Face(new[]{i,(i+1)%8,(i+1)%8+8,i+8}));
 var loop=Enumerable.Range(0,8).Select(i=>StudioRetopologyOperations.Edge(i+8,(i+1)%8+8)).ToHashSet();
 var next=StudioRetopologyExtrude.Apply(ring,loop,Vector3.up*.2f,null,0);
 Check(ring.vertices.Count==24&&ring.faces.Count==16&&next.Count==8,"Closed loop shares one new vertex per corner");
 Check(StudioRetopologyOperations.Edges(ring).All(e=>e.Value.Count<=2)&&loop.All(e=>StudioRetopologyOperations.Edges(ring)[e].Count==2),"Closed loop stays welded and manifold");
 doc=ScriptableObject.CreateInstance<StudioRetopologyDocument>();doc.parts.Add(p);doc.selected=0;
 using(var editor=new StudioRetopologyEditor(doc,()=>{})){
 var flags=BindingFlags.NonPublic|BindingFlags.Instance;var type=typeof(StudioRetopologyEditor);
 var selection=type.GetField("selectedEdges",flags).GetValue(editor) as HashSet<(int,int)>;selection.UnionWith(border);
 var mode=type.GetField("selectionMode",flags);mode.SetValue(editor,Enum.Parse(mode.FieldType,"Edges"));doc.project=false;
 type.GetMethod("SyncComponentVertices",flags).Invoke(editor,null);
 int before=p.faces.Count;type.GetMethod("ExtrudeSelectedEdges",flags).Invoke(editor,null);Undo.FlushUndoRecordObjects();
 Check(doc.parts[0].faces.Count==before+1&&selection.Count==1,"Editor action keeps new edge selected");
 Undo.PerformUndo();Check(doc.parts[0].faces.Count==before,"One Undo removes extrusion");
 }
 log.Add("SUCCESS");
 }catch(Exception e){log.Add("FAIL "+e);}finally{if(doc)UnityEngine.Object.DestroyImmediate(doc);File.WriteAllLines("Library/extrude-validation.txt",log);}
 }
}
[InitializeOnLoad] internal static class ExtrudeValidationOnce {static ExtrudeValidationOnce(){EditorApplication.delayCall+=()=>{if(SessionState.GetBool("ExtrudeValidation.v1",false))return;SessionState.SetBool("ExtrudeValidation.v1",true);StudioExtrudeValidation.Run();};}}
}
