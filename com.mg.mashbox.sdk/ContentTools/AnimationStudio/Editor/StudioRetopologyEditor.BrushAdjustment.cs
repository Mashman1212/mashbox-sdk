using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private bool adjustingRetopoBrush;
        private int brushAdjustmentControl,brushAdjustmentAxis;
        private Vector2 brushAdjustmentAnchor,brushAdjustmentPointer;
        private float brushInitialRadius,brushInitialStrength,stripAdjustmentPixelsPerUnit;
        private RetopoTool brushAdjustmentTool;
        private float RetopoBrushRadius
        {
            get=>brushAdjustmentTool==RetopoTool.Strip?stripWidth:brushAdjustmentTool==RetopoTool.RelaxBrush?relaxRadius:resolutionRadius;
            set{if(brushAdjustmentTool==RetopoTool.Strip)stripWidth=value;else if(brushAdjustmentTool==RetopoTool.RelaxBrush)relaxRadius=value;else resolutionRadius=value;}
        }
        private void EndBrushAdjustment(bool cancel=false)
        {
            if(!adjustingRetopoBrush)return;
            if(cancel){RetopoBrushRadius=brushInitialRadius;if(brushAdjustmentTool==RetopoTool.RelaxBrush)relaxStrength=brushInitialStrength;}
            else
            {
                if(!Mathf.Approximately(RetopoBrushRadius,brushInitialRadius))RecordTutorialAction("brushRadius");
                if(brushAdjustmentTool==RetopoTool.RelaxBrush&&!Mathf.Approximately(relaxStrength,brushInitialStrength))RecordTutorialAction("brushStrength");
            }
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
                        brushAdjustmentAxis=brushAdjustmentTool!=RetopoTool.RelaxBrush||Mathf.Abs(delta.x)>=Mathf.Abs(delta.y)?1:2;
                    if(brushAdjustmentAxis==1)
                    {float value=brushInitialRadius*Mathf.Exp(Mathf.Clamp(delta.x*.01f,-10,10));RetopoBrushRadius=brushAdjustmentTool==RetopoTool.Strip?Mathf.Max(.001f,value):Mathf.Clamp(value,12,200);brushAdjustmentPointer=brushAdjustmentAnchor+new Vector2(delta.x,0);}
                    else if(brushAdjustmentAxis==2)
                    {relaxStrength=Mathf.Clamp01(brushInitialStrength-delta.y*.005f);brushAdjustmentPointer=brushAdjustmentAnchor+new Vector2(0,delta.y);}
                    e.Use();repaint();return true;
                }
                if(e.isMouse||e.isKey||e.type==EventType.ScrollWheel)e.Use();
                return true;
            }
            if(activeTool!=RetopoTool.RelaxBrush&&activeTool!=RetopoTool.Resolution&&activeTool!=RetopoTool.Strip)return false;
            if(e.type!=EventType.MouseDown||e.button!=2||!e.control||e.alt||GUIUtility.hotControl!=0||relaxStroke||resolutionStroke||stripStroke||contextHeld)return false;
            if(canvas is MashBoxStudioViewport viewport&&(!viewport.ModelCanvasRect.Contains(e.mousePosition)||viewport.IsOverViewportControls(e.mousePosition)||viewport.IsOverSkinningPanels(e.mousePosition)))return false;
            brushAdjustmentTool=activeTool;brushInitialRadius=RetopoBrushRadius;brushInitialStrength=relaxStrength;
            stripAdjustmentPixelsPerUnit=0;
            if(activeTool==RetopoTool.Strip&&ReferenceHit(e.mousePosition,out var hit))
                stripAdjustmentPixelsPerUnit=Vector2.Distance(HandleUtility.WorldToGUIPoint(hit.point),HandleUtility.WorldToGUIPoint(hit.point+canvas.camera.transform.right*stripWidth))/Mathf.Max(.001f,stripWidth);
            if(activeTool==RetopoTool.Strip&&stripAdjustmentPixelsPerUnit<=0)
                stripAdjustmentPixelsPerUnit=StudioBrushCursor.PixelRadius(canvas,1,stripCursorDepth);
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
                bool valid=brushAdjustmentTool==RetopoTool.Strip?ReferenceHit(brushAdjustmentAnchor,out _):Active!=null&&Active.showOutput&&PickFace(brushAdjustmentAnchor)>=0;
                Handles.color=valid?new Color(.5f,1,.87f):StudioBrushCursor.Inactive;
                float previewRadius=brushAdjustmentTool==RetopoTool.Strip?stripWidth*stripAdjustmentPixelsPerUnit*.5f:RetopoBrushRadius;
                if(previewRadius>0)Handles.DrawWireDisc(brushAdjustmentAnchor,Vector3.forward,previewRadius);
                Handles.DrawAAPolyLine(2,brushAdjustmentAnchor,brushAdjustmentPointer);
                Handles.DrawSolidDisc(brushAdjustmentAnchor,Vector3.forward,3);
                Handles.DrawSolidDisc(brushAdjustmentPointer,Vector3.forward,3);
                string text=brushAdjustmentTool==RetopoTool.Strip?$"Width {stripWidth:F3} · drag sideways":brushAdjustmentAxis==1?$"Radius {RetopoBrushRadius:F0} px":brushAdjustmentAxis==2?$"Strength {relaxStrength:P0}":
                    brushAdjustmentTool==RetopoTool.Resolution?"Drag sideways: radius":"Sideways: radius | Up/down: strength";
                GUI.Box(new Rect(Mathf.Clamp(brushAdjustmentAnchor.x+18,0,Mathf.Max(0,view.position.width-270)),Mathf.Clamp(brushAdjustmentAnchor.y+18,40,Mathf.Max(40,view.position.height-36)),260,28),text);
            }
            finally{Handles.color=color;Handles.EndGUI();}
        }
    }
}
