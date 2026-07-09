using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;

namespace MashBoxBridge.Achievements
{
    [CreateAssetMenu(fileName = "AchievementDatabase", menuName = "Achievements/Achievement Database", order = 2)]
    public class AchievementDatabase : ScriptableObject
    {
        public List<AchievementData> achievements = new List<AchievementData>();

        public AchievementData GetByInternalId(string internalId)
        {
            return achievements.Find(a => a.internalId == internalId);
        }

        public string GetPlatformIdByInternal(string internalId)
        {
            var achievement = GetByInternalId(internalId);
            return achievement != null ? achievement.GetPlatformAchievementId() : null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            string assetPath = AssetDatabase.GetAssetPath(this);
            string folderPath = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');

            if (string.IsNullOrEmpty(folderPath))
                return;

            string[] guids = AssetDatabase.FindAssets("t:AchievementData", new[] { folderPath });
            var discoveredAchievements = new List<AchievementData>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AchievementData data = AssetDatabase.LoadAssetAtPath<AchievementData>(path);

                if (data != null)
                {
                    discoveredAchievements.Add(data);
                }
            }

            if (discoveredAchievements.Count == 0)
                return;

            achievements.Clear();
            achievements.AddRange(discoveredAchievements);

            // Mark dirty so changes get saved
            EditorUtility.SetDirty(this);
        }
#endif
    }
}