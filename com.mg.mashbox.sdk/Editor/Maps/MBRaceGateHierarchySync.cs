#if UNITY_EDITOR

using System;
using System.Linq;
using MashBoxSDK.Maps;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    [InitializeOnLoad]
    internal static class MBRaceGateHierarchySync
    {
        private const float MinimumGateAxisScale = 0.01f;
        private const double MinimumSyncDelaySeconds = 0.75d;
        private static bool pendingSync;
        private static double nextAllowedSyncTime;

        static MBRaceGateHierarchySync()
        {
            EditorApplication.hierarchyChanged += QueueSync;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            QueueSync();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                QueueSync();
        }

        private static void QueueSync()
        {
            if (pendingSync || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            pendingSync = true;
            EditorApplication.delayCall += RunQueuedSyncWhenReady;
        }

        private static void RunQueuedSyncWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunQueuedSyncWhenReady;
                return;
            }

            if (EditorApplication.timeSinceStartup < nextAllowedSyncTime)
            {
                EditorApplication.delayCall += RunQueuedSyncWhenReady;
                return;
            }

            SyncAllRaceGates();
        }

        private static void SyncAllRaceGates()
        {
            pendingSync = false;
            nextAllowedSyncTime = EditorApplication.timeSinceStartup + MinimumSyncDelaySeconds;

            var races = Resources
                .FindObjectsOfTypeAll<MBRace>()
                .Where(race => race != null && race.gameObject.scene.IsValid())
                .ToList();

            foreach (var race in races)
                SyncRaceGateNames(race);
        }

        private static void SyncRaceGateNames(MBRace race)
        {
            if (race == null)
                return;

            var gates = race.transform
                .Cast<UnityEngine.Transform>()
                .Where(child => child.name.StartsWith("Gate", StringComparison.Ordinal) || child.GetComponent<MBRaceGate>() != null || child.GetComponent<MBGateGizmo>() != null)
                .OrderBy(child => child.GetSiblingIndex())
                .ToList();

            for (var index = 0; index < gates.Count; index++)
            {
                EnsureRaceGateComponent(gates[index].gameObject);
                var expectedName = $"Gate {index + 1:00}";
                var scaleChanged = SanitizeRaceGateScale(gates[index]);
                if (gates[index].name == expectedName && !scaleChanged)
                    continue;

                gates[index].name = expectedName;
                EditorUtility.SetDirty(gates[index].gameObject);
            }
        }

        private static bool SanitizeRaceGateScale(Transform gate)
        {
            if (gate == null)
                return false;

            var raceGate = gate.GetComponent<MBRaceGate>();
            if (raceGate != null)
            {
                var previousScale = gate.localScale;
                raceGate.EnforceValidScale();
                return gate.localScale != previousScale;
            }

            var currentScale = gate.localScale;
            var sanitizedScale = new Vector3(
                Mathf.Max(Mathf.Abs(currentScale.x), MinimumGateAxisScale),
                Mathf.Max(Mathf.Abs(currentScale.y), MinimumGateAxisScale),
                1f);

            if (currentScale == sanitizedScale)
                return false;

            gate.localScale = sanitizedScale;
            EditorUtility.SetDirty(gate);
            return true;
        }

        private static void EnsureRaceGateComponent(GameObject gateObject)
        {
            if (gateObject == null)
                return;

            if (gateObject.GetComponent<MBRaceGate>() == null)
                gateObject.AddComponent<MBRaceGate>();

            var legacyGizmo = gateObject.GetComponent<MBGateGizmo>();
            if (legacyGizmo != null)
                UnityEngine.Object.DestroyImmediate(legacyGizmo);
        }
    }
}

#endif
