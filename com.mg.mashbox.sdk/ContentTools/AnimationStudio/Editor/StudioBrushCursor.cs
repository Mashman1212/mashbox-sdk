using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioBrushCursor
    {
        internal static readonly Color Inactive = new Color(.65f,.69f,.73f,.85f);
        internal static bool InViewport(SceneView view,Vector2 mouse)
        {
            return !(view is MashBoxStudioViewport studio) ||
                (studio.ModelCanvasRect.Contains(mouse)&&!studio.IsOverSkinningPanels(mouse)&&!studio.IsOverViewportControls(mouse));
        }
        internal static float PixelRadius(SceneView view,float radius,float depth)
        {
            var camera=view.camera;
            if(depth<=0)depth=Vector3.Dot(view.pivot-camera.transform.position,camera.transform.forward);
            depth=Mathf.Max(camera.nearClipPlane*2,depth);
            var center=camera.transform.position+camera.transform.forward*depth;
            return Vector2.Distance(HandleUtility.WorldToGUIPoint(center),HandleUtility.WorldToGUIPoint(center+camera.transform.right*radius));
        }
        internal static void Draw(SceneView view,Vector2 mouse,float radius,bool valid,Vector3 point,Vector3 normal,Color color,ref float depth)
        {
            if(Event.current.type!=EventType.Repaint||!InViewport(view,mouse))return;
            var previous=Handles.color;
            try
            {
                if(valid)
                {
                    depth=Vector3.Dot(point-view.camera.transform.position,view.camera.transform.forward);
                    Handles.color=color;Handles.DrawWireDisc(point,normal,radius);
                }
                else
                {
                    Handles.BeginGUI();
                    try{Handles.color=Inactive;Handles.DrawWireDisc(mouse,Vector3.forward,PixelRadius(view,radius,depth));}
                    finally{Handles.EndGUI();}
                }
            }
            finally{Handles.color=previous;}
        }
    }
}
