using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.Cloth;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;
namespace MashBoxSDK.AnimationStudio {
public static class StudioClothSeamValidation {
public static void Run(){var log=new List<string>();Mesh mesh=null;GameObject go=null;MashBoxClothProfile profile=null;MashBoxClothSession session=null;var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
void Check(bool ok,string label){if(!ok)throw new Exception(label);log.Add("PASS "+label);}
try{
var p=new Part{vertices=new List<Vector3>{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(0,1,0)},faces=new List<Face>{new Face(new[]{0,1,2}){cornerUV=new[]{Vector2.zero,Vector2.right,Vector2.one}},new Face(new[]{0,2,3}){cornerUV=new[]{Vector2.one,Vector2.zero,Vector2.up}}}};
mesh=StudioRetopologyDocument.BuildMesh(p);var originalUV=mesh.uv;
var seams=MashBoxClothSeams.Build(mesh);Check(mesh.vertexCount==6&&seams.Distinct().Count()==4,"Retopology export UV splits reconnect as four physical vertices");
var movement=Enumerable.Repeat(.2f,6).ToArray();movement[0]=0;
Check(MashBoxClothSeams.Synchronize(movement,seams),"Conflicting seam weights repaired");
Check(Enumerable.Range(0,6).Where(i=>seams[i]==0).All(i=>movement[i]==0),"Pinned seam remains pinned on every copy");
Check(!MashBoxClothSeams.Synchronize(movement,seams),"Repair is idempotent");
var neighbors=AnimationStudioWindow.BuildClothPaintNeighbors(6,mesh.triangles,seams);
Check(neighbors[0].Contains(1)&&neighbors[0].Contains(3),"Smooth brush crosses both sides of exported seam");
var targets=new float[6];AnimationStudioWindow.ClothSmoothTargets(movement,neighbors,targets);
Check(Enumerable.Range(0,6).All(i=>targets[i]==targets[seams[i]]),"Seam copies receive identical smooth targets");
go=new GameObject("Cloth seam test");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();
profile=ScriptableObject.CreateInstance<MashBoxClothProfile>();profile.Initialize(mesh);profile.motion=Enumerable.Repeat(.2f,6).ToArray();profile.motion[0]=0;profile.wind=Vector3.zero;profile.bodyCollisions=false;
session=new MashBoxClothSession(renderer,profile,go.transform);for(int i=0;i<10;i++)session.Step(1f/60);
Check(Enumerable.Range(0,6).All(i=>session.Vertices[i]==session.Vertices[seams[i]]),"Simulated UV seam stays sealed despite legacy conflicting profile");
Check(originalUV.SequenceEqual(mesh.uv)&&mesh.vertexCount==6,"UV layout and source render splits untouched");
var bones=new BoneWeight[6];for(int i=0;i<6;i++)bones[i]=new BoneWeight{boneIndex0=i,weight0=1};mesh.boneWeights=bones;
Check(MashBoxClothSeams.Build(mesh).Distinct().Count()==6,"Coincident vertices with different skin bindings stay separate");
log.Add("SUCCESS");
}catch(Exception e){log.Add("FAIL "+e);}finally{session?.Dispose();if(go)UnityEngine.Object.DestroyImmediate(go);if(mesh)UnityEngine.Object.DestroyImmediate(mesh);if(profile)UnityEngine.Object.DestroyImmediate(profile);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);File.WriteAllLines("Library/cloth-seam-validation.txt",log);}
}}
}
