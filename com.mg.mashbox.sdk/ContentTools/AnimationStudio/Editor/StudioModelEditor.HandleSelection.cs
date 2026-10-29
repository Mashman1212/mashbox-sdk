using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private bool handleComponentClickCandidate,handleComponentClickMoved;
        private int handleComponentClickControl;
        private Vector2 handleComponentClickStart;
        private bool handleComponentClickAdd,handleComponentClickSubtract;
        private void BeginHandleComponentClick(SceneView view,EventType type,Vector2 pointer)
        {
            if(type==EventType.MouseDown)
            {
                handleComponentClickCandidate=false;handleComponentClickControl=0;handleComponentClickMoved=false;
                var evt=Event.current;
                if(evt.button!=0||evt.alt||pivot||pivotSnapHeld||mode==ComponentMode.Object||GUIUtility.hotControl!=0)return;
                var canvas=view is MashBoxStudioViewport studio?studio.ModelCanvasRect:new Rect(Vector2.zero,view.position.size);
                if(!canvas.Contains(pointer)||view is MashBoxStudioViewport overlay&&overlay.IsOverViewportControls(pointer))return;
                if(FindSelectedComponentTarget(view,pointer,out _) < 0)return;
                handleComponentClickCandidate=true;handleComponentClickStart=pointer;
                handleComponentClickAdd=evt.shift;handleComponentClickSubtract=evt.control||evt.command;
            }
            else if(type==EventType.MouseDrag&&handleComponentClickCandidate)handleComponentClickMoved=true;
            else if(type==EventType.MouseLeaveWindow||type==EventType.Ignore)
            {handleComponentClickCandidate=false;handleComponentClickControl=0;}
        }
        private void FinishHandleComponentClick(SceneView view,int selectionControl,EventType type,Vector2 pointer)
        {
            if(!handleComponentClickCandidate)return;
            if(type==EventType.MouseDown)
            {
                int control=GUIUtility.hotControl;
                // Unconsumed clicks continue through the normal selection path.
                if(control==0||control==selectionControl){handleComponentClickCandidate=false;return;}
                handleComponentClickControl=control;return;
            }
            if(type!=EventType.MouseUp)return;
            bool click=handleComponentClickControl!=0&&!handleComponentClickMoved&&
                (pointer-handleComponentClickStart).sqrMagnitude<16&&GUIUtility.hotControl==0;
            handleComponentClickCandidate=false;handleComponentClickControl=0;
            if(!click)return;
            end=handleComponentClickStart;additive=handleComponentClickAdd;subtract=handleComponentClickSubtract;selectionClickCount=1;
            Run(()=>Pick(view,new Rect(end,Vector2.zero),true));Event.current.Use();repaint();
        }
    }
}
