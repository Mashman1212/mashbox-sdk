using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace MashBoxSDK.MapTools
{
    public class MGBrushWindow : EditorWindow
    {
        private enum ToolMode { Decor, Painter }
        private ToolMode currentMode = ToolMode.Decor;

        // --- Common Settings ---
        private float brushRadius = 2.0f;
        private float brushStrength = 0.5f;

        // --- Decor Settings ---
        [SerializeField] private List<GameObject> prefabPalette = new List<GameObject>();
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
        private Color paintColor = Color.white;
        private bool useFalloff = true;
        private bool painterBrushActive = true;
        [SerializeField] private List<GameObject> paintTargets = new List<GameObject>();
        private string painterStatusMessage = "Add mesh objects here before painting. Only listed targets can be cloned or modified.";

        // --- Internal State ---
        private Vector2 scrollPos;
        private GameObject previewObject;
        private Vector3 lastHitPoint;
        private Vector3 lastHitNormal;
        private bool isPainting = false;
        private float lastScatterTime = 0f;
        private HashSet<Mesh> strokeMeshes = new HashSet<Mesh>();
        private bool sceneToolActive;

        public static void ShowWindow()
        {
            GetWindow<MGBrushWindow>("MG Brush");
        }

        private void OnEnable()
        {
            ActivateSceneTool();
        }

        private void OnDisable()
        {
            DeactivateSceneTool();
        }

        public void ActivateSceneTool()
        {
            if (sceneToolActive)
                return;

            sceneToolActive = true;
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        public void DeactivateSceneTool()
        {
            if (!sceneToolActive)
                return;

            sceneToolActive = false;
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
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
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(currentMode == ToolMode.Decor, "Decor (Scatter)", "Button")) currentMode = ToolMode.Decor;
            if (GUILayout.Toggle(currentMode == ToolMode.Painter, "Painter (Vertex)", "Button")) currentMode = ToolMode.Painter;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            if (!embeddedInParentWindow)
                scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            EditorGUILayout.Space(5);
            brushRadius = EditorGUILayout.Slider("Brush Radius", brushRadius, 0.1f, 10f);
            brushStrength = EditorGUILayout.Slider("Brush Strength", brushStrength, 0.01f, 1f);

            if (currentMode == ToolMode.Decor)
            {
                DrawDecorSettings();
            }
            else
            {
                DrawPainterSettings();
            }

            if (!embeddedInParentWindow)
                EditorGUILayout.EndScrollView();
        }

        private void OnUndoRedoPerformed()
        {
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
            EditorGUILayout.PropertyField(paletteProp, true);
            so.ApplyModifiedProperties();

            if (prefabPalette.Count > 0)
            {
                selectedPrefabIndex = EditorGUILayout.IntSlider("Selected Prefab", selectedPrefabIndex, 0, prefabPalette.Count - 1);
            }

            if (GUILayout.Button("Simulate & Settle (Physics)"))
            {
                SimulatePhysics();
            }
        }

        private void DrawPainterSettings()
        {
            EditorGUILayout.LabelField("Vertex Color Settings", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            painterEditMode = (PainterEditMode)EditorGUILayout.EnumPopup("Edit Mode", painterEditMode);
            if (painterEditMode == PainterEditMode.ProxyCopy)
                hideSourceRendererForProxy = EditorGUILayout.Toggle("Hide Source Renderer", hideSourceRendererForProxy);
            if (EditorGUI.EndChangeCheck())
                ApplyProxyRendererVisibility();

            paintColor = EditorGUILayout.ColorField("Paint Color", paintColor);
            useFalloff = EditorGUILayout.Toggle("Use Falloff", useFalloff);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Brush Control", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    painterBrushActive ? "Status: Active" : "Status: Paused",
                    GUILayout.Width(100f));

                if (GUILayout.Button(
                    painterBrushActive ? "Pause Brush (W)" : "Enable Brush (B)",
                    GUILayout.Height(24f)))
                {
                    SetPainterBrushActive(!painterBrushActive);
                }
            }
            
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("UV Generation", EditorStyles.boldLabel);
            targetUVChannel = (UVChannel)EditorGUILayout.EnumPopup("Target UV Channel", targetUVChannel);

            EditorGUILayout.HelpBox("Painting modifies vertex colors. Auto UV will generate unwrapped coordinates for the selected channel.", MessageType.Info);
            EditorGUILayout.HelpBox("Scene View: B enables the paint brush. W pauses the brush so you can select and move objects.", MessageType.None);

            DrawPaintTargetSettings();

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
                        painterStatusMessage = "Paint target list cleared.";
                    }
                }
            }

            DrawPaintTargetDropZone();

            SerializedObject so = new SerializedObject(this);
            SerializedProperty targetsProperty = so.FindProperty("paintTargets");
            EditorGUILayout.PropertyField(targetsProperty, true);
            so.ApplyModifiedProperties();
            CleanPaintTargets();
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
            AddPaintTargets(Selection.objects);
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
        }

        private int GetValidPaintTargetCount()
        {
            int count = 0;
            for (int i = 0; i < paintTargets.Count; i++)
            {
                if (paintTargets[i] && HasPaintableMesh(paintTargets[i]))
                    count++;
            }

            return count;
        }

        private static bool HasPaintableMesh(GameObject gameObject)
        {
            return gameObject && gameObject.GetComponentInChildren<MeshFilter>() != null;
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
            
            // Handle Hotkeys
            if (e.type == EventType.KeyDown)
            {
                if (currentMode == ToolMode.Painter && !EditorGUIUtility.editingTextField)
                {
                    if (e.keyCode == KeyCode.B)
                    {
                        SetPainterBrushActive(true);
                        e.Use();
                        sceneView.Repaint();
                        return;
                    }

                    if (e.keyCode == KeyCode.W)
                    {
                        SetPainterBrushActive(false);
                        Tools.current = Tool.Move;
                        sceneView.Repaint();
                        return;
                    }
                }

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

            if (currentMode == ToolMode.Painter)
            {
                DrawPainterSceneModeOverlay();
                if (!painterBrushActive)
                {
                    if (e.type == EventType.MouseMove)
                        sceneView.Repaint();

                    return;
                }
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit))
            {
                lastHitPoint = hit.point;
                lastHitNormal = hit.normal;
                
                // Draw Brush Disc
                Handles.color = currentMode == ToolMode.Decor ? Color.cyan : GetPainterBrushColor(hit, e.shift);
                Handles.DrawWireDisc(hit.point, hit.normal, brushRadius);
                if (currentMode == ToolMode.Painter)
                    DrawPainterHoverLabel(hit, e.shift);

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
                    strokeMeshes.Clear();
                    Undo.IncrementCurrentGroup();
                    Undo.SetCurrentGroupName(currentMode == ToolMode.Decor ? "Scatter Decor" : "Paint Vertex Color");
                    
                    GUIUtility.hotControl = controlID;
                    ExecuteAction(hit);
                    e.Use();
                }

                if (isPainting && e.type == EventType.MouseDrag && e.button == 0)
                {
                    ExecuteAction(hit);
                    e.Use();
                }

                if (e.type == EventType.MouseUp && e.button == 0)
                {
                    if (isPainting)
                    {
                        isPainting = false;
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                }
            }
            
            if (e.type == EventType.MouseMove) sceneView.Repaint();
        }

        private void SetPainterBrushActive(bool active)
        {
            if (active)
                ActivateSceneTool();

            if (painterBrushActive == active)
                return;

            painterBrushActive = active;
            isPainting = false;
            strokeMeshes.Clear();
            if (!active)
                GUIUtility.hotControl = 0;

            painterStatusMessage = active
                ? "Brush active. Paint listed targets, or Shift-click a mesh to add it."
                : "Brush paused. Select and move objects in the Scene View. Press B to paint.";
            Repaint();
        }

        private void DrawPainterSceneModeOverlay()
        {
            Handles.BeginGUI();

            const float width = 330f;
            const float height = 48f;
            Rect rect = new Rect(12f, 12f, width, height);
            Color bg = EditorGUIUtility.isProSkin
                ? new Color(0.08f, 0.08f, 0.08f, 0.78f)
                : new Color(1f, 1f, 1f, 0.82f);
            Color accent = painterBrushActive
                ? new Color(0.25f, 1f, 0.45f, 0.95f)
                : new Color(0.2f, 0.75f, 1f, 0.95f);

            EditorGUI.DrawRect(rect, bg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accent);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = accent }
            };
            var textStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.86f, 0.86f, 0.86f, 1f)
                        : new Color(0.18f, 0.18f, 0.18f, 1f)
                }
            };

            GUI.Label(new Rect(rect.x + 12f, rect.y + 6f, rect.width - 20f, 18f),
                painterBrushActive ? "Vertex Painter: Brush" : "Vertex Painter: Select / Move", titleStyle);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 26f, rect.width - 20f, 16f),
                painterBrushActive ? "W: select/move    Shift+Click: add target" : "B: brush    W: move tool", textStyle);

            Handles.EndGUI();
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
            else
            {
                PaintVertexColors(hit);
            }
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
            if (Time.realtimeSinceStartup - lastScatterTime < 0.1f / scatterDensity) return;
            lastScatterTime = Time.realtimeSinceStartup;

            int count = Mathf.Max(1, (int)(brushRadius * 2f * scatterDensity));
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
