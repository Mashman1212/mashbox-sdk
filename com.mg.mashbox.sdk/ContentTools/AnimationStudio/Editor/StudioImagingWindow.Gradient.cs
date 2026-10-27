using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioImagingWindow
    {
        private enum ImageGradientMode { Linear, Radial }
        [SerializeField] private ImageGradientMode imageGradientMode;
        [SerializeField] private Color gradientEndColor=Color.white;
        [SerializeField] private float gradientOpacity=1;
        private bool drawingImageGradient;
        private Vector2 gradientStart,gradientEnd;
        private int gradientControl;
        private void EndImageGradient()
        {
            if(drawingImageGradient && GUIUtility.hotControl==gradientControl)GUIUtility.hotControl=0;
            drawingImageGradient=false;inputSnapshot=null;Repaint();
        }
        private bool HandleImageGradient(Rect rect)
        {
            var e=Event.current;int id=GUIUtility.GetControlID("Image gradient".GetHashCode(),FocusType.Passive);
            if(drawingImageGradient)
            {
                if(e.type==EventType.KeyDown && e.keyCode==KeyCode.Escape){EndImageGradient();e.Use();return true;}
                if(GUIUtility.hotControl!=gradientControl || e.type==EventType.Ignore){EndImageGradient();return true;}
                if(e.type==EventType.MouseDrag || e.rawType==EventType.MouseUp)
                {
                    gradientEnd=GuiToCanvas(e.mousePosition,rect);
                    if(e.shift){Vector2 delta=gradientEnd-gradientStart;if(Mathf.Abs(delta.x)>Mathf.Abs(delta.y))gradientEnd.y=gradientStart.y;else gradientEnd.x=gradientStart.x;}
                    if(e.rawType==EventType.MouseUp){ApplyImageGradient();EndImageGradient();}
                    e.Use();Repaint();return true;
                }
                return true;
            }
            if(imageTool!=ImageTool.Gradient)return false;
            if(e.type==EventType.MouseDown && e.button==0 && !e.alt && rect.Contains(e.mousePosition) && CanAdjustImage && GUIUtility.hotControl==0)
            {
                gradientStart=gradientEnd=GuiToCanvas(e.mousePosition,rect);drawingImageGradient=true;
                gradientControl=id;GUIUtility.hotControl=id;GUIUtility.keyboardControl=0;e.Use();Repaint();
            }
            return true;
        }
        private void DrawImageGradient(Rect rect)
        {
            if(!drawingImageGradient || Event.current.type!=EventType.Repaint)return;
            Vector2 start=CanvasToGui(gradientStart,rect),end=CanvasToGui(gradientEnd,rect);
            DrawImageIconLine(Color.black,3,start,end);DrawImageIconLine(Color.white,1,start,end);
            DrawImageGuiCircle(start,4,Color.white);DrawImageGuiCircle(end,4,Color.white);
            if(imageGradientMode==ImageGradientMode.Radial)DrawImageGuiCircle(start,Vector2.Distance(start,end),StudioTheme.Current.Accent);
        }
        private void DrawImageGradientSettings()
        {
            imageGradientMode=(ImageGradientMode)EditorGUILayout.EnumPopup("Type",imageGradientMode);
            brushColor=EditorGUILayout.ColorField(new GUIContent("Start color"),brushColor,true,true,false);
            gradientEndColor=EditorGUILayout.ColorField(new GUIContent("End color"),gradientEndColor,true,true,false);
            gradientOpacity=EditorGUILayout.Slider("Opacity",gradientOpacity,0,1);
            if(GUILayout.Button("Swap colors")){var color=brushColor;brushColor=gradientEndColor;gradientEndColor=color;}
            EditorGUILayout.LabelField("Drag to set direction and reach; release to apply. Shift constrains the direction. Esc cancels. Uses the selected layer and marquee.",StudioTheme.Styles.wordWrappedMiniLabel);
        }
        private float ImageGradientAmount(Vector2 point)
        {
            Vector2 delta=gradientEnd-gradientStart;
            return Mathf.Clamp01(imageGradientMode==ImageGradientMode.Radial?Vector2.Distance(point,gradientStart)/delta.magnitude:Vector2.Dot(point-gradientStart,delta)/delta.sqrMagnitude);
        }
        private void ApplyImageGradient()
        {
            if(!CanAdjustImage || Vector2.Distance(gradientStart,gradientEnd)<1 || gradientOpacity<=0)return;
            var layer=layers[selectedLayer];var texture=ReadTexture(layer.texture);
            try
            {
                var pixels=texture.GetPixels();bool changed=false;
                for(int y=0;y<texture.height;y++)for(int x=0;x<texture.width;x++)
                {
                    if(!ImagePixelEditable(layer,x,y))continue;
                    int i=y*texture.width+x;Color old=pixels[i];
                    Color color=Color.Lerp(brushColor,gradientEndColor,ImageGradientAmount(ImagePixelCanvasPosition(layer,x,y)));
                    float alpha=color.a*gradientOpacity, resultAlpha=alpha+old.a*(1-alpha);
                    if(alpha<=0 || resultAlpha<=0)continue;
                    Color result=(color*alpha+old*(old.a*(1-alpha)))/resultAlpha;result.a=resultAlpha;
                    if(imageChannel!=ImageChannel.RGB)result=ImageChannelDraw(old,color,gradientOpacity);
                    if(result!=old){pixels[i]=result;changed=true;}
                }
                if(changed)
                {
                    inputSnapshot=CaptureImage();texture.SetPixels(pixels);texture.Apply(false);
                    ownedTextures.Add(texture);layer.texture=texture;texture=null;MarkCompositeDirty();inputSnapshot=null;
                }
            }
            finally{if(texture)DestroyImmediate(texture);}
        }
    }
}
