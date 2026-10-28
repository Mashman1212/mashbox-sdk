using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        internal Action<GameObject> SendToCloth;

        private void SendPartToCloth(StudioSkinningDocument.Part part)
        {
            if(part==null||!part.mesh||SendToCloth==null)return;
            EndStroke();FinishMeshRename(false);CancelBrushInteraction();
            SendToCloth(CreateClothTransfer(part));
        }

        internal GameObject CreateClothTransfer(StudioSkinningDocument.Part part)
        {
            if(part==null||!part.mesh)throw new InvalidOperationException("Select a mesh to send to Cloth Studio.");
            const string folder="Assets/MashBox Studio Transfers";
            if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets","MashBox Studio Transfers");
            string name=new string(part.name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c).ToArray());
            string meshPath=AssetDatabase.GenerateUniqueAssetPath(folder+"/"+name+" Cloth.asset");
            string prefabPath=AssetDatabase.GenerateUniqueAssetPath(folder+"/"+name+" Cloth.prefab");
            GameObject root=null;Mesh mesh=null;bool complete=false;
            try
            {
                mesh=Object.Instantiate(part.mesh);mesh.name=part.name;mesh.hideFlags=HideFlags.None;
                if(part.bound)
                {
                    var pose=CapturePartExportPose(part);
                    root=Object.Instantiate(Document.skeleton);root.hideFlags=HideFlags.None;
                    root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);root.transform.localScale=Vector3.one;
                    foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))Object.DestroyImmediate(renderer);
                    foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))Object.DestroyImmediate(filter);
                    PoseExportSkeleton(root,pose);
                    var child=new GameObject(part.name);child.transform.SetParent(root.transform,false);
                    var skin=child.AddComponent<SkinnedMeshRenderer>();
                    skin.bones=ResolveExportBones(root,skin,new StudioSkinWeights(mesh),pose);
                    // Bake using the same SDK pose as FBX export, preserving
                    // stitched bind corrections in the detached copy.
                    BakePartForTPose(mesh,pose);
                    skin.sharedMesh=mesh;skin.sharedMaterials=part.materials;skin.rootBone=root.transform;skin.localBounds=mesh.bounds;
                }
                else
                {
                    root=new GameObject(part.name);
                    root.AddComponent<MeshFilter>().sharedMesh=mesh;root.AddComponent<MeshRenderer>().sharedMaterials=part.materials;
                }
                AssetDatabase.CreateAsset(mesh,meshPath);
                var prefab=PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
                if(!prefab)throw new InvalidOperationException("Could not create the Cloth Studio transfer model.");
                AssetDatabase.SaveAssetIfDirty(mesh);complete=true;return prefab;
            }
            finally
            {
                if(root)Object.DestroyImmediate(root);
                if(!complete){AssetDatabase.DeleteAsset(prefabPath);AssetDatabase.DeleteAsset(meshPath);}
                if(mesh&&!EditorUtility.IsPersistent(mesh))Object.DestroyImmediate(mesh);
            }
        }
    }

    public sealed partial class AnimationStudioWindow
    {
        private void SendModelToCloth(GameObject model)
        {
            if(!model)return;
            // Add before binding the mode so an existing simulation and its
            // profile are released through the regular garment-import path.
            AddClothModels(new[]{model});
            OpenClothMode();
        }

        private void SendRetopologyToCloth(StudioRetopologyDocument.Part source,bool reference)
        {
            if(source==null||!source.reference)return;
            var part=source;
            if(reference)
            {
                part=new StudioRetopologyDocument.Part {
                    name=source.name,reference=source.reference,materials=source.materials,
                    skinBoneNames=source.skinBoneNames,skinBindCorrections=source.skinBindCorrections
                };
                StudioRetopologyDocument.CopyReference(part);
            }
            // Reuse weight transfer without modifying the user's Skinner workspace.
            var document=CreateInstance<StudioSkinningDocument>();document.hideFlags=HideFlags.HideAndDontSave;
            document.skeleton=ResolveSdkSkeleton();
            StudioSkinningEditor transfer=null;GameObject model;
            try
            {
                transfer=new StudioSkinningEditor(document,()=>{});
                transfer.ImportRetopology(part);
                if(document.parts.Count==0)throw new InvalidOperationException("The selected mesh has no faces to send.");
                model=transfer.CreateClothTransfer(document.parts[document.selected]);
            }
            finally
            {
                transfer?.Dispose();
                foreach(var item in document.parts)if(item.mesh&&!EditorUtility.IsPersistent(item.mesh))DestroyImmediate(item.mesh);
                Undo.ClearUndo(document);DestroyImmediate(document);
            }
            SendModelToCloth(model);
        }
    }
}
