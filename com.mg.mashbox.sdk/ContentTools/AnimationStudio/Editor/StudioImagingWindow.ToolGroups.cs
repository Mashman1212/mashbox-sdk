using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioImagingWindow
    {
        private enum ImageBrushMode { Paint, Burn, Dodge, Smudge }
        [SerializeField] private ImageBrushMode imageBrushMode;
        private enum ImageShape { Rectangle, RoundedRectangle, Ellipse, Polygon }
        [SerializeField] private ImageShape imageShape;
        [SerializeField] private bool shapeFilled=true;
        [SerializeField] private float shapeStrokeWidth=4,shapeCornerRadius=20;
        [SerializeField] private int shapeSides=5;
        private bool holdingToolGroup;
        private ImageTool heldToolGroup;
        private int toolGroupControl;
        private double toolGroupDeadline;
        private Rect toolGroupScreenRect;
        private Vector2 brushDabDelta;
        private bool HandleImageToolGroup(Rect rect,ImageTool tool,string tooltip)
        {
            if(tool!=ImageTool.Brush && tool!=ImageTool.Shape && tool!=ImageTool.Marquee)return false;
            var e=Event.current;int id=GUIUtility.GetControlID(("Image tool group "+tool).GetHashCode(),FocusType.Passive);
            GUI.Label(rect,new GUIContent("",tooltip+" — hold or right-click for more tools"),GUIStyle.none);
            if(e.type==EventType.MouseDown && rect.Contains(e.mousePosition) && (e.button==0 || e.button==1))
            {
                heldToolGroup=tool;toolGroupScreenRect=new Rect(GUIUtility.GUIToScreenPoint(rect.position),rect.size);
                if(e.button==1){e.Use();EditorApplication.delayCall+=OpenImageToolGroup;}
                else{holdingToolGroup=true;toolGroupControl=id;GUIUtility.hotControl=id;toolGroupDeadline=EditorApplication.timeSinceStartup+.35;e.Use();}
            }
            if(holdingToolGroup && heldToolGroup==tool && e.rawType==EventType.MouseUp)
            {
                holdingToolGroup=false;if(GUIUtility.hotControl==toolGroupControl)GUIUtility.hotControl=0;
                if(rect.Contains(e.mousePosition))imageTool=tool;e.Use();Repaint();
            }
            if(Event.current.type==EventType.Repaint)
                EditorGUI.DrawRect(new Rect(rect.xMax-4,rect.yMax-4,3,2),StudioTheme.Current.Muted);
            return true;
        }
        private void UpdateImageToolHold()
        {
            if(!holdingToolGroup || EditorApplication.timeSinceStartup<toolGroupDeadline)return;
            holdingToolGroup=false;if(GUIUtility.hotControl==toolGroupControl)GUIUtility.hotControl=0;
            OpenImageToolGroup();
        }
        private void OpenImageToolGroup()
        {
            if(!this)return;var menu=new StudioMenu();
            if(heldToolGroup==ImageTool.Marquee)
                foreach(ImageSelectionTool mode in System.Enum.GetValues(typeof(ImageSelectionTool)))
                {var selected=mode;menu.AddItem(new GUIContent(mode==ImageSelectionTool.MagneticLasso?"Magnetic Lasso":mode==ImageSelectionTool.Rectangle?"Rectangular Selection": "Lasso"),selectionTool==mode,()=>ChooseImageSelectionTool(selected));}
            else if(heldToolGroup==ImageTool.Brush)
                foreach(ImageBrushMode mode in System.Enum.GetValues(typeof(ImageBrushMode)))
                {var selected=mode;menu.AddItem(new GUIContent(mode+" Brush"),imageBrushMode==mode,()=>{imageBrushMode=selected;imageTool=ImageTool.Brush;imageToolSettingsExpanded=true;Repaint();});}
            else
                foreach(ImageShape shape in System.Enum.GetValues(typeof(ImageShape)))
                {var selected=shape;menu.AddItem(new GUIContent(shape==ImageShape.RoundedRectangle?"Rounded Rectangle":shape.ToString()),imageShape==shape,()=>{imageShape=selected;imageTool=ImageTool.Shape;imageToolSettingsExpanded=true;Repaint();});}
            StudioMenuPopup.Open(menu,toolGroupScreenRect);
        }
        private bool drawingImageShape;
        private int shapeControl;
        private Vector2 shapeStart,shapeEnd;
        private Rect ImageShapeBounds => Rect.MinMaxRect(Mathf.Min(shapeStart.x,shapeEnd.x),Mathf.Min(shapeStart.y,shapeEnd.y),Mathf.Max(shapeStart.x,shapeEnd.x),Mathf.Max(shapeStart.y,shapeEnd.y));
        private void EndImageShape()
        {if(drawingImageShape && GUIUtility.hotControl==shapeControl)GUIUtility.hotControl=0;drawingImageShape=false;inputSnapshot=null;Repaint();}
        private bool HandleImageShape(Rect rect)
        {
            var e=Event.current;int id=GUIUtility.GetControlID("Image shape".GetHashCode(),FocusType.Passive);
            if(drawingImageShape)
            {
                if(e.type==EventType.KeyDown && e.keyCode==KeyCode.Escape){EndImageShape();e.Use();return true;}
                if(GUIUtility.hotControl!=shapeControl || e.type==EventType.Ignore){EndImageShape();return true;}
                if(e.type==EventType.MouseDrag || e.rawType==EventType.MouseUp)
                {
                    shapeEnd=GuiToCanvas(e.mousePosition,rect);
                    if(e.shift){var d=shapeEnd-shapeStart;float side=Mathf.Max(Mathf.Abs(d.x),Mathf.Abs(d.y));shapeEnd=shapeStart+new Vector2(Mathf.Sign(d.x)*side,Mathf.Sign(d.y)*side);}
                    if(e.rawType==EventType.MouseUp){ApplyImageShape();EndImageShape();}
                    e.Use();Repaint();return true;
                }
                return true;
            }
            if(imageTool!=ImageTool.Shape)return false;
            if(e.type==EventType.MouseDown && e.button==0 && !e.alt && rect.Contains(e.mousePosition) && CanAdjustImage && GUIUtility.hotControl==0)
            {shapeStart=shapeEnd=GuiToCanvas(e.mousePosition,rect);drawingImageShape=true;shapeControl=id;GUIUtility.hotControl=id;e.Use();Repaint();}
            return true;
        }
        private void DrawImageShapeSettings()
        {
            imageShape=(ImageShape)EditorGUILayout.EnumPopup("Shape",imageShape);
            shapeFilled=EditorGUILayout.Toggle("Filled",shapeFilled);
            if(!shapeFilled)shapeStrokeWidth=EditorGUILayout.Slider("Outline width",shapeStrokeWidth,1,100);
            if(imageShape==ImageShape.RoundedRectangle)shapeCornerRadius=EditorGUILayout.Slider("Corner radius",shapeCornerRadius,0,200);
            if(imageShape==ImageShape.Polygon)shapeSides=EditorGUILayout.IntSlider("Sides",shapeSides,3,12);
            EditorGUILayout.LabelField("Drag to draw on the selected layer. Shift makes a square or circle. Uses the toolbar color and marquee. Esc cancels.",StudioTheme.Styles.wordWrappedMiniLabel);
        }
        private Vector2[] ImageShapeVertices(Rect bounds)
        {
            int count=imageShape==ImageShape.Polygon?shapeSides:96;
            var points=new Vector2[count];
            for(int i=0;i<count;i++)
            {float angle=-Mathf.PI*.5f+i*Mathf.PI*2/count;points[i]=bounds.center+Vector2.Scale(new Vector2(Mathf.Cos(angle),Mathf.Sin(angle)),bounds.size*.5f);}
            return points;
        }
        private static float SegmentDistance(Vector2 p,Vector2 a,Vector2 b)
        {var d=b-a;return Vector2.Distance(p,a+d*Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(.0001f,d.sqrMagnitude)));}
        private float ShapeSignedDistance(Vector2 point,Rect bounds,Vector2[] polygon)
        {
            var half=bounds.size*.5f;var local=point-bounds.center;
            if(imageShape==ImageShape.Polygon)
            {
                bool inside=false;float distance=float.MaxValue;
                for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
                {var a=polygon[i];var b=polygon[j];distance=Mathf.Min(distance,SegmentDistance(point,a,b));if((a.y>point.y)!=(b.y>point.y) && point.x<(b.x-a.x)*(point.y-a.y)/(b.y-a.y)+a.x)inside=!inside;}
                return inside?-distance:distance;
            }
            if(imageShape==ImageShape.Ellipse)
            {var p=new Vector2(local.x/half.x,local.y/half.y);return (p.magnitude-1)*Mathf.Min(half.x,half.y);}
            float radius=imageShape==ImageShape.RoundedRectangle?Mathf.Min(shapeCornerRadius,Mathf.Min(half.x,half.y)):0;
            var q=new Vector2(Mathf.Abs(local.x),Mathf.Abs(local.y))-half+Vector2.one*radius;
            return new Vector2(Mathf.Max(q.x,0),Mathf.Max(q.y,0)).magnitude+Mathf.Min(Mathf.Max(q.x,q.y),0)-radius;
        }
        private void DrawImageShapePreview(Rect rect)
        {
            if(!drawingImageShape || Event.current.type!=EventType.Repaint)return;
            var bounds=ImageShapeBounds;if(bounds.width<1 || bounds.height<1)return;
            Vector2[] vertices;
            if(imageShape==ImageShape.Rectangle)vertices=new[]{bounds.min,new Vector2(bounds.xMax,bounds.yMin),bounds.max,new Vector2(bounds.xMin,bounds.yMax)};
            else if(imageShape==ImageShape.RoundedRectangle)
            {
                vertices=new Vector2[64];float radius=Mathf.Min(shapeCornerRadius,Mathf.Min(bounds.width,bounds.height)*.5f);
                for(int corner=0;corner<4;corner++)for(int step=0;step<16;step++)
                {float angle=(corner*90+step*90f/15)*Mathf.Deg2Rad;Vector2 center=new Vector2(corner==0||corner==3?bounds.xMax-radius:bounds.xMin+radius,corner<2?bounds.yMax-radius:bounds.yMin+radius);vertices[corner*16+step]=center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;}
            }
            else vertices=ImageShapeVertices(bounds);
            var lines=new Vector3[vertices.Length+1];for(int i=0;i<vertices.Length;i++)lines[i]=CanvasToGui(vertices[i],rect);lines[vertices.Length]=lines[0];
            DrawImageIconLine(Color.black,3,lines);DrawImageIconLine(Color.white,1,lines);
        }
        private static Color ImageOver(Color source,Color destination,float opacity)
        {float a=source.a*opacity,alpha=a+destination.a*(1-a);if(alpha<=0)return Color.clear;Color c=(source*a+destination*(destination.a*(1-a)))/alpha;c.a=alpha;return c;}
        private void ApplyImageShape()
        {
            if(!CanAdjustImage)return;var bounds=ImageShapeBounds;if(bounds.width<1 || bounds.height<1)return;
            var layer=layers[selectedLayer];var texture=ReadTexture(layer.texture);
            try
            {
                var pixels=texture.GetPixels();var polygon=imageShape==ImageShape.Polygon?ImageShapeVertices(bounds):null;bool changed=false;
                for(int y=0;y<texture.height;y++)for(int x=0;x<texture.width;x++)
                {
                    if(!ImagePixelEditable(layer,x,y))continue;
                    float distance=ShapeSignedDistance(ImagePixelCanvasPosition(layer,x,y),bounds,polygon);
                    float coverage=Mathf.Clamp01(.5f-(shapeFilled?distance:Mathf.Abs(distance)-shapeStrokeWidth*.5f)/Mathf.Max(.001f,layer.scale));
                    if(coverage<=0)continue;int i=y*texture.width+x;var result=ImageChannelDraw(pixels[i],brushColor,coverage);
                    if(result!=pixels[i]){pixels[i]=result;changed=true;}
                }
                if(changed){inputSnapshot=CaptureImage();texture.SetPixels(pixels);texture.Apply(false);ownedTextures.Add(texture);layer.texture=texture;texture=null;MarkCompositeDirty();inputSnapshot=null;}
            }
            finally{if(texture)DestroyImmediate(texture);}
        }
    }
}
