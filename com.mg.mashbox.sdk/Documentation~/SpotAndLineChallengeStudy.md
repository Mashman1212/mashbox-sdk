# BMX Streets Spot and Line challenges: study and SDK proposal

Studied 2026-09-23. This is a source and serialized-asset review, not a Unity playtest or an implemented port. Proposed SDK names and behavior below are recommendations.

## Source location

The screenshot points to `D:\BMX Streets`. That checkout contains some MashBox core code but no Spot/Line challenge implementation was found in its Assets. The separate `D:\BMXStreets` checkout contains the newer MashBox gameplay systems.

The original Spot/Line implementation and authored examples were found under:

`D:\MG-PC\BMX Streets\Assets\MG Core\C-R-I-D\MG Challenge System`

An older challenge implementation also exists under `D:\MG\BMX Streets`. The findings here use the MG-PC version, including its base prefabs and challenge data.

## What made the old system useful

Spot and Line were configurations of a shared condition framework. Challenge data described goals and rewards; scene objects determined where and when those goals could progress; the challenge runner managed staging, retries, failures, and completion.

| Layer | Original implementation | Responsibility |
| --- | --- | --- |
| Challenge definition | `SmartData_Challenge.cs` | Title, type, condition sets, prefab, session-marker policy, lifecycle, saved statistics |
| Goal/reward tier | `Condition Data/ChallengeConditionSetData.cs` | Group conditions, evaluate completion, award stars/rewards, display progress |
| Individual goal | `Condition Data/SmartDataChallengeCondition.cs` | Counts, numeric/string targets, trick matching, progress freeze, failure/reset behavior |
| Scene evaluation | `Challenge Elements/ConditionAbilityBehaviour.cs`, `ConditionZone.cs`, `ConditionSetBehaviour.cs` | Observe tricks, associate conditions with zones, enable ordered child conditions |
| Attempt lifecycle | `ChallengeBehaviourNew.cs` | Stage, teleport, reset, soft fail, stop, update events and statistics |

### Spot behavior

The base Spot prefab freezes condition progression on reset. Its condition-zone enter and stay events unfreeze progression. **Its exit event has no calls**: this is not equivalent to requiring the rider to remain inside a collider continuously. Entering the spot can enable an attempt that continues outside the trigger.

The runner explicitly avoids forcing another teleport when challenge contents are already active and session-marker placement is allowed. That supports repeated attempts from the player's own marker. Soft failure marks active conditions failed, waits 500 ms, resets conditions, and invokes a soft-reset event.

Source: `StandardChallengeTypes/Spot/Challenge_(Spot)_Base Spot Challenge.prefab`; `ChallengeBehaviourNew.cs`, especially freeze/reset methods and `Data_OnStageCallback`.

The authored **Bonk Tower** assets contain:

- Basic: Wall Ride, target count 1.
- Am: 200 points.
- Pro: Barspin and Whopper goals, each target count 1.
- Legendary: references the Basic, Am, and Pro condition sets; hidden during runtime, with horizontal combined goal formatting.

The Legendary references were checked against the tier assets' GUIDs. This demonstrates composite tiers. Exact playable timing and inherited prefab wiring still need a Unity parity test.

### Line behavior

The base Line prefab has numbered condition zones. Enter/stay callbacks unfreeze conditions for that zone's index and activate zone checking. Landing listeners check active zones; unmet conditions invoke a soft-fail action. Another landing listener freezes progression again.

The authored **Back Alley Buster** Basic set contains Crook at index 0, Tooth at index 1, and 180 at index 2. Its set disables completion checks on each individual condition passing and instead checks on the landing-confirmation event.

Distinguish two mechanisms: numbered zones associate goals with locations; `ConditionSetBehaviour.requireOrderedCompletion` separately enables child conditions in hierarchy order. An ordered index alone does not prove that every authored Line strictly rejects visiting zones out of order. Make that an explicit SDK policy.

Source: `StandardChallengeTypes/Line/Challenge_Base Line Challenge_(Line) Variant.prefab`; `ConditionZone.cs`; `ConditionSetBehaviour.cs`; `Game Challenge Data/Challenge_(Line)_Back Alley Buster_ConditionSet_Basic.asset` and its three Basic condition assets.

### Rules worth retaining

- Numeric counts and targets, increment/record styles, string targets, inverse and end conditions.
- Optional conditions and independent visibility in the brief and runtime progress display.
- Trick matching against current or last combos; evaluation on performance, combo confirmation, or unfreeze.
- Repeat-count goals, per-zone condition indices, permanent-pass conditions that survive reset.
- Multiple tiers and composite tiers, stars/rewards, attempt count, completion count, time spent, and best completion time.
- Events for stage, progress, completion, failure, reset, and shutdown.

Do not mechanically copy every legacy flag. The ability-data overload of `PlayerAbilityCombo.ComboMatch` uses string contains/equality and ignores its `containsAnyOfAbilityCombo` argument (that branch is commented out). `exactMatchAbilities` is not passed by the condition's matching methods. Repeated trick matching uses a regex over combo text. These are reasons to define clear matching semantics in the port.

## Current SDK fit

`Runtime/Maps/MBMapTaskList.cs` already provides task names, task kinds, verb/preposition/adjective fields, targets, and counts. It does not define the old challenge's zones, tiers, landing policy, or retry lifecycle.

`Runtime/Maps/MBExpertLine.cs` provides a named line, time limit, gate visualization, state notifications, and UnityEvents. It is not the old trick-objective Line challenge. Its `AllowGroundedTouches` accessor currently always returns true. Preserve its existing use when adding the new challenge style.

`Runtime/MashBoxBridge/Common/Interfaces/IPlayerTrickGameplay.cs` exposes combo-ended notification, whether a combo is running, and a current-trick string. `ActivityTrackingService.cs` forwards activity descriptions. Those interfaces alone do not provide a full, immutable landed-combo result with score, trick sequence, player identity, and failure reason.

The newer game's `D:\BMXStreets\Assets\MashBox\Core\Runtime\Gameplay\ChallengeSystems\SdkChallengeActivityRecorder.cs` already joins SDK features to activity tracking and some landed-trick handling. It is a useful integration point to investigate, but a port needs game-side work as well as SDK authoring components.

## Recommended authoring model

Provide **Spot Challenge** and **Line Challenge** as two components/presets backed by one evaluator. Keep definitions separate from per-player attempt state and persistent rewards.

| Authoring area | Suggested controls |
| --- | --- |
| Identity | Stable challenge ID, title, description, icon, optional start marker |
| Start and retry | Interact/enter/manual activation; teleport policy; allow player marker; soft retry/full restart |
| Spot region | One or more volumes; count only while inside, or arm an attempt until landing/reset |
| Line steps | Ordered step list; zone per step; required goals; skip/wrong-order policy; optional deadline per step |
| Objective | Trick/combination, points, count, duration, distance, gate/zone, or named custom signal |
| Matching | Exact sequence, contains all required tricks, any allowed trick, minimum repetitions; use stable trick IDs |
| Completion | Immediate, confirmed landing, combo end, or explicit finish trigger |
| Failure | Bail, timeout, missed step, reset/teleport, forbidden action, leaving permitted area |
| Progress scope | Current combo, current attempt, or retained session progress |
| Tiers | Basic/Am/Pro/Legendary defaults; editable names and goals; explicit composite requirements |
| Presentation | Goal labels, progress visibility, zone colors, next-step cues, lifecycle UnityEvents |

Suggested public types: `MBSpotChallenge`, `MBLineChallenge`, `MBChallengeZone`, and shared objective/tier definitions. These names do not exist yet.

A game adapter should send structured events such as trick performed, combo landed, bail, teleport/reset, and zone entry/exit. Include player ID, attempt/combo ID, ordered tricks, score, and relevant measurements. Reject stale or duplicate events. Resolve goal progress and landing confirmation in a defined order before issuing completion and rewards.

Map tasks should be able to reference a specific challenge and tier by stable ID, for example “Earn Pro at Bonk Tower” or “Finish three Line challenges.” Display text should not act as the gameplay identifier.

## Implementation sequence and acceptance cases

1. Build the shared definitions and attempt evaluator, with separate transient progress and persistent tier completion.
2. Add Spot/Line components, zone editing, starter presets, and inspector validation for missing zones, duplicate IDs, impossible targets, and cyclic tier references.
3. Add the MashBox game adapter, player filtering, landing/bail handling, retry behavior, HUD, and save integration.
4. Connect specific challenge/tier completion to map tasks.
5. Recreate Bonk Tower and Back Alley Buster as parity samples and playtest them against the intended old behavior.

Focused acceptance cases: no progress before activation; a Spot attempt can leave its trigger when configured to continue; bail never banks a pending combo; a Line goal only counts in its assigned step; strict ordering works when enabled; duplicate landing events award once; reset clears only the configured progress; one player's activity cannot complete another player's challenge; saved tier rewards survive reload without being awarded again; two instances of one definition do not share attempt state.

Only this study document was added. No runtime behavior was changed, and no Unity compilation or playtest was performed.
