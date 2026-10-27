using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioImagingWindow
    {
        private StickerLayer inlineTextLayer;
        private string inlineTextCheckpoint,inlineTextOriginal;
        private bool inlineTextFocus,inlineTextWasUnsaved,inlineTextWasPending;
        private Rect inlineTextScreenBounds;
        private double inlineTextNextRepaint;
        private const string InlineImageTextControl="MashBoxInlineImageText";

        private void BeginInlineImageText(int index)
        {
            if(index<0||index>=layers.Count||!layers[index].isText||layers[index].locked)return;
            FinishInlineImageText(true);CancelImageAdjustment();EndImageTransform();draggingLayer=false;
            GUIUtility.hotControl=0;selectedLayer=index;imageTool=ImageTool.Text;
            inlineTextLayer=layers[index];inlineTextCheckpoint=CaptureImage();inlineTextOriginal=inlineTextLayer.textContent;
            inlineTextWasUnsaved=hasUnsavedChanges;inlineTextWasPending=pendingExport;
            inlineTextFocus=true;focusImageText=false;imageToolSettingsExpanded=true;Repaint();
        }
        private bool HandleInlineImageTextActivation(Rect rect)
        {
            if(inlineTextLayer!=null)return false;var e=Event.current;
            if(e.type!=EventType.MouseDown||e.button!=0||e.clickCount<2||e.alt||!rect.Contains(e.mousePosition))return false;
            int hit=HitTestLayers(GuiToCanvas(e.mousePosition,rect));
            if(hit<0||!layers[hit].isText||layers[hit].locked)return false;
            BeginInlineImageText(hit);e.Use();return true;
        }
        private bool HandleInlineImageTextInput()
        {
            if(inlineTextLayer==null)return false;var e=Event.current;
            if(e.type==EventType.KeyDown)
            {
                if(e.keyCode==KeyCode.Escape){FinishInlineImageText(false);e.Use();return true;}
                if((e.control||e.command)&&(e.keyCode==KeyCode.Return||e.keyCode==KeyCode.KeypadEnter))
                {FinishInlineImageText(true);e.Use();return true;}
                if((e.control||e.command)&&e.keyCode==KeyCode.T)
                {FinishInlineImageText(true);imageTool=ImageTool.Transform;e.Use();return true;}
                if((e.control||e.command)&&e.keyCode==KeyCode.S)
                {FinishInlineImageText(true);SaveImageDocument();e.Use();return true;}
            }
            if(e.type==EventType.MouseDown&&!inlineTextFocus&&!inlineTextScreenBounds.Contains(GUIUtility.GUIToScreenPoint(e.mousePosition)))
            {FinishInlineImageText(true);return false;}
            EditorGUIUtility.editingTextField=true;
            return true;
        }
        private void SetInlineImageText(string text)
        {
            if(inlineTextLayer==null)return;
            if(text.Length>4096)text=text.Substring(0,4096);
            if(text==inlineTextLayer.textContent)return;
            string previous=inlineTextLayer.textContent;inlineTextLayer.textContent=text;inputSnapshot=null;
            try{RefreshImageText(inlineTextLayer);}
            catch(System.Exception error){inlineTextLayer.textContent=previous;status=error.Message;statusType=MessageType.Warning;}
            Repaint();
        }
        private void FinishInlineImageText(bool commit)
        {
            if(inlineTextLayer==null)return;
            bool changed=inlineTextLayer.textContent!=inlineTextOriginal;
            string checkpoint=inlineTextCheckpoint;
            bool unsaved=inlineTextWasUnsaved,pending=inlineTextWasPending;
            inlineTextLayer=null;inlineTextCheckpoint=null;inlineTextOriginal=null;inlineTextFocus=false;
            GUIUtility.keyboardControl=0;EditorGUIUtility.editingTextField=false;inputSnapshot=null;
            if(commit&&changed){inputSnapshot=checkpoint;MarkCompositeDirty();inputSnapshot=null;}
            else if(checkpoint!=null){RestoreImage(checkpoint);hasUnsavedChanges=unsaved;pendingExport=pending;}
            Repaint();
        }
        private void DrawInlineImageText(Rect canvas)
        {
            var layer=inlineTextLayer;if(layer==null)return;
            if(!layers.Contains(layer)||selectedLayer<0||layers[selectedLayer]!=layer||layer.locked)
            {FinishInlineImageText(true);return;}
            var points=ImageTransformPoints(canvas,layer);var size=new Vector2(layer.texture.width,layer.texture.height);
            Vector2 x=(points[1]-points[0])/size.x,y=(points[3]-points[0])/size.y;
            var transform=Matrix4x4.identity;transform.m00=x.x;transform.m10=x.y;transform.m01=y.x;transform.m11=y.y;transform.m03=points[0].x;transform.m13=points[0].y;
            var field=new Rect(0,0,Mathf.Max(16,size.x),Mathf.Max(size.y,layer.textSize*1.4f));
            Vector2 screenMin=Vector2.one*float.MaxValue,screenMax=Vector2.one*float.MinValue;
            foreach(var p in new[]{field.min,new Vector2(field.xMax,field.y),field.max,new Vector2(field.x,field.yMax)})
            {Vector2 screen=GUIUtility.GUIToScreenPoint(transform.MultiplyPoint3x4(p));screenMin=Vector2.Min(screenMin,screen);screenMax=Vector2.Max(screenMax,screen);}
            inlineTextScreenBounds=Rect.MinMaxRect(screenMin.x-6,screenMin.y-6,screenMax.x+6,screenMax.y+6);
            var style=new GUIStyle(EditorStyles.textArea)
            {
                font=ResolveImageTextFont(layer.textFont,layer.textSize),fontSize=layer.textSize,fontStyle=layer.textStyle,
                alignment=layer.textAlignment==1?TextAnchor.UpperCenter:layer.textAlignment==2?TextAnchor.UpperRight:TextAnchor.UpperLeft,
                wordWrap=false,padding=new RectOffset(4,4,0,0),border=new RectOffset(),margin=new RectOffset(),clipping=TextClipping.Overflow
            };
            // The compositor supplies the actual glyphs, including color and blending.
            // This transparent native text editor supplies the caret and text selection.
            foreach(var state in new[]{style.normal,style.hover,style.active,style.focused,style.onNormal,style.onHover,style.onActive,style.onFocused})
            {state.background=null;state.textColor=Color.clear;}
            var matrix=GUI.matrix;Color cursor=GUI.skin.settings.cursorColor,selection=GUI.skin.settings.selectionColor;
            string content=layer.textContent??"";
            try
            {
                GUI.matrix=matrix*transform;GUI.skin.settings.cursorColor=Color.white;GUI.skin.settings.selectionColor=new Color(.2f,.5f,1,.3f);
                GUI.SetNextControlName(InlineImageTextControl);content=GUI.TextArea(field,content,4096,style);
                if(inlineTextFocus&&Event.current.type==EventType.Repaint)
                {
                    GUI.FocusControl(InlineImageTextControl);EditorGUIUtility.editingTextField=true;
                    var editor=GUIUtility.GetStateObject(typeof(TextEditor),GUIUtility.keyboardControl) as TextEditor;
                    if(editor!=null){editor.text=content;editor.SelectAll();}inlineTextFocus=false;
                }
            }
            finally{GUI.matrix=matrix;GUI.skin.settings.cursorColor=cursor;GUI.skin.settings.selectionColor=selection;}
            SetInlineImageText(content);
        }
        private void RepaintInlineImageText()
        {if(inlineTextLayer!=null&&EditorApplication.timeSinceStartup>=inlineTextNextRepaint){inlineTextNextRepaint=EditorApplication.timeSinceStartup+.05;Repaint();}}
    }
}
