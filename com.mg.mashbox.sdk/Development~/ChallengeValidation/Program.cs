using System;
using System.Collections.Generic;
using MashBoxSDK.Maps;

static class Program
{
    static int count;
    static MBChallengeGoal Trick(int step = 0, params string[] tricks) => new MBChallengeGoal
    { stepIndex = step, tricks = new List<string>(tricks.Length == 0 ? new[] { "180" } : tricks) };
    static MBChallengeTier Tier(params MBChallengeGoal[] goals) => new MBChallengeTier { goals = new List<MBChallengeGoal>(goals) };
    static MBChallengeSession Spot(MBChallengeRules rules = null, MBChallengeGoal goal = null, int zones = 1)
        => new MBChallengeSession(Tier(goal ?? Trick()), rules ?? new MBChallengeRules(), false, zones);
    static void Assert(bool condition, string text) { if (!condition) throw new Exception(text); }
    static void Test(string name, Action action) { action(); count++; Console.WriteLine("PASS " + name); }

    static void Main()
    {
        Test("No credit before entry or on stale replay", () => {
            var s = Spot(); s.ReportLandedCombo(1, new[] { "180" }, 0); s.EnterZone(0);
            s.ReportLandedCombo(1, new[] { "180" }, 0); Assert(s.State == MBChallengeState.Running, "stale event credited");
            s.ReportLandedCombo(2, new[] { "180" }, 0); Assert(s.State == MBChallengeState.Completed, "fresh combo failed");
        });
        Test("Spot arms until landing outside volume", () => {
            var s = Spot(); s.EnterZone(0); s.ExitZone(0); s.ReportLandedCombo(1, new[] { "180" }, 0);
            Assert(s.State == MBChallengeState.Completed, "outside landing should count");
        });
        Test("While-inside policy rejects outside landing", () => {
            var s = Spot(new MBChallengeRules { zonePolicy = MBChallengeZonePolicy.WhileInside });
            s.EnterZone(0); s.ExitZone(0); s.ReportLandedCombo(1, new[] { "180" }, 0);
            Assert(s.State == MBChallengeState.Running && s.GetProgress(0) == 0, "outside credited");
        });
        Test("Alternative Spot zones retain presence", () => {
            var s = Spot(new MBChallengeRules { zonePolicy = MBChallengeZonePolicy.WhileInside }, zones: 2);
            s.EnterZone(0); s.EnterZone(1); s.ExitZone(0); s.ReportLandedCombo(1, new[] { "180" }, 0);
            Assert(s.State == MBChallengeState.Completed, "still in second zone");
        });
        Test("Duplicate combo cannot increment twice", () => {
            var g = Trick(); g.target = 2; var s = Spot(goal: g); s.EnterZone(0);
            s.ReportLandedCombo(1, new[] { "180" }, 0); s.ReportLandedCombo(1, new[] { "180" }, 0);
            Assert(s.GetProgress(0) == 1, "duplicate credited");
        });
        Test("Repeated requirements count distinct tricks", () => {
            var s = Spot(goal: Trick(0, "Barspin", "Barspin")); s.EnterZone(0);
            s.ReportLandedCombo(1, new[] { "Barspin" }, 0); Assert(s.GetProgress(0) == 0, "one trick counted twice");
            s.ReportLandedCombo(2, new[] { "Barspin", "barspin" }, 0); Assert(s.State == MBChallengeState.Completed, "two tricks failed");
        });
        Test("Exact sequence rejects extras and wrong order", () => {
            var g = Trick(0, "180", "Barspin"); g.match = MBChallengeMatch.ExactSequence;
            var s = Spot(goal: g); s.EnterZone(0); s.ReportLandedCombo(1, new[] { "Barspin", "180" }, 0);
            s.ReportLandedCombo(2, new[] { "180", "Barspin", "360" }, 0); Assert(s.GetProgress(0) == 0, "inexact passed");
            s.ReportLandedCombo(3, new[] { "180", "Barspin" }, 0); Assert(s.State == MBChallengeState.Completed, "exact failed");
        });
        Test("Any match uses full identifiers", () => {
            var g = Trick(0, "180", "Barspin"); g.match = MBChallengeMatch.ContainsAny;
            var s = Spot(goal: g); s.EnterZone(0); s.ReportLandedCombo(1, new[] { "1180" }, 0);
            Assert(s.GetProgress(0) == 0, "substring matched"); s.ReportLandedCombo(2, new[] { "Barspin" }, 0);
            Assert(s.State == MBChallengeState.Completed, "any match failed");
        });
        Test("Line advances once per zone and confirmed landing", () => {
            var s = new MBChallengeSession(Tier(Trick(0, "Crook"), Trick(1, "Tooth"), Trick(2, "180")), new MBChallengeRules(), true, 3);
            s.EnterZone(0); s.ReportLandedCombo(1, new[] { "Crook", "Tooth", "180" }, 0);
            Assert(s.CurrentStep == 1 && s.GetProgress(1) == 0, "one combo passed multiple steps");
            s.ReportLandedCombo(2, new[] { "Tooth" }, 0); Assert(s.GetProgress(1) == 0, "next zone not entered");
            s.EnterZone(1); s.ReportLandedCombo(3, new[] { "Tooth" }, 0); s.EnterZone(2); s.ReportLandedCombo(4, new[] { "180" }, 0);
            Assert(s.State == MBChallengeState.Completed, "line did not complete");
        });
        Test("Wrong-order policy", () => {
            var tier = Tier(Trick(0), Trick(1)); var s = new MBChallengeSession(tier, new MBChallengeRules(), true, 2);
            s.EnterZone(1); Assert(s.State == MBChallengeState.Failed, "wrong order accepted");
            s = new MBChallengeSession(tier, new MBChallengeRules { failOnWrongOrder = false }, true, 2);
            s.EnterZone(1); Assert(s.State == MBChallengeState.Running && s.CurrentStep == 0, "future entry advanced");
        });
        Test("Score best differs from total", () => {
            var g = new MBChallengeGoal { kind = MBChallengeGoalKind.Score, target = 200, accumulation = MBChallengeAccumulation.Best };
            var s = Spot(goal: g); s.EnterZone(0); s.ReportLandedCombo(1, null, 120); s.ReportLandedCombo(2, null, 120);
            Assert(s.GetProgress(0) == 120, "best was summed");
            g.accumulation = MBChallengeAccumulation.Total; s = Spot(goal: g); s.EnterZone(0);
            s.ReportLandedCombo(1, null, 120); s.ReportLandedCombo(2, null, 120); Assert(s.State == MBChallengeState.Completed, "total not summed");
        });
        Test("Custom signal waits for landing", () => {
            var s = Spot(goal: new MBChallengeGoal { kind = MBChallengeGoalKind.CustomSignal, signal = "manual", target = 10 });
            s.EnterZone(0); s.ReportSignal(1, "wrong", 10); Assert(s.GetProgress(0) == 0, "wrong signal passed");
            s.ReportSignal(2, "manual", 10); Assert(s.State == MBChallengeState.Running, "completed without landing");
            s.ReportLandedCombo(3, null, 0); Assert(s.State == MBChallengeState.Completed, "landing did not finish");
        });
        Test("Visit goal can complete immediately", () => {
            var s = Spot(new MBChallengeRules { requireLanding = false }, new MBChallengeGoal { kind = MBChallengeGoalKind.VisitZone });
            s.EnterZone(0); Assert(s.State == MBChallengeState.Completed, "entry did not complete");
        });
        Test("Bail is terminal and retry state is isolated", () => {
            var tier = Tier(Trick()); var s = new MBChallengeSession(tier, new MBChallengeRules(), false, 1);
            s.EnterZone(0); s.Bail(); s.ReportLandedCombo(1, new[] { "180" }, 0);
            Assert(s.State == MBChallengeState.Failed && s.GetProgress(0) == 0, "bail banked combo");
            var retry = new MBChallengeSession(tier, new MBChallengeRules(), false, 1); Assert(retry.GetProgress(0) == 0, "shared state");
        });
        Test("Timeout boundary and invalid deltas", () => {
            var s = Spot(new MBChallengeRules { timeLimitSeconds = 5 }); s.Tick(float.NaN); s.Tick(-10); s.Tick(4.99f);
            Assert(s.State == MBChallengeState.Running, "early timeout"); s.Tick(.02f); Assert(s.State == MBChallengeState.Failed, "missing timeout");
        });
        Test("Definitions are snapshotted", () => {
            var goal = Trick(); var s = Spot(goal: goal); goal.tricks.Clear(); goal.target = 99;
            s.EnterZone(0); s.ReportLandedCombo(1, new[] { "180" }, 0); Assert(s.State == MBChallengeState.Completed, "definition mutated attempt");
        });
        Test("Missed landing and exit failures", () => {
            var s = Spot(new MBChallengeRules { failOnMissedLanding = true }); s.EnterZone(0); s.ReportLandedCombo(1, new[] { "360" }, 0);
            Assert(s.State == MBChallengeState.Failed, "miss ignored");
            s = Spot(new MBChallengeRules { failOnZoneExit = true }); s.EnterZone(0); s.ExitZone(0);
            Assert(s.State == MBChallengeState.Failed, "exit ignored");
        });
        Test("Invalid definitions rejected", () => {
            Assert(MBChallengeSession.Validate(Tier(), new MBChallengeRules(), false, 1).Count > 0, "empty tier accepted");
            Assert(MBChallengeSession.Validate(Tier(Trick(1)), new MBChallengeRules(), false, 1).Count > 0, "missing step accepted");
            var goal = Trick(); goal.target = float.NaN;
            Assert(MBChallengeSession.Validate(Tier(goal), new MBChallengeRules(), false, 1).Count > 0, "NaN accepted");
        });
        Test("Invalid zones and non-finite input rejected", () => {
            var s = Spot(); s.EnterZone(-1); s.EnterZone(9); s.ReportLandedCombo(1, new[] { "180" }, 0);
            Assert(s.GetProgress(0) == 0, "invalid zone armed"); s.EnterZone(0); s.ReportLandedCombo(2, new[] { "180" }, float.NaN);
            Assert(s.GetProgress(0) == 0, "NaN accepted");
        });
        Test("Outside landing disarms until reentry", () => {
            var g = Trick(); g.target = 2; var s = Spot(goal: g); s.EnterZone(0); s.ExitZone(0);
            s.ReportLandedCombo(1, new[] { "180" }, 0); s.ReportLandedCombo(2, new[] { "180" }, 0);
            Assert(s.GetProgress(0) == 1, "second outside combo credited");
            s.EnterZone(0); s.ReportLandedCombo(3, new[] { "180" }, 0); Assert(s.State == MBChallengeState.Completed, "reentry failed");
        });
        Test("Allowed respawn still clears spatial eligibility", () => {
            var s = Spot(new MBChallengeRules { failOnRespawn = false, zonePolicy = MBChallengeZonePolicy.WhileInside });
            s.EnterZone(0); s.Respawn(); s.ReportLandedCombo(1, new[] { "180" }, 0);
            Assert(s.State == MBChallengeState.Running && s.GetProgress(0) == 0, "old zone survived teleport");
            s.EnterZone(0); s.ReportLandedCombo(2, new[] { "180" }, 0); Assert(s.State == MBChallengeState.Completed, "fresh zone failed");
        });
        Test("Allowed bail clears pending spatial eligibility", () => {
            var s = Spot(new MBChallengeRules { failOnBail = false }); s.EnterZone(0); s.Bail();
            s.ReportLandedCombo(1, new[] { "180" }, 0); Assert(s.GetProgress(0) == 0, "bail left attempt armed");
        });
        Console.WriteLine($"{count} challenge regression tests passed.");
    }
}
