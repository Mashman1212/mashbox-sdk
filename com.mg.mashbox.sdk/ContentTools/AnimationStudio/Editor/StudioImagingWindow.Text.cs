using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioImagingWindow
    {
        [SerializeField] private string imageTextFont="Arial";
        [SerializeField] private int imageTextSize=64,imageTextAlignment;
        [SerializeField] private FontStyle imageTextStyle;
        [SerializeField] private Color imageTextColor=Color.white;
        private bool focusImageText;
        private string[] imageTextFonts;
        private void DrawImageTextFontMenu(StickerLayer layer)
        {
            string family=layer!=null?layer.textFont:imageTextFont;
            if(string.IsNullOrWhiteSpace(family))family="Arial";
            // A native indexed popup can return stale indices when another tool changes
            // the inspector's controls. Font choices use captured names instead.
            Rect row=EditorGUILayout.GetControlRect(false,EditorGUIUtility.singleLineHeight,GUILayout.ExpandWidth(true),GUILayout.MinWidth(0));
            Rect field=EditorGUI.PrefixLabel(row,new GUIContent("Font"));
            if(!EditorGUI.DropdownButton(field,new GUIContent(family,family),FocusType.Passive,EditorStyles.popup))return;
            if(imageTextFonts==null||imageTextFonts.Length==0)
            {
                var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"Arial"};
                foreach(string name in Font.GetOSInstalledFontNames()??Array.Empty<string>())if(!string.IsNullOrWhiteSpace(name))names.Add(name);
                var sorted=new List<string>(names);sorted.Sort(StringComparer.OrdinalIgnoreCase);imageTextFonts=sorted.ToArray();
            }
            var menu=new StudioMenu();
            foreach(string name in imageTextFonts)
            {
                string selected=name;
                AddImageMenuItem(menu,name.Replace('/','∕').Replace('\t',' '),()=>
                {
                    imageTextFont=selected;
                    if(layer!=null&&layers.Contains(layer)&&!layer.locked)
                    {layer.textFont=selected;try{RefreshImageText(layer);}catch(Exception error){status=error.Message;statusType=MessageType.Warning;}}
                },true,string.Equals(name,family,StringComparison.OrdinalIgnoreCase));
            }
            menu.DropDown(field,this,"Text font");
        }
        private readonly Dictionary<string,Font> imageTextFontCache=new Dictionary<string,Font>();
        private Material imageTextMaterial;
        private void EditImageText()
        {imageTool=ImageTool.Text;imageToolSettingsExpanded=true;focusImageText=true;Repaint();}
        private void RasterizeImageText()
        {if(!HasSelectedLayer||!layers[selectedLayer].isText||layers[selectedLayer].locked)return;layers[selectedLayer].isText=false;MarkCompositeDirty();}
        private Font ResolveImageTextFont(string family,int size)
        {
            string key=family+"/"+size;
            if(imageTextFontCache.TryGetValue(key,out var cached)&&cached)return cached;
            if(imageTextFontCache.Count>=16){string oldest=new List<string>(imageTextFontCache.Keys)[0];if(imageTextFontCache[oldest])DestroyImmediate(imageTextFontCache[oldest]);imageTextFontCache.Remove(oldest);}
            Font font=Font.CreateDynamicFontFromOSFont(string.IsNullOrEmpty(family)?"Arial":family,size);
            if(!font)font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            else {font.hideFlags=HideFlags.HideAndDontSave;imageTextFontCache[key]=font;}
            return font;
        }
        private Texture2D RenderImageText(StickerLayer layer)
        {
            int size=Mathf.Clamp(layer.textSize,1,512);
            var font=ResolveImageTextFont(layer.textFont,size);
            var settings=new TextGenerationSettings
            {
                font=font,fontSize=size,fontStyle=layer.textStyle,color=layer.textColor,scaleFactor=1,
                textAnchor=layer.textAlignment==1?TextAnchor.UpperCenter:layer.textAlignment==2?TextAnchor.UpperRight:TextAnchor.UpperLeft,
                generationExtents=new Vector2(8192,8192),pivot=new Vector2(0,1),lineSpacing=1,
                horizontalOverflow=HorizontalWrapMode.Overflow,verticalOverflow=VerticalWrapMode.Overflow,
                richText=false,generateOutOfBounds=true,updateBounds=true,alignByGeometry=true
            };
            string text=layer.textContent??"";
            using(var generator=new TextGenerator())
            {
                if(!generator.Populate(text,settings))throw new InvalidOperationException("Could not render this text with the selected font.");
                // TextGenerator returns the glyph quads themselves in Unity 6.
                // Removing a presumed trailing quad drops the final visible character.
                var verts=generator.verts;int count=verts.Count/4*4;
                if(count==0)
                {var empty=new Texture2D(2,Mathf.Max(2,size),TextureFormat.RGBA32,false,false){hideFlags=HideFlags.HideAndDontSave};empty.SetPixels32(new Color32[empty.width*empty.height]);empty.Apply();return empty;}
                Vector2 min=Vector2.one*float.MaxValue,max=Vector2.one*float.MinValue;
                for(int i=0;i<count;i++){Vector2 point=verts[i].position;min=Vector2.Min(min,point);max=Vector2.Max(max,point);}
                int width=Mathf.Max(2,Mathf.CeilToInt(max.x-min.x)+8),height=Mathf.Max(2,Mathf.CeilToInt(max.y-min.y)+8);
                if(width>8192||height>8192)throw new InvalidOperationException("Text is too large. Reduce the font size or split it into multiple text layers.");
                var positions=new Vector3[count];var uv=new Vector2[count];var colors=new Color32[count];var indices=new int[count/4*6];
                for(int i=0;i<count;i++){var v=verts[i];positions[i]=new Vector3(v.position.x-min.x+4,v.position.y-min.y+4,0);uv[i]=v.uv0;colors[i]=v.color;}
                for(int q=0;q<count/4;q++){int i=q*6,v=q*4;indices[i]=v;indices[i+1]=v+1;indices[i+2]=v+2;indices[i+3]=v+2;indices[i+4]=v+3;indices[i+5]=v;}
                var mesh=new Mesh{hideFlags=HideFlags.HideAndDontSave};mesh.vertices=positions;mesh.uv=uv;mesh.colors32=colors;mesh.triangles=indices;
                if(!imageTextMaterial){var shader=Shader.Find("Hidden/MashBox/ImagingText");if(!shader){DestroyImmediate(mesh);throw new InvalidOperationException("Text rendering shader is unavailable.");}imageTextMaterial=new Material(shader){hideFlags=HideFlags.HideAndDontSave};}
                var premultiplied=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
                var straight=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
                var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
                try
                {
                    RenderTexture.active=premultiplied;GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear;GL.Clear(false,true,Color.clear);
                    GL.PushMatrix();try{GL.LoadPixelMatrix(0,width,0,height);imageTextMaterial.SetTexture("_MainTex",font.material.mainTexture);imageTextMaterial.SetPass(0);Graphics.DrawMeshNow(mesh,Matrix4x4.identity);}finally{GL.PopMatrix();}
                    Graphics.Blit(premultiplied,straight,imageTextMaterial,1);var result=ReadTexture(straight);result.name="Text: "+text;return result;
                }
                finally{GL.sRGBWrite=srgb;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(premultiplied);RenderTexture.ReleaseTemporary(straight);DestroyImmediate(mesh);}
            }
        }
        private void RefreshImageText(StickerLayer layer)
        {
            if(layer==inlineTextLayer)inputSnapshot=null;
            Vector2 anchor=layer.texture?ImageUVToCanvas(layer,Vector2.zero):layer.position;
            var texture=RenderImageText(layer);ownedTextures.Add(texture);layer.texture=texture;layer.sourceRect=FullSourceRect;layer.textRasterVersion=1;
            layer.position+=anchor-ImageUVToCanvas(layer,Vector2.zero);MarkCompositeDirty();
        }
        private void CreateImageTextLayer(Vector2 point)
        {
            var layer=new StickerLayer{name="Text",isText=true,textContent="Text",textFont=imageTextFont,textSize=imageTextSize,textStyle=imageTextStyle,textAlignment=imageTextAlignment,textColor=imageTextColor,position=point};
            var texture=RenderImageText(layer);ownedTextures.Add(texture);layer.texture=texture;layer.textRasterVersion=1;layer.position=point+new Vector2(texture.width,texture.height)*.5f;
            layers.Add(layer);selectedLayer=layers.Count-1;imageChannel=ImageChannel.RGB;MarkCompositeDirty();EditImageText();
        }
        private bool HandleImageText(Rect rect)
        {
            if(imageTool!=ImageTool.Text)return false;var e=Event.current;
            EditorGUIUtility.AddCursorRect(new Rect(0,0,canvasArea.width,canvasArea.height),MouseCursor.Text);
            if(e.type==EventType.MouseDown&&e.button==0&&!e.alt&&rect.Contains(e.mousePosition))
            {
                CancelImageAdjustment();var point=GuiToCanvas(e.mousePosition,rect);int hit=HitTestLayers(point);
                if(hit>=0&&layers[hit].isText){selectedLayer=hit;EditImageText();}
                else try{CreateImageTextLayer(point);}catch(Exception error){status=error.Message;statusType=MessageType.Warning;}
                e.Use();Repaint();
            }
            return true;
        }
        private void RepairLegacyImageText()
        {
            foreach(var layer in layers)
            {
                if(!layer.isText||layer.textRasterVersion>=1)continue;
                try{RefreshImageText(layer);}catch(Exception error){Debug.LogWarning("Could not refresh text layer '"+layer.name+"': "+error.Message);}
            }
        }
        private void DrawImageTextSettings()
        {
            StickerLayer layer=HasSelectedLayer&&layers[selectedLayer].isText?layers[selectedLayer]:null;
            if(layer==null){EditorGUILayout.LabelField("Click the image to add text. Click existing text to edit it.",StudioTheme.Styles.wordWrappedMiniLabel);}
            using(new EditorGUI.DisabledScope(layer!=null&&layer.locked))
            {
                EditorGUI.BeginChangeCheck();
                if(layer!=null)
                {
                    using(new EditorGUI.DisabledScope(layer==inlineTextLayer))
                    {GUI.SetNextControlName("ImageTextContent");string content=EditorGUILayout.TextArea(layer.textContent??"",GUILayout.Height(76),GUILayout.MinWidth(0),GUILayout.ExpandWidth(true));layer.textContent=content.Length>4096?content.Substring(0,4096):content;}
                }
                DrawImageTextFontMenu(layer);
                int size=EditorGUILayout.IntSlider("Size",layer!=null?layer.textSize:imageTextSize,1,512);
                var style=(FontStyle)EditorGUILayout.EnumPopup("Style",layer!=null?layer.textStyle:imageTextStyle);
                int alignment=EditorGUILayout.Popup("Alignment",layer!=null?layer.textAlignment:imageTextAlignment,new[]{"Left","Center","Right"});
                var color=EditorGUILayout.ColorField("Color",layer!=null?layer.textColor:imageTextColor);
                if(EditorGUI.EndChangeCheck())
                {
                    imageTextSize=size;imageTextStyle=style;imageTextAlignment=alignment;imageTextColor=color;
                    if(layer!=null)
                    {layer.textSize=size;layer.textStyle=style;layer.textAlignment=alignment;layer.textColor=color;try{RefreshImageText(layer);}catch(Exception error){status=error.Message;statusType=MessageType.Warning;}}
                }
            }
            if(focusImageText&&layer!=null&&Event.current.type==EventType.Repaint){EditorGUI.FocusTextInControl("ImageTextContent");focusImageText=false;}
            if(layer!=null)EditorGUILayout.LabelField("Double-click text on the image to type there. Ctrl+Enter finishes; Esc cancels. Ctrl+T transforms.",StudioTheme.Styles.wordWrappedMiniLabel);
            EditorGUILayout.Space(8);
        }
        private void ReleaseImageTextResources()
        {if(imageTextMaterial)DestroyImmediate(imageTextMaterial);imageTextMaterial=null;foreach(var font in imageTextFontCache.Values)if(font)DestroyImmediate(font);imageTextFontCache.Clear();}
    }
}
