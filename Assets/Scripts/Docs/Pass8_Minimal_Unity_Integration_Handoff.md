# Pass VIII Minimal Unity Integration Handoff

What Codex needs to connect, in priority order. Everything below is source-complete and Roslyn-proven; nothing here asks Codex to design game logic, only to wire source authorities into the live scene and validate in Unity. No scenes, prefabs, terrain, Packages, or ProjectSettings were touched in Pass VIII.

## Tick and driver hookups

Most Pass VIII logic is written as pure source-side drivers with explicit hookup points already installed in `SimulationDrivers`, but several need their Unity-side tick to actually fire in live play (the harness cannot execute `SimulationDrivers`):

1. **NPC business lifecycle driver** (`DriveNpcBusinessLifecycleWeek`, beside the entrepreneurship call in `OnWeekChanged`). Without this call, NPC businesses still never close in live play.
2. **Revenue collection scans** (`DrivePostalRevenue` runs after `hub.Postal.AdvanceDay`; freight scan needs a production tick drive — see below).
3. **NPC owner request resolution** (`DriveNpcOwnerRequestResolution` in `DriveAutonomousLabor`).
4. **Opportunity pursuit** (`DriveOpportunityPursuit` daily).
5. **Housing absence sweep** (`DriveHousingAbsenceSweep` daily; strict policy installed, lenient available).
6. **Tenancy payment loop** (`DriveTenancyPayments` daily).
7. **Business lender** (`hub.BusinessLender.ProcessDay` daily; bank chartered once at the documented site).
8. **Social knowledge intake**: manager procurement is wired (`SocialKnowledge = hub.KnowledgeTransmissions`); the lender intake needs `hub.BusinessLender.BindSocialKnowledge(hub.KnowledgeTransmissions, officerPersonId)` once the bank has a real officer person (call site marked; do not invent one).
9. **Freight production instances**: `FreightCompanyRuntime` has zero production instances — its dispatch docstring calls live instances scene work. Creating and ticking freight companies is the precondition for freight revenue collection in live play.

## Scene and registration work

- **Boarding houses**: `BoardingLodgingBridge.RegisterHouse` needs a real parcel id per house; supply the landlord resolver and lender/kin/employer opportunity sources for full housing discovery.
- **Construction ports**: runtime adapters for six ports (`IConstructionParcelAuthority` to `TitleAuthority`, material sale port to lumber-yard/sawmill/general-store runtimes, material source, price port to lot price lists, restock port, household tool custody). A production driver must tick projects day-to-day (only the vertical-slice proof calls the executor today). Visual placement: data construction proceeds with `VisualPlacementPending=true`; prefabs must satisfy each design's `PrefabExposedContract`.
- **Mine**: wire `MineProductionCycle` into the live mine manager and labor system; wire the optional `businessCash`/`buyerCash` parameters through live business managers (safe defaults until wired).
- **Bank**: when a player-owned bank business exists, bind its own `BankDepositLedger` to the lender service in place of the chartered town-bank capital.
- **Attendance**: the Unity hub needs a weekly attendance accumulator fed by task completion before it can install `WeeklyAttendanceProvider` in production (seam, fail-closed semantics, and replay honesty are proven; the scenario driver demonstrates the wiring).
- **Saloon scene**: connect to `ConversationOpportunity` validation for live social encounters (already-assigned item from Pass VII).

## Validation Codex should run

- Unity compilation of the branch after import.
- Unity Test Runner: the full suite, especially the suites excluded from the Roslyn harness (`ConstructionInputSupportTests`, boarding production tests, `HouseholdVerticalSlicePhaseH`).
- PlayMode: the 90-day scenario's beats (or a shortened equivalent) with attention to the new drivers firing; the Ruston Decline closure beats; a New Game → Restart cycle confirming no ghost state (due events, authored lots, reservations).
- Camera/materialization observation: confirm off-screen economics match on-screen (no camera-dependent behavior was built, but this was never observed live).

## Known Unity-only limitations of the source proofs

- `HouseholdVerticalSlicePhaseH`'s construction proof fix follows the proven pattern but could not execute in the harness (heavy deps); verify in Unity.
- `SimulationDrivers` hooks and `SharedBusinessRuntimeManager` service-branch changes carry Roslyn syntax verification and full-suite regression safety only.
- The lender trust-reduction figures (0.15 corroborated, 0.18 records-confirmed) are documented calibration, not tuned against long-run simulation.
- Service visit caps (2 per household per week) are a starting calibration.

## What Codex should NOT do

- Do not reintroduce manufactured revenue or hidden stabilization.
- Do not create a second cash writer or debt authority.
- Do not wire the twelve registered-unwired runtimes without treating each as a scoped item (see `Docs/UnwiredSystems.md`).
- Do not approve asset sales without appraisal (the null-appraisal denial is the conservative correct behavior).
- Do not invent persons (lending officers, appraisers) to satisfy seams.
