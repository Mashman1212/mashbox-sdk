#if UNITY_EDITOR
using System;
using System.Linq;
using MashBoxSDK.EditorTools.Maps;
using MashBoxSDK.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    public partial class MashBoxMapToolsWindow
    {
        private void DrawTrailNetworkSection()
        {
            Scene scene = SceneManager.GetActiveScene();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Trail Network", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Named trail splines drive navigation steering and the discoverable Trails challenge. " +
                    "Shape each spline from its trailhead to its finish with Unity's spline tools.",
                    MessageType.None);

                GameObject network = scene.IsValid() && scene.isLoaded
                    ? scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Trail Network")
                    : null;
                using (new EditorGUI.DisabledScope(!scene.IsValid() || !scene.isLoaded))
                {
                    if (!network && GUILayout.Button("Create Trail Network", GUILayout.Height(28f)))
                    {
                        Selection.activeGameObject = MBTrailSplineEditor.CreateNetwork(scene);
                        sceneToolCacheDirty = true;
                        EditorSceneManager.MarkSceneDirty(scene);
                        GUIUtility.ExitGUI();
                    }
                    if (GUILayout.Button("Add Trail Spline", GUILayout.Height(32f)))
                    {
                        MBTrailSpline trail = MBTrailSplineEditor.CreateTrail(scene);
                        PlaceNewChallengeObject(trail.transform);
                        sceneToolCacheDirty = true;
                        EditorSceneManager.MarkSceneDirty(scene);
                        GUIUtility.ExitGUI();
                    }
                }

                if (cachedTrails.Count == 0)
                {
                    EditorGUILayout.HelpBox("No trail splines are in this map yet.", MessageType.None);
                    return;
                }

                foreach (MBTrailSpline trail in cachedTrails)
                {
                    if (!trail) continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUI.BeginChangeCheck();
                        string updatedName = EditorGUILayout.TextField(trail.TrailName);
                        if (EditorGUI.EndChangeCheck() && !string.IsNullOrWhiteSpace(updatedName))
                        {
                            Undo.RecordObject(trail.gameObject, "Rename Trail");
                            Undo.RecordObject(trail, "Rename Trail");
                            trail.gameObject.name = updatedName.Trim();
                            trail.SetTrailName(updatedName.Trim());
                            EditorUtility.SetDirty(trail);
                            EditorSceneManager.MarkSceneDirty(scene);
                        }
                        EditorGUI.BeginChangeCheck();
                        MBTrailDifficulty difficulty = (MBTrailDifficulty)EditorGUILayout.EnumPopup(
                            trail.Difficulty, GUILayout.Width(170f));
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(trail, "Set Trail Difficulty");
                            trail.Difficulty = difficulty;
                            EditorUtility.SetDirty(trail);
                            EditorSceneManager.MarkSceneDirty(scene);
                        }                        if (GUILayout.Button("Select", GUILayout.Width(64f)))
                        {
                            Selection.activeGameObject = trail.gameObject;
                            EditorGUIUtility.PingObject(trail.gameObject);
                        }
                        if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                        {
                            Undo.DestroyObjectImmediate(trail.gameObject);
                            sceneToolCacheDirty = true;
                            EditorSceneManager.MarkSceneDirty(scene);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
        }
    }
}
#endif