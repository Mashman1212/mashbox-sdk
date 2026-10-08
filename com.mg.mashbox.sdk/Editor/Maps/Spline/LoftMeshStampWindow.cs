using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.Spline;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Spline = UnityEngine.Splines.Spline;

namespace MashBoxSDK.MapTools
{
    public sealed class LoftMeshStampWindow : EditorWindow
    {
        enum HandleMode { Move, Rotate, Scale }

        [SerializeField] MultiSplineLoft loft;
        [SerializeField] Mesh mesh;
        [SerializeField] LoftMeshStamp.Settings settings = new LoftMeshStamp.Settings();
        [SerializeField] bool preview = true;
        [SerializeField] int extraCurves = 1;
        [SerializeField] int supportKnots = 128;
        [SerializeField] HandleMode handleMode;
        bool placing, positioned, dirty = true;
        string message;
        Vector2 scroll;
        List<LoftMeshStamp.Plan> plans = new List<LoftMeshStamp.Plan>();
        Vector3[] ghostLines;
        Mesh ghostMesh;
        double nextPreview;

        [MenuItem("MashBox/Splines/Loft Mesh Stamp")]
        public static void OpenSelection()
        {
            var selected = Selection.activeGameObject;
            Open(selected != null ? selected.GetComponentInParent<MultiSplineLoft>() : null);
        }

        public static void Open(MultiSplineLoft target)
        {
            var window = GetWindow<LoftMeshStampWindow>("Loft Mesh Stamp");
            window.minSize = new Vector2(340, 500);
            if (target != null) window.SetLoft(target);
            window.Show();
        }

        void SetLoft(MultiSplineLoft target)
        {
            loft = target;
            if (loft != null)
            {
                var filter = loft.GetComponent<MeshFilter>();
                settings.position = filter != null && filter.sharedMesh != null
                    ? filter.transform.TransformPoint(filter.sharedMesh.bounds.center) : loft.transform.position;
                positioned = true;
            }
            Invalidate();
        }

        void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndo;
            Spline.Changed += OnSplineChanged;
            wantsMouseMove = true;
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndo;
            Spline.Changed -= OnSplineChanged;
            SceneView.RepaintAll();
        }

        void OnUndo()
        {
            Invalidate();
        }

        void OnSplineChanged(Spline spline, int index, SplineModification change)
        {
            if (loft == null) return;
            foreach (var source in loft.Sources)
                if (source?.IsValid == true && source.container.Splines[source.splineIndex] == spline)
                { Invalidate(); return; }
        }
        void Invalidate() { dirty = true; Repaint(); SceneView.RepaintAll(); }

        void OnGUI()
        {
            HandleModeShortcut(Event.current);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("Stamp a mesh into editable loft curves. Orange shows the mesh; cyan shows the resulting spline shape. Mesh local Y is height, X/Z is its footprint.", MessageType.Info);
            EditorGUI.BeginChangeCheck();
            var target = (MultiSplineLoft)EditorGUILayout.ObjectField("Target Loft", loft, typeof(MultiSplineLoft), true);
            if (target != loft) SetLoft(target);
            if (GUILayout.Button("Use Selected Loft"))
            {
                var selected = Selection.activeGameObject;
                SetLoft(selected != null ? selected.GetComponentInParent<MultiSplineLoft>() : null);
            }
            mesh = (Mesh)EditorGUILayout.ObjectField("Stamp Mesh", mesh, typeof(Mesh), false);
            settings.mode = (LoftMeshStamp.Mode)EditorGUILayout.EnumPopup("Operation", settings.mode);
            settings.width = Positive(EditorGUILayout.FloatField("Width (m)", settings.width), 6);
            settings.length = Positive(EditorGUILayout.FloatField("Length (m)", settings.length), 10);
            settings.height = Positive(EditorGUILayout.FloatField("Height (m)", settings.height), 2);
            Vector3 angles = EditorGUILayout.Vector3Field(new GUIContent("Rotation (X/Y/Z)", "Pitch, yaw and roll in degrees."),
                new Vector3(settings.pitch, settings.yaw, settings.roll));
            settings.pitch = angles.x;
            settings.yaw = angles.y;
            settings.roll = angles.z;
            settings.position = EditorGUILayout.Vector3Field("Stamp Position", settings.position);
            settings.strength = EditorGUILayout.Slider("Strength", settings.strength, 0, 1);
            settings.blend = EditorGUILayout.Slider(new GUIContent("Edge Blend", "Fraction of the rectangular footprint used for a smooth transition."), settings.blend, 0, 1);
            settings.resolution = EditorGUILayout.IntSlider(new GUIContent("Knot Resolution", "Approximate minimum intervals across the shorter stamp dimension. Existing knots are retained; only nearby spans are subdivided."), settings.resolution, 4, 64);
            preview = EditorGUILayout.Toggle("Scene Preview", preview);
            if (EditorGUI.EndChangeCheck()) Invalidate();

            var selectedMode = (HandleMode)GUILayout.Toolbar((int)handleMode, new[] { "Move (W)", "Rotate (E)", "Scale (R)" });
            if (selectedMode != handleMode) SelectHandleMode(selectedMode);

            using (new EditorGUI.DisabledScope(loft == null || mesh == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button(placing ? "Stop Placement" : "Place on Loft", GUILayout.Height(26)))
                {
                    placing = !placing;
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button("Apply Mesh Stamp", GUILayout.Height(30))) Apply();
            }
            EditorGUILayout.HelpBox("Place on Loft: move over the loft, then click to place. W: move, E: rotate, R: scale. Rotation handles control pitch, yaw and roll; scale handles adjust width, height and length. Ctrl+wheel resizes; Shift+wheel adjusts yaw. Enter stamps; Escape stops placement. Ctrl+Z undoes a stamp. Placement uses the loft's colliders.", MessageType.None);
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("More Curves Across the Trail", EditorStyles.boldLabel);
            extraCurves = EditorGUILayout.IntSlider("Curves per Gap", extraCurves, 1, 3);
            supportKnots = EditorGUILayout.IntSlider("Knots per New Curve", supportKnots, 16, 512);
            EditorGUILayout.HelpBox("For detail between existing rails, add intermediate curves first. This samples the full trail and can change its surface between the original curves. Review before stamping; adding curves has its own Undo. Only normalized cross-section alignment is supported.", MessageType.None);
            using (new EditorGUI.DisabledScope(loft == null || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Add Intermediate Curves")) AddIntermediateCurves();
            EditorGUILayout.HelpBox("The stamp changes world height, so overhangs and vertical faces cannot be reproduced. Stamp resolution controls spline knots; the loft's mesh resolution must also be high enough to display them. Existing sculpt and UV modifiers still apply after regeneration. Shared source curves affect every loft using them.", MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        static float Positive(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(.01f, value);

        void SelectHandleMode(HandleMode mode)
        {
            handleMode = mode;
            placing = false;
            Invalidate();
        }

        void HandleModeShortcut(Event e)
        {
            // Let text fields, modified shortcuts, handle drags and Scene-view
            // camera navigation keep their normal keyboard input.
            if (loft == null || mesh == null || EditorApplication.isPlayingOrWillChangePlaymode
                || e.type != EventType.KeyDown || e.alt || e.control || e.command || e.shift
                || EditorGUIUtility.editingTextField || GUIUtility.hotControl != 0 || Tools.viewToolActive) return;
            switch (e.keyCode)
            {
                case KeyCode.W: SelectHandleMode(HandleMode.Move); break;
                case KeyCode.E: SelectHandleMode(HandleMode.Rotate); break;
                case KeyCode.R: SelectHandleMode(HandleMode.Scale); break;
                default: return;
            }
            e.Use();
        }

        void DrawStampHandle()
        {
            Quaternion rotation = settings.Rotation;
            EditorGUI.BeginChangeCheck();
            switch (handleMode)
            {
                case HandleMode.Move:
                    settings.position = Handles.PositionHandle(settings.position, rotation);
                    break;
                case HandleMode.Rotate:
                    rotation = Handles.RotationHandle(rotation, settings.position);
                    if (GUI.changed)
                    {
                        Vector3 angles = rotation.eulerAngles;
                        settings.pitch = Mathf.DeltaAngle(0, angles.x);
                        settings.yaw = Mathf.DeltaAngle(0, angles.y);
                        settings.roll = Mathf.DeltaAngle(0, angles.z);
                    }
                    break;
                case HandleMode.Scale:
                    Vector3 size = Handles.ScaleHandle(new Vector3(settings.width, settings.height, settings.length),
                        settings.position, rotation, HandleUtility.GetHandleSize(settings.position));
                    settings.width = Positive(size.x, settings.width);
                    settings.height = Positive(size.y, settings.height);
                    settings.length = Positive(size.z, settings.length);
                    break;
            }
            if (EditorGUI.EndChangeCheck()) { positioned = true; Invalidate(); }
        }

        void RebuildPreview()
        {
            dirty = false;
            plans.Clear();
            if (loft == null || mesh == null) return;
            try { plans = LoftMeshStamp.Prepare(loft, mesh, settings); }
            catch (Exception e) { message = e.Message; }
        }

        void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            try
            {
                // Always rebuild at commit: previews may have been throttled or
                // source transforms changed since the last repaint.
                plans = LoftMeshStamp.Prepare(loft, mesh, settings);
                if (plans.Count == 0) message = "No source knots intersect the stamp shape. Move or enlarge it, or add intermediate curves for detail between rails.";
                else
                {
                    int inserted = 0, shaped = 0;
                    foreach (var plan in plans) { inserted += plan.splits.Count; shaped += plan.changed.Count; }
                    LoftMeshStamp.Apply(loft, plans);
                    message = $"Stamped {plans.Count} curves: added {inserted} knots and shaped {shaped}. Undo restores the whole stamp.";
                }
                placing = false;
            }
            catch (Exception e) { message = e.Message; Debug.LogException(e); }
            Invalidate();
        }

        void OnSceneGUI(SceneView view)
        {
            if (loft == null || mesh == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            Event e = Event.current;
            HandleModeShortcut(e);
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (placing && e.type == EventType.Layout) HandleUtility.AddDefaultControl(control);
            if (placing && !e.alt && (e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.MouseDown))
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                float nearest = float.PositiveInfinity;
                bool hitLoft = false;
                foreach (var hit in Physics.RaycastAll(ray, float.PositiveInfinity, ~0, QueryTriggerInteraction.Ignore))
                    if (hit.collider.GetComponentInParent<MultiSplineLoft>() == loft && hit.distance < nearest)
                    {
                        nearest = hit.distance;
                        settings.position = hit.point;
                        positioned = hitLoft = true;
                    }
                if (hitLoft && e.type == EventType.MouseDown && e.button == 0) { placing = false; e.Use(); }
                Invalidate();
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { placing = false; e.Use(); }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Return && positioned) { Apply(); e.Use(); }
            if (e.type == EventType.ScrollWheel && (e.control || e.shift) && !e.alt)
            {
                if (e.shift) settings.yaw = Mathf.Repeat(settings.yaw + e.delta.y * 3 + 180, 360) - 180;
                else
                {
                    float scale = Mathf.Exp(-e.delta.y * .04f);
                    settings.width = Mathf.Max(.01f, settings.width * scale);
                    settings.length = Mathf.Max(.01f, settings.length * scale);
                }
                Invalidate(); e.Use();
            }
            if (!placing) DrawStampHandle();
            if (!preview || e.type != EventType.Repaint) return;
            foreach (var plan in plans)
                if (plan.source.container == null || plan.source.container.transform.localToWorldMatrix != plan.transformMatrix) dirty = true;
            if (dirty && EditorApplication.timeSinceStartup >= nextPreview)
            {
                RebuildPreview();
                nextPreview = EditorApplication.timeSinceStartup + .1;
            }
            Color color = Handles.color;
            try
            {
                DrawGhost();
                Handles.color = Color.cyan;
                foreach (var plan in plans)
                {
                    int count = plan.result.Closed ? plan.result.Count : plan.result.Count - 1;
                    for (int i = 0; i < count; i++)
                    {
                        var curve = plan.result.GetCurve(i);
                        var transform = plan.source.container.transform;
                        Handles.DrawBezier(transform.TransformPoint(curve.P0), transform.TransformPoint(curve.P3),
                            transform.TransformPoint(curve.P1), transform.TransformPoint(curve.P2), Color.cyan, null, 2f);
                    }
                    foreach (int index in plan.changed)
                    {
                        Vector3 p = plan.source.container.transform.TransformPoint(plan.result[index].Position);
                        Handles.DotHandleCap(0, p, Quaternion.identity, HandleUtility.GetHandleSize(p) * .035f, EventType.Repaint);
                    }
                }
                Handles.Label(settings.position, $"Loft Mesh Stamp · {handleMode} · W/E/R modes · Enter to apply");
            }
            finally { Handles.color = color; }
        }

        void DrawGhost()
        {
            if (ghostMesh != mesh)
            {
                ghostMesh = mesh;
                var lines = new List<Vector3>();
                using (var data = MeshUtility.AcquireReadOnlyMeshData(mesh))
                using (var positions = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp))
                {
                    data[0].GetVertices(positions);
                    for (int sub = 0; sub < data[0].subMeshCount; sub++)
                    {
                        var desc = data[0].GetSubMesh(sub);
                        if (desc.topology != MeshTopology.Triangles) continue;
                        using (var indices = new Unity.Collections.NativeArray<int>(desc.indexCount, Unity.Collections.Allocator.Temp))
                        {
                            data[0].GetIndices(indices, sub, true);
                            int stride = Mathf.Max(1, desc.indexCount / 12000) * 3;
                            for (int i = 0; i + 2 < indices.Length; i += stride)
                                for (int edge = 0; edge < 3; edge++)
                                {
                                    lines.Add(positions[indices[i + edge]]);
                                    lines.Add(positions[indices[i + (edge + 1) % 3]]);
                                }
                        }
                    }
                }
                ghostLines = lines.ToArray();
            }
            Matrix4x4 matrix = Matrix4x4.Translate(settings.position) * settings.MeshToStamp(mesh);
            using (new Handles.DrawingScope(new Color(1, .6f, .15f, .65f), matrix))
                if (ghostLines != null) Handles.DrawLines(ghostLines);
        }

        void AddIntermediateCurves()
        {
            if (loft == null || EditorUtility.IsPersistent(loft)) return;
            if (loft.Alignment == MultiSplineLoft.AlongAlignment.ReferencePerpendicular || loft.CloseAcrossSplines)
            {
                message = "Adding curves requires normalized cross-section alignment and an open cross-section. Stamping existing curves supports either alignment.";
                return;
            }
            var sources = loft.Sources;
            if (sources.Count < 2 || sources.Exists(s => s?.IsValid != true)) { message = "The loft needs at least two valid source curves."; return; }
            if ((sources.Count - 1) * extraCurves + sources.Count > 64) { message = "This would exceed 64 source curves. Reduce Curves per Gap."; return; }
            bool closed = sources[0].container.Splines[sources[0].splineIndex].Closed;
            if (sources.Exists(s => s.container.Splines[s.splineIndex].Closed != closed)) { message = "All source curves must share the same closed/open setting."; return; }
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add Loft Stamp Curves");
            LoftMeshStamp.TrackUndo(loft);
            Undo.RegisterCompleteObjectUndo(loft, "Add Loft Stamp Curves");
            try
            {
                var expanded = new List<MultiSplineLoft.SplineSource>();
                for (int i = 0; i < sources.Count - 1; i++)
                {
                    expanded.Add(sources[i]);
                    for (int j = 1; j <= extraCurves; j++)
                    {
                        float across = j / (float)(extraCurves + 1);
                        var points = new List<float3>();
                        for (int k = 0; k < supportKnots; k++)
                        {
                            float t = k / (float)(closed ? supportKnots : supportKnots - 1);
                            Vector3 a = Evaluate(sources[i], t), b = Evaluate(sources[i + 1], t);
                            Vector3 p = Vector3.Lerp(a, b, across);
                            if (loft.AcrossMode == MultiSplineLoft.AcrossInterpolation.CatmullRom)
                            {
                                Vector3 p0 = Evaluate(sources[Mathf.Max(0, i - 1)], t);
                                Vector3 p3 = Evaluate(sources[Mathf.Min(sources.Count - 1, i + 2)], t);
                                float u = across;
                                p = .5f * (2 * a + (-p0 + b) * u + (2 * p0 - 5 * a + 4 * b - p3) * u * u + (-p0 + 3 * a - 3 * b + p3) * u * u * u);
                            }
                            points.Add(loft.transform.InverseTransformPoint(p));
                        }
                        var go = new GameObject("Stamp Support Curve");
                        go.transform.SetParent(loft.transform, false);
                        Undo.RegisterCreatedObjectUndo(go, "Add Loft Stamp Curves");
                        var container = Undo.AddComponent<SplineContainer>(go);
                        container.Spline = new Spline(points, TangentMode.AutoSmooth, closed);
                        expanded.Add(new MultiSplineLoft.SplineSource { container = container });
                    }
                }
                expanded.Add(sources[sources.Count - 1]);
                sources.Clear(); sources.AddRange(expanded);
                EditorUtility.SetDirty(loft);
                PrefabUtility.RecordPrefabInstancePropertyModifications(loft);
                loft.Regenerate();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(loft.gameObject.scene);
                Undo.CollapseUndoOperations(group);
                message = "Intermediate curves added. Review the surface, then place and apply the stamp.";
            }
            catch (Exception e) { Undo.RevertAllDownToGroup(group); loft.Regenerate(); message = e.Message; }
            Invalidate();
        }

        static Vector3 Evaluate(MultiSplineLoft.SplineSource source, float t) =>
            source.container.EvaluatePosition(source.splineIndex, source.reverse ? 1 - t : t);
    }
}
