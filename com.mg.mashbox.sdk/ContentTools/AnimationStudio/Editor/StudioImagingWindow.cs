using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioImagingWindow : EditorWindow
    {
        [Serializable]
        private sealed class StickerLayer
        {
            public string name = "Layer";
            public Texture2D texture;
            public bool visible = true;
            public Vector2 position = new Vector2(512f, 512f);
            public float scale = 1f;
            public float rotation;
            public Vector2 corner0, corner1, corner2, corner3;
            public float opacity = 1f;
            public Color tint = Color.white;
            public bool blackToAlpha;
            public int blendMode;
            public float exposure, saturation = 1f;
            public bool locked;
            public bool isText;
            public string textContent="Text",textFont="Arial";
            public int textSize=64,textAlignment;
            public int textRasterVersion;
            public FontStyle textStyle;
            public Color textColor=Color.white;
            // Normalized top-left-origin rectangle into the source image. This keeps sheet crops non-destructive.
            public Rect sourceRect = new Rect(0f, 0f, 1f, 1f);
        }

        private enum RendererScope
        {
            [InspectorName("Selected renderer only")] SelectedRenderer,
            [InspectorName("All children using selected material")] MatchingMaterial,
            [InspectorName("All compatible child materials")] CompatibleMaterials
        }

        private readonly struct TargetBinding
        {
            public readonly Renderer renderer;
            public readonly int materialIndex;
            public readonly Material material;

            public TargetBinding(Renderer renderer, int materialIndex, Material material)
            {
                this.renderer = renderer;
                this.materialIndex = materialIndex;
                this.material = material;
            }
        }

        private sealed class PreviewBinding
        {
            public Renderer renderer;
            public int materialIndex;
            public MaterialPropertyBlock originalBlock;
        }

        private enum MeshUvChannel
        {
            [InspectorName("UV0 (mesh.uv)")] UV0,
            [InspectorName("UV1 (mesh.uv2)")] UV1,
            [InspectorName("UV2 (mesh.uv3)")] UV2,
            [InspectorName("UV3 (mesh.uv4)")] UV3
        }

        private static readonly int[] CanvasResolutions = { 512, 1024, 2048, 4096 };
        private static readonly string[] CanvasResolutionLabels = { "512", "1024", "2048", "4096" };
        private static readonly GUIContent[] ImageUvChannelLabels =
        {
            new GUIContent("UV0", "UV channel 0 (mesh.uv)"),
            new GUIContent("UV1", "UV channel 1 (mesh.uv2)"),
            new GUIContent("UV2", "UV channel 2 (mesh.uv3)"),
            new GUIContent("UV3", "UV channel 3 (mesh.uv4)")
        };
        private const string CompositeShaderName = "Hidden/MashBox/ImagingComposite";
        private const string DefaultOutputName = "StickerMap.png";
        private const string MashBoxSdkPackageRoot = "Packages/com.mg.mashbox.sdk";

        [SerializeField] private List<StickerLayer> layers = new List<StickerLayer>();
        [SerializeField] private int selectedLayer = -1;
        [SerializeField] private int resolutionIndex = 2;
        [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0f);
        [SerializeField] private GameObject targetRoot;
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private RendererScope rendererScope = RendererScope.MatchingMaterial;
        [SerializeField] private int materialIndex;
        [SerializeField] private int texturePropertyIndex;
        [SerializeField] private string selectedShaderImageProperty;
        [SerializeField] private Material selectedShaderImageMaterial;
        [SerializeField] private MeshUvChannel uvChannel = MeshUvChannel.UV0;
        [SerializeField] private bool showUvOverlay = true;
        [SerializeField] private Color uvOverlayColor = new Color(0f, 1f, 1f, 0.75f);

        [SerializeField] private bool autoOpenCurrentMap = true;
        [SerializeField] private bool autoSave;
        [SerializeField] private string outputAssetPath;

        [NonSerialized] private RenderTexture composite;
        [NonSerialized] private Material compositeMaterial;
        [NonSerialized] private Texture2D savedTexture;
        [NonSerialized] private readonly List<Texture2D> ownedTextures = new List<Texture2D>();
        [NonSerialized] private string[] texturePropertyNames = Array.Empty<string>();
        [NonSerialized] private string[] texturePropertyLabels = Array.Empty<string>();
        [NonSerialized] private bool compositeDirty = true;
        [NonSerialized] private string status;
        [NonSerialized] private MessageType statusType = MessageType.None;
        [NonSerialized] private bool draggingLayer;
        [NonSerialized] private Vector2 dragOffsetPixels;
        [NonSerialized] private double nextAutoSaveTime;
        [NonSerialized] private readonly List<PreviewBinding> previewBindings = new List<PreviewBinding>();
        [NonSerialized] private Vector2 layerScroll;
        [NonSerialized] private Vector2 inspectorScroll;

        [MenuItem("MashBox/MashBox Studio/2.5D Imaging", false, 124)]
        internal static void Open()
        {
            StudioImagingWindow window = GetWindow<StudioImagingWindow>();
            window.titleContent = new GUIContent("MashBox 2.5D Imaging");
            window.minSize = new Vector2(940f, 620f);
            window.Show();
        }

        private int CanvasSize => CanvasResolutions[Mathf.Clamp(resolutionIndex, 0, CanvasResolutions.Length - 1)];

        private void OnEnable()
        {
            titleContent = new GUIContent("MashBox 2.5D Imaging");
            ResumeImageUvMeshes();
            imageSettingsRatio=Mathf.Clamp(StudioPreferenceStore.GetFloat("Imaging.SettingsSplit",.55f),.15f,.85f);
            imageToolSettingsExpanded=StudioPreferenceStore.GetFloat("Imaging.ToolSettingsExpanded",1)>.5f;
            imagePanelWidth=StudioPreferenceStore.GetFloat("Imaging.PanelWidth",340);
            wantsMouseMove = true;
            wantsMouseEnterLeaveWindow = true;
            EnableImaging();
            EditorApplication.update += EditorUpdate;
            EnsureCompositeMaterial();
            RefreshTextureProperties();
            // Never retain an SDK texture as a writable output, including paths serialized by older versions.
            if (IsMashBoxSdkAssetPath(outputAssetPath))
                outputAssetPath = null;
            if (!string.IsNullOrEmpty(outputAssetPath))
                savedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(outputAssetPath);
            TryAutoOpenCurrentStickerMap();
            compositeDirty = true;
        }

        private void OnDisable()
        {
            RestoreImageUvMeshes();
            DisableImaging();
            EditorApplication.update -= EditorUpdate;
            RestoreLivePreview();
            ReleaseComposite();
            if (compositeMaterial != null)
                DestroyImmediate(compositeMaterial);
            compositeMaterial = null;
            foreach (Texture2D texture in ownedTextures.Where(texture => texture != null))
                DestroyImmediate(texture);
            ownedTextures.Clear();
        }

        private void OnGUI()
        {
            using (StudioTheme.Scope()) {
            EditorGUI.DrawRect(new Rect(0,0,position.width,position.height),StudioTheme.Current.Panel);
            BeginImageInput();
            EnsureCompositeMaterial();
            DrawToolbar();
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawImageToolRail();
                DrawCanvasPanel();
                DrawImageMaterialDock();
                DrawImagePanelWidthDivider();
                using (new EditorGUILayout.VerticalScope(StudioTheme.Styles.modelPanel, GUILayout.Width(ImagePanelWidth), GUILayout.ExpandHeight(true)))
                {
                    DrawInspectorPanel();
                    DrawImagePanelDivider();
                    DrawLayersPanel();
                }
            }
            DrawStatus();
            EndImageInput();
            }
        }

        private void DrawToolbar() => DrawImageToolbar();

        private const string ImageLayerDragKey="MashBox.Imaging.Layer";
        private sealed class ImageLayerDrag { internal StudioImagingWindow owner; internal StickerLayer layer; }
        private StickerLayer layerDragCandidate;
        private Vector2 layerDragStart;
        private int layerDropSlot=-1;
        private void HandleImageLayerReorder(Rect row,int index)
        {
            var e=Event.current;
            if(e.type==EventType.MouseDown && e.button==0 && row.Contains(e.mousePosition) && e.mousePosition.x>=row.x+26)
            {layerDragCandidate=layers[index];layerDragStart=e.mousePosition;}
            if(e.type==EventType.MouseDrag && e.button==0 && layerDragCandidate!=null && Vector2.Distance(e.mousePosition,layerDragStart)>5)
            {
                GUIUtility.hotControl=0;
                DragAndDrop.PrepareStartDrag();DragAndDrop.objectReferences=Array.Empty<UnityEngine.Object>();
                DragAndDrop.SetGenericData(ImageLayerDragKey,new ImageLayerDrag{owner=this,layer=layerDragCandidate});
                DragAndDrop.StartDrag("Reorder "+layerDragCandidate.name);layerDragCandidate=null;e.Use();
            }
            var drag=DragAndDrop.GetGenericData(ImageLayerDragKey) as ImageLayerDrag;
            if(drag==null || drag.owner!=this)return;
            if((e.type==EventType.DragUpdated || e.type==EventType.DragPerform) && row.Contains(e.mousePosition))
            {
                // Rows are displayed in reverse compositing order.
                layerDropSlot=e.mousePosition.y<row.center.y?index+1:index;
                DragAndDrop.visualMode=DragAndDropVisualMode.Move;
                if(e.type==EventType.DragPerform)
                {
                    int slot=layerDropSlot;var moved=drag.layer;
                    DragAndDrop.AcceptDrag();DragAndDrop.SetGenericData(ImageLayerDragKey,null);layerDropSlot=-1;
                    EditorApplication.delayCall+=()=>{if(this)ReorderImageLayer(moved,slot);};
                }
                e.Use();Repaint();
            }
            if(e.type==EventType.Repaint && layerDropSlot>=0)
            {
                if(layerDropSlot==index+1)EditorGUI.DrawRect(new Rect(row.x,row.y-1,row.width,2),StudioTheme.Current.Accent);
                else if(layerDropSlot==0 && index==0)EditorGUI.DrawRect(new Rect(row.x,row.yMax-1,row.width,2),StudioTheme.Current.Accent);
            }
            if(e.type==EventType.DragExited){layerDropSlot=-1;layerDragCandidate=null;Repaint();}
        }
        private void ReorderImageLayer(StickerLayer layer,int insertionSlot)
        {
            int source=layers.IndexOf(layer);if(source<0)return;
            int target=Mathf.Clamp(insertionSlot,0,layers.Count);
            if(target>source)target--;
            if(target==source)return;
            inputSnapshot=CaptureImage();
            layers.RemoveAt(source);layers.Insert(target,layer);selectedLayer=target;
            MarkCompositeDirty();inputSnapshot=null;
        }

        private void DrawLayersPanel()
        {
            using (var panel=new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                if(Event.current.type==EventType.Repaint)
                {
                    EditorGUI.DrawRect(panel.rect,StudioTheme.Current.Header);
                    EditorGUI.DrawRect(new Rect(panel.rect.x,panel.rect.y,panel.rect.width,1),StudioTheme.Current.Border);
                }
                DrawImageLayerTabs();
                if(imageChannelsTab){DrawImageChannelsPanel();return;}
                DrawImageLayerBlendControls();
                layerScroll = EditorGUILayout.BeginScrollView(layerScroll, GUIStyle.none, GUILayout.ExpandHeight(true));
                if (layers.Count == 0)
                {
                    GUILayout.Space(24);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(new GUIContent("Add Layer", "Add a paint layer"), StudioTheme.Styles.overlayButton, GUILayout.Width(120), GUILayout.Height(30)))
                            AddPaintLayer();
                        GUILayout.FlexibleSpace();
                    }
                }
                for (int index = layers.Count - 1; index >= 0; index--)
                {
                    StickerLayer layer = layers[index];
                    var row=GUILayoutUtility.GetRect(10,44,GUILayout.ExpandWidth(true));
                    EditorGUI.DrawRect(row,StudioTheme.Current.Panel);
                    if(selectedLayer==index)EditorGUI.DrawRect(new Rect(row.x+26,row.y,Mathf.Max(0,row.width-26),row.height),StudioTheme.Current.Selection);
                    EditorGUI.DrawRect(new Rect(row.x+25,row.y,1,row.height),StudioTheme.Current.Border);
                    EditorGUI.DrawRect(new Rect(row.x,row.yMax-1,row.width,1),StudioTheme.Current.Border);
                    if((Event.current.type==EventType.ContextClick || (Event.current.type==EventType.MouseDown && Event.current.button==1)) && row.Contains(Event.current.mousePosition))
                    {
                        selectedLayer=index;
                        var menu=new StudioMenu();
                        AddImageMenuItem(menu,"New Layer",AddPaintLayer);
                        menu.AddSeparator("");
                        AddImageMenuItem(menu,"Move Up",()=>MoveLayer(1),index<layers.Count-1);
                        AddImageMenuItem(menu,"Move Down",()=>MoveLayer(-1),index>0);
                        menu.AddSeparator("");
                        if(layer.isText)
                        {
                            AddImageMenuItem(menu,"Edit Text",EditImageText,!layer.locked);
                            AddImageMenuItem(menu,"Rasterize Text",RasterizeImageText,!layer.locked);
                            menu.AddSeparator("");
                        }
                        AddImageMenuItem(menu,"Duplicate",DuplicateSelectedLayer);
                        AddImageMenuItem(menu,"Delete",DeleteSelectedLayer,!layer.locked);
                        // Open after this scroll-view event ends; capture screen coordinates before leaving its GUI group.
                        var anchor=new Rect(GUIUtility.GUIToScreenPoint(Event.current.mousePosition),Vector2.one);
                        Event.current.Use();Repaint();
                        EditorApplication.delayCall+=()=>{if(this)StudioMenuPopup.Open(menu,anchor);};
                    }
                    if(renamingImageLayer!=layer)HandleImageLayerReorder(row,index);
                    var eye=new Rect(row.x+6,row.center.y-8,18,16);
                    bool visible=GUI.Toggle(eye,layer.visible,new GUIContent("","Show or hide layer"));
                    if(visible!=layer.visible){layer.visible=visible;MarkCompositeDirty();}
                    var thumb=new Rect(row.x+28,row.y+4,36,36);
                    EditorGUI.DrawRect(thumb,StudioTheme.Current.Header);
                    if(layer.texture)GUI.DrawTexture(thumb,layer.texture,ScaleMode.ScaleToFit,true);
                    var label=new Rect(row.x+70,row.y+4,row.width-74,36);
                    DrawImageLayerName(row,label,index);
                }
                if(Event.current.rawType==EventType.MouseUp)layerDragCandidate=null;
                EditorGUILayout.EndScrollView();

                DrawImageLayerBottomBar();
                if((Event.current.type==EventType.ContextClick || (Event.current.type==EventType.MouseDown && Event.current.button==1)) && panel.rect.Contains(Event.current.mousePosition))
                {
                    var menu=new StudioMenu();
                    AddImageMenuItem(menu,"New Layer",AddPaintLayer);
                    var anchor=new Rect(GUIUtility.GUIToScreenPoint(Event.current.mousePosition),Vector2.one);
                    Event.current.Use();Repaint();
                    EditorApplication.delayCall+=()=>{if(this)StudioMenuPopup.Open(menu,anchor);};
                }
            }
        }

        private void DrawCanvasPanel() => DrawImageCanvas();

        [SerializeField] private bool showImageLiveLink;
        [SerializeField] private bool imageSelectionLocked;
        private Vector2 imageMaterialScroll;

        private void DrawImageMaterialDock()
        {
            if(!showImageLiveLink)return;

            using(var panel=new EditorGUILayout.VerticalScope(GUILayout.Width(300),GUILayout.ExpandHeight(true)))
            {
                if(Event.current.type==EventType.Repaint)
                {
                    EditorGUI.DrawRect(panel.rect,StudioTheme.Current.Panel);
                    EditorGUI.DrawRect(new Rect(panel.rect.x,panel.rect.y,1,panel.rect.height),StudioTheme.Current.Border);
                }
                var header=GUILayoutUtility.GetRect(0,26,GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(header,StudioTheme.Current.Header);
                EditorGUI.DrawRect(new Rect(header.x,header.yMax-1,header.width,1),StudioTheme.Current.Border);
                GUI.Label(new Rect(header.x+8,header.y+1,header.width-38,24),"MATERIAL / LIVE LINK",StudioTheme.Styles.boldLabel);
                if(GUI.Button(new Rect(header.xMax-27,header.y+1,24,24),new GUIContent("×","Close material panel"),StudioTheme.Styles.overlayButton))
                    showImageLiveLink=false;
                using(new EditorGUILayout.VerticalScope(StudioTheme.Styles.modelPanel,GUILayout.ExpandHeight(true)))
                {
                    imageMaterialScroll=EditorGUILayout.BeginScrollView(imageMaterialScroll);
                    float previousLabelWidth=EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth=120;
                    try { DrawMeshAndMaterialInspector(); }
                    finally
                    {
                        EditorGUIUtility.labelWidth=previousLabelWidth;
                        EditorGUILayout.EndScrollView();
                    }
                }
            }
        }

        private void OnSelectionChange()
        {
            Repaint();
        }

        private static Renderer SelectedImageRenderer()
        {
            var root=Selection.activeGameObject;
            if(!root)return null;
            var selected=Selection.activeObject as Renderer;
            if(selected && selected.sharedMaterials.Any(material=>material))return selected;
            return root.GetComponentsInChildren<Renderer>(true)
                .FirstOrDefault(renderer=>renderer.sharedMaterials.Any(material=>material));
        }

        private void LinkImageSelection()
        {
            if(surfaceRenderers!=null)return;
            var material=Selection.activeObject as Material;
            var renderer=material?null:SelectedImageRenderer();
            if(!material && !renderer)return;
            if(material)
            {
                if(directMaterial!=material)
                {
                    RestoreLivePreview();
                    directMaterial=material;
                    RefreshTextureProperties();
                    TryAutoOpenCurrentStickerMap();
                    MarkCompositeDirty();
                }
            }
            else SetTargetRoot(Selection.activeGameObject,renderer);
            imageSelectionLocked=true;

            MarkCompositeDirty();
            Repaint();
        }

        private void UnlinkImageMaterial()
        {
            RestoreImageUvMeshes(); imageUvMode = ImageUvMode.Paint; imageUvTarget = null;
            EndSurfaceInput();
            RestoreLivePreview();
            directMaterial=null;
            targetRoot=null;
            targetRenderer=null;
            surfaceRenderers=null;
            surfaceSuspended=false;
            SurfaceChanged=null;
            materialIndex=0;
            imageSelectionLocked=false;

            observedBaseMaterial=null;
            observedBaseProperty=null;
            observedBaseTexture=null;
            RefreshTextureProperties();
            compositeDirty=true;
            Repaint();
        }

        private void DrawImageSelectionLink()
        {
            bool linked = CurrentMaterial;
            using(new EditorGUI.DisabledScope(!linked && !(Selection.activeObject is Material) && !SelectedImageRenderer()))
            {
                if(GUILayout.Button(new GUIContent(linked ? "Unlink" : "Link Selection", linked
                    ? "Disconnect this material and restore its normal preview"
                    : "Capture the selected object's material and keep this target when scene selection changes"),GUILayout.Height(28)))
                {
                    if (linked) UnlinkImageMaterial(); else LinkImageSelection();
                }
            }
            linked = CurrentMaterial;
            using(new EditorGUILayout.HorizontalScope())
            {
                if (linked)
                {
                    var dot = GUILayoutUtility.GetRect(10, 14, GUILayout.Width(10));
                    var previous = Handles.color;
                    Handles.color = new Color(.3f, .9f, .5f);
                    Handles.DrawSolidDisc(new Vector3(dot.center.x, dot.center.y, 0), Vector3.forward, 3);
                    Handles.color = previous;
                }
                GUILayout.Label(linked ? (imageSelectionLocked ? "Selection linked" : "Material linked")
                    : "Select an object, then click Link Selection",StudioTheme.Styles.miniLabel);
            }
            EditorGUILayout.Space(6);
        }

        private float imagePanelWidth=340, imageWidthStartX, imageWidthStart, imageWidthOriginal;
        private bool resizingImageWidth;
        private int imageWidthControl;
        private float ImagePanelWidthLimit => Mathf.Max(240,position.width-380-(showImageLiveLink?300:0));
        private float ImagePanelWidth => Mathf.Clamp(imagePanelWidth,240,ImagePanelWidthLimit);
        private void DrawImagePanelWidthDivider()
        {
            var rect=GUILayoutUtility.GetRect(6,0,GUILayout.Width(6),GUILayout.ExpandHeight(true));
            var e=Event.current;int id=GUIUtility.GetControlID("Imaging panel width".GetHashCode(),FocusType.Passive);
            EditorGUIUtility.AddCursorRect(rect,MouseCursor.ResizeHorizontal);
            if(e.type==EventType.Repaint)
            {
                bool active=resizingImageWidth || rect.Contains(e.mousePosition);
                var palette=StudioTheme.Current;
                EditorGUI.DrawRect(rect,active?palette.Hover:palette.Button);
                for(int i=-2;i<=2;i++)EditorGUI.DrawRect(new Rect(rect.center.x-2,Mathf.Floor(rect.center.y)+i*4,4,1),active?palette.Text:palette.Muted);
            }
            if(e.type==EventType.MouseDown && e.button==0 && rect.Contains(e.mousePosition) && GUIUtility.hotControl==0)
            {
                resizingImageWidth=true;imageWidthControl=id;GUIUtility.hotControl=id;GUIUtility.keyboardControl=0;
                imageWidthStartX=GUIUtility.GUIToScreenPoint(e.mousePosition).x;imageWidthStart=ImagePanelWidth;imageWidthOriginal=imagePanelWidth;e.Use();
            }
            if(!resizingImageWidth)return;
            if(GUIUtility.hotControl!=imageWidthControl){resizingImageWidth=false;return;}
            if(e.type==EventType.MouseDrag)
            {
                imagePanelWidth=Mathf.Clamp(imageWidthStart+imageWidthStartX-GUIUtility.GUIToScreenPoint(e.mousePosition).x,240,ImagePanelWidthLimit);
                e.Use();Repaint();
            }
            else if(e.rawType==EventType.MouseUp && e.button==0)
            {
                resizingImageWidth=false;GUIUtility.hotControl=0;StudioPreferenceStore.SetFloat("Imaging.PanelWidth",imagePanelWidth);e.Use();Repaint();
            }
            else if(e.type==EventType.KeyDown && e.keyCode==KeyCode.Escape)
            {
                imagePanelWidth=imageWidthOriginal;resizingImageWidth=false;GUIUtility.hotControl=0;e.Use();Repaint();
            }
        }

        private float imageSettingsRatio=.55f, imageSplitStartY, imageSplitStartHeight, imageSplitOriginalRatio;
        private int imageSplitControl;
        private bool resizingImagePanels;
        private float ImagePanelAvailableHeight => Mathf.Max(300,position.height-100);
        private float ImageSettingsHeight => Mathf.Clamp(ImagePanelAvailableHeight*imageSettingsRatio,140,ImagePanelAvailableHeight-140);
        private void DrawImagePanelDivider()
        {
            var rect=GUILayoutUtility.GetRect(0,10,GUILayout.ExpandWidth(true));
            int id=GUIUtility.GetControlID("Imaging panel divider".GetHashCode(),FocusType.Passive);
            var e=Event.current;
            EditorGUIUtility.AddCursorRect(rect,MouseCursor.ResizeVertical);
            if(e.type==EventType.Repaint)
            {
                var palette=StudioTheme.Current;
                bool highlighted=resizingImagePanels||rect.Contains(e.mousePosition);
                EditorGUI.DrawRect(rect,highlighted?palette.Hover:palette.Button);
                var color=highlighted?palette.Text:palette.Muted;
                for(int i=-2;i<=2;i++)EditorGUI.DrawRect(new Rect(Mathf.Floor(rect.center.x)+i*4,rect.center.y-3,1,6),color);
            }
            if(e.type==EventType.MouseDown && e.button==0 && rect.Contains(e.mousePosition) && GUIUtility.hotControl==0)
            {
                resizingImagePanels=true;imageSplitControl=id;GUIUtility.hotControl=id;GUIUtility.keyboardControl=0;
                imageSplitStartY=GUIUtility.GUIToScreenPoint(e.mousePosition).y;
                imageSplitStartHeight=ImageSettingsHeight;imageSplitOriginalRatio=imageSettingsRatio;
                e.Use();
            }
            if(!resizingImagePanels)return;
            if(GUIUtility.hotControl!=imageSplitControl){resizingImagePanels=false;return;}
            if(e.type==EventType.MouseDrag)
            {
                float height=imageSplitStartHeight+GUIUtility.GUIToScreenPoint(e.mousePosition).y-imageSplitStartY;
                imageSettingsRatio=Mathf.Clamp(height,140,ImagePanelAvailableHeight-140)/ImagePanelAvailableHeight;
                e.Use();Repaint();
            }
            else if(e.rawType==EventType.MouseUp && e.button==0)
            {
                resizingImagePanels=false;GUIUtility.hotControl=0;
                StudioPreferenceStore.SetFloat("Imaging.SettingsSplit",imageSettingsRatio);e.Use();Repaint();
            }
            else if(e.type==EventType.KeyDown && e.keyCode==KeyCode.Escape)
            {
                imageSettingsRatio=imageSplitOriginalRatio;resizingImagePanels=false;GUIUtility.hotControl=0;e.Use();Repaint();
            }
        }

        private void DrawInspectorPanel()
        {
            // Reserve the lower part of the right dock for the independently scrolling layer stack.
            using (new EditorGUILayout.VerticalScope(GUILayout.Height(ImageSettingsHeight)))
            {
                inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
                DrawImageColorRangeSettings();DrawImageAdjustment();
                DrawImageToolSettings();
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawMeshAndMaterialInspector()
        {
            if (surfaceRenderers != null)
            {
                EditorGUILayout.LabelField("Painting texture", CurrentTextureProperty ?? "No texture");
                EditorGUILayout.HelpBox("Choose the mesh and texture slot in Studio's Painting workspace. This canvas edits the same layers.", MessageType.Info);
                return;
            }
            DrawImageSelectionLink();
            DrawDirectMaterial();
            using(new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Shader",CurrentMaterial?CurrentMaterial.shader:null,typeof(Shader),false);
            if(!directMaterial)
            {
                EditorGUILayout.LabelField("Object",targetRoot?targetRoot.name:"None");
                EditorGUILayout.LabelField("Renderer",targetRenderer?targetRenderer.name:"None");
            }

            EditorGUI.BeginChangeCheck();
            rendererScope = (RendererScope)EditorGUILayout.EnumPopup("Apply scope", rendererScope);
            if (EditorGUI.EndChangeCheck())
            {
                RestoreLivePreview();
                MarkCompositeDirty();
            }

            Material[] materials = targetRenderer != null ? targetRenderer.sharedMaterials : Array.Empty<Material>();
            string[] materialLabels = materials.Select((material, index) =>
                $"{index}: {(material != null ? material.name : "Missing")}").ToArray();
            if(!directMaterial)
            using (new EditorGUI.DisabledScope(materialLabels.Length == 0))
            {
                EditorGUI.BeginChangeCheck();
                materialIndex = materialLabels.Length > 0
                    ? EditorGUILayout.Popup("Material slot", Mathf.Clamp(materialIndex, 0, materialLabels.Length - 1), materialLabels)
                    : 0;
                if (EditorGUI.EndChangeCheck())
                {
                    RestoreLivePreview();
                    RefreshTextureProperties();
                    TryAutoOpenCurrentStickerMap();
                    MarkCompositeDirty();
                }
            }

            DrawShaderImagePicker();
        }

        private void DrawStatus()
        {
            if (string.IsNullOrEmpty(status))
                return;
            EditorGUILayout.HelpBox(status, statusType);
        }

        private bool HasSelectedLayer => selectedLayer >= 0 && selectedLayer < layers.Count;

        private Material CurrentMaterial
        {
            get
            {
                if (directMaterial) return directMaterial;
                if (targetRenderer == null)
                    return null;
                Material[] materials = targetRenderer.sharedMaterials;
                return materialIndex >= 0 && materialIndex < materials.Length ? materials[materialIndex] : null;
            }
        }

        private string CurrentTextureProperty =>
            texturePropertyIndex >= 0 && texturePropertyIndex < texturePropertyNames.Length
                ? texturePropertyNames[texturePropertyIndex]
                : null;

        private void EnsureCompositeMaterial()
        {
            if (compositeMaterial != null)
                return;
            Shader shader = Shader.Find(CompositeShaderName);
            if (shader == null)
            {
                status = $"Required shader '{CompositeShaderName}' is still importing. Reopen this window after Unity finishes compiling.";
                statusType = MessageType.Warning;
                return;
            }
            compositeMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        private void EnsureComposite()
        {
            int size = CanvasSize;
            if (composite != null && composite.width == size && composite.height == size)
                return;
            ReleaseComposite();
            composite = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "MashBox 2D Imaging Composite",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            composite.Create();
            compositeDirty = true;
        }

        private void ReleaseComposite()
        {
            ReleaseImageChannelTextures();
            if (composite == null)
                return;
            RestoreLivePreview();
            composite.Release();
            DestroyImmediate(composite);
            composite = null;
        }

        private void RenderComposite() => RenderImageComposite();

        private void DrawCheckerboard(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f, 1f));
            const int cells = 16;
            float size = rect.width / cells;
            Color light = new Color(0.32f, 0.32f, 0.32f, 1f);
            for (int y = 0; y < cells; y++)
            for (int x = 0; x < cells; x++)
            {
                if (((x + y) & 1) == 0)
                    continue;
                EditorGUI.DrawRect(new Rect(rect.x + x * size, rect.y + y * size, size + 1f, size + 1f), light);
            }
        }

        private void DrawUvOverlay(Rect canvasRect)
        {
            if (!showUvOverlay || targetRenderer == null || Event.current.type != EventType.Repaint)
                return;

            try
            {
                Handles.BeginGUI();
                Handles.color = uvOverlayColor;
                foreach (TargetBinding target in GetTargetBindings())
                {
                    Mesh mesh = GetRendererMesh(target.renderer);
                    if (mesh == null || mesh.subMeshCount == 0)
                        continue;
                    var uvs = new List<Vector2>();
                    mesh.GetUVs((int)uvChannel, uvs);
                    if (uvs.Count != mesh.vertexCount)
                        continue;
                    int subMesh = Mathf.Clamp(target.materialIndex, 0, mesh.subMeshCount - 1);
                    Handles.DrawLines(QuadUvShellLines(mesh, subMesh, uvs, canvasRect));
                }
                Handles.EndGUI();
            }
            catch (Exception exception)
            {
                status = "Could not draw this UV channel. Enable Read/Write on the mesh if necessary. " + exception.Message;
                statusType = MessageType.Warning;
            }
        }

        private void DrawSelectedLayerOutline(Rect canvasRect)
        {
            if (!HasSelectedLayer || (imageTool != ImageTool.Move && imageTool != ImageTool.Transform) || Event.current.type != EventType.Repaint)
                return;
            StickerLayer layer = layers[selectedLayer];
            if (layer.texture == null)
                return;
            var points=ImageTransformPoints(canvasRect,layer);
            Vector3[] corners = {points[0],points[1],points[2],points[3],points[0]};
            DrawImageIconLine(StudioTheme.Current.Accent,1f,corners);
        }

        private void HandleCanvasInput(Rect canvasRect)
        {
            Event current = Event.current;
            bool inside = canvasRect.Contains(current.mousePosition);
            if(EditorGUIUtility.editingTextField) return;
            if(HandleImageTools(canvasRect)) return;
            if (inside && current.type == EventType.KeyDown && current.control && current.keyCode == KeyCode.V)
            {
                PasteClipboardImage();
                current.Use();
                return;
            }
            if (inside && current.type == EventType.ScrollWheel && HasSelectedLayer && !layers[selectedLayer].locked)
            {
                layers[selectedLayer].scale = Mathf.Clamp(
                    layers[selectedLayer].scale * Mathf.Exp(-current.delta.y * 0.05f),
                    0.001f,
                    100f);
                MarkCompositeDirty();
                current.Use();
                return;
            }
            if (inside && current.type == EventType.MouseDown && current.button == 0)
            {
                Vector2 canvasPoint = GuiToCanvas(current.mousePosition, canvasRect);
                int hit = HitTestLayers(canvasPoint);
                if (hit >= 0 && !layers[hit].locked)
                {
                    selectedLayer = hit;
                    draggingLayer = true;
                    dragOffsetPixels = layers[hit].position - canvasPoint;
                    GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                    current.Use();
                }
            }
            if (draggingLayer && current.type == EventType.MouseDrag && current.button == 0 && HasSelectedLayer)
            {
                layers[selectedLayer].position = GuiToCanvas(current.mousePosition, canvasRect) + dragOffsetPixels;
                MarkCompositeDirty();
                current.Use();
            }
            if (draggingLayer && current.rawType == EventType.MouseUp)
            {
                draggingLayer = false;
                GUIUtility.hotControl = 0;
                current.Use();
            }
            if (HasSelectedLayer && !layers[selectedLayer].locked && current.type == EventType.KeyDown)
            {
                Vector2 movement = Vector2.zero;
                if (current.keyCode == KeyCode.LeftArrow) movement.x = -1f;
                if (current.keyCode == KeyCode.RightArrow) movement.x = 1f;
                if (current.keyCode == KeyCode.UpArrow) movement.y = -1f;
                if (current.keyCode == KeyCode.DownArrow) movement.y = 1f;
                if (movement != Vector2.zero)
                {
                    layers[selectedLayer].position += movement * (current.shift ? 10f : 1f);
                    MarkCompositeDirty();
                    current.Use();
                }
            }
        }

        private void HandleCanvasDragAndDrop(Rect canvasRect)
        {
            Event current = Event.current;
            if (!canvasRect.Contains(current.mousePosition) ||
                (current.type != EventType.DragUpdated && current.type != EventType.DragPerform))
                return;

            bool hasImage = DragAndDrop.objectReferences.OfType<Texture2D>().Any() ||
                            DragAndDrop.paths.Any(IsSupportedImagePath);
            if (!hasImage)
                return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                var addedAssets = new HashSet<Texture2D>();
                foreach (Texture2D texture in DragAndDrop.objectReferences.OfType<Texture2D>())
                {
                    AddLayer(texture, texture.name, GuiToCanvas(current.mousePosition, canvasRect));
                    addedAssets.Add(texture);
                }
                foreach (string path in DragAndDrop.paths)
                {
                    Texture2D asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (asset != null && addedAssets.Contains(asset))
                        continue;
                    if (asset != null)
                        AddLayer(asset, asset.name, GuiToCanvas(current.mousePosition, canvasRect));
                    else if (IsSupportedImagePath(path))
                        AddExternalImage(path, GuiToCanvas(current.mousePosition, canvasRect));
                }
                current.Use();
            }
        }

        private void AddExternalFiles()
        {
            string path = EditorUtility.OpenFilePanelWithFilters(
                "Add sticker image",
                string.Empty,
                new[] { "Images", "png,jpg,jpeg", "All files", "*" });
            if (!string.IsNullOrEmpty(path))
                AddExternalImage(path, Vector2.one * (CanvasSize * 0.5f));
        }

        private void AddExternalImage(string path, Vector2 position)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
                {
                    name = Path.GetFileNameWithoutExtension(path),
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (!texture.LoadImage(bytes, false))
                {
                    DestroyImmediate(texture);
                    throw new InvalidOperationException("Unity could not decode this image format.");
                }
                ownedTextures.Add(texture);
                AddLayer(texture, texture.name, position);
            }
            catch (Exception exception)
            {
                status = "Could not add external image: " + exception.Message;
                statusType = MessageType.Error;
            }
        }

        private void PasteClipboardImage()
        {
            PasteImageSelection();
        }
        private void AddLayer(Texture2D texture, string layerName, Vector2? position = null)
        {
            if (texture == null)
                return;
            var layer = new StickerLayer
            {
                texture = texture,
                name = string.IsNullOrWhiteSpace(layerName) ? "Layer" : layerName,
                position = position ?? Vector2.one * (CanvasSize * 0.5f)
            };
            FitLayer(layer, false);
            layers.Add(layer);
            selectedLayer = layers.Count - 1;
            MarkCompositeDirty();
        }

        private Texture2D GetCurrentStickerTexture()
        {
            Material material = CurrentMaterial;
            string property = CurrentTextureProperty;
            return material != null && !string.IsNullOrEmpty(property) && material.HasProperty(property)
                ? material.GetTexture(property) as Texture2D
                : null;
        }

        private Material observedBaseMaterial;
        private string observedBaseProperty;
        private Texture2D observedBaseTexture;
        private bool observedAutoOpen;

        // Material assets can become available after OnEnable (especially after reload).
        // Observe target changes, not empty layers, so deleting a layer or New stays empty.
        private void UpdateLinkedBaseImage()
        {
            var material = CurrentMaterial;
            var property = CurrentTextureProperty;
            var texture = GetCurrentStickerTexture();
            bool changed = material != observedBaseMaterial || property != observedBaseProperty ||
                texture != observedBaseTexture || autoOpenCurrentMap != observedAutoOpen;
            observedBaseMaterial = material;
            observedBaseProperty = property;
            observedBaseTexture = texture;
            observedAutoOpen = autoOpenCurrentMap;
            if (changed && texture) TryAutoOpenCurrentStickerMap();
        }

        private void TryAutoOpenCurrentStickerMap()
        {
            if (autoOpenCurrentMap && layers.Count == 0)
                OpenCurrentStickerMap(true, true);
        }

        private void OpenCurrentStickerMap(bool replaceCanvas, bool automatic)
        {
            Texture2D texture = GetCurrentStickerTexture();
            if (texture == null)
            {
                if (!automatic)
                {
                    status = "The selected material property does not currently contain a Texture2D.";
                    statusType = MessageType.Warning;
                }
                return;
            }

            if (replaceCanvas && layers.Count > 0 && !automatic && !EditorUtility.DisplayDialog(
                    "Open current image",
                    "Replace the current layer stack with the image assigned to this material? Unsaved layer edits will be cleared.",
                    "Open Map",
                    "Cancel"))
                return;

            RestoreLivePreview();
            if (replaceCanvas)
            {
                layers.Clear();
                selectedLayer = -1;
            }

            if (texture.width == texture.height)
            {
                int matchingResolution = Array.IndexOf(CanvasResolutions, texture.width);
                if (matchingResolution >= 0 && matchingResolution != resolutionIndex)
                {
                    resolutionIndex = matchingResolution;
                    ReleaseComposite();
                }
            }

            var layer = new StickerLayer
            {
                texture = texture,
                name = texture.name + (replaceCanvas ? " (Existing Base)" : ""),
                position = Vector2.one * (CanvasSize * 0.5f),
                scale = texture.width == texture.height ? CanvasSize / (float)Mathf.Max(1, texture.width) : 1f,
                sourceRect = FullSourceRect
            };
            layers.Add(layer);
            selectedLayer = layers.Count - 1;

            if (replaceCanvas)
            {
                outputAssetPath = null;
                savedTexture = texture;
            }

            status = replaceCanvas
                ? $"Opened {texture.name} as the editable base layer." +
                  (IsMashBoxSdkAssetPath(AssetDatabase.GetAssetPath(texture))
                      ? " MashBox SDK assets are protected, so Save will ask for a new PNG location."
                      : string.IsNullOrEmpty(outputAssetPath)
                          ? " Its source is not a PNG, so Save will create a new PNG rather than overwrite it."
                          : " Update PNG will save back to this asset.")
                : $"Added {texture.name} as another layer.";
            statusType = MessageType.Info;
            MarkCompositeDirty();
        }

        private void FitLayer(StickerLayer layer, bool recenter = true)
        {
            if (layer?.texture == null)
                return;
            float maximum = CanvasSize * 0.7f;
            Vector2 croppedSize = CroppedPixelSize(layer);
            layer.scale = Mathf.Min(1f, maximum / Mathf.Max(croppedSize.x, croppedSize.y));
            if (recenter)
                layer.position = Vector2.one * (CanvasSize * 0.5f);
        }

        private void MoveLayer(int direction)
        {
            if (!HasSelectedLayer)
                return;
            int destination = Mathf.Clamp(selectedLayer + direction, 0, layers.Count - 1);
            if (destination == selectedLayer)
                return;
            StickerLayer layer = layers[selectedLayer];
            layers.RemoveAt(selectedLayer);
            layers.Insert(destination, layer);
            selectedLayer = destination;
            MarkCompositeDirty();
        }

        private void DuplicateSelectedLayer()
        {
            if (!HasSelectedLayer)
                return;
            StickerLayer source = layers[selectedLayer];
            var copy = new StickerLayer
            {
                name = source.name + " Copy",
                isText=source.isText,textContent=source.textContent,textFont=source.textFont,textSize=source.textSize,textStyle=source.textStyle,textAlignment=source.textAlignment,textColor=source.textColor,textRasterVersion=source.textRasterVersion,
                texture = source.texture,
                visible = source.visible,
                position = source.position + new Vector2(20f, 20f),
                scale = source.scale,
                rotation = source.rotation,
                corner0=source.corner0, corner1=source.corner1, corner2=source.corner2, corner3=source.corner3,
                opacity = source.opacity,
                tint = source.tint,
                blackToAlpha = source.blackToAlpha,
                blendMode=source.blendMode, exposure=source.exposure, saturation=source.saturation,
                sourceRect = source.sourceRect
            };
            layers.Insert(selectedLayer + 1, copy);
            selectedLayer++;
            MarkCompositeDirty();
        }

        private void DeleteSelectedLayer()
        {
            if (!HasSelectedLayer)
                return;
            if(layers[selectedLayer].locked) return;
            layers.RemoveAt(selectedLayer);
            selectedLayer = Mathf.Clamp(selectedLayer - 1, -1, layers.Count - 1);
            MarkCompositeDirty();
        }

        private void NewDocument()
        {
            if (layers.Count > 0 && !EditorUtility.DisplayDialog(
                    "New image document",
                    "Clear every current layer? Saved PNG assets will not be deleted.",
                    "Clear Layers",
                    "Cancel"))
                return;
            UnlinkImageMaterial();
            CancelImageAdjustment();CancelImageColorRange();CancelSelectionTrace();SetImageSelection(null,0,new Rect());imageAdjustment=ImageAdjustment.None;
            zoom=1;
            pan=Vector2.zero;
            uvChannel=MeshUvChannel.UV0;
            imageChannel=ImageChannel.RGB;
            imageChannelsTab=false;
            layers.Clear();
            documentPath=null;
            selectedLayer = -1;
            outputAssetPath = null;
            savedTexture = null;
            status=null;
            statusType=MessageType.None;
            MarkCompositeDirty();
        }

        private void ChangeResolution(int changedIndex)
        {
            int oldSize = CanvasSize;
            resolutionIndex = Mathf.Clamp(changedIndex, 0, CanvasResolutions.Length - 1);
            int newSize = CanvasSize;
            if (oldSize != newSize && oldSize > 0)
            {
                float ratio = newSize / (float)oldSize;
                foreach (StickerLayer layer in layers)
                {
                    layer.position *= ratio;
                    layer.scale *= ratio;
                }
            }
            ReleaseComposite();
            MarkCompositeDirty();
        }

        private bool SaveComposite(bool allowPathPrompt)
        {
            if (layers.Count == 0)
            {
                status = "Add at least one image layer before saving.";
                statusType = MessageType.Warning;
                return false;
            }
            string currentSlotPath = AssetDatabase.GetAssetPath(GetCurrentStickerTexture());
            bool protectedSdkOutput = IsMashBoxSdkAssetPath(outputAssetPath) ||
                                      (string.IsNullOrEmpty(outputAssetPath) && IsMashBoxSdkAssetPath(currentSlotPath));
            if (protectedSdkOutput)
                outputAssetPath = null;
            if (string.IsNullOrEmpty(outputAssetPath))
            {
                if (!allowPathPrompt)
                    return false;
                outputAssetPath = EditorUtility.SaveFilePanelInProject(
                    protectedSdkOutput ? "Save a copy of the MashBox SDK image" : "Save image",
                    DefaultOutputName,
                    "png",
                    protectedSdkOutput
                        ? "MashBox SDK textures cannot be overwritten. Choose a new PNG asset path under Assets."
                        : "Choose a PNG asset path for this image document.");
                if (string.IsNullOrEmpty(outputAssetPath))
                    return false;
            }

            // Defense in depth: do not write if a package path somehow gets returned or assigned later.
            if (IsMashBoxSdkAssetPath(outputAssetPath))
            {
                outputAssetPath = null;
                status = "MashBox SDK textures are protected. Choose a new PNG location under Assets.";
                statusType = MessageType.Warning;
                return false;
            }

            try
            {
                if (compositeDirty)
                    RenderComposite();
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = composite;
                var readable = new Texture2D(CanvasSize, CanvasSize, TextureFormat.RGBA32, false, false);
                readable.ReadPixels(new Rect(0f, 0f, CanvasSize, CanvasSize), 0, 0, false);
                readable.Apply(false, false);
                byte[] png = readable.EncodeToPNG();
                DestroyImmediate(readable);
                RenderTexture.active = previous;

                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(projectRoot))
                    throw new InvalidOperationException("Unity project root could not be resolved.");
                string absolutePath = Path.GetFullPath(Path.Combine(projectRoot, outputAssetPath));
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? projectRoot);
                File.WriteAllBytes(absolutePath, png);
                AssetDatabase.ImportAsset(outputAssetPath, ImportAssetOptions.ForceSynchronousImport);
                if (AssetImporter.GetAtPath(outputAssetPath) is TextureImporter importer)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = true;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = true;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    importer.maxTextureSize = CanvasSize;
                    importer.SaveAndReimport();
                }
                savedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(outputAssetPath);
                status = "Image saved: " + outputAssetPath;
                statusType = MessageType.Info;
                return savedTexture != null;
            }
            catch (Exception exception)
            {
                status = "Could not save image: " + exception.Message;
                statusType = MessageType.Error;
                return false;
            }
        }

        private void AssignSavedTexture()
        {
            string property = CurrentTextureProperty;
            if (savedTexture == null || string.IsNullOrEmpty(property))
                return;
            RestoreLivePreview();
            List<Material> materials = GetTargetBindings()
                .Select(target => target.material)
                .Where(material => material != null && material.HasProperty(property))
                .Distinct()
                .ToList();
            if(directMaterial && !materials.Contains(directMaterial)) materials.Add(directMaterial);
            if (materials.Count == 0)
                return;
            Undo.RecordObjects(materials.Cast<UnityEngine.Object>().ToArray(), "Assign image");
            foreach (Material material in materials)
            {
                material.SetTexture(property, savedTexture);
                ApplyMgUvFlags(material);
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
            status = $"Assigned {savedTexture.name} to {materials.Count} material asset(s), property {property}, using {uvChannel}.";
            statusType = MessageType.Info;
        }

        private void ApplyLivePreview()
        {
            if (surfaceSuspended) return;
            string property = CurrentTextureProperty;
            if (layers.Count == 0)
            {
                RestoreLivePreview();
                return;
            }
            if (composite == null || (targetRenderer == null && !directMaterial) || string.IsNullOrEmpty(property))
                return;
            RestoreLivePreview();
            foreach (TargetBinding target in GetTargetBindings())
            {
                if (target.renderer == null || target.material == null || EditorUtility.IsPersistent(target.renderer) ||
                    !target.material.HasProperty(property))
                    continue;
                var originalBlock = new MaterialPropertyBlock();
                target.renderer.GetPropertyBlock(originalBlock, target.materialIndex);
                var previewBlock = new MaterialPropertyBlock();
                target.renderer.GetPropertyBlock(previewBlock, target.materialIndex);
                // An indexed block takes precedence over the renderer-wide block.
                // Preserve those existing map/tint overrides when introducing a slot block.
                if (previewBlock.isEmpty) target.renderer.GetPropertyBlock(previewBlock);
                previewBlock.SetTexture(property, composite);
                ApplyMgUvFlags(previewBlock, target.material);
                target.renderer.SetPropertyBlock(previewBlock, target.materialIndex);
                previewBindings.Add(new PreviewBinding
                {
                    renderer = target.renderer,
                    materialIndex = target.materialIndex,
                    originalBlock = originalBlock
                });
            }
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private void RestoreLivePreview()
        {
            foreach (PreviewBinding preview in previewBindings)
            {
                if (preview.renderer == null || preview.materialIndex < 0 || preview.originalBlock == null)
                    continue;
                preview.renderer.SetPropertyBlock(preview.originalBlock, preview.materialIndex);
            }
            previewBindings.Clear();
            SceneView.RepaintAll();
        }

        private void ApplyMgUvFlags(Material material)
        {
            if(CurrentTextureProperty!="_StickerMap") return;
            if (material == null)
                return;
            SetMaterialFloat(material, "_StickerUseUV1", uvChannel == MeshUvChannel.UV1 ? 1f : 0f);
            SetMaterialFloat(material, "_StickerUseUV2", uvChannel == MeshUvChannel.UV2 ? 1f : 0f);
            SetMaterialFloat(material, "_StickerUseUV3", uvChannel == MeshUvChannel.UV3 ? 1f : 0f);
        }

        private void ApplyMgUvFlags(MaterialPropertyBlock block, Material material)
        {
            if(CurrentTextureProperty!="_StickerMap") return;
            if (material.HasProperty("_StickerUseUV1")) block.SetFloat("_StickerUseUV1", uvChannel == MeshUvChannel.UV1 ? 1f : 0f);
            if (material.HasProperty("_StickerUseUV2")) block.SetFloat("_StickerUseUV2", uvChannel == MeshUvChannel.UV2 ? 1f : 0f);
            if (material.HasProperty("_StickerUseUV3")) block.SetFloat("_StickerUseUV3", uvChannel == MeshUvChannel.UV3 ? 1f : 0f);
        }

        private static void RestoreMgUvFlags(MaterialPropertyBlock block, Material material)
        {
            if (material.HasProperty("_StickerUseUV1") && !block.HasFloat("_StickerUseUV1")) block.SetFloat("_StickerUseUV1", material.GetFloat("_StickerUseUV1"));
            if (material.HasProperty("_StickerUseUV2") && !block.HasFloat("_StickerUseUV2")) block.SetFloat("_StickerUseUV2", material.GetFloat("_StickerUseUV2"));
            if (material.HasProperty("_StickerUseUV3") && !block.HasFloat("_StickerUseUV3")) block.SetFloat("_StickerUseUV3", material.GetFloat("_StickerUseUV3"));
        }

        private static void SetMaterialFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
                material.SetFloat(property, value);
        }

        private void SetTargetRenderer(Renderer renderer)
        {
            bool hadDirectMaterial=directMaterial;
            if(hadDirectMaterial)RestoreLivePreview();
            directMaterial=null;
            if (targetRenderer == renderer && !hadDirectMaterial)
            {
                TryAutoOpenCurrentStickerMap();
                return;
            }
            RestoreLivePreview();
            targetRenderer = renderer;
            if (renderer != null && (targetRoot == null || !renderer.transform.IsChildOf(targetRoot.transform)))
                targetRoot = renderer.gameObject;
            materialIndex = 0;
            RefreshTextureProperties();
            TryAutoOpenCurrentStickerMap();
            MarkCompositeDirty();
        }

        private void SetTargetRoot(GameObject root, Renderer preferredRenderer)
        {
            bool hadDirectMaterial=directMaterial;
            if(hadDirectMaterial)RestoreLivePreview();
            directMaterial=null;
            if (targetRoot == root && !hadDirectMaterial && (preferredRenderer == null || preferredRenderer == targetRenderer))
            {
                TryAutoOpenCurrentStickerMap();
                return;
            }
            RestoreLivePreview();
            targetRoot = root;
            Renderer[] renderers = GetRootRenderers();
            targetRenderer = preferredRenderer != null && renderers.Contains(preferredRenderer)
                ? preferredRenderer
                : renderers.FirstOrDefault();
            materialIndex = 0;
            RefreshTextureProperties();
            TryAutoOpenCurrentStickerMap();
            MarkCompositeDirty();
        }

        private Renderer[] GetRootRenderers()
        {
            if (targetRoot != null)
                return targetRoot.GetComponentsInChildren<Renderer>(true);
            return targetRenderer != null ? new[] { targetRenderer } : Array.Empty<Renderer>();
        }

        private List<TargetBinding> GetTargetBindings()
        {
            var results = new List<TargetBinding>();
            if (surfaceRenderers != null)
            {
                var selected = CurrentMaterial;
                foreach (var renderer in surfaceRenderers)
                {
                    if (!renderer || !selected) continue;
                    var slots = renderer.sharedMaterials;
                    for (int i = 0; i < slots.Length; i++)
                        if (slots[i] == selected) results.Add(new TargetBinding(renderer, i, selected));
                }
                return results;
            }
            if(directMaterial)
            {
                foreach(var renderer in Resources.FindObjectsOfTypeAll<Renderer>().Where(r=>!EditorUtility.IsPersistent(r)))
                {
                    var slots=renderer.sharedMaterials;
                    for(int i=0;i<slots.Length;i++)if(slots[i]==directMaterial)results.Add(new TargetBinding(renderer,i,directMaterial));
                }
                return results;
            }
            Material selectedMaterial = CurrentMaterial;
            if (targetRenderer == null || selectedMaterial == null)
                return results;

            if (rendererScope == RendererScope.SelectedRenderer)
            {
                results.Add(new TargetBinding(targetRenderer, materialIndex, selectedMaterial));
                return results;
            }

            string property = CurrentTextureProperty;
            foreach (Renderer renderer in GetRootRenderers())
            {
                if (renderer == null)
                    continue;
                Material[] materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material material = materials[slot];
                    if (material == null)
                        continue;
                    bool include = rendererScope == RendererScope.MatchingMaterial
                        ? material == selectedMaterial
                        : !string.IsNullOrEmpty(property) && material.HasProperty(property);
                    if (include)
                        results.Add(new TargetBinding(renderer, slot, material));
                }
            }
            return results;
        }

        private Material imageSlotMaterial;
        private void RefreshTextureProperties()
        {
            Material material = CurrentMaterial;
            string previous=material==imageSlotMaterial?CurrentTextureProperty:null;
            if (string.IsNullOrEmpty(previous) && material == selectedShaderImageMaterial)
                previous = selectedShaderImageProperty;
            imageSlotMaterial=material;
            if (material == null || material.shader == null)
            {
                texturePropertyNames = Array.Empty<string>();
                texturePropertyLabels = Array.Empty<string>();
                texturePropertyIndex = 0;
                return;
            }

            var names = new List<string>();
            var labels = new List<string>();
            Shader shader = material.shader;
            for (int index = 0; index < shader.GetPropertyCount(); index++)
            {
                if (shader.GetPropertyType(index) != ShaderPropertyType.Texture || shader.GetPropertyTextureDimension(index)!=TextureDimension.Tex2D)
                    continue;
                string property = shader.GetPropertyName(index);
                names.Add(property);
                labels.Add(ObjectNames.NicifyVariableName(shader.GetPropertyDescription(index)));
            }
            texturePropertyNames = names.ToArray();
            texturePropertyLabels = labels.ToArray();
            int preferred=Array.IndexOf(texturePropertyNames,previous);
            // Migrate an already-open canvas from versions that only stored an index.
            // Its source texture identifies the original slot without replacing edits.
            if (preferred < 0 && string.IsNullOrEmpty(selectedShaderImageProperty) && layers.Count > 0)
                preferred = names.FindIndex(property => material.GetTexture(property) &&
                    layers.Any(layer => layer.texture == material.GetTexture(property)));
            if(preferred<0)
                foreach(string property in new[]{"_BaseColorMap","_BaseMap","_MainTex"})
                {int index=names.IndexOf(property);if(index>=0 && material.GetTexture(property)){preferred=index;break;}}
            if(preferred<0)preferred=names.FindIndex(property=>material.GetTexture(property));
            texturePropertyIndex=Mathf.Max(0,preferred);
            RememberShaderImageSelection();
        }

        private void RememberShaderImageSelection()
        {
            selectedShaderImageMaterial = CurrentMaterial;
            selectedShaderImageProperty = CurrentTextureProperty;
        }

        private void MarkCompositeDirty()
        {
            ImageChanged();
            compositeDirty = true;
            nextAutoSaveTime = EditorApplication.timeSinceStartup + 0.45d;
            Repaint();
            SurfaceChanged?.Invoke();
        }

        private void EditorUpdate()
        {
            if(inlineTextLayer!=null){RepaintInlineImageText();return;}
            if (surfaceRenderers != null && !surfaceSuspended) RefreshSurfacePreview();
            UpdateImageToolHold();
            UpdateLinkedBaseImage();
            if (!autoSave || !pendingExport || string.IsNullOrEmpty(outputAssetPath) ||
                EditorApplication.timeSinceStartup < nextAutoSaveTime)
                return;
            if(SaveComposite(false)) pendingExport=false;
            Repaint();
        }

        private int HitTestLayers(Vector2 point)
        {
            for (int index = layers.Count - 1; index >= 0; index--)
            {
                StickerLayer layer = layers[index];
                if (!layer.visible || layer.texture == null)
                    continue;
                if (ImageCanvasToUV(layer,point,out var uv) && uv.x>=0 && uv.x<=1 && uv.y>=0 && uv.y<=1)
                    return index;
            }
            return -1;
        }

        private static Rect FullSourceRect => new Rect(0f, 0f, 1f, 1f);

        private static bool ApproximatelyFullRect(Rect rect)
        {
            rect = ClampSourceRect(rect);
            return Mathf.Approximately(rect.x, 0f) && Mathf.Approximately(rect.y, 0f) &&
                   Mathf.Approximately(rect.width, 1f) && Mathf.Approximately(rect.height, 1f);
        }

        private static Rect ClampSourceRect(Rect rect)
        {
            // Existing serialized layers predate source cropping and deserialize this new field as an empty rect.
            if (rect.width <= 0f || rect.height <= 0f)
                return FullSourceRect;
            float xMin = Mathf.Clamp01(Mathf.Min(rect.xMin, rect.xMax));
            float yMin = Mathf.Clamp01(Mathf.Min(rect.yMin, rect.yMax));
            if (xMin >= 1f) xMin = 0.9999f;
            if (yMin >= 1f) yMin = 0.9999f;
            float xMax = Mathf.Clamp(Mathf.Max(rect.xMin, rect.xMax), xMin + 0.0001f, 1f);
            float yMax = Mathf.Clamp(Mathf.Max(rect.yMin, rect.yMax), yMin + 0.0001f, 1f);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static Vector2 CroppedPixelSize(StickerLayer layer)
        {
            if (layer?.texture == null)
                return Vector2.one;
            Rect source = ClampSourceRect(layer.sourceRect);
            return new Vector2(
                Mathf.Max(1f, layer.texture.width * source.width),
                Mathf.Max(1f, layer.texture.height * source.height));
        }

        private static Rect SourceRectToGui(Rect source, Rect preview)
        {
            source = ClampSourceRect(source);
            return new Rect(
                preview.x + source.x * preview.width,
                preview.y + source.y * preview.height,
                source.width * preview.width,
                source.height * preview.height);
        }

        private static Vector2 GuiToSourceNormalized(Vector2 point, Rect preview)
        {
            return new Vector2(
                Mathf.Clamp01((point.x - preview.x) / Mathf.Max(1f, preview.width)),
                Mathf.Clamp01((point.y - preview.y) / Mathf.Max(1f, preview.height)));
        }

        private static Rect NormalizedRectFromPoints(Vector2 first, Vector2 second)
        {
            float xMin = Mathf.Clamp01(Mathf.Min(first.x, second.x));
            float yMin = Mathf.Clamp01(Mathf.Min(first.y, second.y));
            if (xMin >= 1f) xMin = 0.9999f;
            if (yMin >= 1f) yMin = 0.9999f;
            float xMax = Mathf.Clamp(Mathf.Max(first.x, second.x), xMin + 0.0001f, 1f);
            float yMax = Mathf.Clamp(Mathf.Max(first.y, second.y), yMin + 0.0001f, 1f);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static RectInt SourceRectToPixels(Rect source, Texture2D texture)
        {
            source = ClampSourceRect(source);
            int x = Mathf.Clamp(Mathf.RoundToInt(source.x * texture.width), 0, Mathf.Max(0, texture.width - 1));
            int y = Mathf.Clamp(Mathf.RoundToInt(source.y * texture.height), 0, Mathf.Max(0, texture.height - 1));
            int width = Mathf.Clamp(Mathf.RoundToInt(source.width * texture.width), 1, texture.width - x);
            int height = Mathf.Clamp(Mathf.RoundToInt(source.height * texture.height), 1, texture.height - y);
            return new RectInt(x, y, width, height);
        }

        private static Rect PixelsToSourceRect(RectInt pixels, Texture2D texture)
        {
            int x = Mathf.Clamp(pixels.x, 0, Mathf.Max(0, texture.width - 1));
            int y = Mathf.Clamp(pixels.y, 0, Mathf.Max(0, texture.height - 1));
            int width = Mathf.Clamp(pixels.width, 1, texture.width - x);
            int height = Mathf.Clamp(pixels.height, 1, texture.height - y);
            return new Rect(
                x / (float)texture.width,
                y / (float)texture.height,
                width / (float)texture.width,
                height / (float)texture.height);
        }

        private static bool IsSupportedImagePath(string path)
        {
            string extension = Path.GetExtension(path)?.ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg";
        }

        private static bool IsMashBoxSdkAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return false;
            string normalized = assetPath.Replace('\\', '/').TrimEnd('/');
            return normalized.Equals(MashBoxSdkPackageRoot, StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(MashBoxSdkPackageRoot + "/", StringComparison.OrdinalIgnoreCase);
        }

        private Vector2 GuiToCanvas(Vector2 gui, Rect rect)
        {
            return new Vector2(
                (gui.x - rect.x) / rect.width * CanvasSize,
                (gui.y - rect.y) / rect.height * CanvasSize);
        }

        private Vector3 CanvasToGui(Vector2 point, Rect rect)
        {
            return new Vector3(
                rect.x + point.x / CanvasSize * rect.width,
                rect.y + point.y / CanvasSize * rect.height,
                0f);
        }

        private static Vector3 UvToGui(Vector2 uv, Rect rect)
        {
            return new Vector3(rect.x + uv.x * rect.width, rect.y + (1f - uv.y) * rect.height, 0f);
        }

        private static Vector2 Rotate(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(value.x * cosine - value.y * sine, value.x * sine + value.y * cosine);
        }

        private static Mesh GetRendererMesh(Renderer renderer)
        {
            if (renderer == null)
                return null;
            if (renderer is SkinnedMeshRenderer skinned)
                return skinned.sharedMesh;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }
    }
}
