using MashBoxBridge.Common.Commands;
using MashBoxSDK.Services;
using UnityEngine;

namespace MashBoxBridge.Common.Sys
{
    /// <summary>Keeps the SDK independent of the game's command and character assemblies.</summary>
    public sealed class SDKChallengePlayerService : IMBChallengePlayerService
    {
        public Transform LocalPlayerRoot => CommandSystemServiceHandler.LocalCharacterManager?.Root;
        public bool IsLocalPlayerAlive => CommandSystemServiceHandler.LocalCharacterManager != null
            && CommandSystemServiceHandler.LocalCharacterManager.IsLocalPlayer
            && CommandSystemServiceHandler.LocalCharacterManager.IsAlive;
        public void RecordCompletion(string kind, string challengeAndTier)
            => ActivityTrackingService.RecordActivity("Completed", kind, challengeAndTier, false);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            MBChallengeServices.SetPlayerService(new SDKChallengePlayerService());
            CommandSystemServiceHandler.OnLocalPlayerKilled -= MBChallengeServices.ReportLocalBail;
            CommandSystemServiceHandler.OnLocalPlayerKilled += MBChallengeServices.ReportLocalBail;
            CommandSystemServiceHandler.OnLocalPlayerRespawned -= MBChallengeServices.ReportLocalRespawn;
            CommandSystemServiceHandler.OnLocalPlayerRespawned += MBChallengeServices.ReportLocalRespawn;
        }
    }
}
