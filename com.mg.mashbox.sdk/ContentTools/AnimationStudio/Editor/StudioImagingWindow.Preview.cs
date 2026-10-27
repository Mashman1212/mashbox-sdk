using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioImagingWindow
    {
        private Texture2D imageSelectionGpuMask;
        private Material imageSelectionOutlineMaterial,imageAdjustmentMaterial;
        private RenderTexture imageSelectionOutlineView,imageAdjustmentPreview;
        private StickerLayer imageAdjustmentLayer;
        private Texture imageAdjustmentSource;
        private bool imageAdjustmentPreviewDirty;

        private void EnsureImageSelectionGpuMask()
        {
            if(selectionMask==null)return;
            if(imageSelectionGpuMask&&!selectionOutlineDirty)return;
            if(imageSelectionGpuMask)DestroyImmediate(imageSelectionGpuMask);
            int size=selectionMaskSize;var pixels=new byte[size*size];
            for(int y=0;y<size;y++)System.Buffer.BlockCopy(selectionMask,y*size,pixels,(size-1-y)*size,size);
            imageSelectionGpuMask=new Texture2D(size,size,TextureFormat.Alpha8,true,true){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            int level=0,width=size,height=size;
            while(true)
            {
                imageSelectionGpuMask.SetPixelData(pixels,level);
                if(width==1&&height==1)break;
                int nextWidth=Mathf.Max(1,width/2),nextHeight=Mathf.Max(1,height/2);var next=new byte[nextWidth*nextHeight];
                // Max mipmaps preserve even a one-pixel selection when the image is zoomed out.
                for(int y=0;y<nextHeight;y++)for(int x=0;x<nextWidth;x++)
                {int x0=x*2,y0=y*2;byte value=0;for(int dy=0;dy<2;dy++)for(int dx=0;dx<2;dx++)value=System.Math.Max(value,pixels[Mathf.Min(height-1,y0+dy)*width+Mathf.Min(width-1,x0+dx)]);next[y*nextWidth+x]=value;}
                pixels=next;width=nextWidth;height=nextHeight;level++;
            }
            imageSelectionGpuMask.Apply(false,true);selectionOutlineDirty=false;
        }
        private void DrawImageMaskOutline(Rect rect)
        {
            EnsureImageSelectionGpuMask();if(!imageSelectionGpuMask)return;
            if(!imageSelectionOutlineMaterial)
            {var shader=Shader.Find("Hidden/MashBox/ImagingSelectionOutline");if(!shader)return;imageSelectionOutlineMaterial=new Material(shader){hideFlags=HideFlags.HideAndDontSave};}
            float dpi=EditorGUIUtility.pixelsPerPoint;
            int width=Mathf.Max(1,Mathf.CeilToInt(canvasArea.width*dpi)),height=Mathf.Max(1,Mathf.CeilToInt(canvasArea.height*dpi));
            if(!imageSelectionOutlineView||imageSelectionOutlineView.width!=width||imageSelectionOutlineView.height!=height)
            {if(imageSelectionOutlineView){imageSelectionOutlineView.Release();DestroyImmediate(imageSelectionOutlineView);}imageSelectionOutlineView=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point};imageSelectionOutlineView.Create();}
            imageSelectionOutlineMaterial.SetVector("_MaskOffset",selectionMaskOffset);
            imageSelectionOutlineMaterial.SetVector("_ImageRect",new Vector4(rect.x,rect.y,rect.width,rect.height));
            imageSelectionOutlineMaterial.SetVector("_ViewSize",new Vector4(canvasArea.width,canvasArea.height,0,0));
            float footprint=Mathf.Max(selectionMaskSize/rect.width,selectionMaskSize/rect.height);
            imageSelectionOutlineMaterial.SetFloat("_Mip",Mathf.Max(0,Mathf.Ceil(Mathf.Log(Mathf.Max(1,footprint),2))));
            var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
            try{GL.sRGBWrite=false;Graphics.Blit(imageSelectionGpuMask,imageSelectionOutlineView,imageSelectionOutlineMaterial);}finally{GL.sRGBWrite=srgb;RenderTexture.active=previous;}
            GUI.DrawTexture(new Rect(0,0,canvasArea.width,canvasArea.height),imageSelectionOutlineView,ScaleMode.StretchToFill,true);
        }
        private void ReleaseImageMaskOutline()
        {
            if(imageSelectionGpuMask)DestroyImmediate(imageSelectionGpuMask);imageSelectionGpuMask=null;
            if(imageSelectionOutlineMaterial)DestroyImmediate(imageSelectionOutlineMaterial);imageSelectionOutlineMaterial=null;
            if(imageSelectionOutlineView){imageSelectionOutlineView.Release();DestroyImmediate(imageSelectionOutlineView);}imageSelectionOutlineView=null;selectionOutlineDirty=true;
        }
        private void RequestImageAdjustmentPreview()
        {if(imageAdjustment!=ImageAdjustment.None){imageAdjustmentPreviewDirty=true;Repaint();}}
        private void StartImageAdjustmentPreview()
        {
            imageAdjustmentLayer=layers[selectedLayer];imageAdjustmentSource=imageAdjustmentLayer.texture;
            imageAdjustmentPreviewDirty=true;RefreshImageAdjustmentPreview();
        }
        private void RefreshImageAdjustmentPreview()
        {
            if(imageAdjustmentLayer==null)return;
            if(!HasSelectedLayer||layers[selectedLayer]!=imageAdjustmentLayer||imageAdjustmentLayer.texture!=imageAdjustmentSource)
            {CancelImageAdjustment();return;}
            if(!imageAdjustmentPreviewDirty)return;
            if(!imageAdjustmentMaterial)
            {var shader=Shader.Find("Hidden/MashBox/ImagingAdjustment");if(!shader)return;imageAdjustmentMaterial=new Material(shader){hideFlags=HideFlags.HideAndDontSave};}
            if(!imageAdjustmentPreview)
            {imageAdjustmentPreview=new RenderTexture(imageAdjustmentSource.width,imageAdjustmentSource.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear};imageAdjustmentPreview.Create();}
            EnsureImageSelectionGpuMask();
            var layer=imageAdjustmentLayer;var crop=ClampSourceRect(layer.sourceRect);
            imageAdjustmentMaterial.SetFloat("_Adjustment",(int)imageAdjustment);
            imageAdjustmentMaterial.SetFloat("_Channel",(int)imageChannel);
            imageAdjustmentMaterial.SetVector("_Hue",new Vector4(adjustmentHue,adjustmentSaturation,adjustmentLightness,0));
            imageAdjustmentMaterial.SetVector("_Levels",new Vector4(levelsBlack,levelsWhite,levelsGamma,0));
            imageAdjustmentMaterial.SetVector("_Output",new Vector4(levelsOutputBlack,levelsOutputWhite,adjustmentShadows/100,adjustmentHighlights/100));
            imageAdjustmentMaterial.SetVector("_Crop",new Vector4(crop.x,crop.y,crop.width,crop.height));
            imageAdjustmentMaterial.SetVector("_Layer",new Vector4(layer.position.x,layer.position.y,layer.scale,layer.rotation*Mathf.Deg2Rad));
            imageAdjustmentMaterial.SetVector("_SourceSize",new Vector4(layer.texture.width,layer.texture.height,CanvasSize,0));
            imageAdjustmentMaterial.SetVector("_MaskOffset",selectionMaskOffset);
            imageAdjustmentMaterial.SetVector("_Selection",new Vector4(imageSelection.xMin,imageSelection.yMin,imageSelection.xMax,imageSelection.yMax));
            imageAdjustmentMaterial.SetFloat("_HasSelection",hasImageSelection?1:0);
            imageAdjustmentMaterial.SetFloat("_UseMask",selectionMask!=null?1:0);
            imageAdjustmentMaterial.SetTexture("_SelectionMask",imageSelectionGpuMask);
            for(int i=0;i<4;i++){var q=ImageCorner(layer,i);imageAdjustmentMaterial.SetVector("_Corner"+i,new Vector4(q.x,q.y,0,0));}
            var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
            try{GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear;Graphics.Blit(imageAdjustmentSource,imageAdjustmentPreview,imageAdjustmentMaterial);}finally{GL.sRGBWrite=srgb;RenderTexture.active=previous;}
            imageAdjustmentPreviewDirty=false;compositeDirty=true;imageChannelDirty=true;
        }
        private void CancelImageAdjustment()
        {
            imageAdjustment=ImageAdjustment.None;imageAdjustmentLayer=null;imageAdjustmentSource=null;imageAdjustmentPreviewDirty=false;
            if(imageAdjustmentPreview){imageAdjustmentPreview.Release();DestroyImmediate(imageAdjustmentPreview);}imageAdjustmentPreview=null;
            compositeDirty=true;imageChannelDirty=true;Repaint();
        }
        private void ReleaseImageAdjustmentResources()
        {CancelImageAdjustment();if(imageAdjustmentMaterial)DestroyImmediate(imageAdjustmentMaterial);imageAdjustmentMaterial=null;}
        private Texture ImageLayerDisplayTexture(StickerLayer layer)=>layer==imageAdjustmentLayer&&imageAdjustmentPreview?imageAdjustmentPreview:layer.texture;
    }
}
