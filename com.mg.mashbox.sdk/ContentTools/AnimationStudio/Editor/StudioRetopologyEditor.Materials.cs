using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private bool referenceTextured,workingTextured;
        private readonly StudioMeshMaterialPreview materialPreview=new StudioMeshMaterialPreview();
        private readonly Dictionary<Part,bool> materialFoldouts=new Dictionary<Part,bool>();
        private void SetMaterial(Part part,int slot,Material material)
        {
            if(part==null)return;
            int index=Document.parts.IndexOf(part);if(index<0)return;
            if(Document.selected!=index){CancelResolution();CancelVertexSelection();ClearComponents();ClearPatchCorners();}
            Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(Document,"Assign retopology material");
            part.materials=StudioMeshMaterialPreview.Assign(part.materials,part.reference?part.reference.subMeshCount:1,slot,material);
            Document.dirty=true;EditorUtility.SetDirty(Document);
            Document.selected=index;referenceTextured=workingTextured=true;referenceChecker=workingChecker=false;
            ApplyCheckerDisplay();RecordTutorialAction("assignMaterial");RecordTutorialAction("referenceTexture");RecordTutorialAction("workingTexture");repaint();
        }
        private void DrawMaterialSlots(Part part)
        {
            bool open=materialFoldouts.TryGetValue(part,out var value)?value:part==Active;
            materialFoldouts[part]=StudioMeshMaterialPreview.DrawSlots(part.materials,part.reference?part.reference.subMeshCount:1,open,(slot,m)=>SetMaterial(part,slot,m));
        }
        private void ToggleTextured(bool reference)
        {
            if(reference){referenceTextured=!referenceTextured;if(referenceTextured)referenceChecker=false;}
            else{workingTextured=!workingTextured;if(workingTextured)workingChecker=false;}
            if(reference?referenceTextured:workingTextured)RecordTutorialAction(reference?"referenceTexture":"workingTexture");
            ApplyCheckerDisplay();repaint();
        }
        private void DropMaterialOnViewport(Event evt)
        {
            if(!StudioMeshMaterialPreview.IsMaterialDrag)return;
            var ray=HandleUtility.GUIPointToWorldRay(evt.mousePosition);float nearest=float.PositiveInfinity;int hit=-1,slot=-1;
            for(int i=0;i<Document.parts.Count;i++)
            {
                var p=Document.parts[i];
                if(p.showReference&&StudioMeshMaterialPreview.Hit(p.reference,Matrix4x4.identity,ray,ref nearest,out int rs)){hit=i;slot=rs;}
                if(p.showOutput&&i<display.Count&&StudioMeshMaterialPreview.Hit(display[i],Matrix4x4.identity,ray,ref nearest,out int ws)){hit=i;slot=ws;}
            }
            if(hit>=0){var part=Document.parts[hit];int index=slot;StudioMeshMaterialPreview.Drop(m=>SetMaterial(part,index,m));}
        }
    }
}
