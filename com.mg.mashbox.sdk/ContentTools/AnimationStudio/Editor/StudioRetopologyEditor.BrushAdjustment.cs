using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private bool adjustingRetopoBrush;
        private int brushAdjustmentControl,brushAdjustmentAxis;
        private Vector2 brushAdjustmentAnchor,brushAdjustmentPointer;
        private float brushInitialRadius,brushInitialStrength;
        private RetopoTool brushAdjustmentTool;
        private float RetopoBrushRadius
        {
            get=>brushAdjustmentTool==RetopoTool.RelaxBrush?relaxRadius:resolutionRadius;
            set{if(brushAdjustmentTool==RetopoTool.RelaxBrush)relaxRadius=value;else resolutionRadius=value;}
        }
        private void EndBrushAdjustment(bool cancel=false)
        {
            if(!adjustingRetopoBrush)return;
            if(cancel){RetopoBrushRadius=brushInitialRadius;if(brushAdjustmentTool==RetopoTool.RelaxBrush)relaxStrength=brushInitialStrength;}
            if(GUIUtility.hotControl==brushAdjustmentControl)GUIUtility.hotControl=0;
            adjustingRetopoBrush=false;repaint();
        }
        private bool HandleBrushAdjustment(SceneView canvas)
        {
            var e=Event.current;int id=GUIUtility.GetControlID("Retopology brush adjustment".GetHashCode(),FocusType.Passive);
            if(adjustingRetopoBrush)
            {
                if(GUIUtility.hotControl!=brushAdjustmentControl||e.type==EventType.Ignore||e.type==EventType.MouseLeaveWindow||activeTool!=brushAdjustmentTool)
                {EndBrushAdjustment();return true;}
                if(e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape){EndBrushAdjustment(true);e.Use();return true;}
                if(e.rawType==EventType.MouseUp&&e.button==2){EndBrushAdjustment();e.Use();return true;}
                if(e.type==EventType.MouseDrag&&e.button==2)
                {
                    var delta=e.mousePosition-brushAdjustmentAnchor;
                    if(brushAdjustmentAxis==0&&Mathf.Max(Mathf.Abs(delta.x),Mathf.Abs(delta.y))>=4)
                        brushAdjustmentAxis=brushAdjustmentTool==RetopoTool.Resolution||Mathf.Abs(delta.x)>=Mathf.Abs(delta.y)?1:2;
                    if(brushAdjustmentAxis==1)
                    {RetopoBrushRadius=Mathf.Clamp(brushInitialRadius*Mathf.Exp(delta.x*.01f),12,200);brushAdjustmentPointer=brushAdjustmentAnchor+new Vector2(delta.x,0);}
                    else if(brushAdjustmentAxis==2)
                    {relaxStrength=Mathf.Clamp01(brushInitialStrength-delta.y*.005f);brushAdjustmentPointer=brushAdjustmentAnchor+new Vector2(0,delta.y);}
                    e.Use();repaint();return true;
                }
                if(e.isMouse||e.isKey||e.type==EventType.ScrollWheel)e.Use();
                return true;
            }
            if(activeTool!=RetopoTool.RelaxBrush&&activeTool!=RetopoTool.Resolution)return false;
            if(e.type!=EventType.MouseDown||e.button!=2||!e.control||e.alt||GUIUtility.hotControl!=0||relaxStroke||resolutionStroke||contextHeld)return false;
            if(canvas is MashBoxStudioViewport viewport&&(!viewport.ModelCanvasRect.Contains(e.mousePosition)||viewport.IsOverViewportControls(e.mousePosition)||viewport.IsOverSkinningPanels(e.mousePosition)))return false;
            brushAdjustmentTool=activeTool;brushInitialRadius=RetopoBrushRadius;brushInitialStrength=relaxStrength;
            brushAdjustmentAnchor=brushAdjustmentPointer=e.mousePosition;brushAdjustmentAxis=0;
            adjustingRetopoBrush=true;brushAdjustmentControl=id;GUIUtility.hotControl=id;GUIUtility.keyboardControl=0;EditorGUIUtility.editingTextField=false;
            e.Use();repaint();return true;
        }
        private void DrawBrushAdjustment()
        {
            if(!adjustingRetopoBrush||Event.current.type!=EventType.Repaint)return;
            Handles.BeginGUI();var color=Handles.color;
            try
            {
                Handles.color=new Color(.5f,1,.87f);
                Handles.DrawWireDisc(brushAdjustmentAnchor,Vector3.forward,RetopoBrushRadius);
                Handles.DrawAAPolyLine(2,brushAdjustmentAnchor,brushAdjustmentPointer);
                Handles.DrawSolidDisc(brushAdjustmentAnchor,Vector3.forward,3);
                Handles.DrawSolidDisc(brushAdjustmentPointer,Vector3.forward,3);
                string text=brushAdjustmentAxis==1?$"Radius {RetopoBrushRadius:F0} px":brushAdjustmentAxis==2?$"Strength {relaxStrength:P0}":
                    brushAdjustmentTool==RetopoTool.Resolution?"Drag sideways: radius":"Sideways: radius | Up/down: strength";
                GUI.Box(new Rect(Mathf.Clamp(brushAdjustmentAnchor.x+18,0,Mathf.Max(0,view.position.width-270)),Mathf.Clamp(brushAdjustmentAnchor.y+18,40,Mathf.Max(40,view.position.height-36)),260,28),text);
            }
            finally{Handles.color=color;Handles.EndGUI();}
        }
    }
}
