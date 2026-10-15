/* VertexDirt precision editor. Original VertexDirt copyright 2014-2020 Zoltan Farago. */
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

internal class VertexDirtWindow : EditorWindow
{
    const string PrefsKey = "VertexDirt.Precision.Settings.v1";
    [SerializeField] VDPrecisionBaker.Settings settings = new VDPrecisionBaker.Settings();
    [SerializeField] GameObject target;
    [SerializeField] bool children = true, followSelection = true, showRadius;
    [SerializeField] Vector2 scroll, orbit = new Vector2(25, -25);
    [SerializeField] float zoom = 1;
    [SerializeField] int previewMode;
    [SerializeField] string lastBackup, message;
    List<VDPrecisionBaker.Result> results = new List<VDPrecisionBaker.Result>();
    VDPrecisionBaker.Job job;
    PreviewRenderUtility preview;
    Material previewMaterial;
    MeshFilter[] targets = Array.Empty<MeshFilter>();
    Bounds bounds;
    int vertices, triangles;
    bool samplingChanged, colorsChanged;
    double bakeSeconds;
    string error;

    [MenuItem("MashBox/Content Tools/Vertex AO", false, 10)]
    internal static void ShowWindow() => MashBoxSDK.SDKMain.MashBoxSDKWindow.OpenVertexAO();
    Action repaintHost;
    new void Repaint() { base.Repaint(); repaintHost?.Invoke(); }
    internal void Draw(Action repaint)
    {
        repaintHost = repaint;
        float oldWidth = EditorGUIUtility.labelWidth;
        try { OnGUI(); }
        finally { EditorGUIUtility.labelWidth = oldWidth; }
    }
    void OnEnable()
    {
        titleContent = new GUIContent("MashBox Vertex AO");
        minSize = new Vector2(430, 650); maxSize = new Vector2(10000, 10000);
        if (EditorPrefs.HasKey(PrefsKey)) JsonUtility.FromJsonOverwrite(EditorPrefs.GetString(PrefsKey), settings);
        Selection.selectionChanged += SelectionChanged;
        EditorApplication.update += Tick;
        SceneView.duringSceneGui += DrawScene;
        Undo.undoRedoPerformed += Repaint;
        if (!target) target = Selection.activeGameObject;
        RefreshTargets();
    }
    void OnDisable()
    {
        EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(settings));
        Selection.selectionChanged -= SelectionChanged;
        EditorApplication.update -= Tick;
        SceneView.duringSceneGui -= DrawScene;
        Undo.undoRedoPerformed -= Repaint;
        job?.Dispose(); job = null;
        ReleaseResults();
        preview?.Cleanup(); preview = null;
        if (previewMaterial) DestroyImmediate(previewMaterial);
    }
    void SelectionChanged()
    {
        if (followSelection && job == null && Selection.activeGameObject && !EditorUtility.IsPersistent(Selection.activeGameObject))
        {
            if (target != Selection.activeGameObject) { target = Selection.activeGameObject; ReleaseResults(); }
            RefreshTargets();
        }
        Repaint();
    }
    void RefreshTargets()
    {
        targets = VDPrecisionBaker.Targets(target, children);
        bounds = VDPrecisionBaker.BoundsOf(targets);
        vertices = targets.Sum(f => f.sharedMesh.vertexCount);
        triangles = targets.Sum(f => Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(s => (int)f.sharedMesh.GetIndexCount(s) / 3));
    }
    void ReleaseResults()
    {
        foreach (var r in results) if (r.geometry) DestroyImmediate(r.geometry);
        results.Clear(); samplingChanged = colorsChanged = false;
    }
    void Tick()
    {
        if (job == null) return;
        try
        {
            if (job.Step())
            {
                ReleaseResults(); results = job.TakeResults(); bakeSeconds = job.elapsed;
                job.Dispose(); job = null;
                VDPrecisionBaker.Apply(results, settings);
                UpdatePreviewColors();
                message = $"Baked {vertices:N0} vertices in {bakeSeconds:F1}s. Colors applied to the scene; save to FBX when ready.";
                samplingChanged = colorsChanged = false;
            }
        }
        catch (Exception ex) { error = ex.Message; Debug.LogException(ex); job?.Dispose(); job = null; }
        Repaint();
    }
    void OnGUI()
    {
        EditorGUIUtility.labelWidth = Mathf.Clamp(EditorGUIUtility.currentViewWidth * .45f, 165, 240);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        GUILayout.Space(5);
        GUILayout.Label("Vertex AO", EditorStyles.boldLabel);
        GUILayout.Label("Measure. Bake. Tune. Save into the original FBX.", EditorStyles.miniLabel);
        using (new EditorGUI.DisabledScope(job != null))
        {
            EditorGUI.BeginChangeCheck();
            target = (GameObject)EditorGUILayout.ObjectField("Scene target", target, typeof(GameObject), true);
            using (new EditorGUILayout.HorizontalScope())
            {
                followSelection = GUILayout.Toggle(followSelection, "Follow selection");
                children = GUILayout.Toggle(children, "Include children");
                if (GUILayout.Button("Frame", GUILayout.Width(55))) Frame();
            }
            if (EditorGUI.EndChangeCheck()) { ReleaseResults(); RefreshTargets(); }
        }
        if (targets.Length == 0)
            EditorGUILayout.HelpBox("Select a mesh object in the scene Hierarchy. This tool bakes Mesh Filters; skinned meshes are not supported.", MessageType.Info);
        else
        {
            EditorGUILayout.LabelField($"{targets.Length} mesh(es)    {vertices:N0} vertices    {triangles:N0} triangles", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"World size: {bounds.size.x * 1000:F2} x {bounds.size.y * 1000:F2} x {bounds.size.z * 1000:F2} mm", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Units assume 1 Unity unit = 1 metre.", EditorStyles.miniLabel);
            DrawPreview();
            if (vertices < 500) EditorGUILayout.HelpBox("This mesh has few vertices. Baked detail can only change at vertices; higher samples reduce noise but do not add geometry.", MessageType.Info);
        }
        using (new EditorGUI.DisabledScope(job != null))
        {
            Section("1   Sampling");
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Fit to small part", "Distance = 10% of the largest dimension. Bias = 0.02%."))) FitSmallPart();
                if (GUILayout.Button("Draft")) { settings.samples = 32; GUI.changed = true; }
                if (GUILayout.Button("Fine")) { settings.samples = 256; GUI.changed = true; }
                if (GUILayout.Button("Final")) { settings.samples = 512; GUI.changed = true; }
            }
            settings.distance = Millimetres("Occlusion distance (mm)", "Reach of the AO rays, in world space. Type tiny values directly.", settings.distance, .000001f);
            settings.bias = Millimetres("Surface bias (mm)", "Offset rays from the surface. Lower this for thin teeth and close gaps; raise it for self-shadow speckling.", settings.bias, 0);
            settings.samples = EditorGUILayout.IntSlider(new GUIContent("Samples per vertex", "More rays reduce sampling noise; they do not increase mesh resolution."), settings.samples, 16, 1024);
            settings.angle = EditorGUILayout.Slider(new GUIContent("Sampling cone (degrees)", "180 samples the full outward hemisphere."), settings.angle, 30, 180);
            settings.falloff = EditorGUILayout.Slider(new GUIContent("Distance falloff", "0 gives equal weight to all hits. Higher values emphasize nearby crevices."), settings.falloff, 0, 4);
            settings.sceneOccluders = EditorGUILayout.Toggle(new GUIContent("Use surrounding meshes", "Off: only targets occlude. On: enabled Mesh Renderers in the same scene also occlude. Terrain and skinned renderers are excluded."), settings.sceneOccluders);
            settings.layerMask = EditorGUILayout.MaskField("Occluder layers", settings.layerMask, LayerNames());
            settings.twoSided = EditorGUILayout.Toggle(new GUIContent("Two-sided occluders", "Useful for thin rings and teeth. Both sides of each triangle can block a ray."), settings.twoSided);
            settings.smoothNormals = EditorGUILayout.Toggle(new GUIContent("Average seam normals", "Average sampling directions at coincident vertices. Does not change mesh normals or topology."), settings.smoothNormals);
            if (settings.smoothNormals)
            {
                settings.smoothTolerance = Millimetres("Seam tolerance (mm)", "Keep much smaller than the thinnest gap.", settings.smoothTolerance, .000000001f);
                settings.smoothAngle = EditorGUILayout.Slider("Maximum seam angle", settings.smoothAngle, 0, 180);
            }
            if (EditorGUI.EndChangeCheck()) { samplingChanged = results.Count > 0; EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(settings)); }
            showRadius = EditorGUILayout.Toggle("Show radius in Scene view", showRadius);
            if (settings.bias >= settings.distance) EditorGUILayout.HelpBox("Surface bias must be smaller than occlusion distance.", MessageType.Error);
            if (targets.Length > 0 && settings.bias > bounds.size.magnitude * .01f)
                EditorGUILayout.HelpBox("The bias is large relative to this part and may skip narrow gaps. Try Fit to small part.", MessageType.Warning);
            if (settings.layerMask == 0) EditorGUILayout.HelpBox("No occluder layers are enabled. The bake will be fully unoccluded.", MessageType.Warning);
            if (samplingChanged) EditorGUILayout.HelpBox("Sampling settings changed. Bake again to update occlusion.", MessageType.Info);
            Section("2   Color tuning - no rebake needed");
            EditorGUI.BeginChangeCheck();
            settings.output = (VDPrecisionBaker.Output)EditorGUILayout.EnumPopup("Write to channels", settings.output);
            settings.strength = EditorGUILayout.Slider("AO strength", settings.strength, 0, 3);
            settings.gamma = EditorGUILayout.Slider("Midtone gamma", settings.gamma, .1f, 3);
            settings.blackPoint = EditorGUILayout.Slider("Black point", settings.blackPoint, 0, .99f);
            settings.whitePoint = EditorGUILayout.Slider("White point", settings.whitePoint, settings.blackPoint + .001f, 1);
            if (settings.output == VDPrecisionBaker.Output.RGB || settings.output == VDPrecisionBaker.Output.RGBA)
            {
                settings.shadow = EditorGUILayout.ColorField("Occluded color", settings.shadow);
                settings.light = EditorGUILayout.ColorField("Unoccluded color", settings.light);
            }
            settings.multiply = EditorGUILayout.Toggle(new GUIContent("Multiply source colors", "Uses the original source colors, so repeated bakes do not accumulate darkness."), settings.multiply);
            if (EditorGUI.EndChangeCheck()) { colorsChanged = results.Count > 0; UpdatePreviewColors(); }
            EditorGUILayout.HelpBox("RGB preserves alpha. Single-channel output preserves every other channel. Your material must read vertex colors; use the preview to inspect the data directly.", MessageType.None);
            DrawStatistics();
            using (new EditorGUI.DisabledScope(results.Count == 0))
                if (GUILayout.Button(colorsChanged ? "Apply tuned colors to scene *" : "Apply tuned colors to scene")) Run(() => { VDPrecisionBaker.Apply(results, settings); colorsChanged = false; });
        }
        if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
        Section("3   Save to source FBX");
        string sourcePath = null;
        try
        {
            if (results.Count > 0) sourcePath = VDFbxColorWriter.SourcePath(results);
            else if (targets.Length > 0)
            {
                var paths = targets.Select(t => AssetDatabase.GetAssetPath(VDPrecisionBaker.Source(t))).Distinct().ToArray();
                if (paths.Length == 1 && paths[0].EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) sourcePath = paths[0];
            }
        }
        catch (Exception ex) { EditorGUILayout.HelpBox(ex.Message, MessageType.Info); }
        if (sourcePath != null) EditorGUILayout.SelectableLabel(sourcePath, EditorStyles.wordWrappedMiniLabel, GUILayout.Height(36));
        EditorGUILayout.HelpBox("Overwrite updates the original binary FBX's vertex colors. Its .meta / GUID and other FBX data are preserved. A dated backup is kept in Library/VertexDirtBackups. All uses of this model receive the saved colors.", MessageType.None);
        using (new EditorGUI.DisabledScope(job != null || results.Count == 0 || sourcePath == null || samplingChanged))
            if (GUILayout.Button("Overwrite original FBX...", GUILayout.Height(30)))
                if (EditorUtility.DisplayDialog("Overwrite source FBX", "Save the tuned vertex colors into:\n\n" + sourcePath + "\n\nAll uses of this model will update. A backup will be created before replacement.", "Overwrite FBX", "Cancel"))
                    Run(() =>
                    {
                        VDPrecisionBaker.Apply(results, settings);
                        lastBackup = VDFbxColorWriter.Overwrite(results);
                        message = "Saved and verified the original FBX. Backup: " + lastBackup;
                        colorsChanged = false; RefreshTargets();
                    });
        if (!string.IsNullOrEmpty(lastBackup) && GUILayout.Button("Show last FBX backup")) EditorUtility.RevealInFinder(lastBackup);
        GUILayout.Space(8);
        EditorGUILayout.EndScrollView();
        DrawBakeFooter();
    }
    void DrawBakeFooter()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            if (job != null)
            {
                float progress = job.total > 0 ? (float)job.completed / job.total : 0;
                var rect = GUILayoutUtility.GetRect(1, 23, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(rect, progress, $"{progress:P0}  |  {job.completed:N0}/{job.total:N0} vertices  |  {job.elapsed:F1}s");
                double remaining = job.completed > 0 ? job.elapsed * (job.total - job.completed) / job.completed : 0;
                GUILayout.Label($"{job.triangleCount:N0} occluder triangles  |  about {remaining:F0}s remaining", EditorStyles.miniLabel);
                if (GUILayout.Button("Cancel bake (keep previous colors)", GUILayout.Height(26)))
                { job.Dispose(); job = null; message = "Bake cancelled. Previous colors were kept."; }
            }
            else
            {
                if (results.Count > 0) GUILayout.Label(samplingChanged ? "Sampling changed - rebake to update" : colorsChanged ? "Preview updated - apply tuned colors below" : $"Bake complete  |  {vertices:N0} vertices  |  {bakeSeconds:F1}s", EditorStyles.miniLabel);
                using (new EditorGUI.DisabledScope(targets.Length == 0 || settings.bias >= settings.distance || EditorApplication.isPlaying))
                    if (GUILayout.Button($"Bake occlusion  |  {(long)vertices * settings.samples:N0} rays", GUILayout.Height(32))) StartBake();
            }
        }
    }
    void StartBake() { Run(() => { RefreshTargets(); job = new VDPrecisionBaker.Job(targets, settings); message = null; }); }
    void Run(Action action) { try { error = null; action(); } catch (Exception ex) { error = ex.Message; Debug.LogException(ex); } }
    void FitSmallPart()
    {
        float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (size <= 0) return;
        settings.distance = size * .1f; settings.bias = size * .0002f;
        settings.smoothTolerance = size * .00001f; settings.smoothAngle = 120;
        settings.samples = 256; settings.angle = 180; settings.falloff = 1; settings.twoSided = true;
        settings.smoothNormals = true; settings.sceneOccluders = false; GUI.changed = true;
    }
    void Frame() { RefreshTargets(); SceneView.lastActiveSceneView?.Frame(bounds, false); orbit = new Vector2(25, -25); zoom = 1; }
    static float Millimetres(string label, string tooltip, float metres, float minimum)
    {
        float value = EditorGUILayout.FloatField(new GUIContent(label, tooltip), metres * 1000) / 1000;
        return float.IsNaN(value) || float.IsInfinity(value) ? metres : Mathf.Max(minimum, value);
    }
    static string[] LayerNames() => Enumerable.Range(0, 32).Select(i => string.IsNullOrEmpty(LayerMask.LayerToName(i)) ? "Layer " + i : LayerMask.LayerToName(i)).ToArray();
    static void Section(string label) { GUILayout.Space(10); GUILayout.Label(label, EditorStyles.boldLabel); }
    void UpdatePreviewColors()
    {
        foreach (var r in results)
            if (r.geometry) r.geometry.colors32 = previewMode == 5 ? r.visibility.Select(v => (Color32)new Color(v, v, v, 1)).ToArray() : VDPrecisionBaker.Remap(r, settings);
        Repaint();
    }
    void DrawStatistics()
    {
        if (results.Count == 0) return;
        var values = results.SelectMany(r => r.visibility).ToArray();
        if (values.Length == 0) return;
        EditorGUILayout.LabelField($"Raw AO: min {values.Min():F3}   mean {values.Average():F3}   max {values.Max():F3}   |   {bakeSeconds:F1}s", EditorStyles.miniLabel);
        int[] bins = new int[32]; foreach (float value in values) bins[Mathf.Min(31, (int)(value * 32))]++;
        Rect rect = GUILayoutUtility.GetRect(1, 32, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(.1f, .1f, .1f));
        int maximum = Mathf.Max(1, bins.Max());
        for (int i = 0; i < bins.Length; i++)
        {
            float height = rect.height * bins[i] / maximum;
            EditorGUI.DrawRect(new Rect(rect.x + rect.width * i / 32, rect.yMax - height, rect.width / 32 - 1, height), Color.Lerp(new Color(.2f, .55f, .65f), Color.white, i / 31f));
        }
        GUILayout.Label("Occluded / dark                                                  Open / light", EditorStyles.miniLabel);
    }
    void DrawPreview()
    {
        EditorGUI.BeginChangeCheck();
        previewMode = EditorGUILayout.Popup("Inspect", previewMode, new[] { "Vertex RGB", "Red channel", "Green channel", "Blue channel", "Alpha channel", "Raw AO", "Wireframe" });
        if (EditorGUI.EndChangeCheck()) UpdatePreviewColors();
        Rect rect = GUILayoutUtility.GetRect(100, Mathf.Clamp(EditorGUIUtility.currentViewWidth * .52f, 180, 330), GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.MouseDrag && rect.Contains(Event.current.mousePosition) && Event.current.button == 0)
        { orbit += Event.current.delta; Event.current.Use(); Repaint(); }
        if (Event.current.type == EventType.ScrollWheel && rect.Contains(Event.current.mousePosition))
        { zoom = Mathf.Clamp(zoom * Mathf.Exp(Event.current.delta.y * .04f), .3f, 3); Event.current.Use(); Repaint(); }
        if (Event.current.type != EventType.Repaint) return;
        if (preview == null)
        {
            preview = new PreviewRenderUtility(); preview.camera.fieldOfView = 30;
            preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 100;
        }
        if (!previewMaterial)
        {
            var shader = Shader.Find("Hidden/VertexDirt/PrecisionPreview");
            if (!shader) { GUI.Label(rect, "Preview shader is importing..."); return; }
            previewMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        previewMaterial.SetInt("_Channel", previewMode >= 1 && previewMode <= 4 ? previewMode : 0);
        preview.BeginPreview(rect, GUIStyle.none);
        preview.camera.transform.position = new Vector3(0, 0, -2.3f * zoom);
        preview.camera.transform.rotation = Quaternion.identity;
        preview.camera.clearFlags = CameraClearFlags.SolidColor;
        preview.camera.backgroundColor = new Color(.11f, .12f, .14f);
        float scale = 1 / Mathf.Max(Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z), .000001f);
        Vector3 face = bounds.size.x < bounds.size.y && bounds.size.x < bounds.size.z ? Vector3.right
            : bounds.size.y < bounds.size.z ? Vector3.up : Vector3.forward;
        Quaternion rotation = Quaternion.Euler(orbit.y, orbit.x, 0) * Quaternion.FromToRotation(face, Vector3.forward);
        Matrix4x4 matrix = Matrix4x4.Rotate(rotation) * Matrix4x4.Scale(Vector3.one * scale) * Matrix4x4.Translate(-bounds.center);
        foreach (var f in targets)
        {
            if (!f || !f.sharedMesh) continue;
            var result = results.FirstOrDefault(r => r.filter == f);
            Mesh mesh = result != null && result.geometry ? result.geometry : f.sharedMesh;
            var properties = new MaterialPropertyBlock();
            properties.SetFloat("_HasColors", mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color) ? 1 : 0);
            for (int sub = 0; sub < mesh.subMeshCount; sub++) preview.DrawMesh(mesh, matrix * f.transform.localToWorldMatrix, previewMaterial, sub, properties);
        }
        bool wireframe = GL.wireframe;
        try { GL.wireframe = previewMode == 6; preview.Render(false); }
        finally { GL.wireframe = wireframe; }
        GUI.DrawTexture(rect, preview.EndPreview(), ScaleMode.StretchToFill, false);
        GUI.Label(new Rect(rect.x + 8, rect.y + 5, rect.width - 16, 20), "Drag to orbit  |  Scroll to zoom", EditorStyles.whiteMiniLabel);
        if (results.Count == 0) GUI.Label(new Rect(rect.x + 8, rect.yMax - 24, rect.width - 16, 20), previewMode == 5 ? "Bake to see raw AO" : "Current mesh colors", EditorStyles.whiteMiniLabel);
    }
    void DrawScene(SceneView view)
    {
        if (!showRadius || targets.Length == 0) return;
        Color old = Handles.color; Handles.color = new Color(.25f, .85f, 1, .8f);
        Handles.DrawWireDisc(bounds.center, Vector3.up, settings.distance);
        Handles.DrawWireDisc(bounds.center, Vector3.right, settings.distance);
        Handles.Label(bounds.center, $"AO reach {settings.distance * 1000:F3} mm  |  bias {settings.bias * 1000:F4} mm");
        Handles.color = old;
    }
}

