using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio {
public static class StudioPantsValidation {
 public static void Run(){int code=1;var log=new System.Collections.Generic.List<string>();
 try {
 var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.mg.mashbox.sdk/Runtime/Characters/Human/Human_Pants_Punch_WC.fbx");
 if(!asset)throw new Exception("Missing pants asset");
 var r=asset.GetComponentInChildren<SkinnedMeshRenderer>(true);
 var mesh=StudioSkinningDocument.CreateEditableCopy(r.sharedMesh);
 var matrix=asset.transform.worldToLocalMatrix*r.transform.localToWorldMatrix;
 mesh.vertices=mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();mesh.RecalculateBounds();
 var sourcePart=new StudioRetopologyDocument.Part{reference=mesh};StudioRetopologyDocument.CopyReference(sourcePart);File.WriteAllText("pants-source.json",JsonUtility.ToJson(sourcePart));
 var original=mesh.vertices;log.Add("Reference bounds: "+mesh.bounds);
 foreach(int budget in new[]{1500,3000,5000}){
 var p=StudioRetopologyPants.Generate(mesh,budget,0);
 if(!p.faces.All(f=>f.vertices.Length==4))throw new Exception("Not all quads");
 if(StudioRetopologyLoops.Boundaries(p).Count!=3)throw new Exception("Not 3 openings");
 if(p.faces.Any(f=>{var a=p.vertices[f.vertices[0]];var b=p.vertices[f.vertices[1]];var c=p.vertices[f.vertices[2]];var d=p.vertices[f.vertices[3]];return Vector3.Dot(Vector3.Cross(b-a,c-a),Vector3.Cross(c-a,d-a))<=0;}))throw new Exception("Folded quad");
 if(StudioRetopologyOperations.Edges(p).Any(e=>e.Value.Count>2))throw new Exception("Nonmanifold edge");
 if(!mesh.vertices.SequenceEqual(original))throw new Exception("Reference mutated");
 if(p.vertices.Any(v=>float.IsNaN(v.x)||float.IsInfinity(v.x)))throw new Exception("Nonfinite output");
 log.Add("PASS "+budget+": "+StudioRetopologyOperations.Audit(p));
 File.WriteAllText("pants-"+budget+".json",JsonUtility.ToJson(p));
 }
 bool cancel=false;try{StudioRetopologyPants.Generate(mesh,3000,0,_=>true);}catch(OperationCanceledException){cancel=true;}
 if(!cancel)throw new Exception("Cancellation failed");log.Add("PASS cancellation");
 var bad=new Mesh();bad.vertices=new[]{Vector3.zero,Vector3.right,Vector3.forward};bad.triangles=new[]{0,1,2};bad.RecalculateBounds();
 bool rejected=false;try{StudioRetopologyPants.Generate(bad,3000,0);}catch(InvalidOperationException){rejected=true;}
 if(!rejected)throw new Exception("Invalid source accepted");log.Add("PASS invalid source rejected");
 code=0;log.Add("SUCCESS");
 }catch(Exception e){log.Add("FAIL "+e);}finally{File.WriteAllLines("pants-results.txt",log);EditorApplication.Exit(code);}
 }
}}
