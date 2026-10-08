#if UNITY_6000_0_OR_NEWER
using System;
using UnityEngine;

namespace MashBoxSDK.Maps
{
    // Authoring data only. The game owns prefabs, physics, seats and networking.
    [AddComponentMenu("MashBox/Maps/Chairlift")]
    [DisallowMultipleComponent]
    public sealed class MBChairlift : MonoBehaviour
    {
        // Game-owned asset selection; designers name the lift, not its runtime definition.
        [HideInInspector] public string definitionId = "default";
        [Tooltip("The lift name used by its GameObject, map menus and in-game HUD.")]
        public string chairliftName = "Chair Lift";
        [Min(0)] public float baseSpeedMetersPerSecond = 2;
        [Min(0)] public float overallSpeedMultiplier = 1;
        [Min(0)] public float towerCableSpeedMultiplier = 1;
        [Min(.001f)] public float loadingBaySpeedMultiplier = .35f;
        public bool reverse;
        public bool stopped;
        public static event Action<MBChairlift> EnabledInPlayMode;
        public event Action SettingsChanged;
        public GameObject RuntimeInstance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => EnabledInPlayMode = null;
        private void Reset() { chairliftName = gameObject.name; SyncGameObjectName(); }
        private void OnEnable()
        {
            SyncGameObjectName();
            if (RuntimeInstance != null) RuntimeInstance.SetActive(true);
            if (Application.isPlaying) EnabledInPlayMode?.Invoke(this);
        }
        private void OnDisable()
        {
            if (RuntimeInstance != null) RuntimeInstance.SetActive(false);
        }
        private void OnValidate() => ApplySettings();
        public void SetRuntimeInstance(GameObject instance) => RuntimeInstance = instance;
        public void ApplySettings()
        {
            SyncGameObjectName();
            SettingsChanged?.Invoke();
        }
        private void SyncGameObjectName()
        {
            // Preserve existing authored display names when upgrading old proxies.
            string value = string.IsNullOrWhiteSpace(chairliftName) ? gameObject.name : chairliftName;
            chairliftName = string.IsNullOrWhiteSpace(value) ? "Chair Lift" : value.Trim();
            if (gameObject.name != chairliftName) gameObject.name = chairliftName;
        }
        public void Stop() { stopped = true; ApplySettings(); }
        public void Resume() { stopped = false; ApplySettings(); }
        public void SetForward() { reverse = false; ApplySettings(); }
        public void SetReverse() { reverse = true; ApplySettings(); }
        public void SetSpeed(float value) { baseSpeedMetersPerSecond = Mathf.Max(0, value); ApplySettings(); }

        public bool Validate(out string error)
        {
            int mains = 0;
            bool hasChairs = false;
            if (GetComponentsInChildren<MBChairlift>(true).Length != 1)
            { error = "Chairlifts cannot be nested."; return false; }
            var paths = GetComponentsInChildren<MBChairliftPath>(true);
            foreach (var path in paths)
            {
                if (path.GetComponentInParent<MBChairlift>() != this) continue;
                if (!path.loadingBay) mains++;
                hasChairs |= path.spawnChairs;
                if (!path.Validate(out error)) return false;
            }
            if (mains != 1) { error = "A chairlift requires exactly one tower cable path."; return false; }
            if (!hasChairs) { error = "Enable chair spawning on at least one path."; return false; }
            if (towerCableSpeedMultiplier <= 0 || loadingBaySpeedMultiplier <= 0)
            { error = "Cable and loading-bay speed ratios must be positive. Use Stop to stop the lift."; return false; }
            foreach (var station in GetComponentsInChildren<MBChairliftStation>(true))
                if (station.loadingBayPath == null || !station.loadingBayPath.loadingBay ||
                    station.loadingBayPath.GetComponentInParent<MBChairlift>() != this)
                { error = "Every loading bay must reference a loading-bay path in the same lift."; return false; }
            foreach (var zone in GetComponentsInChildren<MBChairliftTransfer>(true))
                if (zone.targetPath == null || zone.targetPath.GetComponentInParent<MBChairlift>() != this)
                { error = "Every transfer must target a path in the same lift."; return false; }
            error = null;
            return true;
        }
    }
}
#endif
