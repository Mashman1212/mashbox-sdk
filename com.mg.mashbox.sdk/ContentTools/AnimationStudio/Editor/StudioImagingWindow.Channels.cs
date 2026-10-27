using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioImagingWindow
    {
        private enum ImageChannel { RGB, Red, Green, Blue, Alpha }
        [SerializeField] private bool imageChannelsTab;
        [SerializeField] private ImageChannel imageChannel;
        private Material imageChannelMaterial;
        private RenderTexture imageChannelSource,imageChannelPreview;
        private bool imageChannelDirty=true;
        private StickerLayer imageChannelLayer;
        private ImageChannel imageChannelRendered;
        private Vector2 imageChannelsScroll;
        // Keep the original four numeric IDs so existing documents retain their blend modes.
        private static readonly string[] ImageBlendNames={"Normal","Multiply","Screen","Linear Dodge (Add)","Dissolve","Darken","Color Burn","Linear Burn","Darker Color","Lighten","Color Dodge","Lighter Color","Overlay","Soft Light","Hard Light","Vivid Light","Linear Light","Pin Light","Hard Mix","Difference","Exclusion","Subtract","Divide","Hue","Saturation","Color","Luminosity"};
        private static readonly int[][] ImageBlendGroups={new[]{0,4},new[]{5,1,6,7,8},new[]{9,2,10,3,11},new[]{12,13,14,15,16,17,18},new[]{19,20,21,22},new[]{23,24,25,26}};
        private void DrawImageLayerTabs()
        {
            DrawImageLayerSectionDivider();
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Toggle(!imageChannelsTab,"Layers",StudioTheme.Styles.overlayButton,GUILayout.Width(65),GUILayout.Height(24))&&imageChannelsTab)
                {imageChannelsTab=false;imageChannel=ImageChannel.RGB;RequestImageAdjustmentPreview();Repaint();}
                if(GUILayout.Toggle(imageChannelsTab,"Channels",StudioTheme.Styles.overlayButton,GUILayout.Width(78),GUILayout.Height(24))&&!imageChannelsTab)
                {imageChannelsTab=true;Repaint();}
                GUILayout.FlexibleSpace();
            }
            DrawImageLayerSectionDivider();
        }
        private void DrawImageLayerSectionDivider()
        {
            var divider=GUILayoutUtility.GetRect(0,3,GUILayout.ExpandWidth(true));
            if(Event.current.type==EventType.Repaint)EditorGUI.DrawRect(divider,StudioTheme.Current.Button);
        }
        private void DrawImageLayerBottomBar()
        {
            DrawImageLayerSectionDivider();
            var bar=GUILayoutUtility.GetRect(0,26,GUILayout.ExpandWidth(true));
            if(Event.current.type==EventType.Repaint)EditorGUI.DrawRect(bar,StudioTheme.Current.Header);
            if(GUI.Button(new Rect(bar.xMax-27,bar.y+1,24,24),new GUIContent("+","Add paint layer"),StudioTheme.Styles.overlayButton))AddPaintLayer();
        }
        private void DrawImageLayerBlendControls()
        {
            using(new EditorGUI.DisabledScope(!HasSelectedLayer))
            {
                var layer=HasSelectedLayer?layers[selectedLayer]:null;
                using(new EditorGUILayout.HorizontalScope())
                {
                    Rect row=GUILayoutUtility.GetRect(10,23,GUILayout.ExpandWidth(true));
                    float blendWidth=Mathf.Clamp(row.width*.43f,80,200);
                    Rect blend=new Rect(row.x,row.y,blendWidth,21);
                    string name=layer==null?"Normal":ImageBlendNames[Mathf.Clamp(layer.blendMode,0,ImageBlendNames.Length-1)];
                    if(EditorGUI.DropdownButton(blend,new GUIContent(name,"Layer blend mode"),FocusType.Passive,EditorStyles.popup)&&layer!=null)
                    {
                        var menu=new StudioMenu();
                        for(int group=0;group<ImageBlendGroups.Length;group++)
                        {if(group>0)menu.AddSeparator("");foreach(int mode in ImageBlendGroups[group]){int selected=mode;AddImageMenuItem(menu,ImageBlendNames[mode],()=>{layer.blendMode=selected;MarkCompositeDirty();},true,layer.blendMode==mode);}}
                        menu.DropDown(blend,this,"Layer blend mode");
                    }
                    var opacityRect=new Rect(blend.xMax+8,row.y,Mathf.Max(40,row.xMax-blend.xMax-8),21);
                    EditorGUI.BeginChangeCheck();
                    float oldWidth=EditorGUIUtility.labelWidth,oldFieldWidth=EditorGUIUtility.fieldWidth;EditorGUIUtility.labelWidth=49;EditorGUIUtility.fieldWidth=34;
                    float opacity=EditorGUI.Slider(opacityRect,new GUIContent("Opacity","Layer opacity (%)"),(layer==null?1:layer.opacity)*100,0,100);
                    EditorGUIUtility.labelWidth=oldWidth;EditorGUIUtility.fieldWidth=oldFieldWidth;
                    if(EditorGUI.EndChangeCheck()&&layer!=null){layer.opacity=opacity/100;MarkCompositeDirty();}
                }
            }
            DrawImageLayerSectionDivider();
            EditorGUILayout.Space(3);
        }
        private void DrawImageChannelsPanel()
        {
            using(var scroll=new EditorGUILayout.ScrollViewScope(imageChannelsScroll,GUILayout.ExpandHeight(true)))
            {
            imageChannelsScroll=scroll.scrollPosition;
            if(!HasSelectedLayer){EditorGUILayout.HelpBox("Select a layer to paint its channels.",MessageType.Info);return;}
            EditorGUILayout.LabelField(layers[selectedLayer].name,StudioTheme.Styles.miniLabel);
            foreach(ImageChannel channel in System.Enum.GetValues(typeof(ImageChannel)))
            {
                var row=GUILayoutUtility.GetRect(10,38,GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(row,imageChannel==channel?StudioTheme.Current.Selection:StudioTheme.Current.Panel);
                var thumb=new Rect(row.x+6,row.y+4,30,30);
                if(Event.current.type==EventType.Repaint&&layers[selectedLayer].texture&&EnsureImageChannelMaterial())
                {imageChannelMaterial.SetFloat("_Channel",(int)channel);EditorGUI.DrawPreviewTexture(thumb,layers[selectedLayer].texture,imageChannelMaterial,ScaleMode.ScaleToFit);}
                GUI.Label(new Rect(row.x+44,row.y+8,row.width-48,24),channel==ImageChannel.RGB?"RGB — full color":channel.ToString(),StudioTheme.Styles.label);
                if(GUI.Button(row,new GUIContent("","Paint "+channel+" on the selected layer"),GUIStyle.none)){imageChannel=channel;RequestImageAdjustmentPreview();Repaint();}
            }
            EditorGUILayout.HelpBox(imageChannel==ImageChannel.RGB?"RGB paints normally. Select a channel to edit it alone.":"Painting "+imageChannel+" only. Use white to raise values and black to lower them. Other channels stay intact.",MessageType.None);
            }
        }
        private bool EnsureImageChannelMaterial()
        {
            if(imageChannelMaterial)return true;var shader=Shader.Find("Hidden/MashBox/ImagingChannelPreview");if(!shader)return false;
            imageChannelMaterial=new Material(shader){hideFlags=HideFlags.HideAndDontSave};return true;
        }
        private Texture ImageCanvasDisplay()
        {
            if(imageChannel==ImageChannel.RGB||!HasSelectedLayer||!EnsureImageChannelMaterial())return composite;
            if(!imageChannelSource||imageChannelSource.width!=CanvasSize)
            {
                ReleaseImageChannelTextures();
                imageChannelSource=new RenderTexture(CanvasSize,CanvasSize,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){hideFlags=HideFlags.HideAndDontSave};imageChannelSource.Create();
                imageChannelPreview=new RenderTexture(CanvasSize,CanvasSize,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){hideFlags=HideFlags.HideAndDontSave};imageChannelPreview.Create();imageChannelDirty=true;
            }
            bool refresh=imageChannelDirty||imageChannelLayer!=layers[selectedLayer];
            if(Event.current.type==EventType.Repaint&&(refresh||imageChannelRendered!=imageChannel))
            {
                if(refresh){RenderImageComposite(layers[selectedLayer],false,imageChannelSource);imageChannelLayer=layers[selectedLayer];imageChannelDirty=false;}
                imageChannelMaterial.SetFloat("_Channel",(int)imageChannel);
                bool srgb=GL.sRGBWrite;var previous=RenderTexture.active;
                try{GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear;Graphics.Blit(imageChannelSource,imageChannelPreview,imageChannelMaterial);}finally{GL.sRGBWrite=srgb;RenderTexture.active=previous;}
                imageChannelRendered=imageChannel;
            }
            return imageChannelPreview;
        }
        private void ReleaseImageChannelTextures()
        {if(imageChannelSource){imageChannelSource.Release();DestroyImmediate(imageChannelSource);}if(imageChannelPreview){imageChannelPreview.Release();DestroyImmediate(imageChannelPreview);}imageChannelSource=imageChannelPreview=null;imageChannelLayer=null;imageChannelDirty=true;}
        private void ReleaseImageChannels()
        {ReleaseImageChannelTextures();if(imageChannelMaterial)DestroyImmediate(imageChannelMaterial);imageChannelMaterial=null;}
        private static float ImageChannelGray(Color color)=>Mathf.Clamp01(.299f*color.r+.587f*color.g+.114f*color.b);
        private float ImageChannelValue(Color color)=>imageChannel==ImageChannel.Red?color.r:imageChannel==ImageChannel.Green?color.g:imageChannel==ImageChannel.Blue?color.b:color.a;
        private Color SetImageChannel(Color color,float value)
        {switch(imageChannel){case ImageChannel.Red:color.r=value;break;case ImageChannel.Green:color.g=value;break;case ImageChannel.Blue:color.b=value;break;case ImageChannel.Alpha:color.a=value;break;}return color;}
        private Color ImageChannelDraw(Color original,Color source,float amount)
        {if(imageChannel==ImageChannel.RGB)return ImageOver(source,original,amount);return SetImageChannel(original,Mathf.Lerp(ImageChannelValue(original),ImageChannelGray(source),Mathf.Clamp01(amount*source.a)));}
        private Color ImageChannelBrush(Color original,float amount)
        {
            float value=ImageChannelValue(original);
            if(imageTool==ImageTool.Eraser)value=Mathf.Lerp(value,0,amount);
            else if(imageBrushMode==ImageBrushMode.Burn)value*=1-amount*.5f;
            else if(imageBrushMode==ImageBrushMode.Dodge)value=Mathf.Lerp(value,1,amount*.25f);
            else value=Mathf.Lerp(value,ImageChannelGray(brushColor),amount*brushColor.a);
            return SetImageChannel(original,value);
        }
        private Color ImageChannelEffect(Color original,Color adjusted)
        {return imageChannel==ImageChannel.RGB?adjusted:SetImageChannel(original,ImageChannelValue(adjusted));}
        private Color ImageChannelAdjustment(Color original)
        {
            if(imageChannel==ImageChannel.RGB)return AdjustImagePixel(original);
            float value=ImageChannelValue(original);var adjusted=AdjustImagePixel(new Color(value,value,value,original.a));
            return SetImageChannel(original,adjusted.r);
        }
    }
}
