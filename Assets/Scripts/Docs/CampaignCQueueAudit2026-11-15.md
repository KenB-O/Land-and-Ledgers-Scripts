# Campaign C — C-lane queue audit (package 13)

- **Branch:** `muse/campaign-c-persistence-2026-11-15`
- **Parent commit:** `6f8210d0832186446694f00dc05835671e6d759c` (DEF-073, pushed and verified)
- **This file's own commit SHA** is recorded in the checkpoint report
  `agent-c-checkpoint-13.md` in the campaign directory (outside the repo clone).
  It is not recorded here: a file cannot record its own amended SHA before
  it is committed (lesson from B package 10, whose committed header recorded
  the superseded pre-amend SHA `6228bc0d`).
- **Files in this commit (pre-push):** 2 — this file
  (`Assets/Scripts/Docs/CampaignCQueueAudit2026-11-15.md`) and its `.meta`
  (fresh GUID `d542cb7524754bd6846eb161452e2cc5`).
- **Change type:** documentation only. Zero production files, zero test files
  changed. No harness run (nothing executable changed); prior suites stand at
  224 tests from package 12 (DEF-073).
- **Register read:** `DEFECT_REGISTER.md`, 3,677 lines, 73 entries, update 13.
- **Documentation blocker still in force:** Documentation v3.2 has NOT been
  provided and is NOT available locally. All work below is mechanical or
  characterization under Documentation v3.0. No supplier terms, prices,
  wages, conversion factors, or policies were invented.

## Verdict: YES — the C lane has further authorized mechanical work

Read the current register (update 13) and audited all **28 C-assigned open
entries** plus the **3 UNASSIGNED entries in the persistence / save-load /
time / rehydration domain** (DEF-012 opening date, DEF-017 day length and
speed cap, DEF-024 SaveLoadManager scene references), each with one targeted
source check in the C tree. **The lane is NOT waiting on Kennedy for its next
package.** It IS waiting on Kennedy (and on v3.2, and on the Codex Unity run)
for the Tier-3/Tier-4 items and for live-run proof of the already-implemented
fixes.

## Scope — what was audited and what was not

**Audited (31 entries):** C-assigned — DEF-009, DEF-010, DEF-011, DEF-014,
DEF-020, DEF-021, DEF-022, DEF-023, DEF-025, DEF-028, DEF-029, DEF-030,
DEF-031, DEF-033, DEF-041, DEF-045, DEF-046, DEF-047, DEF-048, DEF-049,
DEF-050, DEF-061, DEF-062, DEF-063, DEF-064, DEF-065, DEF-066, DEF-073
(28). UNASSIGNED in-domain — DEF-012, DEF-017, DEF-024 (3).

**NOT audited (out of scope):** B-assigned (30 entries — see B package 10,
`agent-b-checkpoint-10.md`); D-assigned (6 — DEF-015 meal-count conflict,
DEF-027 baker's-hand-kit, DEF-039 notions provenance, DEF-040
mortality/fertility, DEF-067 double-charge loops, DEF-068 freight-company
premises); E-assigned (1 — DEF-026 maintenance-pass test evidence);
UNASSIGNED outside the persistence/time domain (5 — DEF-013 opening-economy
provenance, DEF-016 freight pricing, DEF-032 horse animation asset, DEF-038
record correction, DEF-072 timber regrowth). DEF-013 touches
`FirstLedgerSliceBootstrapper.cs` (a C-shared file) but its substance is
economy provenance, not persistence; it stays out of scope.

## Ranked queue

### Tier 1 — Mechanical and authorized (do next, in priority order)

1. **DEF-066** (Medium) — Nov 13 real-time conversation rule not implemented
   anywhere. The register states the remaining blocker is "None for the
   implementation (the rule is a settled design decision)". Verified: the
   conversation systems exist (`AcquisitionSellerMeetingController`,
   `ConversationOpportunityService`, `DailySocialDrive`) but none touches
   speed or time scale (grep for `TimeScale` / `SetSpeed` /
   `speedMultiplier` / `SimulationSpeed` across those files: zero hits), and
   no global slow-to-real-time rule exists anywhere in production. Package:
   determine the hook point first (`SimulationTickContext.cs` is the named
   candidate in the register), then slow the sim globally to real time on
   entering a direct player–NPC conversation and restore the previous speed
   on exit; NPC-to-NPC conversations must not change player speed. Open
   scope question (u) below (Check 9 Q5: delegated-manager negotiations)
   stays open and does NOT block the direct-conversation hook.
2. **DEF-062** (Medium) — `CancelTask` does not cancel due events. Verified:
   `TaskAuthority.CancelTask` (`TaskAuthority.cs:599-611`) releases the
   commitment and resources and marks the task Cancelled, with no due-event
   handling. The seam is fully installable: `DueEventScheduler.Cancel(eventId)`
   exists (`Core/Time/DueEventScheduler.cs:88`, lazy removal, no-op on
   unknown id) and the task carries its due id (`task.DueEventId`,
   set/cleared by `TaskAuthorityLifecycle.cs:188,215,344`). **Narrowing
   found by this audit:** the live harm is smaller than the register states —
   `CompleteTaskAtDueEvent` (`SimulationDrivers.cs:8992-9000`) already
   guards with `if (task.Status != TaskStatus.InProgress) return;`, so a
   stale due for a cancelled task fires and no-ops silently (the "ignored"
   diagnostic fires only for unknown tasks). The fix is hygiene plus a
   diagnostic (cancel the due on `CancelTask`; log the cancellation), not a
   live-corruption fix. The package must first establish how `TaskAuthority`
   reaches the scheduler (no wiring was read in this audit).
3. **DEF-061** (Medium) — No achieved-speed readout (readout slice only; the
   catch-up policy is Tier 3). Verified: `pendingGameSeconds` accumulates
   unboundedly (`TimeManager.cs:540`) and is drained at most
   `min(pending, budget)` per frame (`:546`); `LastAchievedGameSecondsPerRealSecond`
   is computed every frame (`:467`) and has zero production consumers outside
   `TimeManager.cs` (repo-wide grep). Package (diagnostic layer only):
   surface the computed achieved speed (daily summary / diagnostic line) and
   warn when the pending queue grows past a stated threshold. Do NOT invent a
   catch-up policy (shed time / shed fidelity / fall behind) — that is
   Check 9 Q6, owner question (t) below. No UI is invented by this package;
   a diagnostic/summary surface only.
4. **DEF-063** (Medium) — Task-duration warning caps hide stalls past the
   cap. Verified: `MaxWarningsPerDay = 12` and `MaxTrackedRecords = 512`
   (`Core/Time/TaskDurationDiagnostics.cs:290,293,425`); evictions and excess
   warnings are counted as omitted (`:567,638-652,674`) but the summary
   names no task. Package (diagnostic layer only): record the identities of
   the omitted tasks in the daily summary (which tasks stalled and were
   omitted), or raise the caps. Do NOT silently raise the caps without
   recording which tasks were omitted — the point of the defect is
   identifiability, not volume.
5. **DEF-030 + DEF-031** (Low, one commit, same file) —
   `CoupledTransportMovementController.cs`. DEF-030 verified: arrival
   confirmation does `FindAnyObjectByType<SimulationDrivers>()` (`:546`,
   origin arrival; `:709`, second site). Package: resolve the drivers
   reference once at bind time and hold it, instead of a per-arrival scene
   lookup. DEF-031 verified: the misleading log line is still present, now
   at `:584` ("regional destination trigger required") — line numbers moved
   since the register cited `:372` (C-7 leg-state additions). Package:
   reword the line to describe the presentation handoff, not a missing
   completion mechanism. No movement, trigger, or completion change.
6. **DEF-028** (Low) — Guarded save/load drop paths. Verified all three
   still present: (1) shopping trips drop if the driver is null at hub-load
   time (`SimulationSystemsHub.cs:1493-1495`, diagnosed); (2) CustomerQueue
   entries drop if the queue is null at Pass 9 restore
   (`Infrastructure/Persistence/Series2Pass9Save.cs:1240-1246`, diagnosed);
   (3) sawmill sections drop if the bootstrapper is missing
   (`SaveLoadManager.cs:573`, warning). Package: harden by deferring the
   restore instead of dropping — retry the section on the next tick / once
   the owner appears — rather than dropping with a diagnostic. Touches
   shared files (`SimulationSystemsHub.cs`, `SaveLoadManager.cs`); coordinate
   via Agent E. Lowest Tier-1 priority: all three paths are guarded
   (diagnosed, not silent) and should not fire in the shipped scene.

**Recommended next package: DEF-066**, then DEF-062, then the DEF-061
readout slice, then DEF-063, then DEF-030 + DEF-031, then DEF-028.

### Tier 2 — Verify-then-document / diagnostic slices (no code fix)

- **DEF-020 diagnostic slice** (Medium) — Failed construction procurement
  permanently strands the package. Verified: `AllowProcurementRetry`
  (`NpcConstructionDriver.cs:378`) has no production caller (definition
  plus doc comments only); the strand is durable across save/load
  (`DeriveBlockedProcurementPackages`, `:360-376`, rebuilds the block from
  persisted `Stranded` markers). The stall is silent to the player (only a
  `Debug.Log` line). Slice: surface stranded procurement packages in the
  daily summary / household incident feed so the stall is visible. The real
  fix (a retry path) needs the retry-disposition manager call, owner
  question (r) below, and is sequenced after DEF-004 (no lumber supply —
  the failure that would strand a package today).

### Tier 3 — Blocked on owner / manager decision (no mechanical work)

- **DEF-009** (High) — Fuel Dealer has no runtime, cash book, or save
  section. Verified: `FuelDealerSaveDto` has zero hits repo-wide; still
  nothing to persist. Blocked on the D-2 decision family (same as DEF-001).
- **DEF-010** (High) — Domestic labour triggers off. Verified: the three
  triggers are still hard-coded off (`SimulationDrivers.cs:3595`
  `MealsAtHomeCount = 0`, `:3599` `FuelHandlingNeeded = false`, `:3603`
  `CleaningNeeded = false`; line numbers moved since the register cited
  `:3435/:3439/:3443`). Blocked on the design decision (what authorities
  report the three missing need sources).
- **DEF-011** (High) — NPC construction commissioning trigger has zero
  production callers. Verified: `QueueNpcConstructionRequest` has exactly
  one non-test hit — its own definition
  (`SimulationDrivers.cs:1681`). Blocked on the commissioning-trigger design
  decision. Also depends on DEF-004 (materials).
- **DEF-014** (Medium) — Midday meal slot flexible/optional. Verified:
  `MealsPerPersonPerDay = 2` and a deliberately skipped flexible midday
  meal is neither logged as missed nor charged as a nutritional deficit
  (`DailyNeedsService.cs:422-426`). Blocked on owner question (p) below
  (manager ruling: is the midday meal required behaviour?).
- **DEF-020 real fix** (Medium) — see Tier 2. Retry disposition is owner
  question (r); sequenced after DEF-004.
- **DEF-024** (Medium) — All 19 SaveLoadManager scene references are
  `{fileID: 0}`. Verified in this tree's `Main Scene.unity:43853-43871`:
  all 19 references (`townWorld` … `firstLedgerBootstrap`) are
  `{fileID: 0}`; save/load works only because `AutoWire()` resolves them.
  No Muse implementation — Codex scene-binding work. Blocked on Codex, not
  on an owner decision.
- **DEF-025** (Medium) — L8 no-worker escalation gap. Verified: the only
  non-test references to `L8NoWorkerExists` are the enum member and its
  label mapping (`TaskDurationDiagnostics.cs:337,1229`). Blocked on the C1
  design call (warn/escalate when a queued task's reason is L8 past
  EscalationDays?).
- **DEF-029** (Low) — Old saves lose in-flight shopping trips; no migration.
  Verified: the migration envelope validates versions only
  (`SaveMigrationEnvelope.cs`: `CurrentFormatVersion = 6`,
  `MinimumSupportedFormatVersion = 0`); `BootstrapLegacyEnvelope` records
  only the manifest-bootstrap step (`MR-P002_MIGRATION_MANIFEST_BOOTSTRAP`);
  no trip-migration step exists anywhere. Old saves still read the additive
  trip sections as empty. Blocked on owner question (o) below (is an
  old-save migration wanted at all?). Note: the envelope infrastructure
  exists, so if the manager says yes, the migration step itself is
  mechanical.
- **DEF-046** (High) — Float32 absolute timeline. Quantified by C package 11
  (`0b6f9330`); no double-precision migration has started (no
  `double dueGameSeconds` anywhere; the double timeline still has exactly
  two production consumers, `AgentMover` and `WorldInteractionController`).
  Blocked on owner question (n).
- **DEF-061 policy layer** (Medium) — same entry as the Tier-1 readout
  slice: the catch-up policy under sustained overload is owner question
  (t) (Check 9 Q6).
- **DEF-064** (Medium) — No tenancy-move path. Verified: `MoveTenancy` /
  `TransferTenancy` have zero matches repo-wide; `DriveTenancyPayments`
  (`SimulationDrivers.cs:3399,3668`) settles rents only. **Tension noted:**
  the register says "No owner decision required; this is a missing move
  path" and "Remaining blocker: None (implementation work)" — but a move
  path needs a trigger rule (which households move, on what evidence, what
  happens to the old tenancy), which is a design decision analogous to
  DEF-011's commissioning trigger. Recommend a manager ruling on the move
  trigger (owner question (q) below) before any wiring; the mechanism itself
  is mechanical once the rule lands.

### Tier 4 — Blocked on v3.2

- **DEF-012** (High) — Opening date five-way split. Verified in this tree:
  1870 `FirstLedger.asset` (`startYear: 1870`, agrees with Canon XXXI Rev
  XXV §1.1 CANON LOCK); 1880 `CalendarAnchor.cs:21`
  (`DefaultAnchorYear = 1880`) and `LegalRegime.cs:118`
  (`EpochYear = 1880`); 1886 `DefaultSimulationTimeSettings.asset`
  (`startingYear: 1886`); 1892 `PopulationGenerator.cs:35`
  (`startYear = 1892`); 1897 repo root `AGENTS.md:3` ("spring 1897").
  v3.2-DEPENDENT; owner decision after v3.2 is read.
- **DEF-017** (Medium) — Day length (24 real min/day) and speed cap (500x)
  have no doc authority. Verified: `SimulationTimeSettings.cs:11`
  (`MaximumSpeedMultiplier = 500f`), `:116`
  (`realSecondsPerGameDayAt1x = 1440f`); the asset agrees
  (`realSecondsPerGameDayAt1x: 1440`, `speed500xMultiplier: 500`).
  v3.2-DEPENDENT; speed design reopened Nov 13 (strategic fast-forward vs
  played speeds).

### Tier 5 — Characterized / complete (nothing further to code)

- **DEF-021** (Medium, SOURCE VERIFIED) — agent-cap truncation diagnostics
  present (`PopulationPathingDirector.cs:145-154,352-354`,
  `CutByAgentCap` counters). Complete.
- **DEF-022** (Medium, SOURCE VERIFIED) — `ReceiveCarriedBatch` has its
  first production caller (`ShoppingTripDriver.cs:1422`). Complete.
- **DEF-023** (Medium, SOURCE VERIFIED) — rehydration guard present
  (`NeedsRehydrateAfterLoad`, `RehydrateAfterLoad`,
  `ShoppingTripDriver.cs:220-226,1141-1180`). Complete.
- **DEF-033** (Low, CONFIRMED) — "no journey locations" warn-once flag
  present (`SimulationDrivers.cs:392`,
  `noJourneyLocationsWarningLogged`). Deliberate Part A trade-off (review
  concern C2, minor). Nothing to code; the remaining item is a manager nod
  accepting once-per-spell as the permanent rule (owner question (s)
  below).
- **DEF-041** (Low, CONFIRMED) — finders intentionally unbound
  (`SimulationDrivers.cs:3006`, "PremisesFinder and EquipmentFinder
  intentionally remain …"). Intentional configuration limit; binding needs
  real property rights and a real stocked/sold equipment asset (a larger
  design item). Nothing to code.
- **DEF-045** (High, SOURCE VERIFIED) — `TimeSettingsSnapshot` exists
  (`TimeManager.cs:306-307`, `TimeSettingsSnapshot.cs:26,45`). Complete.
- **DEF-047** (High, SOURCE VERIFIED) — `WagonLegSaveDto` exists
  (`CoupledTransportMovementController.cs:235,288`,
  `LogisticsRuntimeManager.cs:174-175`). Complete.
- **DEF-048** (High, SOURCE VERIFIED) — `OriginArrivalRefusalReason`
  exists (`OriginArrivalRefusalReason.cs:52`,
  `SimulationDrivers.cs:3800-3837`). Complete as a diagnostic; disposition
  is owner question (l).
- **DEF-049** (High, SOURCE VERIFIED) — `DriveTaskPruning` exists
  (`SimulationDrivers.cs:3448,3467`). Complete.
- **DEF-050** (High, SOURCE VERIFIED) — `dispatch-hold-stranded:`
  diagnostic exists (`SimulationDrivers.cs:7622`). Complete as a
  diagnostic; disposition is owner question (m).
- **DEF-065** (Medium, SOURCE VERIFIED) — dev-hotkey load claim refuted as
  stated (C package 3, `1ed6aaf8`); residual silent skip now fails loudly.
  Complete. Remaining owner question (d).
- **DEF-073** (Low, CONFIRMED in register update 13; FIXED on this branch
  by C package 12, `6f8210d0`) — patience bindings present
  (`FirstLedgerSliceBootstrapper.cs:2111`
  `EnsurePass9SaveOwnerForRestore`, `ShoppingTripDriver.cs:512`
  `RearmQueueTickWatermark`, `SimulationDrivers.cs:1983` Pass 9 queue
  binding). Implemented branch-pushed + Roslyn harness (224 tests); NOT
  Unity verified. E checkpoint and register status update pending — the
  status change is the coordinator's call (`IMPLEMENTED` is the closest
  register vocabulary; "FIXED" is not in the counts table).

**Counts:** Tier 1: 7 entries (6 packages — DEF-030 + DEF-031 are one
commit), Tier 2: 1 entry, Tier 3: 10 entries, Tier 4: 2 entries, Tier 5: 12
entries. Rows total 33 (DEF-020 appears in both Tier 2 as a diagnostic slice
and Tier 3 as the real fix; DEF-061 appears in both Tier 1 as a readout
slice and Tier 3 as the policy layer — one entry, two layers each). Distinct
entries audited: 31.

## Register citation corrections (for the coordinator — the register is coordinator-owned)

Found while doing the targeted checks; substance of the entries is
unaffected, only file paths / line numbers are stale:

- DEF-012: the register cites `Assets/Scripts/Domains/Time/CalendarAnchor.cs`,
  `Assets/Scripts/Domains/Legal/LegalRegime.cs`, and
  `Assets/Core/Scenarios/FirstLedger.asset`. Actual paths in this tree:
  `Assets/Scripts/Core/Time/CalendarAnchor.cs`,
  `Assets/Scripts/Domains/Economy/Legal/LegalRegime.cs`,
  `Assets/Scripts/Orchestration/Scenarios/FirstLedger/FirstLedger.asset`.
- DEF-028: the register cites
  `Assets/Scripts/Domains/Economy/Series2Pass9Save.cs`. Actual path:
  `Assets/Scripts/Infrastructure/Persistence/Series2Pass9Save.cs`.
- DEF-063: the register cites
  `Assets/Scripts/Domains/Tasks/TaskDurationDiagnostics.cs`. Actual path:
  `Assets/Scripts/Core/Tasks/TaskDurationDiagnostics.cs`.
- Line numbers moved since the register was written (C-lane additions):
  DEF-010 triggers now at `SimulationDrivers.cs:3595/3599/3603` (register
  cites `:3435/:3439/:3443`); DEF-031 log line now at
  `CoupledTransportMovementController.cs:584` (register cites `:372`).
- DEF-041: the register cites `SimulationDrivers.cs:2900-2907` for the
  intentionally-unbound finders; the comment now sits at `:3006`.

## Open owner questions (precise wording)

### Carried — C lane (a)–(n)

(a) Mid-return physical-route failure with paid goods — what disposition?
(b) Arranged-delivery batches on dead trips — how should a paid arranged
delivery complete after its trip is lost across save/load?
(c) Persistent rehydration skip — should such a load refuse to tick / halt
instead of proceeding diagnosed-but-corrupt?
(d) Continue-only post-load steps do not run on the hotkey path — intended,
or should a mid-session hotkey reload re-run them? (Re-running
`ConfigureScenarioAuthorities()` would replace the live `PlayerDirector` —
manager/owner call.)
(e) Retention floor 7 days — acceptable or different?
(f) Cancelled-task anchor — should cancellation record its day (~20 call
sites)?
(g) `ReworkRequired` pruning — prune once the corrective task
exists/completes, or retain?
(h) Fresh-scene Continue — re-establish the opening transport presentation
on Continue, or is the loud refusal accepted?
(i) Rider-approach on resume — wait/retry until Ethan arrives, or refuse?
(j) Destination-no-longer-exists — rely on arrival gates at completion, or
refuse at resume?
(k) D3 restore contract — merge instead of clear-then-load for
`RuntimeItems`, or accept the bookkeeping-only save-record gap?
(l) DEF-048 disposition (Check 10 Q5) — retry the leg, re-drive the return,
or declare the haul failed with its cargo released?
(m) DEF-050 disposition (Check 10 Q4) — should a freight dispatch hold
expire, and if a dispatch task fails or is cancelled: new dispatch task,
return shipment to Planned, or cancel the agreement?
(n) DEF-046 timeline precision — "The float32 absolute timeline's quantum
first exceeds the 60 s smallest scheduling unit during day 6214 (~17.01
years in), and reaches ±32 s rounding error at 25 years. Do we accept
float32 with its horizon limits — and if so, what is the supported
simulation horizon (e.g. scenarios capped well before ~17 years, with
multi-year/railway scenarios explicitly out of scope until the migration
lands)? Or is a double-precision timeline a planned architectural change,
and if so, when (before multi-year scenarios ship, per the Nov 9
recommendation)? If the answer is 'accept with limits', the limits need
stating in plain terms: due-event ordering between events less than ~64 s
apart is unreliable past ~17 years; domestic minute-of-day skips minutes
and the pre-midnight day index can read tomorrow for up to ~32 s;
reservation windows can be off by up to ~64 s. No migration work is
authorized by this package."

### New from this audit — (o)–(u)

(o) Old-save migration (DEF-029) — "Pre-Slice-25 saves lose in-flight
shopping trips on load, with no migration; the migration envelope exists
(version 6, manifest bootstrap) but migrates no data. Is an old-save
migration step wanted at all — and if so, for which save versions and which
sections (trips only, or custody / Slice 19 fields as well)?"
(p) Midday meal required? (DEF-014) — "Kennedy's loop requirement is that
each NPC carries one midday meal from household inventory, but the live path
serves morning + evening only (`MealsPerPersonPerDay = 2`) and a skipped
flexible midday meal is neither a miss nor a deficit. Is the midday meal
required behaviour (every NPC carries and eats one midday meal from
household inventory), or is the two-meal day accepted and the requirement
formally dropped?"
(q) Tenancy-move trigger (DEF-064) — "Rent settlement runs daily, but no
household can change tenancy in production. What triggers a tenancy move —
and what happens to the old tenancy when one occurs — or is the no-move rule
formally accepted?"
(r) Procurement retry disposition (DEF-020) — "A failed construction
procurement strands the package permanently; `AllowProcurementRetry` has no
production caller. Should a stranded package retry automatically (on what
cadence / after what event), be cancellable by the player, escalate to the
owner, or stay operator-only?"
(s) Once-per-spell acceptance (DEF-033) — "The 'no journey locations'
warning fires once per empty spell (Part A trade-off, review concern C2).
Is once-per-spell accepted as the permanent rule, or should a persistently
empty journey model keep flagging?"
(t) Catch-up policy (DEF-061, Check 9 Q6) — "When the frame budget cannot
keep up and `pendingGameSeconds` grows unboundedly, should the clock fall
behind silently (current), shed time, shed visual fidelity, or warn — and
should an achieved-speed readout be added?"
(u) Delegated-manager negotiations (DEF-066, Check 9 Q5) — "The Nov 13
real-time conversation rule covers direct player–NPC conversations. Should
delegated-manager negotiations also slow the sim to real time, or do they
run at the selected speed?"

### B-lane questions (carried by Agent B, not re-listed here)

Questions (1)–(20) (hiring wages, bakery fuel D-2/D-F1..D-F4, notions supply
term, retail-bread live-run proof, opening date / freight rate card /
opening-economy provenance, C1 design call, QA-15/OD-2 shopper policy,
staffing model, Sawmill formation, sawing gate, grant bridge, grant
issuance, Hire act kind, consent boundary (proposal 107),
candidate-response driver inputs + unknown-reliability trap, counter
handling, DirectApproach "wants better terms") live in
`agent-b-checkpoint-10.md`. Nothing in this audit changes them.

## Notes for the coordinator

- The register was NOT edited (coordinator-owned). No status changes are
  recommended by this audit except the DEF-073 change already reported by
  package 12 (CONFIRMED → IMPLEMENTED on branch `6f8210d0`; E checkpoint
  and register update pending).
- Documentation blocker still in force: Documentation v3.2 has not been
  provided; all work above is mechanical or characterization under v3.0.
- DEF-024 is Codex scene work; DEF-012 / DEF-017 are v3.2-dependent owner
  decisions. Neither is C-lane coding work.
- DEF-066's implementation is unblocked per the register ("None for the
  implementation"); only the v3.2 reconciliation of how the rule is recorded
  and the Q5 scope question (u) remain.
