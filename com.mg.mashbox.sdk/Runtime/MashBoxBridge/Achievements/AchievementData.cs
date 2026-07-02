using System;
using UnityEngine;

namespace MashBoxBridge.Achievements
{
    public enum AchievementCheckType
    {
        KeywordMatch, // current behavior
        MinSpin,
        MinFlips,
        MinAirTime,
        MinUniqueTricks,
        MinGrindDistance,

        MinMannyTime
        // You could add other types later (score, time, etc.)
    }

    [CreateAssetMenu(fileName = "NewAchievement", menuName = "Achievements/Achievement Data", order = 1)]
    public class AchievementData : ScriptableObject
    {
        [Header("Internal Achievement ID (Shared Across All Platforms)")]
        public string internalId;

        [Header("Platform-Specific IDs")] public string steamId;
        public string gameCoreId; // Xbox (GDK)
        public string playStationId;

        [Header("Display Info")] public string displayName;
        [TextArea] public string description;

        [Header("Unlock Condition")] public AchievementCheckType checkType = AchievementCheckType.KeywordMatch;

        [Tooltip("Used for KeywordMatch checks")]
        public string[] requiredActivityKeywords;

        [Tooltip("Minimum value needed for MinSpin / MinFlips checks")]
        public float minValue;

        public string GetPlatformAchievementId()
        {
#if UNITY_GAMECORE
            return gameCoreId;
#elif UNITY_STANDALONE
            return steamId;
#elif UNITY_PS5 || UNITY_PS4
            return playStationId;
#else
            Debug.LogWarning($"[AchievementData] Unknown or unsupported platform for achievement '{internalId}'");
            return null;
#endif
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(playStationId))
            {
                playStationId = steamId;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            }

            if (string.IsNullOrEmpty(gameCoreId))
            {
                gameCoreId = steamId;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            }
        }
    }
}