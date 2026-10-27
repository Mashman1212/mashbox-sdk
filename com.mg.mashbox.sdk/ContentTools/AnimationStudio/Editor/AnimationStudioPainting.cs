using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [Serializable] private sealed class PaintingMap
        {
            public Material material;
            public string property;
            public StudioImagingWindow image;
        }
        [SerializeField] private bool paintingMode;
        [SerializeField] private float paintingExposure = .75f;
        [SerializeField] private List<PaintingMap> paintingMaps = new List<PaintingMap>();
        [SerializeField] private int paintingRendererIndex, paintingSlot, paintingProperty;
        private Renderer[] paintingRenderers = Array.Empty<Renderer>();
        private Transform[] paintingTransforms;
        private StudioPaintingSurface paintingSurface;
        private StudioImagingWindow paintingImage;
        private string[] paintingProperties = Array.Empty<string>();
        private Vector2 paintingScroll, paintingLastMouse;
        private int paintingControl;
        private bool paintingStroke;
        private string paintingStatus;
        private readonly Vector3[] paintingBrushRing = new Vector3[49];

        private void OpenPaintingMode()
        {
            if (paintingMode) return;
            FlushEdits(); playing = false;
            skinningEditor?.EndStroke(); skinningEditor?.SuspendLighting(); retopologyEditor?.SuspendLighting();
            modelEditor?.CancelMarkingMenu(); modelEditor?.CloseToolOptions();
            skinningMode = false; retopologyMode = false; modelingMode = true; paintingMode = true;
            if (!viewport) OpenViewport(); else BindPaintingMode();
        }

        private void BindPaintingMode()
        {
            EnsureModelEditor();
            viewport.wantsMouseMove = true;
            viewport.Bind(modelEditor.Scene);
            viewport.SetRetopologyPanels(false);
            viewport.BindSkinInfluences(null); viewport.BindSkinPreviewTimeline(null); viewport.BindSkinPractice(null);
            viewport.BindTimeline(null); viewport.BindLimbBlend(null); viewport.BindAttachments(null);
            viewport.BindModes(DrawStudioModes, true);
            viewport.BindRigControls(DrawPaintingTargets);
            viewport.BindInspector(DrawPaintingInspector);
            viewport.BindModelToolOptions(null); viewport.BindMarkingMenu(null); viewport.BindStatistics(null);
            viewport.BindViewportControls(DrawStudioMenuBar, null, DrawStudioSettings);
            viewport.BindModelInput(() => paintingMode, HandlePaintingKeyboard, EndPaintingStroke);
            viewport.rootVisualElement.UnregisterCallback<UnityEngine.UIElements.KeyDownEvent>(ShieldPaintingHistory, UnityEngine.UIElements.TrickleDown.TrickleDown);
            viewport.rootVisualElement.RegisterCallback<UnityEngine.UIElements.KeyDownEvent>(ShieldPaintingHistory, UnityEngine.UIElements.TrickleDown.TrickleDown);
            viewport.BindStatusBar(false);
            viewport.SyncStudioToolbarState(true);
            RefreshPaintingTargets();
            StudioPreferenceStore.SetString("Studio.Mode", "Painting");
        }

        private void RefreshPaintingTargets(int selectFromNode = -1)
        {
            EndPaintingStroke();
            paintingImage?.SuspendSurface(); paintingImage = null;
            paintingSurface?.Dispose(); paintingSurface = null;
            modelEditor.UsePaintingMaterials(true);
            paintingTransforms = modelEditor.Transforms;
            paintingRenderers = modelEditor.Transforms.Where(t => t).Select(t => t.GetComponent<Renderer>()).Where(r => r).ToArray();
            paintingRendererIndex = Mathf.Clamp(paintingRendererIndex, 0, Mathf.Max(0, paintingRenderers.Length - 1));
            if (selectFromNode >= 0)
            {
                int added = Array.FindIndex(paintingRenderers, r => Array.IndexOf(paintingTransforms, r.transform) >= selectFromNode);
                if (added >= 0) { paintingRendererIndex = added; paintingSlot = 0; }
            }
            paintingSurface = new StudioPaintingSurface(paintingRenderers);
            RefreshPaintingProperties();
        }

        private Renderer PaintingRenderer => paintingRendererIndex >= 0 && paintingRendererIndex < paintingRenderers.Length ? paintingRenderers[paintingRendererIndex] : null;
        private Material PaintingMaterial
        {
            get
            {
                var renderer = PaintingRenderer;
                if (!renderer) return null;
                var materials = renderer.sharedMaterials;
                return paintingSlot >= 0 && paintingSlot < materials.Length ? materials[paintingSlot] : null;
            }
        }

        private void RefreshPaintingProperties()
        {
            var renderer = PaintingRenderer;
            paintingSlot = Mathf.Clamp(paintingSlot, 0, Mathf.Max(0, renderer ? renderer.sharedMaterials.Length - 1 : 0));
            var material = PaintingMaterial;
            var properties = new List<string>();
            if (material && material.shader)
                for (int i = 0; i < material.shader.GetPropertyCount(); i++)
                    if (material.shader.GetPropertyType(i) == ShaderPropertyType.Texture && material.shader.GetPropertyTextureDimension(i) == TextureDimension.Tex2D && IsPaintingColorSlot(material, i))
                        properties.Add(material.shader.GetPropertyName(i));
            paintingProperties = properties.ToArray();
            paintingProperty = 0;
            foreach (var preferred in new[] { "_BaseColorMap", "_BaseMap", "_MainTex", "_StickerMap" })
            {
                int index = properties.IndexOf(preferred);
                if (index >= 0) { paintingProperty = index; break; }
            }
            SelectPaintingMap();
        }

        private static bool IsPaintingColorSlot(Material material, int index)
        {
            var shader = material.shader;
            if ((shader.GetPropertyFlags(index) & ShaderPropertyFlags.Normal) != 0) return false;
            string property = shader.GetPropertyName(index);
            string name = property.ToLowerInvariant();
            // The Imager currently composites/exports sRGB color. Do not present data
            // maps as paintable until their linear encoding and normal packing are shared too.
            foreach (string data in new[] { "normal", "bump", "mask", "metal", "rough", "smooth", "height", "displace", "occlusion" })
                if (name.Contains(data)) return false;
            var texture = material.GetTexture(property);
            return !texture || texture.isDataSRGB;
        }

        private void SelectPaintingMap()
        {
            EndPaintingStroke();
            paintingImage?.SuspendSurface(); paintingImage = null;
            var material = PaintingMaterial;
            if (!material || paintingProperties.Length == 0) return;
            string property = paintingProperties[paintingProperty];
            paintingMaps.RemoveAll(m => !m.image);
            var map = paintingMaps.FirstOrDefault(m => m.material == material && m.property == property);
            bool initialize = map == null;
            if (initialize)
            {
                map = new PaintingMap { material = material, property = property, image = CreateInstance<StudioImagingWindow>() };
                paintingMaps.Add(map);
            }
            paintingImage = map.image;
            paintingImage.BindSurface(PaintingRenderer, paintingSlot, property, paintingRenderers, initialize);
            // Give every document a native window and its own save-on-close lifecycle.
            // Studio and the 2D canvas hold the same instance, never copies of its layers.
            if (initialize) { paintingImage.ShowSurfaceCanvas(); viewport?.Focus(); }
            paintingImage.SurfaceChanged = () => { if (viewport) viewport.MarkDirty(MashBoxStudioViewport.DirtyState.Scene); };
            paintingStatus = null;
            viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
        }

        private void DrawPaintingTargets()
        {
            if (!ReferenceEquals(paintingTransforms, modelEditor.Transforms)) RefreshPaintingTargets();
            HandlePaintingModelDrop(null);
            paintingScroll = EditorGUILayout.BeginScrollView(paintingScroll);
            GUILayout.Label("MESHES", StudioTheme.Styles.boldLabel);
            if (GUILayout.Button("Add FBX…")) Guard(() => ImportPaintingMeshes(() => modelEditor.ImportPaintingModel(LoadStudioModel)));
            var source = (GameObject)EditorGUILayout.ObjectField("Add model", null, typeof(GameObject), false);
            if (source) Guard(() => ImportPaintingMeshes(() => modelEditor.ImportModel(source, LoadStudioModel)));
            EditorGUILayout.LabelField("Drop one or more models here to add them. Click a mesh to paint; double-click to frame.", StudioTheme.Styles.wordWrappedMiniLabel);
            if (paintingRenderers.Length == 0)
                EditorGUILayout.HelpBox("Import an FBX or switch to Modeling to build a mesh. Painting shares that workspace.", MessageType.Info);
            else
            {
                DrawPaintingMeshList();
                EditorGUILayout.Space(8);
                GUILayout.Label("SELECTED MESH", StudioTheme.Styles.boldLabel);
                if (PaintingRenderer) EditorGUILayout.LabelField(PaintingRenderer.name, StudioTheme.Styles.label);
                var materials = PaintingRenderer ? PaintingRenderer.sharedMaterials : Array.Empty<Material>();
                if (materials.Length > 0)
                {
                    int slot = EditorGUILayout.Popup("Material", paintingSlot, materials.Select((m, i) => i + ": " + (m ? m.name : "Missing material")).ToArray());
                    if (slot != paintingSlot) { paintingSlot = slot; RefreshPaintingProperties(); }
                }
                if (paintingProperties.Length > 0)
                {
                    int property = EditorGUILayout.Popup("Color texture", paintingProperty, paintingProperties);
                    if (property != paintingProperty) { paintingProperty = property; SelectPaintingMap(); }
                }
                else EditorGUILayout.HelpBox("Choose a material with a 2D color texture slot. Normal maps and linear PBR data maps are not supported by this color-painting workspace yet.", MessageType.Info);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Frame Selected")) FramePaintingMesh(PaintingRenderer);
                    if (GUILayout.Button("Frame All")) modelEditor.Frame(viewport);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Zero Selected", "Reset this model's root placement; keep its child meshes and skeleton aligned."))) ZeroPaintingPlacement(false);
                    if (GUILayout.Button(new GUIContent("Zero All Models", "Reset each model root to the origin, identity rotation and unit scale."))) ZeroPaintingPlacement(true);
                }
                if (GUILayout.Button("Refresh Meshes")) RefreshPaintingTargets();
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Each material / texture slot keeps its own layers. The active map previews on every mesh using that material.", StudioTheme.Styles.wordWrappedMiniLabel);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("UV brush: size is measured in texture pixels. Shared or overlapping UVs share paint. Use the 2D canvas for image placement, selections and adjustments.", StudioTheme.Styles.wordWrappedMiniLabel);
            }
            if (!string.IsNullOrEmpty(paintingStatus)) EditorGUILayout.HelpBox(paintingStatus, MessageType.Info);
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Error);
            EditorGUILayout.EndScrollView();
        }

        private void ImportPaintingMeshes(Action import)
        {
            int first = modelEditor.Document ? modelEditor.Document.nodes.Count : 0;
            EndPaintingStroke(); paintingImage?.SuspendSurface();
            try { import(); }
            finally { modelEditor.ZeroPaintingRoots(first, -1, false); RefreshPaintingTargets(first); }
            if (modelEditor.Document && modelEditor.Document.nodes.Count > first) modelEditor.Frame(viewport);
        }

        private bool HandlePaintingModelDrop(SceneView view)
        {
            var e = Event.current;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return false;
            bool perform = e.type == EventType.DragPerform;
            int first = modelEditor.Document ? modelEditor.Document.nodes.Count : 0;
            if (perform) { EndPaintingStroke(); paintingImage?.SuspendSurface(); }
            bool handled = modelEditor.HandleModelDrop(view, LoadStudioModel);
            if (perform && handled)
            {
                modelEditor.ZeroPaintingRoots(first, -1, false);
                RefreshPaintingTargets(first);
                if (modelEditor.Document && modelEditor.Document.nodes.Count > first) modelEditor.Frame(viewport);
            }
            return handled;
        }

        private void SelectPaintingMesh(int index)
        {
            if (index == paintingRendererIndex || index < 0 || index >= paintingRenderers.Length) return;
            EndPaintingStroke(); paintingRendererIndex = index; paintingSlot = 0; RefreshPaintingProperties();
        }

        private void ZeroPaintingPlacement(bool all)
        {
            EndPaintingStroke();
            int selected = all ? -1 : Array.IndexOf(modelEditor.Transforms, PaintingRenderer ? PaintingRenderer.transform : null);
            if (!all && selected < 0) return;
            if (!modelEditor.ZeroPaintingRoots(0, selected, true)) return;
            RefreshPaintingTargets();
            modelEditor.Frame(viewport);
        }

        private void SetPaintingMeshVisible(int index, bool visible)
        {
            if (index < 0 || index >= paintingRenderers.Length || !paintingRenderers[index]) return;
            EndPaintingStroke();
            modelEditor.SetPaintingVisibility(paintingRenderers[index], visible);
            viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
        }

        private void FramePaintingMesh(Renderer renderer)
        {
            if (!renderer) return;
            var bounds = renderer.bounds;
            bounds.size = Vector3.Max(bounds.size * 1.3f, Vector3.one * .05f);
            viewport.Frame(bounds, false);
        }

        private void DrawPaintingMeshList()
        {
            for (int i = 0; i < paintingRenderers.Length; i++)
            {
                var renderer = paintingRenderers[i];
                if (!renderer) continue;
                var row = GUILayoutUtility.GetRect(40, 27, GUILayout.ExpandWidth(true));
                var visibility = new Rect(row.x, row.center.y - 9, 18, 18);
                bool visible = renderer.gameObject.activeSelf;
                bool next = GUI.Toggle(visibility, visible, new GUIContent("", "Show or hide this mesh"));
                if (next != visible) SetPaintingMeshVisible(i, next);
                var button = new Rect(row.x + 23, row.y, row.width - 23, row.height);
                if (i == paintingRendererIndex) EditorGUI.DrawRect(button, StudioTheme.Current.Selection);
                else EditorGUI.DrawRect(button, StudioTheme.Current.Panel);
                var icon = EditorGUIUtility.IconContent(renderer is SkinnedMeshRenderer ? "SkinnedMeshRenderer Icon" : "Mesh Icon").image;
                GUI.DrawTexture(new Rect(button.x + 5, button.center.y - 8, 16, 16), icon, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(button.x + 28, button.y, button.width - 32, button.height),
                    new GUIContent(renderer.name, renderer.gameObject.activeInHierarchy ? "Select paint mesh" : "Hidden mesh"), StudioTheme.Styles.label);
                var e = Event.current;
                if (e.type == EventType.MouseDown && e.button == 0 && button.Contains(e.mousePosition))
                {
                    SelectPaintingMesh(i);
                    if (e.clickCount == 2) FramePaintingMesh(renderer);
                    e.Use(); viewport.Repaint();
                }
            }
        }

        private void DrawPaintingInspector()
        {
            EditorGUI.BeginChangeCheck();
            paintingExposure = EditorGUILayout.Slider(new GUIContent("Preview brightness", "Camera-following studio lights. Adjusts only the viewport, not painted colors or exported textures."), paintingExposure, -4, 4);
            if (EditorGUI.EndChangeCheck()) viewport?.Repaint();
            if (paintingImage) paintingImage.DrawSurfaceInspector();
            else
            {
                EditorGUILayout.HelpBox("Select a mesh, material and texture to start painting.", MessageType.Info);
                if (PaintingMaterial && paintingProperties.Length > 0 && GUILayout.Button("Open Paint Document")) SelectPaintingMap();
            }
        }
        private void DrawPaintingMenu() { if (paintingImage) paintingImage.DrawSurfaceFileMenu(viewport); }

        private bool HandlePaintingKeyboard()
        {
            if (viewport.HandleWindowShortcut()) return true;
            var e = Event.current;
            if (e.type == EventType.KeyDown && !EditorGUIUtility.editingTextField && !e.alt && !e.control && !e.command)
            {
                if (e.keyCode == KeyCode.F) { modelEditor.Frame(viewport); e.Use(); return true; }
                if (e.keyCode == KeyCode.Escape && paintingStroke)
                { paintingImage?.CancelSurfaceStroke(); EndPaintingStroke(); e.Use(); return true; }
            }
            return paintingImage && paintingImage.SurfaceKeyboard();
        }

        private void ShieldPaintingHistory(UnityEngine.UIElements.KeyDownEvent evt)
        {
            if (!paintingMode || (!evt.ctrlKey && !evt.commandKey) || evt.altKey ||
                (evt.keyCode != KeyCode.Z && evt.keyCode != KeyCode.Y)) return;
            bool redo = evt.keyCode == KeyCode.Y || evt.shiftKey;
            evt.imguiEvent?.Use(); evt.StopImmediatePropagation();
            var image = paintingImage;
            EditorApplication.delayCall += () =>
            {
                if (!this || !paintingMode || !image || image != paintingImage) return;
                EndPaintingStroke();
                viewport.rootVisualElement.focusController?.focusedElement?.Blur();
                EditorGUIUtility.editingTextField = false;
                image.UndoSurface(redo);
            };
        }

        private void BeforePaintingScene(SceneView view)
        {
            if (!ReferenceEquals(paintingTransforms, modelEditor.Transforms)) RefreshPaintingTargets();
            view.sceneLighting = true;
            if (view.camera) view.camera.cameraType = CameraType.Preview;
            modelEditor?.ConfigureLightingCamera(view);
            modelEditor?.ConfigurePaintingLighting(view.camera, paintingExposure);
            StudioPreviewLighting.ConfigureStudioBackground(view);
            if (view.cameraMode.drawMode != DrawCameraMode.Textured) view.cameraMode = SceneView.GetBuiltinCameraMode(DrawCameraMode.Textured);
            HandlePaintingModelDrop(view);
        }

        private bool TryPaintingHit(Vector2 mouse, out StudioPaintingSurface.Hit hit)
        {
            hit = default;
            if (paintingSurface == null || !paintingImage || !PaintingMaterial) return false;
            // Raycast all visible parts first. A non-target material in front occludes the target.
            if (!paintingSurface.Raycast(HandleUtility.GUIPointToWorldRay(mouse), paintingImage.SurfaceUvChannel, out hit)) return false;
            var slots = hit.renderer.sharedMaterials;
            if (hit.slot >= slots.Length || slots[hit.slot] != PaintingMaterial) return false;
            if (!hit.hasUv) { paintingStatus = "This mesh has no UVs in the selected channel."; return false; }
            string property = paintingProperties[paintingProperty];
            hit.uv = Vector2.Scale(hit.uv, PaintingMaterial.GetTextureScale(property)) + PaintingMaterial.GetTextureOffset(property);
            if (hit.uv.x < 0 || hit.uv.y < 0 || hit.uv.x > 1 || hit.uv.y > 1)
            { paintingStatus = "This brush supports the 0–1 texture tile. UVs outside that tile need a UV edit before painting."; return false; }
            paintingStatus = null;
            return true;
        }

        private void DrawPaintingScene(SceneView view)
        {
            var e = Event.current;
            if (viewport.modelGridVisible) viewport.DrawGroundGrid();
            if (e.type == EventType.Repaint) viewport.RecordWorkspaceFrame(EditorApplication.timeSinceStartup);
            if (!paintingImage) return;
            if (paintingStroke && (e.type == EventType.Ignore || e.type == EventType.MouseLeaveWindow || GUIUtility.hotControl != paintingControl)) EndPaintingStroke();
            if (e.rawType == EventType.MouseUp && paintingStroke) { EndPaintingStroke(); e.Use(); return; }
            var rect = new Rect(0, 0, view.position.width, view.position.height);
            if (paintingImage.SurfaceBrushAdjustment(rect))
            {
                if (e.type == EventType.Repaint)
                {
                    bool adjustmentHit = TryPaintingHit(e.mousePosition, out var adjustmentSurface);
                    DrawPaintingCursor(view, adjustmentHit, adjustmentSurface);
                }
                return;
            }
            if (HandlePaintingKeyboard()) return;
            int id = GUIUtility.GetControlID("MashBox surface painting".GetHashCode(), FocusType.Passive);
            if (e.type == EventType.Layout && !e.alt) HandleUtility.AddDefaultControl(id);
            if (e.alt) { if (paintingStroke) EndPaintingStroke(); return; }
            bool hitFound = TryPaintingHit(e.mousePosition, out var hit);
            if (e.type == EventType.MouseDown && e.button == 0 && GUIUtility.hotControl == 0 && HandleUtility.nearestControl == id && hitFound)
            {
                paintingStroke = paintingImage.BeginSurfaceStroke(hit.uv);
                if (paintingStroke) { GUIUtility.hotControl = paintingControl = id; paintingLastMouse = e.mousePosition; paintingImage.FlushSurfaceStroke(); }
                e.Use(); view.Repaint();
            }
            else if (paintingStroke && e.type == EventType.MouseDrag && e.button == 0)
            {
                // Resample screen positions, then raycast each one. Never interpolate between
                // unrelated UV coordinates on opposite sides of a seam or empty space.
                int steps = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(paintingLastMouse, e.mousePosition) / 2), 1, 256);
                for (int i = 1; i <= steps; i++)
                {
                    var point = Vector2.Lerp(paintingLastMouse, e.mousePosition, i / (float)steps);
                    if (TryPaintingHit(point, out var sample)) paintingImage.SurfaceDab(sample.uv);
                    else paintingImage.BreakSurfaceStroke();
                }
                paintingLastMouse = e.mousePosition;
                paintingImage.FlushSurfaceStroke(); e.Use(); view.Repaint();
            }
            if (e.type == EventType.MouseMove || e.type == EventType.MouseLeaveWindow)
                viewport.MarkDirty(MashBoxStudioViewport.DirtyState.Overlay);
            if (e.type == EventType.Repaint) DrawPaintingCursor(view, hitFound, hit);
        }

        private void DrawPaintingCursor(SceneView view, bool hitFound, StudioPaintingSurface.Hit hit)
        {
            var mouse = Event.current.mousePosition;
            var rootMouse = mouse + viewport.CanvasInputOrigin;
            if (!viewport.ModelCanvasRect.Contains(rootMouse) || viewport.IsOverViewportControls(rootMouse)) return;
            float fallbackRadius = Mathf.Clamp(paintingImage.SurfaceBrushRadius, 4, 200);
            bool projected = false;
            if (hitFound && paintingSurface.BrushAxes(hit, paintingImage.SurfaceUvChannel, out var u, out var v))
            {
                var scale = PaintingMaterial.GetTextureScale(paintingProperties[paintingProperty]);
                if (Mathf.Abs(scale.x) > 1e-6f && Mathf.Abs(scale.y) > 1e-6f)
                {
                    float radius = paintingImage.SurfaceBrushRadius / paintingImage.SurfaceResolution;
                    var center = HandleUtility.WorldToGUIPoint(hit.point);
                    projected = true;
                    for (int i = 0; i < paintingBrushRing.Length; i++)
                    {
                        float angle = i * Mathf.PI * 2 / (paintingBrushRing.Length - 1);
                        var world = hit.point + radius * (u * (Mathf.Cos(angle) / scale.x) + v * (Mathf.Sin(angle) / scale.y));
                        var offset = HandleUtility.WorldToGUIPoint(world) - center;
                        if (view.camera.WorldToViewportPoint(world).z <= view.camera.nearClipPlane ||
                            float.IsNaN(offset.sqrMagnitude) || offset.sqrMagnitude > 512 * 512)
                        { projected = false; break; }
                        paintingBrushRing[i] = mouse + offset;
                    }
                }
            }
            if (!projected)
                for (int i = 0; i < paintingBrushRing.Length; i++)
                {
                    float angle = i * Mathf.PI * 2 / (paintingBrushRing.Length - 1);
                    paintingBrushRing[i] = mouse + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * fallbackRadius;
                }
            // Cursor feedback is an overlay, not scene geometry. Depth testing a
            // tangent-plane ring clips it against curved faces and causes broken arcs.
            var color = Handles.color; var depth = Handles.zTest; var matrix = Handles.matrix;
            Handles.BeginGUI();
            try
            {
                Handles.matrix = Matrix4x4.identity; Handles.zTest = CompareFunction.Always;
                Handles.color = new Color(0, 0, 0, .8f); Handles.DrawAAPolyLine(4, paintingBrushRing);
                Handles.color = hitFound ? Color.white : new Color(1, 1, 1, .65f); Handles.DrawAAPolyLine(1.5f, paintingBrushRing);
            }
            finally { Handles.EndGUI(); Handles.color = color; Handles.zTest = depth; Handles.matrix = matrix; }
        }

        private void EndPaintingStroke()
        {
            paintingImage?.EndSurfaceInput();
            if (paintingStroke && GUIUtility.hotControl == paintingControl) GUIUtility.hotControl = 0;
            paintingStroke = false;
        }

        private void LeavePaintingMode()
        {
            if (!paintingMode) return;
            viewport?.rootVisualElement.UnregisterCallback<UnityEngine.UIElements.KeyDownEvent>(ShieldPaintingHistory, UnityEngine.UIElements.TrickleDown.TrickleDown);
            EndPaintingStroke(); paintingImage?.SuspendSurface(); paintingImage = null;
            paintingSurface?.Dispose(); paintingSurface = null;
            modelEditor?.UsePaintingMaterials(false);
            paintingMode = false;
        }

        private void ReleasePainting()
        {
            viewport?.rootVisualElement.UnregisterCallback<UnityEngine.UIElements.KeyDownEvent>(ShieldPaintingHistory, UnityEngine.UIElements.TrickleDown.TrickleDown);
            EndPaintingStroke();
            foreach (var map in paintingMaps) if (map.image) map.image.SuspendSurface();
            paintingSurface?.Dispose(); paintingSurface = null;
        }
    }

    internal sealed partial class StudioModelEditor
    {
        internal bool ZeroPaintingRoots(int firstNode, int selectedNode, bool recordUndo)
        {
            if (!Document) return false;
            int root = selectedNode;
            if (root >= Document.nodes.Count) return false;
            while (root >= 0 && Document.nodes[root].parent >= 0) root = Document.nodes[root].parent;
            var roots = Enumerable.Range(Mathf.Clamp(firstNode, 0, Document.nodes.Count), Document.nodes.Count - Mathf.Clamp(firstNode, 0, Document.nodes.Count))
                .Where(i => Document.nodes[i].parent < 0 && (root < 0 || i == root))
                .Where(i => {
                    var n = Document.nodes[i];
                    return n.position != Vector3.zero || n.positionOffset != Vector3.zero ||
                        !StudioModelDocument.Node.SameRotation(n.rotation, Quaternion.identity) || n.scale != Vector3.one;
                }).ToArray();
            if (roots.Length == 0) return false;
            if (recordUndo) StudioUndo.RegisterCompleteObjectUndo(Document, "Zero painting model placement");
            foreach (int i in roots)
            {
                // Match Skinner's model-local import space. Only the outer placement
                // is reset: child locals, mesh vertices and skin bindposes stay intact.
                var node = Document.nodes[i];
                node.position = node.positionOffset = Vector3.zero;
                node.rotation = Quaternion.identity; node.RotationChannels = Vector3.zero;
                node.scale = Vector3.one;
                if (i < Transforms.Length && Transforms[i]) Document.ApplyTransform(i, Transforms[i]);
            }
            Changed(false);
            return true;
        }

        internal void SetPaintingVisibility(Renderer renderer, bool visible)
        {
            int index = Array.IndexOf(Transforms, renderer.transform);
            if (!Document || index < 0 || index >= Document.nodes.Count || Document.nodes[index].active == visible) return;
            StudioUndo.RecordObject(Document, "Show painting mesh");
            Document.nodes[index].active = visible;
            renderer.gameObject.SetActive(visible);
            Changed(false);
        }
        internal void ImportPaintingModel(Action<StudioModelDocument> loaded)
        {
            var path = EditorUtility.OpenFilePanel("Import FBX for Painting", "", "fbx");
            if (!string.IsNullOrEmpty(path)) ImportPath(path, loaded);
        }
        internal void ConfigurePaintingLighting(Camera camera, float exposure)
        {
            if (!camera || previewLighting == null) return;
            // Light rays follow the view into the surface. Opposing, shallow
            // offsets keep form readable without losing the back when orbiting.
            previewLighting.Key.transform.rotation = camera.transform.rotation * Quaternion.Euler(20, -25, 0);
            previewLighting.Fill.transform.rotation = camera.transform.rotation * Quaternion.Euler(-15, 30, 0);
            previewLighting.SetMaterialPreviewExposure(exposure);
        }

        internal void UsePaintingMaterials(bool painting)
        {
            // Modeling's default camera-lit shader supplies its own illumination and
            // leaves the actual lights off. Original HDRP materials need the real
            // material-preview rig, exposure and reflection environment instead.
            // Recreate the disposable rig so material-preview camera overrides and
            // reflection probes cannot leak back into Modeling's lighting settings.
            previewLighting?.Dispose();
            previewLighting = new StudioPreviewLighting(Scene);
            if (painting) previewLighting.ApplyMaterialPreview();
            else { previewLighting.Apply(LightingSettings); ApplyDisplayMaterials(); return; }
            if (!Document) return;
            for (int i = 0; i < Transforms.Length && i < Document.nodes.Count; i++)
            {
                var renderer = Transforms[i] ? Transforms[i].GetComponent<Renderer>() : null;
                if (!renderer) continue;
                renderer.forceRenderingOff = false;
                renderer.sharedMaterials = Document.nodes[i].materials ?? Array.Empty<Material>();
            }
        }
    }
}
