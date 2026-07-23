using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MashBoxSDK.Maps.Painting;
using MashBoxSDK.Maps.Spline;
using MashBoxSDK.SDKMain;

namespace MashBoxSDK.MapTools
{
    public class MGBrushWindow : EditorWindow
    {
        private enum ToolMode { Decor, Painter, SplatMap }
        [SerializeField] private ToolMode currentMode = ToolMode.Decor;

        // --- Common Settings ---
        private float brushRadius = 2.0f;
        private float brushStrength = 0.5f;

        // --- Decor Settings ---
        [SerializeField] private List<GameObject> prefabPalette = new List<GameObject>();
        [SerializeField] private bool prefabPaletteExpanded;
        private int selectedPrefabIndex = 0;
        private bool scatterMode = true;
        private bool alignToSurface = true;
        private bool gridSnapping = false;
        private float gridSize = 1.0f;
        private Vector2 scaleRange = new Vector2(0.8f, 1.2f);
        private bool randomizeRotationY = true;
        private float scatterDensity = 0.5f;
        private float yOffset = 0f;

        // --- Painter Settings ---
        private enum UVChannel { UV0 = 0, UV1 = 1, UV2 = 2, UV3 = 3 }
        private enum PainterEditMode { CloneOnTarget, ProxyCopy }
        private const string PaintProxyName = "MG Brush Paint Proxy";
        private PainterEditMode painterEditMode = PainterEditMode.ProxyCopy;
        private bool hideSourceRendererForProxy = true;
        private UVChannel targetUVChannel = UVChannel.UV1;
        private Color paintColor
        {
            get => MBEditorToolState.PaintColor;
            set => MBEditorToolState.PaintColor = value;
        }
        private bool useFalloff = true;
        [SerializeField, Range(0.05f, 1f)] private float vertexPaintSpacing = 0.2f;
        private bool painterBrushActive = true;
        private bool wPauseHeld;
        [SerializeField] private List<GameObject> paintTargets = new List<GameObject>();
        private string painterStatusMessage = "Add mesh objects here before painting. Only listed targets can be cloned or modified.";

        // --- Splat Map Settings ---
        private enum SplatChannel { Red, Green, Blue, Alpha }
        [SerializeField] private Texture2D splatMapTexture;
        [SerializeField] private SplatChannel splatChannel;
        [SerializeField, Range(1, 512)] private int splatBrushPixels = 48;
        [SerializeField, Range(0f, 1f)] private float splatPaintWeight = 1f;
        [SerializeField] private bool normalizeSplatWeights = true;
        [SerializeField] private bool splatUseFalloff = true;
        [SerializeField] private int newSplatResolution = 1024;
        private string splatStatusMessage = "Assign a splat-map texture, then paint through a MeshCollider's UV0 coordinates.";
        private bool splatTextureDirty;
        private bool splatUndoRegistered;

        // --- Internal State ---
        private Vector2 scrollPos;
        private GameObject previewObject;
        private Vector3 lastHitPoint;
        private Vector3 lastHitNormal;
        private bool isPainting = false;
        private float lastScatterTime = 0f;
        private HashSet<Mesh> strokeMeshes = new HashSet<Mesh>();
        private bool sceneToolActive;
        private bool sceneCameraRightMouseHeld;
        private bool isAdjustingBrush;
        private Vector2 brushAdjustMousePosition;
        private bool hasBrushAdjustSurface;
        private Vector3 brushAdjustHitPoint;
        private Vector3 brushAdjustHitNormal;
        private bool paintTargetCacheDirty = true;
        private int cachedValidPaintTargetCount;
        private int paintUndoGroup = -1;
        private bool hasLastLoftPaintPoint;
        private Vector3 lastLoftPaintPoint;
        private bool clearingVisualSelection;
        private UnityEngine.Object[] lastVisualEditingSelection = System.Array.Empty<UnityEngine.Object>();

        public static void ShowWindow()
        {
            GetWindow<MGBrushWindow>("MG Brush");
        }

        private void OnEnable()
        {
            currentMode = (ToolMode)MBEditorToolState.BrushMode;
            MBEditorToolState.BrushModeChanged -= OnSharedBrushModeChanged;
            MBEditorToolState.BrushModeChanged += OnSharedBrushModeChanged;
            MBEditorToolState.PaintColorChanged -= OnSharedPaintColorChanged;
            MBEditorToolState.PaintColorChanged += OnSharedPaintColorChanged;
            EditorApplication.hierarchyChanged -= InvalidatePaintTargetCache;
            EditorApplication.hierarchyChanged += InvalidatePaintTargetCache;
            InvalidatePaintTargetCache();
            if (MBEditorToolState.ActiveEditing)
                ActivateSceneTool();
            else
                DeactivateSceneTool();
        }

        private void OnDisable()
        {
            MBEditorToolState.BrushModeChanged -= OnSharedBrushModeChanged;
            MBEditorToolState.PaintColorChanged -= OnSharedPaintColorChanged;
            EditorApplication.hierarchyChanged -= InvalidatePaintTargetCache;
            DeactivateSceneTool();
        }

        public void ActivateSceneTool()
        {
            sceneToolActive = true;
            // Re-register idempotently so B can recover after Unity drops a
            // Scene-view callback during a focus/tool-context transition.
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            Selection.selectionChanged -= OnVisualEditingSelectionChanged;
            Selection.selectionChanged += OnVisualEditingSelectionChanged;
            ClearSelectionForVertexPainting();
        }

        public void DeactivateSceneTool()
        {
            FinishPaintUndoGroup();
            hasLastLoftPaintPoint = false;
            EndBrushAdjustment();
            if (!sceneToolActive)
                return;

            sceneToolActive = false;
            sceneCameraRightMouseHeld = false;
            wPauseHeld = false;
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            Selection.selectionChanged -= OnVisualEditingSelectionChanged;
            CleanupPreview();
        }

        private void OnGUI()
        {
            Draw();
        }

        public void Draw(bool embeddedInParentWindow = false)
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("MG Brush", EditorStyles.boldLabel);

            ToolMode requestedMode = (ToolMode)MashBoxTabDrawer.DrawTabs(
                (int)currentMode,
                new[] { "Decor (Scatter)", "Vertex Painter", "Splat Map" },
                MashBoxTabDrawer.TabVisualStyle.Secondary);
            if (requestedMode != currentMode)
                SetToolMode(requestedMode);
            EditorGUILayout.EndVertical();

            if (!embeddedInParentWindow)
                scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            EditorGUILayout.Space(5);
            brushRadius = EditorGUILayout.Slider("Brush Radius", brushRadius, 0.1f, 10f);
            brushStrength = EditorGUILayout.Slider("Brush Strength", brushStrength, 0.01f, 1f);

            if (currentMode == ToolMode.Decor)
                DrawDecorSettings();
            else if (currentMode == ToolMode.Painter)
                DrawPainterSettings();
            else
                DrawSplatMapSettings();

            if (!embeddedInParentWindow)
                EditorGUILayout.EndScrollView();
        }

        private void SetToolMode(ToolMode mode)
        {
            currentMode = mode;
            MBEditorToolState.BrushMode = (MBBrushMode)mode;
            isPainting = false;
            FinishPaintUndoGroup();
            hasLastLoftPaintPoint = false;
            EndBrushAdjustment();
            strokeMeshes.Clear();
            GUIUtility.hotControl = 0;

            if (mode == ToolMode.Painter || mode == ToolMode.SplatMap)
            {
                CleanupPreview();
                wPauseHeld = false;
                painterBrushActive = true;
                if (mode == ToolMode.Painter)
                    painterStatusMessage = "Brush active. Paint listed targets, or Shift-click a mesh to add it.";
            }

            if (MBEditorToolState.ActiveEditing)
                ActivateSceneTool();
            else
                DeactivateSceneTool();
            GUI.FocusControl(null);
            GUI.changed = true;
            EditorUtility.SetDirty(this);
            InternalEditorUtility.RepaintAllViews();
            SceneView.RepaintAll();
            ClearSelectionForVertexPainting();
        }

        private void OnVisualEditingSelectionChanged()
        {
            ClearSelectionForVertexPainting();
        }

        private void ClearSelectionForVertexPainting()
        {
            if (clearingVisualSelection
                || currentMode != ToolMode.Painter
                || Selection.objects == null
                || Selection.objects.Length == 0)
            {
                return;
            }

            clearingVisualSelection = true;
            lastVisualEditingSelection = Selection.objects;
            Selection.objects = System.Array.Empty<UnityEngine.Object>();
            clearingVisualSelection = false;
            SceneView.RepaintAll();
        }

        private void OnSharedBrushModeChanged()
        {
            ToolMode mode = (ToolMode)MBEditorToolState.BrushMode;
            if (currentMode != mode)
                SetToolMode(mode);
            Repaint();
        }

        private void OnSharedPaintColorChanged()
        {
            Repaint();
            SceneView.RepaintAll();
        }

        private void DrawSplatMapSettings()
        {
            EditorGUILayout.LabelField("Splat Map Texture", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            splatMapTexture = (Texture2D)EditorGUILayout.ObjectField("Texture", splatMapTexture, typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck())
            {
                splatTextureDirty = false;
                splatStatusMessage = splatMapTexture != null
                    ? "Texture assigned. Paint in the Scene view through a MeshCollider."
                    : "Assign a splat-map texture, then paint through a MeshCollider's UV0 coordinates.";
            }

            splatChannel = (SplatChannel)EditorGUILayout.EnumPopup("Paint Channel", splatChannel);
            splatPaintWeight = EditorGUILayout.Slider("Paint Weight", splatPaintWeight, 0f, 1f);
            splatBrushPixels = EditorGUILayout.IntSlider("Texture Brush (Pixels)", splatBrushPixels, 1, 512);
            splatUseFalloff = EditorGUILayout.Toggle("Use Falloff", splatUseFalloff);
            normalizeSplatWeights = EditorGUILayout.Toggle(
                new GUIContent("Normalize RGBA Weights", "Keeps the four splat channels adding up to one while painting."),
                normalizeSplatWeights);

            EditorGUILayout.HelpBox(splatStatusMessage, splatTextureDirty ? MessageType.Warning : MessageType.Info);
            EditorGUILayout.HelpBox("Scene View: paint with Left Mouse. Hold Shift to erase the selected channel. The hit collider must provide UV coordinates (normally a MeshCollider).", MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(splatMapTexture == null))
                {
                    if (GUILayout.Button("Make Readable"))
                        MakeSplatTextureReadable();
                    if (GUILayout.Button("Save Texture"))
                        SaveSplatTexture(false);
                    if (GUILayout.Button("Save As PNG"))
                        SaveSplatTexture(true);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                newSplatResolution = EditorGUILayout.IntPopup(
                    "New Resolution",
                    newSplatResolution,
                    new[] { "256", "512", "1024", "2048", "4096" },
                    new[] { 256, 512, 1024, 2048, 4096 });
                if (GUILayout.Button("Create New Splat Map", GUILayout.Width(180f)))
                    CreateSplatTexture();
            }

            if (splatMapTexture != null)
            {
                Rect previewRect = GUILayoutUtility.GetAspectRect(4f, GUILayout.MaxHeight(180f));
                EditorGUI.DrawPreviewTexture(previewRect, splatMapTexture, null, ScaleMode.ScaleToFit);
            }
        }

        private void OnUndoRedoPerformed()
        {
            RebuildLoftVertexPaintModifiers();
            RefreshPaintTargetMeshes();
        }

        private void DrawDecorSettings()
        {
            EditorGUILayout.LabelField("Placement Settings", EditorStyles.boldLabel);
            scatterMode = EditorGUILayout.Toggle("Scatter Mode", scatterMode);
            alignToSurface = EditorGUILayout.Toggle("Align to Surface", alignToSurface);
            gridSnapping = EditorGUILayout.Toggle("Grid Snapping", gridSnapping);
            if (gridSnapping) gridSize = EditorGUILayout.FloatField("Grid Size", gridSize);
            
            yOffset = EditorGUILayout.FloatField("Y Offset", yOffset);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Randomization", EditorStyles.boldLabel);
            randomizeRotationY = EditorGUILayout.Toggle("Randomize Rotation (Y)", randomizeRotationY);
            scaleRange = EditorGUILayout.Vector2Field("Scale Range (Min/Max)", scaleRange);
            if (scatterMode) scatterDensity = EditorGUILayout.Slider("Scatter Density", scatterDensity, 0.01f, 1f);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Prefab Palette", EditorStyles.boldLabel);
            
            SerializedObject so = new SerializedObject(this);
            SerializedProperty paletteProp = so.FindProperty("prefabPalette");
            DrawPrefabPalette(paletteProp);
            so.ApplyModifiedProperties();

            selectedPrefabIndex = prefabPalette.Count > 0
                ? Mathf.Clamp(selectedPrefabIndex, 0, prefabPalette.Count - 1)
                : 0;

            if (prefabPalette.Count > 0)
            {
                selectedPrefabIndex = EditorGUILayout.IntSlider("Selected Prefab", selectedPrefabIndex, 0, prefabPalette.Count - 1);
            }

            if (GUILayout.Button("Simulate & Settle (Physics)"))
            {
                SimulatePhysics();
            }
        }

        private void DrawPrefabPalette(SerializedProperty paletteProperty)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                prefabPaletteExpanded = EditorGUILayout.Foldout(
                    prefabPaletteExpanded,
                    "Prefab Palette",
                    true);

                int requestedSize = Mathf.Max(0, EditorGUILayout.IntField(
                    paletteProperty.arraySize,
                    GUILayout.Width(60f)));
                if (requestedSize != paletteProperty.arraySize)
                    paletteProperty.arraySize = requestedSize;
            }

            if (!prefabPaletteExpanded)
                return;

            EditorGUI.indentLevel++;
            for (int index = 0; index < paletteProperty.arraySize; index++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(
                        paletteProperty.GetArrayElementAtIndex(index),
                        new GUIContent($"Prefab {index + 1}"));

                    if (GUILayout.Button("−", GUILayout.Width(24f)))
                    {
                        int previousSize = paletteProperty.arraySize;
                        paletteProperty.DeleteArrayElementAtIndex(index);
                        if (paletteProperty.arraySize == previousSize)
                            paletteProperty.DeleteArrayElementAtIndex(index);
                        break;
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Add Prefab", GUILayout.Width(110f)))
                {
                    int newIndex = paletteProperty.arraySize;
                    paletteProperty.InsertArrayElementAtIndex(newIndex);
                    paletteProperty.GetArrayElementAtIndex(newIndex).objectReferenceValue = null;
                }
            }
            EditorGUI.indentLevel--;
        }

        private void DrawPainterSettings()
        {
            EditorGUILayout.LabelField("Vertex Color Settings", EditorStyles.boldLabel);
            bool hasLoftTarget = HasLoftPaintTarget();
            if (hasLoftTarget && painterEditMode != PainterEditMode.CloneOnTarget)
            {
                painterEditMode = PainterEditMode.CloneOnTarget;
                ApplyProxyRendererVisibility();
            }

            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledScope(hasLoftTarget))
                painterEditMode = (PainterEditMode)EditorGUILayout.EnumPopup("Edit Mode", painterEditMode);
            if (painterEditMode == PainterEditMode.ProxyCopy)
                hideSourceRendererForProxy = EditorGUILayout.Toggle("Hide Source Renderer", hideSourceRendererForProxy);
            if (EditorGUI.EndChangeCheck())
                ApplyProxyRendererVisibility();

            if (hasLoftTarget)
                EditorGUILayout.HelpBox("Loft painting uses Clone On Target. Brush strokes are stored locally and replayed after loft regeneration.", MessageType.Info);

            paintColor = EditorGUILayout.ColorField("Paint Color", paintColor);
            useFalloff = EditorGUILayout.Toggle("Use Falloff", useFalloff);
            if (hasLoftTarget)
                vertexPaintSpacing = EditorGUILayout.Slider("Stroke Spacing", vertexPaintSpacing, 0.05f, 1f);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("UV Generation", EditorStyles.boldLabel);
            targetUVChannel = (UVChannel)EditorGUILayout.EnumPopup("Target UV Channel", targetUVChannel);

            EditorGUILayout.HelpBox("Painting modifies vertex colors. Auto UV will generate unwrapped coordinates for the selected channel.", MessageType.Info);
            EditorGUILayout.HelpBox("Scene View: Ctrl+Middle-drag adjusts the brush horizontally for radius and vertically for strength.", MessageType.None);

            DrawPaintTargetSettings();
            if (hasLoftTarget)
                DrawLoftVertexPaintHistory();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(GetValidPaintTargetCount() == 0))
            {
                if (GUILayout.Button("Flood Color"))
                    FloodPaintTargets();

                if (GUILayout.Button("Auto UV Targets"))
                    GenerateAutoUVsForPaintTargets();
            }
            if (GUILayout.Button("Save Mesh"))
            {
                SaveSelectedMesh();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLoftVertexPaintHistory()
        {
            MultiSplineLoft loft = GetFocusedPaintTargetLoft();
            VertexPaintModifier modifier = loft != null ? loft.VertexPaintModifier : null;

            EditorGUILayout.Space(5f);
            EditorGUILayout.LabelField("Loft Paint History", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                loft != null
                    ? $"{loft.gameObject.name}: {(modifier != null ? modifier.StrokeCount : 0)} recorded strokes"
                    : "No loft target selected.",
                EditorStyles.miniLabel);

            using (new EditorGUI.DisabledScope(modifier == null || modifier.StrokeCount == 0))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Remove Last"))
                {
                    Undo.RecordObject(modifier, "Remove Vertex Paint Stroke");
                    modifier.RemoveLastStroke();
                    modifier.Rebuild();
                    EditorUtility.SetDirty(modifier);
                }

                if (GUILayout.Button("Clear Strokes"))
                {
                    Undo.RecordObject(modifier, "Clear Vertex Paint Strokes");
                    modifier.ClearStrokes();
                    modifier.Rebuild();
                    EditorUtility.SetDirty(modifier);
                }
            }
        }

        private void DrawPaintTargetSettings()
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Paint Targets", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(painterStatusMessage, MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Selection", GUILayout.Height(24f)))
                    AddSelectedPaintTargets();

                using (new EditorGUI.DisabledScope(paintTargets.Count == 0))
                {
                    if (GUILayout.Button("Clean", GUILayout.Width(72f), GUILayout.Height(24f)))
                        CleanPaintTargets();

                    if (GUILayout.Button("Clear", GUILayout.Width(72f), GUILayout.Height(24f)))
                    {
                        Undo.RecordObject(this, "Clear Paint Targets");
                        paintTargets.Clear();
                        InvalidatePaintTargetCache();
                        painterStatusMessage = "Paint target list cleared.";
                    }
                }
            }

            DrawPaintTargetDropZone();

            SerializedObject so = new SerializedObject(this);
            SerializedProperty targetsProperty = so.FindProperty("paintTargets");
            EditorGUILayout.PropertyField(targetsProperty, true);
            if (so.ApplyModifiedProperties())
                InvalidatePaintTargetCache();
        }

        private void DrawPaintTargetDropZone()
        {
            Rect dropRect = GUILayoutUtility.GetRect(0f, 42f, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "Drag paint target objects here");

            Event current = Event.current;
            if (!dropRect.Contains(current.mousePosition))
                return;

            if (current.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                current.Use();
            }
            else if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                AddPaintTargets(DragAndDrop.objectReferences);
                current.Use();
            }
        }

        private void AddSelectedPaintTargets()
        {
            UnityEngine.Object[] selectedObjects = Selection.objects != null && Selection.objects.Length > 0
                ? Selection.objects
                : lastVisualEditingSelection;
            AddPaintTargets(selectedObjects);
        }

        private void AddPaintTargets(Object[] objects)
        {
            if (objects == null || objects.Length == 0)
            {
                painterStatusMessage = "No objects were added.";
                return;
            }

            Undo.RecordObject(this, "Add Paint Targets");
            int added = 0;
            for (int i = 0; i < objects.Length; i++)
            {
                if (TryAddPaintTarget(objects[i]))
                    added++;
            }

            painterStatusMessage = added > 0
                ? $"Added {added} paint target{(added == 1 ? string.Empty : "s")}."
                : "No new mesh targets were added.";
        }

        private bool TryAddPaintTarget(Object obj)
        {
            GameObject gameObject = obj switch
            {
                GameObject go => go,
                Component component => component.gameObject,
                _ => null
            };

            if (!gameObject || !HasPaintableMesh(gameObject) || paintTargets.Contains(gameObject))
                return false;

            paintTargets.Add(gameObject);
            if (ResolvePaintTargetLoft(gameObject) != null)
            {
                painterEditMode = PainterEditMode.CloneOnTarget;
                ApplyProxyRendererVisibility();
            }
            InvalidatePaintTargetCache();
            return true;
        }

        private void AddPaintTargetFromHit(RaycastHit hit)
        {
            MeshFilter meshFilter = GetPaintMeshFilter(hit);
            if (!meshFilter)
                return;

            if (TryAddPaintTarget(meshFilter.gameObject))
                painterStatusMessage = $"Added '{meshFilter.gameObject.name}' to Paint Targets.";
            else
                painterStatusMessage = $"'{meshFilter.gameObject.name}' is already in Paint Targets.";
        }

        private Color GetPainterBrushColor(RaycastHit hit, bool shiftPressed)
        {
            MeshFilter meshFilter = GetPaintMeshFilter(hit);
            if (!meshFilter)
                return new Color(0.7f, 0.7f, 0.7f, 0.85f);

            if (IsPaintTarget(meshFilter.gameObject))
                return new Color(0.25f, 1f, 0.45f, 0.95f);

            return shiftPressed
                ? new Color(0.2f, 0.75f, 1f, 0.95f)
                : new Color(1f, 0.55f, 0.15f, 0.9f);
        }

        private void DrawPainterHoverLabel(RaycastHit hit, bool shiftPressed)
        {
            MeshFilter meshFilter = GetPaintMeshFilter(hit);
            string label;

            if (!meshFilter)
            {
                label = "No Mesh";
            }
            else if (IsPaintTarget(meshFilter.gameObject))
            {
                label = $"Active: {meshFilter.gameObject.name}";
            }
            else
            {
                label = shiftPressed
                    ? $"Click to Add: {meshFilter.gameObject.name}"
                    : "Shift+Click Add Target";
            }

            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            style.normal.textColor = GetPainterBrushColor(hit, shiftPressed);

            float handleSize = HandleUtility.GetHandleSize(hit.point);
            Handles.Label(hit.point + hit.normal * handleSize * 0.22f, label, style);
        }

        private void CleanPaintTargets()
        {
            for (int i = paintTargets.Count - 1; i >= 0; i--)
            {
                if (!paintTargets[i] || !HasPaintableMesh(paintTargets[i]))
                    paintTargets.RemoveAt(i);
            }

            cachedValidPaintTargetCount = 0;
            for (int i = 0; i < paintTargets.Count; i++)
            {
                if (paintTargets[i] && HasPaintableMesh(paintTargets[i]))
                    cachedValidPaintTargetCount++;
            }
            paintTargetCacheDirty = false;
        }

        private int GetValidPaintTargetCount()
        {
            if (paintTargetCacheDirty)
                CleanPaintTargets();
            return cachedValidPaintTargetCount;
        }

        private void InvalidatePaintTargetCache()
        {
            paintTargetCacheDirty = true;
        }

        private static bool HasPaintableMesh(GameObject gameObject)
        {
            return gameObject && gameObject.GetComponentInChildren<MeshFilter>() != null;
        }

        private bool HasLoftPaintTarget()
        {
            for (int i = 0; i < paintTargets.Count; i++)
            {
                if (ResolvePaintTargetLoft(paintTargets[i]) != null)
                    return true;
            }

            return false;
        }

        private MultiSplineLoft GetFocusedPaintTargetLoft()
        {
            MultiSplineLoft selectedLoft = ResolvePaintTargetLoft(Selection.activeGameObject);
            if (selectedLoft != null)
            {
                for (int i = 0; i < paintTargets.Count; i++)
                {
                    GameObject target = paintTargets[i];
                    if (target && (selectedLoft.gameObject == target || selectedLoft.transform.IsChildOf(target.transform)))
                        return selectedLoft;
                }
            }

            for (int i = 0; i < paintTargets.Count; i++)
            {
                MultiSplineLoft loft = ResolvePaintTargetLoft(paintTargets[i]);
                if (loft != null)
                    return loft;
            }

            return null;
        }

        private static MultiSplineLoft ResolvePaintTargetLoft(GameObject gameObject)
        {
            if (!gameObject)
                return null;

            return gameObject.GetComponent<MultiSplineLoft>()
                ?? gameObject.GetComponentInParent<MultiSplineLoft>()
                ?? gameObject.GetComponentInChildren<MultiSplineLoft>();
        }

        private static MultiSplineLoft ResolvePaintTargetLoft(MeshFilter meshFilter)
        {
            if (!meshFilter)
                return null;

            return meshFilter.GetComponent<MultiSplineLoft>()
                ?? meshFilter.GetComponentInParent<MultiSplineLoft>();
        }

        private bool IsPaintTarget(GameObject gameObject)
        {
            if (!gameObject)
                return false;

            for (int i = 0; i < paintTargets.Count; i++)
            {
                GameObject target = paintTargets[i];
                if (!target)
                    continue;

                if (gameObject == target || gameObject.transform.IsChildOf(target.transform))
                    return true;
            }

            return false;
        }

        private void FloodPaintTargets()
        {
            int floodedMeshes = 0;
            for (int i = 0; i < paintTargets.Count; i++)
            {
                GameObject target = paintTargets[i];
                if (!target)
                    continue;

                MeshFilter[] meshFilters = target.GetComponentsInChildren<MeshFilter>();
                for (int meshIndex = 0; meshIndex < meshFilters.Length; meshIndex++)
                {
                    if (meshFilters[meshIndex] && !IsPaintProxyMeshFilter(meshFilters[meshIndex]))
                    {
                        FillMesh(meshFilters[meshIndex].gameObject);
                        floodedMeshes++;
                    }
                }
            }

            painterStatusMessage = floodedMeshes > 0
                ? $"Flooded {floodedMeshes} target mesh{(floodedMeshes == 1 ? string.Empty : "es")}."
                : "No paint target meshes were flooded.";
            RefreshPaintTargetMeshes();
        }

        private void GenerateAutoUVsForPaintTargets()
        {
            for (int i = 0; i < paintTargets.Count; i++)
            {
                GameObject target = paintTargets[i];
                if (!target)
                    continue;

                MeshFilter[] meshFilters = target.GetComponentsInChildren<MeshFilter>();
                for (int meshIndex = 0; meshIndex < meshFilters.Length; meshIndex++)
                {
                    if (meshFilters[meshIndex] && !IsPaintProxyMeshFilter(meshFilters[meshIndex]))
                        GenerateAutoUVs(meshFilters[meshIndex].gameObject);
                }
            }
        }

        private static MeshFilter GetPaintMeshFilter(RaycastHit hit)
        {
            if (!hit.collider)
                return null;

            MeshFilter meshFilter = hit.collider.GetComponent<MeshFilter>();
            return meshFilter ? meshFilter : hit.collider.GetComponentInParent<MeshFilter>();
        }

        private Mesh EnsureEditableMesh(MeshFilter meshFilter, string undoName, out MeshFilter editableMeshFilter)
        {
            editableMeshFilter = null;
            if (!meshFilter || !meshFilter.sharedMesh)
                return null;

            MultiSplineLoft loft = ResolvePaintTargetLoft(meshFilter);
            if (loft != null)
            {
                painterEditMode = PainterEditMode.CloneOnTarget;
                editableMeshFilter = loft.GetComponent<MeshFilter>();
                return loft.GeneratedMesh;
            }

            editableMeshFilter = painterEditMode == PainterEditMode.ProxyCopy
                ? EnsurePaintProxyMeshFilter(meshFilter, undoName)
                : meshFilter;

            if (!editableMeshFilter || !editableMeshFilter.sharedMesh)
                return null;

            Mesh mesh = editableMeshFilter.sharedMesh;
            Undo.RecordObject(editableMeshFilter, undoName);

            if (!mesh.name.Contains("(Clone)"))
            {
                mesh = Instantiate(editableMeshFilter.sharedMesh);
                mesh.name = editableMeshFilter.sharedMesh.name + " (Clone)";
                editableMeshFilter.sharedMesh = mesh;
                painterStatusMessage = painterEditMode == PainterEditMode.ProxyCopy
                    ? $"Created paint proxy mesh for '{meshFilter.gameObject.name}'."
                    : $"Created editable mesh clone for '{meshFilter.gameObject.name}'.";
            }

            return mesh;
        }

        private MeshFilter EnsurePaintProxyMeshFilter(MeshFilter sourceMeshFilter, string undoName)
        {
            Transform proxyTransform = sourceMeshFilter.transform.Find(PaintProxyName);
            GameObject proxyObject = proxyTransform ? proxyTransform.gameObject : null;
            if (!proxyObject)
            {
                proxyObject = new GameObject(PaintProxyName);
                Undo.RegisterCreatedObjectUndo(proxyObject, undoName);
                proxyTransform = proxyObject.transform;
                proxyTransform.SetParent(sourceMeshFilter.transform, false);
                proxyTransform.localPosition = Vector3.zero;
                proxyTransform.localRotation = Quaternion.identity;
                proxyTransform.localScale = Vector3.one;
                proxyObject.hideFlags = HideFlags.NotEditable;
            }

            MeshFilter proxyMeshFilter = proxyObject.GetComponent<MeshFilter>();
            if (!proxyMeshFilter)
                proxyMeshFilter = Undo.AddComponent<MeshFilter>(proxyObject);

            MeshRenderer proxyRenderer = proxyObject.GetComponent<MeshRenderer>();
            if (!proxyRenderer)
                proxyRenderer = Undo.AddComponent<MeshRenderer>(proxyObject);

            Renderer sourceRenderer = sourceMeshFilter.GetComponent<Renderer>();
            if (sourceRenderer)
            {
                Undo.RecordObject(proxyRenderer, undoName);
                proxyRenderer.sharedMaterials = sourceRenderer.sharedMaterials;

                Undo.RecordObject(sourceRenderer, undoName);
                sourceRenderer.enabled = !hideSourceRendererForProxy;
            }

            if (proxyMeshFilter.sharedMesh == null)
            {
                Mesh proxyMesh = Instantiate(sourceMeshFilter.sharedMesh);
                proxyMesh.name = sourceMeshFilter.sharedMesh.name + " (Clone)";
                proxyMeshFilter.sharedMesh = proxyMesh;
            }

            proxyObject.SetActive(true);
            return proxyMeshFilter;
        }

        private static bool IsPaintProxyMeshFilter(MeshFilter meshFilter)
        {
            return meshFilter && string.Equals(meshFilter.gameObject.name, PaintProxyName, System.StringComparison.Ordinal);
        }

        private MeshFilter GetEditableMeshFilter(MeshFilter sourceMeshFilter)
        {
            if (!sourceMeshFilter)
                return null;

            if (painterEditMode != PainterEditMode.ProxyCopy)
                return sourceMeshFilter;

            Transform proxyTransform = sourceMeshFilter.transform.Find(PaintProxyName);
            return proxyTransform ? proxyTransform.GetComponent<MeshFilter>() : sourceMeshFilter;
        }

        private void ApplyProxyRendererVisibility()
        {
            for (int i = 0; i < paintTargets.Count; i++)
            {
                GameObject target = paintTargets[i];
                if (!target)
                    continue;

                MeshFilter[] meshFilters = target.GetComponentsInChildren<MeshFilter>(true);
                for (int meshIndex = 0; meshIndex < meshFilters.Length; meshIndex++)
                {
                    MeshFilter meshFilter = meshFilters[meshIndex];
                    if (!meshFilter || IsPaintProxyMeshFilter(meshFilter))
                        continue;

                    Transform proxyTransform = meshFilter.transform.Find(PaintProxyName);
                    if (!proxyTransform)
                        continue;

                    Renderer proxyRenderer = proxyTransform.GetComponent<Renderer>();
                    if (proxyRenderer)
                        proxyRenderer.enabled = painterEditMode == PainterEditMode.ProxyCopy;

                    Renderer sourceRenderer = meshFilter.GetComponent<Renderer>();
                    if (sourceRenderer)
                        sourceRenderer.enabled = !(painterEditMode == PainterEditMode.ProxyCopy && hideSourceRendererForProxy);
                }
            }
        }

        private Mesh EnsureEditableMesh(MeshFilter meshFilter, string undoName)
        {
            return EnsureEditableMesh(meshFilter, undoName, out _);
        }

        private void RefreshPaintTargetMeshes()
        {
            for (int i = 0; i < paintTargets.Count; i++)
            {
                GameObject target = paintTargets[i];
                if (!target)
                    continue;

                MeshFilter[] meshFilters = target.GetComponentsInChildren<MeshFilter>(true);
                for (int meshIndex = 0; meshIndex < meshFilters.Length; meshIndex++)
                    RefreshPaintMesh(meshFilters[meshIndex]);
            }

            SceneView.RepaintAll();
            Repaint();
        }

        private static void RefreshPaintMesh(MeshFilter meshFilter)
        {
            if (!meshFilter || !meshFilter.sharedMesh)
                return;

            Mesh mesh = meshFilter.sharedMesh;
            EditorUtility.SetDirty(mesh);
            EditorUtility.SetDirty(meshFilter);
            EditorUtility.SetDirty(meshFilter.gameObject);
            mesh.UploadMeshData(false);
        }

        private void GenerateAutoUVs(GameObject go)
        {
            if (go == null) return;
            if (!IsPaintTarget(go))
            {
                painterStatusMessage = $"Skipped '{go.name}'. Add it to Paint Targets before generating UVs.";
                return;
            }

            if (ResolvePaintTargetLoft(go) != null)
            {
                painterStatusMessage = $"Skipped '{go.name}'. Loft UVs are controlled by the loft generator.";
                return;
            }

            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;

            Mesh mesh = EnsureEditableMesh(mf, "Generate Auto UVs", out MeshFilter editableMeshFilter);
            if (mesh == null)
                return;

            Undo.RegisterCompleteObjectUndo(mesh, "Generate Auto UVs");

            // Unity's Unwrapping.GenerateSecondaryUVSet always targets the 'UV1' (index 1) channel.
            // We use it as our "Auto UV" generator.
            Unwrapping.GenerateSecondaryUVSet(mesh);

            // If the user wanted a different channel, we copy the generated data there.
            if (targetUVChannel != UVChannel.UV1)
            {
                List<Vector2> generatedUVs = new List<Vector2>();
                mesh.GetUVs(1, generatedUVs);
                mesh.SetUVs((int)targetUVChannel, generatedUVs);
                
                // Optional: Clear UV1 if it wasn't intended to be modified? 
                // Usually better to leave it unless requested, as UV1 is the "standard" place.
            }
            
            Debug.Log($"Generated Auto UVs for {go.name} on channel {targetUVChannel}");
            RefreshPaintMesh(editableMeshFilter);
        }

        private void FillMesh(GameObject go)
        {
            if (go == null) return;
            if (!IsPaintTarget(go))
            {
                painterStatusMessage = $"Skipped '{go.name}'. Add it to Paint Targets before filling colors.";
                return;
            }

            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;

            MultiSplineLoft loft = ResolvePaintTargetLoft(mf);
            if (loft != null)
            {
                VertexPaintModifier modifier = EnsureLoftVertexPaintModifier(loft);
                if (modifier == null)
                    return;

                Undo.RecordObject(modifier, "Fill Loft Vertex Color");
                modifier.AddStrokeAndApply(modifier.CreateFill(paintColor));
                EditorUtility.SetDirty(modifier);
                return;
            }

            Mesh mesh = EnsureEditableMesh(mf, "Fill Vertex Color", out MeshFilter editableMeshFilter);
            if (mesh == null)
                return;

            Undo.RegisterCompleteObjectUndo(mesh, "Fill Vertex Color");

            Color[] colors = new Color[mesh.vertexCount];
            for (int i = 0; i < colors.Length; i++) colors[i] = paintColor;
            mesh.colors = colors;

            RefreshPaintMesh(editableMeshFilter);
        }

        private void SaveSelectedMesh()
        {
            if (Selection.activeGameObject == null) return;
            if (!IsPaintTarget(Selection.activeGameObject))
            {
                painterStatusMessage = $"Skipped '{Selection.activeGameObject.name}'. Add it to Paint Targets before saving its mesh.";
                return;
            }

            MeshFilter mf = Selection.activeGameObject.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            MeshFilter editableMeshFilter = GetEditableMeshFilter(mf);
            if (editableMeshFilter == null || editableMeshFilter.sharedMesh == null) return;

            string path = EditorUtility.SaveFilePanelInProject("Save Painted Mesh", editableMeshFilter.sharedMesh.name, "asset", "Save your painted mesh as an asset.");
            if (!string.IsNullOrEmpty(path))
            {
                AssetDatabase.CreateAsset(Instantiate(editableMeshFilter.sharedMesh), path);
                AssetDatabase.SaveAssets();
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            Event e = Event.current;

            if ((e.type == EventType.MouseDown || e.rawType == EventType.MouseDown) && e.button == 1)
                sceneCameraRightMouseHeld = true;
            else if ((e.type == EventType.MouseUp || e.rawType == EventType.MouseUp) && e.button == 1)
                sceneCameraRightMouseHeld = false;

            if (e.type == EventType.MouseLeaveWindow || e.type == EventType.Ignore)
            {
                sceneCameraRightMouseHeld = false;
                wPauseHeld = false;
                painterBrushActive = true;
                isPainting = false;
                FinishPaintUndoGroup();
                hasLastLoftPaintPoint = false;
                EndBrushAdjustment();
                strokeMeshes.Clear();
                GUIUtility.hotControl = 0;
            }

            if (isPainting && e.button == 0 && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp))
            {
                isPainting = false;
                splatUndoRegistered = false;
                strokeMeshes.Clear();
                FinishPaintUndoGroup();
                hasLastLoftPaintPoint = false;
                GUIUtility.hotControl = 0;
                if (currentMode == ToolMode.SplatMap && splatTextureDirty)
                    splatStatusMessage = "Splat map changed. Use Save Texture to write the pixels to the source asset.";
                if (e.type != EventType.Used)
                    e.Use();
                sceneView.Repaint();
                return;
            }

            if (e.type == EventType.KeyDown
                && e.keyCode == KeyCode.F
                && !EditorGUIUtility.editingTextField
                && !sceneCameraRightMouseHeld
                && !Tools.viewToolActive
                && !e.shift
                && !e.alt
                && !e.control
                && !e.command)
            {
                Ray focusRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (Physics.Raycast(focusRay, out RaycastHit focusHit))
                {
                    sceneView.LookAt(
                        focusHit.point,
                        sceneView.rotation,
                        Mathf.Max(0.5f, brushRadius * 2f));
                    e.Use();
                    sceneView.Repaint();
                    return;
                }
            }
            
            bool usesMomentaryPause = currentMode == ToolMode.Painter || currentMode == ToolMode.SplatMap;
            if (usesMomentaryPause && !EditorGUIUtility.editingTextField
                && e.keyCode == KeyCode.W)
            {
                if (e.type == EventType.KeyDown)
                {
                    bool cameraNavigation = sceneCameraRightMouseHeld
                        || Tools.viewToolActive
                        || e.shift
                        || e.alt
                        || e.control
                        || e.command;
                    if (cameraNavigation)
                        return;

                    if (!wPauseHeld)
                    {
                        wPauseHeld = true;
                        SetPainterBrushActive(false);
                        Tools.current = Tool.Move;
                    }
                    e.Use();
                    sceneView.Repaint();
                    return;
                }

                if (e.type == EventType.KeyUp && wPauseHeld)
                {
                    wPauseHeld = false;
                    SetPainterBrushActive(true);
                    e.Use();
                    sceneView.Repaint();
                    return;
                }
            }

            // Handle Hotkeys
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha9)
                {
                    int index = e.keyCode - KeyCode.Alpha1;
                    if (index < prefabPalette.Count)
                    {
                        selectedPrefabIndex = index;
                        Repaint();
                    }
                }
                if (e.keyCode == KeyCode.RightArrow)
                {
                    selectedPrefabIndex = (selectedPrefabIndex + 1) % Mathf.Max(1, prefabPalette.Count);
                    Repaint();
                }
                if (e.keyCode == KeyCode.LeftArrow)
                {
                    selectedPrefabIndex = (selectedPrefabIndex - 1 + prefabPalette.Count) % Mathf.Max(1, prefabPalette.Count);
                    Repaint();
                }
            }

            int brushAdjustControlId = GUIUtility.GetControlID("MGBrushAdjust".GetHashCode(), FocusType.Passive);
            if (HandleBrushAdjustment(e, brushAdjustControlId, sceneView))
                return;

            if (usesMomentaryPause)
            {
                if (!painterBrushActive)
                {
                    if (e.type == EventType.MouseMove)
                        sceneView.Repaint();

                    return;
                }
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            RaycastHit hit;

            if (TryGetBrushHit(ray, out hit))
            {
                lastHitPoint = hit.point;
                lastHitNormal = hit.normal;
                
                // Draw Brush Disc
                Handles.color = currentMode == ToolMode.Decor
                    ? Color.cyan
                    : currentMode == ToolMode.SplatMap ? GetSplatChannelColor(e.shift) : GetPainterBrushColor(hit, e.shift);
                Handles.DrawWireDisc(hit.point, hit.normal, brushRadius);
                if (currentMode == ToolMode.Painter)
                    DrawPainterHoverLabel(hit, e.shift);
                else if (currentMode == ToolMode.SplatMap)
                    DrawSplatHoverLabel(hit, e.shift);

                // Handle Input
                int controlID = GUIUtility.GetControlID(FocusType.Passive);

                if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
                {
                    if (currentMode == ToolMode.Painter && e.shift)
                    {
                        AddPaintTargetFromHit(hit);
                        e.Use();
                        sceneView.Repaint();
                        return;
                    }

                    isPainting = true;
                    splatUndoRegistered = false;
                    strokeMeshes.Clear();
                    hasLastLoftPaintPoint = false;
                    Undo.IncrementCurrentGroup();
                    paintUndoGroup = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName(currentMode == ToolMode.Decor
                        ? "Scatter Decor"
                        : currentMode == ToolMode.Painter ? "Paint Vertex Color" : "Paint Splat Map");
                    
                    GUIUtility.hotControl = controlID;
                    ExecuteAction(hit);
                    e.Use();
                }

                if (isPainting && e.type == EventType.MouseDrag && e.button == 0)
                {
                    ExecuteAction(hit);
                    e.Use();
                }

            }
            
            if (e.type == EventType.MouseMove) sceneView.Repaint();
        }

        private bool HandleBrushAdjustment(Event e, int controlId, SceneView sceneView)
        {
            if (e.type == EventType.MouseDown && e.button == 2 && e.control && !e.alt)
            {
                isPainting = false;
                FinishPaintUndoGroup();
                hasLastLoftPaintPoint = false;
                strokeMeshes.Clear();
                isAdjustingBrush = true;
                brushAdjustMousePosition = e.mousePosition;
                CaptureBrushAdjustmentSurface(e.mousePosition);
                GUIUtility.hotControl = controlId;
                EditorGUIUtility.SetWantsMouseJumping(1);
                e.Use();
            }
            else if (isAdjustingBrush && e.type == EventType.MouseDrag && e.button == 2)
            {
                brushRadius = Mathf.Clamp(brushRadius * Mathf.Exp(e.delta.x * 0.01f), 0.1f, 10f);
                brushStrength = Mathf.Clamp(brushStrength - e.delta.y * 0.005f, 0.01f, 1f);
                e.Use();
                Repaint();
                sceneView.Repaint();
            }
            else if (isAdjustingBrush && e.type == EventType.MouseUp && e.button == 2)
            {
                EndBrushAdjustment();
                GUIUtility.hotControl = 0;
                e.Use();
                Repaint();
                sceneView.Repaint();
                return true;
            }

            if (!isAdjustingBrush)
                return false;

            DrawBrushAdjustmentGizmo();

            Handles.BeginGUI();
            Rect panelRect = new Rect(
                brushAdjustMousePosition.x + 18f,
                brushAdjustMousePosition.y + 18f,
                250f,
                50f);
            GUI.Box(panelRect, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(
                new Rect(panelRect.x + 8f, panelRect.y + 4f, panelRect.width - 16f, 18f),
                $"Radius  {brushRadius:0.00}   (drag horizontally)",
                EditorStyles.miniBoldLabel);
            EditorGUI.ProgressBar(
                new Rect(panelRect.x + 8f, panelRect.y + 27f, panelRect.width - 16f, 16f),
                brushStrength,
                $"Strength  {brushStrength:0.00}   (drag vertically)");
            Handles.EndGUI();
            return true;
        }

        private void CaptureBrushAdjustmentSurface(Vector2 mousePosition)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            hasBrushAdjustSurface = Physics.Raycast(ray, out RaycastHit hit);
            if (!hasBrushAdjustSurface)
                return;

            brushAdjustHitPoint = hit.point;
            brushAdjustHitNormal = hit.normal;
            lastHitPoint = brushAdjustHitPoint;
            lastHitNormal = brushAdjustHitNormal;
        }

        private void DrawBrushAdjustmentGizmo()
        {
            if (!hasBrushAdjustSurface)
                return;

            Handles.color = new Color(1f, 0.82f, 0.12f, 1f);
            Handles.DrawWireDisc(brushAdjustHitPoint, brushAdjustHitNormal, brushRadius);

            Color strengthColor = Color.Lerp(
                new Color(1f, 0.25f, 0.12f, 0.9f),
                new Color(0.2f, 1f, 0.35f, 0.95f),
                brushStrength);
            Handles.color = strengthColor;
            Handles.DrawWireDisc(
                brushAdjustHitPoint
                    + brushAdjustHitNormal * HandleUtility.GetHandleSize(brushAdjustHitPoint) * 0.002f,
                brushAdjustHitNormal,
                brushRadius * brushStrength);
        }

        private void EndBrushAdjustment()
        {
            if (!isAdjustingBrush)
                return;

            isAdjustingBrush = false;
            hasBrushAdjustSurface = false;
            EditorGUIUtility.SetWantsMouseJumping(0);
            GUIUtility.hotControl = 0;
        }

        private void SetPainterBrushActive(bool active)
        {
            if (active)
            {
                ActivateSceneTool();
                GUIUtility.hotControl = 0;
                isPainting = false;
                FinishPaintUndoGroup();
                hasLastLoftPaintPoint = false;
                EndBrushAdjustment();
                splatUndoRegistered = false;
                strokeMeshes.Clear();
                painterBrushActive = true;
                InternalEditorUtility.RepaintAllViews();
                SceneView.RepaintAll();
                return;
            }

            if (!painterBrushActive)
                return;

            painterBrushActive = false;
            isPainting = false;
            FinishPaintUndoGroup();
            hasLastLoftPaintPoint = false;
            EndBrushAdjustment();
            splatUndoRegistered = false;
            strokeMeshes.Clear();
            GUIUtility.hotControl = 0;

            Repaint();
            SceneView.RepaintAll();
        }

        private void ExecuteAction(RaycastHit hit)
        {
            if (currentMode == ToolMode.Decor)
            {
                if (Event.current.shift)
                    ErasePrefabs(hit);
                else
                    PlacePrefabs(hit);
            }
            else if (currentMode == ToolMode.Painter)
            {
                PaintVertexColors(hit);
            }
            else
            {
                PaintSplatTexture(hit, Event.current.shift);
            }
        }

        private bool TryGetBrushHit(Ray ray, out RaycastHit hit)
        {
            if (currentMode != ToolMode.SplatMap)
                return Physics.Raycast(ray, out hit);

            RaycastHit[] hits = Physics.RaycastAll(ray);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                if (!CanReadSplatUv(hits[i]))
                    continue;

                hit = hits[i];
                return true;
            }

            hit = default;
            return false;
        }

        private static bool CanReadSplatUv(RaycastHit hit)
        {
            if (hit.collider is TerrainCollider)
                return true;

            if (!(hit.collider is MeshCollider meshCollider))
                return false;

            Mesh mesh = meshCollider.sharedMesh;
            return mesh != null
                && mesh.isReadable
                && mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0)
                && mesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord0) >= 2;
        }

        private void PlacePrefabs(RaycastHit hit)
        {
            if (prefabPalette.Count == 0 || prefabPalette.All(p => p == null)) return;

            if (!scatterMode)
            {
                // Single Place: Only on MouseDown
                if (Event.current.type == EventType.MouseDown)
                {
                    SpawnPrefab(hit.point, hit.normal, prefabPalette[selectedPrefabIndex]);
                }
                return;
            }

            // Scatter Mode: Drag support
            float effectiveScatterDensity = Mathf.Max(
                0.01f,
                scatterDensity * Mathf.Lerp(0.2f, 1.8f, brushStrength));
            if (Time.realtimeSinceStartup - lastScatterTime < 0.1f / effectiveScatterDensity) return;
            lastScatterTime = Time.realtimeSinceStartup;

            int count = Mathf.Max(1, (int)(brushRadius * 2f * effectiveScatterDensity));
            for (int i = 0; i < count; i++)
            {
                Vector2 randomPoint = Random.insideUnitCircle * brushRadius;
                Vector3 origin = hit.point + new Vector3(randomPoint.x, 10f, randomPoint.y);
                Ray scatterRay = new Ray(origin, Vector3.down);
                RaycastHit scatterHit;

                if (Physics.Raycast(scatterRay, out scatterHit, 20f))
                {
                    GameObject prefab = prefabPalette[Random.Range(0, prefabPalette.Count)];
                    if (prefab != null) SpawnPrefab(scatterHit.point, scatterHit.normal, prefab);
                }
            }
        }

        private void SpawnPrefab(Vector3 position, Vector3 normal, GameObject prefab)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, "Place Prefab");

            Vector3 pos = position;
            if (gridSnapping)
            {
                pos.x = Mathf.Round(pos.x / gridSize) * gridSize;
                pos.z = Mathf.Round(pos.z / gridSize) * gridSize;
            }
            instance.transform.position = pos + normal * yOffset;

            if (alignToSurface) instance.transform.up = normal;
            if (randomizeRotationY) instance.transform.Rotate(Vector3.up, Random.Range(0f, 360f), Space.Self);
            instance.transform.localScale = Vector3.one * Random.Range(scaleRange.x, scaleRange.y);
        }

        private void ErasePrefabs(RaycastHit hit)
        {
            Collider[] colliders = Physics.OverlapSphere(hit.point, brushRadius);
            foreach (var col in colliders)
            {
                if (col.gameObject != hit.collider.gameObject)
                {
                    Undo.DestroyObjectImmediate(col.gameObject);
                }
            }
        }

        private void PaintVertexColors(RaycastHit hit)
        {
            MeshFilter mf = GetPaintMeshFilter(hit);
            if (mf == null || mf.sharedMesh == null) return;
            if (!IsPaintTarget(mf.gameObject))
            {
                painterStatusMessage = $"'{mf.gameObject.name}' is not a Paint Target. Shift-click it or add it to the list before painting.";
                return;
            }

            MultiSplineLoft loft = ResolvePaintTargetLoft(mf);
            if (loft != null)
            {
                PaintLoftVertexColors(loft, hit);
                return;
            }

            Mesh mesh = EnsureEditableMesh(mf, "Clone Mesh for Painting", out MeshFilter editableMeshFilter);
            if (mesh == null)
                return;

            // 2. Register Undo for the mesh once per stroke
            if (!strokeMeshes.Contains(mesh))
            {
                Undo.RegisterCompleteObjectUndo(mesh, "Paint Vertex Color");
                strokeMeshes.Add(mesh);
            }

            Vector3[] vertices = mesh.vertices;
            Color[] colors = mesh.colors;
            if (colors.Length == 0)
            {
                colors = new Color[vertices.Length];
                for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            }

            Matrix4x4 localToWorld = mf.transform.localToWorldMatrix;
            bool changed = false;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldV = localToWorld.MultiplyPoint3x4(vertices[i]);
                float dist = Vector3.Distance(worldV, hit.point);

                if (dist < brushRadius)
                {
                    float falloff = useFalloff ? Mathf.Clamp01(1.0f - (dist / brushRadius)) : 1.0f;
                    float influence = brushStrength * falloff;
                    colors[i] = Color.Lerp(colors[i], paintColor, influence);
                    changed = true;
                }
            }

            if (changed)
            {
                mesh.colors = colors;
                RefreshPaintMesh(editableMeshFilter);
            }
        }

        private void PaintLoftVertexColors(MultiSplineLoft loft, RaycastHit hit)
        {
            if (hasLastLoftPaintPoint && Vector3.Distance(hit.point, lastLoftPaintPoint) < brushRadius * vertexPaintSpacing)
                return;

            VertexPaintModifier modifier = EnsureLoftVertexPaintModifier(loft);
            if (modifier == null)
                return;

            Undo.RecordObject(modifier, "Paint Loft Vertex Color");
            modifier.AddStrokeAndApply(modifier.CreateStroke(loft.GeneratedMesh, hit.point, paintColor, brushRadius, brushStrength, useFalloff));
            EditorUtility.SetDirty(modifier);
            lastLoftPaintPoint = hit.point;
            hasLastLoftPaintPoint = true;
            painterStatusMessage = $"Painting '{loft.gameObject.name}' non-destructively ({modifier.StrokeCount} recorded strokes).";
        }

        private void FinishPaintUndoGroup()
        {
            if (paintUndoGroup < 0)
                return;

            Undo.CollapseUndoOperations(paintUndoGroup);
            paintUndoGroup = -1;
        }

        private static VertexPaintModifier EnsureLoftVertexPaintModifier(MultiSplineLoft loft)
        {
            if (loft == null)
                return null;

            VertexPaintModifier modifier = loft.VertexPaintModifier;
            if (modifier == null)
                modifier = loft.GetComponent<VertexPaintModifier>();

            bool created = modifier == null;
            if (created)
                modifier = Undo.AddComponent<VertexPaintModifier>(loft.gameObject);

            bool needsLink = modifier.LinkedLoft != loft || modifier.Target != loft.GetComponent<MeshFilter>();
            if (needsLink)
            {
                Undo.RecordObject(modifier, "Link Vertex Paint Modifier To Loft");
                modifier.LinkToLoft(loft);
                EditorUtility.SetDirty(modifier);
            }

            if (loft.VertexPaintModifier != modifier)
            {
                Undo.RecordObject(loft, "Link Vertex Paint Modifier To Loft");
                loft.VertexPaintModifier = modifier;
                EditorUtility.SetDirty(loft);
            }

            if (created || needsLink)
                loft.Regenerate();

            return modifier;
        }

        private void RebuildLoftVertexPaintModifiers()
        {
            var rebuilt = new HashSet<VertexPaintModifier>();
            for (int i = 0; i < paintTargets.Count; i++)
            {
                MultiSplineLoft loft = ResolvePaintTargetLoft(paintTargets[i]);
                if (loft == null)
                    continue;

                VertexPaintModifier modifier = loft.VertexPaintModifier != null
                    ? loft.VertexPaintModifier
                    : loft.GetComponent<VertexPaintModifier>();
                if (modifier != null && rebuilt.Add(modifier))
                    modifier.Rebuild();
                else if (modifier == null)
                    loft.Regenerate();
            }
        }

        private Color GetSplatChannelColor(bool erasing)
        {
            if (erasing)
                return new Color(1f, 0.25f, 0.25f, 1f);

            return splatChannel switch
            {
                SplatChannel.Red => Color.red,
                SplatChannel.Green => Color.green,
                SplatChannel.Blue => new Color(0.2f, 0.55f, 1f, 1f),
                _ => Color.white
            };
        }

        private void DrawSplatHoverLabel(RaycastHit hit, bool erasing)
        {
            if (!CanReadSplatUv(hit))
                return;

            Handles.BeginGUI();
            Vector2 mouse = Event.current.mousePosition;
            var style = new GUIStyle(EditorStyles.helpBox);
            style.normal.textColor = GetSplatChannelColor(erasing);
            string action = erasing ? "Erase" : "Paint";
            GUI.Label(
                new Rect(mouse.x + 18f, mouse.y + 18f, 280f, 38f),
                $"{action} {splatChannel}   UV {hit.textureCoord.x:0.000}, {hit.textureCoord.y:0.000}",
                style);
            Handles.EndGUI();
        }

        private void PaintSplatTexture(RaycastHit hit, bool erase)
        {
            if (splatMapTexture == null)
            {
                splatStatusMessage = "Assign a splat-map Texture2D before painting.";
                return;
            }

            if (!splatMapTexture.isReadable)
            {
                splatStatusMessage = "The assigned texture is not readable. Click Make Readable before painting.";
                return;
            }

            if (!CanReadSplatUv(hit))
            {
                splatStatusMessage = "The hit collider does not provide readable UV0 coordinates.";
                return;
            }

            if (!splatUndoRegistered)
            {
                Undo.RegisterCompleteObjectUndo(splatMapTexture, "Paint Splat Map");
                splatUndoRegistered = true;
            }

            Vector2 uv = hit.textureCoord;
            int centerX = Mathf.Clamp(Mathf.RoundToInt(uv.x * (splatMapTexture.width - 1)), 0, splatMapTexture.width - 1);
            int centerY = Mathf.Clamp(Mathf.RoundToInt(uv.y * (splatMapTexture.height - 1)), 0, splatMapTexture.height - 1);
            int radius = Mathf.Max(1, splatBrushPixels);
            int minX = Mathf.Max(0, centerX - radius);
            int minY = Mathf.Max(0, centerY - radius);
            int maxX = Mathf.Min(splatMapTexture.width - 1, centerX + radius);
            int maxY = Mathf.Min(splatMapTexture.height - 1, centerY + radius);
            int width = maxX - minX + 1;
            int height = maxY - minY + 1;
            Color[] pixels = splatMapTexture.GetPixels(minX, minY, width, height);
            float targetWeight = erase ? 0f : splatPaintWeight;
            int channel = (int)splatChannel;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float distance = Vector2.Distance(new Vector2(minX + x, minY + y), new Vector2(centerX, centerY));
                    if (distance > radius)
                        continue;

                    float falloff = splatUseFalloff ? Mathf.Clamp01(1f - distance / radius) : 1f;
                    float influence = Mathf.Clamp01(brushStrength * falloff);
                    int pixelIndex = y * width + x;
                    Color color = pixels[pixelIndex];
                    float selectedWeight = Mathf.Lerp(GetColorChannel(color, channel), targetWeight, influence);
                    SetColorChannel(ref color, channel, selectedWeight);
                    if (normalizeSplatWeights)
                        NormalizeSplatColor(ref color, channel);
                    pixels[pixelIndex] = color;
                }
            }

            splatMapTexture.SetPixels(minX, minY, width, height, pixels);
            splatMapTexture.Apply(false, false);
            EditorUtility.SetDirty(splatMapTexture);
            splatTextureDirty = true;
            SceneView.RepaintAll();
        }

        private static float GetColorChannel(Color color, int channel)
        {
            return channel switch
            {
                0 => color.r,
                1 => color.g,
                2 => color.b,
                _ => color.a
            };
        }

        private static void SetColorChannel(ref Color color, int channel, float value)
        {
            value = Mathf.Clamp01(value);
            switch (channel)
            {
                case 0: color.r = value; break;
                case 1: color.g = value; break;
                case 2: color.b = value; break;
                default: color.a = value; break;
            }
        }

        private static void NormalizeSplatColor(ref Color color, int selectedChannel)
        {
            float selected = GetColorChannel(color, selectedChannel);
            float otherTotal = 0f;
            for (int channel = 0; channel < 4; channel++)
            {
                if (channel != selectedChannel)
                    otherTotal += GetColorChannel(color, channel);
            }

            float remaining = Mathf.Max(0f, 1f - selected);
            for (int channel = 0; channel < 4; channel++)
            {
                if (channel == selectedChannel)
                    continue;
                float normalized = otherTotal > 0.00001f
                    ? GetColorChannel(color, channel) / otherTotal * remaining
                    : remaining / 3f;
                SetColorChannel(ref color, channel, normalized);
            }
        }

        private void MakeSplatTextureReadable()
        {
            if (splatMapTexture == null)
                return;

            string path = AssetDatabase.GetAssetPath(splatMapTexture);
            if (string.IsNullOrEmpty(path) || !(AssetImporter.GetAtPath(path) is TextureImporter importer))
            {
                splatStatusMessage = splatMapTexture.isReadable
                    ? "Texture is readable."
                    : "This texture has no editable TextureImporter. Save it as a PNG asset first.";
                return;
            }

            importer.isReadable = true;
            importer.sRGBTexture = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            splatMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            splatStatusMessage = "Texture is readable and configured as linear splat-weight data.";
        }

        private void CreateSplatTexture()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Splat Map",
                "SplatMap",
                "png",
                "Choose where to create the RGBA splat-weight texture.");
            if (string.IsNullOrEmpty(path))
                return;

            int resolution = Mathf.Clamp(newSplatResolution, 16, 8192);
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true);
            var pixels = new Color[resolution * resolution];
            for (int index = 0; index < pixels.Length; index++)
                pixels[index] = new Color(1f, 0f, 0f, 0f);
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            File.WriteAllBytes(Path.GetFullPath(path), texture.EncodeToPNG());
            DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            splatMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            MakeSplatTextureReadable();
            splatTextureDirty = false;
            splatStatusMessage = $"Created {resolution} x {resolution} splat map. Red is the initial full-weight layer.";
        }

        private void SaveSplatTexture(bool saveAs)
        {
            if (splatMapTexture == null)
                return;

            string path = saveAs ? string.Empty : AssetDatabase.GetAssetPath(splatMapTexture);
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (saveAs || (extension != ".png" && extension != ".tga" && extension != ".asset"))
            {
                path = EditorUtility.SaveFilePanelInProject(
                    "Save Splat Map",
                    splatMapTexture.name + "_Painted",
                    "png",
                    "Save the painted splat map as a PNG asset.");
                extension = ".png";
            }
            if (string.IsNullOrEmpty(path))
                return;

            if (extension == ".asset")
            {
                EditorUtility.SetDirty(splatMapTexture);
                AssetDatabase.SaveAssets();
            }
            else
            {
                if (!AssetDatabase.MakeEditable(path))
                {
                    splatStatusMessage = "The texture asset is read-only. Check it out from version control or use Save As PNG.";
                    return;
                }

                byte[] bytes = extension == ".tga" ? splatMapTexture.EncodeToTGA() : splatMapTexture.EncodeToPNG();
                File.WriteAllBytes(Path.GetFullPath(path), bytes);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                splatMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                MakeSplatTextureReadable();
            }

            splatTextureDirty = false;
            splatStatusMessage = $"Saved splat map to {path}.";
        }

        private void SimulatePhysics()
        {
            GameObject[] selected = Selection.gameObjects;
            foreach (var go in selected)
            {
                if (go.GetComponent<Rigidbody>() == null)
                {
                    var rb = Undo.AddComponent<Rigidbody>(go);
                    rb.mass = 1f;
                }
            }
            Debug.Log("Simulate & Settle: Rigidbodies added. Use Play Mode to settle.");
        }

        private void CleanupPreview()
        {
            if (previewObject != null) DestroyImmediate(previewObject);
        }
    }
}
