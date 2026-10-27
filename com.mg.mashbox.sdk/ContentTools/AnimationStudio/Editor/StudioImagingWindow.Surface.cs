using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // The surface editor is another input device for the existing layered image document.
    // It deliberately shares PaintAt, selection masks, channel edits, compositing and history.
    public sealed partial class StudioImagingWindow
    {
        [NonSerialized] private Renderer[] surfaceRenderers;
        [NonSerialized] private bool surfaceSuspended, surfaceStroke, surfaceHasPrevious, surfaceAdjusting;
        [NonSerialized] private string surfaceCheckpoint;
        [NonSerialized] private string[] surfaceUndoBefore, surfaceRedoBefore;
        [NonSerialized] private bool surfaceWasDirty;
        [NonSerialized] internal Action SurfaceChanged;
        internal int SurfaceUvChannel => (int)uvChannel;
        internal float SurfaceBrushRadius => brushRadius;
        internal int SurfaceResolution => CanvasSize;
        internal bool SurfaceCanPaint => imageAdjustment == ImageAdjustment.None &&
            (!HasSelectedLayer || (!layers[selectedLayer].locked && layers[selectedLayer].visible && layers[selectedLayer].texture));

        internal void BindSurface(Renderer renderer, int slot, string property, Renderer[] renderers, bool initialize)
        {
            EndSurfaceStroke();
            RestoreLivePreview();
            directMaterial = null;
            targetRoot = renderer.gameObject;
            targetRenderer = renderer;
            materialIndex = slot;
            rendererScope = RendererScope.SelectedRenderer;
            surfaceRenderers = renderers;
            surfaceSuspended = false;
            liveMaterialPreview = true;
            autoOpenCurrentMap = false;
            RefreshTextureProperties();
            texturePropertyIndex = Array.IndexOf(texturePropertyNames, property);
            if (initialize)
            {
                restoring = true;
                try
                {
                    if (GetCurrentStickerTexture()) OpenCurrentStickerMap(true, true);
                    AddPaintLayer();
                    imageTool = ImageTool.Brush;
                }
                finally { restoring = false; }
                hasUnsavedChanges = false;
            }
            compositeDirty = true;
            RefreshSurfacePreview();
        }

        internal void SuspendSurface()
        {
            EndSurfaceInput();
            surfaceSuspended = true;
            RestoreLivePreview();
            SurfaceChanged = null;
        }

        internal void RefreshSurfacePreview()
        {
            if (surfaceSuspended) return;
            EnsureCompositeMaterial();
            if (compositeDirty) RenderComposite();
        }

        internal void DrawSurfaceInspector()
        {
            BeginImageInput();
            GUILayout.Label("PAINT", StudioTheme.Styles.boldLabel);
            int tool = imageTool == ImageTool.Eraser ? 1 : imageTool == ImageTool.Eyedropper ? 2 : 0;
            int next = GUILayout.Toolbar(tool, new[] { "Brush", "Eraser", "Sample" });
            if (next != tool) imageTool = next == 1 ? ImageTool.Eraser : next == 2 ? ImageTool.Eyedropper : ImageTool.Brush;
            if (next == 0) imageBrushMode = (ImageBrushMode)EditorGUILayout.EnumPopup("Brush", imageBrushMode);
            brushColor = EditorGUILayout.ColorField("Color", brushColor);
            brushRadius = EditorGUILayout.Slider("Radius (texture px)", brushRadius, 1, 200);
            brushOpacity = EditorGUILayout.Slider("Strength", brushOpacity, .01f, 1);
            imageChannel = (ImageChannel)EditorGUILayout.EnumPopup("Paint channel", imageChannel);
            var channel = (MeshUvChannel)EditorGUILayout.EnumPopup("Mesh UV", uvChannel);
            if (channel != uvChannel) { uvChannel = channel; compositeDirty = true; }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("2D Canvas")) ShowSurfaceCanvas();
                if (GUILayout.Button("Save Layers")) SaveImageDocument();
                if (GUILayout.Button("Export PNG")) SaveComposite(true);
            }
            EditorGUILayout.LabelField("B brush · E erase · I sample · Ctrl+MMB size/strength", StudioTheme.Styles.wordWrappedMiniLabel);
            if (!SurfaceCanPaint) EditorGUILayout.HelpBox("Select a visible, unlocked layer and apply or cancel any image adjustment before painting.", MessageType.Info);
            DrawLayersPanel();
            EndImageInput();
            RefreshSurfacePreview();
        }

        internal void ShowSurfaceCanvas()
        {
            titleContent = new GUIContent("Painting — " + (CurrentMaterial ? CurrentMaterial.name : "Image"));
            minSize = new Vector2(940, 620);
            Show(); Focus();
        }

        internal void DrawSurfaceFileMenu(EditorWindow owner)
        {
            var rect = new Rect(0, 0, 50, 21);
            if (StudioMenu.MenuBarButton(owner, rect, new GUIContent("Image")))
                BuildImageFileMenu().DropDown(rect, owner, "Image");
        }

        internal bool SurfaceKeyboard()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField || e.alt) return false;
            if (e.control || e.command)
            {
                if (e.keyCode == KeyCode.Z || e.keyCode == KeyCode.Y)
                { UndoSurface(e.keyCode == KeyCode.Y || e.shift); }
                else if (e.keyCode == KeyCode.S) SaveImageDocument();
                else return false;
            }
            else if (e.keyCode == KeyCode.B) imageTool = ImageTool.Brush;
            else if (e.keyCode == KeyCode.E) imageTool = ImageTool.Eraser;
            else if (e.keyCode == KeyCode.I) imageTool = ImageTool.Eyedropper;
            else if (e.keyCode == KeyCode.LeftBracket) brushRadius = Mathf.Max(1, brushRadius / 1.15f);
            else if (e.keyCode == KeyCode.RightBracket) brushRadius = Mathf.Min(200, brushRadius * 1.15f);
            else return false;
            e.Use(); RefreshSurfacePreview(); SurfaceChanged?.Invoke(); Repaint(); return true;
        }

        internal void UndoSurface(bool redo)
        {
            EndSurfaceStroke(); ImageUndo(redo); RefreshSurfacePreview(); SurfaceChanged?.Invoke(); Repaint();
        }

        internal bool SurfaceBrushAdjustment(Rect rect)
        {
            canvasArea = rect;
            bool handled = HandleImageBrushAdjustment(rect);
            surfaceAdjusting = handled && adjustingImageBrush;
            if (handled) SurfaceChanged?.Invoke();
            return handled;
        }

        internal void EndSurfaceInput()
        {
            EndSurfaceStroke();
            if (surfaceAdjusting) EndImageBrushAdjustment();
            surfaceAdjusting = false;
        }

        internal bool BeginSurfaceStroke(Vector2 uv)
        {
            if (!SurfaceCanPaint) return false;
            EndSurfaceStroke();
            Vector2 point = SurfaceCanvasPoint(uv);
            if (imageTool == ImageTool.Eyedropper)
            {
                RenderComposite(); var sample = ReadTexture(composite);
                try { brushColor = sample.GetPixel(Mathf.Clamp((int)point.x, 0, CanvasSize - 1), Mathf.Clamp(CanvasSize - 1 - (int)point.y, 0, CanvasSize - 1)); }
                finally { DestroyImmediate(sample); }
                imageTool = ImageTool.Brush; SurfaceChanged?.Invoke(); return false;
            }
            inputSnapshot = CaptureImage();
            surfaceCheckpoint = inputSnapshot; surfaceUndoBefore = undoImages.ToArray(); surfaceRedoBefore = redoImages.ToArray(); surfaceWasDirty = hasUnsavedChanges;
            if (!HasSelectedLayer) AddPaintLayer();
            if (imageTool != ImageTool.Eraser) imageTool = ImageTool.Brush;
            paintTexture = ReadTexture(layers[selectedLayer].texture);
            ownedTextures.Add(paintTexture);
            layers[selectedLayer].texture = paintTexture;
            surfaceStroke = true;
            surfaceHasPrevious = false;
            SurfaceDab(uv);
            return true;
        }

        private Vector2 SurfaceCanvasPoint(Vector2 uv) => new Vector2(uv.x * CanvasSize, (1 - uv.y) * CanvasSize);

        internal void SurfaceDab(Vector2 uv)
        {
            if (!surfaceStroke || !paintTexture || !HasSelectedLayer) return;
            var point = SurfaceCanvasPoint(uv);
            // A UV seam is not a line across the texture. In particular, smudge must not
            // pull unrelated islands into one another when the cursor crosses a seam.
            brushDabDelta = surfaceHasPrevious && Vector2.Distance(lastPaint, point) <= brushRadius * 2
                ? point - lastPaint : Vector2.zero;
            PaintAt(point);
            lastPaint = point; surfaceHasPrevious = true;
        }

        internal void BreakSurfaceStroke() => surfaceHasPrevious = false;

        internal void FlushSurfaceStroke()
        {
            if (!surfaceStroke || !paintTexture) return;
            paintTexture.Apply(false);
            MarkCompositeDirty(); RefreshSurfacePreview();
        }

        internal void EndSurfaceStroke()
        {
            if (!surfaceStroke) return;
            FlushSurfaceStroke();
            surfaceStroke = false; surfaceHasPrevious = false; paintTexture = null; inputSnapshot = null; surfaceCheckpoint = null;
            surfaceUndoBefore = surfaceRedoBefore = null;
        }

        internal void CancelSurfaceStroke()
        {
            if (!surfaceStroke || string.IsNullOrEmpty(surfaceCheckpoint)) return;
            string state = surfaceCheckpoint;
            surfaceStroke = false; surfaceHasPrevious = false; paintTexture = null; inputSnapshot = null; surfaceCheckpoint = null;
            // The history entry is added on the first flush, not on every dab.
            undoImages.Clear(); undoImages.AddRange(surfaceUndoBefore);
            redoImages.Clear(); redoImages.AddRange(surfaceRedoBefore);
            surfaceUndoBefore = surfaceRedoBefore = null;
            RestoreImage(state); hasUnsavedChanges = surfaceWasDirty;
            RefreshSurfacePreview(); SurfaceChanged?.Invoke();
        }
    }
}
