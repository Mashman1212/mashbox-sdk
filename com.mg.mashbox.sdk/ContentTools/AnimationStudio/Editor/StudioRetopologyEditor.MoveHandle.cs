using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        // As in the modeler, component movement has no dependency on Unity's
        // scene-wide grid/increment snap settings. Surface projection is applied
        // after the handle has produced its continuous, unprojected position.
        private static Vector3 DrawRetopologyMoveHandle(Vector3 center, Quaternion orientation)
        {
            float size=HandleUtility.GetHandleSize(center);
            var result=center;
            using(new Handles.DrawingScope())
            {
                for(int axis=0;axis<3;axis++)
                {
                    int id=GUIUtility.GetControlID(0x525400+axis,FocusType.Passive);
                    Handles.color=axis==0?Handles.xAxisColor:axis==1?Handles.yAxisColor:Handles.zAxisColor;
                    var direction=orientation*(axis==0?Vector3.right:axis==1?Vector3.up:Vector3.forward);
                    var next=Handles.Slider(id,center,direction,size,Handles.ArrowHandleCap,0);
                    if(GUIUtility.hotControl==id)result=next;
                }
                for(int plane=0;plane<3;plane++)
                {
                    int a=plane==2?1:0,b=plane==0?1:2;
                    var first=orientation*(a==0?Vector3.right:Vector3.up);
                    var second=orientation*(b==1?Vector3.up:Vector3.forward);
                    var offset=(first+second)*size*.23f;
                    int id=GUIUtility.GetControlID(0x525410+plane,FocusType.Passive);
                    Handles.color=plane==0?Handles.zAxisColor:plane==1?Handles.yAxisColor:Handles.xAxisColor;
                    var next=Handles.Slider2D(id,center+offset,Vector3.Cross(first,second),first,second,size*.065f,Handles.RectangleHandleCap,Vector2.zero);
                    if(GUIUtility.hotControl==id)result=next-offset;
                }
                int free=GUIUtility.GetControlID(0x525420,FocusType.Passive);
                Handles.color=Color.yellow;
                var freePosition=Handles.FreeMoveHandle(free,center,size*.075f,Vector3.zero,Handles.RectangleHandleCap);
                if(GUIUtility.hotControl==free)result=freePosition;
            }
            return result;
        }
    }
}