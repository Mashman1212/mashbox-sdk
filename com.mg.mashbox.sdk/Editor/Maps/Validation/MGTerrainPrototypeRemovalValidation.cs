using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
static class MGTerrainPrototypeRemovalValidation
{
    static string Folder => Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(MGTerrain).Assembly).resolvedPath, "Development~/MGTerrainImplementation~/PrototypeRemoval/");
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static MGTerrainPrototypeRemovalValidation() { EditorApplication.delayCall += Requested; }
    static void Requested()
    {
        if (!File.Exists(Folder + "validate.request") || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Folder + "validate.request");
        Run();
    }
    static object Call(Editor editor, string name, params object[] args) =>
        editor.GetType().GetMethod(name, Flags).Invoke(editor, args);
    static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }

    [MenuItem("Tools/MashBox/MG Terrain/Validate World Prototype Removal")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene = EditorSceneManager.NewPreviewScene();
        var tiles = new List<MGTerrain>();
        var maps = new List<Texture2D>();
        Editor editor = null;
        var previousTool = Tools.current;
        GameObject Go(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.SetActive(false);
            return go;
        }
        try
        {
            var world = Go("Prototype validation world").AddComponent<MGTerrainWorld>();
            var tree = Go("Tree to remove");
            var grass = Go("Surviving grass");
            var unused = Go("Unused tree to remove");
            for (int tileIndex = 0; tileIndex < 4; tileIndex++)
            {
                var host = Go("Tile " + tileIndex);
                host.transform.SetParent(world.transform);
                var tile = host.AddComponent<MGTerrain>();
                tiles.Add(tile);
                typeof(MGTerrain).GetField("m_World", Flags).SetValue(tile, world);
                // The same assets occupy different indices on alternating tiles.
                var prefabs = tileIndex % 2 == 0 ? new[] { tree, grass, unused } : new[] { unused, tree, grass };
                foreach (var prefab in prefabs)
                    tile.FindOrAddPrototype(prefab, prefab == grass ? MGTerrain.InstanceKind.Detail : MGTerrain.InstanceKind.Tree);
                var map = new Texture2D(2, 2, TextureFormat.R16, false, true);
                map.SetPixelData(new ushort[] { 1, 2, 3, 4 }, 0); map.Apply(); maps.Add(map);
                using var data = new SerializedObject(tile);
                var layers = data.FindProperty("m_DensityDetailLayers");
                var instances = data.FindProperty("m_Instances");
                layers.arraySize = instances.arraySize = 2;
                for (int i = 0; i < 2; i++)
                {
                    int index = Array.IndexOf(prefabs, i == 0 ? tree : grass);
                    var layer = layers.GetArrayElementAtIndex(i);
                    layer.FindPropertyRelative("m_PrototypeIndex").intValue = index;
                    layer.FindPropertyRelative("m_DensityMap").objectReferenceValue = map;
                    instances.GetArrayElementAtIndex(i).FindPropertyRelative("m_PrototypeIndex").intValue = index;
                }
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            editor = Editor.CreateEditor(tiles[0]);
            Call(editor, "PrepareSharedWorldDetails", tiles[0]);
            editor.serializedObject.Update();
            Undo.IncrementCurrentGroup();
            foreach (var prefab in new[] { tree, unused })
            {
                int index = -1;
                for (int i = 0; i < tiles[0].Prototypes.Count; i++)
                    if (tiles[0].Prototypes[i].Prefab == prefab) index = i;
                editor.GetType().GetField("m_SelectedPrototype", Flags).SetValue(editor, index);
                Call(editor, "RemoveSelectedPrototype", tiles[0]);
                Call(editor, "CommitSharedWorldDetails", tiles[0]);
                editor.serializedObject.Update();
            }
            void Verify()
            {
                for (int i = 0; i < tiles.Count; i++)
                {
                    Check(tiles[i].Prototypes.Count == 1 && tiles[i].Prototypes[0].Prefab == grass, "Deleted trees returned on tile " + i);
                    using var data = new SerializedObject(tiles[i]);
                    foreach (string name in new[] { "m_DensityDetailLayers", "m_Instances" })
                    {
                        var items = data.FindProperty(name);
                        Check(items.arraySize == 1, "Removed references remain in " + name);
                        Check(items.GetArrayElementAtIndex(0).FindPropertyRelative("m_PrototypeIndex").intValue == 0, "Survivor index was not remapped");
                    }
                    Check(tiles[i].DensityDetailLayers[0].DensityMap == maps[i], "Tile-local painting changed");
                    Check(maps[i].GetPixelData<ushort>(0)[3] == 4, "Paint contents changed");
                }
            }
            Verify();
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            foreach (var tile in tiles) Check(tile.Prototypes.Count == 3, "Undo did not restore all prototypes");
            Undo.PerformRedo();
            Verify();
            Object.DestroyImmediate(editor);
            editor = Editor.CreateEditor(tiles[2]);
            Call(editor, "PrepareSharedWorldDetails", tiles[2]);
            Verify();
            File.WriteAllText(Folder + "validation.txt", "PASS: Four tiles with different prototype orders; used and unused tree removal; layer and instance remapping; local map preservation; Undo/Redo; inspector recreation on another tile.\n");
            Debug.Log("[MG Terrain] World prototype removal validation passed.");
        }
        catch (Exception error)
        {
            File.WriteAllText(Folder + "validation.txt", "FAIL: " + error);
            Debug.LogException(error);
        }
        finally
        {
            if (editor != null) Object.DestroyImmediate(editor);
            Tools.current = previousTool;
            foreach (var tile in tiles) { Undo.ClearUndo(tile); typeof(MGTerrain).GetField("m_World", Flags).SetValue(tile, null); }
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var map in maps) Object.DestroyImmediate(map);
        }
    }
}
