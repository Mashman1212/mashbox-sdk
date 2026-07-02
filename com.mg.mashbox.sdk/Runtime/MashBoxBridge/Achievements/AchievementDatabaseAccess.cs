using UnityEngine;

namespace MashBoxBridge.Achievements
{
    public static class AchievementDatabaseAccess
    {
        private static AchievementDatabase _cachedDataBase;

        public static AchievementDatabase GetAchievementDatabase()
        {
            if (_cachedDataBase != null) return _cachedDataBase;

            string gameName = Application.productName;//.Replace(" ", ""); // sanitize if needed
            string path = $"{gameName}_AchievementDatabase"; 

            _cachedDataBase = Resources.Load<AchievementDatabase>(path);

#if UNITY_EDITOR
            if (_cachedDataBase == null)
            {
                Debug.LogWarning($"ColorPalette not found at: Resources/{path}. Make sure the asset is named correctly and in a Resources folder.");
            }
#endif

            return _cachedDataBase;
        }
    }
}