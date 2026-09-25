using System;
using System.Collections.Generic;
using System.Linq;

namespace MashBoxSDK.Maps
{
    public enum MBChallengeGoalKind { TrickCombo, Score, CustomSignal, VisitZone }
    public enum MBChallengeMatch { ContainsAll, ContainsAny, ExactSequence }
    public enum MBChallengeAccumulation { Total, Best }
    public enum MBChallengeZonePolicy { ArmUntilLanding, WhileInside }
    public enum MBChallengeStartMode { EnterZone, Manual }
    public enum MBChallengeState { Ready, Running, Completed, Failed }

    [Serializable]
    public class MBChallengeGoal
    {
        public string label = "Land a 180";
        public MBChallengeGoalKind kind;
        public int stepIndex;
        public float target = 1;
        public MBChallengeAccumulation accumulation;
        public List<string> tricks = new List<string> { "180" };
        public MBChallengeMatch match;
        public string signal = "manual-distance";

        internal MBChallengeGoal Copy() => new MBChallengeGoal
        {
            label = label, kind = kind, stepIndex = stepIndex, target = target,
            accumulation = accumulation, tricks = tricks == null ? new List<string>() : new List<string>(tricks),
            match = match, signal = signal
        };
    }

    [Serializable]
    public class MBChallengeTier
    {
        public string id = Guid.NewGuid().ToString("N");
        public string title = "Basic";
        public List<MBChallengeGoal> goals = new List<MBChallengeGoal>();
    }

    [Serializable]
    public class MBChallengeRules
    {
        public MBChallengeZonePolicy zonePolicy = MBChallengeZonePolicy.ArmUntilLanding;
        public bool requireLanding = true;
        public bool failOnBail = true;
        public bool failOnRespawn = true;
        public bool failOnZoneExit;
        public bool failOnWrongOrder = true;
        public bool failOnMissedLanding;
        public float timeLimitSeconds;

        internal MBChallengeRules Copy() => (MBChallengeRules)MemberwiseClone();
    }

    /// <summary>Per-attempt evaluator. No scene, player, save, or Unity dependencies.</summary>
    public sealed class MBChallengeSession
    {
        private readonly List<MBChallengeGoal> goals;
        private readonly MBChallengeRules rules;
        private readonly float[] progress;
        private readonly HashSet<int> inside = new HashSet<int>();
        private readonly HashSet<long> events = new HashSet<long>();
        private readonly int stepCount;
        private readonly int zoneCount;
        private readonly bool line;
        private bool armed;

        public MBChallengeState State { get; private set; } = MBChallengeState.Ready;
        public string FailureReason { get; private set; } = string.Empty;
        public int CurrentStep { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public int GoalCount => progress.Length;
        public float GetProgress(int index) => progress[index];
        public bool IsGoalComplete(int index) => progress[index] >= goals[index].target;

        public MBChallengeSession(MBChallengeTier tier, MBChallengeRules settings, bool isLine, int zoneCount)
        {
            if (tier == null || settings == null) throw new ArgumentNullException();
            var errors = Validate(tier, settings, isLine, zoneCount);
            if (errors.Count != 0) throw new ArgumentException(string.Join(" ", errors));
            goals = tier.goals.Select(goal => goal.Copy()).ToList();
            progress = new float[goals.Count];
            rules = settings.Copy();
            line = isLine;
            this.zoneCount = zoneCount;
            stepCount = isLine ? zoneCount : 1;
            State = MBChallengeState.Running;
        }

        public static List<string> Validate(MBChallengeTier tier, MBChallengeRules settings, bool line, int zones)
        {
            var errors = new List<string>();
            if (zones < 1) errors.Add("Add at least one zone.");
            if (settings == null || !Finite(settings.timeLimitSeconds) || settings.timeLimitSeconds < 0)
                errors.Add("Time limit must be a finite number of seconds, zero for unlimited.");
            if (tier == null || tier.goals == null || tier.goals.Count == 0)
            {
                errors.Add("Add at least one goal to every tier.");
                return errors;
            }
            foreach (var goal in tier.goals)
            {
                if (goal == null) { errors.Add("Remove the missing goal."); continue; }
                if (goal.stepIndex < 0 || goal.stepIndex >= (line ? zones : 1))
                    errors.Add($"Goal '{goal.label}' refers to a missing step. Spot goals use step 0.");
                if (!Finite(goal.target) || goal.target <= 0) errors.Add($"Goal '{goal.label}' needs a positive target.");
                if (goal.kind == MBChallengeGoalKind.TrickCombo && (goal.tricks == null || goal.tricks.Count == 0 || goal.tricks.Any(string.IsNullOrWhiteSpace)))
                    errors.Add($"Goal '{goal.label}' needs non-empty trick identifiers.");
                if (goal.kind == MBChallengeGoalKind.CustomSignal && string.IsNullOrWhiteSpace(goal.signal))
                    errors.Add($"Goal '{goal.label}' needs a signal name.");
            }
            for (int step = 0; step < (line ? zones : 1); step++)
                if (!tier.goals.Any(goal => goal != null && goal.stepIndex == step))
                    errors.Add($"Tier '{tier.title}' needs at least one goal for step {step + 1}.");
            return errors;
        }

        public void EnterZone(int index)
        {
            if (State != MBChallengeState.Running || index < 0 || index >= zoneCount) return;
            if (!inside.Add(index)) return;
            if (line && index != CurrentStep)
            {
                if (index > CurrentStep && rules.failOnWrongOrder) Fail("Entered a later step out of order.");
                return;
            }
            armed = true;
            Apply(MBChallengeGoalKind.VisitZone, null, 1, null);
            CheckStep(false);
        }

        public void ExitZone(int index)
        {
            if (!inside.Remove(index) || State != MBChallengeState.Running) return;
            if (line && index != CurrentStep) return;
            if (!line && inside.Count != 0) return;
            if (rules.failOnZoneExit) { Fail("Left the challenge zone."); return; }
            if (rules.zonePolicy == MBChallengeZonePolicy.WhileInside) armed = false;
        }

        public void ReportLandedCombo(long eventId, IReadOnlyList<string> tricks, float score)
        {
            if (!Accept(eventId) || !Finite(score) || score < 0) return;
            Apply(MBChallengeGoalKind.TrickCombo, tricks, 1, null);
            Apply(MBChallengeGoalKind.Score, null, score, null);
            CheckStep(true);
            // A new attempt at the feature must enter a zone again if the rider landed outside it.
            armed = line ? inside.Contains(CurrentStep) : inside.Count > 0;
        }

        public void ReportSignal(long eventId, string signal, float value)
        {
            if (!Finite(value) || value <= 0 || !Accept(eventId)) return;
            Apply(MBChallengeGoalKind.CustomSignal, null, value, signal);
            CheckStep(false);
        }

        private bool Accept(long eventId)
        {
            // Consume even out-of-zone events: replaying them after entry must not earn credit.
            return State == MBChallengeState.Running && events.Add(eventId) && armed;
        }

        private void Apply(MBChallengeGoalKind kind, IReadOnlyList<string> tricks, float value, string signal)
        {
            for (int i = 0; i < goals.Count; i++)
            {
                var goal = goals[i];
                if (goal.kind != kind || goal.stepIndex != CurrentStep) continue;
                if (kind == MBChallengeGoalKind.TrickCombo && !Matches(goal, tricks)) continue;
                if (kind == MBChallengeGoalKind.CustomSignal && !string.Equals(goal.signal, signal, StringComparison.Ordinal)) continue;
                progress[i] = Math.Min(goal.target, goal.accumulation == MBChallengeAccumulation.Best
                    ? Math.Max(progress[i], value) : progress[i] + value);
            }
        }

        private void CheckStep(bool landed)
        {
            bool complete = true;
            for (int i = 0; i < goals.Count; i++)
                if (goals[i].stepIndex == CurrentStep && !IsGoalComplete(i)) complete = false;
            if (!complete)
            {
                if (landed && rules.failOnMissedLanding) Fail("Landed without completing the step goals.");
                return;
            }
            if (rules.requireLanding && !landed) return;
            if (!line || CurrentStep == stepCount - 1) State = MBChallengeState.Completed;
            else
            {
                CurrentStep++;
                // Overlapping zones cannot automatically advance multiple steps on one event.
                armed = false;
                inside.Clear();
            }
        }

        public void Tick(float seconds)
        {
            if (State != MBChallengeState.Running || !Finite(seconds) || seconds <= 0) return;
            ElapsedSeconds += seconds;
            if (rules.timeLimitSeconds > 0 && ElapsedSeconds >= rules.timeLimitSeconds) Fail("Time limit expired.");
        }

        public void Bail()
        {
            inside.Clear(); armed = false;
            if (rules.failOnBail) Fail("Bailed.");
        }
        public void Respawn()
        {
            inside.Clear(); armed = false;
            if (rules.failOnRespawn) Fail("Reset or teleported.");
        }
        public void Fail(string reason)
        {
            if (State != MBChallengeState.Running) return;
            State = MBChallengeState.Failed;
            FailureReason = reason;
        }

        private static bool Matches(MBChallengeGoal goal, IReadOnlyList<string> actual)
        {
            if (actual == null) return false;
            if (goal.match == MBChallengeMatch.ExactSequence)
                return goal.tricks.SequenceEqual(actual, StringComparer.OrdinalIgnoreCase);
            // Multiset matching preserves repeated requirements, e.g. two Barspins in one combo.
            var remaining = new List<string>(actual);
            foreach (string required in goal.tricks)
            {
                int index = remaining.FindIndex(value => string.Equals(value, required, StringComparison.OrdinalIgnoreCase));
                if (goal.match == MBChallengeMatch.ContainsAny && index >= 0) return true;
                if (index < 0 && goal.match == MBChallengeMatch.ContainsAll) return false;
                if (index >= 0) remaining.RemoveAt(index);
            }
            return goal.match == MBChallengeMatch.ContainsAll;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
