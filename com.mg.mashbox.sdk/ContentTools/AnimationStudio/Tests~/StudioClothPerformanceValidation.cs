using System;
using System.IO;
using System.Diagnostics;
using MashBoxSDK.Cloth;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace MashBoxSDK.AnimationStudio {
public static class StudioClothPerformanceValidation {
public static void Run(){var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();GameObject root=null;Mesh mesh=null;MashBoxClothProfile profile=null;MashBoxClothSession session=null;
try{
const int n=109; var verts=new Vector3[n*n];var tris=new int[(n-1)*(n-1)*6];int k=0;
for(int y=0;y<n;y++)for(int x=0;x<n;x++){verts[y*n+x]=new Vector3(x*.01f,y*.01f,.1f*Mathf.Sin(x*.1f));if(x<n-1&&y<n-1){int a=y*n+x;tris[k++]=a;tris[k++]=a+n;tris[k++]=a+1;tris[k++]=a+1;tris[k++]=a+n;tris[k++]=a+n+1;}}
mesh=new Mesh{vertices=verts,triangles=tris};mesh.RecalculateNormals();
root=new GameObject("Cloth benchmark"){hideFlags=HideFlags.HideAndDontSave};UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);root.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=root.AddComponent<MeshRenderer>();
profile=ScriptableObject.CreateInstance<MashBoxClothProfile>();profile.Initialize(mesh);for(int i=0;i<profile.motion.Length;i++)profile.motion[i]=.15f;
profile.particleBudget=1462;profile.frequency=30;profile.substeps=5;profile.iterations=6;profile.interpolateMotion=true;profile.bodyCollisions=true;profile.normalsEverySteps=1;
profile.bodyCapsules=new ClothBoneCapsule[10];for(int i=0;i<10;i++){var a=new GameObject("a"+i).transform;var b=new GameObject("b"+i).transform;a.SetParent(root.transform);b.SetParent(root.transform);a.localPosition=new Vector3(i*.12f,0,0);b.localPosition=new Vector3(i*.12f,1,0);profile.bodyCapsules[i]=new ClothBoneCapsule{startBone=a.name,endBone=b.name,radius=.12f,endRadius=.06f};}
session=new MashBoxClothSession(renderer,profile,root.transform);for(int i=0;i<5;i++)session.Advance(0);
var watch=Stopwatch.StartNew();for(int i=0;i<20;i++)session.Advance(0);watch.Stop();
float maxError=0;
for(int v=0;v<verts.Length;v++){var expected=verts[v];for(int pass=0;pass<3;pass++)for(int c=0;c<10;c++){var capsule=new ClothCapsule(new Vector3(c*.12f,0,0),new Vector3(c*.12f,1,0),.12f,.06f);float distance=capsule.Distance(expected,profile.thickness,out var normal);if(distance<0)expected-=(Vector3)normal*distance;}maxError=Mathf.Max(maxError,Vector3.Distance(expected,session.Vertices[v]));}
if(maxError>0.00001f)throw new Exception("Collision correction differs from reference by "+maxError);
File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/cloth-performance.txt")),"Maximum position error vs original correction: "+maxError+"\n11881 vertices, 10 tapered colliders, display correction average ms: "+(watch.Elapsed.TotalMilliseconds/20).ToString("F3"));
}catch(Exception e){File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/cloth-performance.txt")),e.ToString());}
finally{session?.Dispose();if(root)Object.DestroyImmediate(root);if(mesh)Object.DestroyImmediate(mesh);if(profile)Object.DestroyImmediate(profile);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
}}}
