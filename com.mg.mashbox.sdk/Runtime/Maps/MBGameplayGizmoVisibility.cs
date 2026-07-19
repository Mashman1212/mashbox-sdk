namespace MashBoxSDK.Maps
{
    /// <summary>Shared editor visibility setting for MashBox gameplay gizmos and handles.</summary>
    public static class MBGameplayGizmoVisibility
    {
        private const string EditorPreferenceKey = "MashBoxSDK.ShowGameplayGizmos";

#if UNITY_EDITOR
        private static bool initialized;
        private static bool visible;
#endif

        public static bool Visible
        {
            get
            {
#if UNITY_EDITOR
                if (!initialized)
                {
                    visible = UnityEditor.EditorPrefs.GetBool(EditorPreferenceKey, true);
                    initialized = true;
                }

                return visible;
#else
                return true;
#endif
            }
            set
            {
#if UNITY_EDITOR
                visible = value;
                initialized = true;
                UnityEditor.EditorPrefs.SetBool(EditorPreferenceKey, value);
#endif
            }
        }
    }
}
