using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.MapTools;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class TerrainWindowPlayModeProbe
{
    const string Phase = "TerrainWindowProbe.Phase";
    const string Report = "C:/Users/matth/.codex/visualizations/2026/11/02/01a19ca8-03f2-7823-8496-6dd65058b3ef/TerrainPlayMode/result.txt";
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static TerrainWindowPlayModeProbe() => EditorApplication.playModeStateChanged += Changed;

    public static void RunRebind()
    {
        try
        {
            File.WriteAllText(Report, "Starting isolated scene-reload regression.\n");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var other = new GameObject("Other world").AddComponent<MGTerrainWorld>();
            var target = new GameObject("Locked target world").AddComponent<MGTerrainWorld>();
            MGTerrainWorldWindow.Open(target);
            var window = Window();
            typeof(MGTerrainWorldWindow).GetField("m_Locked", Private).SetValue(window, true);
            Selection.activeGameObject = other.gameObject;
            CheckTarget();
            File.AppendAllText(Report, "PASS: locked target survives selecting another world.\n");
            var transition = typeof(MGTerrainWorldWindow).GetMethod("OnPlayModeStateChanged", Private);
            foreach (var pair in new[] {
                new[] { PlayModeStateChange.ExitingEditMode, PlayModeStateChange.EnteredPlayMode },
                new[] { PlayModeStateChange.ExitingPlayMode, PlayModeStateChange.EnteredEditMode } })
            {
                transition.Invoke(window, new object[] { pair[0] });
                UnityEngine.Object.DestroyImmediate(target.gameObject);
                target = new GameObject("Locked target world").AddComponent<MGTerrainWorld>();
                transition.Invoke(window, new object[] { pair[1] });
                CheckTarget();
                var actual = typeof(MGTerrainWorldWindow).GetField("m_World", Private).GetValue(window);
                if (!ReferenceEquals(actual, target)) throw new Exception("Window retained the old scene object.");
                File.AppendAllText(Report, "PASS: " + pair[1] + " rebinds to the recreated target with multiple worlds and lock enabled.\n");
            }
            using (var settings = new SerializedObject(target))
            {
                settings.FindProperty("m_DetailDistance").floatValue = 123;
                settings.ApplyModifiedProperties();
            }
            if (!Mathf.Approximately(target.DetailDistance, 123)) throw new Exception("Detail distance did not apply.");
            File.AppendAllText(Report, "PASS: detail distance applies to the rebound world.\nALL PASSED\n");
            window.Close();
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Fail(error); }
    }

    public static void Run()
    {
        try
        {
            File.WriteAllText(Report, "Starting isolated Play Mode lifecycle regression.\n");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var first = new GameObject("Other world").AddComponent<MGTerrainWorld>();
            var second = new GameObject("Locked target world").AddComponent<MGTerrainWorld>();
            MGTerrainWorldWindow.Open(second);
            var window = Window();
            typeof(MGTerrainWorldWindow).GetField("m_Locked", Private).SetValue(window, true);
            Selection.activeGameObject = first.gameObject;
            CheckTarget();
            File.AppendAllText(Report, "PASS: locked target survives selecting another world.\n");
            // Save the fixture so normal scene reload has a persisted source to clone.
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/Fixture.unity");
            SessionState.SetInt(Phase, 1);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception error) { Fail(error); }
    }

    static MGTerrainWorldWindow Window() => Resources.FindObjectsOfTypeAll<MGTerrainWorldWindow>().Single();
    static void CheckTarget()
    {
        var window = Window();
        typeof(MGTerrainWorldWindow).GetMethod("RefreshWorlds", Private).Invoke(window, null);
        var world = (MGTerrainWorld)typeof(MGTerrainWorldWindow).GetField("m_World", Private).GetValue(window);
        if (world == null || world.name != "Locked target world" || !world.gameObject.scene.isLoaded)
            throw new Exception("Window did not bind to its locked world in the loaded scene.");
    }

    static void Changed(PlayModeStateChange state)
    {
        if (SessionState.GetInt(Phase, 0) == 1 && state == PlayModeStateChange.EnteredPlayMode)
            EditorApplication.delayCall += () =>
            {
                try
                {
                    CheckTarget();
                    var world = (MGTerrainWorld)typeof(MGTerrainWorldWindow).GetField("m_World", Private).GetValue(Window());
                    using (var settings = new SerializedObject(world))
                    {
                        settings.FindProperty("m_DetailDistance").floatValue = 123;
                        settings.ApplyModifiedProperties();
                    }
                    if (!Mathf.Approximately(world.DetailDistance, 123)) throw new Exception("Live rendering setting did not apply.");
                    File.AppendAllText(Report, "PASS: locked target rebinds after entering Play Mode; live detail distance applies.\n");
                    SessionState.SetInt(Phase, 2);
                    EditorApplication.ExitPlaymode();
                }
                catch (Exception error) { Fail(error); }
            };
        if (SessionState.GetInt(Phase, 0) == 2 && state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += () =>
            {
                try
                {
                    CheckTarget();
                    File.AppendAllText(Report, "PASS: locked target rebinds after returning to Edit Mode.\nALL PASSED\n");
                    SessionState.SetInt(Phase, 0);
                    Window().Close();
                    EditorApplication.Exit(0);
                }
                catch (Exception error) { Fail(error); }
            };
    }
    static void Fail(Exception error)
    {
        File.AppendAllText(Report, "FAIL: " + error + "\n");
        Debug.LogException(error);
        EditorApplication.Exit(1);
    }
}
