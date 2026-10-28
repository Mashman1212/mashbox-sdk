using System;
using System.IO;
using System.Diagnostics;
using MashBoxSDK.Cloth;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace MashBoxSDK.AnimationStudio {
public static class StudioClothSolverPerformanceValidation {
public static void Run(){var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();GameObject root=null;Mesh mesh=null;MashBoxClothProfile profile=null;MashBoxClothSession session=null;
try{
const int n=109; var verts=new Vector3[n*n];var tris=new int[(n-1)*(n-1)*6];int k=0;
for(int y=0;y<n;y++)for(int x=0;x<n;x++){verts[y*n+x]=new Vector3(x*.01f,y*.01f,.1f*Mathf.Sin(x*.1f));if(x<n-1&&y<n-1){int a=y*n+x;tris[k++]=a;tris[k++]=a+n;tris[k++]=a+1;tris[k++]=a+1;tris[k++]=a+n;tris[k++]=a+n+1;}}
mesh=new Mesh{vertices=verts,triangles=tris};mesh.RecalculateNormals();
root=new GameObject("Cloth benchmark"){hideFlags=HideFlags.HideAndDontSave};UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);root.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=root.AddComponent<MeshRenderer>();
profile=ScriptableObject.CreateInstance<MashBoxClothProfile>();profile.Initialize(mesh);for(int i=0;i<profile.motion.Length;i++)profile.motion[i]=.15f;
profile.particleBudget=1462;profile.frequency=30;profile.substeps=5;profile.iterations=6;profile.interpolateMotion=true;profile.bodyCollisions=true;profile.normalsEverySteps=1;
profile.bodyCapsules=new ClothBoneCapsule[10];for(int i=0;i<10;i++){var a=new GameObject("a"+i).transform;var b=new GameObject("b"+i).transform;a.SetParent(root.transform);b.SetParent(root.transform);a.localPosition=new Vector3(i*.12f,0,0);b.localPosition=new Vector3(i*.12f,1,0);profile.bodyCapsules[i]=new ClothBoneCapsule{startBone=a.name,endBone=b.name,radius=.12f,endRadius=.06f};}
session=new MashBoxClothSession(renderer,profile,root.transform);
var report="Particles: "+session.Solver.ParticleCount+"; 5 substeps, 6 iterations, 10 tapered colliders\n";
foreach(bool collisions in new[]{false,true}){profile.bodyCollisions=collisions;session.Reset();for(int i=0;i<5;i++)session.Step(1f/30);double total=0;for(int i=0;i<40;i++){session.Step(1f/30);total+=session.SolveMilliseconds;}report+=(collisions?"Collision ON":"Collision OFF")+" solve ms: "+(total/40).ToString("F3")+"\n";}
var samples=new System.Text.StringBuilder();foreach(var v in session.Vertices)samples.AppendLine(v.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+v.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+v.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/cloth-solver-positions.txt")),samples.ToString());
File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/cloth-performance.txt")),report);

}catch(Exception e){File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/cloth-performance.txt")),e.ToString());}
finally{session?.Dispose();if(root)Object.DestroyImmediate(root);if(mesh)Object.DestroyImmediate(mesh);if(profile)Object.DestroyImmediate(profile);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
}}}
