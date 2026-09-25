using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Services;
using MashBoxSDK.Maps.Rigging;
using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps
{
    [Serializable] public class MBChallengeTextEvent : UnityEvent<string> { }

    [Serializable]
    public class MBChallengeTierRecord
    {
        public string tierId;
        public int completions;
        public float bestSeconds;
    }

    [Serializable]
    public class MBChallengeProgress
    {
        public string challengeId;
        public List<MBChallengeTierRecord> tiers = new List<MBChallengeTierRecord>();
    }

    /// <summary>Scene authoring and local-player lifecycle shared by Spot and Line challenges.</summary>
    [DisallowMultipleComponent]
    public abstract class MBTrickChallenge : MonoBehaviour
    {
        [SerializeField] private string challengeId = Guid.NewGuid().ToString("N");
        [SerializeField] private string challengeName = "New Challenge";
        [SerializeField, TextArea] private string description;
        [SerializeField] private MBChallengeStartMode startMode = MBChallengeStartMode.EnterZone;
        [SerializeField, Tooltip("After failing, entering the first zone starts a fresh attempt.")]
        private bool retryOnZoneEntry = true;
        [SerializeField] private MBChallengeRules rules = new MBChallengeRules();
        [SerializeField] private List<MBChallengeTier> tiers = new List<MBChallengeTier>();
        [SerializeField] private int selectedTier;
        [SerializeField, Tooltip("Ordered Line steps, or the alternative entry volumes for a Spot.")]
        private List<MBChallengeZone> zones = new List<MBChallengeZone>();
        [Header("Events")]
        [SerializeField] private UnityEvent onStarted;
        [SerializeField] private UnityEvent onProgressChanged;
        [SerializeField] private MBIntEvent onStepChanged;
        [SerializeField] private MBChallengeTextEvent onTierCompleted;
        [SerializeField] private MBChallengeTextEvent onFailed;
        [SerializeField] private UnityEvent onReset;

        private MBChallengeSession session;
        private MBChallengeProgress saved = new MBChallengeProgress();
        private readonly HashSet<int> occupied = new HashSet<int>();
        private int attemptToken;
        private long eventSequence;
        private MBChallengeState notifiedState;
        private int notifiedStep;
        private string attemptTierId;
        private string attemptTierTitle;
        private Transform trackedPlayer;

        public abstract bool IsLine { get; }
        public string ChallengeId => challengeId;
        public string ChallengeName { get => challengeName; set => challengeName = value; }
        public string Description => description;
        public MBChallengeStartMode StartMode => startMode;
        public bool RetryOnZoneEntry => retryOnZoneEntry;
        public MBChallengeRules Rules => rules;
        public List<MBChallengeTier> Tiers => tiers;
        public List<MBChallengeZone> Zones => zones;
        public int SelectedTier => selectedTier;
        public int AttemptToken => attemptToken;
        public MBChallengeSession Session => session;
        public MBChallengeState State => session?.State ?? MBChallengeState.Ready;
        public event Action Changed;
        public event Action<MBTrickChallenge, string> TierCompleted;

        protected virtual void OnEnable()
        {
            MBChallengeServices.Bailed += ReportBail;
            MBChallengeServices.Respawned += ReportRespawn;
        }

        protected virtual void OnDisable()
        {
            MBChallengeServices.Bailed -= ReportBail;
            MBChallengeServices.Respawned -= ReportRespawn;
            ResetAttempt();
        }

        protected virtual void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(challengeId)) challengeId = Guid.NewGuid().ToString("N");
            selectedTier = Mathf.Clamp(selectedTier, 0, Mathf.Max(0, tiers.Count - 1));
        }

        private void Update()
        {
            if (!MBGameplayStateGuard.IsGameplayActive) return;
            var player = MBChallengeServices.Player;
            Transform root = player?.LocalPlayerRoot;
            if (root != trackedPlayer)
            {
                if (trackedPlayer != null) ReportRespawn();
                occupied.Clear();
                trackedPlayer = root;
            }
            if (root != null && player.IsLocalPlayerAlive)
            {
                // A single local-player point avoids remote riders and multi-collider double entries.
                for (int i = 0; i < zones.Count; i++)
                {
                    bool contains = zones[i] != null && zones[i].isActiveAndEnabled && zones[i].Contains(root.position);
                    if (contains && !occupied.Contains(i)) EnterZone(i);
                    else if (!contains && occupied.Contains(i)) ExitZone(i);
                }
            }
            session?.Tick(Time.deltaTime);
            NotifyState(false);
        }

        public List<string> GetValidationErrors()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(challengeId)) errors.Add("Challenge ID is missing.");
            if (string.IsNullOrWhiteSpace(challengeName)) errors.Add("Challenge name is missing.");
            if (zones.Count == 0 || zones.Any(zone => zone == null)) errors.Add("Assign every zone.");
            if (zones.Distinct().Count() != zones.Count) errors.Add("A zone is assigned more than once.");
            foreach (var zone in zones)
                if (zone != null && zone.GetComponentInParent<MBTrickChallenge>() != this)
                    errors.Add($"Zone '{zone.name}' must belong to this challenge hierarchy.");
            if (tiers.Count == 0) errors.Add("Add at least one tier.");
            var ids = new HashSet<string>();
            foreach (var tier in tiers)
            {
                if (tier == null) { errors.Add("Remove the missing tier."); continue; }
                if (string.IsNullOrWhiteSpace(tier.id) || !ids.Add(tier.id)) errors.Add("Each tier needs a unique, non-empty ID.");
                if (string.IsNullOrWhiteSpace(tier.title)) errors.Add("Give every tier a title.");
                errors.AddRange(MBChallengeSession.Validate(tier, rules, IsLine, zones.Count));
            }
            return errors;
        }

        public void StartChallenge() => StartTier(selectedTier);

        public void StartTier(int index)
        {
            if (!MBGameplayStateGuard.IsGameplayActive || index < 0 || index >= tiers.Count) return;
            var errors = GetValidationErrors();
            if (errors.Count != 0) { Debug.LogWarning(string.Join("\n", errors), this); return; }
            attemptToken++;
            selectedTier = index;
            attemptTierId = tiers[index].id;
            attemptTierTitle = tiers[index].title;
            session = new MBChallengeSession(tiers[index], rules, IsLine, zones.Count);
            notifiedState = MBChallengeState.Running;
            notifiedStep = 0;
            occupied.Clear();
            onStarted?.Invoke();
            Changed?.Invoke();
        }

        public void ResetAttempt()
        {
            attemptToken++;
            session = null;
            occupied.Clear();
            notifiedState = MBChallengeState.Ready;
            onReset?.Invoke();
            Changed?.Invoke();
        }

        public void EnterZone(int index)
        {
            if (!MBGameplayStateGuard.IsGameplayActive || index < 0 || index >= zones.Count) return;
            if (!occupied.Add(index)) return;
            bool first = !IsLine || index == 0;
            if (first && ((State == MBChallengeState.Ready && startMode == MBChallengeStartMode.EnterZone)
                || (State == MBChallengeState.Failed && retryOnZoneEntry)))
            {
                StartChallenge();
                occupied.Add(index);
            }
            session?.EnterZone(index);
            NotifyState();
        }

        public void ExitZone(int index)
        {
            occupied.Remove(index);
            session?.ExitZone(index);
            NotifyState();
        }

        /// <summary>Adapter must capture the token when the combo starts, not when it lands.</summary>
        public void ReportLandedCombo(int token, long eventId, string[] tricks, float score)
        {
            if (token != attemptToken || !MBGameplayStateGuard.IsGameplayActive) return;
            session?.ReportLandedCombo(eventId, tricks, score);
            NotifyState();
        }

        public void ReportSignal(int token, long eventId, string signalName, float amount)
        {
            if (token != attemptToken || !MBGameplayStateGuard.IsGameplayActive) return;
            session?.ReportSignal(eventId, signalName, amount);
            NotifyState();
        }

        public long NextEventId() => ++eventSequence;
        public void ReportBail() { attemptToken++; session?.Bail(); occupied.Clear(); NotifyState(); }
        public void ReportRespawn() { attemptToken++; session?.Respawn(); occupied.Clear(); NotifyState(); }

        private void NotifyState(bool progressChanged = true)
        {
            if (session == null) return;
            var notifyingSession = session;
            if (notifiedStep != session.CurrentStep)
            {
                notifiedStep = session.CurrentStep;
                occupied.Clear();
                onStepChanged?.Invoke(notifiedStep);
                if (session != notifyingSession) return;
            }
            if (notifiedState != session.State)
            {
                notifiedState = session.State;
                progressChanged = true;
                if (session.State == MBChallengeState.Completed)
                {
                    var record = saved.tiers.Find(value => value.tierId == attemptTierId);
                    if (record == null)
                    {
                        record = new MBChallengeTierRecord { tierId = attemptTierId, bestSeconds = session.ElapsedSeconds };
                        saved.tiers.Add(record);
                    }
                    record.completions++;
                    record.bestSeconds = Mathf.Min(record.bestSeconds, session.ElapsedSeconds);
                    string completedTierId = attemptTierId;
                    // Publish the gameplay event before author-wired callbacks can start another tier.
                    TierCompleted?.Invoke(this, completedTierId);
                    onTierCompleted?.Invoke(completedTierId);
                }
                else if (session.State == MBChallengeState.Failed) onFailed?.Invoke(session.FailureReason);
            }
            if (progressChanged)
            {
                onProgressChanged?.Invoke();
                Changed?.Invoke();
            }
        }

        public string CaptureProgress()
        {
            saved.challengeId = challengeId;
            return JsonUtility.ToJson(saved);
        }

        public bool RestoreProgress(string json)
        {
            try
            {
                var data = JsonUtility.FromJson<MBChallengeProgress>(json);
                if (data == null || data.challengeId != challengeId || data.tiers == null) return false;
                if (data.tiers.Any(record => record == null || string.IsNullOrWhiteSpace(record.tierId) || record.completions < 0 || record.bestSeconds < 0
                    || float.IsNaN(record.bestSeconds) || float.IsInfinity(record.bestSeconds))) return false;
                if (data.tiers.Select(record => record.tierId).Distinct().Count() != data.tiers.Count) return false;
                saved = data;
                Changed?.Invoke();
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        public bool HasCompletedTier(string tierId) => saved.tiers.Any(record => record.tierId == tierId && record.completions > 0);
        public string CompletionActivity => $"{challengeName} {attemptTierTitle}";

        public void RegenerateIdentity()
        {
            challengeId = Guid.NewGuid().ToString("N");
            foreach (var tier in tiers) if (tier != null) tier.id = Guid.NewGuid().ToString("N");
        }

        public void RepairTierIdentities()
        {
            var seen = new HashSet<string>();
            foreach (var tier in tiers)
            {
                if (tier == null) continue;
                if (string.IsNullOrWhiteSpace(tier.id) || !seen.Add(tier.id))
                {
                    tier.id = Guid.NewGuid().ToString("N");
                    seen.Add(tier.id);
                }
            }
        }
    }
}
