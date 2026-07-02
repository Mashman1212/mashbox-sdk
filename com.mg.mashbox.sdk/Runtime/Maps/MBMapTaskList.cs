using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.Maps
{
    public enum MBMapTaskKind
    {
        Standard,
        RaceTime,
        GroundSpeedOnRace,
        BeatLeaderboardPlayer,
        ParkHit,
        ManualDistance,
        StoppieDistance,
        ExpertLineSession
    }

    [Serializable]
    public class MBMapTaskDefinition
    {
        public bool enabled = true;
        public MBMapTaskKind taskType = MBMapTaskKind.Standard;
        public string displayName = "New Task";
        public string verb = string.Empty;
        public string preposition = string.Empty;
        public string adjective = string.Empty;
        public float targetValue;
        public int targetCount = 1;

        public string DisplayNameOrFallback
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(displayName))
                    return displayName.Trim();

                string parts = $"{verb} {preposition} {adjective}".Trim();
                return string.IsNullOrWhiteSpace(parts) ? "Map Task" : parts;
            }
        }

        public void Sanitize()
        {
            displayName = SanitizeText(displayName);
            verb = SanitizeText(verb);
            preposition = SanitizeText(preposition);
            adjective = SanitizeText(adjective);
            targetValue = Mathf.Max(0.0f, targetValue);
            targetCount = Mathf.Max(1, targetCount);
        }

        private static string SanitizeText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("MashBox/Maps/Map Task List")]
    public class MBMapTaskList : MonoBehaviour
    {
        public const string RootName = "Map Tasks";

        [SerializeField] private List<MBMapTaskDefinition> tasks = new List<MBMapTaskDefinition>();

        public List<MBMapTaskDefinition> Tasks => tasks;

        public bool HasEnabledTasks
        {
            get
            {
                if (tasks == null)
                    return false;

                for (int i = 0; i < tasks.Count; i++)
                {
                    if (tasks[i] != null && tasks[i].enabled)
                        return true;
                }

                return false;
            }
        }

        private void Reset()
        {
            AddStarterTasks();
        }

        private void OnValidate()
        {
            Sanitize();
        }

        public void AddStarterTasks()
        {
            if (tasks == null)
                tasks = new List<MBMapTaskDefinition>();

            if (tasks.Count > 0)
                return;

            tasks.Add(new MBMapTaskDefinition
            {
                displayName = "360 an Expert Line",
                taskType = MBMapTaskKind.Standard,
                verb = "360",
                preposition = "Expert Line",
                targetCount = 1
            });

            tasks.Add(new MBMapTaskDefinition
            {
                displayName = "Session Expert Lines",
                taskType = MBMapTaskKind.ExpertLineSession,
                targetCount = 2
            });

            tasks.Add(new MBMapTaskDefinition
            {
                displayName = "Tuck No for the Camera",
                taskType = MBMapTaskKind.Standard,
                verb = "Tuck No",
                preposition = "Camera",
                targetCount = 1
            });

            Sanitize();
        }

        public void Sanitize()
        {
            if (tasks == null)
                tasks = new List<MBMapTaskDefinition>();

            for (int i = tasks.Count - 1; i >= 0; i--)
            {
                if (tasks[i] == null)
                {
                    tasks.RemoveAt(i);
                    continue;
                }

                tasks[i].Sanitize();
            }
        }

        public static MBMapTaskList FindFirstLoaded()
        {
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                MBMapTaskList list = FindInScene(scene, requireEnabledTasks: true);
                if (list != null)
                    return list;
            }

            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                MBMapTaskList list = FindInScene(scene, requireEnabledTasks: false);
                if (list != null)
                    return list;
            }

            return null;
        }

        public static MBMapTaskList FindInScene(Scene scene, bool requireEnabledTasks = false)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                MBMapTaskList[] lists = roots[i].GetComponentsInChildren<MBMapTaskList>(true);
                for (int j = 0; j < lists.Length; j++)
                {
                    if (lists[j] == null)
                        continue;

                    if (!requireEnabledTasks || lists[j].HasEnabledTasks)
                        return lists[j];
                }
            }

            return null;
        }
    }
}
