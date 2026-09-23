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
        [SerializeField] bool draw, insert;
        [SerializeField] float placementHeight;
        int selectedKnot = -1;
        Vector2 scroll;
        string message;
        UnityEditor.Editor inspector;

        [MenuItem("MashBox/Map Tools/Road Tool")]
        public static void Open() => GetWindow<MGRoadTool>("Road Tool");
        public static void Open(MGRoadNetwork value, MGRoad selected = null)
        {
            var window = GetWindow<MGRoadTool>("Road Tool");
            window.network = value; window.road = selected; window.draw = false;
        }
        void OnEnable() { minSize = new Vector2(350, 440); SceneView.duringSceneGui += SceneGUI; }
        void OnDisable() { SceneView.duringSceneGui -= SceneGUI; if (inspector != null) DestroyImmediate(inspector); }
        void OnSelectionChange()
        {
            var go = Selection.activeGameObject;
            if (go != null)
            {
                var selected = go.GetComponent<MGRoad>();
                if (selected != null) { road = selected; network = selected.Network; selectedKnot = -1; }
                else if (go.GetComponent<MGRoadNetwork>() != null) network = go.GetComponent<MGRoadNetwork>();
            }
            Repaint();
        }
        void OnGUI()
        {
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                network = (MGRoadNetwork)EditorGUILayout.ObjectField("Road Network", network, typeof(MGRoadNetwork), true);
                if (GUILayout.Button("Create Road Network")) network = CreateNetwork();
                if (network == null) { EditorGUILayout.HelpBox("Create a road network, assign its Terrain World, then create roads and click in the Scene view.", MessageType.Info); return; }
                tab = GUILayout.Toolbar(tab, new[] { "Roads", "Network", "Terrain" });
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
                        Field(so, "terrainWorld"); Field(so, "terrain");
                        TerrainHelp();
                    }
                    if (so.ApplyModifiedProperties()) network.Rebuild();
                    if (tab == 2 && GUILayout.Button("Apply Terrain to Network Roads")) Apply(network.Roads);
                }
                if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
                EditorGUILayout.EndScrollView();
            }
        }
        void DrawRoads()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("New Road")) { road = CreateRoad(network, false); draw = true; insert = false; }
                if (GUILayout.Button("New Loop")) { road = CreateRoad(network, true); draw = true; insert = false; }
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
                draw = GUILayout.Toggle(draw, "Edit in Scene", "Button");
                insert = GUILayout.Toggle(insert, "Insert", "Button");
                if (GUILayout.Button("Frame")) { Selection.activeGameObject = road.gameObject; SceneView.lastActiveSceneView?.FrameSelected(); }
            }
            placementHeight = EditorGUILayout.FloatField("Fallback Plane Height", placementHeight);
            EditorGUILayout.HelpBox("Click to append points; Insert clicks the nearest curve. Drag point handles to move them. Delete removes the selected point. Escape stops drawing; Alt navigates. Standard Unity spline tools can edit tangents.", MessageType.Info);
            UnityEditor.Editor.CreateCachedEditor(road, typeof(MGRoadEditor), ref inspector);
            inspector.OnInspectorGUI();
        }
        internal static void Field(SerializedObject so, string name) => EditorGUILayout.PropertyField(so.FindProperty(name), true);
        internal static void TerrainHelp() => EditorGUILayout.HelpBox("Road Follows Terrain updates the generated surface when the road moves. Terrain Follows Road uses Apply and supports Undo. Undo a previous Apply before moving a road if you want to remove its old terrain imprint. Only tiles belonging to the assigned MG Terrain World are edited. Overlapping roads apply in hierarchy order.", MessageType.Info);
        void Apply(MGRoad[] roads)
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
            if (!draw || road == null || Application.isPlaying || PrefabStageUtility.GetPrefabStage(road.gameObject) != null) return;
            var spline = road.Container.Spline;
            var ev = Event.current;
            if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape) { draw = false; ev.Use(); Repaint(); return; }
            if (ev.type == EventType.Layout && !ev.alt) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            Handles.color = new Color(.1f, .85f, 1);
            for (int i = 0; i < spline.Count; i++)
            {
                Vector3 p = road.transform.TransformPoint((Vector3)spline[i].Position);
                float size = HandleUtility.GetHandleSize(p) * .07f;
                if (Handles.Button(p, Quaternion.identity, size, size, Handles.DotHandleCap)) selectedKnot = i;
                Handles.Label(p + Vector3.up * size * 2, i.ToString());
                if (selectedKnot != i) continue;
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.PositionHandle(p, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(road.Container, "Move Road Point");
                    var knot = spline[i]; knot.Position = (float3)road.transform.InverseTransformPoint(moved); spline[i] = knot;
                    Dirty();
                }
            }
            if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Delete && selectedKnot >= 0 && selectedKnot < spline.Count)
            {
                Undo.RecordObject(road.Container, "Remove Road Point"); spline.RemoveAt(selectedKnot); selectedKnot = -1; Dirty(); ev.Use();
            }
            if (ev.type != EventType.MouseDown || ev.button != 0 || ev.alt || GUIUtility.hotControl != 0) return;
            var ray = HandleUtility.GUIPointToWorldRay(ev.mousePosition);
            if (insert && spline.Count >= 2)
            {
                float best = float.PositiveInfinity, bestT = 0;
                int samples = Mathf.Clamp(spline.Count * 64, 128, 8192);
                for (int i = 0; i <= samples; i++)
                {
                    float t = i / (float)samples;
                    Vector3 p = road.Container.EvaluatePosition(t);
                    if (Vector3.Dot(p - ray.origin, ray.direction) <= 0) continue;
                    float d = Vector2.Distance(HandleUtility.WorldToGUIPoint(p), ev.mousePosition);
                    if (d < best) { best = d; bestT = t; }
                }
                if (best > 30) return;
                int curve = SplineUtility.SplineToCurveT(spline, bestT, out float curveT);
                if (curveT < .001f || curveT > .999f) return;
                Undo.RecordObject(road.Container, "Insert Road Point");
                InsertPoint(spline, curve, curveT);
                selectedKnot = curve + 1;
            }
            else
            {
                Vector3? point = null;
                float nearest = float.PositiveInfinity;
                foreach (var hit in Physics.RaycastAll(ray, 100000, ~0, QueryTriggerInteraction.Ignore))
                    if (hit.collider.GetComponentInParent<MGRoad>() == null && hit.distance < nearest) { nearest = hit.distance; point = hit.point; }
                if (!point.HasValue && new Plane(Vector3.up, new Vector3(0, placementHeight, 0)).Raycast(ray, out float distance)) point = ray.GetPoint(distance);
                if (!point.HasValue) return;
                Undo.RecordObject(road.Container, "Add Road Point");
                spline.Add(new BezierKnot((float3)road.transform.InverseTransformPoint(point.Value)), TangentMode.AutoSmooth);
                selectedKnot = spline.Count - 1;
            }
            Dirty(); ev.Use();
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
            CurveUtility.Split(spline.GetCurve(curve), t, out var left, out var right);
            int next = (curve + 1) % spline.Count;
            var a = spline[curve]; var b = spline[next];
            spline.SetTangentMode(curve, TangentMode.Broken);
            spline.SetTangentMode(next, TangentMode.Broken);
            a.TangentOut = math.rotate(math.inverse(a.Rotation), left.P1 - left.P0);
            b.TangentIn = math.rotate(math.inverse(b.Rotation), right.P2 - right.P3);
            spline[curve] = a; spline[next] = b;
            spline.Insert(curve + 1, new BezierKnot(left.P3, left.P2 - left.P3, right.P1 - right.P0, quaternion.identity), TangentMode.Broken);
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
                tab = GUILayout.Toolbar(tab, new[] { "Shape", "Surface / UV", "Terrain" });
                serializedObject.Update();
                string[] fields = tab == 0 ? new[] { "width", "shoulderWidth", "shoulderDrop", "crown", "bankAngle", "sampleSpacing", "generateCollider" }
                    : tab == 1 ? new[] { "roadMaterial", "shoulderMaterial", "uvMetresAlong", "uvMetresAcross", "uvOffset", "swapUV" }
                    : new[] { "overrideTerrain" };
                foreach (var field in fields) MGRoadTool.Field(serializedObject, field);
                if (tab == 2)
                {
                    using (new EditorGUI.DisabledScope(!serializedObject.FindProperty("overrideTerrain").boolValue && road.Network != null)) MGRoadTool.Field(serializedObject, "terrain");
                    EditorGUILayout.LabelField("Effective Mode", road.TerrainSettings.mode.ToString());
                    MGRoadTool.TerrainHelp();
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
                tab = GUILayout.Toolbar(tab, new[] { "Defaults", "Terrain", "Roads" });
                serializedObject.Update();
                if (tab == 0) foreach (var field in new[] { "defaultWidth", "defaultRoadMaterial", "defaultShoulderMaterial" }) MGRoadTool.Field(serializedObject, field);
                if (tab == 1) { MGRoadTool.Field(serializedObject, "terrainWorld"); MGRoadTool.Field(serializedObject, "terrain"); MGRoadTool.TerrainHelp(); }
                if (serializedObject.ApplyModifiedProperties()) network.Rebuild();
                if (tab == 2) foreach (var road in network.Roads) if (GUILayout.Button(road.name)) Selection.activeGameObject = road.gameObject;
                if (GUILayout.Button("Rebuild Network")) network.Rebuild();
                if (tab == 1 && GUILayout.Button("Apply Terrain to Network Roads"))
                {
                    try { message = MGRoadTerrain.Apply(network.Roads); }
                    catch (Exception ex) { message = ex.Message; Debug.LogException(ex); }
                }
                if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
            }
        }
    }
}
