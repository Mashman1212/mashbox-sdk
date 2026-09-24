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
        // No update/tool-exit/reload autosaves: importing mesh assets can stall the editor.
        // Meshes and scenes stay dirty until the user saves through Unity.
        static MGTerrainDirectSculpt()
        {
            MeshSculptModifier.PrepareTerrainEdit += Prepare;
            Undo.undoRedoPerformed += RefreshUndo;

        }

        internal static void Prepare(MeshSculptModifier modifier) => Prepare(modifier, true);

        internal static void Prepare(MeshSculptModifier modifier, bool clearStrokes)
        {
            var filter = modifier.Target;
            var terrain = filter.GetComponentInParent<MGTerrain>(true);
            if (terrain == null || terrain.MeshFilter != filter || filter.sharedMesh == null) return;
            EditedTerrains.Add(terrain);
            int group = Undo.GetCurrentGroup();
            if (!UndoGroups.TryGetValue(terrain, out int previous) || previous != group)
            {
                MGTerrainGridRepair.Repair(terrain);
                Mesh mesh = filter.sharedMesh;
                // Undo must reference a persistent pre-stroke mesh. Legacy sculpt outputs
                // are scene-only objects and can disappear when Undo restores references.
                if (!AssetDatabase.Contains(mesh))
                {
                    mesh.hideFlags = HideFlags.None;
                    MGTerrainSceneAssets.Create(mesh, terrain, "BeforeSculpt");
                    // CreateAsset already persisted the pre-stroke geometry.
                }
                bool shared = Object.FindObjectsByType<MGTerrain>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Any(t => t != terrain && t.MeshFilter != null && t.MeshFilter.sharedMesh == mesh);
                if (terrain.EditableSculptMesh != mesh || !AssetDatabase.Contains(mesh) || shared)
                {
                    // Adopt the visible result, never rebuild/reapply the legacy strokes.
                    var editable = Object.Instantiate(mesh);
                    editable.name = terrain.name + " Sculpted";
                    editable.hideFlags = HideFlags.None;
                    MGTerrainSceneAssets.Create(editable, terrain, "SculptedMesh");
                    Undo.RegisterCompleteObjectUndo(filter, "Sculpt Terrain");
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
            if (clearStrokes) modifier.ClearStrokes();
            const HideFlags workerFlags = HideFlags.HideInInspector | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            // Reassigning even identical flags invalidates Unity's gizmo registry.
            if (modifier.hideFlags != workerFlags) modifier.hideFlags = workerFlags;
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
                if (terrain.MeshFilter == null || terrain.MeshFilter.sharedMesh == null) continue;
                terrain.NotifySurfaceMeshChanged();
                terrain.RefreshSurfaceCollidersFromMesh();
            }
            UndoGroups.Clear();
            // Keep Undo changes dirty in memory for Unity's normal explicit save workflow.
            foreach (var terrain in EditedTerrains)
                if (terrain != null && terrain.MeshFilter != null && terrain.MeshFilter.sharedMesh != null)
                    EditorUtility.SetDirty(terrain.MeshFilter.sharedMesh);
            SceneView.RepaintAll();
        }
    }
}
#endif
