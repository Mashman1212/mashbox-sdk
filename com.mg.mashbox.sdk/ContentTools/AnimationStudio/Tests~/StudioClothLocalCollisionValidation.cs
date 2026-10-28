using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.Cloth;
namespace MashBoxSDK.AnimationStudio {
public static class StudioClothLocalCollisionValidation {
 public static void Run(){var log=new List<string>();GameObject root=null;Mesh mesh=null;MashBoxClothProfile profile=null;MashBoxClothSession session=null;var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
 void Check(bool ok,string name){if(!ok)throw new Exception(name);log.Add("PASS "+name);}
 try{
 root=new GameObject("Local cloth collision test");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
 Transform Bone(string name,Transform parent){var t=new GameObject(name).transform;t.SetParent(parent,false);return t;}
 var hip=Bone("hip",root.transform);var left=Bone("left",hip);var right=Bone("right",hip);var lf=Bone("lf",left);var rf=Bone("rf",right);var arm=Bone("arm",hip);var hand=Bone("hand",arm);
 var starts=new[]{left,right,arm};var ends=new[]{lf,rf,hand};
 Check(MashBoxClothCollisionFilter.BoneMask(right,starts,ends,3)==2,"Right leg excludes left leg and arm");
 Check(MashBoxClothCollisionFilter.BoneMask(left,starts,ends,3)==1,"Left leg excludes right leg");
 var masks=new[]{1UL,2UL,4UL};
 Check(MashBoxClothCollisionFilter.VertexMask(new BoneWeight{boneIndex0=0,weight0=.5f,boneIndex1=1,weight1=.5f},masks)==3,"Blended joint retains both contributing regions");
 Check(MashBoxClothCollisionFilter.VertexMask(new BoneWeight{boneIndex0=1,weight0=.99f,boneIndex1=0,weight1=.01f},masks)==2,"Tiny stray skin influence does not admit opposite leg");
 var vertices=new[]{Vector3.zero,new Vector3(.02f,0,0),new Vector3(0,.02f,0)};mesh=new Mesh{vertices=vertices,triangles=new[]{0,1,2},bindposes=new[]{Matrix4x4.identity}};mesh.boneWeights=new[]{new BoneWeight{boneIndex0=0,weight0=1},new BoneWeight{boneIndex0=0,weight0=1},new BoneWeight{boneIndex0=0,weight0=1}};mesh.RecalculateNormals();
 var skin=root.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;skin.bones=new[]{right};skin.rootBone=hip;
 profile=ScriptableObject.CreateInstance<MashBoxClothProfile>();profile.Initialize(mesh);profile.motion=new[]{1f,1f,1f};profile.gravity=profile.wind=Vector3.zero;profile.bodyCapsules=new[]{new ClothBoneCapsule{startBone="hip/left",endBone="hip/left/lf",radius=.3f}};
 session=new MashBoxClothSession(skin,profile,root.transform);session.Step(1f/30);
 Check(session.Vertices[0].magnitude<.001f,"Filtered render and solve ignore overlapping unrelated leg");
 profile.localBoneCollisions=false;session.Step(1f/30);
 Check(session.Vertices[0].magnitude>.29f,"Full-body toggle restores collision live");
 session.Dispose();session=null;profile.localBoneCollisions=true;profile.bodyCapsules[0].startBone="hip/right";profile.bodyCapsules[0].endBone="hip/right/rf";
 session=new MashBoxClothSession(skin,profile,root.transform);session.Step(1f/30);
 Check(session.Vertices[0].magnitude>.29f,"Associated leg still collides");
 log.Add("SUCCESS");
 }catch(Exception e){log.Add("FAIL "+e);}finally{session?.Dispose();if(root)UnityEngine.Object.DestroyImmediate(root);if(mesh)UnityEngine.Object.DestroyImmediate(mesh);if(profile)UnityEngine.Object.DestroyImmediate(profile);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);File.WriteAllLines("Library/cloth-local-collision-validation.txt",log);}
 }
}
}
