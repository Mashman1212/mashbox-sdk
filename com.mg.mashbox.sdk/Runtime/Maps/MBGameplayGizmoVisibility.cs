using System;

namespace MashBoxSDK.Maps
{
    /// <summary>Shared editor visibility settings for MashBox gameplay gizmos and handles.</summary>
    public static class MBGameplayGizmoVisibility
    {
        private static readonly VisibilityPreference gameplay = new VisibilityPreference("MashBoxSDK.ShowGameplayGizmos");
        private static readonly VisibilityPreference challenges = new VisibilityPreference("MashBoxSDK.ShowChallengeGizmos");
        private static readonly VisibilityPreference trailNetwork = new VisibilityPreference("MashBoxSDK.ShowTrailNetworkGizmos");
        private static readonly VisibilityPreference races = new VisibilityPreference("MashBoxSDK.ShowRaceGizmos");

        private static readonly VisibilityPreference loftSplines = new VisibilityPreference("MashBoxSDK.ShowLoftSplineGizmos");

#if UNITY_6000_0_OR_NEWER
        private static readonly VisibilityPreference chairlifts = new VisibilityPreference("MashBoxSDK.ShowChairliftGizmos");
        public static bool ChairliftsEnabled { get => chairlifts.Value; set => chairlifts.Value = value; }
        public static bool ChairliftsVisible => Visible && ChairliftsEnabled;
#endif

        public static event Action Changed;

        public static bool Visible { get => gameplay.Value; set => gameplay.Value = value; }

        // Store layer choices independently so the master switch never resets them.
        public static bool ChallengesEnabled { get => challenges.Value; set => challenges.Value = value; }
        public static bool TrailNetworkEnabled { get => trailNetwork.Value; set => trailNetwork.Value = value; }
        public static bool RacesEnabled { get => races.Value; set => races.Value = value; }

        public static bool ChallengesVisible => Visible && ChallengesEnabled;
        public static bool TrailNetworkVisible => Visible && TrailNetworkEnabled;
        public static bool RacesVisible => Visible && RacesEnabled;
        public static bool LoftSplinesEnabled { get => loftSplines.Value; set => loftSplines.Value = value; }
        public static bool LoftSplinesVisible => Visible && LoftSplinesEnabled;

        private sealed class VisibilityPreference
        {
            private readonly string key;
#if UNITY_EDITOR
            private bool initialized;
            private bool visible;
#endif

            public VisibilityPreference(string key) => this.key = key;

            public bool Value
            {
                get
                {
#if UNITY_EDITOR
                    if (!initialized)
                    {
                        visible = UnityEditor.EditorPrefs.GetBool(key, true);
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
                    bool changed = Value != value;
                    visible = value;
                    initialized = true;
                    UnityEditor.EditorPrefs.SetBool(key, value);
                    if (changed)
                    {
                        Changed?.Invoke();
                        UnityEditor.SceneView.RepaintAll();
                    }
#endif
                }
            }
        }
    }
}
