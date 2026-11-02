using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MG.GodSurface.Editor
{
    /// <summary>Editor-only setup; the existing world component owns rendering at runtime.</summary>
    public static class MGSurfaceRootSetup
    {
        public sealed class Check
        {
            public int total, eligible, surfaces;
            public readonly List<Behaviour> conflicts = new List<Behaviour>();
            public readonly Dictionary<string, int> fallbacks = new Dictionary<string, int>();
            public string Summary => $"{total:N0} prop renderers; {eligible:N0} eligible for cells; {surfaces:N0} MG Lit Basic surfaces to cook.\n" +
                string.Join("\n", fallbacks.Select(p => $"{p.Key}: {p.Value:N0}")) +
                (conflicts.Count == 0 ? "\nNo known combiner/instancer conflicts." : $"\n{conflicts.Count} competing combiner/instancer components. Resolve these before cooking.");
        }

#if MashBoxDev
        [MenuItem("MashBox/Dev/Content Tools/God Props/Create or Select Prop Cell Manager")]
#endif
        public static void AddSelected()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var world = MGSurfaceWorld.ForScene(scene);
            if (!world)
            {
                var root = new GameObject("PROP CELLS");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                Undo.RegisterCreatedObjectUndo(root, "Create prop cell manager");
                world = Undo.AddComponent<MGSurfaceWorld>(root);
            }
            Selection.activeGameObject = world.gameObject;
            EditorGUIUtility.PingObject(world);
        }
#if MashBoxDev
        [MenuItem("MashBox/Dev/Content Tools/God Props/Create or Select Prop Cell Manager", true)]
#endif
        static bool CanAdd() => !EditorApplication.isPlayingOrWillChangePlaymode;

        public static Check Inspect(MGSurfaceWorld world)
        {
            MGSurfaceSingleManager.Validate(world.gameObject.scene);
            var result = new Check();
            bool active = world.IsRendering;
            world.Release();
            try
            {
                var renderers = world.EnumerateRenderers().ToArray();
                var materials = new HashSet<Material>();
                var inspectedMaterials = new HashSet<Material>();
                var inspectedParents = new HashSet<Transform>();
                var candidates = new HashSet<Behaviour>();
                foreach (var renderer in renderers)
                {
                    result.total++;
                    var reason = world.UnsupportedReason(renderer);
                    if (reason == null)
                    {
                        result.eligible++;
                        foreach (var material in renderer.sharedMaterials)
                        {
                            if (!inspectedMaterials.Add(material)) continue;
                            var original = MGSurfaceBaker.Original(material, world.surfaceLibrary);
                            if (original && original.shader && original.shader.name == MGSurfaceBaker.SourceShader) materials.Add(original);
                        }
                    }
                    else { result.fallbacks.TryGetValue(reason, out int n); result.fallbacks[reason] = n + 1; }
                    // Include ancestors above the chosen root: they can also consume these renderers.
                    for (var parent = renderer.transform; parent && inspectedParents.Add(parent); parent = parent.parent)
                        foreach (var component in parent.GetComponents<Behaviour>())
                            if (component && IsConflict(component)) candidates.Add(component);
                }
                result.surfaces = materials.Count;
                result.conflicts.AddRange(candidates.OrderBy(c => c.name));
                return result;
            }
            finally { if (active) world.RequestRebuild(); }
        }
        static bool IsConflict(Behaviour component)
        {
            if (component is RuntimeMeshCombiner)
            {
                var so = new SerializedObject(component);
                // Awake runs even on a disabled component.
                return component.enabled || so.FindProperty("combineOnAwake").boolValue;
            }
            return component is MashBoxSDK.Map.Rendering.Instancer.InstanceGroup && component.enabled;
        }
        public static int DisableConflicts(MGSurfaceWorld world)
        {
            var check = Inspect(world);
            var owned = new HashSet<MeshRenderer>(world.EnumerateRenderers());
            foreach (var component in check.conflicts)
                if (component.GetComponentsInChildren<MeshRenderer>(true).Any(r => !owned.Contains(r)))
                    throw new InvalidOperationException($"{component.name}: this {component.GetType().Name} also manages props outside this root's scope. Split its scope or disable it manually before cooking.");
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Use God Prop rendering");
            foreach (var component in check.conflicts)
            {
                var so = new SerializedObject(component);
                so.FindProperty("m_Enabled").boolValue = false;
                var awake = so.FindProperty("combineOnAwake"); if (awake != null) awake.boolValue = false;
                var readWrite = so.FindProperty("enableReadWriteOnValidate"); if (readWrite != null) readWrite.boolValue = false;
                so.ApplyModifiedProperties();
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
            Undo.CollapseUndoOperations(group);
            world.RequestRebuild();
            return check.conflicts.Count;
        }
        public static string Cook(MGSurfaceWorld world, int resolution, bool compress, bool vertexAO, MGSurfaceVertexAO.Settings ao)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.IsPersistent(world))
                throw new InvalidOperationException("Cook a scene root outside Play Mode.");
            var check = Inspect(world);
            if (check.conflicts.Count > 0) throw new InvalidOperationException("Resolve competing combiners/instancers with Check Props before cooking.");
            if (check.eligible == 0) throw new InvalidOperationException("No eligible renderers. Check Props shows why they fall back.");
            bool oldPreview = world.previewInEditor;
            // Roll back scene assignments on cancellation/failure; generated library versions remain available.
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Cook God Prop Root");
            try
            {
                string summary;
                if (check.surfaces > 0)
                {
                    var oldMaterials = world.surfaceLibrary ? MGSurfaceVertexAO.Materials(world.surfaceLibrary) : Array.Empty<Material>();
                    float previousAO = oldMaterials.Length > 0 ? oldMaterials[0].GetFloat(MGSurfaceVertexAO.Strength) : 0;
                    var library = MGSurfaceBaker.Bake(world, resolution, compress);
                    int count = MGSurfaceBaker.Apply(world, library);
                    if (!vertexAO && previousAO > 0) MGSurfaceVertexAO.SetStrength(world, library, previousAO);
                    summary = $"Cooked {check.surfaces} surfaces; updated {count:N0} renderers. ";
                }
                else summary = "Using existing materials for cell instancing. ";
                if (vertexAO)
                {
                    if (!world.surfaceLibrary) throw new InvalidOperationException("Vertex AO requires MG Lit Basic surfaces cooked into a God Surface library.");
                    summary += MGSurfaceVertexAO.BakeWorld(world, ao) + "\n";
                }
                if (world.surfaceLibrary)
                {
                    var geometry = MGSurfaceGeometryBaker.Bake(world);
                    summary += $"Baked {geometry.shapes.Length} geometry shapes into one shared carrier. ";
                }
                Undo.RecordObject(world, "Enable God Prop Root");
                world.enabled = true; world.renderInPlayMode = true; world.previewInEditor = true;
                EditorUtility.SetDirty(world); PrefabUtility.RecordPrefabInstancePropertyModifications(world);
                EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
                world.Rebuild();
                if (!string.IsNullOrEmpty(world.LastError)) throw new InvalidOperationException(world.LastError);
                return summary + $"Ready: {world.InstanceCount:N0} instances in {world.CellCount:N0} cells. {world.FallbackCount:N0} renderers keep normal rendering. Save the scene to keep assignments.";
            }
            catch
            {
                world.Release(); Undo.RevertAllDownToGroup(group); world.previewInEditor = oldPreview; world.RequestRebuild(); throw;
            }
            finally { Undo.CollapseUndoOperations(group); SceneView.RepaintAll(); }
        }
    }

    [CustomEditor(typeof(MGSurfaceWorld))]
    public sealed class MGSurfaceWorldInspector : UnityEditor.Editor
    {
        bool advanced;
        bool showGeneratedMaterials = true;
        readonly HashSet<int> expandedSurfaceBanks = new HashSet<int>();
        bool drawStats = true;
        double nextStatsRefresh;
        void OnEnable() => EditorApplication.update += RefreshStats;
        void OnDisable() => EditorApplication.update -= RefreshStats;
        void RefreshStats()
        {
            if (!drawStats || !target || EditorApplication.isCompiling || EditorApplication.timeSinceStartup < nextStatsRefresh) return;
            nextStatsRefresh = EditorApplication.timeSinceStartup + .25;
            Repaint(); // Counters only: never rebuild or request a Scene view render here.
        }
        MGSurfaceRootSetup.Check check;
        string status;
        MessageType statusType;
        public override void OnInspectorGUI()
        {
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(EditorGUIUtility.currentViewWidth * .52f, 180, 270);
            var world = (MGSurfaceWorld)target;
            if (!world.IsSceneManager)
            {
                EditorGUILayout.HelpBox("Only one God Prop Root is allowed per scene. This extra component cannot render, cook or build. Use the existing manager's Prop Roots list.", MessageType.Error);
                var owner = MGSurfaceWorld.ForScene(world.gameObject.scene);
                EditorGUILayout.ObjectField("Existing manager", owner, typeof(MGSurfaceWorld), true);
                if (owner && GUILayout.Button("Select existing manager")) Selection.activeGameObject = owner.gameObject;
                if (GUILayout.Button("Remove this extra component (Undo)"))
                { Undo.DestroyObjectImmediate(world); GUIUtility.ExitGUI(); }
                EditorGUIUtility.labelWidth = oldLabelWidth;
                return;
            }
            serializedObject.Update();
            EditorGUILayout.LabelField("God Prop Root", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("One Prop Cell manager per scene. Place it on an empty object, then drag the prop roots you want into the list below. Roots can be anywhere in this scene; props and colliders stay in their existing hierarchies.", MessageType.Info);
            Property("roots", "Prop Roots");
            if (world.roots == null || world.roots.Length == 0)
                EditorGUILayout.HelpBox("Add prop roots to the list. An empty list renders nothing.", MessageType.Info);
            if (world.roots != null && world.roots.Any(r => r && r.gameObject.scene != world.gameObject.scene))
                EditorGUILayout.HelpBox("Prop roots must belong to the same scene as this manager. Cross-scene entries are ignored.", MessageType.Error);
            Property("cellSize", "Cell Size (m)");
            Property("cellDrawDistance", "Cell Draw Distance (m)");
            Property("lodSelection", "LOD Selection");
            Property("renderInPlayMode", "Render In Play Mode");
            Property("previewInEditor", "Preview Props In Editor");
            Property("drawCellBounds", "Show Prop Cells");
            DrawSurfaceAssets();
            DrawStats(world);
            advanced = EditorGUILayout.Foldout(advanced, "Scope and advanced settings", true);
            if (advanced)
            {
                Property("supportedShaders", "Supported Source Shaders");
                EditorGUILayout.HelpBox("All listed prop roots share this single registry. Unsupported renderers keep normal rendering and are outside cell culling. Source materials remain usable without cooking arrays.", MessageType.None);
            }
            serializedObject.ApplyModifiedProperties();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if ((world.transform.childCount > 0 || world.GetComponent<MeshRenderer>()) && GUILayout.Button("Move manager to a new empty object (Undo)"))
                { MGSurfaceSingleManager.MoveToEmptyObject(world); GUIUtility.ExitGUI(); }
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.IsPersistent(world)))
            {
                if (GUILayout.Button("1. Check Props")) Run(() => { check = MGSurfaceRootSetup.Inspect(world); return "Prop check complete."; });
                if (check != null) EditorGUILayout.HelpBox(check.Summary, check.conflicts.Count > 0 ? MessageType.Warning : MessageType.Info);
                if (check != null && check.conflicts.Count > 0)
                {
                    foreach (var component in check.conflicts) if (component) EditorGUILayout.ObjectField(component, typeof(Behaviour), true);
                    if (GUILayout.Button("Disable Listed Combiners / Instancers (Undo)")) Run(() =>
                    {
                        int count = MGSurfaceRootSetup.DisableConflicts(world); check = MGSurfaceRootSetup.Inspect(world);
                        return $"Disabled {count} competing components, including Combine On Awake.";
                    });
                }
                EditorGUILayout.Space();
                serializedObject.Update();
                Property("autoCookOnBundleBuild", "Auto Cook on Bundle Build");
                if (world.autoCookOnBundleBuild)
                    EditorGUILayout.HelpBox("MashBox bundle builds cook enabled roots and SAVE their scene before collecting dependencies. This includes other unsaved scene edits. Conflicting combiners or a failed bake stop the build. Each build creates a new bake version. Disabled roots and roots with Render In Play Mode off are skipped.", MessageType.Info);
                var size = serializedObject.FindProperty("cookResolution");
                size.intValue = EditorGUILayout.IntPopup("Maximum Texture Size", size.intValue, new[] { "128", "256", "512", "1024", "2048" }, new[] {128, 256, 512, 1024, 2048});
                Property("cookCompress", "BC7 Compression");
                Property("cookVertexAO", "Bake Vertex AO");
                if (serializedObject.FindProperty("cookVertexAO").boolValue)
                {
                    Property("cookAOSamples", "AO Samples");
                    Property("cookAOReach", "AO Reach / Mesh Size");
                }
                serializedObject.ApplyModifiedProperties();
                EditorGUILayout.HelpBox("Cook packs compatible surfaces into shared banks with trilinear filtering, 8x anisotropy and zero mip bias. Texture addressing and active render settings stay separate. It saves a new library version beside the scene in GodSurface, with baked geometry in Meshes, applies it, and starts the cell preview. Scene assignments support Undo. Re-cook after adding new meshes or surface materials. Rebuild cells after placing more instances of baked props.", MessageType.None);
                if (GUILayout.Button("2. Cook & Apply", GUILayout.Height(30))) Run(() => MGSurfaceRootSetup.Cook(world, world.cookResolution, world.cookCompress, world.cookVertexAO, BakeSettings(world)));
                if (world.surfaceLibrary && GUILayout.Button("Bake Geometry IDs (keep surface textures)")) Run(() =>
                {
                    var geometry = MGSurfaceGeometryBaker.Bake(world); world.Rebuild();
                    if (!string.IsNullOrEmpty(world.LastError)) throw new InvalidOperationException(world.LastError);
                    return $"Baked {geometry.shapes.Length} geometry shapes into one carrier; {world.DrawGroupCount} render groups.";
                });
                if (GUILayout.Button("Rebuild Cells (keep current bake)")) Run(() => { world.Rebuild(); return "Registry rebuilt. Enable Preview Props In Editor to see the cell renderer."; });
                if (world.surfaceLibrary)
                {
                    var materials = MGSurfaceVertexAO.Materials(world.surfaceLibrary);
                    if (materials.Length > 0)
                    {
                        EditorGUI.BeginChangeCheck();
                        float strength = EditorGUILayout.Slider("Vertex AO Strength", materials[0].GetFloat(MGSurfaceVertexAO.Strength), 0, 1);
                        if (EditorGUI.EndChangeCheck()) MGSurfaceVertexAO.SetStrength(world, world.surfaceLibrary, strength);
                    }
                }
                if (EditorGraphicsSettings.batchRendererGroupShaderStrippingMode != BatchRendererGroupStrippingMode.KeepAll)
                {
                    EditorGUILayout.HelpBox("Before building, set Graphics > Shader Stripping > BatchRendererGroup Variants to Keep All.", MessageType.Warning);
                    if (GUILayout.Button("Open Graphics Settings"))
                        SettingsService.OpenProjectSettings("Project/Graphics");
                }
            }
            if (!string.IsNullOrEmpty(world.LastError)) EditorGUILayout.HelpBox(world.LastError, MessageType.Error);
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, statusType);
            EditorGUILayout.HelpBox("Turn Preview Props In Editor off to edit with normal renderers. Disable this component to stop cell rendering. Save the scene after cooking.", MessageType.None);
            EditorGUIUtility.labelWidth = oldLabelWidth;
        }
        void DrawSurfaceAssets()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("God Surface Shader & Materials", EditorStyles.boldLabel);
            Property("surfaceLibrary", "Baked Surface Library");
            var library = serializedObject.FindProperty("surfaceLibrary").objectReferenceValue as MGSurfaceLibrary;
            AssetReference("God Surface Shader", Shader.Find(MGSurfaceLibrary.ShaderName), typeof(Shader));
            if (!library)
            {
                EditorGUILayout.HelpBox("No baked library is assigned. Cook & Apply generates the God Surface materials and assigns them to supported props.", MessageType.Info);
                return;
            }
            if (library.geometry)
            {
                AssetReference("Geometry ID Library", library.geometry, typeof(MGSurfaceGeometry));
                AssetReference("Shared Geometry Carrier", library.geometry.carrier, typeof(Mesh));
                EditorGUILayout.LabelField("Baked shapes / geometry pieces", $"{library.geometry.shapes.Length} / {library.geometry.chunkCount}");
                EditorGUILayout.LabelField("Geometry texture memory", $"{library.geometry.TexturePayloadBytes / 1048576.0:F2} MiB");
            }
            else EditorGUILayout.HelpBox("Geometry IDs have not been baked. Cook & Apply or Bake Geometry IDs to render different shapes through one shared carrier.", MessageType.Info);
            var banks = library.banks ?? Array.Empty<MGSurfaceLibrary.Bank>();
            int surfaces = 0;
            foreach (var bank in banks) if (bank != null) surfaces += bank.previewMaterials?.Length ?? 0;
            showGeneratedMaterials = EditorGUILayout.Foldout(showGeneratedMaterials,
                $"Generated Materials ({banks.Length} banks / {surfaces} surfaces)", true);
            EditorGUILayout.LabelField("Array texture memory", $"{library.TexturePayloadBytes / 1048576.0:F2} MiB");
            if (!showGeneratedMaterials) return;
            EditorGUILayout.HelpBox("Bank materials are used for cell instancing. Expand a bank's surface materials to inspect the individual material IDs assigned to props. Select opens the actual generated asset in the Inspector.", MessageType.None);
            for (int i = 0; i < banks.Length; i++)
            {
                var bank = banks[i];
                if (bank == null) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    AssetReference($"Bank {i} — Instanced Material", bank.sharedMaterial, typeof(Material));
                    if (!bank.sharedMaterial || !bank.sharedMaterial.shader || bank.sharedMaterial.shader.name != MGSurfaceLibrary.ShaderName)
                        EditorGUILayout.HelpBox("This bank is missing its God Surface material. Re-cook the library.", MessageType.Warning);
                    string[] channelNames = { "Base", "Normal", "Mask", "Emission", "Detail", "Decal" };
                    if (bank.arrays != null)
                        EditorGUILayout.LabelField("Unique texture slices", string.Join(" / ", bank.arrays.Select((a, n) =>
                            $"{(n < channelNames.Length ? channelNames[n] : n.ToString())}: {(a ? a.depth : 0)}")), EditorStyles.wordWrappedMiniLabel);
                    bool expanded = EditorGUILayout.Foldout(expandedSurfaceBanks.Contains(i),
                        $"Surface Materials ({bank.previewMaterials?.Length ?? 0})", true);
                    if (expanded) expandedSurfaceBanks.Add(i); else expandedSurfaceBanks.Remove(i);
                    if (expanded && bank.previewMaterials != null)
                        for (int id = 0; id < bank.previewMaterials.Length; id++)
                        {
                            string name = bank.surfaceNames != null && id < bank.surfaceNames.Length ? bank.surfaceNames[id] : "Surface";
                            AssetReference($"ID {id}: {name}", bank.previewMaterials[id], typeof(Material));
                        }
                }
            }
        }
        static void AssetReference(string label, UnityEngine.Object asset, Type type)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField(new GUIContent(label, asset ? AssetDatabase.GetAssetPath(asset) : "Missing asset"), asset, type, false);
                using (new EditorGUI.DisabledScope(!asset))
                    if (GUILayout.Button("Select", GUILayout.Width(54)))
                    { Selection.activeObject = asset; EditorGUIUtility.PingObject(asset); }
            }
        }
        void DrawStats(MGSurfaceWorld world)
        {
            EditorGUILayout.Space();
            drawStats = EditorGUILayout.Foldout(drawStats, "Live Draw Stats", true);
            if (!drawStats) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (!world.IsRendering)
                    EditorGUILayout.HelpBox("Cell rendering is inactive. Enable this component and Preview Props In Editor, or enter Play Mode with Render In Play Mode enabled.", MessageType.Info);
                EditorGUILayout.LabelField("Registered instances", world.InstanceCount.ToString("N0"));
                EditorGUILayout.LabelField("GPU geometry instances", world.GeometryInstanceCount.ToString("N0"));
                EditorGUILayout.LabelField("Occupied cells", world.CellCount.ToString("N0"));
                EditorGUILayout.LabelField("Visible cells (last camera)", world.LastVisibleCells.ToString("N0"));
                EditorGUILayout.LabelField("Visible props (last camera)", world.LastVisibleInstances.ToString("N0"));
                EditorGUILayout.LabelField("Draw commands (last camera)", world.LastDrawCommands.ToString("N0"));
                EditorGUILayout.LabelField("Shared meshes / draw groups", $"{world.PrototypeCount:N0} / {world.DrawGroupCount:N0}");
                EditorGUILayout.LabelField("Cell LOD families", world.CellLodFamilyCount.ToString("N0"));
                EditorGUILayout.LabelField("LOD decisions (last camera)", world.LastLodEvaluations.ToString("N0"));
                EditorGUILayout.LabelField("Instance buffer", $"{world.InstanceBufferBytes / 1048576.0:F2} MiB");
                EditorGUILayout.LabelField("Normal-renderer fallbacks", world.FallbackCount.ToString("N0"));
                foreach (var item in world.FallbackReasons)
                    EditorGUILayout.LabelField(item.Key, item.Value.ToString("N0"));
                EditorGUILayout.HelpBox("Counters refresh four times per second. Last-camera counts are from the latest camera cull, not total frame draw calls; normal-renderer fallbacks are excluded.", MessageType.None);
            }
            EditorGUILayout.Space();
        }
        public static MGSurfaceVertexAO.Settings BakeSettings(MGSurfaceWorld world) => new MGSurfaceVertexAO.Settings
        { samples = world.cookAOSamples, relativeDistance = world.cookAOReach };
        void Property(string name, string label) => EditorGUILayout.PropertyField(serializedObject.FindProperty(name), new GUIContent(label), true);
        void Run(Func<string> action)
        {
            try { status = action(); statusType = MessageType.Info; }
            catch (OperationCanceledException) { status = "Cook cancelled; scene assignments restored. Generated library assets remain available."; statusType = MessageType.Info; }
            catch (Exception ex) { status = ex.Message; statusType = MessageType.Error; Debug.LogException(ex); }
        }
    }
}
