#if UNITY_EDITOR
using System;
using System.IO;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneMeshProbe
{
    public static void Run()
    {
        if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/SceneMeshProbeBootstrap.unity");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        string folder = "Assets/__SceneMeshProbe_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", folder.Substring(7));
        string path = folder + "/Working.unity";
        Mesh mesh = null;
        try
        {
            var go = new GameObject("Scene sculpt"); SceneManager.MoveGameObjectToScene(go, scene);
            var terrain = go.AddComponent<MGTerrain>();
            mesh = new Mesh { name = "Embedded sculpt", vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward, new Vector3(1,7,1) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one }, triangles = new[] { 0,2,1,1,2,3 } };
            mesh.RecalculateBounds(); mesh.RecalculateNormals(); terrain.MeshFilter.sharedMesh = mesh;
            terrain.ConfigureSurfaceGrid(2,2);
            using (var data = new SerializedObject(terrain))
            { data.FindProperty("m_EditableSculptMesh").objectReferenceValue = mesh; data.ApplyModifiedPropertiesWithoutUndo(); }
            if (!EditorSceneManager.SaveScene(scene,path)) throw new Exception("Save failed.");
            Debug.Log("SCENE MESH PROBE saved: asset path='" + AssetDatabase.GetAssetPath(mesh) + "', contains=" + AssetDatabase.Contains(mesh)
                + ", inlineMesh=" + File.ReadAllText(path).Contains("--- !u!43") + ", assetFiles=" + Directory.GetFiles(folder,"*.asset",SearchOption.AllDirectories).Length);
            EditorSceneManager.CloseScene(scene,true);
            if (mesh != null && !AssetDatabase.Contains(mesh)) UnityEngine.Object.DestroyImmediate(mesh);
            scene = EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            var loaded = scene.GetRootGameObjects()[0].GetComponent<MGTerrain>();
            var loadedMesh=loaded.MeshFilter.sharedMesh;
            if (loadedMesh == null || loadedMesh.vertices[3].y != 7) throw new Exception("Embedded mesh did not survive close/reopen.");
            Debug.Log("SCENE MESH PROBE PASSED: reopened height=" + loadedMesh.vertices[3].y + ", path='" + AssetDatabase.GetAssetPath(loadedMesh) + "', editable=" + (loaded.EditableSculptMesh == loadedMesh));
        }
        finally { if(scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene,true); AssetDatabase.DeleteAsset(folder); }
    }
}
#endif
