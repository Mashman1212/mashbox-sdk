using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioImagingWindow
    {
        private bool marqueeSpaceHeld,movingImageSelection,selectionMoveHover;
        private Vector2 marqueeLastRaw,marqueePointerOffset,selectionMoveStart,selectionMoveMaskStart,selectionMaskOffset;
        private Rect selectionMoveBounds;
        private string selectionMoveCheckpoint;
        private int selectionMoveControl;
        private void HandleMarqueeSpaceKey()
        {
            var e=Event.current;
            if(imageTool!=ImageTool.Marquee||EditorGUIUtility.editingTextField){marqueeSpaceHeld=false;return;}
            if(e.keyCode==KeyCode.Space&&(e.type==EventType.KeyDown||e.type==EventType.KeyUp))
            {marqueeSpaceHeld=e.type==EventType.KeyDown;e.Use();Repaint();}
        }
        private static Vector2 ClampSelectionDelta(Rect bounds,Vector2 delta)=>new Vector2(Mathf.Clamp(delta.x,-bounds.xMin,1-bounds.xMax),Mathf.Clamp(delta.y,-bounds.yMin,1-bounds.yMax));
        private void DragMarqueeRectangle(Vector2 raw)
        {
            if(marqueeSpaceHeld)
            {
                var delta=ClampSelectionDelta(imageSelection,raw-marqueeLastRaw);
                imageSelectionStart+=delta;marqueePointer+=delta;imageSelection.position+=delta;
                marqueePointerOffset=marqueePointer-raw;
            }
            else
            {
                var point=raw+marqueePointerOffset;marqueePointer=new Vector2(Mathf.Clamp01(point.x),Mathf.Clamp01(point.y));
                UpdateMarqueeRectangle();
            }
            marqueeLastRaw=raw;
        }
        private bool HandleImageSelectionMove(Rect rect)
        {
            var e=Event.current;int id=GUIUtility.GetControlID("Image selection move".GetHashCode(),FocusType.Passive);
            var point=GuiToCanvas(e.mousePosition,rect)/CanvasSize;
            selectionMoveHover=imageTool==ImageTool.Marquee&&hasImageSelection&&!drawingImageSelection&&!tracingSelection&&!choosingColorRange&&rect.Contains(e.mousePosition)&&ImageSelectionContains(point);
            if(movingImageSelection)
            {
                if(e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape){FinishImageSelectionMove(false);e.Use();return true;}
                if(e.type==EventType.Ignore||GUIUtility.hotControl!=selectionMoveControl){FinishImageSelectionMove(false);return true;}
                if(e.type==EventType.MouseDrag||e.rawType==EventType.MouseUp)
                {
                    Vector2 delta=point-selectionMoveStart;float step=(e.shift?Mathf.Max(1,marqueeSnapPixels):1)/(float)CanvasSize;
                    delta=new Vector2(Mathf.Round(delta.x/step)*step,Mathf.Round(delta.y/step)*step);delta=ClampSelectionDelta(selectionMoveBounds,delta);
                    imageSelection=new Rect(selectionMoveBounds.position+delta,selectionMoveBounds.size);selectionMaskOffset=selectionMoveMaskStart+delta;
                    RequestImageAdjustmentPreview();Repaint();
                    if(e.rawType==EventType.MouseUp)FinishImageSelectionMove(true);e.Use();return true;
                }
                return true;
            }
            if(selectionMoveHover&&e.type==EventType.MouseDown&&e.button==0&&!e.alt&&GUIUtility.hotControl==0)
            {
                selectionMoveCheckpoint=CaptureImage();selectionMoveStart=point;selectionMoveBounds=imageSelection;selectionMoveMaskStart=selectionMaskOffset;
                movingImageSelection=true;selectionMoveControl=id;GUIUtility.hotControl=id;GUIUtility.keyboardControl=0;e.Use();Repaint();return true;
            }
            return false;
        }
        private void FinishImageSelectionMove(bool commit)
        {
            if(!movingImageSelection)return;
            if(GUIUtility.hotControl==selectionMoveControl)GUIUtility.hotControl=0;
            movingImageSelection=false;
            bool changed=imageSelection.position!=selectionMoveBounds.position;
            if(commit&&changed){inputSnapshot=selectionMoveCheckpoint;MarkCompositeDirty();}
            else{imageSelection=selectionMoveBounds;selectionMaskOffset=selectionMoveMaskStart;RequestImageAdjustmentPreview();}
            selectionMoveCheckpoint=null;inputSnapshot=null;Repaint();
        }
    }
}
