#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps.Sculpting;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    [InitializeOnLoad]
    internal static class MGTerrainDirectSculpt
    {
        static readonly Dictionary<MGTerrain, int> UndoGroups = new Dictionary<MGTerrain, int>();
        static readonly HashSet<MGTerrain> EditedTerrains = new HashSet<MGTerrain>();
        static MGTerrainDirectSculpt()
        {
            MeshSculptModifier.PrepareTerrainEdit += Prepare;
            Undo.undoRedoPerformed += RefreshUndo;
        }

        static void Prepare(MeshSculptModifier modifier)
        {
            var filter = modifier.Target;
            var terrain = filter.GetComponentInParent<MGTerrain>();
            if (terrain == null || terrain.MeshFilter != filter || filter.sharedMesh == null) return;
            EditedTerrains.Add(terrain);
            int group = Undo.GetCurrentGroup();
            if (!UndoGroups.TryGetValue(terrain, out int previous) || previous != group)
            {
                Mesh mesh = filter.sharedMesh;
                bool shared = Object.FindObjectsByType<MGTerrain>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Any(t => t != terrain && t.MeshFilter != null && t.MeshFilter.sharedMesh == mesh);
                if (terrain.EditableSculptMesh != mesh || !AssetDatabase.Contains(mesh) || shared)
                {
                    // Adopt the visible result, never rebuild/reapply the legacy strokes.
                    var editable = Object.Instantiate(mesh);
                    editable.name = terrain.name + " Sculpted";
                    editable.hideFlags = HideFlags.None;
                    MGTerrainSceneAssets.Create(editable, terrain, "SculptedMesh");
                    Undo.RecordObject(filter, "Sculpt Terrain");
                    Undo.RecordObject(terrain, "Sculpt Terrain");
                    filter.sharedMesh = editable;
                    using var data = new SerializedObject(terrain);
                    data.FindProperty("m_EditableSculptMesh").objectReferenceValue = editable;
                    data.ApplyModifiedProperties();
                    mesh = editable;
                }
                Undo.RegisterCompleteObjectUndo(mesh, "Sculpt Terrain");
                UndoGroups[terrain] = group;
            }
            // The brush uses this worker only during editor sessions. Mesh assets carry the result.
            modifier.ClearStrokes();
            modifier.hideFlags = HideFlags.HideInInspector | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            EditorUtility.SetDirty(filter.sharedMesh);
            EditorUtility.SetDirty(filter);
            EditorUtility.SetDirty(terrain);
            if (terrain.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        }

        static void RefreshUndo()
        {
            foreach (var terrain in EditedTerrains.ToArray())
            {
                if (terrain == null) { EditedTerrains.Remove(terrain); continue; }
                terrain.NotifySurfaceMeshChanged();
                terrain.RefreshSurfaceCollidersFromMesh();
            }
            UndoGroups.Clear();
            SceneView.RepaintAll();
        }
    }
}
#endif
