# Spot and Line challenges

Open **MashBox Map Tools → Gameplay → Challenges → Spot Challenges / Line Challenges**. Use **Add Spot Challenge** or **Add Line Challenge** alongside the existing races, Expert Lines, and B.I.K.E.S tools.

The SDK now includes scene authoring, a runtime evaluator, an editor rules preview, validation, export metadata, and a game telemetry interface. The game's full trick/score stream still needs to call that interface; the existing current-trick string is deliberately not treated as a complete landed combo.

## Author a challenge

1. Add a Spot or Line. A Spot starts with one box zone; a Line starts with three step zones. Each receives Basic, Am, Pro, and Legendary starter tiers and a Challenge Bridge.
2. Move, rotate, and resize the zones using their transforms and Box Colliders. Keep colliders marked **Is Trigger**. Scene gizmos show volumes, labels, and the Line path. Zones test the local character root position, not remote riders or individual bike colliders.
3. Select the challenge and expand **Tiers → Goals**. Set goal labels, the required step, targets, and matching rules. Starter goals are editable examples, not copies of shipped BMX Streets challenge assets.
4. Choose the **Active Tier**. Each attempt evaluates one selected tier. Use **Add Tier** to copy the active tier with a fresh identity. Legendary is an editable tier, not an implicit dependency on three other tiers.
5. Set **Rules**, the start mode, and retry policy. Use **Preview Rules**, start a preview, enter a zone, and send sample landed combos, scores, signals, bails, or time advances.
6. Use **Add Active Tier to Map Tasks** to create a task for completing that named challenge/tier. Run Gameplay validation before export.

Adding a Line step also adds an editable 180 goal to every tier. Removing or reordering zones requires reviewing the goals' step assignments. Validation reports missing steps and empty tiers.

## Goals and rules

| Setting | Behavior |
| --- | --- |
| Trick Combo | A successful landed combo satisfies the configured trick list. Target counts matching combos. |
| Contains All | All listed tricks must occur in that combo; repeated list entries require repeated occurrences. |
| Contains Any | At least one listed trick must occur. |
| Exact Sequence | Full ordered list must match, with no extra tricks. |
| Score | Counts landed score, either total across the attempt or the best single combo. |
| Custom Signal | Counts named numeric events; useful for manual distance, time in a state, collectibles, or custom interactions. The sender determines units. |
| Visit Zone | Counts fresh zone entries. |
| Arm Until Landing | Entry permits the next landed combo even outside the zone. After an outside landing, reenter to earn further progress. |
| While Inside | Leaving all applicable zones stops progress. |
| Require Landing | Step completion waits for a confirmed landing. Trick and score goals always consume landed results regardless of this setting. |
| Fail On Missed Landing | A landing that leaves any current-step goal incomplete fails the attempt. Disable to build progress over multiple combos. |
| Fail On Wrong Order | Entering a future Line step fails. If disabled, future-step entry is ignored; steps still advance sequentially. |
| Fail On Zone Exit | Leaving the current step, or all Spot zones, fails the attempt. |
| Time Limit | Total attempt duration in gameplay seconds; zero means unlimited. |
| Retry On Zone Entry | After failure, reenter the first Line zone or a Spot zone to start again. Completed attempts require an explicit restart. |

Trick identifiers match whole strings, case-insensitively, with no regex or substring guessing. Enter the identifiers supplied by the game's telemetry adapter. For two tricks in the **same combo**, put both in one Contains All goal. Two separate goals can be completed on separate landings when missed-landing failure is disabled.

**Preview Rules** runs an isolated evaluator without moving the player, calling UnityEvents, or granting/saving rewards. Restart its preview after changing authored rules. It is not an in-game playtest.

## Game integration

`MashBoxSDK.Services.MBChallengeServices` is the local-player input boundary. `SDKChallengePlayerService`, in the existing MashBoxBridge assembly, installs local-player position/alive-state access, forwards kill/respawn notifications, and sends successful tiers into the existing activity service. The SDK assembly does not depend on MashBoxBridge.

The authoritative trick system must call these methods for the **local player only**:

```csharp
// Monotonically increasing ID for each local combo, including across retries.
MBChallengeServices.BeginLocalCombo(comboId);
MBChallengeServices.AddLocalTrick(comboId, "180");
MBChallengeServices.AddLocalTrick(comboId, "Barspin");

// Emit exactly one terminal result after adjudicating the landing/bail.
MBChallengeServices.LandLocalCombo(comboId, landedScore);
// Or: MBChallengeServices.FailLocalCombo(comboId);
```

Send individual stable trick IDs, not a formatted HUD string. An empty successful combo can still confirm visit/custom-signal goals. A generic combo-ended event is insufficient because the existing game also emits it for failed combos. Do not report success until bail/landing status is definitive.

For UnityEvent wiring or custom game adapters, a scene's `MBChallengeBridge` exposes `BeginCombo`, `AddTrick`, `SetComboScore`, `ConfirmLanding`, `Bail`, `Respawn`, `Signal`, and `SignalValue`. Choose the global telemetry route or the direct bridge route for a given producer; do not report the same combo through both. Direct wiring must call BeginCombo at combo start and ConfirmLanding once. The global route rejects duplicate/out-of-order combo IDs.

Low-level adapters can call `ReportLandedCombo(token, eventId, tricks, score)` and `ReportSignal(token, eventId, name, amount)` on the challenge. Capture `AttemptToken` when work starts; use a unique event ID across both report methods. Restart, reset, bail, and respawn invalidate old tokens. This prevents pending work from completing a later attempt.

Runtime entry points also include `StartChallenge()`, `StartTier(index)`, and `ResetAttempt()`. UnityEvents expose start, progress changes, step changes, failure reason, reset, and completed tier ID. `Session` exposes per-goal progress, elapsed time, state, and failure reason for a game HUD. Start modes are zone entry or explicit/manual calls; automatic teleport and marker locking are not implemented.

## Map tasks, saves, and export

Successful tiers report an activity with verb `Completed`, context `Spot Challenge` or `Line Challenge`, and the authored challenge/tier names. The generated Standard map task uses those fields, preserving the existing task schema and task HUD compatibility. Rename matching tasks after renaming a challenge or tier; activity matching currently uses names, so use unique challenge names.

Tier completion counts and best times are separate from attempt progress. `CaptureProgress()` returns JSON for the game's profile save system; `RestoreProgress(json)` restores matching challenge records without firing reward/completion events. **This SDK does not automatically write profile saves or grant inventory rewards.** Connect the completed-tier event to the game's reward system and store the captured progress through its existing persistence layer. `HasCompletedTier(id)` supports one-time reward checks.

Challenges and tiers have stable IDs. When duplicating an entire scene challenge, use **New Identity for Duplicated Challenge**. When duplicating tier array entries, use **Repair Duplicated Tier IDs**; this preserves existing unique IDs. Validation blocks duplicate challenge IDs and invalid tier definitions.

Map bundles retain the serialized components. Challenge JSON manifests now also include a `trickChallenges` list with IDs, rules, tier goals, start/retry settings, and zone transforms. Existing categories and tasks retain their format. Zone local-to-world matrices and local box geometry preserve nonuniform parent transforms; the world center/size/rotation fields are convenient summaries.

## Validation

The regression runner in `Development~/ChallengeValidation` exercises zone policies, ordered steps, landing confirmation, trick matching, duplicate events, score aggregation, failure, timeout, and snapshot isolation. Run with:

```powershell
dotnet run --project 'Development~/ChallengeValidation/ChallengeValidation.csproj'
```

`compile-unity.cjs <UnityProjectPath> <UnityEditorVersionDirectory>` compiles the runtime, tools, and bridge against an already-imported Unity project's compiler references. Its outputs stay in the ignored validation artifacts directory. Compilation does not replace an in-game playtest of the telemetry/HUD/save integration.
