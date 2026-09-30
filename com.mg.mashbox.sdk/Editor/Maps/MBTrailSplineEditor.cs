using System;
using System.Collections.Generic;
using MashBoxSDK.Maps;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

namespace MashBoxSDK.EditorTools.Maps
{
    public static class MBTrailSplineEditor
    {
        [InitializeOnLoadMethod]
        private static void RegisterIdValidation()
        {
            EditorApplication.hierarchyChanged -= EnsureUniqueIds;
            EditorApplication.hierarchyChanged += EnsureUniqueIds;
            EditorApplication.delayCall += EnsureUniqueIds;
        }

        private static void EnsureUniqueIds()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var seen = new HashSet<string>();
#if UNITY_6000_0_OR_NEWER
            MBTrailSpline[] trails = UnityEngine.Object.FindObjectsByType<MBTrailSpline>(FindObjectsInactive.Include);
#else
            MBTrailSpline[] trails = UnityEngine.Object.FindObjectsByType<MBTrailSpline>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#endif
            Array.Sort(trails, (a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
            foreach (MBTrailSpline trail in trails)
            {
                if (!trail || !trail.gameObject.scene.IsValid()) continue;
                if (!trail.HasAuthoredTrailName)
                {
                    Undo.RecordObject(trail, "Set Trail Name");
                    trail.SetTrailName(trail.gameObject.name);
                    EditorUtility.SetDirty(trail);
                }
                string key = trail.gameObject.scene.handle + ":" + trail.PersistentId;
                if (!string.IsNullOrEmpty(trail.PersistentId) && seen.Add(key)) continue;
                Undo.RecordObject(trail, "Assign Unique Trail ID");
                do
                {
                    trail.RegeneratePersistentId();
                    key = trail.gameObject.scene.handle + ":" + trail.PersistentId;
                } while (!seen.Add(key));
                EditorUtility.SetDirty(trail);
            }
        }
        [MenuItem("GameObject/MashBox/Maps/Trail Network", false, 10)]
        private static void CreateNetwork(MenuCommand command)
        {
            GameObject context = command.context as GameObject;
            Scene scene = context ? context.scene : SceneManager.GetActiveScene();
            Selection.activeGameObject = CreateNetwork(scene);
        }

        [MenuItem("GameObject/MashBox/Maps/Trail Spline", false, 11)]
        private static void CreateTrail(MenuCommand command)
        {
            GameObject context = command.context as GameObject;
            Scene scene = context ? context.scene : SceneManager.GetActiveScene();
            CreateTrail(scene, context);
        }

        public static MBTrailSpline CreateTrail(Scene scene, GameObject context = null)
        {
            GameObject network = FindNetwork(context, scene);
            if (!network) network = CreateNetwork(scene);
            int number = network.transform.childCount + 1;
            var go = new GameObject("Trail " + number.ToString("00"));
            Undo.RegisterCreatedObjectUndo(go, "Create Trail Spline");
            Undo.SetTransformParent(go.transform, network.transform, "Place Trail In Network");
            var container = go.AddComponent<SplineContainer>();
            container.Spline = new Spline(new[] {
                new float3(0, 0, 0), new float3(0, 0, 10)
            }, TangentMode.AutoSmooth);
            var trail = go.AddComponent<MBTrailSpline>();
            trail.SetTrailName(go.name);
            trail.EnsurePersistentId();
            Selection.activeGameObject = go;
            return trail;
        }

        private static GameObject FindNetwork(GameObject context, Scene scene)
        {
            for (Transform parent = context ? context.transform : null; parent; parent = parent.parent)
                if (parent.name == "Trail Network") return parent.gameObject;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == "Trail Network") return root;
            return null;
        }

        public static GameObject CreateNetwork(Scene scene)
        {
            var go = new GameObject("Trail Network");
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "Create Trail Network");
            return go;
        }
    }
}
