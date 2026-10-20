using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor : IDisposable
    {
        internal enum ComponentMode { Object, Vertex, Edge, Face }
        internal StudioModelDocument Document { get; private set; }
        internal Scene Scene { get; private set; }
        internal Transform[] Transforms { get; private set; } = Array.Empty<Transform>();
        internal int Active { get; private set; } = -1;
        internal Mesh ActiveMesh => Document && Active >= 0 && Active < Document.nodes.Count ? Document.nodes[Active].mesh : null;
        internal readonly HashSet<int> Components = new HashSet<int>();
        private readonly HashSet<int> objects = new HashSet<int>();
        private ComponentMode mode;
        private Tool tool = Tool.Move;
        private bool pivot, local = true, xray = true, weld = true;
        private Vector2 hierarchyScroll, inspectorScroll;
        private readonly HashSet<StudioModelDocument.Node> collapsedHierarchy = new HashSet<StudioModelDocument.Node>();
        private List<int> hierarchyRows;
        private readonly HashSet<int> hierarchyParents = new HashSet<int>();
        private readonly Dictionary<StudioModelDocument.Node,(GUIContent label,GUIContent show,GUIContent hide)> hierarchyContents = new Dictionary<StudioModelDocument.Node,(GUIContent,GUIContent,GUIContent)>();
        private int[] hierarchyDepths;
        private bool hierarchyRowsSearching;
        private string search = "", error;
        private static GUIContent hierarchyHeader;
        private bool marquee, additive, subtract;
        private Vector2 start, end;
        private int gestureUndo = -1;
        private int componentRotationControl;
        private Quaternion componentHandleRotation;
        private Mesh edgeMesh;
        private List<(int a, int b)> cachedEdges;
        private readonly Action repaint;
        private StudioModelDocument.Node[] previewNodes = Array.Empty<StudioModelDocument.Node>();
        private readonly Dictionary<Mesh,int> undoMeshVersions = new Dictionary<Mesh,int>();
        internal StudioModelEditor(Action repaint)
        {
            this.repaint = repaint; Scene = EditorSceneManager.NewPreviewScene();
            Undo.undoRedoEvent += OnUndoEvent;
            EditorApplication.update += Tick;
        }
        internal void Load(StudioModelDocument document)
        {
            FinishChannelEdit();
            CancelHierarchyDrag();
            collapsedHierarchy.Clear();
            EndPivotSnap();
            if (componentPivot) componentPivot.custom = false;
            ClearGridSnap();
            CancelMarkingMenu();
            StopCutTool(); Document = document; Active = -1; objects.Clear(); Components.Clear(); Rebuild();
        }
        private void Rebuild()
        {
            hierarchyRenameNode = null;
            hierarchyRows = null; hierarchyContents.Clear(); hierarchyParents.Clear();
            var currentNodes = Document ? new HashSet<StudioModelDocument.Node>(Document.nodes) : new HashSet<StudioModelDocument.Node>();
            collapsedHierarchy.RemoveWhere(node => !currentNodes.Contains(node));
            componentFrameValid = false;
            ResetHandleExtrusion();
            scaleDrag = null;
            moveDragControl = 0;
            gestureUndo = -1;
            componentRotationControl = 0;
            ClearDisplayMaterials();
            previewLighting?.Dispose(); previewLighting = null;
            ClearSelectionWireframes();
            pivotTargetCache.Clear(); pivotHover = null;
            statisticsCache.Clear();
            foreach (var go in Scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(go);
            Transforms = Document ? Document.Build(Scene) : Array.Empty<Transform>();
            previewNodes = Document ? Document.nodes.ToArray() : Array.Empty<StudioModelDocument.Node>();
            edgeMesh = null; cachedEdges = null; cutTopology = null; cutMesh = null;
            faceNode = null; cachedFaces = null;
            previewLighting = new StudioPreviewLighting(Scene);
            previewLighting.Apply(LightingSettings);
            ApplyDisplayMaterials();
            if (Active >= Transforms.Length) Active = -1;
            objects.RemoveWhere(i => i < 0 || i >= Transforms.Length);
            Components.RemoveWhere(i => i < 0 || i >= ComponentCount());
            CaptureUndoMeshVersions();
            repaint();
        }
        private void CaptureUndoMeshVersions()
        {
            undoMeshVersions.Clear();
            if(Document)foreach(var node in Document.nodes)
                if(node.mesh)undoMeshVersions[node.mesh]=EditorUtility.GetDirtyCount(node.mesh);
        }
        private void OnUndoEvent(in UndoRedoInfo info)
        {
            bool transformsOnly=info.undoName=="Transform model objects" || info.undoName=="Scale model objects" || info.undoName=="Edit object channels";
            if(transformsOnly && RestoreObjectTransforms())return;
            OnUndo();
        }
        private bool RestoreObjectTransforms()
        {
            if(!Document || Document.nodes.Count!=Transforms.Length || previewNodes.Length!=Transforms.Length)return false;
            // A channel edit can also move a pivot and modify its mesh. Only reuse
            // render caches when geometry and the preview hierarchy are unchanged.
            for(int i=0;i<Transforms.Length;i++)
            {
                var node=Document.nodes[i];
                if(!ReferenceEquals(node,previewNodes[i]) || !Transforms[i])return false;
                if(node.parent>=Transforms.Length || node.parent < -1)return false;
                var parent=node.parent>=0?Transforms[node.parent]:null;
                if(Transforms[i].parent!=parent)return false;
                if(node.mesh && (!undoMeshVersions.TryGetValue(node.mesh,out int version) || version!=EditorUtility.GetDirtyCount(node.mesh)))return false;
            }
            channelUndoGroup=-1;resetChannelFocus=true;
            ResetCutGesture();ResetHandleExtrusion();scaleDrag=null;
            moveDragControl=0;gestureUndo=-1;componentRotationControl=0;
            for(int i=0;i<Transforms.Length;i++)
            {
                Document.ApplyTransform(i,Transforms[i]);
                Transforms[i].gameObject.SetActive(Document.nodes[i].active);
            }
            Changed(false);
            return true;
        }
        private void OnUndo()
        {
            if (!Document) return;
            channelUndoGroup = -1; resetChannelFocus = true;
            ResetCutGesture();
            foreach (var mesh in Document.nodes.Select(node => node.mesh).Where(mesh => mesh).Distinct())
            {
                if (!mesh.isReadable) continue;
                // Undo restores the CPU mesh used by picking and wireframes, but a renderer
                // can retain its previous vertex buffer. Invalidate and upload the restored
                // data before rebuilding renderers; keep authored normals/UVs and readability.
                mesh.vertices = mesh.vertices;
                mesh.UploadMeshData(false);
            }
            Rebuild(); Changed();
        }
        private void Tick()
        {
            if (pivotSnapHeld && (!vertexSnapView || EditorWindow.focusedWindow != vertexSnapView))
            {
                if (pivotSnapUndo >= 0) Undo.CollapseUndoOperations(pivotSnapUndo);
                EndPivotSnap(); repaint();
            }
            if (gridSnapHeld && (!gridSnapView || EditorWindow.focusedWindow != gridSnapView)) ClearGridSnap();
            if (markingOpen && (!markingView || EditorWindow.focusedWindow != markingView)) CancelMarkingMenu();
            TickMarkingMenu();
        }
        internal void Changed(bool geometryChanged = true) { componentFrameValid = false; if (geometryChanged) { ClearSelectionWireframes(); statisticsCache.Clear(); CaptureUndoMeshVersions(); } pivotTargetCache.Clear(); pivotHover = null; if (Document) EditorUtility.SetDirty(Document); repaint(); }
        internal void MeshTopologyChanged() { if (componentPivot) componentPivot.custom = false; Components.Clear(); Rebuild(); Changed(); }
        internal void Save() { if (Document) Document.Save(); }
        public void Dispose()
        {
            FinishChannelEdit();
            CancelHierarchyDrag();
            EndPivotSnap();
            if (componentPivot) UnityEngine.Object.DestroyImmediate(componentPivot);
            ClearDisplayMaterials();
            lightingWindow?.Close(); previewLighting?.Dispose(); previewLighting = null;
            ClearSelectionWireframes();
            CloseToolOptions();
            DisposeComponentDrawing();
            if(wireBatchMaterial)UnityEngine.Object.DestroyImmediate(wireBatchMaterial);
            if(componentEdgeStroke)UnityEngine.Object.DestroyImmediate(componentEdgeStroke);
            CancelMarkingMenu();
            Undo.undoRedoEvent -= OnUndoEvent; EditorApplication.update -= Tick;
            if (Scene.IsValid()) EditorSceneManager.ClosePreviewScene(Scene);
            Document = null; Transforms = Array.Empty<Transform>(); Active = -1;
        }
        private void Run(Action action)
        {
            try { action(); error = null; }
            catch (Exception e) { error = e.GetBaseException().Message; Debug.LogException(e); }
            repaint();
        }
        internal void DrawHierarchy(Action<StudioModelDocument> loaded)
        {
            CommitHierarchyRenameOnClick();
            HandleHierarchyDragCancellation();
            HandleModelDrop(null, loaded);
            HandleExtrudeShortcut();
            HandleObjectDuplicate();
            HandleObjectDelete();
            using (new EditorGUILayout.VerticalScope(StudioTheme.Styles.modelPanel))
            {
                if (hierarchyHeader == null)
                    hierarchyHeader = new GUIContent("HIERARCHY", EditorGUIUtility.IconContent("UnityEditor.SceneHierarchyWindow").image);
                GUILayout.Label(hierarchyHeader, StudioTheme.Styles.panelHeader);
                search = EditorGUILayout.TextField(search, StudioTheme.Styles.toolbarSearchField);
                hierarchyScroll = EditorGUILayout.BeginScrollView(hierarchyScroll);
                var rowStyle = StudioTheme.Styles.hierarchyRow;
                var rows = Document ? HierarchyRows() : null;
                if (Document) foreach (int i in rows)
                {
                    var n = Document.nodes[i]; if (!string.IsNullOrEmpty(search) && n.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    int depth = hierarchyDepths[i];
                    {
                        float height = rowStyle.fixedHeight > 0 ? rowStyle.fixedHeight : EditorGUIUtility.singleLineHeight;
                        var fullRow = GUILayoutUtility.GetRect(0,height,rowStyle,HierarchyRowLayout);
                        fullRow.height = Mathf.Max(fullRow.height,height);
                        fullRow.xMin += Mathf.Min(depth * 12, 96);
                        var foldoutRect = new Rect(fullRow.x,fullRow.y,14,fullRow.height);
                        if (hierarchyParents.Contains(i))
                        {
                            bool expanded = !collapsedHierarchy.Contains(n);
                            bool nextExpanded = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, false);
                            if (nextExpanded != expanded)
                            {
                                if (nextExpanded) collapsedHierarchy.Remove(n); else collapsedHierarchy.Add(n);
                                hierarchyRows = null;
                                repaint();
                            }
                        }
                        if (!hierarchyContents.TryGetValue(n,out var contents) || contents.label.text != n.name)
                        {
                            contents = (new GUIContent(n.name, !string.IsNullOrEmpty(n.anchorType) ? AnchorIcon : EditorGUIUtility.IconContent(n.mesh ? "Mesh Icon" : "Transform Icon").image),
                                new GUIContent("", "Show " + n.name), new GUIContent("", "Hide " + n.name));
                            hierarchyContents[n] = contents;
                        }
                        var content = contents.label;
                        var old = GUI.backgroundColor;
                        var row = new Rect(fullRow.x+14,fullRow.y,Mathf.Max(0,fullRow.width-38),fullRow.height);
                        HandleHierarchyContext(new Rect(row.x, row.y, row.width + 24, row.height), i);
                        DrawHierarchyDragRow(row, content, rowStyle, i);
                        GUI.backgroundColor = old;
                        int nodeIndex = i;
                        bool visible = GUI.Toggle(new Rect(row.xMax + 4, row.y, 20, row.height), n.active, n.active ? contents.hide : contents.show);

                        if (visible != n.active) Run(() => SetHierarchyVisibility(nodeIndex, visible));
                        row.xMax += 24;
                        GUI.backgroundColor = old;
                    }
                }
                else EditorGUILayout.HelpBox("Create primitives or import FBX models into this workspace. Each imported model keeps its hierarchy.", MessageType.Info);
                var rootDrop = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                HandleHierarchyDrop(rootDrop, -1);
                EditorGUILayout.EndScrollView();
            }
        }
        private static readonly GUILayoutOption[] HierarchyRowLayout = { GUILayout.ExpandWidth(true) };
        private List<int> HierarchyRows()
        {
            bool searching = !string.IsNullOrEmpty(search);
            if (hierarchyRows != null && hierarchyDepths.Length == Document.nodes.Count && hierarchyRowsSearching == searching) return hierarchyRows;
            hierarchyParents.Clear(); hierarchyDepths = new int[Document.nodes.Count];
            var children = new Dictionary<int, List<int>>();
            for (int i = 0; i < Document.nodes.Count; i++)
            {
                int parent = Document.nodes[i].parent;
                if (parent >= 0) hierarchyParents.Add(parent);
                if (!children.TryGetValue(parent, out var siblings)) children[parent] = siblings = new List<int>();
                siblings.Add(i);
            }
            var rows = new List<int>();
            void Visit(int parent, int depth)
            {
                if (!children.TryGetValue(parent, out var siblings)) return;
                foreach (int i in siblings)
                {
                    rows.Add(i);
                    hierarchyDepths[i] = depth;
                    // Search can still find objects inside collapsed branches without
                    // changing the user's saved foldout choices for this session.
                    if (searching || !collapsedHierarchy.Contains(Document.nodes[i])) Visit(i,depth + 1);
                }
            }
            Visit(-1,0); hierarchyRowsSearching = searching; hierarchyRows = rows; return rows;
        }
        internal string StatusText => string.IsNullOrEmpty(error) ? "Ready" : error;
        internal void DrawInspector()
        {
            using (new EditorGUILayout.VerticalScope(StudioTheme.Styles.modelPanel)) DrawChannelContent();
        }
        private void DrawChannelContent()
        {
            HandleChannelHistory();
            FinishInactiveChannelEdit();
            pendingChannelScrub = null;
            EditorGUIUtility.labelWidth = 115;
            GUILayout.Label("CHANNELS", StudioTheme.Styles.panelHeader);
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            if (Document && Active >= 0)
            {
                var n = Document.nodes[Active];
                using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(n.anchorType)))
                {
                    GUI.SetNextControlName("StudioObjectName" + Active);
                    EditorGUI.BeginChangeCheck();
                    string nextName = EditorGUILayout.DelayedTextField(n.name);
                    if (EditorGUI.EndChangeCheck()) Run(() => RenameObject(Active, nextName));
                }
                DrawAnchorChannels();
                EditorGUI.BeginChangeCheck();
                var p = DrawObjectChannels("Position", n.position);
                using (new EditorGUI.DisabledScope(true)) DrawChannels("World Position", Transforms[Active].position);
                EditorGUILayout.Space(4);
                Vector3 r, s;
                using (new EditorGUI.DisabledScope(pivot))
                { r = DrawObjectChannels("Rotation", n.RotationChannels); EditorGUILayout.Space(4); s = DrawObjectChannels("Scale", n.scale); }
                bool visible = EditorGUILayout.Toggle("Visibility", n.active);
                if (EditorGUI.EndChangeCheck()) Run(() => SetObjectChannels(p, r, s, visible));
            }
            else GUILayout.Label("No selection", StudioTheme.Styles.miniLabel);
            DrawMaterialChannels();
            DrawBlendShapeChannels();
            DrawExtrusionOptions();
            FinishInactiveChannelEdit();

            EditorGUILayout.EndScrollView();
        }
        private void ImportPath(string path, Action<StudioModelDocument> loaded)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!string.Equals(Path.GetExtension(path), ".fbx", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose an FBX file.");
            string relative = FileUtil.GetProjectRelativePath(path);
            if (string.IsNullOrEmpty(relative) || (!relative.StartsWith("Assets/") && !relative.StartsWith("Packages/")))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Studio Imports")) AssetDatabase.CreateFolder("Assets", "Studio Imports");
                relative = AssetDatabase.GenerateUniqueAssetPath("Assets/Studio Imports/" + Path.GetFileName(path));
                File.Copy(path, Path.GetFullPath(relative)); AssetDatabase.ImportAsset(relative, ImportAssetOptions.ForceSynchronousImport);
            }
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(relative);
            if (!source) throw new InvalidOperationException("Unity could not import this model.");
            ImportModel(source, loaded);
        }
        internal void ImportModel(GameObject source, Action<StudioModelDocument> loaded)
        {
            CancelMarkingMenu(); EnsureWorkspace(loaded);
            int root = Document.AppendModel(source);
            mode = ComponentMode.Object; pivot = false; search = "";
            Rebuild(); SelectObject(root); Changed();
        }
        internal static string ExportTo(string path, GameObject root)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditor.Formats.Fbx.Exporter.ModelExporter")).FirstOrDefault(t => t != null);
            var method = type?.GetMethod("ExportObject", new[] { typeof(string), typeof(UnityEngine.Object) });
            if (method == null) throw new InvalidOperationException("Install Unity's FBX Exporter package (com.unity.formats.fbx) to export models.");
            var result = method.Invoke(null, new object[] { path, root }) as string;
            if (string.IsNullOrEmpty(result) || !File.Exists(path)) throw new IOException("FBX exporter did not produce a file. Check the Console.");
            return result;
        }
        private void Export()
        {
            Export(false);
        }
        private void Export(bool selectionOnly)
        {
            if (!Document || (selectionOnly && !CanExportSelection)) return;
            string filename = selectionOnly && Active >= 0 ? Document.nodes[Active].name : Document.name;
            string path = EditorUtility.SaveFilePanel(selectionOnly ? "Export selection as FBX" : "Export workspace as FBX", "", filename, "fbx");
            if (string.IsNullOrEmpty(path)) return;
            ExportFile(path, selectionOnly);
            AssetDatabase.Refresh();
        }
        internal void ExportFile(string path, bool selectionOnly)
        {
            if (!Document || (selectionOnly && !CanExportSelection)) throw new InvalidOperationException("Select an object before exporting.");
            // A separate, temporary hierarchy avoids exporter changes affecting the live editor.
            var scene = EditorSceneManager.NewPreviewScene();
            var exportMeshes = new List<Mesh>();
            try
            {
                var nodes = Document.Build(scene);
                if (selectionOnly) FilterExportSelection(nodes);
                for (int i = 0; i < nodes.Length; i++)
                {
                    if (!nodes[i] || !nodes[i].GetComponent<Renderer>()) continue;
                    var source = Document.nodes[i].mesh; if (!source) continue;
                    var copy = UnityEngine.Object.Instantiate(source); exportMeshes.Add(copy);
                    PadUVChannels(copy);
                    if (Document.nodes[i].skinned) nodes[i].GetComponent<SkinnedMeshRenderer>().sharedMesh = copy;
                    else nodes[i].GetComponent<MeshFilter>().sharedMesh = copy;
                }
                // For a multi-model workspace, use the active model root as the export origin.
                var origin = Active >= 0 && Active < nodes.Length && nodes[Active] ? nodes[Active].root.position : Vector3.zero;
                ExportTo(path, ExportRoot(nodes.Where(t => t).ToArray(), scene, origin));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); foreach (var mesh in exportMeshes) UnityEngine.Object.DestroyImmediate(mesh); }
        }
        internal static GameObject ExportRoot(Transform[] nodes, Scene scene, Vector3? origin = null)
        {
            var roots = nodes.Where(t => !t.parent).ToArray();
            if (roots.Length == 0) throw new InvalidOperationException("Create or import geometry before exporting.");
            if (roots.Length == 1) { roots[0].position = Vector3.zero; return roots[0].gameObject; }
            var root = new GameObject("Model"); SceneManager.MoveGameObjectToScene(root, scene);
            foreach (var t in roots) { t.position -= origin ?? Vector3.zero; t.SetParent(root.transform, false); }
            return root;
        }
        internal static void PadUVChannels(Mesh mesh)
        {
            // FBX layers are sequential. Empty lower slots must exist to preserve UV channel numbers on reimport.
            var uv = new List<Vector2>(); int highest = -1;
            for (int i = 0; i < 8; i++) { mesh.GetUVs(i, uv); if (uv.Count > 0) highest = i; }
            for (int i = 0; i < highest; i++) { mesh.GetUVs(i, uv); if (uv.Count == 0) mesh.SetUVs(i, new Vector2[mesh.vertexCount]); }
        }
        internal void SelectObject(int index, bool add = false, bool remove = false)
        {
            FinishChannelEdit();
            if (!remove && Document && index >= 0 && index < Document.nodes.Count)
                for (int p = Document.nodes[index].parent; p >= 0; p = Document.nodes[p].parent) collapsedHierarchy.Remove(Document.nodes[p]);
                hierarchyRows = null;
            if (componentPivot) componentPivot.custom = false;
            StopCutTool();
            if (!add && !remove) objects.Clear();
            if (remove) objects.Remove(index); else objects.Add(index);
            Active = objects.Contains(index) ? index : objects.Count > 0 ? objects.Last() : -1;
            Components.Clear(); repaint();
        }
        private Bounds BoundsFor(int index)
        {
            var renderers = Transforms[index].GetComponentsInChildren<Renderer>();
            // A relocated origin must never enlarge the geometry's selection bounds.
            if (renderers.Length == 0) return new Bounds(Transforms[index].position, Vector3.one * .1f);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }
        internal void Frame(SceneView view)
        {
            if (Transforms.Length == 0) return;
            var bounds = BoundsFor(Active >= 0 ? Active : 0);
            if (mode == ComponentMode.Object)
            {
                // Frame the combined selection, independently of which object is active.
                var selected = objects.Where(i => i >= 0 && i < Transforms.Length).ToArray();
                if (selected.Length > 0)
                {
                    bounds = BoundsFor(selected[0]);
                    for (int i = 1; i < selected.Length; i++) bounds.Encapsulate(BoundsFor(selected[i]));
                }
            }
            else if (ActiveMesh && Components.Count > 0)
            {
                // Edges and faces resolve to their selected vertices in the current mode.
                var vertices = ActiveMesh.vertices; var indices = SelectedVertices();
                if (indices.Count > 0)
                {
                    bounds = new Bounds(Transforms[Active].TransformPoint(vertices[indices.First()]), Vector3.zero);
                    foreach (int i in indices) bounds.Encapsulate(Transforms[Active].TransformPoint(vertices[i]));
                    // Keep point/flat selections frameable without shifting their center.
                    bounds.size = Vector3.Max(bounds.size, Vector3.one * .05f);
                }
            }
            view.Frame(bounds, true);
            // Account for the hierarchy and inspector covering the sides of the SceneView.
            float canvasWidth = view is MashBoxStudioViewport studio ? studio.ModelCanvasRect.width : view.position.width - 680;
            view.size *= Mathf.Clamp(view.position.width / Mathf.Max(250, canvasWidth), 1, 2.5f);
        }
        private List<(int a, int b)> Edges()
        {
            if (ActiveMesh && edgeMesh == ActiveMesh && cachedEdges != null) return cachedEdges;
            var result = new List<(int, int)>(); if (!ActiveMesh) return result;
            var seen = new HashSet<(int, int)>();
            // Use the same polygon boundaries as object outlines and cut tools, even
            // before the first cut. This list also drives picking and edge transforms.
            foreach (var face in Faces())
                for (int i = 0; i < face.Vertices.Count; i++)
                {
                    int a = face.Vertices[i], b = face.Vertices[(i + 1) % face.Vertices.Count];
                    if (seen.Add((Math.Min(a, b), Math.Max(a, b)))) result.Add((a, b));
                }
            edgeMesh = ActiveMesh; cachedEdges = result; return result;
        }
        private int ComponentCount() => !ActiveMesh ? 0 : mode == ComponentMode.Vertex ? ActiveMesh.vertexCount : mode == ComponentMode.Edge ? Edges().Count : Faces().Count;
        internal HashSet<int> SelectedVertices()
        {
            var result = new HashSet<int>(); if (!ActiveMesh) return result;
            var faces = mode == ComponentMode.Face ? Faces() : null; var edges = mode == ComponentMode.Edge ? Edges() : null;
            foreach (int c in Components)
            {
                if (mode == ComponentMode.Vertex && c < ActiveMesh.vertexCount) result.Add(c);
                if (mode == ComponentMode.Edge && c < edges.Count) { result.Add(edges[c].a); result.Add(edges[c].b); }
                if (mode == ComponentMode.Face && c >= 0 && c < faces.Count) foreach (int vertex in faces[c].Vertices) result.Add(vertex);
            }
            if (weld)
            {
                var vertices = ActiveMesh.vertices;
                var positions = new HashSet<Vector3>(result.Select(i => vertices[i]));
                for (int i = 0; i < vertices.Length; i++) if (positions.Contains(vertices[i])) result.Add(i);
            }
            return result;
        }
        internal void SceneGUI(SceneView view, Action<StudioModelDocument> loaded)
        {
            var evt = Event.current;
            view.wantsMouseMove = true;
            // Clear before drawing if a tool can consume the event or return early.
            // Preserve the last target between pointer events so movement within it
            // does not request another scene render.
            if (evt.type == EventType.Repaint) hoveredComponent = -1;
            DrawSnapStatus(view);
            if (evt.type == EventType.Used) return;
            DrawSceneWireframes(view.camera);
            if (!evt.isKey && HandlePivotSnap(view)) return;
            if (markingOpen)
            {
                // The menu owns input, but the current selection and tool still render below it.
                // Repaint-only drawing prevents handles from registering or consuming gestures.
                if (evt.type == EventType.Repaint)
                {
                    DrawAnchorMarkers(view);
                    DrawComponents(view);
                    DrawHandle();
                }
                return;
            }
            if (CutToolSceneGUI(view)) return;
            if (mode != ComponentMode.Object && !CanEditComponents()) { mode = ComponentMode.Object; Components.Clear(); }
            if ((evt.type == EventType.ValidateCommand || evt.type == EventType.ExecuteCommand) && evt.commandName == "FrameSelected") { if (evt.type == EventType.ExecuteCommand) Frame(view); evt.Use(); }
            HandleModelKeyboard(view);
            int id = GUIUtility.GetControlID("StudioModelSelection".GetHashCode(), FocusType.Passive);
            // Unity's RectSelection also registers its fallback on MouseMove.
            // Keep Studio's replacement consistent across both passes, otherwise
            // EndHandles sees a different hover control and requests another render.
            if ((evt.type == EventType.Layout || evt.type == EventType.MouseMove) && !evt.alt) HandleUtility.AddDefaultControl(id);
            DrawAnchorMarkers(view);
            UpdateComponentHover(view, id);
            DrawComponents(view);
            if (evt.rawType == EventType.MouseDown && evt.button == 0 && !evt.alt && gestureUndo < 0)
            { Undo.IncrementCurrentGroup(); gestureUndo = Undo.GetCurrentGroup(); }
            DrawHandle();
            if (PivotTargetSceneGUI(view, id)) return;
            if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt && GUIUtility.hotControl == 0 && HandleUtility.nearestControl == id)
            {
                start = end = evt.mousePosition; additive = evt.shift; subtract = evt.control || evt.command;
                marquee = true; GUIUtility.hotControl = id; evt.Use();
            }
            if (marquee && evt.type == EventType.MouseDrag) { end = evt.mousePosition; evt.Use(); repaint(); }
            if (marquee && evt.type == EventType.MouseUp)
            {
                end = evt.mousePosition; Run(() => Pick(view, Rect.MinMaxRect(Mathf.Min(start.x, end.x), Mathf.Min(start.y, end.y), Mathf.Max(start.x, end.x), Mathf.Max(start.y, end.y)), (end - start).sqrMagnitude < 16));
                marquee = false; GUIUtility.hotControl = 0; evt.Use(); repaint();
            }
            if (marquee && evt.type == EventType.Repaint)
            {
                Handles.BeginGUI(); var rect = Rect.MinMaxRect(Mathf.Min(start.x, end.x), Mathf.Min(start.y, end.y), Mathf.Max(start.x, end.x), Mathf.Max(start.y, end.y));
                EditorGUI.DrawRect(rect, new Color(.15f, .85f, .75f, .15f)); GUI.Box(rect, GUIContent.none); Handles.EndGUI();
            }
            if (evt.rawType == EventType.MouseUp && gestureUndo >= 0) { Undo.CollapseUndoOperations(gestureUndo); gestureUndo = -1; }
        }
        private void DrawHandle()
        {
            if (handleExtrudeControl != 0 && GUIUtility.hotControl != handleExtrudeControl) ResetHandleExtrusion();
            if (Active >= 0 && !Transforms[Active].gameObject.activeInHierarchy) return;
            bool moveDragEvent = Event.current.type == EventType.MouseDrag;
            // Native handles consume mouse-down (including rawType). Snapshot the
            // modifier gesture before drawing, then confirm that a handle captured it.
            bool shiftHandleMouseDown = Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                Event.current.shift && !Event.current.alt && !Event.current.control && !Event.current.command;
            var movePointer = Event.current.mousePosition;
            if (!Document || Active < 0 || marquee || (tool == Tool.None && !pivot)) return;
            var t = Transforms[Active]; var orientation = pivot || local ? PivotOrientation : Quaternion.identity;
            bool capturedScale = !pivot && tool == Tool.Scale && scaleDrag != null && scaleDrag.control == GUIUtility.hotControl;
            var indices = mode == ComponentMode.Object ? null : capturedScale ? scaleDrag.componentIndices : handleExtrudeVertices ?? SelectedVertices();
            if (indices != null && indices.Count == 0) return;
            bool rotatingComponents = indices != null && !pivot && tool == Tool.Rotate;
            // Keep a component's movement axes fixed for the duration of the drag,
            // even when the edit changes the surrounding surface normals.
            if (indices != null && !pivot && tool == Tool.Move && moveDragControl != 0 && GUIUtility.hotControl == moveDragControl)
                orientation = moveDragOrientation;
            // RotationHandle returns the orientation reached since mouse-down. Mesh edits
            // do not rotate the object's transform, so retain the last handle orientation
            // to apply only the new angular increment to the already-edited vertices.
            if (rotatingComponents && componentRotationControl != 0 && GUIUtility.hotControl == componentRotationControl)
                orientation = componentHandleRotation;
            var vertices = indices != null ? capturedScale ? scaleDrag.meshVertices : ActiveMesh.vertices : null;
            Vector3 center = capturedScale ? scaleDrag.center : indices == null ? t.position : indices.Aggregate(Vector3.zero, (sum, i) => sum + t.TransformPoint(vertices[i])) / indices.Count;
            if (indices != null && HasComponentPivot) center = componentPivot.position;
            bool scaling = !pivot && tool == Tool.Scale;
            if (!scaling || (scaleDrag != null && scaleDrag.control != GUIUtility.hotControl)) scaleDrag = null;
            if (scaleDrag != null) { center = scaleDrag.center; orientation = scaleDrag.orientation; }
            EditorGUI.BeginChangeCheck();
            int previousHotControl = GUIUtility.hotControl;
            var position = center; var rotation = orientation; var scale = Vector3.one;
            // Uniform drawing-space scaling changes the entire handle, including its pick
            // regions. Convert move results back to world space before editing geometry.
            float handleSize = StudioSettings.Current.handleSize * (pivot ? 1.25f : 1f);
            using (new Handles.DrawingScope(Matrix4x4.Scale(Vector3.one * handleSize)))
            {
                var handleCenter = center / handleSize;
                if (pivot) position = DrawPivotHandle(handleCenter, orientation) * handleSize;
                else if (tool == Tool.Move) position = DrawMoveHandle(handleCenter, orientation) * handleSize;
                else if (tool == Tool.Rotate) rotation = Handles.RotationHandle(orientation, handleCenter);
                else scale = Handles.ScaleHandle(Vector3.one, handleCenter, orientation, HandleUtility.GetHandleSize(handleCenter));
            }
            bool handleChanged = EditorGUI.EndChangeCheck();
            if (previousHotControl == 0 && GUIUtility.hotControl != 0 && shiftHandleMouseDown &&
                !pivot && (mode == ComponentMode.Edge || mode == ComponentMode.Face))
            {
                handleExtrudeControl = GUIUtility.hotControl; handleExtrudePending = true;
                handleExtrudeSourceSelection = Components.ToArray();
            }
            if (scaling && GUIUtility.hotControl != 0 && scaleDrag == null)
                scaleDrag = CaptureScaleDrag(GUIUtility.hotControl, center, orientation, indices, vertices);
            componentRotationControl = rotatingComponents ? GUIUtility.hotControl : 0;
            componentHandleRotation = rotation;
            if ((pivot || tool == Tool.Move) && moveDragControl != 0 && GUIUtility.hotControl == moveDragControl && moveDragEvent)
            {
                if (pivotSnapHeld)
                {
                    // V temporarily takes priority over X; retain the last position
                    // when no eligible vertex is beneath the cursor.
                    position = TryMoveSnapTarget(SceneView.currentDrawingSceneView, movePointer, out var target)
                        ? ConstrainMove(moveDragOrigin, target, moveDragOrientation, moveDragAxes) : center;
                    handleChanged = (position - center).sqrMagnitude > 1e-12f;
                }
                else if (gridSnapHeld)
                    position = SnapConstrainedGrid(moveDragOrigin, position, moveDragOrientation, moveDragAxes, StudioSettings.Current.gridSpacing);
            }
            if (!handleChanged) return;
            if (handleExtrudeFailed) return;
            if (handleExtrudePending)
            {
                if (!moveDragEvent || (tool == Tool.Move && (position - center).sqrMagnitude < 1e-12f) ||
                    (tool == Tool.Rotate && Quaternion.Angle(rotation, orientation) < .0001f) ||
                    (tool == Tool.Scale && (scale - Vector3.one).sqrMagnitude < 1e-12f)) return;
                handleExtrudePending = false; handleExtrudeFailed = true;
                Run(() => { BeginHandleExtrusion(position, center, rotation, orientation, scale); handleExtrudeFailed = false; });
                return;
            }
            Run(() => {
                if (scaling) { if (scaleDrag != null) ApplyScaleDrag(scale); return; }
                if (pivot) { MoveEditingPivot(position); return; }
                else if (indices != null)
                {
                    PreserveFaceTopology();
                    Undo.RegisterCompleteObjectUndo(ActiveMesh, "Transform mesh components");
                    foreach (int i in indices)
                    {
                        var world = t.TransformPoint(vertices[i]);
                        if (tool == Tool.Move) world += position - center;
                        else if (tool == Tool.Rotate) world = center + rotation * Quaternion.Inverse(orientation) * (world - center);
                        else world = center + orientation * Vector3.Scale(Quaternion.Inverse(orientation) * (world - center), scale);
                        vertices[i] = t.InverseTransformPoint(world);
                    }
                    ActiveMesh.vertices = vertices; ActiveMesh.RecalculateNormals(); ActiveMesh.RecalculateBounds();
                    if (ActiveMesh.uv.Length == ActiveMesh.vertexCount) ActiveMesh.RecalculateTangents(); EditorUtility.SetDirty(ActiveMesh);
                    UpdateComponentPivotAfterTransform(position, center, rotation, orientation);
                }
                else
                {
                    Undo.RegisterCompleteObjectUndo(Document, "Transform model objects");
                    // Children selected with their parents inherit the change only once.
                    foreach (int i in objects.Where(i => !HasSelectedParent(i)))
                    {
                        var target = Transforms[i];
                        if (tool == Tool.Move) target.position += position - center;
                        else if (tool == Tool.Rotate) { var delta = rotation * Quaternion.Inverse(orientation); target.position = center + delta * (target.position - center); target.rotation = delta * target.rotation; }
                        else { target.position = center + orientation * Vector3.Scale(Quaternion.Inverse(orientation) * (target.position - center), scale); target.localScale = Vector3.Scale(target.localScale, scale); }
                        Document.ReadTransform(i, target);
                    }
                }
                if (indices != null && !pivot) PositionsChanged();
                else Changed(pivot);
            });
        }
        private bool HasSelectedParent(int i)
        { for (int p = Document.nodes[i].parent; p >= 0; p = Document.nodes[p].parent) if (objects.Contains(p)) return true; return false; }
        internal bool CanEditComponents()
        {
            if (HasBlendShape) return false;
            if (!ActiveMesh || !Document.nodes[Active].skinned) return true;
            var n = Document.nodes[Active]; var poses = n.mesh.bindposes;
            for (int i = 0; i < n.bones.Length && i < poses.Length; i++)
            {
                if (n.bones[i] < 0) return false;
                var m = Transforms[Active].worldToLocalMatrix * Transforms[n.bones[i]].localToWorldMatrix * poses[i];
                for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++)
                    if (Mathf.Abs(m[row, col] - (row == col ? 1 : 0)) > .001f) return false;
            }
            return true;
        }
        private void DrawComponents(SceneView view)
        {
            if (Event.current.type != EventType.Repaint || !Document) return;
            var old = Handles.color; var depth = Handles.zTest;
            Handles.zTest = displayXray ? UnityEngine.Rendering.CompareFunction.Always : UnityEngine.Rendering.CompareFunction.LessEqual;
            try
            {
                if (mode == ComponentMode.Object || Active < 0 || !ActiveMesh || !Transforms[Active].gameObject.activeInHierarchy) return;
                DrawComponentBatches(view);
            }
            finally { Handles.color = old; Handles.zTest = depth; }
        }
        private void Pick(SceneView view, Rect rect, bool click)
        {
            if (!Document) return;
            ResetCutGesture();
            if (mode == ComponentMode.Object)
            {
                if (!additive && !subtract) objects.Clear();
                int hit = -1; float nearest = float.PositiveInfinity;
                var ray = HandleUtility.GUIPointToWorldRay(end);
                for (int i = 0; i < Transforms.Length; i++)
                {
                    var mesh = Document.nodes[i].mesh; if (!mesh || !Transforms[i].gameObject.activeInHierarchy) continue;
                    if (click)
                    {
                        var v = PickVertices(i); var tr = mesh.triangles;
                        for (int j = 0; j < tr.Length; j += 3) if (RayTriangle(ray, Transforms[i].TransformPoint(v[tr[j]]), Transforms[i].TransformPoint(v[tr[j + 1]]), Transforms[i].TransformPoint(v[tr[j + 2]]), out float distance) && distance < nearest) { nearest = distance; hit = i; }
                    }
                    else if (PickVertices(i).Any(v => VisiblePoint(view, Transforms[i].TransformPoint(v), rect))) { if (subtract) objects.Remove(i); else objects.Add(i); }
                }
                int anchorHit = PickAnchors(view,rect,click);
                if (anchorHit >= 0) hit = anchorHit;
                if (hit >= 0) { if (subtract) objects.Remove(hit); else objects.Add(hit); }
                Active = hit >= 0 && objects.Contains(hit) ? hit : objects.Count > 0 ? objects.Last() : -1;
                Components.Clear(); return;
            }
            if (!ActiveMesh || !Transforms[Active].gameObject.activeInHierarchy) return;
            if (!additive && !subtract) Components.Clear();
            if (click)
            {
                int best = FindComponentAt(view, end);
                if (best >= 0) { if (subtract) Components.Remove(best); else Components.Add(best); }
                return;
            }
            var vertices = ActiveMesh.vertices; var edges = Edges(); var t = Transforms[Active];
            int count = ComponentCount();
            for (int i = 0; i < count; i++)
            {
                Vector3 world = mode == ComponentMode.Vertex ? t.TransformPoint(vertices[i]) : mode == ComponentMode.Edge ? t.TransformPoint((vertices[edges[i].a] + vertices[edges[i].b]) * .5f) : t.TransformPoint(FaceCenter(Faces()[i], vertices));
                if (view.camera.WorldToViewportPoint(world).z <= 0 || (!xray && Occluded(view, world))) continue;
                if (rect.Contains(HandleUtility.WorldToGUIPoint(world))) { if (subtract) Components.Remove(i); else Components.Add(i); }
            }
        }
        // Hover and click share the same tolerance, occlusion and polygon hit rules.
        private int FindComponentAt(SceneView view, Vector2 pointer)
        {
            if (mode == ComponentMode.Object || !ActiveMesh || !Transforms[Active].gameObject.activeInHierarchy) return -1;
            var vertices = ActiveMesh.vertices; var edges = Edges(); var t = Transforms[Active];
            int best = -1; float bestDistance = mode == ComponentMode.Face ? float.PositiveInfinity : 12;
            int count = ComponentCount(); var ray = HandleUtility.GUIPointToWorldRay(pointer);
            for (int i = 0; i < count; i++)
            {
                Vector3 world = mode == ComponentMode.Vertex ? t.TransformPoint(vertices[i]) : mode == ComponentMode.Edge ? t.TransformPoint((vertices[edges[i].a] + vertices[edges[i].b]) * .5f) : t.TransformPoint(FaceCenter(Faces()[i], vertices));
                if (view.camera.WorldToViewportPoint(world).z <= 0 || (!xray && Occluded(view, world))) continue;
                float distance = Vector2.Distance(pointer, HandleUtility.WorldToGUIPoint(world));
                if (mode == ComponentMode.Edge) distance = HandleUtility.DistancePointLine(pointer, HandleUtility.WorldToGUIPoint(t.TransformPoint(vertices[edges[i].a])), HandleUtility.WorldToGUIPoint(t.TransformPoint(vertices[edges[i].b])));
                if (mode == ComponentMode.Face && !RayFace(ray, cachedFaceTriangles[i], vertices, t, out distance)) continue;
                if (distance < bestDistance) { bestDistance = distance; best = i; }
            }
            return best;
        }
        private void UpdateComponentHover(SceneView view, int selectionId)
        {
            var evt = Event.current;
            bool pointerChanged = evt.type == EventType.MouseMove || evt.type == EventType.MouseLeaveWindow ||
                evt.type == EventType.KeyDown || evt.type == EventType.KeyUp;
            if (evt.type != EventType.Repaint && !pointerChanged) return;
            int previous = Components.Contains(hoveredComponent) ? -1 : hoveredComponent;
            hoveredComponent = -1;
            if (evt.type != EventType.MouseLeaveWindow && mode != ComponentMode.Object && !pivot && !pivotSnapHeld &&
                !marquee && !evt.alt && GUIUtility.hotControl == 0 && HandleUtility.nearestControl == selectionId &&
                EditorWindow.mouseOverWindow == view)
            {
                var canvas = view is MashBoxStudioViewport studio ? studio.ModelCanvasRect : new Rect(Vector2.zero, view.position.size);
                if (canvas.Contains(evt.mousePosition) && !(view is MashBoxStudioViewport overlay && overlay.IsOverViewportControls(evt.mousePosition)))
                    hoveredComponent = FindComponentAt(view, evt.mousePosition);
            }
            int next = Components.Contains(hoveredComponent) ? -1 : hoveredComponent;
            if (pointerChanged && previous != next)
            {
                if (view is MashBoxStudioViewport studioView) studioView.MarkDirty(MashBoxStudioViewport.DirtyState.Overlay);
                else view.Repaint();
            }
        }
        private static bool VisiblePoint(SceneView view, Vector3 point, Rect rect) => view.camera.WorldToViewportPoint(point).z > 0 && rect.Contains(HandleUtility.WorldToGUIPoint(point));
        private Vector3[] PickVertices(int index)
        {
            if (!Document.nodes[index].skinned) return Document.nodes[index].mesh.vertices;
            var baked = new Mesh();
            try { Transforms[index].GetComponent<SkinnedMeshRenderer>().BakeMesh(baked); return baked.vertices; }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
        }
        private bool Occluded(SceneView view, Vector3 point)
        {
            var ray = HandleUtility.GUIPointToWorldRay(HandleUtility.WorldToGUIPoint(point)); float limit = Vector3.Dot(point - ray.origin, ray.direction) - .001f;
            for (int n = 0; n < Transforms.Length; n++)
            {
                var mesh = Document.nodes[n].mesh; if (!mesh || !Transforms[n].gameObject.activeInHierarchy) continue; var v = mesh.vertices; var tr = mesh.triangles; var t = Transforms[n];
                for (int i = 0; i < tr.Length; i += 3) if (RayTriangle(ray, t.TransformPoint(v[tr[i]]), t.TransformPoint(v[tr[i + 1]]), t.TransformPoint(v[tr[i + 2]]), out float d) && d < limit) return true;
            }
            return false;
        }
        internal static bool RayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0; var e1 = b - a; var e2 = c - a; var p = Vector3.Cross(ray.direction, e2); float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-8f) return false;
            float inv = 1 / det; var s = ray.origin - a; float u = Vector3.Dot(s, p) * inv; if (u < 0 || u > 1) return false;
            var q = Vector3.Cross(s, e1); float v = Vector3.Dot(ray.direction, q) * inv; if (v < 0 || u + v > 1) return false;
            distance = Vector3.Dot(e2, q) * inv; return distance > 0;
        }
    }
}
