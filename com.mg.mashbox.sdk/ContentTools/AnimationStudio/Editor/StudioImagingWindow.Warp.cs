using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioImagingWindow
    {
        private int imageTransformCorner;
        private Vector2[] imageTransformOffsets;
        private Vector2Int[] imageBrushRows;

        // Offsets are relative to the cropped image size, before layer rotation/scale.
        private static bool ImageIsWarped(StickerLayer layer) => layer.corner0 != Vector2.zero || layer.corner1 != Vector2.zero || layer.corner2 != Vector2.zero || layer.corner3 != Vector2.zero;
        private static Vector2 ImageCorner(StickerLayer layer,int corner)
        {
            switch(corner)
            {
                case 0:return layer.corner0;
                case 1:return Vector2.right+layer.corner1;
                case 2:return Vector2.one+layer.corner2;
                default:return Vector2.up+layer.corner3;
            }
        }
        private static float ImageCross(Vector2 a,Vector2 b) => a.x*b.y-a.y*b.x;
        private static Vector2 ImageQuadPoint(StickerLayer layer,Vector2 uv) => Vector2.LerpUnclamped(Vector2.LerpUnclamped(ImageCorner(layer,0),ImageCorner(layer,1),uv.x),Vector2.LerpUnclamped(ImageCorner(layer,3),ImageCorner(layer,2),uv.x),uv.y);
        private static Vector2 ImageUVToCanvas(StickerLayer layer,Vector2 uv) => layer.position+Rotate(Vector2.Scale(ImageQuadPoint(layer,uv)-Vector2.one*.5f,CroppedPixelSize(layer))*layer.scale,layer.rotation);
        private static bool ImageCanvasToUV(StickerLayer layer,Vector2 point,out Vector2 uv)
        {
            var local=Rotate(point-layer.position,-layer.rotation)/layer.scale;
            var size=CroppedPixelSize(layer);
            var target=new Vector2(local.x/size.x+.5f,local.y/size.y+.5f);
            uv=target;
            if(!ImageIsWarped(layer))return true;
            var a=ImageCorner(layer,0);var b=ImageCorner(layer,1)-a;var c=ImageCorner(layer,3)-a;
            var d=a-ImageCorner(layer,1)+ImageCorner(layer,2)-ImageCorner(layer,3);
            uv=Vector2.one*.5f;
            for(int i=0;i<12;i++)
            {
                var error=a+b*uv.x+c*uv.y+d*(uv.x*uv.y)-target;
                if(error.sqrMagnitude<1e-12f)return true;
                var du=b+d*uv.y;var dv=c+d*uv.x;float det=ImageCross(du,dv);
                if(Mathf.Abs(det)<1e-8f)return false;
                uv-=new Vector2(ImageCross(error,dv),ImageCross(du,error))/det;
            }
            return (ImageQuadPoint(layer,uv)-target).sqrMagnitude<1e-8f;
        }
        private static void SetImageCorners(StickerLayer layer,Vector2[] offsets)
        {layer.corner0=offsets[0];layer.corner1=offsets[1];layer.corner2=offsets[2];layer.corner3=offsets[3];}
        private void ApplyImageCornerTransform(Vector2 point,bool skew)
        {
            var layer=imageTransformLayer;
            if(imageTransformOffsets==null)return;
            layer.scale=imageTransformScale;
            var size=CroppedPixelSize(layer);
            var local=Rotate(point-imageTransformStart,-imageTransformRotation)/imageTransformScale;
            var delta=new Vector2(local.x/size.x,local.y/size.y);
            var offsets=(Vector2[])imageTransformOffsets.Clone();
            if(skew)
            {
                // Slide the grabbed edge along its neighbour; the opposite edge stays fixed.
                if(Mathf.Abs(local.x)>=Mathf.Abs(local.y))
                {int first=imageTransformCorner<2?0:2;offsets[first].x+=delta.x;offsets[first+1].x+=delta.x;}
                else
                {int first=imageTransformCorner==0||imageTransformCorner==3?0:1;int second=first==0?3:2;offsets[first].y+=delta.y;offsets[second].y+=delta.y;}
            }
            else offsets[imageTransformCorner]+=delta;
            var previous=new[]{layer.corner0,layer.corner1,layer.corner2,layer.corner3};
            SetImageCorners(layer,offsets);
            // A folded/collapsed quad has no unique inverse for painting or texture sampling.
            for(int i=0;i<4;i++)
                if(ImageCross(ImageCorner(layer,(i+1)%4)-ImageCorner(layer,i),ImageCorner(layer,(i+2)%4)-ImageCorner(layer,(i+1)%4))<.001f)
                {SetImageCorners(layer,previous);return;}
        }
        private static Vector2 ImageCanvasToPixel(StickerLayer layer,Vector2 point)
        {
            ImageCanvasToUV(layer,point,out var uv);var crop=ClampSourceRect(layer.sourceRect);
            return new Vector2((crop.x+uv.x*crop.width)*layer.texture.width,(1-crop.y-uv.y*crop.height)*layer.texture.height);
        }
        private void BuildImageBrushRows(StickerLayer layer,Vector2 point,int x0,int x1,int y0,int y1)
        {
            if(imageBrushRows==null || imageBrushRows.Length<layer.texture.height)imageBrushRows=new Vector2Int[layer.texture.height];
            // A bilinear quad is linear along each source row. Intersect that line with the brush circle.
            for(int y=y0;y<=y1;y++)imageBrushRows[y]=ImageBrushRow(layer,point,y,x0,x1);
        }
        private Vector2Int ImageBrushRow(StickerLayer layer,Vector2 point,int y,int x0,int x1)
        {
            var origin=ImagePixelCanvasPosition(layer,0,y);var step=ImagePixelCanvasPosition(layer,1,y)-origin;
            var offset=origin-point;float aa=Vector2.Dot(step,step),bb=Vector2.Dot(offset,step);
            float disc=bb*bb-aa*(offset.sqrMagnitude-brushRadius*brushRadius);
            if(aa<1e-12f || disc<0)return new Vector2Int(1,0);
            float root=Mathf.Sqrt(disc);
            return new Vector2Int(Mathf.Max(x0,Mathf.FloorToInt((-bb-root)/aa)),Mathf.Min(x1,Mathf.CeilToInt((-bb+root)/aa)));
        }
        private RectInt ImageBrushSourceBounds(StickerLayer layer,Vector2 point,int x0,int x1,int y0,int y1)
        {
            int left=x1+1,right=x0-1,top=y1+1,bottom=y0-1;
            for(int y=y0;y<=y1;y++)
            {var row=ImageBrushRow(layer,point,y,x0,x1);if(row.x>row.y)continue;left=Mathf.Min(left,row.x);right=Mathf.Max(right,row.y);top=Mathf.Min(top,y);bottom=Mathf.Max(bottom,y);}
            if(right<left)return new RectInt();
            left=Mathf.Max(x0,left-1);right=Mathf.Min(x1,right+1);top=Mathf.Max(y0,top-1);bottom=Mathf.Min(y1,bottom+1);
            return new RectInt(left,top,right-left+1,bottom-top+1);
        }
    }
}
