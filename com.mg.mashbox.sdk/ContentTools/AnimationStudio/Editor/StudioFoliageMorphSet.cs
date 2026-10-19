using System;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    [CreateAssetMenu(menuName = "MashBox Studio/Foliage Morph Set")]
    public sealed class StudioFoliageMorphSet : ScriptableObject
    {
        [Serializable] public sealed class Source
        {
            public string name;
            public Mesh mesh;
            public Vector3 scale = Vector3.one;
            public Vector3 rotation;
        }
        public Source[] sources = new Source[8];
        [Min(128)] public int vertexBudget = 3000;
        [Range(5,12)] public int foliageBoundaryVertices = 8;
        [Range(6,24)] public int trunkSides = 12;
        [Range(5,32)] public int trunkRings = 17;
        [Range(3,10)] public int branchSides = 6;
        [Range(2,8)] public int branchRings = 5;
        public int barkSubmesh = 0, foliageSubmesh = 1;
        public Material barkMaterial, foliageMaterial;
        [Tooltip("Base, tangent-space normal, HDRP mask, thickness. Explicit maps override material lookup.")]
        public Texture2D[] barkMaps = new Texture2D[4], foliageMaps = new Texture2D[4];
        public Vector2 barkBaseTiling = Vector2.one;
        public Vector2 barkBaseOffset;
        public Color barkTint = Color.white, foliageTint = Color.white;
        public int atlasSize = 2048;
        public StudioModelDocument document;
        public Texture2D[] atlases = new Texture2D[4];
        [TextArea(3,20)] public string report;
    }

    [CustomEditor(typeof(StudioFoliageMorphSet))]
    sealed class StudioFoliageMorphSetInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var recipe = (StudioFoliageMorphSet)target;
            EditorGUILayout.HelpBox("Builds editable shared-topology shapes from triangle meshes with separate wood and foliage submeshes. Source meshes and scenes are untouched. Rebuilding replaces the generated workspace meshes; save a workspace copy to keep manual edits.", MessageType.Info);
            if (GUILayout.Button("Build Shared Topology in Studio"))
                try { StudioFoliageMorphBuilder.Build(recipe); AnimationStudioWindow.OpenModelDocument(recipe.document); }
                catch (Exception ex) { Debug.LogException(ex); EditorUtility.DisplayDialog("Foliage Morph Set",ex.Message,"OK"); }
            if (recipe.document && GUILayout.Button("Open Editable Shapes in Studio")) AnimationStudioWindow.OpenModelDocument(recipe.document);
        }
    }
}
