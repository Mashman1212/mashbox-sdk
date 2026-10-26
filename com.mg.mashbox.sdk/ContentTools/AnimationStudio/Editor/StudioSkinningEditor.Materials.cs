using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using PartData=MashBoxSDK.AnimationStudio.StudioSkinningDocument.Part;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private bool showTextured;
        private bool sameShading;
        private void DrawSameShadingButton(Rect rect)
        {
            bool next = GUI.Toggle(rect, sameShading, new GUIContent("Same Shading",
                "Show every mesh with the same neutral body shading for lighting and surface comparisons. Turn off to restore material, checker and weight displays. Preview only."), StudioTheme.Styles.miniButton);
            if (next != sameShading) { EndStroke(); sameShading = next; UpdateDisplay(); }
        }
        private readonly StudioMeshMaterialPreview materialPreview=new StudioMeshMaterialPreview();
        private readonly Dictionary<PartData,bool> materialFoldouts=new Dictionary<PartData,bool>();
        private void SetMaterial(PartData part,int slot,Material material)
        {
            if(part==null||part.referenceOnly||IsDefaultReference(part))return;
            EndStroke();Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(Document,"Assign skinning material");
            part.materials=StudioMeshMaterialPreview.Assign(part.materials,part.mesh.subMeshCount,slot,material);
            Document.selected=Document.parts.IndexOf(part);showTextured=true;showUVChecker=false;RefreshSurface();Changed();
        }
        private void DrawMaterialSlots(PartData part)
        {
            bool open=materialFoldouts.TryGetValue(part,out var value)?value:part==Part&&!part.referenceOnly;
            materialFoldouts[part]=StudioMeshMaterialPreview.DrawSlots(part.materials,part.mesh?part.mesh.subMeshCount:1,open,(slot,m)=>SetMaterial(part,slot,m),!part.referenceOnly);
        }
        private void DrawTexturedButton(Rect rect)
        {
            if(DrawSkinModeButton(rect,false,"Textured preview — use the selected mesh's material slots"))
            {showTextured=!showTextured;if(showTextured)showUVChecker=false;UpdateDisplay();}
            if(Event.current.type!=EventType.Repaint)return;
            StudioMeshMaterialPreview.DrawTextureIcon(rect,showTextured?new Color(.5f,1,.87f):new Color(.75f,.81f,.81f));
            if(showTextured)EditorGUI.DrawRect(new Rect(rect.x+5,rect.yMax-3,rect.width-10,2),new Color(.5f,1,.87f));
        }
        private void DropMaterialOnViewport(Event evt,SceneView view)
        {
            if(!StudioMeshMaterialPreview.IsMaterialDrag||ModeRect(view).Contains(evt.mousePosition)||DisplayToolbarRect(view).Contains(evt.mousePosition)||
                (view is MashBoxStudioViewport viewport&&viewport.IsOverSkinningPanels(evt.mousePosition)))return;
            var ray=HandleUtility.GUIPointToWorldRay(evt.mousePosition);float nearest=float.PositiveInfinity;int hit=-1,slot=-1;
            Mesh baked=null;
            try
            {
                for(int i=0;i<objects.Count;i++)
                {
                    if(!Document.parts[i].visible)continue;
                    var skin=objects[i].GetComponent<SkinnedMeshRenderer>();var mesh=displayMeshes[i];
                    if(skin){if(!baked)baked=new Mesh{hideFlags=HideFlags.HideAndDontSave};skin.BakeMesh(baked);mesh=baked;}
                    if(StudioMeshMaterialPreview.Hit(mesh,objects[i].transform.localToWorldMatrix,ray,ref nearest,out int s)){hit=i;slot=s;}
                }
            }
            finally{if(baked)Object.DestroyImmediate(baked);}
            if(hit<0)return;
            var part=Document.parts[hit];
            if(part.referenceOnly){DragAndDrop.visualMode=DragAndDropVisualMode.Rejected;evt.Use();return;}
            StudioMeshMaterialPreview.Drop(m=>SetMaterial(part,slot,m));
        }
    }
}
