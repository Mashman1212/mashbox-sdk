using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private bool referenceChecker,workingChecker;
        private Material checkerMaterial;
        private Texture2D checkerTexture;
        private Material CheckerMaterial
        {
            get
            {
                if(checkerMaterial)return checkerMaterial;
                const int size=512,cell=16;
                checkerTexture=new Texture2D(size,size,TextureFormat.RGBA32,true){name="Retopology UV checker",hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear,anisoLevel=4};
                var pixels=new Color32[size*size];
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)pixels[y*size+x]=((x/cell+y/cell)&1)==0?new Color32(215,215,215,255):new Color32(48,55,60,255);
                checkerTexture.SetPixels32(pixels);checkerTexture.Apply(true,true);
                checkerMaterial=new Material(referenceMaterial){name="Retopology UV inspection",hideFlags=HideFlags.HideAndDontSave};
                checkerMaterial.SetTexture("_MainTex",checkerTexture);checkerMaterial.SetColor("_Color",Color.white);checkerMaterial.SetFloat("_Unlit",.7f);
                return checkerMaterial;
            }
        }
        private void ToggleChecker(bool reference)
        {
            if(reference){referenceChecker=!referenceChecker;if(referenceChecker)referenceTextured=false;}else{workingChecker=!workingChecker;if(workingChecker)workingTextured=false;}
            if(reference?referenceChecker:workingChecker)RecordTutorialAction(reference?"referenceChecker":"workingChecker");
            ApplyCheckerDisplay();repaint();
        }
        private void ApplyCheckerDisplay()
        {
            for(int i=0;i<references.Count;i++)
            {
                void Apply(GameObject go,Material material)
                {
                    if(!go)return;var r=go.GetComponent<MeshRenderer>();
                    r.sharedMaterials=Enumerable.Repeat(material,Mathf.Max(1,r.sharedMaterials.Length)).ToArray();
                }
                Apply(references[i],i==Document.selected&&referenceChecker?CheckerMaterial:referenceMaterial);
                Apply(outputs[i],i==Document.selected&&workingChecker?CheckerMaterial:outputMaterial);
                var part=Document.parts[i];
                if(i==Document.selected&&referenceTextured)references[i].GetComponent<MeshRenderer>().sharedMaterials=materialPreview.Materials(part.materials,part.reference?part.reference.subMeshCount:1,referenceMaterial);
                if(i==Document.selected&&workingTextured)outputs[i].GetComponent<MeshRenderer>().sharedMaterials=materialPreview.Materials(part.materials,display[i].subMeshCount,outputMaterial);
            }
        }
        private void TransferReferenceUVs()
        {
            if(Active==null||!Active.reference)throw new InvalidOperationException("Select a reference with a working mesh first.");
            var transfer=new StudioRetopologyUVTransfer(Active.reference);
            Change("Transfer reference UVs",()=>transfer.Transfer(Active));
            RecordTutorialAction("transferUVs");
            status="UV0 transferred from the reference. Inspect seams and stretching with the two checker buttons. Faces crossing source seams may need a new unwrap.";
            view?.ShowNotification(new GUIContent("Reference UVs transferred"));
        }
        private void DrawUVMenu()
        {
            Menu("UV",menu=>
            {
                if(Active!=null&&Active.faces.Count>0)menu.AddItem(new GUIContent("UV Editor..."),false,()=>Run(OpenRetopologyUVEditor));
                else menu.AddDisabledItem(new GUIContent("UV Editor..."));
                if(Active?.reference&&Active.faces.Count>0)menu.AddItem(new GUIContent("Transfer UVs from Reference"),false,()=>Run(TransferReferenceUVs));
                else menu.AddDisabledItem(new GUIContent("Transfer UVs from Reference"));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Reference Checker"),referenceChecker,()=>ToggleChecker(true));
                menu.AddItem(new GUIContent("Working Mesh Checker"),workingChecker,()=>ToggleChecker(false));
                menu.AddItem(new GUIContent("Reference Textured"),referenceTextured,()=>ToggleTextured(true));
                menu.AddItem(new GUIContent("Working Mesh Textured"),workingTextured,()=>ToggleTextured(false));
            });
        }
        private void DisposeUVDisplay()
        {
            if(checkerMaterial)UnityEngine.Object.DestroyImmediate(checkerMaterial);
            if(checkerTexture)UnityEngine.Object.DestroyImmediate(checkerTexture);
        }
    }
}
