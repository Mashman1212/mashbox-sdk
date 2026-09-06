#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
        private const string ChallengesRootName = "Challenges";

        private static readonly List<MonoBehaviour> ChallengeComponents = new List<MonoBehaviour>();
        private static bool refreshQueued;
        private static bool applyingIcons;
        private static Texture2D challengeIcon;

        static MBSceneIconUtility()
        {
            EditorApplication.hierarchyChanged += ScheduleRefreshSoon;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            ScheduleRefreshSoon();
        }

        internal static void ApplyChallengeSceneIcon(GameObject target)
        {
            if (target == null)
                return;

            Texture2D icon = challengeIcon;
            if (icon == null)
            {
                icon = EditorGUIUtility.IconContent(ChallengeSceneIconName)?.image as Texture2D;
                challengeIcon = icon;
            }
            if (icon == null)
                return;

            if (EditorGUIUtility.GetIconForObject(target) == icon)
                return;

            EditorGUIUtility.SetIconForObject(target, icon);
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            ScheduleRefreshSoon();
        }

        private static void ScheduleRefreshSoon()
        {
            if (applyingIcons || refreshQueued)
                return;

            refreshQueued = true;
            EditorApplication.delayCall -= ApplyQueuedIcons;
            EditorApplication.delayCall += ApplyQueuedIcons;
        }

        private static void ApplyQueuedIcons()
        {
            EditorApplication.delayCall -= ApplyQueuedIcons;
            refreshQueued = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                ScheduleRefreshSoon();
                return;
            }

            applyingIcons = true;
            try
            {
                ApplyIconsToLoadedScenes();
            }
            finally
            {
                applyingIcons = false;
            }
        }

        private static void ApplyIconsToLoadedScenes()
        {
            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (!string.Equals(root.name, ChallengesRootName, StringComparison.Ordinal))
                        continue;

                    ChallengeComponents.Clear();
                    root.GetComponentsInChildren(true, ChallengeComponents);
                    for (int componentIndex = 0; componentIndex < ChallengeComponents.Count; componentIndex++)
                    {
                        MonoBehaviour component = ChallengeComponents[componentIndex];
                        if (component is MBSecretGap
                            || component is MBSideHit
                            || component is MBCollectible
                            || component is MBCollectLetter
                            || component is MBPhotoSpot)
                        {
                            ApplyChallengeSceneIcon(component.gameObject);
                        }
                    }
                }
            }
            ChallengeComponents.Clear();
        }
    }
}
#endif
