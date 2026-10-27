using System;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioImagingWindow
    {
        [SerializeField] private bool hasImageSelection;
        [SerializeField] private Rect imageSelection;
        private bool drawingImageSelection;
        private Vector2 imageSelectionStart;
        private int imageSelectionControl;
        private bool previousHasSelection;
        private Rect previousSelection;
        [SerializeField] private int marqueeSnapPixels=8;
        private bool marqueeSnapHeld;
        private Vector2 marqueePointer;
        private void HandleMarqueeSnapKey()
        {
            var e=Event.current;
            if(imageTool!=ImageTool.Marquee || selectionTool!=ImageSelectionTool.Rectangle || EditorGUIUtility.editingTextField)
            {marqueeSnapHeld=false;return;}
            bool held=marqueeSnapHeld;
            if((e.keyCode==KeyCode.LeftShift || e.keyCode==KeyCode.RightShift) && (e.type==EventType.KeyDown || e.type==EventType.KeyUp))held=e.type==EventType.KeyDown;
            else if(e.isMouse)held=e.shift;
            if(held==marqueeSnapHeld)return;
            marqueeSnapHeld=held;if(drawingImageSelection)UpdateMarqueeRectangle();Repaint();
        }
        private void UpdateMarqueeRectangle()
        {
            Vector2 p=marqueePointer;
            if(marqueeSnapHeld)
            {
                float step=Mathf.Max(1,marqueeSnapPixels)/(float)CanvasSize;
                var delta=p-imageSelectionStart;
                p=imageSelectionStart+new Vector2(Mathf.Round(delta.x/step)*step,Mathf.Round(delta.y/step)*step);
                p=new Vector2(Mathf.Clamp01(p.x),Mathf.Clamp01(p.y));
            }
            imageSelection=Rect.MinMaxRect(Mathf.Min(p.x,imageSelectionStart.x),Mathf.Min(p.y,imageSelectionStart.y),Mathf.Max(p.x,imageSelectionStart.x),Mathf.Max(p.y,imageSelectionStart.y));
            hasImageSelection=imageSelection.width*CanvasSize>=1 && imageSelection.height*CanvasSize>=1;
        }
        private enum ImageAdjustment { None, HueSaturation, Levels, ShadowsHighlights }
        private ImageAdjustment imageAdjustment;
        private float adjustmentHue, adjustmentSaturation, adjustmentLightness;
        private float levelsBlack, levelsWhite=1, levelsGamma=1, levelsOutputBlack, levelsOutputWhite=1;
        private float adjustmentShadows, adjustmentHighlights;
        private bool CanAdjustImage => HasSelectedLayer && !layers[selectedLayer].isText && !layers[selectedLayer].locked && layers[selectedLayer].texture;

        private Vector2 SelectionPoint(Vector2 mouse,Rect rect)
        {
            Vector2 p=GuiToCanvas(mouse,rect)/CanvasSize;
            return new Vector2(Mathf.Clamp01(p.x),Mathf.Clamp01(p.y));
        }
        private bool HandleImageMarquee(Rect rect)
        {
            if(HandleImageSelectionMove(rect))return true;
            if(HandleImageColorRange(rect)||HandleImageLasso(rect))return true;
            var e=Event.current;
            int id=GUIUtility.GetControlID("Image marquee".GetHashCode(),FocusType.Passive);
            var viewport=new Rect(0,0,canvasArea.width,canvasArea.height);

            if(drawingImageSelection)
            {
                if(e.type==EventType.KeyDown && e.keyCode==KeyCode.Escape)
                {hasImageSelection=previousHasSelection;imageSelection=previousSelection;EndImageMarquee();e.Use();return true;}
                if(GUIUtility.hotControl!=imageSelectionControl || e.type==EventType.Ignore){EndImageMarquee();return true;}
                if(e.type==EventType.MouseDrag || e.rawType==EventType.MouseUp)
                {
                    DragMarqueeRectangle(GuiToCanvas(e.mousePosition,rect)/CanvasSize);
                    if(e.rawType==EventType.MouseUp){selectionMask=null;selectionMaskSize=0;selectionMaskOffset=Vector2.zero;selectionOutlineDirty=true;inputSnapshot=selectionBeforeTrace;MarkCompositeDirty();selectionBeforeTrace=null;EndImageMarquee();}
                    e.Use();Repaint();return true;
                }
                return true;
            }
            if(imageTool!=ImageTool.Marquee)return false;
            // The empty workspace is a valid starting point: selecting up to an image
            // edge should not require landing the first click exactly inside it.
            if(e.type==EventType.MouseDown && e.button==0 && !e.alt && viewport.Contains(e.mousePosition) && GUIUtility.hotControl==0)
            {
                selectionBeforeTrace=CaptureImage();
                previousHasSelection=hasImageSelection;previousSelection=imageSelection;
                imageSelectionStart=SelectionPoint(e.mousePosition,rect);imageSelection=new Rect(imageSelectionStart,Vector2.zero);hasImageSelection=false;
                marqueePointer=imageSelectionStart;marqueePointerOffset=Vector2.zero;marqueeLastRaw=GuiToCanvas(e.mousePosition,rect)/CanvasSize;
                drawingImageSelection=true;imageSelectionControl=id;GUIUtility.hotControl=id;GUIUtility.keyboardControl=0;e.Use();Repaint();
            }
            return true;
        }
        private void EndImageMarquee()
        {
            FinishImageSelectionMove(false);
            if(drawingImageSelection && GUIUtility.hotControl==imageSelectionControl)GUIUtility.hotControl=0;
            drawingImageSelection=false;inputSnapshot=null;Repaint();
        }
        private void DrawImageMarquee(Rect rect)
        {
            DrawImageSelectionPath(rect);
            if(Event.current.type!=EventType.Repaint || (!hasImageSelection && !drawingImageSelection) || (selectionMask!=null&&!drawingImageSelection))return;
            if(drawingImageSelection)
            {
                string size=$"{Mathf.RoundToInt(imageSelection.width*CanvasSize)} × {Mathf.RoundToInt(imageSelection.height*CanvasSize)} px";
                string snap=marqueeSnapHeld?$"Snap: {marqueeSnapPixels} px":"Shift: snap · Space: move";
                GUI.Label(new Rect(10,Mathf.Max(0,canvasArea.height-28),300,22),size+"  ·  "+snap,EditorStyles.helpBox);
            }
            Vector2 min=CanvasToGui(imageSelection.min*CanvasSize,rect),max=CanvasToGui(imageSelection.max*CanvasSize,rect);
            float dpi=EditorGUIUtility.pixelsPerPoint;
            min=new Vector2(Mathf.Round(min.x*dpi)/dpi,Mathf.Round(min.y*dpi)/dpi);
            max=new Vector2(Mathf.Max(min.x+1,Mathf.Round(max.x*dpi)/dpi),Mathf.Max(min.y+1,Mathf.Round(max.y*dpi)/dpi));
            // Draw in GUI space so neither camera state nor continuous repainting is required.
            EditorGUI.DrawRect(new Rect(min.x,min.y,max.x-min.x,1),Color.black);
            EditorGUI.DrawRect(new Rect(min.x,max.y,max.x-min.x,1),Color.black);
            EditorGUI.DrawRect(new Rect(min.x,min.y,1,max.y-min.y),Color.black);
            EditorGUI.DrawRect(new Rect(max.x,min.y,1,max.y-min.y),Color.black);
            for(float x=Mathf.Max(min.x,-8);x<Mathf.Min(max.x,canvasArea.width);x+=8)
            {float w=Mathf.Min(4,max.x-x);EditorGUI.DrawRect(new Rect(x,min.y,w,1),Color.white);EditorGUI.DrawRect(new Rect(x,max.y,w,1),Color.white);}
            for(float y=Mathf.Max(min.y,-8);y<Mathf.Min(max.y,canvasArea.height);y+=8)
            {float h=Mathf.Min(4,max.y-y);EditorGUI.DrawRect(new Rect(min.x,y,1,h),Color.white);EditorGUI.DrawRect(new Rect(max.x,y,1,h),Color.white);}
        }
        private bool ImagePixelEditable(StickerLayer layer,int x,int y)
        {
            var texture=layer.texture;var crop=ClampSourceRect(layer.sourceRect);
            Vector2 uv=new Vector2((x+.5f)/texture.width,1-(y+.5f)/texture.height);
            if(!crop.Contains(uv))return false;
            if(!hasImageSelection)return true;
            return ImageSelectionContains(ImagePixelCanvasPosition(layer,x,y)/CanvasSize);
        }
        private Vector2 ImagePixelCanvasPosition(StickerLayer layer,int x,int y)
        {
            var texture=layer.texture;var crop=ClampSourceRect(layer.sourceRect);
            var uv=new Vector2(((x+.5f)/texture.width-crop.x)/crop.width,((texture.height-y-.5f)/texture.height-crop.y)/crop.height);
            return ImageUVToCanvas(layer,uv);
        }
        private void BeginImageAdjustment(ImageAdjustment adjustment)
        {
            if(!CanAdjustImage)return;
            CancelImageAdjustment();CancelImageColorRange();CancelSelectionTrace();EndImageMarquee();
            imageAdjustment=adjustment;adjustmentHue=adjustmentSaturation=adjustmentLightness=0;
            levelsBlack=levelsOutputBlack=0;levelsWhite=levelsOutputWhite=levelsGamma=1;
            adjustmentShadows=adjustmentHighlights=0;inspectorScroll=Vector2.zero;StartImageAdjustmentPreview();Repaint();
        }
        private void DrawImageAdjustment()
        {
            if(imageAdjustment==ImageAdjustment.None)return;
            EditorGUILayout.LabelField(imageAdjustment==ImageAdjustment.HueSaturation?"HUE / SATURATION":imageAdjustment==ImageAdjustment.Levels?"LEVELS":"SHADOWS / HIGHLIGHTS",StudioTheme.Styles.panelHeader);
            EditorGUILayout.LabelField(hasImageSelection?"Applies inside the selection on the selected layer.":"Applies to the selected layer. Make a selection to limit the area.",StudioTheme.Styles.wordWrappedMiniLabel);
            EditorGUI.BeginChangeCheck();
            if(imageAdjustment==ImageAdjustment.HueSaturation)
            {
                adjustmentHue=EditorGUILayout.Slider("Hue",adjustmentHue,-180,180);
                adjustmentSaturation=EditorGUILayout.Slider("Saturation",adjustmentSaturation,-100,100);
                adjustmentLightness=EditorGUILayout.Slider("Lightness",adjustmentLightness,-100,100);
            }
            else if(imageAdjustment==ImageAdjustment.Levels)
            {
                levelsBlack=EditorGUILayout.Slider("Input black",levelsBlack,0,levelsWhite-.001f);
                levelsWhite=EditorGUILayout.Slider("Input white",levelsWhite,levelsBlack+.001f,1);
                levelsGamma=EditorGUILayout.Slider("Gamma",levelsGamma,.1f,10);
                levelsOutputBlack=EditorGUILayout.Slider("Output black",levelsOutputBlack,0,levelsOutputWhite);
                levelsOutputWhite=EditorGUILayout.Slider("Output white",levelsOutputWhite,levelsOutputBlack,1);
            }
            else
            {
                adjustmentShadows=EditorGUILayout.Slider("Lift shadows",adjustmentShadows,0,100);
                adjustmentHighlights=EditorGUILayout.Slider("Reduce highlights",adjustmentHighlights,0,100);
            }
            if(EditorGUI.EndChangeCheck())RequestImageAdjustmentPreview();
            EditorGUILayout.LabelField("Live preview — Apply keeps the result; Cancel restores the original.",StudioTheme.Styles.wordWrappedMiniLabel);
            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUI.DisabledScope(!CanAdjustImage))if(GUILayout.Button("Apply"))ApplyImageAdjustment();
                if(GUILayout.Button("Cancel"))CancelImageAdjustment();
            }
            EditorGUILayout.Space(8);
        }
        private Color AdjustImagePixel(Color source)
        {
            Color c=source;
            if(imageAdjustment==ImageAdjustment.HueSaturation)
            {
                Color.RGBToHSV(c,out float h,out float s,out float v);
                s=adjustmentSaturation<0?s*(1+adjustmentSaturation/100):Mathf.Lerp(s,1,adjustmentSaturation/100);
                c=Color.HSVToRGB(Mathf.Repeat(h+adjustmentHue/360,1),Mathf.Clamp01(s),v);
                c=Color.Lerp(c,adjustmentLightness<0?Color.black:Color.white,Mathf.Abs(adjustmentLightness)/100);
            }
            else if(imageAdjustment==ImageAdjustment.Levels)
            {
                float Level(float value)=>Mathf.Lerp(levelsOutputBlack,levelsOutputWhite,Mathf.Pow(Mathf.Clamp01((value-levelsBlack)/Mathf.Max(.001f,levelsWhite-levelsBlack)),1/levelsGamma));
                c.r=Level(c.r);c.g=Level(c.g);c.b=Level(c.b);
            }
            else if(imageAdjustment==ImageAdjustment.ShadowsHighlights)
            {
                float luminance=.2126f*c.r+.7152f*c.g+.0722f*c.b;
                float shadow=(1-luminance)*(1-luminance)*adjustmentShadows/100;
                float highlight=luminance*luminance*adjustmentHighlights/100;
                c=Color.Lerp(c,Color.white,shadow*.7f);c=Color.Lerp(c,Color.black,highlight*.7f);
            }
            c.a=source.a;return c;
        }
        private enum ImagePixelCommand { Invert, WhiteToAlpha, BlackToAlpha }
        private static Color ConvertImagePixel(Color color,ImagePixelCommand command)
        {
            if(command==ImagePixelCommand.Invert)return new Color(1-color.r,1-color.g,1-color.b,color.a);
            float amount=command==ImagePixelCommand.BlackToAlpha?Mathf.Max(color.r,Mathf.Max(color.g,color.b)):1-Mathf.Min(color.r,Mathf.Min(color.g,color.b));
            if(amount<=.000001f)return Color.clear;
            float removed=command==ImagePixelCommand.WhiteToAlpha?1-amount:0;
            return new Color((color.r-removed)/amount,(color.g-removed)/amount,(color.b-removed)/amount,color.a*amount);
        }
        private void ApplyImagePixelCommand(ImagePixelCommand command)
        {
            if(!CanAdjustImage)return;
            var layer=layers[selectedLayer];var texture=ReadTexture(layer.texture);
            try
            {
                var pixels=texture.GetPixels();bool changed=imageChannel==ImageChannel.RGB&&layer.blackToAlpha;
                for(int y=0;y<texture.height;y++)for(int x=0;x<texture.width;x++)
                {
                    int i=y*texture.width+x;Color old=pixels[i];
                    // Retain old documents' visible conversion when replacing the retired toggle with pixel edits.
                    if(imageChannel==ImageChannel.RGB&&layer.blackToAlpha)
                    {Color rgb=QualitySettings.activeColorSpace==ColorSpace.Linear?old.linear:old;old=new Color(1,1,1,old.a*(.2126f*rgb.r+.7152f*rgb.g+.0722f*rgb.b));pixels[i]=old;}
                    if(!ImagePixelEditable(layer,x,y))continue;
                    Color next=ImageChannelEffect(old,ConvertImagePixel(old,command));
                    if(imageChannel==ImageChannel.Alpha&&command==ImagePixelCommand.Invert)next=SetImageChannel(old,1-old.a);
                    if(next!=old){pixels[i]=next;changed=true;}
                }
                if(changed)
                {
                    inputSnapshot=CaptureImage();texture.SetPixels(pixels);texture.Apply(false);
                    ownedTextures.Add(texture);layer.texture=texture;texture=null;if(imageChannel==ImageChannel.RGB)layer.blackToAlpha=false;
                    MarkCompositeDirty();inputSnapshot=null;
                }
            }
            finally{if(texture)DestroyImmediate(texture);}
        }

        private void ApplyImageAdjustment()
        {
            if(!CanAdjustImage || imageAdjustment==ImageAdjustment.None)return;
            var layer=layers[selectedLayer];var texture=ReadTexture(layer.texture);
            try
            {
                var pixels=texture.GetPixels();bool changed=false;
                for(int y=0;y<texture.height;y++)for(int x=0;x<texture.width;x++)
                {
                    int i=y*texture.width+x;if((imageChannel==ImageChannel.RGB&&pixels[i].a<=0) || !ImagePixelEditable(layer,x,y))continue;
                    Color next=ImageChannelAdjustment(pixels[i]);if(next!=pixels[i]){pixels[i]=next;changed=true;}
                }
                if(changed)
                {
                    inputSnapshot=CaptureImage();texture.SetPixels(pixels);texture.Apply(false);
                    ownedTextures.Add(texture);layer.texture=texture;texture=null;
                    MarkCompositeDirty();inputSnapshot=null;
                }
                CancelImageAdjustment();Repaint();
            }
            finally{if(texture)DestroyImmediate(texture);}
        }
    }
}
