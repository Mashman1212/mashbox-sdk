using System.Linq;
using MashBoxSDK.MapTools;
using UnityEditor.EditorTools;
using UnityEditor.Splines;
using System;
using MashBoxSDK.Maps.Roads;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;
using UnitySpline = UnityEngine.Splines.Spline;

namespace MashBoxSDK.Maps.Roads.Editor
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "MappyX.Roads.Editor", "Assembly-CSharp-Editor", null)]
    public sealed class MGRoadTool : EditorWindow
    {
        [SerializeField] MGRoadNetwork network;
        [SerializeField] MGRoad road;
        [SerializeField] int tab;
        static MGRoadTool sceneOwner;
        internal static MGRoadTool ActiveSceneTool => sceneOwner;
        internal static event Action ControlsChanged;
        internal Tool KnotTool => knotTool;
        internal bool HasNetwork => network != null;
        internal bool HasRoad => road != null;
        internal void ActivateSceneTool()
        {
            if (sceneOwner == this) return;
            sceneOwner?.SuspendEditing();
            sceneOwner = this;
            OnSelectionChange();
            OnEditingChanged();
        }
        internal void SuspendEditing() { RestoreObjectTools(); ClearPreview(); }
        internal void SetKnotTool(Tool value) { knotTool = value; ControlsChanged?.Invoke(); SceneView.RepaintAll(); }
        internal void NewRoad(bool closed)
        {
            if (network == null || Application.isPlaying) return;
            road = CreateRoad(network, closed);
            MBEditorToolState.Mode = MBEditorAuthoringMode.Road;
            MBEditorToolState.ActiveEditing = true;
        }
        internal void RebuildSelection()
        {
            if (Application.isPlaying) return;
            if (road != null) road.Rebuild(); else if (network != null) network.Rebuild();
        }
        Func<Vector3, Vector3> previewProjector;
        MGRoad previewRoad;
        [SerializeField] float placementHeight;
        int selectedKnot = -1;
        Tool knotTool = Tool.Move;
        bool hidingObjectTools, previousToolsHidden;
        Vector2 scroll;
        string message;
        UnityEditor.Editor inspector;

        [MenuItem("MashBox/Map Tools/Road Tool")]
        public static void Open()
        {
            var window = GetWindow<MGRoadTool>("Road Tool");
            window.ActivateSceneTool();
            MBEditorToolState.Mode = MBEditorAuthoringMode.Road;
            MBEditorToolState.ActiveEditing = true;
        }
        public static void Open(MGRoadNetwork value, MGRoad selected = null)
        {
            var window = GetWindow<MGRoadTool>("Road Tool");
            window.ActivateSceneTool();
            window.network = value; window.road = selected; window.selectedKnot = -1;
            Selection.activeGameObject = selected != null ? selected.gameObject : value != null ? value.gameObject : null;
            MBEditorToolState.Mode = MBEditorAuthoringMode.Road;
            MBEditorToolState.ActiveEditing = true;
        }
        void OnEnable()
        {
            minSize = new Vector2(350, 440);
            SceneView.duringSceneGui += SceneGUI;
            Selection.selectionChanged += OnSelectionChange;
            MBEditorToolState.ModeChanged += OnEditingChanged;
            MBEditorToolState.ActiveEditingChanged += OnEditingChanged;
            ActivateSceneTool();
        }
        void OnEditingChanged()
        {
            if (!MBEditorToolState.ActiveEditing || MBEditorToolState.Mode != MBEditorAuthoringMode.Road) SuspendEditing();
            else if (sceneOwner == this && ToolManager.activeContextType == typeof(SplineToolContext))
                ToolManager.SetActiveContext<GameObjectToolContext>();
            Repaint(); SceneView.RepaintAll();
        }
        void OnDisable()
        {
            SuspendEditing();
            if (sceneOwner == this) sceneOwner = null;
            SceneView.duringSceneGui -= SceneGUI;
            Selection.selectionChanged -= OnSelectionChange;
            MBEditorToolState.ModeChanged -= OnEditingChanged;
            MBEditorToolState.ActiveEditingChanged -= OnEditingChanged;
            if (inspector != null) DestroyImmediate(inspector);
        }
        void OnSelectionChange()
        {
            var go = Selection.activeGameObject;
            var selected = go != null && go.scene.IsValid() && !EditorUtility.IsPersistent(go)
                ? go.GetComponentInParent<MGRoad>() : null;
            var selectedNetwork = selected != null ? selected.Network
                : go != null && go.scene.IsValid() && !EditorUtility.IsPersistent(go) ? go.GetComponent<MGRoadNetwork>() : null;
            if (road != selected) { selectedKnot = -1; SuspendEditing(); }
            road = selected;
            network = selectedNetwork;
            ControlsChanged?.Invoke();
            Repaint();
        }
        void OnGUI() => Draw();
        internal void Draw()
        {
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                network = (MGRoadNetwork)EditorGUILayout.ObjectField("Road Network", network, typeof(MGRoadNetwork), true);
                if (GUILayout.Button("Create Road Network")) network = CreateNetwork();
                if (network == null) { EditorGUILayout.HelpBox("Create a road network, assign its Terrain World, then create roads and Shift-click in the Scene view.", MessageType.Info); return; }
                tab = GUILayout.Toolbar(tab, new[] { "Roads", "Network", "Terrain", "Details" });
                scroll = EditorGUILayout.BeginScrollView(scroll);
                if (tab == 0) DrawRoads();
                else
                {
                    var so = new SerializedObject(network); so.Update();
                    if (tab == 1)
                    {
                        Field(so, "defaultWidth"); Field(so, "defaultRoadMaterial"); Field(so, "defaultShoulderMaterial");
                        if (GUILayout.Button("Rebuild Network")) network.Rebuild();
                    }
                    else
                    {
                        Field(so, "terrainWorld"); Field(so, tab == 3 ? "details" : "terrain");
                        if (tab == 3) DetailHelp(); else TerrainHelp();
                    }
                    if (so.ApplyModifiedProperties()) network.Rebuild();
                    if (tab == 2 && GUILayout.Button("Apply Terrain to Network Roads")) Apply(network.Roads);
                    if (tab == 2) LayerButtons(network.Roads, network, ref message);
                    if (tab == 3) DetailButtons(network.Roads, ref message);
                }
                if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
                EditorGUILayout.EndScrollView();
            }
        }
        void DrawRoads()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("New Road")) { NewRoad(false); }
                if (GUILayout.Button("New Loop")) { NewRoad(true); }
            }
            foreach (var item in network.Roads)
            {
                if (item.Network != network) continue;
                if (GUILayout.Toggle(road == item, item.name, "Button") && road != item)
                { road = item; selectedKnot = -1; Selection.activeGameObject = item.gameObject; }
            }
            if (road == null || road.Network != network) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                bool editing = MBEditorToolState.ActiveEditing && MBEditorToolState.Mode == MBEditorAuthoringMode.Road;
                bool nextEditing = GUILayout.Toggle(editing, "Edit in Scene", "Button");
                if (nextEditing != editing)
                {
                    if (nextEditing) { ActivateSceneTool(); MBEditorToolState.Mode = MBEditorAuthoringMode.Road; }
                    MBEditorToolState.ActiveEditing = nextEditing;
                }

                if (GUILayout.Button("Frame")) { Selection.activeGameObject = road.gameObject; SceneView.lastActiveSceneView?.FrameSelected(); }
            }
            placementHeight = EditorGUILayout.FloatField("Fallback Plane Height", placementHeight);
            EditorGUILayout.HelpBox("Hold Shift to preview; Shift-click extends the nearest start or end. Ctrl-click inserts on the curve (Ctrl takes priority). Click points to select. W moves along the road; E rotates/banks the knot; R scales width (X), crown/shoulder height (Y), and tangent length (Z). F focuses the selected knot. Delete removes the selected point. Escape stops editing; Alt navigates. Closed loops extend from their last knot.", MessageType.Info);
            UnityEditor.Editor.CreateCachedEditor(road, typeof(MGRoadEditor), ref inspector);
            inspector.OnInspectorGUI();
        }
        internal static void Field(SerializedObject so, string name) => EditorGUILayout.PropertyField(so.FindProperty(name), true);
        internal static void TerrainHelp() => EditorGUILayout.HelpBox("Road Follows Terrain updates the generated surface when the road moves. Terrain Follows Road uses replaceable layers. Enable Auto Apply Terrain for live editing; Apply also updates a layer without accumulating imprints. Remove restores the terrain underneath. Bake keeps the result and removes every road layer in the assigned world. Baked/removed roads pause until Apply. Save the scene to persist the gameplay mesh; no runtime layer replay. Conform Width Offset adjusts each side in metres: negative pulls the complete sculpting influence inward through the padding, road edge and falloff until no terrain is affected; positive widens the road/shoulder footprint. Conform Padding Cells adds a full-strength terrain-cell margin (2 keeps the original behavior). For precise width control, set Conform Padding Cells and Minimum Falloff Cells to 0, then adjust the offset and Falloff Distance. Very narrow footprints remain limited by terrain resolution. Strength and height-mode restrictions still apply. Only tiles belonging to the assigned MG Terrain World are edited. Overlapping roads apply in hierarchy order.", MessageType.Info);
        internal static void DetailHelp() => EditorGUILayout.HelpBox("Clear painted Terrain World detail cells beneath the road and shoulders without changing the original paint. Extra Width expands the footprint; touching detail cells are also cleared to prevent grass poking through. Auto Update follows road edits in every terrain mode. Turn Clear Details off to restore; overlapping roads keep their own clearing. Applies to all painted detail layers, including palette-generated details, not manually placed objects. Save the scene to retain the final mask for gameplay.", MessageType.Info);
        internal static void DetailButtons(MGRoad[] roads, ref string result)
        {
            if (roads.Any(r => r != null && !r.detailMaskEnabled)) EditorGUILayout.HelpBox("Detail clearing is paused on some roads after Restore. Apply resumes it.", MessageType.Info);
            try
            {
                if (GUILayout.Button("Apply Detail Clearing")) result = MGRoadDetailService.Apply(roads);
                if (GUILayout.Button("Restore Details / Pause Clearing")) result = MGRoadDetailService.Remove(roads);
            }
            catch (Exception ex) { result = ex.Message; Debug.LogException(ex); }
        }
        internal static void LayerButtons(MGRoad[] roads, MGRoadNetwork network, ref string result)
        {
            if (roads.Any(r => r != null && !r.terrainLayerEnabled)) EditorGUILayout.HelpBox("Some road layers are paused after Remove/Bake. Apply resumes them.", MessageType.Info);
            try
            {
                if (GUILayout.Button("Remove Terrain Layers")) result = MGRoadLayerService.Remove(roads);
                using (new EditorGUI.DisabledScope(network == null || network.terrainWorld == null))
                    if (GUILayout.Button("Bake All Road Layers in Terrain World")) result = MGRoadLayerService.Bake(network.terrainWorld);
            }
            catch (Exception ex) { result = ex.Message; Debug.LogException(ex); }
        }        void Apply(MGRoad[] roads)
        {
            try { message = MGRoadTerrain.Apply(roads); }
            catch (Exception ex) { message = ex.Message; Debug.LogException(ex); }
        }
        internal static MGRoadNetwork CreateNetwork()
        {
            var go = new GameObject("Road Network");
            Undo.RegisterCreatedObjectUndo(go, "Create Road Network");
            var value = Undo.AddComponent<MGRoadNetwork>(go);
            Selection.activeGameObject = go;
            return value;
        }
        internal static MGRoad CreateRoad(MGRoadNetwork parent, bool closed)
        {
            var go = new GameObject(closed ? "Road Loop" : "Road");
            Undo.RegisterCreatedObjectUndo(go, "Create Road");
            Undo.SetTransformParent(go.transform, parent.transform, "Parent Road");
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            var result = Undo.AddComponent<MGRoad>(go);
            result.width = parent.defaultWidth;
            result.Container.Spline = new UnitySpline { Closed = closed };
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            return result;
        }
        void SceneGUI(SceneView view)
        {
            if (sceneOwner != this || !MBEditorToolState.ActiveEditing || MBEditorToolState.Mode != MBEditorAuthoringMode.Road
                || road == null || Application.isPlaying || PrefabStageUtility.GetPrefabStage(road.gameObject) != null)
            { RestoreObjectTools(); ClearPreview(); return; }
            if (!hidingObjectTools) { previousToolsHidden = Tools.hidden; hidingObjectTools = true; Tools.hidden = true; }
            view.wantsMouseMove = true;
            var spline = road.Container.Spline;
            var ev = Event.current;
            if (selectedKnot >= 0 && selectedKnot < spline.Count && !EditorGUIUtility.editingTextField && GUIUtility.hotControl == 0)
            {
                bool focusKey = ev.type == EventType.KeyDown && ev.keyCode == KeyCode.F
                    && !ev.alt && !ev.control && !ev.command && !ev.shift;
                bool focusCommand = (ev.type == EventType.ValidateCommand || ev.type == EventType.ExecuteCommand)
                    && ev.commandName == "FrameSelected";
                if (focusKey || focusCommand)
                {
                    if (ev.type != EventType.ValidateCommand)
                    {
                        Vector3 position = road.transform.TransformPoint((Vector3)spline[selectedKnot].Position);
                        Vector3 scale = road.transform.lossyScale;
                        float diameter = (Mathf.Max(.1f, road.width) + 2 * Mathf.Max(0, road.shoulderWidth))
                            * MGRoadKnotShape.GetScale(spline, selectedKnot).x
                            * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                        view.Frame(new Bounds(position, Vector3.one * Mathf.Max(2, diameter)), false);
                    }
                    ev.Use();
                    return;
                }
            }
            if (selectedKnot >= 0 && ev.type == EventType.KeyDown && !ev.alt && !ev.control && !ev.shift && !EditorGUIUtility.editingTextField)
            {
                if (ev.keyCode == KeyCode.W || ev.keyCode == KeyCode.E || ev.keyCode == KeyCode.R)
                {
                    SetKnotTool(ev.keyCode == KeyCode.W ? Tool.Move : ev.keyCode == KeyCode.E ? Tool.Rotate : Tool.Scale);
                    ev.Use(); view.Repaint();
                }
            }
            bool inserting = ev.control && !ev.alt;
            bool extending = ev.shift && !ev.control && !ev.alt;
            if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape)
            { MBEditorToolState.ActiveEditing = false; SuspendEditing(); ev.Use(); view.Repaint(); Repaint(); return; }
            if (!extending || previewRoad != road) ClearPreview();
            if (ev.type == EventType.MouseMove || ev.type == EventType.KeyDown || ev.type == EventType.KeyUp || ev.type == EventType.MouseLeaveWindow)
                view.Repaint();
            int placementControl = GUIUtility.GetControlID(FocusType.Passive);
            if (ev.type == EventType.Layout && (extending || inserting)) HandleUtility.AddDefaultControl(placementControl);
            var oldColor = Handles.color;
            Handles.color = new Color(.1f, .85f, 1);
            // Modifier clicks belong to placement; they must not be swallowed by knot/position handles.
            if (!extending && !inserting)
                for (int i = 0; i < spline.Count; i++)
                {
                    Vector3 p = road.transform.TransformPoint((Vector3)spline[i].Position);
                    float size = HandleUtility.GetHandleSize(p) * .07f;
                    if (Handles.Button(p, Quaternion.identity, size, size, Handles.DotHandleCap)) selectedKnot = i;
                    Handles.Label(p + Vector3.up * size * 2, i.ToString());
                    if (selectedKnot != i) continue;
                    int curveIndex = i == spline.Count - 1 && !spline.Closed ? Mathf.Max(0, i - 1) : i;
                    float curveT = i == spline.Count - 1 && !spline.Closed ? 1 : 0;
                    Quaternion frame = spline.Count > 1 ? MGRoadKnotShape.Frame(spline, curveIndex, curveT, road.transform) : road.transform.rotation;
                    Handles.Label(p, "  W Move / E Rotate / R Scale / F Focus");
                    EditorGUI.BeginChangeCheck();
                    if (knotTool == Tool.Rotate)
                    {
                        Quaternion rotated = Handles.RotationHandle(frame, p);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(road.Container, "Rotate Road Knot");
                            MGRoadKnotShape.Rotate(spline, i, rotated * Quaternion.Inverse(frame), road.transform); Dirty();
                        }
                    }
                    else if (knotTool == Tool.Scale)
                    {
                        Vector3 scaled = Handles.ScaleHandle(MGRoadKnotShape.GetScale(spline, i), p, frame, HandleUtility.GetHandleSize(p));
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(road.Container, "Scale Road Knot");
                            MGRoadKnotShape.Scale(spline, i, scaled); Dirty();
                        }
                    }
                    else
                    {
                        Vector3 moved = Handles.PositionHandle(p, frame);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(road.Container, "Move Road Point");
                            var knot = spline[i]; knot.Position = (float3)road.transform.InverseTransformPoint(moved); spline[i] = knot; Dirty();
                        }
                    }
                }
            Handles.color = oldColor;
            if (ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Delete || ev.keyCode == KeyCode.Backspace) && !EditorGUIUtility.editingTextField && GUIUtility.hotControl == 0 && !ev.alt && !ev.control && !ev.command && selectedKnot >= 0 && selectedKnot < spline.Count)
            {
                Undo.RecordObject(road.Container, "Remove Road Point"); spline.RemoveAt(selectedKnot); selectedKnot = -1; Dirty(); ev.Use();
            }
            if ((!extending && !inserting) || GUIUtility.hotControl != 0) return;
            if (EditorWindow.mouseOverWindow != view) return;
            var ray = HandleUtility.GUIPointToWorldRay(ev.mousePosition);
            bool click = ev.type == EventType.MouseDown && ev.button == 0 && HandleUtility.nearestControl == placementControl;
            if (inserting)
            {
                if (spline.Count < 2 || !FindInsertion(spline, ray, ev.mousePosition, out int curve, out float curveT)) return;
                Vector3 position = road.transform.TransformPoint((Vector3)CurveUtility.EvaluatePosition(spline.GetCurve(curve), curveT));
                if (ev.type == EventType.Repaint)
                {
                    using (new Handles.DrawingScope(Color.yellow))
                    { Handles.DrawWireDisc(position, Vector3.up, HandleUtility.GetHandleSize(position) * .08f); Handles.Label(position, "Ctrl-click: insert knot"); }
                }
                if (!click) return;
                Undo.RecordObject(road.Container, "Insert Road Point"); InsertPoint(spline, curve, curveT); selectedKnot = curve + 1;
            }
            else
            {
                if (!TryPlacement(ray, out Vector3 point)) return;
                bool prepend = ExtendFromStart(spline, road.transform, point);
                if (ev.type == EventType.Repaint) DrawExtensionPreview(spline, point, prepend);
                if (!click) return;
                Undo.RecordObject(road.Container, prepend ? "Extend Road Start" : "Extend Road End");
                selectedKnot = AddEndpoint(spline, (float3)road.transform.InverseTransformPoint(point), prepend);
            }
            ClearPreview(); Dirty(); ev.Use();
        }
        void RestoreObjectTools() { if (hidingObjectTools) { Tools.hidden = previousToolsHidden; hidingObjectTools = false; } }
        void ClearPreview() { previewProjector = null; previewRoad = null; }
        bool TryPlacement(Ray ray, out Vector3 point)
        {
            float nearest = float.PositiveInfinity; point = default;
            foreach (var hit in Physics.RaycastAll(ray, 100000, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<MGRoad>() == null && hit.distance < nearest)
                { nearest = hit.distance; point = hit.point; }
            if (!float.IsPositiveInfinity(nearest)) return true;
            if (!new Plane(Vector3.up, new Vector3(0, placementHeight, 0)).Raycast(ray, out float distance)) return false;
            point = ray.GetPoint(distance); return true;
        }
        internal static bool ExtendFromStart(UnitySpline spline, Transform transform, Vector3 point)
        {
            if (spline.Closed || spline.Count < 2) return false;
            Vector3 start = transform.TransformPoint((Vector3)spline[0].Position);
            Vector3 end = transform.TransformPoint((Vector3)spline[spline.Count - 1].Position);
            return (point - start).sqrMagnitude < (point - end).sqrMagnitude;
        }
        internal static int AddEndpoint(UnitySpline spline, float3 localPoint, bool prepend)
        {
            Vector3 inheritedScale = spline.Count > 0 ? MGRoadKnotShape.GetScale(spline, prepend ? 0 : spline.Count - 1) : Vector3.one;
            var knot = new BezierKnot(localPoint, float3.zero, float3.zero, spline.Count > 0 ? spline[prepend ? 0 : spline.Count - 1].Rotation : quaternion.identity);
            if (prepend) { spline.Insert(0, knot, TangentMode.AutoSmooth); MGRoadKnotShape.SetScale(spline, 0, inheritedScale); return 0; }
            spline.Add(knot, TangentMode.AutoSmooth); MGRoadKnotShape.SetScale(spline, spline.Count - 1, inheritedScale); return spline.Count - 1;
        }
        internal static UnitySpline ExtensionPreview(UnitySpline spline, float3 localPoint, bool prepend)
        {
            var preview = new UnitySpline(spline);
            AddEndpoint(preview, localPoint, prepend);
            return preview;
        }
        bool FindInsertion(UnitySpline spline, Ray ray, Vector2 mouse, out int curve, out float curveT)
        {
            float best = float.PositiveInfinity, bestT = 0;
            int samples = Mathf.Clamp(spline.Count * 64, 128, 8192);
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples; Vector3 p = road.Container.EvaluatePosition(t);
                if (Vector3.Dot(p - ray.origin, ray.direction) <= 0) continue;
                float d = Vector2.Distance(HandleUtility.WorldToGUIPoint(p), mouse);
                if (d < best) { best = d; bestT = t; }
            }
            curve = SplineUtility.SplineToCurveT(spline, bestT, out curveT);
            return best <= 30 && curveT > .001f && curveT < .999f;
        }
        void DrawExtensionPreview(UnitySpline source, Vector3 point, bool prepend)
        {
            if (previewRoad != road)
            {
                previewRoad = road;
                if (road.TerrainSettings.mode == RoadTerrainMode.RoadFollowsTerrain)
                    previewProjector = MGRoadTerrain.CreatePreviewProjector(road);
            }
            var preview = ExtensionPreview(source, (float3)road.transform.InverseTransformPoint(point), prepend);
            Vector3 marker = previewProjector != null ? previewProjector(point) : point;
            using (new Handles.DrawingScope(new Color(.15f, .9f, 1, .9f)))
            {
                Handles.DrawWireDisc(marker, Vector3.up, HandleUtility.GetHandleSize(marker) * .1f);
                Handles.Label(marker, source.Count == 0 ? "Shift-click: first knot" : prepend ? "Shift-click: extend START" : "Shift-click: extend END");
            }
            if (preview.Count < 2) return;
            int curveCount = preview.Closed ? preview.Count : preview.Count - 1;
            int first = prepend ? 0 : Mathf.Max(0, curveCount - 2);
            int last = prepend ? Mathf.Min(curveCount, 2) : curveCount;
            if (preview.Closed) first = 0;
            var previous = new Vector3[5]; var current = new Vector3[5];
            var triangle = new Vector3[3];
            float half = Mathf.Max(.1f, road.width) * .5f, edge = half + Mathf.Max(0, road.shoulderWidth);
            float[] offsets = { -edge, -half, 0, half, edge };
            for (int curveIndex = first; curveIndex < last; curveIndex++)
            {
                // Only the closing span and its neighbours change when extending a loop.
                if (preview.Closed && curveIndex > 0 && curveIndex < curveCount - 3) continue;
                var curve = preview.GetCurve(curveIndex);
                float length = Vector3.Distance(road.transform.TransformPoint((Vector3)curve.P0), road.transform.TransformPoint((Vector3)curve.P1))
                    + Vector3.Distance(road.transform.TransformPoint((Vector3)curve.P1), road.transform.TransformPoint((Vector3)curve.P2))
                    + Vector3.Distance(road.transform.TransformPoint((Vector3)curve.P2), road.transform.TransformPoint((Vector3)curve.P3));
                int steps = Mathf.Clamp(Mathf.CeilToInt(length / Mathf.Max(.5f, road.sampleSpacing)), 8, 256);
                for (int row = 0; row <= steps; row++)
                {
                    float t = row / (float)steps;
                    MGRoadKnotShape.Ring(road, preview, curveIndex, t, previewProjector, current);
                    if (row > 0)
                    {
                        using (new Handles.DrawingScope(new Color(.1f, .8f, 1f, .3f)))
                            for (int col = 0; col < 4; col++)
                            {
                                if ((col == 0 || col == 3) && road.shoulderWidth <= 0) continue;
                                triangle[0] = previous[col]; triangle[1] = current[col]; triangle[2] = previous[col + 1]; Handles.DrawAAConvexPolygon(triangle);
                                triangle[0] = previous[col + 1]; triangle[1] = current[col]; triangle[2] = current[col + 1]; Handles.DrawAAConvexPolygon(triangle);
                            }
                        using (new Handles.DrawingScope(new Color(.1f, .9f, 1f, .95f)))
                        { Handles.DrawLine(previous[0], current[0]); Handles.DrawLine(previous[4], current[4]); }
                    }
                    var swap = previous; previous = current; current = swap;
                }
            }
        }
        void Dirty()
        {
            EditorUtility.SetDirty(road.Container); road.RequestRebuild();
            PrefabUtility.RecordPrefabInstancePropertyModifications(road.Container);
            EditorSceneManager.MarkSceneDirty(road.gameObject.scene);
            SceneView.RepaintAll(); Repaint();
        }
        internal static void InsertPoint(UnitySpline spline, int curve, float t)
        {
            Vector3 insertedScale = MGRoadKnotShape.ScaleAt(spline, curve, t);
            Quaternion insertedRotation = Quaternion.Slerp((Quaternion)spline[curve].Rotation, (Quaternion)spline[(curve + 1) % spline.Count].Rotation, t);
            CurveUtility.Split(spline.GetCurve(curve), t, out var left, out var right);
            int next = (curve + 1) % spline.Count;
            var a = spline[curve]; var b = spline[next];
            spline.SetTangentMode(curve, TangentMode.Broken);
            spline.SetTangentMode(next, TangentMode.Broken);
            a.TangentOut = math.rotate(math.inverse(a.Rotation), left.P1 - left.P0);
            b.TangentIn = math.rotate(math.inverse(b.Rotation), right.P2 - right.P3);
            spline[curve] = a; spline[next] = b;
            spline.Insert(curve + 1, new BezierKnot(left.P3, math.rotate(math.inverse((quaternion)insertedRotation), left.P2 - left.P3), math.rotate(math.inverse((quaternion)insertedRotation), right.P1 - right.P0), (quaternion)insertedRotation), TangentMode.Broken);
            MGRoadKnotShape.SetScale(spline, curve + 1, insertedScale);
        }
    }

    [CustomEditor(typeof(MGRoad))]
    public sealed class MGRoadEditor : UnityEditor.Editor
    {
        int tab;
        string message;
        public override void OnInspectorGUI()
        {
            var road = (MGRoad)target;
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Open Road Tool")) MGRoadTool.Open(road.Network, road);
                tab = GUILayout.Toolbar(tab, new[] { "Shape", "Surface / UV", "Terrain", "Details" });
                serializedObject.Update();
                string[] fields = tab == 0 ? new[] { "width", "shoulderWidth", "shoulderDrop", "crown", "bankAngle", "sampleSpacing", "generateCollider" }
                    : tab == 1 ? new[] { "roadMaterial", "shoulderMaterial", "uvMetresAlong", "uvMetresAcross", "uvOffset", "swapUV" }
                    : tab == 2 ? new[] { "overrideTerrain" } : new[] { "overrideDetails" };
                foreach (var field in fields) MGRoadTool.Field(serializedObject, field);
                if (tab == 2)
                {
                    if (!serializedObject.FindProperty("overrideTerrain").boolValue && road.Network != null)
                    {
                        EditorGUILayout.LabelField("Inheriting network terrain settings", EditorStyles.miniLabel);
                        var inherited = new SerializedObject(road.Network);
                        using (new EditorGUI.DisabledScope(true)) MGRoadTool.Field(inherited, "terrain");
                    }
                    else MGRoadTool.Field(serializedObject, "terrain");
                    EditorGUILayout.LabelField("Effective Mode", road.TerrainSettings.mode.ToString());
                    MGRoadTool.TerrainHelp();
                }
                if (tab == 3)
                {
                    if (!serializedObject.FindProperty("overrideDetails").boolValue && road.Network != null)
                    {
                        EditorGUILayout.LabelField("Inheriting network detail settings", EditorStyles.miniLabel);
                        using (var inherited = new SerializedObject(road.Network))
                            using (new EditorGUI.DisabledScope(true)) MGRoadTool.Field(inherited, "details");
                    }
                    else MGRoadTool.Field(serializedObject, "details");
                    MGRoadTool.DetailHelp();
                }
                if (serializedObject.ApplyModifiedProperties()) road.RequestRebuild();
                if (tab == 0)
                {
                    bool closed = road.Container.Spline.Closed;
                    bool next = EditorGUILayout.Toggle("Closed Loop", closed);
                    if (next != closed) { Undo.RecordObject(road.Container, "Toggle Road Loop"); road.Container.Spline.Closed = next; EditorUtility.SetDirty(road.Container); }
                }
                if (GUILayout.Button("Rebuild Road")) road.Rebuild();
                if (tab == 2 && road.TerrainSettings.mode == RoadTerrainMode.TerrainFollowsRoad && GUILayout.Button("Apply Terrain to This Road"))
                {
                    try { message = MGRoadTerrain.Apply(new[] { road }); }
                    catch (Exception ex) { message = ex.Message; Debug.LogException(ex); }
                }
                if (tab == 2) MGRoadTool.LayerButtons(new[] { road }, road.Network, ref message);
                if (tab == 3) MGRoadTool.DetailButtons(new[] { road }, ref message);
                if (!string.IsNullOrEmpty(road.LastBuildMessage)) EditorGUILayout.HelpBox(road.LastBuildMessage, MessageType.Warning);
                if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
            }
        }
    }

    [CustomEditor(typeof(MGRoadNetwork))]
    public sealed class MGRoadNetworkEditor : UnityEditor.Editor
    {
        int tab;
        string message;
        public override void OnInspectorGUI()
        {
            var network = (MGRoadNetwork)target;
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Open Road Tool")) MGRoadTool.Open(network);
                if (GUILayout.Button("Create New Road")) MGRoadTool.Open(network, MGRoadTool.CreateRoad(network, false));
                tab = GUILayout.Toolbar(tab, new[] { "Defaults", "Terrain", "Roads", "Details" });
                serializedObject.Update();
                if (tab == 0) foreach (var field in new[] { "defaultWidth", "defaultRoadMaterial", "defaultShoulderMaterial" }) MGRoadTool.Field(serializedObject, field);
                if (tab == 1) { MGRoadTool.Field(serializedObject, "terrainWorld"); MGRoadTool.Field(serializedObject, "terrain"); MGRoadTool.TerrainHelp(); }
                if (tab == 3) { MGRoadTool.Field(serializedObject, "terrainWorld"); MGRoadTool.Field(serializedObject, "details"); MGRoadTool.DetailHelp(); }
                if (serializedObject.ApplyModifiedProperties()) network.Rebuild();
                if (tab == 2) foreach (var road in network.Roads) if (GUILayout.Button(road.name)) Selection.activeGameObject = road.gameObject;
                if (GUILayout.Button("Rebuild Network")) network.Rebuild();
                if (tab == 1 && GUILayout.Button("Apply Terrain to Network Roads"))
                {
                    try { message = MGRoadTerrain.Apply(network.Roads); }
                    catch (Exception ex) { message = ex.Message; Debug.LogException(ex); }
                }
                if (tab == 1) MGRoadTool.LayerButtons(network.Roads, network, ref message);
                if (tab == 3) MGRoadTool.DetailButtons(network.Roads, ref message);
                if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
            }
        }
    }
}
