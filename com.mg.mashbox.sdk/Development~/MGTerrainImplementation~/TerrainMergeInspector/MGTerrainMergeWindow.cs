#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    public sealed class MGTerrainMergeWindow : EditorWindow
    {
        [SerializeField] GameObject destination;
        [SerializeField] Object source;
        [SerializeField] float radius = 20, strength = .25f, hardness = .5f, opacity = .55f;
        [SerializeField] bool overlay = true;
        MGTerrainMergeSession session;
        Material material;
        bool painting, priorEditing;
        Tool priorTool;
        int control;
        Vector3 lastPoint;
        string message;
        MessageType messageType;
        Vector2 scroll;

        [MenuItem("Tools/MashBox/MG Terrain/Terrain Merge")]
        public static void Open()
        {
            var window = GetWindow<MGTerrainMergeWindow>("Terrain Merge");
            window.minSize = new Vector2(360, 460);
            var selected = Selection.activeGameObject;
            if (window.destination == null && selected != null
                && selected.GetComponentInChildren<MGTerrain>(true) != null) window.destination = selected;
            window.Show();
        }
        public static void OpenForDestination(MGTerrain terrain)
        {
            if (terrain == null) return;
            var window = GetWindow<MGTerrainMergeWindow>("Terrain Merge");
            window.ClearPreview();
            window.destination = terrain.gameObject;
            window.source = null;
            window.message = "Destination set to " + terrain.name + ". Choose a source scene, terrain tile/world, or sculpt mesh, then build the merge preview.";
            window.messageType = MessageType.Info;
            window.minSize = new Vector2(360, 460);
            window.Show();
            window.Focus();
            window.Repaint();
        }
        void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndo;
            EditorApplication.playModeStateChanged += OnPlayMode;
            wantsMouseMove = true;
        }
        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndo;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            ClearPreview();
            if (material != null) DestroyImmediate(material);
        }
        void OnPlayMode(PlayModeStateChange state) { ClearPreview(); Repaint(); }
        void OnUndo()
        {
            if (session == null) return;
            try { session.RefreshAfterUndo(); }
            catch (Exception error) { ClearPreview(); Report(error); }
            SceneView.RepaintAll(); Repaint();
        }
        void Report(Exception error)
        {
            message = error.Message; messageType = MessageType.Error; Repaint();
            Debug.LogException(error);
        }
        void ClearPreview()
        {
            StopPainting();
            session?.Dispose(); session = null;
            SceneView.RepaintAll();
        }
        void StartPainting()
        {
            if (session == null) return;
            priorTool = Tools.current; priorEditing = MBEditorToolState.ActiveEditing;
            MBEditorToolState.ActiveEditing = false;
            Tools.current = Tool.None; painting = true;
            SceneView.RepaintAll();
        }
        void StopPainting()
        {
            if (control != 0 && GUIUtility.hotControl == control) GUIUtility.hotControl = 0;
            control = 0;
            session?.EndStroke();
            if (!painting) return;
            painting = false;
            if (Tools.current == Tool.None) Tools.current = priorTool;
            if (!MBEditorToolState.ActiveEditing) MBEditorToolState.ActiveEditing = priorEditing;
            SceneView.RepaintAll();
        }
        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Merge terrain sculpting", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Choose the terrain you are keeping, then a source tile, world, saved scene, or sculpt mesh asset. Preview the differences and brush in the shapes you want.", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUI.BeginChangeCheck();
                var nextDestination = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Destination", "The tile or world you are keeping in an open scene."), destination, typeof(GameObject), true);
                var nextSource = EditorGUILayout.ObjectField(new GUIContent("Source", "Drag a terrain tile/world, Scene asset, or Mesh asset here."), source, typeof(Object), true);
                if (EditorGUI.EndChangeCheck())
                {
                    ClearPreview(); destination = nextDestination; source = nextSource; message = null;
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Selection → Destination"))
                    { ClearPreview(); destination = Selection.activeGameObject; message = null; }
                    if (GUILayout.Button("Selection → Source"))
                    { ClearPreview(); source = Selection.activeObject; message = null; }
                }
                if (source is Mesh)
                    EditorGUILayout.HelpBox("Mesh assets use the destination tile's transform. Select one destination tile with the same original local footprint.", MessageType.None);
                else EditorGUILayout.LabelField("Tiles, worlds, and scenes align by world position.", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(destination == null || source == null))
                    if (GUILayout.Button(session == null ? "Build Merge Preview" : "Rebuild Source Snapshot", GUILayout.Height(28))) BuildPreview();

                if (session != null)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField($"{session.targets.Count} destination tile(s) · {session.CoveredCount:N0} / {session.VertexCount:N0} vertices covered", EditorStyles.wordWrappedMiniLabel);
                    if (session.CoveredCount < session.VertexCount)
                        EditorGUILayout.HelpBox("Only overlapping source geometry can be brushed in. Whole-destination transfer requires full vertex coverage.", MessageType.Info);
                    overlay = EditorGUILayout.Toggle("Show difference overlay", overlay);
                    opacity = EditorGUILayout.Slider("Overlay opacity", opacity, .05f, 1);
                    EditorGUILayout.LabelField("Cyan: source is higher. Orange: source is lower. Unchanged areas are transparent.", EditorStyles.wordWrappedMiniLabel);
                    radius = Mathf.Clamp(EditorGUILayout.FloatField("Brush radius (m)", radius), .01f, 10000);
                    strength = EditorGUILayout.Slider("Brush strength", strength, .01f, 1);
                    hardness = EditorGUILayout.Slider("Brush hardness", hardness, 0, 1);
                    bool active = GUILayout.Toggle(painting, painting ? "Stop Merge Brush" : "Start Merge Brush", "Button", GUILayout.Height(30));
                    if (active != painting) { if (active) StartPainting(); else StopPainting(); }
                    EditorGUILayout.LabelField("Left-drag to merge · Alt to navigate · [ / ] radius · Esc cancels the current stroke · Ctrl/Cmd+Z to undo", EditorStyles.wordWrappedMiniLabel);
                    using (new EditorGUI.DisabledScope(session.CoveredCount != session.VertexCount))
                        if (GUILayout.Button(session.targets.Count == 1 ? "Take Whole Tile Sculpt" : "Take Whole Destination Sculpt")) TakeWhole();
                    if (GUILayout.Button("Close Preview")) ClearPreview();
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Sculpt heights only. Materials, paint, vegetation, and holes stay with the destination. Shared borders blend with neighbouring tiles to keep them joined; mixed resolutions may adjust a full edge segment. Save normally after merging and rebake distant terrain if used.", MessageType.None);
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, messageType);
            EditorGUILayout.EndScrollView();
            if (GUI.changed) SceneView.RepaintAll();
        }

        void BuildPreview()
        {
            ClearPreview();
            Scene reference = default;
            try
            {
                EditorUtility.DisplayProgressBar("Terrain Merge", "Capturing source sculpt and matching destination vertices…", .3f);
                var targets = MGTerrainMergeSession.Tiles(destination);
                var surfaces = new List<MGTerrainMergeSurface>();
                if (source is Mesh mesh)
                {
                    if (targets.Count != 1) throw new InvalidOperationException("A mesh asset source requires one destination tile.");
                    surfaces.Add(new MGTerrainMergeSurface(mesh, targets[0].MeshFilter.transform.localToWorldMatrix));
                }
                else
                {
                    List<MGTerrain> sources;
                    if (source is SceneAsset sceneAsset)
                    {
                        string path = AssetDatabase.GetAssetPath(sceneAsset);
                        if (targets.Any(t => t.gameObject.scene.path == path))
                            throw new InvalidOperationException("Choose a different source scene from the destination scene.");
                        reference = EditorSceneManager.OpenPreviewScene(path);
                        sources = reference.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MGTerrain>(true)).ToList();
                    }
                    else
                    {
                        var root = source as GameObject;
                        if (source is Component component) root = component.gameObject;
                        if (root == null) throw new InvalidOperationException("Source must be a terrain tile/world, Scene asset, or Mesh asset.");
                        sources = MGTerrainMergeSession.Tiles(root);
                    }
                    foreach (var tile in sources)
                    {
                        var owner = tile.GetComponentInParent<MGTerrainWorld>(true);
                        if (targets.Contains(tile) || (owner != null && targets.Any(t => t.GetComponentInParent<MGTerrainWorld>(true) == owner)))
                            throw new InvalidOperationException("Source and destination must be separate terrain worlds or tiles.");
                        MGTerrainTileAuthoring.Validate(tile);
                        surfaces.Add(new MGTerrainMergeSurface(tile.MeshFilter.sharedMesh, tile.MeshFilter.transform.localToWorldMatrix));
                    }
                }
                session = new MGTerrainMergeSession(targets, surfaces);
                message = "Source captured. Brushing changes only the destination; each stroke can be undone.";
                messageType = MessageType.Info;
            }
            catch (Exception error) { Report(error); }
            finally
            {
                if (reference.IsValid()) EditorSceneManager.ClosePreviewScene(reference);
                EditorUtility.ClearProgressBar(); SceneView.RepaintAll();
            }
        }
        void TakeWhole()
        {
            StopPainting();
            try
            {
                session.BeginStroke();
                int count = session.Paint(Vector3.zero, 1, 1, 1, true);
                session.EndStroke();
                message = $"Merged {count:N0} vertex heights. Save the destination scene and assets normally.";
                messageType = MessageType.Info;
            }
            catch (Exception error) { session?.EndStroke(true); Report(error); }
        }
        bool Pick(Ray ray, out Vector3 point)
        {
            point = default; float distance = float.MaxValue; bool found = false;
            foreach (var tile in session.targets)
                if (tile.terrain != null && tile.terrain.gameObject.activeInHierarchy
                    && tile.terrain.RaycastEditingSurface(ray, out var hit, distance))
                { distance = hit.distance; point = hit.point; found = true; }
            return found;
        }
        void DrawOverlay()
        {
            if (!overlay || Event.current.type != EventType.Repaint) return;
            if (material == null)
            {
                var shader = Shader.Find("Hidden/MashBox/TerrainMergeOverlay");
                if (shader == null) return;
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            material.SetFloat("_Opacity", opacity);
            if (!material.SetPass(0)) return;
            foreach (var tile in session.targets)
                if (tile.ghost != null) Graphics.DrawMeshNow(tile.ghost, tile.matrix);
        }
        void OnSceneGUI(SceneView view)
        {
            if (session == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            DrawOverlay();
            if (!painting) return;
            if (MBEditorToolState.ActiveEditing || Tools.current != Tool.None) { StopPainting(); Repaint(); return; }
            var e = Event.current;
            int id = GUIUtility.GetControlID("MGTerrainMergeBrush".GetHashCode(), FocusType.Passive);
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                session.EndStroke(true); StopPainting(); e.Use(); Repaint(); return;
            }
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.LeftBracket || e.keyCode == KeyCode.RightBracket))
            { radius = Mathf.Clamp(radius * (e.keyCode == KeyCode.LeftBracket ? .8f : 1.25f), .01f, 10000); e.Use(); Repaint(); }
            if (e.rawType == EventType.MouseUp && control != 0)
            {
                try { session.EndStroke(); } catch (Exception error) { Report(error); }
                if (GUIUtility.hotControl == control) GUIUtility.hotControl = 0;
                control = 0; e.Use(); return;
            }
            if (e.alt || e.button > 0) return;
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            bool hit = Pick(HandleUtility.GUIPointToWorldRay(e.mousePosition), out var point);
            if (hit)
            {
                using (new Handles.DrawingScope(new Color(.1f, .85f, 1)))
                {
                    Handles.DrawWireDisc(point, Vector3.up, radius);
                    Handles.DrawWireDisc(point, Vector3.up, radius * hardness);
                }
            }
            if (e.type == EventType.MouseMove) view.Repaint();
            try
            {
                if (hit && e.type == EventType.MouseDown && e.button == 0 && HandleUtility.nearestControl == id && GUIUtility.hotControl == 0)
                {
                    session.BeginStroke(); control = id; GUIUtility.hotControl = id;
                    lastPoint = point; session.Paint(point, radius, hardness, strength); e.Use(); view.Repaint();
                }
                else if (hit && e.type == EventType.MouseDrag && e.button == 0 && control != 0 && GUIUtility.hotControl == control)
                {
                    float distance = new Vector2(point.x - lastPoint.x, point.z - lastPoint.z).magnitude;
                    int steps = Mathf.Clamp(Mathf.CeilToInt(distance / Mathf.Max(.01f, radius * .2f)), 1, 128);
                    for (int i = 1; i <= steps; i++) session.Paint(Vector3.Lerp(lastPoint, point, i / (float)steps), radius, hardness, strength);
                    lastPoint = point; e.Use(); view.Repaint();
                }
            }
            catch (Exception error)
            {
                session.EndStroke(true); StopPainting(); Report(error);
            }
        }
    }
}
#endif
