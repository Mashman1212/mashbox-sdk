using System;
using System.Linq;
using MashBoxSDK.Maps.Spline;
using MashBoxSDK.MapTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

public static class LoftColliderPersistenceValidation
{
    static void Check(Scene scene, string phase)
    {
        var colliders = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshCollider>(true)).ToArray();
        if (colliders.Length != 4 || colliders.Any(c => c.sharedMesh == null || c.sharedMesh.vertexCount == 0))
            throw new Exception(phase + ": collider meshes missing; chunks=" + colliders.Length);
        Physics.SyncTransforms();
        if (!colliders.Any(c => c.Raycast(new Ray(new Vector3(2, 10, 25), Vector3.down), out _, 20)))
            throw new Exception(phase + ": collision ray missed");
        Debug.Log(phase + ": 4 meshes present; collision ray hit");
    }
    [MenuItem("MashBox/Validation/Validate Loft Collider Persistence")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run in Edit Mode.");
        string ScenePath = AssetDatabase.GenerateUniqueAssetPath("Assets/LoftColliderPersistenceValidation.unity");
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = default;
        try
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var go = new GameObject("Persistence loft");
            SceneManager.MoveGameObjectToScene(go, scene);
            var loft = go.AddComponent<MultiSplineLoft>();
            loft.AutoRegenerate = false;
            loft.TargetSegmentLength = 2;
            loft.ColliderChunkLength = 50;
            for (int side = 0; side < 2; side++)
            {
                var curve = new GameObject("Curve " + side);
                curve.transform.SetParent(go.transform, false);
                var container = curve.AddComponent<SplineContainer>();
                container.Spline = new UnityEngine.Splines.Spline(new[] {
                    new BezierKnot(new Unity.Mathematics.float3(side * 4, 0, 0)),
                    new BezierKnot(new Unity.Mathematics.float3(side * 4, 0, 200)) });
                loft.Sources.Add(new MultiSplineLoft.SplineSource { container = container });
            }
            loft.Regenerate();
            Check(scene, "generated");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            Check(scene, "saved and reopened");
            loft = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MultiSplineLoft>()).Single();
            // Reproduce the original failure through real Unity serialization.
            foreach (var c in loft.GetComponentsInChildren<MeshCollider>()) c.sharedMesh.hideFlags = HideFlags.DontSave;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            loft = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MultiSplineLoft>()).Single();
            var missing = loft.GetComponentsInChildren<MeshCollider>();
            if (missing.Length != 4 || missing.Any(c => c.sharedMesh != null))
                throw new Exception("Did not reproduce the legacy DontSave failure.");
            Debug.Log("Legacy DontSave reproduced: all four saved mesh references missing.");
            new LoftVisualBuildProcessor().OnProcessScene(scene, null);
            Check(scene, "export repaired legacy missing meshes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            Check(scene, "baked saved and reopened");
            loft = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MultiSplineLoft>()).Single();
            loft.RebuildColliderChunks();
            Check(scene, "baked rebuild preserves collision");
            Debug.Log("LOFT_COLLIDER_PERSISTENCE_PASS");
        }
        finally
        {
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            AssetDatabase.DeleteAsset(ScenePath);
        }
    }
}
