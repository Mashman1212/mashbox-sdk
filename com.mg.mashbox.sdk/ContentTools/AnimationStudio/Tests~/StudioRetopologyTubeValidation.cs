using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
namespace MashBoxSDK.AnimationStudio {
public static class TubeValidation {
public static void Run(){int exit=1;var results=new List<string>();Mesh reference=null,rotated=null;StudioRetopologyDocument doc=null;
void Check(bool yes,string label){if(!yes)throw new Exception(label);results.Add("PASS "+label);}
try{
var source=new StudioRetopologyDocument.Part{name="Cylinder"};int n=32;
for(int row=0;row<2;row++)for(int i=0;i<n;i++){float a=2*Mathf.PI*i/n;source.vertices.Add(new Vector3(Mathf.Cos(a),row*3,Mathf.Sin(a)));source.uv.Add(Vector2.zero);}
for(int i=0;i<n;i++){int j=(i+1)%n;source.faces.Add(new StudioRetopologyDocument.Face(new[]{i,n+i,n+j,j}));}
reference=StudioRetopologyDocument.BuildMesh(source);
var start=new Vector3(0,.2f,-1);var end=new Vector3(0,2.8f,-1);
var tube=StudioRetopologyTube.Generate(reference,start,end,12,8,0);
Check(tube.faces.Count==96&&tube.vertices.Count==108&&tube.faces.All(f=>f.vertices.Length==4),"12 sides and 8 divisions produce 96 quads");
Check(StudioRetopologyLoops.Boundaries(tube).Count==2&&StudioRetopologyOperations.Edges(tube).All(e=>e.Value.Count<=2),"Two open ends and manifold topology");
Check(tube.vertices.All(p=>Mathf.Abs(new Vector2(p.x,p.z).magnitude-1)<.01f),"Rings fit the reference surface");
Check(tube.faces.All(f=>{var a=tube.vertices[f.vertices[0]];var b=tube.vertices[f.vertices[1]];var c=tube.vertices[f.vertices[2]];return Vector3.Dot(Vector3.Cross(b-a,c-a),new Vector3(a.x,0,a.z))>0;}),"Outward-facing quads");
var rotation=Quaternion.Euler(35,17,72);rotated=UnityEngine.Object.Instantiate(reference);rotated.vertices=reference.vertices.Select(p=>rotation*p).ToArray();rotated.RecalculateBounds();
var angled=StudioRetopologyTube.Generate(rotated,rotation*start,rotation*end,12,8,0);Check(angled.faces.Count==96&&StudioRetopologyLoops.Boundaries(angled).Count==2,"Rotated arm orientation supported");
bool rejected=false;try{StudioRetopologyTube.Generate(reference,start,start,12,8,0);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Coincident endpoints rejected");
doc=ScriptableObject.CreateInstance<StudioRetopologyDocument>();var part=new StudioRetopologyDocument.Part{name="Tube test",reference=reference};doc.parts.Add(part);doc.selected=0;
using(var editor=new StudioRetopologyEditor(doc,()=>{})){
const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
var corners=(List<Vector3>)typeof(StudioRetopologyEditor).GetField("corners",flags).GetValue(editor);corners.Add(start);corners.Add(end);
typeof(StudioRetopologyEditor).GetMethod("CreateTube",flags).Invoke(editor,null);
Check(part.faces.Count==96,"Editor creates tube without replacing reference");Undo.FlushUndoRecordObjects();Undo.PerformUndo();Check(doc.parts[0].faces.Count==0&&doc.parts[0].reference==reference,"Tube creation Undo preserves reference");
}
string path="Packages/com.mg.mashbox.sdk/Runtime/Characters/Human/Human_Pants_Punch_WC.fbx";
var pants=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().OrderByDescending(m=>m.vertexCount).FirstOrDefault();
if(!pants){path=AssetDatabase.FindAssets("Human_Pants_Punch_WC").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p=>p.EndsWith(".fbx",StringComparison.OrdinalIgnoreCase));pants=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().OrderByDescending(m=>m.vertexCount).FirstOrDefault();}
if(!pants)throw new Exception("SDK pants missing");
var bounds=pants.bounds;var surface=new StudioRetopologySurface(pants);
float x=bounds.center.x-bounds.size.x*.24f;
var guideStart=surface.Project(new Vector3(x,bounds.min.y+bounds.size.y*.18f,bounds.min.z-.01f),0);
var guideEnd=surface.Project(new Vector3(x,bounds.min.y+bounds.size.y*.57f,bounds.min.z-.01f),0);
var leg=StudioRetopologyTube.Generate(pants,guideStart,guideEnd,12,8,.002f);
Check(leg.faces.Count==96&&StudioRetopologyLoops.Boundaries(leg).Count==2,"Actual SDK pants leg produces an open quad tube");
File.WriteAllText("tube-pants-guide.txt",$"{guideStart} -> {guideEnd}\n{StudioRetopologyOperations.Audit(leg)}");
results.Add("SUCCESS");exit=0;
}catch(Exception e){results.Add("FAIL "+e);}finally{if(doc)UnityEngine.Object.DestroyImmediate(doc);if(reference)UnityEngine.Object.DestroyImmediate(reference);if(rotated)UnityEngine.Object.DestroyImmediate(rotated);File.WriteAllLines("tube-results.txt",results);EditorApplication.Exit(exit);}
}
}}
