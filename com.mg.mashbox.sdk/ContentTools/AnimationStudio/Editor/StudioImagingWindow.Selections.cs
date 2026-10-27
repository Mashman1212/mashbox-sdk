using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioImagingWindow
    {
        private enum ImageSelectionTool { Rectangle, Lasso, MagneticLasso }
        [SerializeField] private ImageSelectionTool selectionTool;
        private byte[] selectionMask;
        private int selectionMaskSize;
        
        private bool selectionOutlineDirty=true, tracingSelection, choosingColorRange;
        private readonly List<Vector2> selectionPath=new List<Vector2>();
        private int selectionPathControl;
        private string selectionBeforeTrace, selectionBeforeRange;
        private Color32[] selectionReference;
        private int selectionReferenceSize;
        [SerializeField] private float magneticRadius=12, colorRangeTolerance=.15f;
        [SerializeField] private Color colorRangeColor=Color.white;
        private static byte[] copiedSelectionPng;
        private static Vector2 copiedSelectionCenter;
        private static uint copiedSelectionSequence;

        private StudioMenu BuildImageSelectMenu()
        {
            var menu=new StudioMenu();
            AddImageMenuItem(menu,"Rectangular Selection\tM",()=>ChooseImageSelectionTool(ImageSelectionTool.Rectangle),true,imageTool==ImageTool.Marquee&&selectionTool==ImageSelectionTool.Rectangle);
            AddImageMenuItem(menu,"Lasso\tL",()=>ChooseImageSelectionTool(ImageSelectionTool.Lasso),true,imageTool==ImageTool.Marquee&&selectionTool==ImageSelectionTool.Lasso);
            AddImageMenuItem(menu,"Magnetic Lasso",()=>ChooseImageSelectionTool(ImageSelectionTool.MagneticLasso),HasSelectedLayer,imageTool==ImageTool.Marquee&&selectionTool==ImageSelectionTool.MagneticLasso);
            AddImageMenuItem(menu,"Color Range…",BeginImageColorRange,HasSelectedLayer);
            menu.AddSeparator("");
            AddImageMenuItem(menu,"Select All\tCtrl+A",SelectAllImage);
            AddImageMenuItem(menu,"Deselect\tCtrl+D",DeselectImage,hasImageSelection);
            AddImageMenuItem(menu,"Invert Selection\tCtrl+Shift+I",InvertImageSelection,hasImageSelection);
            return menu;
        }
        private void ChooseImageSelectionTool(ImageSelectionTool tool)
        {CancelSelectionTrace();CancelImageColorRange();selectionTool=tool;imageTool=ImageTool.Marquee;imageToolSettingsExpanded=true;Repaint();}
        private void SetImageSelection(byte[] mask,int size,Rect bounds)
        {selectionMask=mask;selectionMaskSize=size;selectionMaskOffset=Vector2.zero;imageSelection=bounds;hasImageSelection=mask!=null||(bounds.width>0&&bounds.height>0);selectionOutlineDirty=true;RequestImageAdjustmentPreview();Repaint();}
        private void SelectAllImage()
        {CancelSelectionTrace();CancelImageColorRange();inputSnapshot=CaptureImage();SetImageSelection(null,0,new Rect(0,0,1,1));MarkCompositeDirty();inputSnapshot=null;}
        private void DeselectImage()
        {CancelSelectionTrace();CancelImageColorRange();EndImageMarquee();inputSnapshot=CaptureImage();SetImageSelection(null,0,new Rect());MarkCompositeDirty();inputSnapshot=null;}
        private bool ImageSelectionContains(Vector2 normalized)
        {
            if(!hasImageSelection)return true;
            if(!imageSelection.Contains(normalized))return false;
            if(selectionMask==null)return true;
            normalized-=selectionMaskOffset;
            if(normalized.x<0||normalized.x>=1||normalized.y<0||normalized.y>=1)return false;
            int x=Mathf.Clamp((int)(normalized.x*selectionMaskSize),0,selectionMaskSize-1),y=Mathf.Clamp((int)(normalized.y*selectionMaskSize),0,selectionMaskSize-1);
            return selectionMask[y*selectionMaskSize+x]!=0;
        }
        private void InvertImageSelection()
        {
            inputSnapshot=CaptureImage();int size=CanvasSize;var mask=new byte[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)mask[y*size+x]=(byte)(ImageSelectionContains(new Vector2((x+.5f)/size,(y+.5f)/size))?0:255);
            SetImageSelection(mask,size,ImageMaskBounds(mask,size));MarkCompositeDirty();inputSnapshot=null;
        }
        private static Rect ImageMaskBounds(byte[] mask,int size)
        {
            int x0=size,y0=size,x1=-1,y1=-1;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)if(mask[y*size+x]!=0){x0=Math.Min(x0,x);y0=Math.Min(y0,y);x1=Math.Max(x1,x);y1=Math.Max(y1,y);}
            return x1<x0?new Rect():Rect.MinMaxRect(x0/(float)size,y0/(float)size,(x1+1)/(float)size,(y1+1)/(float)size);
        }
        private static string EncodeSelectionMask(byte[] mask)
        {
            if(mask==null)return null;
            using(var stream=new MemoryStream())
            {using(var compressor=new DeflateStream(stream,System.IO.Compression.CompressionLevel.Fastest,true))compressor.Write(mask,0,mask.Length);return Convert.ToBase64String(stream.ToArray());}
        }
        private static byte[] DecodeSelectionMask(string encoded,int size)
        {
            if(string.IsNullOrEmpty(encoded))return null;
            if(size<1||size>4096)throw new InvalidDataException("Invalid selection size.");
            var data=new byte[size*size];
            using(var stream=new MemoryStream(Convert.FromBase64String(encoded)))using(var reader=new DeflateStream(stream,CompressionMode.Decompress))
            {int count=0;while(count<data.Length){int read=reader.Read(data,count,data.Length-count);if(read==0)throw new InvalidDataException("Incomplete selection mask.");count+=read;}if(reader.ReadByte()!=-1)throw new InvalidDataException("Invalid selection mask length.");}
            return data;
        }
        private Texture2D ReadSelectedImageLayer()
        {
            if(!HasSelectedLayer)return null;
            RenderImageComposite(layers[selectedLayer],false);
            try{return ReadTexture(composite);}finally{RenderImageComposite();}
        }
        private void CaptureSelectionReference()
        {
            var texture=ReadSelectedImageLayer();if(!texture)return;
            try{selectionReference=texture.GetPixels32();selectionReferenceSize=texture.width;}finally{DestroyImmediate(texture);}
        }
        private Color32 SelectionReferencePixel(int x,int y)
        {int size=selectionReferenceSize;return selectionReference[(size-1-Mathf.Clamp(y,0,size-1))*size+Mathf.Clamp(x,0,size-1)];}
        private static float SelectionLuminance(Color32 color)=>color.a/255f*(color.r*.2126f+color.g*.7152f+color.b*.0722f)/255;
        private Vector2 SnapImageSelectionEdge(Vector2 point)
        {
            if(selectionReference==null)return point;
            int size=selectionReferenceSize,cx=Mathf.RoundToInt(point.x*size),cy=Mathf.RoundToInt(point.y*size),radius=Mathf.Clamp(Mathf.RoundToInt(magneticRadius),2,40);
            Vector2 best=point;float score=.08f;
            for(int y=Mathf.Max(1,cy-radius);y<Mathf.Min(size-1,cy+radius+1);y++)for(int x=Mathf.Max(1,cx-radius);x<Mathf.Min(size-1,cx+radius+1);x++)
            {
                float distance=Vector2.Distance(new Vector2(x,y),new Vector2(cx,cy));if(distance>radius)continue;
                float dx=SelectionLuminance(SelectionReferencePixel(x+1,y))-SelectionLuminance(SelectionReferencePixel(x-1,y));
                float dy=SelectionLuminance(SelectionReferencePixel(x,y+1))-SelectionLuminance(SelectionReferencePixel(x,y-1));
                float candidate=Mathf.Sqrt(dx*dx+dy*dy)-.3f*distance/radius;
                if(candidate>score){score=candidate;best=new Vector2((x+.5f)/size,(y+.5f)/size);}
            }
            return best;
        }
        private bool HandleImageLasso(Rect rect)
        {
            if(!tracingSelection&&(imageTool!=ImageTool.Marquee||selectionTool==ImageSelectionTool.Rectangle))return false;
            var e=Event.current;var viewport=new Rect(0,0,canvasArea.width,canvasArea.height);
            int id=GUIUtility.GetControlID("Image lasso".GetHashCode(),FocusType.Passive);

            if(tracingSelection)
            {
                if(e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape){CancelSelectionTrace();e.Use();return true;}
                if(e.type==EventType.KeyDown&&(e.keyCode==KeyCode.Return||e.keyCode==KeyCode.KeypadEnter)){FinishImageLasso();e.Use();return true;}
                if(e.type==EventType.Ignore||GUIUtility.hotControl!=selectionPathControl){CancelSelectionTrace();return true;}
                bool magnetic=selectionTool==ImageSelectionTool.MagneticLasso;
                if(e.type==EventType.MouseDrag||(magnetic&&e.type==EventType.MouseMove))
                {AppendImageLasso(SelectionPoint(e.mousePosition,rect));e.Use();Repaint();return true;}
                if(e.type==EventType.MouseDown&&e.button==0&&magnetic)
                {if(e.clickCount==2||(selectionPath.Count>3&&Vector2.Distance(CanvasToGui(selectionPath[0]*CanvasSize,rect),e.mousePosition)<8))FinishImageLasso();else AppendImageLasso(SelectionPoint(e.mousePosition,rect));e.Use();return true;}
                if(e.rawType==EventType.MouseUp&&e.button==0)
                {if(!magnetic){AppendImageLasso(SelectionPoint(e.mousePosition,rect));FinishImageLasso();}e.Use();return true;}
                return true;
            }
            if(e.type==EventType.MouseDown&&e.button==0&&!e.alt&&viewport.Contains(e.mousePosition)&&GUIUtility.hotControl==0)
            {
                selectionBeforeTrace=CaptureImage();selectionPath.Clear();if(selectionTool==ImageSelectionTool.MagneticLasso)CaptureSelectionReference();
                tracingSelection=true;selectionPathControl=id;GUIUtility.hotControl=id;GUIUtility.keyboardControl=0;
                AppendImageLasso(SelectionPoint(e.mousePosition,rect));e.Use();Repaint();
            }
            return true;
        }
        private void AppendImageLasso(Vector2 point)
        {
            if(selectionTool==ImageSelectionTool.MagneticLasso)point=SnapImageSelectionEdge(point);
            if(selectionPath.Count==0||Vector2.Distance(point,selectionPath[selectionPath.Count-1])*CanvasSize>=1.5f)selectionPath.Add(point);
        }
        private static byte[] RasterImageLasso(IList<Vector2> path,int size)
        {
            var mask=new byte[size*size];if(path.Count<3)return mask;var crossings=new List<float>();
            for(int y=0;y<size;y++)
            {
                float py=(y+.5f)/size;crossings.Clear();
                for(int i=0,j=path.Count-1;i<path.Count;j=i++)
                {var a=path[j];var b=path[i];if((a.y>py)==(b.y>py))continue;crossings.Add((a.x+(py-a.y)*(b.x-a.x)/(b.y-a.y))*size);}
                crossings.Sort();for(int i=0;i+1<crossings.Count;i+=2)
                {int start=Mathf.Clamp(Mathf.CeilToInt(crossings[i]-.5f),0,size),end=Mathf.Clamp(Mathf.CeilToInt(crossings[i+1]-.5f),0,size);for(int x=start;x<end;x++)mask[y*size+x]=255;}
            }
            return mask;
        }
        private void FinishImageLasso()
        {
            if(!tracingSelection)return;
            if(selectionPath.Count>=3){var mask=RasterImageLasso(selectionPath,CanvasSize);inputSnapshot=selectionBeforeTrace;SetImageSelection(mask,CanvasSize,ImageMaskBounds(mask,CanvasSize));MarkCompositeDirty();}
            EndSelectionTrace();
        }
        private void EndSelectionTrace()
        {if(tracingSelection&&GUIUtility.hotControl==selectionPathControl)GUIUtility.hotControl=0;tracingSelection=false;selectionPath.Clear();selectionReference=null;selectionBeforeTrace=null;inputSnapshot=null;Repaint();}
        private void CancelSelectionTrace()=>EndSelectionTrace();
        private void DrawImageSelectionPath(Rect rect)
        {
            if(Event.current.type!=EventType.Repaint)return;
            if(tracingSelection)
            {
                for(int i=1;i<selectionPath.Count;i++)DrawImageIconLine(Color.white,1,CanvasToGui(selectionPath[i-1]*CanvasSize,rect),CanvasToGui(selectionPath[i]*CanvasSize,rect));
                if(selectionPath.Count>1)DrawImageIconLine(Color.gray,1,CanvasToGui(selectionPath[selectionPath.Count-1]*CanvasSize,rect),CanvasToGui(selectionPath[0]*CanvasSize,rect));
            }
            if(!hasImageSelection||selectionMask==null||drawingImageSelection)return;
            DrawImageMaskOutline(rect);
        }
        private void ReleaseImageSelectionOutline()=>ReleaseImageMaskOutline();

        private void BeginImageColorRange()
        {
            CancelImageAdjustment();CancelSelectionTrace();CancelImageColorRange();selectionBeforeRange=CaptureImage();CaptureSelectionReference();
            if(selectionReference==null)return;choosingColorRange=true;imageToolSettingsExpanded=true;PreviewImageColorRange();
        }
        private void PreviewImageColorRange()
        {
            int size=selectionReferenceSize;var mask=new byte[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {Color c=selectionReference[(size-1-y)*size+x];float distance=Mathf.Max(Mathf.Abs(c.r-colorRangeColor.r),Mathf.Max(Mathf.Abs(c.g-colorRangeColor.g),Mathf.Abs(c.b-colorRangeColor.b)));if(c.a>0&&distance<=colorRangeTolerance)mask[y*size+x]=255;}
            SetImageSelection(mask,size,ImageMaskBounds(mask,size));
        }
        private void CancelImageColorRange()
        {
            if(!choosingColorRange)return;choosingColorRange=false;selectionReference=null;
            var saved=JsonUtility.FromJson<ImageFile>(selectionBeforeRange);hasImageSelection=saved.hasSelection;imageSelection=saved.selection;selectionMask=DecodeSelectionMask(saved.mask,saved.maskSize);selectionMaskSize=saved.maskSize;selectionMaskOffset=saved.maskOffset;selectionOutlineDirty=true;selectionBeforeRange=null;Repaint();
        }
        private bool HandleImageColorRange(Rect rect)
        {
            if(!choosingColorRange)return false;var e=Event.current;
            if(e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape){CancelImageColorRange();e.Use();return true;}

            if(e.type==EventType.MouseDown&&e.button==0&&rect.Contains(e.mousePosition))
            {var p=SelectionPoint(e.mousePosition,rect)*selectionReferenceSize;colorRangeColor=SelectionReferencePixel((int)p.x,(int)p.y);PreviewImageColorRange();e.Use();}
            return true;
        }
        private void DrawImageColorRangeSettings()
        {
            if(!choosingColorRange)return;
            EditorGUILayout.LabelField("COLOR RANGE",StudioTheme.Styles.panelHeader);
            EditorGUILayout.LabelField("Click the image to sample a color. Adjust tolerance, then apply the selection.",StudioTheme.Styles.wordWrappedMiniLabel);
            EditorGUI.BeginChangeCheck();colorRangeColor=EditorGUILayout.ColorField("Sample color",colorRangeColor);colorRangeTolerance=EditorGUILayout.Slider("Tolerance",colorRangeTolerance,0,1);if(EditorGUI.EndChangeCheck())PreviewImageColorRange();
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Apply")){inputSnapshot=selectionBeforeRange;choosingColorRange=false;selectionBeforeRange=null;selectionReference=null;MarkCompositeDirty();inputSnapshot=null;}
                if(GUILayout.Button("Cancel"))CancelImageColorRange();
            }
            EditorGUILayout.Space(8);
        }
        private Texture2D CopyImageSelectionTexture()
        {
            if(hasImageSelection&&(imageSelection.width<=0||imageSelection.height<=0)){status="The selection is empty.";statusType=MessageType.Info;return null;}
            var source=ReadSelectedImageLayer();if(!source)return null;
            try
            {
                int size=CanvasSize;var bounds=hasImageSelection?imageSelection:new Rect(0,0,1,1);
                int x0=Mathf.Clamp(Mathf.FloorToInt(bounds.xMin*size),0,size-1),y0=Mathf.Clamp(Mathf.FloorToInt(bounds.yMin*size),0,size-1);
                int x1=Mathf.Clamp(Mathf.CeilToInt(bounds.xMax*size),x0+1,size),y1=Mathf.Clamp(Mathf.CeilToInt(bounds.yMax*size),y0+1,size);
                var pixels=source.GetPixels32();var output=new Color32[(x1-x0)*(y1-y0)];
                for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++)if(ImageSelectionContains(new Vector2((x+.5f)/size,(y+.5f)/size)))output[(y1-1-y)*(x1-x0)+x-x0]=pixels[(size-1-y)*size+x];
                var texture=new Texture2D(x1-x0,y1-y0,TextureFormat.RGBA32,false,false){name=layers[selectedLayer].name+" selection",hideFlags=HideFlags.HideAndDontSave};texture.SetPixels32(output);texture.Apply();
                copiedSelectionCenter=new Vector2((x0+x1)*.5f,(y0+y1)*.5f);return texture;
            }
            finally{DestroyImmediate(source);}
        }
        private void CopyImageSelection()
        {
            if(!HasSelectedLayer)return;var texture=CopyImageSelectionTexture();if(!texture)return;
            try{copiedSelectionPng=texture.EncodeToPNG();StudioImageClipboard.TryWrite(copiedSelectionPng,out var error);copiedSelectionSequence=StudioImageClipboard.Sequence;status=string.IsNullOrEmpty(error)?"Selection copied.":"Selection copied within 2D Imaging. "+error;statusType=MessageType.Info;}
            finally{DestroyImmediate(texture);}
        }
        private void PasteImageSelection()
        {
            bool local=copiedSelectionPng!=null&&StudioImageClipboard.Sequence==copiedSelectionSequence;
            Texture2D texture;
            if(local){texture=new Texture2D(2,2,TextureFormat.RGBA32,false,false);if(!texture.LoadImage(copiedSelectionPng)){DestroyImmediate(texture);return;}}
            else if(!StudioImageClipboard.TryRead(out texture,out var error)){status=error;statusType=MessageType.Warning;return;}
            inputSnapshot=CaptureImage();ownedTextures.Add(texture);AddLayer(texture,"Pasted Selection",local?copiedSelectionCenter:Vector2.one*(CanvasSize*.5f));layers[selectedLayer].scale=1;
            SetImageSelection(null,0,new Rect());imageTool=ImageTool.Move;MarkCompositeDirty();inputSnapshot=null;
        }
    }
}
