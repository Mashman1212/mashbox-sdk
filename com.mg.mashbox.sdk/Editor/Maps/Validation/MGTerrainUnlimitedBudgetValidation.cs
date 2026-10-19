#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
static class MGTerrainUnlimitedBudgetValidation
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static MGTerrainUnlimitedBudgetValidation() { EditorApplication.delayCall += Requested; }
    static void Requested()
    {
        string path = Path.Combine(Path.GetTempPath(), "mg-unlimited-budget.request");
        if (!File.Exists(path) || File.ReadAllText(path).Trim().Replace('\\', '/') != Application.dataPath.Replace('\\', '/')) return;
        File.Delete(path);
        Run();
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    [MenuItem("Tools/MashBox/MG Terrain/Validation/Unlimited Visible Budget")]
    static void Run()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Permanent uncapped world validation");
        root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        string output = Path.Combine(Path.GetTempPath(), "mg-unlimited-budget.txt");
        try
        {
            var world = root.AddComponent<MGTerrainWorld>();
            Check(world.VisibleDetailBudget == int.MaxValue, "New worlds must always be uncapped.");
            Check(new SerializedObject(world).FindProperty("m_VisibleDetailBudget") == null, "Visible count cap must not remain a setting.");
            EditorJsonUtility.FromJsonOverwrite("{\"m_VisibleDetailBudget\":50000}", world);
            Check(world.VisibleDetailBudget == int.MaxValue, "Legacy saved caps must be ignored.");

            var tileObject = new GameObject("Validation tile");
            tileObject.transform.SetParent(root.transform, false);
            var tile = tileObject.AddComponent<MGTerrain>();
            typeof(MGTerrain).GetField("m_World", Flags).SetValue(tile, world);
            typeof(MGTerrain).GetField("m_UseDetailDensityLod", Flags).SetValue(tile, false);
            var prototype = Activator.CreateInstance(typeof(MGTerrain.Prototype), Flags, null,
                new object[] { null, MGTerrain.InstanceKind.Detail, 500f }, null);
            object New(string name, params object[] args) => Activator.CreateInstance(typeof(MGTerrain).GetNestedType(name, Flags), Flags, null, args, null);
            var chunk = New("DensityDetailChunk");
            chunk.GetType().GetField("instanceCount", Flags).SetValue(chunk, 300000);
            var visible = New("VisibleDensityDetail", chunk, prototype, 0f, 0);
            var select = typeof(MGTerrain).GetMethod("GetBudgetedDetailInstanceCount", Flags);
            foreach (float scale in new[] { 0f, .01f, .5f, 1f })
                Check((int)select.Invoke(tile, new[] { visible, (object)scale }) == 300000,
                    "Another view's budget scale must not change a world cell's population.");
            typeof(MGTerrain).GetField("m_UseDetailDensityLod", Flags).SetValue(tile, true);
            typeof(MGTerrain).GetField("m_MidDetailDensity", Flags).SetValue(tile, .5f);
            var medium = New("VisibleDensityDetail", chunk, prototype, 100f, 1);
            Check((int)select.Invoke(tile, new[] { medium, (object)0f }) == 150000, "Authored density LOD must remain effective.");
            typeof(MGTerrain).GetField("m_World", Flags).SetValue(tile, null);
            string report = "PASS: new worlds permanently uncapped; legacy 50,000 caps ignored; count setting absent; a 300,000-instance world cell retains its population at all budget scales; authored 50% density LOD still selects 150,000 instances.";
            File.WriteAllText(output, report);
            Debug.Log(report);
        }
        catch (Exception exception) { File.WriteAllText(output, "FAIL: " + exception); Debug.LogException(exception); }
        finally { UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
    }
}
#endif
