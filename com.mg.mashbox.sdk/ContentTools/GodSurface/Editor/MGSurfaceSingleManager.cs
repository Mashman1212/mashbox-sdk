using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace MG.GodSurface.Editor
{
    public static class MGSurfaceSingleManager
    {
        public static void Validate(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            var worlds = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MGSurfaceWorld>(true)).ToArray();
            if (worlds.Length > 1)
                throw new BuildFailedException($"Scene '{scene.name}' has {worlds.Length} God Prop Root components. Only one is allowed, including disabled components. Remove the extras using their inspector and use the existing manager's Prop Roots list.");
        }
        public static MGSurfaceWorld MoveToEmptyObject(MGSurfaceWorld world)
        {
            if (!world || EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.IsPersistent(world))
                throw new InvalidOperationException("Move a scene manager outside Play Mode.");
            Validate(world.gameObject.scene);
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Move prop manager to empty object");
            var scene = world.gameObject.scene;
            var host = new GameObject("PROP CELLS"); host.SetActive(false); SceneManager.MoveGameObjectToScene(host, scene);
            Undo.RegisterCreatedObjectUndo(host, "Create prop cell manager object");
            world.Release();
            var replacement = Undo.AddComponent<MGSurfaceWorld>(host);
            EditorUtility.CopySerialized(world, replacement);
            if (replacement.roots == null || replacement.roots.Length == 0) replacement.roots = new[] { world.transform };
            Undo.DestroyObjectImmediate(world);
            host.SetActive(true);
            EditorUtility.SetDirty(replacement); EditorSceneManager.MarkSceneDirty(scene);
            replacement.RequestRebuild(); Selection.activeGameObject = host;
            Undo.CollapseUndoOperations(group);
            return replacement;
        }
    }
}
