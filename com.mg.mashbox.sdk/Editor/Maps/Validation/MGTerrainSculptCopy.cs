#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainSculptCopy
    {
        const string Label = "Use Scene Sculpt Copy";

        internal static void Create(IEnumerable<MGTerrain> selected)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before copying terrain sculpt meshes.");
            var tiles = selected.Distinct().ToArray();
            if (tiles.Length == 0) throw new InvalidOperationException("Select at least one terrain tile.");
            foreach (var tile in tiles)
            {
                if (tile == null || EditorUtility.IsPersistent(tile) || !tile.gameObject.scene.IsValid()
                    || !tile.gameObject.scene.isLoaded || EditorSceneManager.IsPreviewScene(tile.gameObject.scene))
                    throw new InvalidOperationException("Choose terrain tiles in an open editable scene.");
                if (tile.MeshFilter == null || tile.MeshFilter.sharedMesh == null || !tile.MeshFilter.sharedMesh.isReadable)
                    throw new InvalidOperationException("Every selected tile needs a readable sculpt mesh.");
            }
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName(Label);
            try
            {
                foreach (var tile in tiles)
                {
                    var filter = tile.MeshFilter;
                    var copy = Object.Instantiate(filter.sharedMesh); copy.hideFlags = HideFlags.None;
                    copy.name = tile.name + " Scene Sculpt Copy";
                    Undo.RegisterCreatedObjectUndo(copy, Label);
                    Undo.RecordObject(tile, Label);
                    using (var data = new SerializedObject(tile))
                    {
                        data.FindProperty("m_SceneSculptCopy").boolValue = true;
                        data.ApplyModifiedPropertiesWithoutUndo();
                    }
                    Undo.FlushUndoRecordObjects();
                    MGTerrainMergeSession.DetachColliders(tile, Label);
                    Undo.RecordObject(filter, Label); Undo.RecordObject(tile, Label);
                    if (tile.MeshCollider != null) Undo.RecordObject(tile.MeshCollider, Label);
                    filter.sharedMesh = copy;
                    using (var data = new SerializedObject(tile))
                    {
                        data.FindProperty("m_EditableSculptMesh").objectReferenceValue = copy;
                        data.ApplyModifiedPropertiesWithoutUndo();
                    }
                    tile.NotifySurfaceMeshChanged(); tile.RefreshSurfaceCollidersFromMesh();
                    MGTerrainMergeUndo.Track(tile);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(tile);
                    if (tile.MeshCollider != null) PrefabUtility.RecordPrefabInstancePropertyModifications(tile.MeshCollider);
                    EditorUtility.SetDirty(filter); EditorUtility.SetDirty(tile);
                    EditorSceneManager.MarkSceneDirty(tile.gameObject.scene);
                    // Flush reference changes before recording the next tile or grouping Undo.
                    Undo.FlushUndoRecordObjects();
                }
                Undo.CollapseUndoOperations(group);
            }
            catch
            {
                Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(group);
                throw;
            }
            EditorApplication.QueuePlayerLoopUpdate(); SceneView.RepaintAll();
        }
    }

    public sealed partial class MGTerrainEditor
    {
        string m_SculptCopyStatus;
        void DrawIndependentSculptCopy()
        {
            var tiles = targets.OfType<MGTerrain>().ToArray();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Scene Sculpt Copy", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Keep this tile's shape in your scene and sculpt it independently. The mesh and its collision copies save inside the scene, without creating separate asset files.", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button(tiles.All(t => t.SceneSculptCopy) ? "Make Fresh Scene Sculpt Copy" : "Use Scene Sculpt Copy")) MakeSculptCopies(tiles);
                }
                EditorGUILayout.LabelField("Sculpt geometry only. Texture/control maps, materials and vegetation are not duplicated.", EditorStyles.wordWrappedMiniLabel);
                if (tiles.All(t => t.SceneSculptCopy)) EditorGUILayout.LabelField("Saved with this scene. Use this scene or tile as the Terrain Merge source later.", EditorStyles.wordWrappedMiniLabel);
                if (!string.IsNullOrEmpty(m_SculptCopyStatus)) EditorGUILayout.HelpBox(m_SculptCopyStatus, MessageType.Info);
            }
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Merge Into This Tile", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Bring sculpting from a reference scene, tile, or mesh into this tile with the merge brush.", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || tiles.Length != 1))
                    if (GUILayout.Button(new GUIContent("Merge From Reference…", "Open Terrain Merge with this tile as the destination you are keeping.")))
                        MGTerrainMergeWindow.OpenForDestination(tiles[0]);
                if (tiles.Length != 1) EditorGUILayout.LabelField("Select one destination tile to merge into.", EditorStyles.wordWrappedMiniLabel);
            }
        }
        void MakeSculptCopies(IEnumerable<MGTerrain> selection)
        {
            try
            {
                var tiles = selection.Distinct().ToArray();
                MGTerrainSculptCopy.Create(tiles);
                serializedObject.Update();
                m_SculptCopyStatus = $"{tiles.Length} tile(s) now use scene-stored sculpt meshes. Save your scene to keep them. Undo restores the previous mesh references.";
            }
            catch (Exception error)
            {
                serializedObject.Update(); m_SculptCopyStatus = null;
                Debug.LogException(error); EditorUtility.DisplayDialog("Scene Sculpt Copy", error.Message, "OK");
            }
        }
    }
}
#endif
