using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.Cloth;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioClothImportValidation
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        static string Report=>Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/cloth-import-validation.txt"));
        public static void Run()
        {
            var log=new List<string>();var owned=new List<Mesh>();var objects=new List<GameObject>();
            var scene=EditorSceneManager.NewPreviewScene();AnimationStudioWindow window=null;
            string folder="Assets/__ClothImportValidation_"+Guid.NewGuid().ToString("N");
            void Check(bool yes,string name){if(!yes)throw new Exception(name);log.Add("PASS "+name);}
            object Get(string name)=>typeof(AnimationStudioWindow).GetField(name,Flags).GetValue(window);
            void Set(string name,object value)=>typeof(AnimationStudioWindow).GetField(name,Flags).SetValue(window,value);
            object Call(string name,params object[] args)=>typeof(AnimationStudioWindow).GetMethod(name,Flags).Invoke(window,args);
            GameObject Garment(string name,bool weightHelper,bool nullHelper=false,bool fiveWeights=false)
            {
                var root=new GameObject(name);root.hideFlags=HideFlags.HideAndDontSave;SceneManager.MoveGameObjectToScene(root,scene);objects.Add(root);
                var names=fiveWeights?new[]{"Hips","Spine","LeftUpLeg","RightUpLeg","UnknownWeightedHelper"}:new[]{"Hips","UnknownHelper"};
                var bones=names.Select(n=>{var bone=new GameObject(n).transform;bone.SetParent(root.transform,false);return bone;}).ToArray();
                if(nullHelper)bones[1]=null;
                var mesh=new Mesh{name=name};owned.Add(mesh);mesh.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up};mesh.triangles=new[]{0,1,2};mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up};mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.bindposes=names.Select(n=>Matrix4x4.identity).ToArray();
                var weights=new StudioSkinWeights(mesh);
                foreach(var list in weights.Values)
                    if(fiveWeights)for(int i=0;i<5;i++)list.Add(new BoneWeight1{boneIndex=i,weight=.2f});
                    else list.Add(new BoneWeight1{boneIndex=weightHelper?1:0,weight=1});
                weights.Apply(mesh);
                var skin=root.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;skin.bones=bones;skin.rootBone=bones[0];skin.localBounds=mesh.bounds;return root;
            }
            try
            {
                var skeleton=AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.mg.mashbox.sdk/Runtime/Characters/Human/Skeleton.fbx");
                var pants=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/PJX CC/Pants/Meshes/Human_Pants_Punch_WC.fbx");
                var other=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/PJX CC/Pants/Meshes/Human_Pants_Destruction_Raw Cut.fbx");
                Check(skeleton&&pants&&other,"Actual project pants and SDK skeleton are available");
                var original=pants.GetComponentInChildren<SkinnedMeshRenderer>(true);var mesh=original.sharedMesh;
                var originalBones=original.bones;var bindposes=mesh.bindposes;var triangles=mesh.triangles;var beforeWeights=new StudioSkinWeights(mesh).Values.SelectMany(v=>v).ToArray();
                using(var rig=new AuthoringRig(skeleton,null,new[]{pants}))
                {
                    var attached=rig.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r=>r.sharedMesh==mesh);
                    Check(attached.bones.Length==originalBones.Length&&attached.sharedMesh==mesh,"Pants attach with the original mesh and bone-slot layout");
                    var used=new HashSet<int>(beforeWeights.Where(w=>w.weight>0).Select(w=>w.boneIndex));
                    Check(used.All(i=>StudioSkinningDocument.BoneName(attached.bones[i].name)==StudioSkinningDocument.BoneName(originalBones[i].name)),"Every weighted pants bone maps to its matching SDK bone");
                    var helpers=Enumerable.Range(0,originalBones.Length).Where(i=>originalBones[i]&&(originalBones[i].name=="joint4"||originalBones[i].name=="joint5")).ToArray();
                    Check(helpers.Length==2&&helpers.All(i=>!used.Contains(i)&&attached.bones[i]==rig.Root.transform),"Unweighted joint4 and joint5 slots use harmless preview placeholders");
                    var baked=new Mesh();owned.Add(baked);attached.BakeMesh(baked,true);
                    Check(baked.vertexCount==mesh.vertexCount&&baked.vertices.All(v=>!float.IsNaN(v.x)&&!float.IsInfinity(v.x))&&baked.bounds.size.sqrMagnitude>0,"Imported pants bake into finite, non-empty cloth preview geometry");
                }
                Check(original.bones.SequenceEqual(originalBones)&&mesh.bindposes.SequenceEqual(bindposes)&&mesh.triangles.SequenceEqual(triangles),"Source FBX skeleton, bind poses and topology are unchanged");
                var afterWeights=new StudioSkinWeights(mesh).Values.SelectMany(v=>v).ToArray();
                Check(beforeWeights.Length==afterWeights.Length&&beforeWeights.Select((w,i)=>w.boneIndex==afterWeights[i].boneIndex&&w.weight==afterWeights[i].weight).All(v=>v),"All original pants skin weights remain unchanged");
                foreach(bool missing in new[]{false,true})
                {
                    var garment=Garment("Unused helper",false,missing);
                    using(var rig=new AuthoringRig(skeleton,null,new[]{garment}))
                        Check(rig.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r=>r.sharedMesh==garment.GetComponent<SkinnedMeshRenderer>().sharedMesh),"Unweighted "+(missing?"null":"unknown")+" helper slots do not block import");
                }
                var invalid=Garment("Weighted incompatible garment",true);
                bool rejected=false;int scenes=EditorSceneManager.previewSceneCount;
                try{using(var rig=new AuthoringRig(skeleton,null,new[]{invalid})){} }catch(InvalidOperationException e){rejected=e.Message.Contains("weighted bone");}
                Check(rejected&&EditorSceneManager.previewSceneCount==scenes,"A truly incompatible weighted bone still fails and cleans up its temporary preview");
                var five=Garment("Five-weight garment",false,false,true);rejected=false;
                try{using(var rig=new AuthoringRig(skeleton,null,new[]{five})){} }catch(InvalidOperationException e){rejected=e.Message.Contains("UnknownWeightedHelper");}
                Check(rejected,"Validation includes influences beyond the first four weights per vertex");

                window=ScriptableObject.CreateInstance<AnimationStudioWindow>();window.hideFlags=HideFlags.HideAndDontSave;Set("clothCharacter",skeleton);Set("clothOutfit",Array.Empty<GameObject>());Set("clothGarment",null);
                Call("BuildClothPreview");
                var rows=(StudioSkinningDocument.Part[])Get("clothMeshRows");
                Check(rows.Length>=4&&rows.All(p=>p.referenceOnly),"Cloth Studio builds its default reference list");
                Call("AddClothModels",(object)new[]{pants});
                rows=(StudioSkinningDocument.Part[])Get("clothMeshRows");
                Check(rows.Any(p=>p.sourceMesh==mesh&&!p.referenceOnly),"Dropping the actual pants adds an editable garment row");
                int selected=(int)Get("clothRendererIndex");var renderers=(Renderer[])Get("clothRenderers");
                Check(rows[selected].sourceMesh==mesh&&renderers[selected].gameObject.activeSelf,"Newly dropped pants are selected and visible");
                var oldRig=(AuthoringRig)Get("clothRig");var oldRows=rows;var oldOutfit=(GameObject[])Get("clothOutfit");var oldRenderers=renderers;
                AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
                var invalidSkin=invalid.GetComponent<SkinnedMeshRenderer>();invalidSkin.sharedMesh.hideFlags=HideFlags.None;AssetDatabase.CreateAsset(invalidSkin.sharedMesh,folder+"/invalid.asset");
                invalid.hideFlags=HideFlags.None;var invalidAsset=PrefabUtility.SaveAsPrefabAsset(invalid,folder+"/invalid.prefab");
                rejected=false;
                try{Call("AddClothModels",(object)new[]{other,invalidAsset});}catch(TargetInvocationException e){rejected=e.GetBaseException() is InvalidOperationException;}
                Check(rejected&&ReferenceEquals(Get("clothRig"),oldRig)&&oldRig.Root&&oldRig.Scene.IsValid(),"Failed drops keep the previous live cloth rig and preview scene");
                Check(ReferenceEquals(Get("clothMeshRows"),oldRows)&&ReferenceEquals(Get("clothRenderers"),oldRenderers)&&(int)Get("clothRendererIndex")==selected,"Failed drops keep mesh rows, renderer objects and selection");
                Check(ReferenceEquals(Get("clothOutfit"),oldOutfit)&&((GameObject[])Get("clothOutfit")).SequenceEqual(new[]{pants}),"A mixed valid/invalid batch rolls back the entire pending outfit addition");
                Call("AddClothModels",(object)new[]{other});
                rows=(StudioSkinningDocument.Part[])Get("clothMeshRows");
                Check(rows.Count(p=>!p.referenceOnly)==2,"Another valid pair of pants can be dropped after an import failure");
                var currentRig=Get("clothRig");Call("AddClothModels",(object)new[]{pants});
                Check(ReferenceEquals(Get("clothRig"),currentRig),"Dropping an existing garment again does not duplicate it or rebuild the preview");
                log.Add("SUCCESS");
            }
            catch(Exception e){log.Add("FAIL "+e);}
            finally
            {
                if(window)Object.DestroyImmediate(window);
                foreach(var root in objects)if(root)Object.DestroyImmediate(root);
                if(AssetDatabase.IsValidFolder(folder))AssetDatabase.DeleteAsset(folder);
                foreach(var mesh in owned)if(mesh&&!EditorUtility.IsPersistent(mesh))Object.DestroyImmediate(mesh);
                if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);File.WriteAllLines(Report,log);
            }
        }
    }
}
