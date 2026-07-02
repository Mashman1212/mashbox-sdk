#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    [InitializeOnLoad]
    internal static class MBSceneIconUtility
    {
        private const string ChallengeSceneIconName = "sv_label_3";
        private const double RefreshIntervalSeconds = 1.0d;

        private static double nextRefreshTime;

        static MBSceneIconUtility()
        {
            EditorApplication.hierarchyChanged += ScheduleRefreshSoon;
            EditorSceneManager.sceneOpened += (_, _) => ScheduleRefreshSoon();
            EditorApplication.update += OnEditorUpdate;
            ScheduleRefreshSoon();
        }

        internal static void ApplyChallengeSceneIcon(GameObject target)
        {
            if (target == null)
                return;

            var icon = EditorGUIUtility.IconContent(ChallengeSceneIconName)?.image as Texture2D;
            if (icon == null)
                return;

            EditorGUIUtility.SetIconForObject(target, icon);
        }

        private static void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < nextRefreshTime)
                return;

            ApplyIconsToLoadedScenes();
            nextRefreshTime = EditorApplication.timeSinceStartup + RefreshIntervalSeconds;
        }

        private static void ScheduleRefreshSoon()
        {
            nextRefreshTime = 0d;
        }

        private static void ApplyIconsToLoadedScenes()
        {
            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    ApplyIcons(root.GetComponentsInChildren<MBSecretGap>(true).Select(component => component.gameObject));
                    ApplyIcons(root.GetComponentsInChildren<MBSideHit>(true).Select(component => component.gameObject));
                    ApplyIcons(root.GetComponentsInChildren<MBCollectible>(true).Select(component => component.gameObject));
                    ApplyIcons(root.GetComponentsInChildren<MBCollectLetter>(true).Select(component => component.gameObject));
                    ApplyIcons(root.GetComponentsInChildren<MBPhotoSpot>(true).Select(component => component.gameObject));
                }
            }
        }

        private static void ApplyIcons(IEnumerable<GameObject> targets)
        {
            foreach (var target in targets)
                ApplyChallengeSceneIcon(target);
        }
    }
}
#endif
