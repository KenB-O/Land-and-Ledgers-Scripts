# Pass 5 — Town Growth: Review and Handoff

Pass 5 of the new series, LAST CONTENT PASS. Base:
`muse/pass4-player-depth` at
`aca546d1f344bb13f0b20788b3873ab38debe81f` (Pass 4, player depth,
verified on GitHub before branching; full SHA confirmed with
`git rev-parse HEAD` after checkout, not copied from a prior report).
Branch for this pass: `muse/pass5-town-growth` (SHA and push
confirmation are in the pass report, not in this file, because
they change at push time).

Governing docs: Documentation v3.0, Canonical Project Bible
Revision XXXI and Technical Implementation Bible Revision XIV,
both dated 2026-10-07. Proposals 001-105 are approved; 106-145
are pending and were NOT promoted here.

Repository house rules honoured: work is additive only under
`Domains/Population/TownGrowth/` plus tests under
`Tests/Editor/Population/` and this doc. No existing file was
edited, no class, enum, or serialized field was renamed, no
namespace was altered. PopulationManager remains the population
authority (`AUTHORITY_MAP.md`); this pass adds a town-growth
model for a future driver to call, it does not replace that
manager.

## 0. What this pass is, and what it is not

Pass 5 ONLY: town growth, source-level. It does NOT start
heating implementation (blocked on Kennedy decisions, see §8).
It does NOT repeat Pass 1 (generic business), Pass 2 (task
system), Pass 3 (economy ordering), or Pass 4 (player depth).

It does NOT claim Unity compilation, Unity Test Runner,
PlayMode, scene wiring, prefab wiring, or Inspector values:
no Unity run happened in this pass, and none is claimed.
Everything verified here was verified in a small Roslyn
harness (section 10). There is deliberately NO Unity-only
test file in this pass: the town-growth model is plain C#
and fully Roslyn-testable, so a Unity-only stub re-asserting
model behaviour would have been a fake Unity test (the same
reasoning as Pass 3). Unity work to verify is driver wiring
and dialogue UI (section 9), by Unity AI / Codex.

After this pass, the next pass is reconciliation only, then
the ChatGPT manager prompt. No further content pass is in
scope for this series.

## 1. Surviving decisions (recorded, not guessed as canon)

- **Family-only is the honest bound.** No friend graph exists.
  A friend source is refused unless the caller supplies friend
  edges, each with non-empty shared-history evidence. Friends
  are never generated.
- **Fredericton is an example, never canon.** No town name is
  hard-coded. Every off-map location is caller-supplied; an
  empty location means generic off-map ("offmap"), exactly as
  PostalService does today.
- **Nothing is invented at ask time.** Profession, disposition,
  wage, location, and role must be supplied by the caller
  (tree build or lead time, from a real source). Missing data
  stays Unknown/empty and is diagnosed.
- **R05 employment id accepted as already in driver code:**
  "arrival-<person>-<business>-<day>". Pass 5 builds exactly
  that format and does not change it. Kennedy should still
  accept it before a driver runs in production.
- **Housing assignment is always pending.** The string
  HousingUnitId to int homeBuildingId mapping belongs to the
  HousingResidence runtime (Codex). This model never assigns
  a home.
- **No headcount, ever.** The need reader has no Headcount
  field by design. Player-stated need opens an effort; task
  evidence ranks roles. Unmet minutes and balks are evidence,
  never a staffing order.

## 2. Lead enrichment (Pass5TownGrowthModel.cs)

- `Pass5OffMapLocation`: TownName + PostalDestinationId.
  `EffectivePostalDestination()` returns the supplied
  destination, or "offmap" when none is supplied.
- `Pass5EnrichedLead`: mirrors RecruitmentLead's contact /
  recommender ids and names, plus BusinessId, RoleId,
  RoleDisplayName (from real business roles, never free text),
  Location, ProfessionId/Name, Disposition (Unknown /
  Employed / RecentlyLostJob / SeekingChange),
  HasCurrentWageCents + CurrentWageCents, DayIndex.
- `Pass5LeadEnrichment.Enrich`: refuses to invent. A work
  lead with a business but no role, an unknown profession,
  a generic location, or an unknown disposition each add a
  diagnostic and leave the field empty/Unknown.
- `Pass5LeadSaveRecord`: saveable lead record, plain
  strings/ints, round-trippable via FromLead/ToLead. This
  addresses Phase 4 F29 (RecruitmentLead has no save DTO, so
  leads are lost on reload) at the model level. It does NOT
  claim the existing RecruitmentLead is now persisted: the
  production driver must adopt this record (or an equivalent
  DTO) and wire it into save/load. Until then, leads remain
  transient, and this doc states that instead of hiding it.

## 3. Settlement readiness (same file)

- `Pass5SettlementInputs` / `Pass5SettlementReadinessModel.Check`:
  pure gate check mirroring ArrivalSettlement. Refused when:
  already on-map; no housing unit id or zero vacancies; no
  compensation terms and no explicit unemployed-with-plan
  flag. EmploymentPending is true when terms are absent but
  the explicit flag is present (driver must supply terms or
  register job-seeking, never invent employment).
- A refused newcomer is never silently made homeless by
  settlement; the legacy monthly roll is the only path that
  admits without those checks, and this model preserves that
  distinction.
- Stove/retention/fuel data: flags, not invented values. If
  a field was not authored, the model records the code
  default (Basic / Basic / Wood) explicitly and flags it as
  a default, so a default is never mistaken for authored
  data. Slice households have no such data today.

## 4. Town driver model (Pass5TownDriverModel.cs)

- `Pass5TownInputs` / `Pass5TownDriverModel.Evaluate`: monthly
  evaluation in canon order work, then housing, then
  reputation. Housing availability = vacant/total, work
  availability = open/(open+filled), reputation = weighted
  sum, normalised if weights do not sum to 1 (mirroring
  TownReputationModel). Growth and opinion scores are
  caller-supplied 0-1 values with Supplied flags; 0 means
  "not supplied", never "town is not growing" or "residents
  dislike the town". A stale snapshot (age over the caller
  MaxSnapshotAgeDays, default 7) blocks arrivals until
  recomputed. Slice suppression blocks arrivals regardless
  of conditions. All thresholds and weights are
  caller-supplied test parameters, never canon.
- Channel honesty: `LegacyRollStillLive` defaults true and
  is never retired by this model. `WithLegacyRollRetired`
  returns a copy with the flag false, documented as a
  Kennedy decision effect, never applied by default. This
  model uses Traveler + Recruitment channels only and never
  claims the three channels are unified.
- Slice departure gate: carried as an explicit enum
  (Undecided / ActivateGate / LeaveLive). Undecided is the
  default, meaning departures stay live on the legacy roll.
  Activating it is a Kennedy decision, never silent.
- Travelers: `Pass5TravelerModel.GenerateVisits` creates
  visit records only, at a caller-supplied frequency, with
  no PersonState, household, or job created, ever.
  `EvaluateStay` requires LookingForStay + boarding
  vacancy + open jobs + housing vacancies + allowed
  arrivals; a stay's NextStep is
  "run-ArrivalSettlementDriver.SettleAndApply", never
  auto-settle. A non-stay is visitor-only, word-of-mouth
  only.
- Departures: `Pass5DepartureModel.DepartNewChannel`
  requires a destination (FamilyContact / PreviousHome /
  SupplierTown / RouteTown / NamedNew, never randomised,
  never "off-map" alone); a LegacyUntracked destination is
  refused for the new channel. Closeout mirrors
  OutmigrationCloseout: membership + employment only.
  Household/property/debt handling remains
  HouseholdPropertyDebtStillRequired = true (F08, still
  open, owned by the town driver model, not done here).
  `PreserveLegacyDeparture` keeps legacy departures
  untracked with no invented destination (F12, still open).

## 5. Economic-need reader (Pass5EconomicNeedReader.cs)

- `Pass5EconomicNeedReader.Rank` reads caller-supplied
  snapshots of LaborDemandAssessor lines (family, unmet
  minutes, balks, sustained days) and Pass 2 missed-work
  evidence, plus player-stated needs. It has no production
  caller feeding it live TaskAuthority state today, and it
  says so in its diagnostics.
- Family-to-role mapping is caller-supplied. A family with
  no mapping is reported as IsUnmappedFamily, never guessed
  into a role, and emits no need for that family.
- Sustained-pattern thresholds are caller parameters. A
  task-evidence role below threshold is reported as
  NotSustained, never as a need. Player-stated needs always
  appear (they open an effort); task evidence ranks them
  and adds unstated roles that meet threshold. Source is
  PlayerStated / TaskEvidence / Both.
- Ranking order (display order, never a quota order):
  sustained task-evidence needs first, then Both, then the
  rest, by unmet minutes within each band.

## 6. Conversation chain (Pass5ConversationChain.cs)

- Dialogue model: `Pass5DialogueQuestion` (speaker,
  business, role id + display name, player-owned flag,
  legitimate-channel flag). `ValidateQuestion` refuses
  non-player-run businesses, missing legitimate channels,
  and roles not in the business's real roles list.
- Chain steps, no skipping: AskWorkers ->
  BuildLetterRequest -> PostLetter -> ReplyPending ->
  ReplyReceived / Negotiate / TravelToInspect / Declined /
  Ignored -> ArrivalSettlement -> Settled. `Advance`
  refuses illegal transitions, refuses posting without
  postage paid (never free carriage), and refuses posting
  without a registered origin (existing origin is
  "general-store"; registration already exists in
  production and is NOT re-registered here).
- Letter posting belongs to RecruitmentLetterPoster and
  settlement belongs to ArrivalSettlementDriver; this file
  is the caller, reply, and arrival glue between those
  existing pieces. It claims no wiring, no UI, and no
  production caller. Dialogue UI and wording are Codex/Unity
  work driven by this model.
- Friend edges: `ValidateFriendEdges` accepts only edges
  with non-empty SharedHistoryEvidence; unevidenced edges
  are refused, never generated.

## 7. What Pass 5 deliberately did NOT do

- No production drivers: monthly town driver, traveler
  generator driver, conversation caller, reply/arrival
  driver, settlement driver, need-reader driver, and
  departure driver are all still missing. The models above
  are not drivers.
- No supplier adapters, no store trading-port change, no
  player-depth change, no economy-ordering change.
- No heating implementation: no fuel type, stove type, or
  heating rate was guessed anywhere in this pass.
- No friend graph built: family-only remains the bound;
  the model accepts caller-supplied evidenced edges only.
- No legacy departure rewritten, no destination invented
  for a past departure, no slice gate activated, no 4%
  roll retired. All three are recorded as open decisions.
- No existing file edited, no serialized type renamed.

## 8. Kennedy decisions still open (separated from code facts)

- Departure gate activation: wire AllowDepartures=false to
  SuppressMigrantArrivals for the slice, or explicitly
  leave slice departures live? (One-line decision.)
- Retire the old 4% arrival roll (and legacy departure
  draws) once traveler + recruitment channels are driven,
  or keep it as a background trickle? (Phase 4 F10.)
- Departure unification: route future legacy departures
  through Outmigration.Depart with a destination, or accept
  untracked legacy departures forever? (F11/F12.)
- Double-ordering guard (Phase 4 F26): no guard was found
  in the precursor audit; do not assume it exists.
- Friend graph vs family-only: build a friend/associate
  graph from real shared history, or accept family-only as
  the bound? Role dialogue extends family-only with job
  context and addresses; it does not yet require friends,
  but "do you know anyone" will hit the family wall
  quickly in a six-person slice.
- Accept the R05 employment id format
  (arrival-<person>-<business>-<day>) before any driver
  runs in production.
- Experience formation rate: unchanged, still nearly
  invisible over realistic timescales (Pass III flag);
  do not retune in the town pass without a decision.
- Which roles are needed, and how need is measured:
  player-stated vs task-evidence vs both; sustained-pattern
  thresholds; family-to-role mapping; whether a role lead
  must match the contact's profession or may retrain (no
  retraining model was found).
- Settlement data to author: slice households have no
  stove/fuel/retention data and no off-map relative
  profession/location data; the town pass model accepts
  defaults explicitly, but a driver that wants testable
  slice variation must author those inputs or accept
  defaults. Whether a newcomer starts with a stove, a
  fireplace, or nothing is also open.
- Heating decisions, carried forward, not decided here:
  which methods to include in a first heating pass
  (audit recommendation: wood-device variation + coal as
  the single second fuel), whether coal is one item or
  three, what one game unit of each fuel weighs and heats,
  whether the Dealer or the Mine sells coal retail, what
  drives house variation, and whether bakery oven fuel
  stays cordwood.
- Seconds: small vs full migration (Pass 2, still open;
  nothing in Pass 5 depends on it).
- All prices/rates/weights/frequencies: reputation
  weights, immigration thresholds, traveler frequency,
  boarding capacity rules, letter reply rates, departure
  draws, reputation penalties - all caller-supplied or
  calibration, none canon.

## 9. Codex / Unity AI only

- Production drivers: monthly town driver (Immigration
  Conditions + TownReputation from a real reader),
  traveler generator, conversation caller + dialogue UI,
  reply/arrival driver, SettlementDriver caller with
  compensation terms or explicit unemployed-with-plan,
  HousingResidence assignment step, need-reader driver
  feeding LaborDemandAssessor + Pass 2 missed-work into
  the ranker, departure driver + closeout with F08
  household/property/debt handling.
- Save/load: adopt Pass5LeadSaveRecord (or an equivalent
  DTO) into save/load, or leads remain transient on reload.
- Postal: no re-registration needed (General Store office
  already registered in production); Codex must confirm
  the letter destination routing uses the enriched lead
  location instead of generic "offmap" once the driver
  exists.
- Unity AI first-compilation check; Codex hard problem
  solving and final integration. Neither prompt is written
  here; ChatGPT (manager) writes those from the parent's
  summary of this pass.

## 10. Tests and migration (section: tests)

Roslyn harness only, `harness-pass5.csproj` (NUnit shim +
Runner + the 4 new TownGrowth files + the test file only;
.NET 8 SDK at `~/workspace/land_and_ledgers/dotnet/sdk/dotnet`):
- File: `Tests/Editor/Population/Pass5TownGrowthTests.cs`
- Result: 11 passed, 0 failed (first run, no test fixes
  needed except one test-input correction found before
  the run: traveler town inputs raised so reputation
  passes, because the original inputs computed 0.36, below
  the 0.5 test threshold - the model was right, the test
  inputs were wrong, and the correction is recorded here
  honestly).
- Coverage: lead enrichment never invents, named location
  routing, save-record round-trip, settlement refusal vs
  defaults vs employment-pending, R05 id format, town
  driver work/housing/reputation order + undecided gate,
  stale snapshot + slice suppression blocks, traveler stay
  conditions + never auto-settle, departure destination
  requirement + legacy preservation, need reader never
  outputs a headcount, conversation chain no-skip +
  postage + friend-edge evidence rules.
- Unity-only tests: none, deliberately (see section 0).
  Until Unity Test Runner runs, report Pass 5 as
  Roslyn-verified model only, not Unity-verified.
- Earlier tests were not re-run (small harness compiles a
  subset only); the pre-existing Phase H Roslyn failures
  and the ConstructionInputSupportTests Unity timeout are
  unchanged and unrelated.
- Migration: additive only. No existing file, type, field,
  or save format was changed, so no save migration is
  needed for this pass. Adopting Pass5LeadSaveRecord into
  production save/load is future driver work (section 9),
  not a migration in this pass.
