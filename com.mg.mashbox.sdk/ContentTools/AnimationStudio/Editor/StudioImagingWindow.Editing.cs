using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioImagingWindow
    {
        private enum ImageTool { Move, Brush, Eraser, Eyedropper, Transform, Marquee, Gradient, Shape, Text }
        [SerializeField] private ImageTool imageTool;
        [SerializeField] private Color brushColor=Color.white;
        [SerializeField] private float brushRadius=20, brushOpacity=1, zoom=1;
        [SerializeField] private Vector2 pan;
        [SerializeField] private string documentPath, session;
        [SerializeField] private Material directMaterial;
        private readonly List<string> undoImages=new List<string>(), redoImages=new List<string>();
        private readonly Dictionary<Texture2D,string> encodedImages=new Dictionary<Texture2D,string>();
        private string inputSnapshot;
        private bool restoring, pendingExport, painting, panning;
        private Texture2D paintTexture;
        private Vector2 lastPaint;
        private int strokeControl;
        private Rect canvasArea;

        [Serializable] private sealed class ImageFile
        {
            public int version=1, resolution, selected;
            public Color background;
            public bool hasSelection;
            public Rect selection;
            public string mask;
            public int maskSize;
            public Vector2 maskOffset;
            public List<ImageLayerFile> layers=new List<ImageLayerFile>();
        }
        [Serializable] private sealed class ImageLayerFile
        {
            public string settings, png;
        }

        private readonly StudioShortcutFocusGuard imageShortcutFocus = new StudioShortcutFocusGuard();
        private void OnFocus() => imageShortcutFocus.Acquire();
        private void OnLostFocus() { FinishImageUvGesture(false); CancelImageUvWheel(); FinishImageLayerRename(true);FinishInlineImageText(true); marqueeSpaceHeld=false;marqueeSnapHeld=false; RestoreImageSelectionCursor(); holdingToolGroup=false; EndImageShape(); EndImageGradient(); CancelSelectionTrace(); EndImageMarquee(); EndImageTransform(); EndImageBrushAdjustment(); imageShortcutFocus.Release(); }

        private void ShieldImageHistory(UnityEngine.UIElements.KeyDownEvent evt)
        {
            if ((!evt.ctrlKey && !evt.commandKey) || evt.altKey ||
                (evt.keyCode != KeyCode.Z && evt.keyCode != KeyCode.Y)) return;
            bool redo = evt.keyCode == KeyCode.Y || evt.shiftKey;
            // Claim the shortcut even with empty image history. It must never undo scene edits.
            evt.imguiEvent?.Use();
            evt.StopImmediatePropagation();
            EditorApplication.delayCall += () =>
            {
                if (!this) return;
                rootVisualElement.focusController?.focusedElement?.Blur();
                EditorGUIUtility.editingTextField = false;
                if (imageUvMode != ImageUvMode.Paint) ImageUvHistory(redo); else ImageUndo(redo);
                Repaint();
            };
        }

        private void EnableImaging()
        {
            rootVisualElement.RegisterCallback<UnityEngine.UIElements.KeyDownEvent>(ShieldImageUvTools, UnityEngine.UIElements.TrickleDown.TrickleDown);
            rootVisualElement.RegisterCallback<UnityEngine.UIElements.KeyDownEvent>(ShieldImageHistory, UnityEngine.UIElements.TrickleDown.TrickleDown);
            if (focusedWindow == this) imageShortcutFocus.Acquire();
            AssemblyReloadEvents.beforeAssemblyReload+=RestoreLivePreview;
            EditorApplication.playModeStateChanged+=PlayModeChanged;
            EditorApplication.projectChanged+=InvalidateImageCache;
            if(!string.IsNullOrEmpty(session))
            {
                try { RestoreImage(session); }
                catch(Exception e){status="Could not restore image session: "+e.Message;statusType=MessageType.Error;}
            }
            saveChangesMessage="Save the layered image document before closing?";
        }
        private void DisableImaging()
        {
            rootVisualElement.UnregisterCallback<UnityEngine.UIElements.KeyDownEvent>(ShieldImageUvTools, UnityEngine.UIElements.TrickleDown.TrickleDown);
            FinishImageLayerRename(true);FinishInlineImageText(true);marqueeSpaceHeld=false;marqueeSnapHeld=false;ReleaseImageTextResources();ReleaseImageAdjustmentResources();ReleaseImageChannels();
            rootVisualElement.UnregisterCallback<UnityEngine.UIElements.KeyDownEvent>(ShieldImageHistory, UnityEngine.UIElements.TrickleDown.TrickleDown);
            holdingToolGroup=false;EndImageShape();EndImageGradient();EndImageMarquee();
            ReleaseImageSelectionCursor();CancelImageColorRange();CancelSelectionTrace();ReleaseImageSelectionOutline();
            EndImageTransform();
            EndImageBrushAdjustment();
            imageShortcutFocus.Release();
            try { session=CaptureImage(); } catch(Exception e){Debug.LogWarning("Image session: "+e.Message);}
            AssemblyReloadEvents.beforeAssemblyReload-=RestoreLivePreview;
            EditorApplication.playModeStateChanged-=PlayModeChanged;
            EditorApplication.projectChanged-=InvalidateImageCache;
        }
        private void InvalidateImageCache(){imageChannelDirty=true;encodedImages.Clear();compositeDirty=true;Repaint();}
        private void PlayModeChanged(PlayModeStateChange change)
        {
            RestoreLivePreview();
            if(change==PlayModeStateChange.EnteredEditMode || change==PlayModeStateChange.EnteredPlayMode) compositeDirty=true;
        }
        public override void SaveChanges(){if(SaveAllImageUvMeshes() && SaveImageDocument()) base.SaveChanges();}
        public override void DiscardChanges(){base.DiscardChanges();}

        private void DrawImageMenu(string title, System.Func<StudioMenu> build, ref float x, float y)
        {
            var content=new GUIContent(title);
            var style=StudioTheme.Styles.menuBarButton;
            var rect=new Rect(x,y,style.CalcSize(content).x+4,21);
            x+=rect.width;
            if(StudioMenu.MenuBarButton(this,rect,content))build().DropDown(rect,this,title);
        }
        private void AddImageMenuItem(StudioMenu menu,string label,System.Action action,bool enabled=true,bool selected=false)
        {
            if(!enabled){menu.AddDisabledItem(new GUIContent(label));return;}
            menu.AddItem(new GUIContent(label),selected,()=>
            {
                if(!this)return;
                inputSnapshot=CaptureImage();
                try{action();}finally{inputSnapshot=null;Repaint();}
            });
        }
        private StudioMenu BuildImageFileMenu()
        {
            var menu=new StudioMenu();
            AddImageMenuItem(menu,"New",NewDocument);
            AddImageMenuItem(menu,"Open Layered Image…",OpenImageDocument);
            AddImageMenuItem(menu,"Save Layers",()=>SaveImageDocument());
            menu.AddSeparator("");
            AddImageMenuItem(menu,"Import Image…",AddExternalFiles);
            menu.AddSeparator("");
            AddImageMenuItem(menu,string.IsNullOrEmpty(outputAssetPath)?"Export PNG…":"Update PNG",()=>SaveComposite(true));
            AddImageMenuItem(menu,"Export PNG and Assign to Material…",()=>{if(SaveComposite(true))AssignSavedTexture();},CurrentMaterial && CurrentTextureProperty!=null);
            AddImageMenuItem(menu,"Auto-update PNG",()=>autoSave=!autoSave,true,autoSave);
            return menu;
        }
        private StudioMenu BuildImageEditMenu()
        {
            var menu=new StudioMenu();
            AddImageMenuItem(menu,"Undo",()=>ImageUndo(false),undoImages.Count>0);
            AddImageMenuItem(menu,"Redo",()=>ImageUndo(true),redoImages.Count>0);
            menu.AddSeparator("");
            AddImageMenuItem(menu,"Copy\tCtrl+C",()=>CopyImageSelection(),HasSelectedLayer);
            AddImageMenuItem(menu,"Paste\tCtrl+V",PasteImageSelection);
            menu.AddSeparator("");
            AddImageMenuItem(menu,"Preferences…",StudioSettingsWindow.Open);
            return menu;
        }
        private StudioMenu BuildImageImageMenu()
        {
            var menu=new StudioMenu();
            AddImageMenuItem(menu,"Invert\tCtrl+I",()=>ApplyImagePixelCommand(ImagePixelCommand.Invert),CanAdjustImage);
            AddImageMenuItem(menu,"White to Alpha",()=>ApplyImagePixelCommand(ImagePixelCommand.WhiteToAlpha),CanAdjustImage);
            AddImageMenuItem(menu,"Black to Alpha",()=>ApplyImagePixelCommand(ImagePixelCommand.BlackToAlpha),CanAdjustImage);
            menu.AddSeparator("");
            AddImageMenuItem(menu,"Adjustments/Hue and Saturation…\tCtrl+U",()=>BeginImageAdjustment(ImageAdjustment.HueSaturation),CanAdjustImage);
            AddImageMenuItem(menu,"Adjustments/Levels…\tCtrl+L",()=>BeginImageAdjustment(ImageAdjustment.Levels),CanAdjustImage);
            AddImageMenuItem(menu,"Adjustments/Shadows and Highlights…",()=>BeginImageAdjustment(ImageAdjustment.ShadowsHighlights),CanAdjustImage);
            menu.AddSeparator("");
            for(int i=0;i<CanvasResolutions.Length;i++)
            {
                int index=i;
                AddImageMenuItem(menu,"Canvas Resolution/"+CanvasResolutionLabels[i],()=>ChangeResolution(index),true,index==resolutionIndex);
            }
            menu.AddSeparator("");
            AddImageMenuItem(menu,"Edit Material Image as Base Layer",()=>OpenCurrentStickerMap(true,false),GetCurrentStickerTexture());
            AddImageMenuItem(menu,"Add Material Image as Layer",()=>OpenCurrentStickerMap(false,false),GetCurrentStickerTexture());
            return menu;
        }
        private StudioMenu BuildImageLayerMenu()
        {
            var menu=new StudioMenu();
            AddImageMenuItem(menu,"New Paint Layer",AddPaintLayer);
            AddImageMenuItem(menu,"New Text Layer",()=>CreateImageTextLayer(Vector2.one*(CanvasSize*.5f)));
            AddImageMenuItem(menu,"Rasterize Text",RasterizeImageText,HasSelectedLayer&&layers[selectedLayer].isText&&!layers[selectedLayer].locked);
            AddImageMenuItem(menu,"Duplicate Layer",DuplicateSelectedLayer,HasSelectedLayer);
            AddImageMenuItem(menu,"Delete Layer\tDelete",DeleteSelectedLayer,HasSelectedLayer && !layers[selectedLayer].locked);
            menu.AddSeparator("");
            AddImageMenuItem(menu,"Move Up",()=>MoveLayer(1),HasSelectedLayer && selectedLayer<layers.Count-1);
            AddImageMenuItem(menu,"Move Down",()=>MoveLayer(-1),HasSelectedLayer && selectedLayer>0);
            return menu;
        }
        private StudioMenu BuildImageViewMenu()
        {
            var menu=new StudioMenu();
            foreach(var palette in StudioTheme.Palettes)
            {
                var selectedPalette=palette;
                AddImageMenuItem(menu,"Theme/"+palette.Name,()=>{StudioSettings.Current.theme=selectedPalette.Id;StudioSettings.Save();},true,StudioSettings.Current.theme==palette.Id);
            }
            AddImageMenuItem(menu,"Theme/Custom",()=>{StudioSettings.Current.theme="custom";StudioSettings.Save();},true,StudioSettings.Current.theme=="custom");
            AddImageMenuItem(menu,"Theme/Customize…",StudioSettingsWindow.Open);
            menu.AddSeparator("");
            AddImageMenuItem(menu,"Fit Canvas",()=>{zoom=1;pan=Vector2.zero;});
            AddImageMenuItem(menu,"Material / Live Link Panel",()=>showImageLiveLink=!showImageLiveLink,true,showImageLiveLink);
            return menu;
        }

        // GUI-only strokes avoid the SceneView Handles camera/depth state leaking into toolbar icons.
        private static void DrawImageIconLine(Color color,float width,params Vector3[] points)
        {
            var matrix=GUI.matrix;
            try
            {
                for(int i=1;i<points.Length;i++)
                {
                    Vector2 start=points[i-1],delta=points[i]-points[i-1];
                    GUI.matrix=matrix;
                    GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,start);
                    EditorGUI.DrawRect(new Rect(start.x,start.y-width*.5f,delta.magnitude,width),color);
                }
            }
            finally{GUI.matrix=matrix;}
        }

        private readonly Vector3[] imageCirclePoints=new Vector3[97];
        private void DrawImageGuiCircle(Vector2 center,float radius,Color color)
        {
            for(int i=0;i<imageCirclePoints.Length;i++)
            {
                float angle=i*Mathf.PI*2/(imageCirclePoints.Length-1);
                imageCirclePoints[i]=center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
            }
            DrawImageIconLine(color,1.5f,imageCirclePoints);
        }

        private void DrawImageToolRail()
        {
            var railRect=GUILayoutUtility.GetRect(36,298,GUILayout.Width(36),GUILayout.ExpandHeight(true));
            {
                if(Event.current.type==EventType.Repaint)
                {
                    EditorGUI.DrawRect(railRect,StudioTheme.Current.ToolRail);
                    EditorGUI.DrawRect(new Rect(railRect.xMax-1,railRect.y,1,railRect.height),StudioTheme.Current.Border);
                    EditorGUI.DrawRect(new Rect(railRect.center.x-8,railRect.y+5,16,1),StudioTheme.Current.Border);
                    EditorGUI.DrawRect(new Rect(railRect.center.x-8,railRect.y+8,16,1),StudioTheme.Current.Border);
                }
                int slot=0;
                foreach(var tool in new[]{ImageTool.Move,ImageTool.Transform,ImageTool.Marquee,ImageTool.Shape,ImageTool.Text,ImageTool.Gradient,ImageTool.Brush,ImageTool.Eraser,ImageTool.Eyedropper})
                {
                    var r=new Rect(railRect.x+4,railRect.y+14+slot++*30,28,28);
                    string tip=tool==ImageTool.Text?"Text (T) — click to add or edit text":tool==ImageTool.Shape?"Shape (U)":tool==ImageTool.Gradient?"Gradient (G) — linear or radial":tool==ImageTool.Marquee?selectionTool==ImageSelectionTool.Rectangle?"Rectangular Selection (M) — Shift: snap; Space while drawing: move; drag inside selection to reposition":selectionTool==ImageSelectionTool.Lasso?"Lasso (L)":"Magnetic Lasso":tool==ImageTool.Move?"Move (V)":tool==ImageTool.Transform?"Transform (Ctrl+T): drag to move; corners scale; Shift+corner unlocks aspect ratio; Ctrl+corner distorts; circle rotates":tool==ImageTool.Brush?imageBrushMode+" Brush (B)":tool==ImageTool.Eraser?"Erase (E)":"Sample color (I)";
                    if(!HandleImageToolGroup(r,tool,tip) && GUI.Button(r,new GUIContent("",tip),GUIStyle.none)){imageTool=tool;Repaint();}
                    if(Event.current.type!=EventType.Repaint)continue;
                    bool selected=imageTool==tool;
                    bool hovered=r.Contains(Event.current.mousePosition);
                    if(selected || hovered)
                        EditorGUI.DrawRect(r,selected?StudioTheme.Current.ToolSelection:StudioTheme.Current.Hover);
                    var iconColor=StudioTheme.Current.Text;
                    Vector3 c=r.center;
                    if(tool==ImageTool.Move){DrawImageIconLine(iconColor,2,c+new Vector3(-11,0,0),c+new Vector3(11,0,0));DrawImageIconLine(iconColor,2,c+new Vector3(0,-11,0),c+new Vector3(0,11,0));
                        foreach(var d in new[]{Vector3.left,Vector3.right,Vector3.up,Vector3.down}){var n=new Vector3(-d.y,d.x);DrawImageIconLine(iconColor,2,c+d*7+n*3,c+d*11,c+d*7-n*3);}}
                    else if(tool==ImageTool.Text){DrawImageIconLine(iconColor,2,c+new Vector3(-8,-9),c+new Vector3(8,-9));DrawImageIconLine(iconColor,2,c+new Vector3(0,-9),c+new Vector3(0,9));DrawImageIconLine(iconColor,2,c+new Vector3(-4,9),c+new Vector3(4,9));}
                    else if(tool==ImageTool.Shape){EditorGUI.DrawRect(new Rect(c.x-8,c.y-7,16,14),iconColor);}
                    else if(tool==ImageTool.Gradient){for(int stripe=0;stripe<18;stripe++)EditorGUI.DrawRect(new Rect(c.x-9+stripe,c.y-7,1,14),Color.Lerp(StudioTheme.Current.ToolRail,iconColor,stripe/17f));}
                    else if(tool==ImageTool.Marquee)
                    {
                        if(selectionTool==ImageSelectionTool.Rectangle){for(int k=-9;k<9;k+=6){EditorGUI.DrawRect(new Rect(c.x+k,c.y-9,3,1),iconColor);EditorGUI.DrawRect(new Rect(c.x+k,c.y+9,3,1),iconColor);EditorGUI.DrawRect(new Rect(c.x-9,c.y+k,1,3),iconColor);EditorGUI.DrawRect(new Rect(c.x+9,c.y+k,1,3),iconColor);}}
                        else{DrawImageIconLine(iconColor,1.5f,c+new Vector3(-8,-5),c+new Vector3(-3,-9),c+new Vector3(6,-7),c+new Vector3(9,0),c+new Vector3(4,6),c+new Vector3(-5,6),c+new Vector3(-8,-5),c+new Vector3(-3,10));if(selectionTool==ImageSelectionTool.MagneticLasso)DrawImageIconLine(iconColor,2,c+new Vector3(4,5),c+new Vector3(4,11),c+new Vector3(10,11),c+new Vector3(10,5));}
                    }
                    else if(tool==ImageTool.Transform){DrawImageIconLine(iconColor,2,c+new Vector3(-9,-9,0),c+new Vector3(9,-9,0),c+new Vector3(9,9,0),c+new Vector3(-9,9,0),c+new Vector3(-9,-9,0));foreach(var d in new[]{new Vector2(-9,-9),new Vector2(9,-9),new Vector2(9,9),new Vector2(-9,9)})EditorGUI.DrawRect(new Rect(r.center+d-Vector2.one*2,Vector2.one*4),iconColor);}
                    else if(tool==ImageTool.Eraser)DrawImageIconLine(iconColor,2,c+new Vector3(-11,3,0),c+new Vector3(1,-9,0),c+new Vector3(11,1,0),c+new Vector3(3,9,0),c+new Vector3(-5,9,0),c+new Vector3(-11,3,0),c+new Vector3(-4,-4,0),c+new Vector3(6,6,0));
                    else {DrawImageIconLine(iconColor,3,c+new Vector3(-6,6,0),c+new Vector3(8,-8,0));if(tool==ImageTool.Brush)DrawImageIconLine(iconColor,2,c+new Vector3(-7,3,0),c+new Vector3(-11,11,0),c+new Vector3(-3,7,0));else DrawImageIconLine(iconColor,3,c+new Vector3(2,-10,0),c+new Vector3(10,-2,0));}

                    if(tool==ImageTool.Brush || tool==ImageTool.Shape || tool==ImageTool.Marquee)EditorGUI.DrawRect(new Rect(r.xMax-4,r.yMax-4,3,2),iconColor);
                }

                float swatchY=railRect.y+14+slot*30+10;
                if(Event.current.type==EventType.Repaint)
                    EditorGUI.DrawRect(new Rect(railRect.x+4,swatchY-6,28,1),StudioTheme.Current.Border);
                var swatch=new Rect(railRect.x+4,swatchY,28,28);
                brushColor=EditorGUI.ColorField(swatch,new GUIContent("","Brush color — click to choose"),brushColor,false,true,false);
            }
        }
        private int imageTransformMode, imageTransformControl;
        private Vector2 imageTransformStart, imageTransformPosition;
        private float imageTransformScale, imageTransformRotation;
        private StickerLayer imageTransformLayer;
        private Vector2[] ImageTransformPoints(Rect rect,StickerLayer layer)
        {
            var points=new Vector2[5];
            var uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
            for(int i=0;i<4;i++)points[i]=CanvasToGui(ImageUVToCanvas(layer,uv[i]),rect);
            var edge=(points[1]-points[0]).normalized;
            points[4]=(points[0]+points[1])*.5f+new Vector2(edge.y,-edge.x)*28;
            return points;
        }
        private void DrawImageTransformHandles(Rect rect)
        {
            if(imageTool!=ImageTool.Transform || !HasSelectedLayer || layers[selectedLayer].locked || Event.current.type!=EventType.Repaint)return;
            var points=ImageTransformPoints(rect,layers[selectedLayer]);
            var color=StudioTheme.Current.Accent;
            DrawImageIconLine(color,2,(points[0]+points[1])*.5f,points[4]);
            DrawImageGuiCircle(points[4],6,color);
            for(int i=0;i<4;i++)EditorGUI.DrawRect(new Rect(points[i]-Vector2.one*4,Vector2.one*8),color);
        }
        private bool HandleImageTransform(Rect rect)
        {
            var e=Event.current;int id=GUIUtility.GetControlID("Image transform".GetHashCode(),FocusType.Passive);
            if(imageTransformMode!=0)
            {
                if(e.type==EventType.KeyDown && e.keyCode==KeyCode.Escape)
                {imageTransformLayer.position=imageTransformPosition;imageTransformLayer.scale=imageTransformScale;imageTransformLayer.rotation=imageTransformRotation;if(imageTransformOffsets!=null)SetImageCorners(imageTransformLayer,imageTransformOffsets);MarkCompositeDirty();EndImageTransform();e.Use();return true;}
                if(e.rawType==EventType.MouseUp || e.type==EventType.Ignore || GUIUtility.hotControl!=imageTransformControl)
                {EndImageTransform();if(e.isMouse)e.Use();return true;}
                if(e.type==EventType.MouseDrag)
                {
                    ApplyImageTransform(GuiToCanvas(e.mousePosition,rect),e.shift,e.control||e.command);
                    MarkCompositeDirty();e.Use();
                }
                return true;
            }
            if(imageTool!=ImageTool.Transform || !HasSelectedLayer || layers[selectedLayer].locked || e.type!=EventType.MouseDown || e.button!=0 || e.alt || GUIUtility.hotControl!=0)return false;
            if(!new Rect(0,0,canvasArea.width,canvasArea.height).Contains(e.mousePosition))return false;
            var layer=layers[selectedLayer];var points=ImageTransformPoints(rect,layer);int mode=0;
            if(Vector2.Distance(e.mousePosition,points[4])<10)mode=3;
            else for(int i=0;i<4;i++)if(Vector2.Distance(e.mousePosition,points[i])<10){mode=2;imageTransformCorner=i;break;}
            if(mode==0 && HitTestLayers(GuiToCanvas(e.mousePosition,rect))==selectedLayer)mode=1;
            if(mode==0)return false;
            imageTransformLayer=layer;imageTransformPosition=layer.position;imageTransformScale=layer.scale;imageTransformRotation=layer.rotation;
            imageTransformOffsets=new[]{layer.corner0,layer.corner1,layer.corner2,layer.corner3};
            imageTransformStart=GuiToCanvas(e.mousePosition,rect);imageTransformMode=mode;imageTransformControl=id;
            GUIUtility.hotControl=id;GUIUtility.keyboardControl=0;e.Use();return true;
        }
        private void ApplyImageTransform(Vector2 point,bool snap,bool skew=false)
        {
            if(imageTransformMode==1)imageTransformLayer.position=imageTransformPosition+point-imageTransformStart;
            else if(imageTransformMode==2)
            {
                if(skew)ApplyImageCornerTransform(point,false);
                else if(snap)ApplyImageAspectTransform(point);
                else
                {
                    if(imageTransformOffsets!=null)SetImageCorners(imageTransformLayer,imageTransformOffsets);
                    imageTransformLayer.scale=Mathf.Clamp(imageTransformScale*Vector2.Distance(point,imageTransformPosition)/Mathf.Max(.001f,Vector2.Distance(imageTransformStart,imageTransformPosition)),.001f,100);
                }
            }
            else if(imageTransformMode==3)
            {float angle=imageTransformRotation+Vector2.SignedAngle(imageTransformStart-imageTransformPosition,point-imageTransformPosition);imageTransformLayer.rotation=snap?Mathf.Round(angle/15)*15:angle;}
        }
        private void EndImageTransform()
        {if(imageTransformMode==0)return;if(GUIUtility.hotControl==imageTransformControl)GUIUtility.hotControl=0;imageTransformMode=0;imageTransformLayer=null;inputSnapshot=null;Repaint();}

        private void DrawImageToolbar()
        {
            var bar=GUILayoutUtility.GetRect(0,24,GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bar,StudioTheme.Current.Button);
            EditorGUI.DrawRect(new Rect(bar.x,bar.yMax-3,bar.width,3),StudioTheme.Current.Header);
            GUI.Label(new Rect(bar.x+4,bar.y,128,21),new GUIContent(" 2.5D Imaging",EditorGUIUtility.IconContent("Texture2D Icon").image),StudioTheme.Styles.menuBarButton);
            float menuX=bar.x+152;
            DrawImageMenu("File", BuildImageFileMenu, ref menuX,bar.y);
            DrawImageMenu("Edit", BuildImageEditMenu, ref menuX,bar.y);
            DrawImageMenu("Image", BuildImageImageMenu, ref menuX,bar.y);
            DrawImageMenu("Select", BuildImageSelectMenu, ref menuX,bar.y);
            DrawImageMenu("Layer", BuildImageLayerMenu, ref menuX,bar.y);
            DrawImageMenu("View", BuildImageViewMenu, ref menuX,bar.y);
        }

        private bool imageToolSettingsExpanded=true;
        private bool DrawImageSettingsFoldout(string title,ref bool expanded,string preference)
        {
            bool next=EditorGUILayout.Foldout(expanded,title,true,StudioTheme.Styles.transformHeader);
            if(next!=expanded)
            {
                expanded=next;StudioPreferenceStore.SetFloat(preference,expanded?1:0);Repaint();
            }
            return expanded;
        }
        private void DrawImageToolSettings()
        {
            if(!DrawImageSettingsFoldout("TOOL SETTINGS",ref imageToolSettingsExpanded,"Imaging.ToolSettingsExpanded"))return;
            if(imageTool==ImageTool.Text){DrawImageTextSettings();return;}
            if(imageTool==ImageTool.Marquee)
            {
                EditorGUI.BeginChangeCheck();var tool=(ImageSelectionTool)EditorGUILayout.EnumPopup("Selection",selectionTool);if(EditorGUI.EndChangeCheck())ChooseImageSelectionTool(tool);
                if(selectionTool==ImageSelectionTool.Rectangle)marqueeSnapPixels=Mathf.Clamp(EditorGUILayout.IntField(new GUIContent("Snap increment (px)","Hold Shift while dragging. Width and height snap independently in image pixels."),marqueeSnapPixels),1,256);
                if(selectionTool==ImageSelectionTool.MagneticLasso)magneticRadius=EditorGUILayout.Slider("Edge distance",magneticRadius,2,40);
                EditorGUILayout.LabelField(selectionTool==ImageSelectionTool.MagneticLasso?"Click to start, follow an edge, then double-click or press Enter to close. Esc cancels.":selectionTool==ImageSelectionTool.Lasso?"Drag a freehand outline; release to close the selection.":"Drag to select. Hold Shift to snap. Hold Space while drawing to reposition. Drag inside a finished selection to move its boundary.",StudioTheme.Styles.wordWrappedMiniLabel);
                EditorGUILayout.Space(8);return;
            }
            if(imageTool==ImageTool.Shape){DrawImageShapeSettings();EditorGUILayout.Space(8);return;}
            if(imageTool==ImageTool.Gradient){DrawImageGradientSettings();EditorGUILayout.Space(8);return;}
            if(imageTool==ImageTool.Brush || imageTool==ImageTool.Eraser)
            {
                if(imageTool==ImageTool.Brush)imageBrushMode=(ImageBrushMode)EditorGUILayout.EnumPopup("Brush",imageBrushMode);
                brushRadius=EditorGUILayout.Slider(new GUIContent("Size", "Ctrl + middle-drag sideways"),brushRadius,1,200);
                brushOpacity=EditorGUILayout.Slider(new GUIContent("Strength", "Ctrl + middle-drag up/down"),brushOpacity,.01f,1);
            }
            else
            {
                string hint=imageTool==ImageTool.Marquee?"Drag a rectangle to limit painting and image adjustments. You can start outside the image and drag across its edges. Ctrl+D clears the selection.":imageTool==ImageTool.Transform?"Drag to move; corners scale. Shift+corner unlocks aspect ratio; Ctrl+corner distorts. Round handle rotates; Shift snaps rotation; Esc cancels.":
                    imageTool==ImageTool.Move?"Drag a layer to move it. Arrow keys nudge; Shift nudges 10 pixels.":"Click the canvas to sample a color.";
                EditorGUILayout.LabelField(hint,StudioTheme.Styles.wordWrappedMiniLabel);
            }
            EditorGUILayout.Space(8);
        }

        private void DrawImageCanvasBar()
        {
            var bar=GUILayoutUtility.GetRect(0,26,GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bar,StudioTheme.Current.Header);
            EditorGUI.DrawRect(new Rect(bar.x,bar.yMax-1,bar.width,1),StudioTheme.Current.Border);
            DrawImageUvToolbar(bar);
            var resolutionRect=new Rect(bar.x+3,bar.y+1,110,24);
            var channelRect=new Rect(bar.xMax-146,bar.y+1,60,24);
            var colorRect=new Rect(bar.xMax-83,bar.y+3,20,20);
            var visibilityRect=new Rect(bar.xMax-57,bar.y+1,24,24);
            var materialRect=new Rect(bar.xMax-27,bar.y+1,24,24);
            var labels=System.Array.ConvertAll(CanvasResolutions,size=>new GUIContent(size+" × "+size,"Canvas resolution"));
            int resolution=EditorGUI.Popup(resolutionRect,resolutionIndex,labels,StudioTheme.Styles.overlayDropdown);
            if(resolution!=resolutionIndex)ChangeResolution(resolution);
            DrawImageUvChannelMenu(channelRect);
            EditorGUI.BeginChangeCheck();
            uvOverlayColor=EditorGUI.ColorField(colorRect,new GUIContent("","UV wireframe color"),uvOverlayColor,false,true,false);
            if(EditorGUI.EndChangeCheck())Repaint();
            bool visible=GUI.Toggle(visibilityRect,showUvOverlay,new GUIContent("",showUvOverlay?"Hide UV wireframe":"Show UV wireframe"),StudioTheme.Styles.overlayButton);
            if(visible!=showUvOverlay){showUvOverlay=visible;Repaint();}
            var materialIcon=new GUIContent(EditorGUIUtility.IconContent("Material Icon"))
            {
                tooltip="Material / Live Link — open or close the material panel"
            };
            bool materialPanel=GUI.Toggle(materialRect,showImageLiveLink,materialIcon,StudioTheme.Styles.overlayButton);
            if(materialPanel!=showImageLiveLink){showImageLiveLink=materialPanel;Repaint();}
            if(Event.current.type==EventType.Repaint)
            {
                Vector3 c=visibilityRect.center;
                Color color=showUvOverlay?StudioTheme.Current.Text:StudioTheme.Current.Muted;
                DrawImageIconLine(color,1.5f,c+new Vector3(-8,0),c+new Vector3(-4,-4),c+new Vector3(0,-5),c+new Vector3(4,-4),c+new Vector3(8,0),c+new Vector3(4,4),c+new Vector3(0,5),c+new Vector3(-4,4),c+new Vector3(-8,0));
                DrawImageGuiCircle(c,2,color);
                if(!showUvOverlay)DrawImageIconLine(color,1.5f,c+new Vector3(-8,7),c+new Vector3(8,-7));
            }
        }

        private void DrawImageCanvas()
        {
            using(new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true),GUILayout.ExpandHeight(true)))
            {
                DrawImageCanvasBar();
                Rect available=GUILayoutUtility.GetRect(280,280,GUILayout.ExpandWidth(true),GUILayout.ExpandHeight(true));
                canvasArea=available;
                EditorGUI.DrawRect(available,StudioTheme.Current.Canvas);
                GUI.BeginGroup(available);
                try
                {
                    float side=Mathf.Max(32,Mathf.Min(available.width-36,available.height-36))*zoom;
                    Rect rect=new Rect((available.width-side)*.5f+pan.x,(available.height-side)*.5f+pan.y,side,side);
                    // Checker cells are bounded to the visible viewport at every zoom.
                    {
                        EditorGUI.DrawRect(rect,Color.Lerp(StudioTheme.Current.Canvas,StudioTheme.Current.Text,.08f));
                        for(int y=Mathf.Max(0,Mathf.FloorToInt(-rect.y/16));y<Mathf.Min(Mathf.CeilToInt(side/16),Mathf.CeilToInt((available.height-rect.y)/16));y++)
                            for(int x=Mathf.Max(0,Mathf.FloorToInt(-rect.x/16));x<Mathf.Min(Mathf.CeilToInt(side/16),Mathf.CeilToInt((available.width-rect.x)/16));x++)
                                if((x+y)%2==0)EditorGUI.DrawRect(new Rect(rect.x+x*16,rect.y+y*16,Mathf.Min(16,side-x*16),Mathf.Min(16,side-y*16)),Color.Lerp(StudioTheme.Current.Canvas,StudioTheme.Current.Text,.18f));
                    }
                    EnsureComposite();
                    if(Event.current.type==EventType.Repaint)RefreshImageAdjustmentPreview();
                    if(compositeDirty && Event.current.type==EventType.Repaint) RenderComposite();
                    if(composite)GUI.DrawTexture(rect,ImageCanvasDisplay(),ScaleMode.StretchToFill,CurrentTextureProperty != "_MaskMap");
                    if(imageUvMode==ImageUvMode.Paint){DrawUvOverlay(rect);DrawSelectedLayerOutline(rect);}
                    bool insideView=new Rect(0,0,available.width,available.height).Contains(Event.current.mousePosition);
                    bool uvOwnsCanvas=HandleImageUvCanvas(rect,insideView);
                    bool inlineOwnsCanvas=uvOwnsCanvas||HandleInlineImageTextActivation(rect)||inlineTextLayer!=null;
                    if(!inlineOwnsCanvas && !(choosingColorRange && HandleImageColorRange(rect)) && !(tracingSelection && HandleImageLasso(rect)) && !(drawingImageShape && HandleImageShape(rect)) && !(drawingImageGradient && HandleImageGradient(rect)) && !((drawingImageSelection||movingImageSelection) && HandleImageMarquee(rect)) && !HandleImageTransform(rect) && !HandleImageBrushAdjustment(rect) && (insideView || painting || panning || draggingLayer)) HandleCanvasInput(rect);
                    if(!uvOwnsCanvas&&insideView&&inlineTextLayer==null) HandleCanvasDragAndDrop(rect);
                    DrawImageMarquee(rect);
                    DrawImageGradient(rect);
                    DrawImageShapePreview(rect);
                    if(imageUvMode==ImageUvMode.Paint)DrawImageTransformHandles(rect);
                    DrawImageBrushAdjustment(rect);
                    UpdateImageSelectionCursor(insideView);
                    DrawInlineImageText(rect);
                    if(layers.Count==0 && imageUvMode==ImageUvMode.Paint)GUI.Label(new Rect(18,available.height*.45f,available.width-36,70),"Drop an image to begin\nOr add a paint layer",new GUIStyle(StudioTheme.Styles.centeredGreyMiniLabel){fontSize=16});
                    DrawImageUvEditing(rect);
                    if(!uvOwnsCanvas && !adjustingImageBrush && (imageTool==ImageTool.Brush || imageTool==ImageTool.Eraser)&&rect.Contains(Event.current.mousePosition)&&Event.current.type==EventType.Repaint)
                    {
                        DrawImageGuiCircle(Event.current.mousePosition,brushRadius*rect.width/CanvasSize,Color.white);
                    }
                }
                finally {GUI.EndGroup();}
            }
        }

        private bool adjustingImageBrush;
        private int imageBrushControl, imageBrushAxis;
        private Vector2 imageBrushAnchor, imageBrushPointer;
        private float imageBrushInitialRadius, imageBrushInitialStrength;

        private void EndImageBrushAdjustment(bool cancel=false)
        {
            if(!adjustingImageBrush)return;
            if(cancel){brushRadius=imageBrushInitialRadius;brushOpacity=imageBrushInitialStrength;}
            if(GUIUtility.hotControl==imageBrushControl)GUIUtility.hotControl=0;
            adjustingImageBrush=false;inputSnapshot=null;Repaint();
        }
        private void ApplyImageBrushAdjustment(Vector2 delta)
        {
            if(imageBrushAxis==0 && Mathf.Max(Mathf.Abs(delta.x),Mathf.Abs(delta.y))>=4)
                imageBrushAxis=Mathf.Abs(delta.x)>=Mathf.Abs(delta.y)?1:2;
            if(imageBrushAxis==1)
                brushRadius=Mathf.Clamp(imageBrushInitialRadius*Mathf.Exp(Mathf.Clamp(delta.x*.01f,-10,10)),1,200);
            else if(imageBrushAxis==2)
                brushOpacity=Mathf.Clamp(imageBrushInitialStrength-delta.y*.005f,.01f,1);
            imageBrushPointer=imageBrushAnchor+(imageBrushAxis==1?new Vector2(delta.x,0):new Vector2(0,delta.y));
        }
        private bool HandleImageBrushAdjustment(Rect rect)
        {
            var e=Event.current;
            int id=GUIUtility.GetControlID("Image brush adjustment".GetHashCode(),FocusType.Passive);
            if(adjustingImageBrush)
            {
                if(GUIUtility.hotControl!=imageBrushControl || e.type==EventType.Ignore || e.type==EventType.MouseLeaveWindow)
                {EndImageBrushAdjustment();return true;}
                if(e.type==EventType.KeyDown && e.keyCode==KeyCode.Escape){EndImageBrushAdjustment(true);e.Use();return true;}
                if(e.rawType==EventType.MouseUp && e.button==2){EndImageBrushAdjustment();e.Use();return true;}
                if(e.type==EventType.MouseDrag && e.button==2)
                {ApplyImageBrushAdjustment(e.mousePosition-imageBrushAnchor);e.Use();Repaint();return true;}
                if(e.isMouse || e.isKey || e.type==EventType.ScrollWheel)e.Use();
                return true;
            }
            if(imageTool!=ImageTool.Brush && imageTool!=ImageTool.Eraser)return false;
            if(e.type!=EventType.MouseDown || e.button!=2 || !e.control || e.alt || GUIUtility.hotControl!=0 ||
                painting || panning || draggingLayer || !rect.Contains(e.mousePosition) ||
                !new Rect(0,0,canvasArea.width,canvasArea.height).Contains(e.mousePosition))return false;
            imageBrushInitialRadius=brushRadius;imageBrushInitialStrength=brushOpacity;
            imageBrushAnchor=imageBrushPointer=e.mousePosition;imageBrushAxis=0;
            adjustingImageBrush=true;imageBrushControl=id;GUIUtility.hotControl=id;
            GUIUtility.keyboardControl=0;EditorGUIUtility.editingTextField=false;inputSnapshot=null;
            e.Use();Repaint();return true;
        }
        private void DrawImageBrushAdjustment(Rect rect)
        {
            if(!adjustingImageBrush || Event.current.type!=EventType.Repaint)return;
            {
                DrawImageGuiCircle(imageBrushAnchor,brushRadius*rect.width/CanvasSize,Color.white);
                DrawImageIconLine(Color.white,2,imageBrushAnchor,imageBrushPointer);
                string text=imageBrushAxis==1?$"Radius {brushRadius:F0} px":imageBrushAxis==2?$"Strength {brushOpacity:P0}":"Sideways: size | Up/down: strength";
                GUI.Box(new Rect(Mathf.Clamp(imageBrushAnchor.x+18,0,Mathf.Max(0,canvasArea.width-270)),
                    Mathf.Clamp(imageBrushAnchor.y+18,0,Mathf.Max(0,canvasArea.height-28)),270,28),text);
            }
        }

        private void BeginImageInput()
        {
            if(HandleImageUvKeys())return;
            if(HandleImageLayerRenameInput())return;
            if(HandleInlineImageTextInput())return;
            HandleMarqueeSpaceKey();HandleMarqueeSnapKey();
            var e=Event.current;
            if(imageTool==ImageTool.Transform && !EditorGUIUtility.editingTextField && e.type==EventType.KeyDown &&
                !e.control && !e.command && !e.alt && (e.keyCode==KeyCode.Return || e.keyCode==KeyCode.KeypadEnter))
            {
                EndImageTransform();
                imageTool=ImageTool.Move;
                GUIUtility.keyboardControl=0;
                e.Use(); Repaint(); return;
            }
            if(imageTool==ImageTool.Text&&e.type==EventType.KeyDown&&(e.control||e.command)&&e.keyCode==KeyCode.T)
            {GUIUtility.keyboardControl=0;EditorGUIUtility.editingTextField=false;imageTool=ImageTool.Transform;e.Use();Repaint();}
            // Hover changes only the cursor; repaint without invalidating the image composite.
            if(e.type==EventType.MouseMove || e.type==EventType.MouseEnterWindow || e.type==EventType.MouseLeaveWindow)
                Repaint();
            if(!EditorGUIUtility.editingTextField && GUIUtility.hotControl==0 && e.type==EventType.KeyDown &&
                !e.control && !e.command && !e.alt && (e.keyCode==KeyCode.Delete || e.keyCode==KeyCode.Backspace))
            {
                if(HasSelectedLayer && !layers[selectedLayer].locked)
                {
                    inputSnapshot=CaptureImage();
                    DeleteSelectedLayer();
                    Repaint();
                }
                e.Use();
                return;
            }
            if(!EditorGUIUtility.editingTextField && e.type==EventType.KeyDown && (e.control||e.command))
            {
                if(e.keyCode==KeyCode.Z){ImageUndo(e.shift);e.Use();}
                else if(e.keyCode==KeyCode.Y){ImageUndo(true);e.Use();}
                else if(e.keyCode==KeyCode.S){SaveImageDocument();e.Use();}
                else if(e.keyCode==KeyCode.C){CopyImageSelection();e.Use();}
                else if(e.keyCode==KeyCode.V){PasteImageSelection();e.Use();}
                else if(e.keyCode==KeyCode.D){DeselectImage();e.Use();}
                else if(e.keyCode==KeyCode.I){if(e.shift)InvertImageSelection();else ApplyImagePixelCommand(ImagePixelCommand.Invert);e.Use();}
                else if(e.keyCode==KeyCode.T){imageTool=ImageTool.Transform;e.Use();Repaint();}
                else if(e.keyCode==KeyCode.U){BeginImageAdjustment(ImageAdjustment.HueSaturation);e.Use();}
                else if(e.keyCode==KeyCode.L){BeginImageAdjustment(ImageAdjustment.Levels);e.Use();}
                else if(e.keyCode==KeyCode.A){SelectAllImage();e.Use();Repaint();}
            }
            if(e.type==EventType.MouseDown || e.type==EventType.KeyDown || e.type==EventType.DragPerform)
                inputSnapshot=CaptureImage();
        }
        private void EndImageInput()
        {
            if(Event.current.rawType==EventType.MouseUp){painting=false;paintTexture=null;inputSnapshot=null;}
        }
        private void ImageChanged()
        {
            imageChannelDirty=true;
            RequestImageAdjustmentPreview();
            if(restoring)return;
            if(inputSnapshot!=null)
            {
                undoImages.Add(inputSnapshot);redoImages.Clear();inputSnapshot=null;
                while(undoImages.Count>20 || (undoImages.Count>1 && undoImages.Sum(s=>(long)s.Length)>64*1024*1024))undoImages.RemoveAt(0);
            }
            hasUnsavedChanges=true;pendingExport=true;
            for(int i=ownedTextures.Count-1;i>=0;i--)
            {
                var texture=ownedTextures[i];
                if(layers.Any(l=>l.texture==texture))continue;
                if(texture){encodedImages.Remove(texture);DestroyImmediate(texture);}
                ownedTextures.RemoveAt(i);
            }
        }
        private void ImageUndo(bool redo)
        {
            FinishImageLayerRename(true);
            FinishInlineImageText(true);
            var from=redo?redoImages:undoImages;var to=redo?undoImages:redoImages;
            if(from.Count==0)return;
            CancelImageAdjustment();EndImageShape();EndImageGradient();
            EndImageMarquee();
            ReleaseImageSelectionCursor();CancelImageColorRange();CancelSelectionTrace();ReleaseImageSelectionOutline();
            EndImageTransform();
            if(painting){painting=false;paintTexture=null;GUIUtility.hotControl=0;}
            to.Add(CaptureImage());string state=from[from.Count-1];from.RemoveAt(from.Count-1);
            RestoreImage(state);inputSnapshot=null;hasUnsavedChanges=true;pendingExport=true;
        }
        private void AddPaintLayer()
        {
            var texture=new Texture2D(CanvasSize,CanvasSize,TextureFormat.RGBA32,false,false){name="Paint",hideFlags=HideFlags.HideAndDontSave};
            texture.SetPixels32(new Color32[CanvasSize*CanvasSize]);texture.Apply();ownedTextures.Add(texture);
            AddLayer(texture,"Paint "+(layers.Count+1));layers[selectedLayer].scale=1;imageTool=ImageTool.Brush;
        }

        private bool HandleImageTools(Rect rect)
        {
            var e=Event.current;
            if(e.type==EventType.KeyDown)
            {
                if(e.keyCode==KeyCode.F){zoom=1;pan=Vector2.zero;e.Use();return true;}
                if(!e.control && !e.command)
                {
                    if(e.keyCode==KeyCode.B)imageTool=ImageTool.Brush;
                    else if(e.keyCode==KeyCode.E)imageTool=ImageTool.Eraser;
                    else if(e.keyCode==KeyCode.V)imageTool=ImageTool.Move;
                    else if(e.keyCode==KeyCode.T)EditImageText();
                    else if(e.keyCode==KeyCode.M)ChooseImageSelectionTool(ImageSelectionTool.Rectangle);
                    else if(e.keyCode==KeyCode.L)ChooseImageSelectionTool(ImageSelectionTool.Lasso);
                    else if(e.keyCode==KeyCode.G)imageTool=ImageTool.Gradient;
                    else if(e.keyCode==KeyCode.U)imageTool=ImageTool.Shape;
                    else if(e.keyCode==KeyCode.I)imageTool=ImageTool.Eyedropper;
                    else return false;
                    e.Use();return true;
                }
            }
            if(e.type==EventType.ScrollWheel)
            {
                Vector2 center=new Vector2(canvasArea.width,canvasArea.height)*.5f;
                float next=Mathf.Clamp(zoom*Mathf.Exp(-e.delta.y*.08f),.1f,16);
                pan=(pan-(e.mousePosition-center))*(next/zoom)+(e.mousePosition-center);zoom=next;
                e.Use();Repaint();return true;
            }
            if(e.type==EventType.MouseDown && (e.button==2 || (e.button==0 && e.alt)))
            {panning=true;GUIUtility.hotControl=strokeControl=GUIUtility.GetControlID(FocusType.Passive);e.Use();return true;}
            if(panning)
            {
                if(e.type==EventType.MouseDrag){pan+=e.delta;e.Use();Repaint();}
                if(e.rawType==EventType.MouseUp){panning=false;GUIUtility.hotControl=0;e.Use();}
                return true;
            }
            if(imageAdjustment!=ImageAdjustment.None)return true;
            if(HandleImageText(rect))return true;
            if(HandleImageShape(rect))return true;
            if(HandleImageGradient(rect))return true;
            if(HandleImageMarquee(rect))return true;
            if(imageTool==ImageTool.Move || imageTool==ImageTool.Transform)return false;
            if(e.type==EventType.MouseDown && e.button==0 && rect.Contains(e.mousePosition))
            {
                Vector2 point=GuiToCanvas(e.mousePosition,rect);
                if(imageTool==ImageTool.Eyedropper)
                {
                    RenderComposite();var sample=ReadTexture(composite);
                    brushColor=sample.GetPixel(Mathf.Clamp((int)point.x,0,CanvasSize-1),Mathf.Clamp(CanvasSize-1-(int)point.y,0,CanvasSize-1));
                    DestroyImmediate(sample);imageTool=ImageTool.Brush;
                }
                else if(HasSelectedLayer && layers[selectedLayer].isText)
                {status="Right-click this text layer and choose Rasterize Text before pixel painting.";statusType=MessageType.Info;}
                else if(HasSelectedLayer && !layers[selectedLayer].locked && layers[selectedLayer].texture)
                {
                    paintTexture=ReadTexture(layers[selectedLayer].texture);ownedTextures.Add(paintTexture);
                    layers[selectedLayer].texture=paintTexture;painting=true;lastPaint=point;brushDabDelta=Vector2.zero;
                    GUIUtility.hotControl=strokeControl=GUIUtility.GetControlID(FocusType.Passive);
                    PaintAt(point);paintTexture.Apply(false);MarkCompositeDirty();
                }
                e.Use();return true;
            }
            if(painting && e.type==EventType.MouseDrag)
            {
                Vector2 point=GuiToCanvas(e.mousePosition,rect);
                int steps=Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(lastPaint,point)/Mathf.Max(1,brushRadius*.25f)),1,512);
                brushDabDelta=(point-lastPaint)/steps;
                for(int i=1;i<=steps;i++)PaintAt(Vector2.Lerp(lastPaint,point,i/(float)steps));
                paintTexture.Apply(false);lastPaint=point;MarkCompositeDirty();e.Use();return true;
            }
            if(painting && e.rawType==EventType.MouseUp){painting=false;paintTexture=null;GUIUtility.hotControl=0;e.Use();return true;}
            return true;
        }
        private void PaintAt(Vector2 point)
        {
            encodedImages.Remove(paintTexture);
            var layer=layers[selectedLayer];var crop=ClampSourceRect(layer.sourceRect);
            bool warped=ImageIsWarped(layer);
            var pixel=warped?Vector2.zero:ImageCanvasToPixel(layer,point);float x=pixel.x,y=pixel.y;
            float radius=Mathf.Max(.5f,brushRadius/layer.scale);
            int x0=Mathf.Max(Mathf.FloorToInt(x-radius),Mathf.CeilToInt(crop.xMin*paintTexture.width));
            int y0=Mathf.Max(Mathf.FloorToInt(y-radius),Mathf.CeilToInt((1-crop.yMax)*paintTexture.height));
            int x1=Mathf.Min(Mathf.CeilToInt(x+radius),Mathf.CeilToInt(crop.xMax*paintTexture.width)-1);
            int y1=Mathf.Min(Mathf.CeilToInt(y+radius),Mathf.CeilToInt((1-crop.yMin)*paintTexture.height)-1);
            if(warped)
            {
                x0=Mathf.CeilToInt(crop.xMin*paintTexture.width);x1=Mathf.CeilToInt(crop.xMax*paintTexture.width)-1;
                y0=Mathf.CeilToInt((1-crop.yMax)*paintTexture.height);y1=Mathf.CeilToInt((1-crop.yMin)*paintTexture.height)-1;
                BuildImageBrushRows(layer,point,x0,x1,y0,y1);
            }
            bool smudge=imageTool==ImageTool.Brush && imageBrushMode==ImageBrushMode.Smudge;
            Vector2 pull=Rotate(brushDabDelta,-layer.rotation)/layer.scale;pull.y=-pull.y;
            if(smudge && pull.sqrMagnitude<.00001f)return;
            // Snapshot the dab region before writing, so smudging is independent of pixel traversal order.
            int sx0=Mathf.Clamp(Mathf.FloorToInt(x0-pull.x)-1,0,paintTexture.width-1),sy0=Mathf.Clamp(Mathf.FloorToInt(y0-pull.y)-1,0,paintTexture.height-1);
            int sx1=Mathf.Clamp(Mathf.CeilToInt(x1-pull.x)+1,0,paintTexture.width-1),sy1=Mathf.Clamp(Mathf.CeilToInt(y1-pull.y)+1,0,paintTexture.height-1);
            int sw=Mathf.Max(1,sx1-sx0+1),sh=Mathf.Max(1,sy1-sy0+1);
            if(warped && smudge)
            {
                var bounds=ImageBrushSourceBounds(layer,point-brushDabDelta,x0,x1,y0,y1);
                if(bounds.width==0||bounds.height==0)return;
                sx0=bounds.x;sy0=bounds.y;sw=bounds.width;sh=bounds.height;
            }
            var smudgePixels=smudge?paintTexture.GetPixels(sx0,sy0,sw,sh):null;
            for(int py=y0;py<=y1;py++)for(int px=warped?imageBrushRows[py].x:x0;px<=(warped?imageBrushRows[py].y:x1);px++)
            {
                float distance=warped?Vector2.Distance(ImagePixelCanvasPosition(layer,px,py),point)/brushRadius:Vector2.Distance(new Vector2(px+.5f,py+.5f),new Vector2(x,y))/radius;
                float a=Mathf.Clamp01((1-distance)*4)*brushOpacity;
                if(a<=0)continue;
                if(!ImagePixelEditable(layer,px,py))continue;
                Color old=paintTexture.GetPixel(px,py),original=old;float channelAmount=a;
                if(imageTool==ImageTool.Eraser)old.a*=1-a;
                else if(imageBrushMode==ImageBrushMode.Burn)
                {old.r*=1-a*.5f;old.g*=1-a*.5f;old.b*=1-a*.5f;}
                else if(imageBrushMode==ImageBrushMode.Dodge)
                {old.r=Mathf.Lerp(old.r,1,a*.25f);old.g=Mathf.Lerp(old.g,1,a*.25f);old.b=Mathf.Lerp(old.b,1,a*.25f);}
                else if(smudge)
                {
                    if(warped && (!ImageCanvasToUV(layer,ImagePixelCanvasPosition(layer,px,py)-brushDabDelta,out var sourceUV) || sourceUV.x<0 || sourceUV.x>1 || sourceUV.y<0 || sourceUV.y>1))continue;
                    var source=warped?ImageCanvasToPixel(layer,ImagePixelCanvasPosition(layer,px,py)-brushDabDelta)-Vector2.one*.5f:new Vector2(px,py)-pull;
                    float u=Mathf.Clamp(source.x-sx0,0,sw-1),v=Mathf.Clamp(source.y-sy0,0,sh-1);
                    int ix=(int)u,iy=(int)v;
                    Color sample=Color.Lerp(Color.Lerp(smudgePixels[iy*sw+ix],smudgePixels[iy*sw+Mathf.Min(ix+1,sw-1)],u-ix),Color.Lerp(smudgePixels[Mathf.Min(iy+1,sh-1)*sw+ix],smudgePixels[Mathf.Min(iy+1,sh-1)*sw+Mathf.Min(ix+1,sw-1)],u-ix),v-iy);
                    if(imageChannel==ImageChannel.RGB){float alpha=old.a;old=Color.Lerp(old,sample,a*sample.a);old.a=alpha;}
                    else old=SetImageChannel(original,Mathf.Lerp(ImageChannelValue(original),ImageChannelValue(sample),a));
                }
                else
                {
                    a*=brushColor.a;float alpha=a+old.a*(1-a);
                    old=alpha>0?(brushColor*a+old*(old.a*(1-a)))/alpha:Color.clear;old.a=alpha;
                }
                if(imageChannel!=ImageChannel.RGB&&!smudge)old=ImageChannelBrush(original,channelAmount);
                paintTexture.SetPixel(px,py,old);
            }
        }

        internal static Texture2D ReadTexture(Texture source)
        {
            var previous=RenderTexture.active;
            bool srgb=GL.sRGBWrite;
            var rt=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            try
            {
                GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear;
                Graphics.Blit(source,rt);RenderTexture.active=rt;
                var texture=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false,false){hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp,name=source.name};
                texture.ReadPixels(new Rect(0,0,source.width,source.height),0,0);texture.Apply();return texture;
            }
            finally {GL.sRGBWrite=srgb;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);}
        }
        private string CaptureImage()
        {
            var file=new ImageFile{resolution=resolutionIndex,selected=selectedLayer,background=backgroundColor,hasSelection=hasImageSelection,selection=imageSelection,mask=EncodeSelectionMask(selectionMask),maskSize=selectionMaskSize,maskOffset=selectionMaskOffset};
            foreach(var layer in layers)
            {
                string png=null;
                if(layer.texture && !encodedImages.TryGetValue(layer.texture,out png))
                {
                    var pixels=ReadTexture(layer.texture);
                    try {png=Convert.ToBase64String(pixels.EncodeToPNG());encodedImages[layer.texture]=png;}
                    finally {DestroyImmediate(pixels);}
                }
                file.layers.Add(new ImageLayerFile{settings=JsonUtility.ToJson(layer),png=png});
            }
            return JsonUtility.ToJson(file);
        }
        private void RestoreImage(string json)
        {
            FinishInlineImageText(true);
            CancelImageAdjustment();
            var file=JsonUtility.FromJson<ImageFile>(json);
            if(file==null || file.version!=1 || file.layers==null || file.layers.Count>128 || file.resolution<0 || file.resolution>=CanvasResolutions.Length)throw new InvalidDataException("Unsupported image document.");
            var restoredMask=DecodeSelectionMask(file.mask,file.maskSize);
            var replacement=new List<StickerLayer>();var created=new List<Texture2D>();
            try
            {
                foreach(var saved in file.layers)
                {
                    var layer=JsonUtility.FromJson<StickerLayer>(saved.settings);layer.texture=null;
                    if(!string.IsNullOrEmpty(saved.png))
                    {
                        var texture=new Texture2D(2,2,TextureFormat.RGBA32,false,false){hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp};created.Add(texture);
                        if(!texture.LoadImage(Convert.FromBase64String(saved.png)))throw new InvalidDataException("Invalid layer image.");
                        layer.texture=texture;
                    }
                    replacement.Add(layer);
                }
            }
            catch{foreach(var t in created)DestroyImmediate(t);throw;}
            RestoreLivePreview();foreach(var texture in ownedTextures)if(texture)DestroyImmediate(texture);ownedTextures.Clear();ownedTextures.AddRange(created);
            encodedImages.Clear();layers=replacement;selectedLayer=Mathf.Clamp(file.selected,-1,layers.Count-1);resolutionIndex=file.resolution;backgroundColor=file.background;hasImageSelection=file.hasSelection;imageSelection=file.selection;selectionMask=restoredMask;selectionMaskSize=file.maskSize;selectionMaskOffset=file.maskOffset;selectionOutlineDirty=true;
            restoring=true;try {RepairLegacyImageText();ReleaseComposite();MarkCompositeDirty();}finally{restoring=false;}
        }
        private bool SaveImageDocument()
        {
            if(string.IsNullOrEmpty(documentPath))documentPath=EditorUtility.SaveFilePanel("Save layered image","","Untitled.mashimage","mashimage");
            if(string.IsNullOrEmpty(documentPath))return false;
            try{session=CaptureImage();File.WriteAllText(documentPath,session);hasUnsavedChanges=false;status="Saved layers: "+Path.GetFileName(documentPath);statusType=MessageType.Info;return true;}
            catch(Exception e){status=e.Message;statusType=MessageType.Error;return false;}
        }
        private void OpenImageDocument()
        {
            if(hasUnsavedChanges && !EditorUtility.DisplayDialog("Open image","Replace unsaved edits?","Open","Cancel"))return;
            string path=EditorUtility.OpenFilePanel("Open layered image","","mashimage");if(string.IsNullOrEmpty(path))return;
            try{string json=File.ReadAllText(path);RestoreImage(json);session=json;documentPath=path;outputAssetPath=null;savedTexture=null;undoImages.Clear();redoImages.Clear();hasUnsavedChanges=false;pan=Vector2.zero;zoom=1;}
            catch(Exception e){status=e.Message;statusType=MessageType.Error;}
        }
        private void DrawDirectMaterial()
        {
            var material=(Material)EditorGUILayout.ObjectField(new GUIContent("Material","The linked material. Choose a material asset to link all scene renderers using it."),CurrentMaterial,typeof(Material),false);
            if(material!=CurrentMaterial){RestoreLivePreview();directMaterial=material;if(!material){targetRoot=null;targetRenderer=null;}RefreshTextureProperties();TryAutoOpenCurrentStickerMap();MarkCompositeDirty();}
        }
        private void DrawShaderImagePicker()
        {
            EditorGUILayout.LabelField(new GUIContent("Shader image","Edit this texture slot. Live preview uses its normal shader role, leaving the other maps unchanged."),StudioTheme.Styles.boldLabel);
            using(new EditorGUI.DisabledScope(texturePropertyNames.Length==0))
            {
                int selected=EditorGUILayout.Popup(texturePropertyIndex,texturePropertyLabels,GUILayout.Height(22));
                if(selected!=texturePropertyIndex) SelectShaderImage(selected);
            }
            if(texturePropertyNames.Length==0)EditorGUILayout.HelpBox("This material has no 2D image slots.",MessageType.None);
        }
        private void SelectShaderImage(int selected)
        {
            if(selected<0 || selected>=texturePropertyNames.Length)return;
            if(hasUnsavedChanges && layers.Count>0 && !EditorUtility.DisplayDialog("Switch shader image",
                "Replace the current canvas with this shader image? Save Layers first if you want to keep these edits.","Switch image","Cancel"))return;
            // Remove the old slot override before previewing the newly selected map.
            // Preserve the user's live-preview setting and the material's normal shader.
            RestoreLivePreview();texturePropertyIndex=selected;
            RememberShaderImageSelection();
            outputAssetPath=null;savedTexture=null;documentPath=null;
            if(GetCurrentStickerTexture())OpenCurrentStickerMap(true,true);
            else
            {
                layers.Clear();selectedLayer=-1;MarkCompositeDirty();
                status="This shader slot is empty. Import an image or add a paint layer.";statusType=MessageType.Info;
            }
            undoImages.Clear();redoImages.Clear();inputSnapshot=null;
            hasUnsavedChanges=false;pendingExport=false;pan=Vector2.zero;zoom=1;
        }
        private void RenderImageComposite(StickerLayer onlyLayer=null,bool preview=true,RenderTexture target=null)
        {
            EnsureComposite();EnsureCompositeMaterial();if(!compositeMaterial)return;
            var output=target?target:composite;bool raw=target;
            var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
            var temp=RenderTexture.GetTemporary(CanvasSize,CanvasSize,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            try
            {
                GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear;
                RenderTexture.active=output;GL.Clear(false,true,onlyLayer!=null?Color.clear:backgroundColor);
                foreach(var layer in layers)
                {
                    if((onlyLayer!=null&&layer!=onlyLayer) || (onlyLayer==null&&!layer.visible) || !layer.texture || (!raw&&layer.opacity<=0))continue;
                    compositeMaterial.SetTexture("_Layer",ImageLayerDisplayTexture(layer));
                    compositeMaterial.SetVector("_Placement",new Vector4(layer.position.x/CanvasSize,1-layer.position.y/CanvasSize,layer.texture.width*layer.scale/CanvasSize,layer.texture.height*layer.scale/CanvasSize));
                    var crop=ClampSourceRect(layer.sourceRect);compositeMaterial.SetVector("_Crop",new Vector4(crop.x,1-crop.yMax,crop.width,crop.height));
                    compositeMaterial.SetFloat("_Rotation",layer.rotation*Mathf.Deg2Rad);
                    compositeMaterial.SetFloat("_Warped",ImageIsWarped(layer)?1:0);
                    for(int corner=0;corner<4;corner++){var q=ImageCorner(layer,corner);compositeMaterial.SetVector("_Corner"+corner,new Vector4(q.x,q.y,0,0));}
                    bool dataMap = CurrentTextureProperty == "_MaskMap";
                    compositeMaterial.SetFloat("_DataMap", dataMap ? 1 : 0);
                    compositeMaterial.SetFloat("_LayerOpacity", raw ? 1 : layer.opacity);
                    compositeMaterial.SetColor("_Tint",raw?Color.white:new Color(layer.tint.r,layer.tint.g,layer.tint.b,layer.tint.a*(dataMap ? 1 : layer.opacity)));
                    compositeMaterial.SetFloat("_BlackToAlpha",!raw&&layer.blackToAlpha?1:0);
                    compositeMaterial.SetFloat("_Blend",raw?0:layer.blendMode);compositeMaterial.SetFloat("_Exposure",raw?0:layer.exposure);compositeMaterial.SetFloat("_Saturation",raw?1:layer.saturation);
                    Graphics.Blit(output,temp,compositeMaterial);Graphics.Blit(temp,output);
                }
                if(!target)compositeDirty=false;if(preview)ApplyLivePreview();
            }
            finally{GL.sRGBWrite=srgb;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(temp);}
        }
    }
}
