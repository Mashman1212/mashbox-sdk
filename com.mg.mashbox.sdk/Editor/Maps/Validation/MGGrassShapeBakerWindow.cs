using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed class MGGrassShapeBakerWindow : EditorWindow
    {
        [SerializeField] MGGrassShapeSet recipe;
        [SerializeField] Material material;
        Vector2 scroll;
        string status;
        MessageType statusType;

        [MenuItem("Tools/MashBox/MG Terrain/Grass Shape Baker")]
        public static void Open() => GetWindow<MGGrassShapeBakerWindow>("Grass Shape Baker");
        public static void Open(MGGrassShapeSet selected)
        {
            var window = GetWindow<MGGrassShapeBakerWindow>("Grass Shape Baker");
            window.recipe = selected;
        }
        void OnEnable()
        {
            if (recipe == null && Selection.activeObject is MGGrassShapeSet selected) recipe = selected;
        }
        void OnGUI() => Draw();
        public void Draw()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            try
            {
                EditorGUILayout.LabelField("Grass Shape Baker", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("One FBX, one shared mesh, eight painted IDs. Each ID selects both its shape and the matching texture-array layer. Empty slots repeat shape 0. Positions and UVs are sampled per vertex before wind; normals use the material's existing controls.", MessageType.Info);
                recipe = (MGGrassShapeSet)EditorGUILayout.ObjectField("Shape Set", recipe, typeof(MGGrassShapeSet), false);
                if (GUILayout.Button("New Shape Set"))
                {
                    string path = EditorUtility.SaveFilePanelInProject("Create Grass Shape Set", "GrassShapes", "asset", "Choose the output folder.");
                    if (!string.IsNullOrEmpty(path))
                    {
                        var created = CreateInstance<MGGrassShapeSet>();
                        AssetDatabase.CreateAsset(created, path); recipe = created;
                    }
                }
                if (recipe == null) return;
                var serialized = new SerializedObject(recipe);
                serialized.Update();
                EditorGUILayout.PropertyField(serialized.FindProperty("sourceFbx"), new GUIContent("Source FBX"));
                EditorGUILayout.PropertyField(serialized.FindProperty("bakeRotationAndScale"), new GUIContent("Bake Rotation and Scale"));
                EditorGUILayout.PropertyField(serialized.FindProperty("boundsPadding"), new GUIContent("Extra Bounds Padding"));
                EditorGUILayout.PropertyField(serialized.FindProperty("bakeVertexAO"),new GUIContent("Bake Vertex AO from Red"));
                serialized.ApplyModifiedProperties();
                EditorGUILayout.HelpBox("Keep the MGShapeID second UV set from Maya. Mesh names map explicitly to IDs below. Display translations are ignored; rotations/scales are baked by default. Freeze negative scales before export. Add enough bounds padding for wind and distance scaling.", MessageType.None);
                if (recipe.sourceFbx != null)
                {
                    var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(recipe.sourceFbx)) as ModelImporter;
                    if (importer != null && (!importer.isReadable || importer.generateSecondaryUV || importer.meshCompression != ModelImporterMeshCompression.Off))
                    {
                        EditorGUILayout.HelpBox("The FBX needs Read/Write, no mesh compression, and no generated lightmap UVs to preserve its shape IDs.", MessageType.Warning);
                        if (GUILayout.Button("Prepare FBX Import Settings"))
                        {
                            importer.isReadable = true; importer.generateSecondaryUV = false;
                            importer.meshCompression = ModelImporterMeshCompression.Off;
                            importer.SaveAndReimport();
                        }
                    }
                    var paths = recipe.sourceFbx.GetComponentsInChildren<MeshFilter>(true)
                        .Select(f => MGGrassShapeBaker.PathFor(recipe.sourceFbx.transform, f.transform)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
                    if (GUILayout.Button("Assign Meshes by Name Order"))
                    {
                        Undo.RecordObject(recipe, "Assign Grass Shapes");
                        recipe.shapePaths = new string[8];
                        for (int i = 0; i < Math.Min(paths.Length, 8); i++) recipe.shapePaths[i] = paths[i];
                        EditorUtility.SetDirty(recipe);
                    }
                    if (paths.Length > 8) EditorGUILayout.HelpBox("The FBX contains more than eight meshes. Choose which eight to use.", MessageType.Warning);
                    for (int i = 0; i < 8; i++)
                    {
                        string current = recipe.shapePaths != null && i < recipe.shapePaths.Length ? recipe.shapePaths[i] ?? "" : "";
                        var choices = new[] { "" }.Concat(paths).ToList();
                        if (!choices.Contains(current)) choices.Add(current);
                        var labels = choices.Select(p => p == "" ? (i == 0 ? "Assign base shape" : "Repeat shape 0") : p + (paths.Contains(p) ? "" : " (missing)")).ToArray();
                        int index = choices.IndexOf(current);
                        EditorGUI.BeginChangeCheck();
                        int selected = EditorGUILayout.Popup("Painted ID " + i, index, labels);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(recipe, "Assign Grass Shape ID");
                            if (recipe.shapePaths == null || recipe.shapePaths.Length != 8) Array.Resize(ref recipe.shapePaths, 8);
                            recipe.shapePaths[i] = choices[selected]; EditorUtility.SetDirty(recipe);
                        }
                    }
                }
                if (GUILayout.Button("Validate Matching IDs and Topology")) Run(() =>
                {
                    var shapes = MGGrassShapeBaker.ReadSource(recipe);
                    status = $"Valid: {shapes[0].positions.Length} shared vertices, {shapes[0].triangles.Length / 3} triangles.";
                });
                if (GUILayout.Button("Bake / Rebuild Position and UV Arrays", GUILayout.Height(28))) Run(() =>
                {
                    MGGrassShapeBaker.Bake(recipe);
                    status = $"Baked {recipe.bakedMesh.vertexCount} vertices into {recipe.positions.width} x {recipe.positions.height} x 8 arrays. Rebuilds preserve asset references.";
                });
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField("Shared Mesh", recipe.bakedMesh, typeof(Mesh), false);
                    EditorGUILayout.ObjectField("Positions", recipe.positions, typeof(Texture2DArray), false);
                    EditorGUILayout.ObjectField("UVs", recipe.uvs, typeof(Texture2DArray), false);
                    EditorGUILayout.ObjectField("Vertex AO (R8)", recipe.vertexAO, typeof(Texture2DArray), false);
                }
                EditorGUILayout.Space();
                material = (Material)EditorGUILayout.ObjectField("GodGrass Material", material, typeof(Material), false);
                using (new EditorGUI.DisabledScope(material == null || recipe.bakedMesh == null))
                {
                    if (GUILayout.Button("Apply Shape Arrays to Material")) Run(() =>
                    { MGGrassShapeBaker.Apply(recipe, material); status = "Shape arrays applied. Match the texture layer numbers to the shape IDs."; });
                    if (GUILayout.Button("Create Single-Layer Detail Prefab")) Run(() =>
                    {
                        string path = EditorUtility.SaveFilePanelInProject("Save Grass Shape Prefab", "GrassShapes_Detail", "prefab", "Assign this prefab to one terrain detail layer.");
                        if (string.IsNullOrEmpty(path)) return;
                        MGGrassShapeBaker.Apply(recipe, material);
                        var go = new GameObject("GrassShapes_Detail");
                        try
                        {
                            go.AddComponent<MeshFilter>().sharedMesh = recipe.bakedMesh;
                            go.AddComponent<MeshRenderer>().sharedMaterial = material;
                            Selection.activeObject = PrefabUtility.SaveAsPrefabAsset(go, path);
                        }
                        finally { DestroyImmediate(go); }
                        status = "Prefab saved. Assign it to one detail layer and paint Sub-IDs 0–7.";
                    });
                }
                if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, statusType);
            }
            finally { EditorGUILayout.EndScrollView(); }
        }
        void Run(Action action)
        {
            try { statusType = MessageType.Info; action(); }
            catch (Exception e) { statusType = MessageType.Error; status = e.Message; Debug.LogException(e); }
        }
    }

    [CustomEditor(typeof(MGGrassShapeSet))]
    sealed class MGGrassShapeSetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (GUILayout.Button("Open Grass Shape Baker")) MGGrassShapeBakerWindow.Open((MGGrassShapeSet)target);
        }
    }

    public static class MGGrassShapeMaterialGUI
    {
        static bool expanded;
        public static void Draw(MaterialEditor editor, Material material)
        {
            if (material == null || !material.HasProperty("_GrassUseShapes")) return;
            expanded = EditorGUILayout.Foldout(expanded, "Grass Shape Layers (Sub-IDs 0–7)", true);
            if (!expanded) return;
            var properties = MaterialEditor.GetMaterialProperties(editor.targets);
            foreach (var name in new[] { "_GrassUseShapes", "_GrassShapePositions", "_GrassShapeUVs", "_GrassUseShapeAO", "_GrassShapeAO" })
            {
                var property = properties.FirstOrDefault(p => p.name == name);
                if (property != null) editor.ShaderProperty(property, name == "_GrassUseShapes" ? "Use Shape Arrays" : name == "_GrassShapePositions" ? "Position Array" : name == "_GrassShapeUVs" ? "UV Array" : name == "_GrassUseShapeAO" ? "Use Shape AO Array" : "Vertex AO Array (R8)");
            }
            EditorGUILayout.HelpBox("Use the baker's shared mesh. Shape and texture layer use the same painted Sub-ID; the Default / Preview Sub-ID above previews both. Normals remain controlled by Normal Up / Spherical Blend.", MessageType.None);
            if (GUILayout.Button("Open Grass Shape Baker")) MGGrassShapeBakerWindow.Open();
        }
    }
}
