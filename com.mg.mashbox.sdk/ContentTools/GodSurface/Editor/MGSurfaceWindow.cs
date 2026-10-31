using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MG.GodSurface.Editor
{
    public sealed class MGSurfaceWindow : EditorWindow
    {
        MGSurfaceWorld world;
        GameObject targetRoot;
        Vector2 scroll;
        string auditSummary;
        [MenuItem("MashBox/Content Tools/God Surface Cells")]
        public static void Open() => GetWindow<MGSurfaceWindow>("God Surface Cells");
        void OnEnable() { minSize = new Vector2(600, 650); FindWorld(); EditorApplication.update += RefreshStats; }
        void OnDisable() => EditorApplication.update -= RefreshStats;
        double nextStatsRefresh;
        void RefreshStats()
        {
            if (EditorApplication.timeSinceStartup < nextStatsRefresh) return;
            nextStatsRefresh = EditorApplication.timeSinceStartup + .25; Repaint();
        }
        void FindWorld()
        {
            world = FindFirstObjectByType<MGSurfaceWorld>();
            if (world) targetRoot = world.gameObject;
            else foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) if (root.name == "WORLD AND PROPS") targetRoot = root;
        }
        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Automatic spatial prop rendering", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Place props as usual. The registry shares meshes and surface materials, assigns spatial cells and preserves colliders. Per Cell Family switches matching LOD mesh sets together inside each cell. Geometry morph baking is a separate stage.", MessageType.Info);
            EditorGUI.BeginChangeCheck();
            targetRoot = (GameObject)EditorGUILayout.ObjectField("Manager object", targetRoot, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck()) { world = targetRoot ? targetRoot.GetComponent<MGSurfaceWorld>() : null; auditSummary = null; }
            if (!world && targetRoot) world = targetRoot.GetComponent<MGSurfaceWorld>();
            if (!world)
            {
                using (new EditorGUI.DisabledScope(!targetRoot || EditorUtility.IsPersistent(targetRoot)))
                    if (GUILayout.Button("Create or select the scene prop manager")) world = Setup(targetRoot);
                EditorGUILayout.EndScrollView(); return;
            }
            var so = new SerializedObject(world); so.Update();
            EditorGUILayout.PropertyField(so.FindProperty("cellSize")); EditorGUILayout.PropertyField(so.FindProperty("roots"), true);
            EditorGUILayout.PropertyField(so.FindProperty("renderInPlayMode")); EditorGUILayout.PropertyField(so.FindProperty("previewInEditor"));
            EditorGUILayout.PropertyField(so.FindProperty("cellDrawDistance"), new GUIContent("Cell Draw Distance (m)"));
            EditorGUILayout.HelpBox("Entire cells stop drawing at this distance from the camera to their nearest bounds, including props without LODGroups. Colliders stay active. Normal-renderer fallbacks are outside the cell renderer.", MessageType.None);
            EditorGUILayout.PropertyField(so.FindProperty("drawCellBounds"));
            EditorGUILayout.PropertyField(so.FindProperty("lodSelection"));
            if (world.drawCellBounds) EditorGUILayout.HelpBox("Cell outlines: cyan = drawing; orange = some props beyond their final LOD cutoff; red = cell draw distance reached or all props beyond their final LOD cutoff; grey = not drawn or awaiting camera data. Colours use this Scene view camera. Outlines remain visible with Gizmos off.", MessageType.None);
            if (world.lodSelection == MGSurfaceWorld.LodSelection.PerCellFamily)
                EditorGUILayout.HelpBox("Matching LOD mesh sets switch together per cell. Near members keep the whole family detailed, which can render more triangles than Per Prop. Props in different cells switch independently; normal-renderer fallbacks retain their original LOD behaviour.", MessageType.None);
            EditorGUILayout.PropertyField(so.FindProperty("surfaceLibrary"));
            so.ApplyModifiedProperties(); // OnValidate rebuilds only when registry inputs change.
            EditorGUILayout.HelpBox("Edit normally with Preview In Editor off. Preview temporarily hides normal renderers and can affect scene picking. Disabling preview or the component restores them. Movable runtime prefabs should carry MGSurfaceProp; scripts changing renderer state call RequestRebuild().", MessageType.None);
            if (!string.IsNullOrEmpty(world.LastError)) EditorGUILayout.HelpBox(world.LastError, MessageType.Warning);
            if (EditorGraphicsSettings.batchRendererGroupShaderStrippingMode != BatchRendererGroupStrippingMode.KeepAll)
                EditorGUILayout.HelpBox("Before a player build: Project Settings > Graphics > Shader Stripping > BatchRendererGroup Variants must be Keep All. The build guard prevents silently stripping this renderer's shader variants.", MessageType.Warning);
            if (GUILayout.Button("Rebuild registry")) world.Rebuild();
            if (GUILayout.Button("Audit eligibility (without changing rendering)")) auditSummary = Audit(world);
            if (!string.IsNullOrEmpty(auditSummary)) EditorGUILayout.HelpBox(auditSummary, MessageType.Info);
            EditorGUILayout.Space();
            if (!world.IsRendering) EditorGUILayout.HelpBox("Rendering is inactive in this view. Enable Preview In Editor or enter Play mode to populate the live counters below.", MessageType.None);
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 280;
            EditorGUILayout.LabelField("Registered instances", world.InstanceCount.ToString("N0"));
            EditorGUILayout.LabelField("Occupied cells", world.CellCount.ToString("N0"));
            EditorGUILayout.LabelField("Cell LOD families / last decisions", $"{world.CellLodFamilyCount:N0} / {world.LastLodEvaluations:N0}");
            EditorGUILayout.LabelField("Shared meshes / groups", $"{world.PrototypeCount:N0} / {world.DrawGroupCount:N0}");
            EditorGUILayout.LabelField("Instance buffer", $"{world.InstanceBufferBytes / 1048576.0:F2} MiB");
            EditorGUILayout.LabelField("Last camera: cells / props / draws", $"{world.LastVisibleCells:N0} / {world.LastVisibleInstances:N0} / {world.LastDrawCommands:N0}");
            EditorGUILayout.LabelField("Normal-renderer fallbacks", world.FallbackCount.ToString("N0"));
            foreach (var item in world.FallbackReasons) EditorGUILayout.LabelField(item.Key, item.Value.ToString("N0"));
            EditorGUIUtility.labelWidth = previousLabelWidth;
            EditorGUILayout.Space();
            if (GUILayout.Button("Select world registry")) Selection.activeGameObject = world.gameObject;
            if (GUILayout.Button("Remove registry and restore original rendering")) { world.Release(); Undo.DestroyObjectImmediate(world); world = null; }
            EditorGUILayout.EndScrollView();
        }
        public static MGSurfaceWorld Setup(GameObject root)
        {
            if (!root || EditorUtility.IsPersistent(root)) throw new ArgumentException("Choose a scene root.");
            var world = MGSurfaceWorld.ForScene(root.scene);
            if (world) { Selection.activeGameObject = world.gameObject; return world; }
            var manager = new GameObject("PROP CELLS");
            SceneManager.MoveGameObjectToScene(manager, root.scene);
            Undo.RegisterCreatedObjectUndo(manager, "Create prop cell manager");
            world = Undo.AddComponent<MGSurfaceWorld>(manager);
            Selection.activeGameObject = manager;
            return world;
        }

        static string Audit(MGSurfaceWorld world)
        {
            bool wasRendering = world.IsRendering; world.Release();
            int eligible = 0; var reasons = new Dictionary<string, int>();
            foreach (var r in world.EnumerateRenderers())
            {
                string reason = world.UnsupportedReason(r); if (reason == null) eligible++;
                else { reasons.TryGetValue(reason, out int n); reasons[reason] = n + 1; }
            }
            string text = $"God Surface pilot: {eligible:N0} eligible MeshRenderers.";
            foreach (var item in reasons) text += $"\n{item.Key}: {item.Value:N0}";
            Debug.Log(text, world); if (wasRendering) world.RequestRebuild();
            return text;
        }
    }

    [InitializeOnLoad]
    static class MGSurfaceEditorLifecycle
    {
        static MGSurfaceEditorLifecycle()
        {
            // Runtime pools (including terrain collision) must not rebuild every static prop.
            // Movable/runtime props notify their manager through MGSurfaceProp.
            EditorApplication.hierarchyChanged += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) Dirty(); };
            Undo.undoRedoPerformed += Dirty;
            Undo.postprocessModifications += modifications =>
            {
                if (EditorApplication.isPlaying) return modifications;
                foreach (var change in modifications)
                {
                    var value = change.currentValue;
                    if (value.target is MGSurfaceWorld && (value.propertyPath == "drawCellBounds" || value.propertyPath == "cellDrawDistance" || value.propertyPath == "lodSelection")) continue;
                    Dirty(); break;
                }
                return modifications;
            };
            AssemblyReloadEvents.beforeAssemblyReload += Restore;
            EditorApplication.playModeStateChanged += _ => { Restore(); Dirty(); };
            EditorSceneManager.sceneSaving += (_, __) => Restore();
            EditorSceneManager.sceneSaved += _ => Dirty();
        }
        static void Dirty() { foreach (var world in UnityEngine.Object.FindObjectsByType<MGSurfaceWorld>(FindObjectsSortMode.None)) world.RequestRebuild(); }
        static void Restore() { foreach (var world in UnityEngine.Object.FindObjectsByType<MGSurfaceWorld>(FindObjectsSortMode.None)) world.Release(); }
    }

    // Runs on Unity's build-scene copy, not the artist's scene. Prevents geometry duplication before static batching.
    sealed class MGSurfaceBuildScene : IProcessSceneWithReport
    {
        public int callbackOrder => 100;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            MGSurfaceSingleManager.Validate(scene);
            foreach (var root in scene.GetRootGameObjects()) foreach (var world in root.GetComponentsInChildren<MGSurfaceWorld>(true))
            {
                world.Release(); if (!world.enabled || !world.renderInPlayMode) continue;
                if (report != null && EditorGraphicsSettings.batchRendererGroupShaderStrippingMode != BatchRendererGroupStrippingMode.KeepAll)
                    throw new BuildFailedException("God Surface requires Graphics > Shader Stripping > BatchRendererGroup Variants = Keep All.");
                foreach (var renderer in world.EnumerateRenderers())
                    if (world.UnsupportedReason(renderer, false) == null)
                        GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & ~StaticEditorFlags.BatchingStatic);
            }
        }
    }
}
