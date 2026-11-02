using System;
using System.IO;
using System.Reflection;
using MashBoxSDK.MapTools;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DetailWorkspaceProbe
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Report = "C:/Users/matth/.codex/visualizations/2026/11/02/01a19ca8-03f2-7823-8496-6dd65058b3ef/PopulationCards/result.txt";
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    static object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    static void Check(bool value, string message) { if (!value) throw new Exception(message); File.AppendAllText(Report, "PASS: " + message + "\n"); }

    public static void Run()
    {
        try
        {
            File.WriteAllText(Report, "Details workspace regression\n");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("World");
            var world = root.AddComponent<MGTerrainWorld>();
            var tileObject = new GameObject("Tile");
            tileObject.transform.SetParent(root.transform);
            var tile = tileObject.AddComponent<MGTerrain>();
            using (var data = new SerializedObject(tile))
            {
                data.FindProperty("m_Prototypes").arraySize = 1;
                var layers = data.FindProperty("m_DensityDetailLayers");
                layers.arraySize = 2;
                for (int i = 0; i < 2; i++)
                {
                    var layer = layers.GetArrayElementAtIndex(i);
                    layer.FindPropertyRelative("m_PrototypeIndex").intValue = 0;
                    layer.FindPropertyRelative("m_UseGrassArray").boolValue = true;
                    layer.FindPropertyRelative("m_GrassPopulation").intValue = i;
                    layer.FindPropertyRelative("m_PaletteSourceOnly").boolValue = false;
                }
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            world.RefreshChunks();
            var editor = (MGTerrainEditor)UnityEditor.Editor.CreateEditor(tile, typeof(MGTerrainEditor));
            editor.serializedObject.Update();
            Set(editor, "m_GrassPaintPopulation", 0);
            Check((int)Call(editor, "DetailTuningLayer", tile, true) == 0, "Population A resolves to its own tuning layer");
            Set(editor, "m_GrassPaintPopulation", 1);
            Check((int)Call(editor, "DetailTuningLayer", tile, true) == 1, "Population B resolves to its own tuning layer");
            Tools.current = Tool.Move;
            Call(editor, "UpdateWorldDetailBrush", tile);
            Check((bool)Get(editor, "m_DetailPainting") && Tools.current == Tool.None, "Details activates the brush without a Paint button");
            var worldEditor = (MGTerrainWorldEditor)UnityEditor.Editor.CreateEditor(world, typeof(MGTerrainWorldEditor));
            Set(worldEditor, "m_ChunkEditor", editor);
            Call(worldEditor, "SetWorkspacePage", 0);
            Check(editor == null && Tools.current == Tool.Move, "Leaving Details disposes its brush and restores the previous scene tool");
            UnityEngine.Object.DestroyImmediate(worldEditor);
            File.AppendAllText(Report, "ALL PASSED\n");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.AppendAllText(Report, "FAIL: " + error + "\n");
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }
}
