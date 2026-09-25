using System.Collections.Generic;
using MashBoxSDK.Services;
using UnityEngine;

namespace MashBoxSDK.Maps
{
    /// <summary>Connect authoritative local-player trick/landing events here. Combo-ended alone is not a success signal.</summary>
    [AddComponentMenu("MashBox/Maps/Challenge Bridge")]
    [DisallowMultipleComponent]
    public sealed class MBChallengeBridge : MonoBehaviour
    {
        [SerializeField] private MBTrickChallenge challenge;
        [SerializeField, Tooltip("Send successful tiers to the existing map-task activity service.")]
        private bool recordCompletionActivity = true;
        private readonly List<string> pendingTricks = new List<string>();
        private int pendingToken = -1;
        private float pendingScore;
        private bool hasPending;
        private long sourceComboId;
        private long lastSourceComboId = long.MinValue;

        public MBTrickChallenge Challenge => challenge;
        private void OnEnable()
        {
            if (challenge == null) challenge = GetComponentInParent<MBTrickChallenge>();
            if (challenge != null) challenge.TierCompleted += Completed;
            MBChallengeServices.Bailed += ClearPending;
            MBChallengeServices.Respawned += ClearPending;
            MBChallengeServices.ComboStarted += BeginSourceCombo;
            MBChallengeServices.TrickPerformed += AddSourceTrick;
            MBChallengeServices.ComboLanded += LandSourceCombo;
            MBChallengeServices.ComboFailed += FailSourceCombo;
        }
        private void OnDisable()
        {
            if (challenge != null) challenge.TierCompleted -= Completed;
            MBChallengeServices.Bailed -= ClearPending;
            MBChallengeServices.Respawned -= ClearPending;
            MBChallengeServices.ComboStarted -= BeginSourceCombo;
            MBChallengeServices.TrickPerformed -= AddSourceTrick;
            MBChallengeServices.ComboLanded -= LandSourceCombo;
            MBChallengeServices.ComboFailed -= FailSourceCombo;
            ClearPending();
        }

        public void BeginCombo()
        {
            ClearPending();
            if (challenge == null || challenge.State != MBChallengeState.Running || !MBGameplayStateGuard.IsGameplayActive) return;
            pendingToken = challenge.AttemptToken;
            hasPending = true;
        }
        public void AddTrick(string trickId)
        {
            if (!hasPending) BeginCombo();
            if (hasPending && !string.IsNullOrWhiteSpace(trickId)) pendingTricks.Add(trickId.Trim());
        }
        public void SetComboScore(float score)
        {
            if (!hasPending) BeginCombo();
            pendingScore = score;
        }
        public void ConfirmLanding()
        {
            if (challenge != null && hasPending)
                challenge.ReportLandedCombo(pendingToken, challenge.NextEventId(), pendingTricks.ToArray(), pendingScore);
            ClearPending();
        }
        public void Bail() { ClearPending(); challenge?.ReportBail(); }
        public void Respawn() { ClearPending(); challenge?.ReportRespawn(); }
        public void Signal(string signalName) => SignalValue(signalName, 1f);
        public void SignalValue(string signalName, float amount)
        {
            if (challenge != null)
                challenge.ReportSignal(challenge.AttemptToken, challenge.NextEventId(), signalName, amount);
        }
        public void ClearPending()
        {
            pendingTricks.Clear(); pendingScore = 0; pendingToken = -1; hasPending = false;
        }
        private void BeginSourceCombo(long id)
        {
            // The game supplies monotonically increasing local combo IDs, including across retries.
            if (id <= lastSourceComboId) return;
            lastSourceComboId = id;
            sourceComboId = id;
            BeginCombo();
        }
        private void AddSourceTrick(long id, string trick)
        {
            if (hasPending && id == sourceComboId) AddTrick(trick);
        }
        private void LandSourceCombo(long id, float score)
        {
            if (!hasPending || id != sourceComboId) return;
            pendingScore = score;
            ConfirmLanding();
        }
        private void FailSourceCombo(long id)
        {
            if (hasPending && id == sourceComboId) Bail();
        }
        private void Completed(MBTrickChallenge source, string tierId)
        {
            if (recordCompletionActivity)
                MBChallengeServices.Player?.RecordCompletion(source.IsLine ? "Line Challenge" : "Spot Challenge", source.CompletionActivity);
        }
    }
}
