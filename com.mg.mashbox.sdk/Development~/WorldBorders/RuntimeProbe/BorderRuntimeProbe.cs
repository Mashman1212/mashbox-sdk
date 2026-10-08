using System;
using System.IO;
using MashBoxSDK.Maps;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class BorderRuntimeProbe
{
    static MBWorldBorderWall wall;
    static Material template;
    static Mesh previousMesh;
    static Material previousMaterial;
    static GameObject previousRoot;
    static int stage;
    static int frame;
    static Scene additive;
    static AsyncOperation unload;
    static readonly string report = Path.GetFullPath("runtime-probe.txt");
    static BorderRuntimeProbe() { EditorApplication.playModeStateChanged += State; }
    public static void Begin()
    {
        File.WriteAllText(report, "Starting isolated Play Mode validation.\n");
        SessionState.SetBool("BorderRuntimeProbe.Running", true);
        EditorApplication.EnterPlaymode();
    }
    static void State(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("BorderRuntimeProbe.Running", false)) return;
        try
        {
            template = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/BorderRuntimeProbe.shader"));
            template.SetTexture("_ALBEDO", Texture2D.whiteTexture);
            var go = new GameObject("Runtime wall proxy"); go.SetActive(false);
            go.transform.SetPositionAndRotation(new Vector3(3, 7, 9), Quaternion.Euler(13, 37, 4));
            go.transform.localScale = new Vector3(1024, 128, .5f);
            wall = go.AddComponent<MBWorldBorderWall>(); wall.materialTemplate = template;
            Check(wall.RuntimeInstance == null, "Inactive proxy spawned geometry.");
            go.SetActive(true);
            Check(wall.RuntimeInstance && wall.RuntimeInstance.GetComponentsInChildren<LODGroup>().Length == 8, "Activation did not generate eight tiles.");
            var instance = wall.RuntimeInstance;
            wall.GenerateRuntime();
            Check(instance == wall.RuntimeInstance && go.transform.childCount == 1, "Startup is not idempotent.");
            var collider = instance.GetComponent<BoxCollider>();
            foreach (var lod in instance.GetComponentsInChildren<LODGroup>()) lod.ForceLOD(1);
            Physics.SyncTransforms();
            Check(collider.Raycast(new Ray(go.transform.position + go.transform.forward * 5, -go.transform.forward), out _, 10), "Culled visuals disabled collision.");
            Remember(); wall.enabled = false;
            Check(!wall.RuntimeInstance && !previousRoot.activeInHierarchy, "Disable did not immediately hide geometry/collision.");
            File.AppendAllText(report, "PASS: automatic activation, idempotent startup, collision under forced visual culling, immediate disable.\n");
            stage = 1; frame = Time.frameCount;
            EditorApplication.update += Tick;
        }
        catch (Exception e) { Fail(e); }
    }
    static void Remember()
    {
        previousRoot = wall.RuntimeInstance;
        previousMesh = previousRoot.GetComponentInChildren<MeshFilter>().sharedMesh;
        previousMaterial = previousRoot.GetComponentInChildren<MeshRenderer>().sharedMaterial;
    }
    static void Tick()
    {
        if (!Application.isPlaying || Time.frameCount <= frame + 1) return;
        try
        {
            if (stage == 1)
            {
                Check(!previousRoot && !previousMesh && !previousMaterial, "Disabled proxy leaked runtime resources.");
                wall.enabled = true;
                Check(wall.RuntimeInstance && wall.transform.childCount == 1, "Re-enable did not regenerate once.");
                wall.transform.localScale = new Vector3(257,129,.75f);
                Remember(); wall.RebuildRuntime();
                Check(wall.RuntimeInstance.GetComponentsInChildren<LODGroup>().Length == 6, "Runtime resize rebuild failed.");
                stage = 2; frame = Time.frameCount;
            }
            else if (stage == 2)
            {
                Check(!previousMesh && !previousMaterial && wall.transform.childCount == 1, "Rebuild leaked or duplicated output.");
                Remember(); UnityEngine.Object.Destroy(wall.gameObject);
                stage = 3; frame = Time.frameCount;
            }
            else if (stage == 3)
            {
                Check(!previousMesh && !previousMaterial && !previousRoot, "Destroy leaked generated resources.");
                File.AppendAllText(report, "PASS: disable cleanup, re-enable, resize rebuild and destruction release meshes/materials.\n");
                additive = SceneManager.CreateScene("Additive border test");
                var go = new GameObject("Additive wall"); go.SetActive(false);
                SceneManager.MoveGameObjectToScene(go, additive);
                go.transform.localScale = new Vector3(256,64,.5f);
                wall = go.AddComponent<MBWorldBorderWall>(); wall.materialTemplate = template;
                go.SetActive(true); Check(wall.RuntimeInstance, "Additive proxy did not generate.");
                Remember(); unload = SceneManager.UnloadSceneAsync(additive);
                stage = 4; frame = Time.frameCount;
            }
            else if (stage == 4 && unload.isDone)
            {
                Check(!previousMesh && !previousMaterial && !previousRoot, "Scene unload leaked resources.");
                File.AppendAllText(report, "PASS: additive scene activation and unload cleanup.\nThis headless lifecycle test uses a minimal test shader; HDRP artwork is checked separately in MappyX.\n");
                SessionState.SetBool("BorderRuntimeProbe.Running", false);
                EditorApplication.update -= Tick;
                EditorApplication.Exit(0);
            }
        }
        catch (Exception e) { Fail(e); }
    }
    static void Check(bool value, string error) { if (!value) throw new Exception(error); }
    static void Fail(Exception e)
    {
        File.AppendAllText(report, "FAIL: " + e + "\n");
        EditorApplication.update -= Tick;
        EditorApplication.Exit(1);
    }
}
