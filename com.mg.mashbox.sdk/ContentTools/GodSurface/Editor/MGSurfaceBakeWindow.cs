using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MG.GodSurface.Editor
{
    public sealed class MGSurfaceBakeWindow : EditorWindow
    {
        MGSurfaceWorld world;
        MGSurfaceLibrary library;
        int resolution = 1024;
        bool compress = true;
        [SerializeField] MGSurfaceVertexAO.Settings ao = new MGSurfaceVertexAO.Settings();
        string status;
        Vector2 scroll;
#if MashBoxDev
        [MenuItem("MashBox/Dev/Content Tools/God Surface Library Baker")]
#endif
        public static void Open() => GetWindow<MGSurfaceBakeWindow>("God Surface Baker");
        void OnEnable() { minSize = new Vector2(580, 500); world = FindFirstObjectByType<MGSurfaceWorld>(); if (world) library = world.surfaceLibrary; }
        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Shared surface library", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Bake MG Lit Basic materials into shared texture arrays. Each surface keeps its colour, UV tiling, detail, decal and emission settings. Repeated texture references share slices independently in each channel. Other shaders keep their current materials.", MessageType.Info);
            world = (MGSurfaceWorld)EditorGUILayout.ObjectField("World", world, typeof(MGSurfaceWorld), true);
            resolution = EditorGUILayout.IntPopup("Maximum array size", resolution, new[] { "128", "256", "512", "1024", "2048" }, new[] { 128, 256, 512, 1024, 2048 });
            compress = EditorGUILayout.Toggle("BC7 compression", compress);
            EditorGUILayout.HelpBox("BC7 targets supported desktop GPUs. A bake creates a new version; existing library assets remain available. Inspect the result before applying it to the scene.", MessageType.None);
            using (new EditorGUI.DisabledScope(!world))
                if (GUILayout.Button("Bake new library")) Run(() => { library = MGSurfaceBaker.Bake(world, resolution, compress); status = "Library baked. Source texture/material dependency check passed."; });
            using (new EditorGUI.DisabledScope(!world))
                if (GUILayout.Button("Audit shared-topology morph families")) Run(() => { status = MGSurfaceMeshAudit.Report(world); Debug.Log(status); });
            library = (MGSurfaceLibrary)EditorGUILayout.ObjectField("Baked library", library, typeof(MGSurfaceLibrary), false);
            if (library)
            {
                EditorGUILayout.LabelField("Surfaces / banks", library.banks.Sum(b => b.surfaceNames.Length) + " / " + library.banks.Length);
                EditorGUILayout.LabelField("GPU texture payload", (library.TexturePayloadBytes / 1048576.0).ToString("F2") + " MiB (includes mipmaps)");
                for (int i = 0; i < library.banks.Length; i++)
                {
                    EditorGUILayout.LabelField("Bank " + i + " — " + library.banks[i].surfaceNames.Length + " surfaces", EditorStyles.boldLabel);
                    var channels = new[] { "Base", "Normal", "Mask", "Emission", "Detail", "Decal" };
                    EditorGUILayout.LabelField(string.Join("  |  ", library.banks[i].arrays.Select((a, channel) => channels[channel] + ": " + a.depth + " × " + a.width)), EditorStyles.wordWrappedLabel);
                    if (GUILayout.Button("Select bank " + i + " material")) Selection.activeObject = library.banks[i].sharedMaterial;
                }
            }
            using (new EditorGUI.DisabledScope(!world || !library))
                if (GUILayout.Button("Apply library to world (Undo supported)")) Run(() => status = MGSurfaceBaker.Apply(world, library).ToString("N0") + " renderer material assignments updated. Save the scene when ready.");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Prop vertex AO", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Bake each unique prop mesh and LOD once, then share it across placements. AO uses vertex red, with the same strength response as God Grass. Green, blue and alpha are preserved. Source meshes and collider meshes stay unchanged. Self-occlusion only: nearby props and terrain are not baked in.", MessageType.None);
            ao.samples = EditorGUILayout.IntSlider("AO samples", ao.samples, 16, 1024);
            ao.relativeDistance = EditorGUILayout.Slider("Reach / mesh size", ao.relativeDistance, .001f, 1);
            ao.bias = Mathf.Max(0, EditorGUILayout.FloatField("Surface bias", ao.bias));
            ao.falloff = EditorGUILayout.Slider("Distance falloff", ao.falloff, 0, 4);
            ao.twoSided = EditorGUILayout.Toggle("Two-sided occluders", ao.twoSided);
            using (new EditorGUI.DisabledScope(!world || !world.surfaceLibrary || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Bake and apply shared vertex AO (Undo supported)"))
                    Run(() => status = MGSurfaceVertexAO.BakeWorld(world, ao));
            if (world && world.surfaceLibrary)
            {
                var aoMaterials = MGSurfaceVertexAO.Materials(world.surfaceLibrary);
                if (aoMaterials.Length > 0)
                {
                    float strength = aoMaterials[0].GetFloat(MGSurfaceVertexAO.Strength);
                    EditorGUI.showMixedValue = aoMaterials.Any(m => !Mathf.Approximately(m.GetFloat(MGSurfaceVertexAO.Strength), strength));
                    EditorGUI.BeginChangeCheck();
                    strength = EditorGUILayout.Slider("Vertex AO Strength (all banks)", strength, 0, 1);
                    if (EditorGUI.EndChangeCheck()) MGSurfaceVertexAO.SetStrength(world, world.surfaceLibrary, strength);
                    EditorGUI.showMixedValue = false;
                    EditorGUILayout.HelpBox("0 = texture AO only; 1 = full baked vertex AO contribution. Each bank material also exposes Vertex AO Strength in its shader inspector.", MessageType.None);
                }
            }
            using (new EditorGUI.DisabledScope(!world || !world.surfaceLibrary))
                if (GUILayout.Button("Restore source materials")) Run(() => status = MGSurfaceBaker.Restore(world).ToString("N0") + " renderer materials restored.");
            using (new EditorGUI.DisabledScope(!library || !Selection.activeGameObject || EditorUtility.IsPersistent(Selection.activeGameObject)))
                if (GUILayout.Button("Export selected prop as a reusable prefab")) Run(() => { var prefab = MGSurfacePrefabExporter.Export(Selection.activeGameObject, library); status = "Created " + AssetDatabase.GetAssetPath(prefab) + ". Place it under one of the manager's listed Prop Roots to join the cell renderer."; Selection.activeObject = prefab; });
            using (new EditorGUI.DisabledScope(!library))
                if (GUILayout.Button("Validate library rendering")) Run(() => { status = MGSurfaceLibraryValidation.Run(library); Debug.Log(status); });
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.Info);
            EditorGUILayout.EndScrollView();
        }
        void Run(Action action) { try { action(); } catch (Exception ex) { status = ex.Message; Debug.LogException(ex); } }
    }
}
