using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;
namespace MashBoxSDK.AnimationStudio {
public static class RelaxBrushValidation {
public static void Run(){int exit=1;var log=new List<string>();StudioRetopologyDocument doc=null;Mesh mesh=null;
void Check(bool yes,string text){if(!yes)throw new Exception(text);log.Add("PASS "+text);}
try{
var p=new Part();for(int y=0;y<3;y++)for(int x=0;x<3;x++){p.vertices.Add(new Vector3(x,y,0));p.uv.Add(new Vector2(x,y));}
for(int y=0;y<2;y++)for(int x=0;x<2;x++){int a=y*3+x;p.faces.Add(new Face(new[]{a,a+1,a+4,a+3},0));}
p.vertices[4]=new Vector3(1.4f,1.2f,0);var old=p.vertices.ToArray();var weights=Enumerable.Range(0,9).ToDictionary(i=>i,i=>1f);
Check(!StudioRetopologyRelaxBrush.Apply(p,weights,0,true,null,0)&&old.SequenceEqual(p.vertices),"Zero strength leaves vertices unchanged");
Check(StudioRetopologyRelaxBrush.Apply(p,weights,.5f,true,null,0),"Relax changes uneven spacing");
Check((p.vertices[4]-new Vector3(1,1,0)).magnitude<(old[4]-new Vector3(1,1,0)).magnitude,"Interior vertex moves toward even spacing");
Check(Enumerable.Range(0,9).Where(i=>i!=4).All(i=>old[i]==p.vertices[i]),"Open borders remain fixed");
Check(p.vertices.Count==9&&p.faces.Count==4&&p.uv.Count==9,"Topology and UV count preserved");
mesh=new Mesh();mesh.vertices=new[]{new Vector3(-5,-5,0),new Vector3(5,-5,0),new Vector3(5,5,0),new Vector3(-5,5,0)};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateBounds();
p.vertices[4]+=Vector3.forward*.1f;StudioRetopologyRelaxBrush.Apply(p,weights,.5f,true,new StudioRetopologySurface(mesh),0);
Check(Mathf.Abs(p.vertices[4].z)<.00001f,"Relax projects back to the reference");
doc=ScriptableObject.CreateInstance<StudioRetopologyDocument>();doc.parts.Add(p);doc.selected=0;p.reference=mesh;
using(var editor=new StudioRetopologyEditor(doc,()=>{})){
var before=p.vertices.ToArray();Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
var method=typeof(StudioRetopologyEditor).GetMethod("Change",BindingFlags.NonPublic|BindingFlags.Instance);
for(int i=0;i<3;i++)method.Invoke(editor,new object[]{"Relax topology brush",(Action)(()=>StudioRetopologyRelaxBrush.Apply(p,weights,.5f,true,null,0))});
Undo.FlushUndoRecordObjects();Undo.CollapseUndoOperations(group);Undo.PerformUndo();
Check(before.SequenceEqual(doc.parts[0].vertices),"One Undo restores all three brush dabs");
}
log.Add("SUCCESS");exit=0;
}catch(Exception e){log.Add("FAIL "+e);}finally{if(doc)UnityEngine.Object.DestroyImmediate(doc);if(mesh)UnityEngine.Object.DestroyImmediate(mesh);File.WriteAllLines("relax-brush-results.txt",log);EditorApplication.Exit(exit);}
}
}}
