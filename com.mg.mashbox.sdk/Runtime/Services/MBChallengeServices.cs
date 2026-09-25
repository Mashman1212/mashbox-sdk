using System;
using UnityEngine;

namespace MashBoxSDK.Services
{
    public interface IMBChallengePlayerService
    {
        Transform LocalPlayerRoot { get; }
        bool IsLocalPlayerAlive { get; }
        void RecordCompletion(string kind, string challengeAndTier);
    }

    /// <summary>Game-owned local-player input. Send BeginCombo before any trick, then exactly one landing or failure.</summary>
    public static class MBChallengeServices
    {
        public static IMBChallengePlayerService Player { get; private set; }
        public static event Action Bailed;
        public static event Action Respawned;
        public static event Action<long> ComboStarted;
        public static event Action<long, string> TrickPerformed;
        public static event Action<long, float> ComboLanded;
        public static event Action<long> ComboFailed;

        public static void SetPlayerService(IMBChallengePlayerService service) => Player = service;
        public static void ReportLocalBail() => Bailed?.Invoke();
        public static void ReportLocalRespawn() => Respawned?.Invoke();
        public static void BeginLocalCombo(long comboId) => ComboStarted?.Invoke(comboId);
        public static void AddLocalTrick(long comboId, string trickId) => TrickPerformed?.Invoke(comboId, trickId);
        public static void LandLocalCombo(long comboId, float score) => ComboLanded?.Invoke(comboId, score);
        public static void FailLocalCombo(long comboId) => ComboFailed?.Invoke(comboId);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Player = null; Bailed = null; Respawned = null; ComboStarted = null;
            TrickPerformed = null; ComboLanded = null; ComboFailed = null;
        }
    }
}
