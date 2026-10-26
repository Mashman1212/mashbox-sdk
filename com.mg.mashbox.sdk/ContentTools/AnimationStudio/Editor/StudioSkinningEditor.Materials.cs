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
            if(DrawSkinModeButton(rect,false,"Same Shading — show every mesh with the same neutral body shading. Turn off to restore material, checker and weight displays. Preview only."))
            { EndStroke(); sameShading=!sameShading; UpdateDisplay(); }
            if(Event.current.type!=EventType.Repaint)return;
            var previous=Handles.color;var matrix=Handles.matrix;
            try
            {
                Handles.matrix=Matrix4x4.identity;
                var tint=sameShading?new Color(.5f,1,.87f):new Color(.75f,.81f,.81f);
                // Two identically shaded spheres and an equals sign.
                foreach(float x in new[]{-8f,8f})
                {
                    var center=new Vector3(rect.center.x+x,rect.center.y,0);
                    Handles.color=new Color(tint.r,tint.g,tint.b,.35f);
                    Handles.DrawSolidArc(center,Vector3.forward,Vector3.up,180,5);
                    Handles.color=tint;Handles.DrawWireDisc(center,Vector3.forward,5);
                }
                Handles.color=tint;
                for(int y=-2;y<=2;y+=4)
                    Handles.DrawAAPolyLine(1.4f,new Vector3(rect.center.x-2,rect.center.y+y),new Vector3(rect.center.x+2,rect.center.y+y));
                if(sameShading)EditorGUI.DrawRect(new Rect(rect.x+5,rect.yMax-3,rect.width-10,2),new Color(.5f,1,.87f));
            }
            finally {Handles.color=previous;Handles.matrix=matrix;}
        }
        private readonly StudioMeshMaterialPreview materialPreview=new StudioMeshMaterialPreview();
        private readonly Dictionary<PartData,bool> materialFoldouts=new Dictionary<PartData,bool>();
        private void SetMaterial(PartData part,int slot,Material material)
        {
            if(part==null||part.referenceOnly||IsDefaultReference(part))return;
            EndStroke();PausePreviewAnimation();Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(Document,"Assign skinning material");
            part.materials=StudioMeshMaterialPreview.Assign(part.materials,part.mesh.subMeshCount,slot,material);
            Document.selected=Document.parts.IndexOf(part);sameShading=false;showTextured=true;showUVChecker=false;RefreshSurface();Changed();
        }
        private void DrawMaterialSlots(PartData part)
        {
            bool open=materialFoldouts.TryGetValue(part,out var value)?value:part==Part&&!part.referenceOnly;
            materialFoldouts[part]=StudioMeshMaterialPreview.DrawSlots(part.materials,part.mesh?part.mesh.subMeshCount:1,open,(slot,m)=>SetMaterial(part,slot,m),!part.referenceOnly);
        }
        private void DrawTexturedButton(Rect rect)
        {
            if(DrawSkinModeButton(rect,false,"Textured preview — show assigned materials on all user meshes"))
            {showTextured=!showTextured;if(showTextured)showUVChecker=false;UpdateDisplay();}
            if(Event.current.type!=EventType.Repaint)return;
            StudioMeshMaterialPreview.DrawTextureIcon(rect,showTextured?new Color(.5f,1,.87f):new Color(.75f,.81f,.81f));
            if(showTextured)EditorGUI.DrawRect(new Rect(rect.x+5,rect.yMax-3,rect.width-10,2),new Color(.5f,1,.87f));
        }
        private void DropMaterialOnViewport(Event evt,SceneView view)
        {
            if(!StudioMeshMaterialPreview.IsMaterialDrag||ModeRect(view).Contains(evt.mousePosition)||DisplayToolbarRect(view).Contains(evt.mousePosition)||PoseToolbarRect(view).Contains(evt.mousePosition)||
                (view is MashBoxStudioViewport viewport&&(!viewport.ModelCanvasRect.Contains(evt.mousePosition)||viewport.IsOverSkinningPanels(evt.mousePosition))))return;
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
            if(hit<0){DragAndDrop.visualMode=DragAndDropVisualMode.Rejected;evt.Use();return;}
            var part=Document.parts[hit];
            if(part.referenceOnly){DragAndDrop.visualMode=DragAndDropVisualMode.Rejected;evt.Use();return;}
            StudioMeshMaterialPreview.Drop(m=>SetMaterial(part,slot,m));
        }
    }
}
