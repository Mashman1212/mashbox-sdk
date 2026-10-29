using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio {
public static class StudioRetopoTransformValidation {
 public static void Run(){var log=new List<string>();Mesh mesh=null;
 void Check(bool ok,string label){if(!ok)throw new Exception(label);log.Add("PASS "+label);}
 Vector3 Apply(Vector3 start,Vector3 position,Quaternion turn,Vector3 size,Tool tool)=>StudioRetopologyEditor.TransformPoint(start,Vector3.zero,position,turn,size,Quaternion.identity,tool);
 try{
 var start=new Vector3(1,0,0);
 Check(Apply(start,Vector3.up,Quaternion.identity,Vector3.one,Tool.Move)==new Vector3(1,1,0),"Move uses full displacement from drag start");
 Check((Apply(start,Vector3.zero,Quaternion.Euler(0,0,90),Vector3.one,Tool.Rotate)-Vector3.up).magnitude<.00001f,"Rotation uses full angle from drag start");
 Check(Apply(start,Vector3.zero,Quaternion.identity,new Vector3(2,3,4),Tool.Scale)==Vector3.right*2,"Scaling uses original geometry");
 mesh=new Mesh();mesh.vertices=new[]{new Vector3(-10,0,-10),new Vector3(-10,0,10),new Vector3(10,0,10),new Vector3(10,0,-10)};mesh.triangles=new[]{0,1,2,0,2,3};
 var surface=new StudioRetopologySurface(mesh);
 var snapped=surface.Project(Apply(start,new Vector3(0,.1f,0),Quaternion.identity,Vector3.one,Tool.Move),0);
 Check((snapped-start).magnitude<.00001f,"Surface constraint still keeps normal-only motion on surface");
 snapped=surface.Project(Apply(start,new Vector3(2,.1f,0),Quaternion.identity,Vector3.one,Tool.Move),0);
 Check((snapped-new Vector3(3,0,0)).magnitude<.00001f,"Tangential movement survives earlier snapped motion");
 var turn=Quaternion.Euler(0,0,60);snapped=surface.Project(Apply(start,Vector3.zero,turn,Vector3.one,Tool.Rotate),0);
 Check(Mathf.Abs(snapped.x-.5f)<.00001f,"Projected rotation retains accumulated angle instead of shrinking incrementally");
 log.Add("SUCCESS");
 }catch(Exception e){log.Add("FAIL "+e);}finally{if(mesh)UnityEngine.Object.DestroyImmediate(mesh);File.WriteAllLines("Library/retopo-transform-validation.txt",log);}
 }
}
}
