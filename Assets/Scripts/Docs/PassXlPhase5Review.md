# Pass XL Phase 5 Review — read-only pass at 63b1c25, then refinements

Base: `muse/passxl-slice-phase4` at `63b1c25947079ce963c090e04e8e10f0fb432dfc`.
Scope: everything passes 1–4 added or changed, read against each other and
against the Pass XL base at `2bd9cf1` (`integration/pass12-unity-readiness`).
Governing docs: documentation v3.0 (2026-10-31); governing originals
Canonical Bible XXXI and Technical Bible XIV, both 2026-10-07, unchanged;
proposals 001–105 approved, 106–145 pending; v2.3 consolidations are
navigation-only. This review does NOT rewrite the phase 4 findings list
(`Docs/PassXlPhase4Findings.md`, F01–F39): it cross-references it and adds
new findings R01–R17 for what phase 4 missed or what only appears when
passes 1–4 are read together. Stage 2 dispositions are filled in below;
the implementation notes and updated Codex handoff are at the end.

Method: for each area, the phase report's claim was checked against the
code (file reads + grep for production callers), then checked for
composition with the other phases, naming/API/DTO consistency, duplicated
logic, contradictory comments, and test gaps. "Confirmed" means verified
by reading the code at 63b1c25. Severity vocabulary matches phase 4:
blocks play / blocks slice / integrity risk / dead code / test gap /
Codex-only / consistency (stale comment or doc/code contradiction).

## 1. Area-by-area review

### A. Player-as-NPC playtest flag (phase 1 + phase 2)

Claimed: `SimulationDrivers.TreatPlayerAsNpc` (default false) gates
player-issued travel, player-task advancement, and player owner-labour
accounting; autonomous drivers untouched; `PlayerNpcPlaytestPolicy` holds
the plain-C# rule; phase 2 persists the flag and forwards the
bootstrapper bool as an OR override.

Actual (confirmed): the claim holds. `Orchestration/Systems/SimulationDrivers.cs`
gates all three paths on the field (travel call, `AdvancePlayerWork`
early return, weekly accumulation + posting), resets the accumulator when
the flag is on, and restructures the travel branch without changing
autonomous advancement. `PlayerNpcPlaytestForwarding.ApplyBootstrapperFlag`
is exactly the OR rule, called on New Game and on Continue after load.
`PassXlSliceSaveDto.treatPlayerAsNpc` + `PassXlSliceSaveCoordinator`
capture/restore it.

Correct: the gating, the OR rule, and the save contract (all proven by
phase 2 tests, re-run here: 13 passed).

Needs: one stale docstring — the policy file still said the flag "is
not currently part of any save DTO", which phase 2 superseded (finding
R07, fixed comment-only). One API inconsistency, left alone: drivers
expose `ShouldAdvancePlayerWork` duplicating the policy method, while
travel and labour gates check the field inline rather than through the
policy (finding R13, no behaviour change — the drivers file is a
MonoBehaviour outside the Roslyn harness, so rewiring it cannot be
proven here).

Codex-only remainder: expose `treatPlayerAsNpc` on SimulationDrivers and
`treatPlayerAsNpcPlaytest` on FirstLedgerSliceBootstrapper in the Main
Scene Inspector (phase 4 finding F30, unchanged).

### B. Off-map procurement, save, and delivery (phase 1 + phase 2)

Claimed: one SKU end to end through `ExternalMarketAuthority` (finite
supply, transit, receipts, boundary reconciliation), a standalone lot
ledger never attached to store `GenericConfiguration`, explicit
seller-paid freight surcharge, CashOnArrival with WaitingPayment retry,
TradeCredit via `FinancialObligationAuthority`, duplicate-safe receipt,
FIFO sale with provenance, and (phase 2) atomic delivery into
`HouseholdInventory` with rollback and persisted delivered marks.

Actual (confirmed): the claim holds. `OffMapNamedSkuProcurementService.cs`
reads exactly as claimed: `PlaceOrder` validates supplier against the
market and books the total (goods + freight) as the boundary outflow;
`ReceiveArrivals` is receipt-idempotent; `SellToHousehold` consumes lots
in list order (FIFO) with per-lot provenance and rolls its own legs back
on failure; `SellAndDeliverToInventory` + `DeliverSaleToInventory` +
`RollbackSale` are the phase 2 atomic path (delivered-sale guard,
AddLot rejection rolls lots + both cash legs back and removes the sale).
`ExportState`/`ImportState` carry orders, lots, sales, received receipts,
delivered ids, and both sequences, idempotently; the coordinator
clear-then-loads on restore.

Correct: no double charge, double lot, or double delivery was found;
phase 2 tests (13) and phase 1 procurement tests (14) all pass here.

Needs: two stale docstring paragraphs in the service file itself — its
PERSISTENCE paragraph still said the service was "NOT yet wired into
SaveLoadManager DTOs" and its HOUSEHOLD RECEIPT paragraph still said
SellToHousehold does not write household inventory, both superseded by
phase 2 (finding R07, fixed comment-only). The single-SKU guard is
intact and lives where phase 1 put it: `GeneralStoreRuntimeManager`
refuses `staple_food` as a single-item offer, and this service never
reads or writes category stock — the service accepts any SKU id by
design, so the guard is the category-stock refusal, not a SKU whitelist
here (finding R11, verified non-finding). Cash discipline holds: the
service writes business cash only through `IBusinessCashPort` and
household cash only through the real `HouseholdLedger`; trade credit
only through `ITradeCreditPort` (finding R11).

Codex-only remainder: register the production service instance with
`SaveLoadManager.PassXlProcurementService` before the first save
(phase 4 finding F19, unchanged — without it, procurement persists
empty with a diagnostic).

### C. No-immigration slice gate (phase 1 + phase 2 + phase 4)

Claimed: `PopulationManager.SuppressMigrantArrivals` set from
`GeneratePopulationSnapshot(fixture)` on New Game, mapped to
`DemographicDriverContext.AllowNewArrivals = false` in
`DriveDemographicDay`, persisted via the slice DTO and restored on
Continue without regenerating population; the slice gate suppresses
arrivals only.

Actual (confirmed): the claim holds, with two qualifications. The
mapping in `DriveDemographicDay` (line ~611) is exactly
`AllowNewArrivals = !SuppressMigrantArrivals`, and the coordinator
restores the flag after population load. The traveler channel respects
the gate (passes on, word of mouth recorded but no arrival), and the
phase 5 traveler overload re-proves it (new test).

Qualification 1 (finding R10, documented, not changed): the flag is set
ONLY by the fixture overload of `GeneratePopulationSnapshot`. The
no-arg overload sets it false, and `PopulationManager.Start` (the
`generateOnStart` path) calls the no-arg overload. Continue is safe
because population is loaded, not regenerated, and the coordinator
restores the flag — but any future path that regenerates population
after a sealed game would silently unseal it. No code change: making
generation preserve the flag would change ordinary New Game semantics.

Qualification 2 (finding R09, Kennedy decision, not changed): the
phase 4 `AllowDepartures` mechanism is still not wired to this gate.
`DriveDemographicDay` builds the context with `AllowNewArrivals` only;
`AllowDepartures` defaults true and has no `PopulationManager`
property, no save field, and no coordinator coverage. If Kennedy
activates the slice-departure gate, the wiring is one line in
`DriveDemographicDay` — `AllowDepartures = !SuppressMigrantArrivals` —
and it is then persisted indirectly through the suppress flag. Until
then, slice residents can still leave on the old driver's distress /
opportunity draws (phase 4 finding F09, unchanged).

Slice gate verdict (phase 4 finding F39, re-verified): intact. No
refinement in this pass turns immigration, traveler arrivals, or
outmigration on for the slice.

### D. Family trees and the wiring seam (phase 3 + phase 4)

Claimed: `FamilyTreeBuilder` completes trees idempotently (two parents,
one grandparent level, sibling edges, off-map relatives as real
allocated ids, materialisation reuses the same record); it is opt-in,
deliberately not called by `PopulationGenerator`; phase 4 adds the
tested seam `FamilyTreeWiring.ApplyAfterGeneration`.

Actual (confirmed): the claim holds. `EnsureCompleteTree` snapshots
on-map residents first, so created parents are never re-processed;
a second call creates nothing (proven by the phase 4 wiring test).
`MaterialiseOffMapRelative` flips the same record and refuses unknown,
already-on-map, and deceased ids. The seam is a thin wrapper with a
diagnostic, exactly as claimed.

Correct: the opt-in rationale is real (off-map records share
`PopulationState.people`, so calling it inside generation would change
on-map counts in generation tests).

Needs: the builder's own docstring still named the raw
`EnsureCompleteTree` call as the wiring point instead of the phase 4
seam (finding R07, fixed comment-only), and it carried dead code — a
`seenParents` set built and then discarded (`_ = seenParents`)
(finding R08, removed; duplicate-parent second passes are no-ops and
are now documented as such). One scope note, not a bug (finding R16):
the builder creates parents and grandparents only, never siblings, so
recruitment sibling leads exist only when generation created the
sibling — a later pass must not "fix" this by inventing siblings.

Codex-only remainder: the one-line call site in
`FirstLedgerSliceBootstrapper` (or the equivalent New Game path) after
`GeneratePopulationSnapshot`, before the first save, with world seed
and scenario start year (phase 4 finding F01, unchanged).

### E. Recruitment conversation and letter posting (phase 3 + phase 4 + phase 5)

Claimed: conversation answers only from real kinship (parents,
children, siblings, spouses, grandparents), only living off-map adults,
only with a legitimate channel, only for player-run businesses on the
work question; `BuildLetterRequest` names the General Store as origin;
phase 4 `RecruitmentLetterPoster` hands the request to
`PostalService.PostItem` (payer overload, one letter per call, no free
carriage).

Actual (confirmed): the claim holds, with one seam gap found and fixed.
The conversation code reads exactly as claimed, including the
family-only bound (no friend graph exists — phase 4 finding F36,
unchanged). The poster validated recipient, postal service, payer, and
origin registration — but it took the origin office id purely from its
caller parameter and never looked at `request.Origin`, so a caller
could post from an office the request does not name (finding R02).
Fixed in this pass: a blank caller origin falls back to the request's
Origin, and a supplied origin disagreeing with a non-blank request
Origin is refused before any postage debit (comparison
case-insensitive, matching the postal office registry). Existing
phase 4 poster tests pass the matching office and are unaffected.

Correct: postage is debited before acceptance by `PostItemCore`
itself; re-posting a successful letter still charges twice by design
(the poster keeps no sent-mail ledger — documented, not pretended
otherwise; a persisted sent-mail ledger would be a save-shape change
and is not attempted here).

Codex-only remainder: register the General Store office (and any
destination offices) in Main Scene bootstrap before any posting
(phase 4 finding F04, unchanged — posting fails in production until
then, proven by test).

### F. Town reputation and immigration conditions (phase 3)

Claimed: `TownReputationModel` aggregates only caller-supplied signals
(housing vacant/total, open/(open+filled) jobs, growth, opinion) with
caller-supplied weights (normalised with a diagnostic, never canon);
`ImmigrationConditions` gates on work, housing, and reputation plus
the slice flag, with a staleness-aware `EvaluateAtDay`.

Actual (confirmed): the claim holds exactly. Weights summing to zero
score zero; negative inputs are clamped; a stale snapshot refuses
until recomputed (proven by phase 3 tests, re-run: 18 passed).

Needs: nothing in this model itself. Its composition gap was on the
traveler side (finding R04, see section G). The reader gap is unchanged:
no production code aggregates live housing/job/growth/opinion inputs
into `TownReputationInputs` (phase 4 finding F23, Codex/driver task),
and reputation weights remain test parameters until Kennedy sets them
(phase 4 design fork 5).

### G. Through-travelers (phase 3 + phase 5)

Claimed: a new arrival channel in canon order (awareness → opportunity
→ work → housing → travel → arrival → stabilization); a traveler stays
only when boarding-or-housing, work, housing, and reputation thresholds
pass and the slice gate allows; a stay does NOT settle them (that is
`ArrivalSettlement`'s job); passing travelers still spread word of
mouth, which can later produce a lead (never a person).

Actual (confirmed): the claim holds, with one composition gap found
and fixed. `EvaluateVisit` reads exactly as claimed, and its phase 4
docstring correction ("intended replacement; old roll still live")
is present. The gap: unlike `ImmigrationConditions.EvaluateAtDay`,
the traveler channel accepted a reputation snapshot of any age, so a
stale score could gate a stay or spread word (finding R04). Fixed
additively: new `EvaluateVisitAtDay` refuses a stale snapshot
(passes on, no word-of-mouth spread from an untrusted score) and
otherwise delegates to `EvaluateVisit` unchanged. Drivers that gate on
reputation should prefer the overload; existing callers are unaffected.

Needs: a visit generator still does not exist (phase 4 finding F24,
Codex/driver task) and traveler frequency remains a Kennedy decision
(phase 4 design fork 7). The traveler channel also does not check
snapshot staleness in its word-of-mouth lead helper beyond the score
it was given — that helper takes a prior outcome, not a snapshot, so
the staleness check belongs at the `EvaluateVisitAtDay` call site,
which is where it was put.

### H. Arrival settlement and its driver (phase 3 + phase 4 + phase 5)

Claimed (phase 3): `ArrivalSettlement.Settle` is a gate + record only —
it refuses unsettleable arrivals (no housing, no job and no explicit
unemployed-with-plan, already on map, deceased, unknown), allocates the
new-household id, and materialises the person. Claimed (phase 4): the
driver applies a settled record (existing household via `AddMember`,
new household via the pre-allocated id, employment only with
caller-supplied terms, residence left to Codex, duplicates refused,
conflicting membership refused, join rejection removes a created
household).

Actual (confirmed): both claims hold — and reading them together
exposes the pass's largest composed-path defect (finding R01). Settle
materialises the person BEFORE the driver runs, so a hand-rolled
Settle-then-Apply sequence that reaches a driver refusal (dissolved
or missing household, conflicting membership, join rejection) strands
the person on map with no membership — and Settle does not itself
validate that an existing household exists or is active, so the
dissolved/missing cases are realistic, not contrived. Fixed in this
pass with a composed entry point, `ArrivalSettlementDriver
.SettleAndApply`: it runs Settle, then Apply only if settled, and on
a driver refusal restores the person's pre-settle flags (isOffMap,
householdId, homeBuildingId). A household Apply created and then
removed on join rejection stays removed; the allocated household id
gap on a rolled-back new-household settlement remains, which is
harmless (allocator ids are monotonic, not dense). Raw Settle + Apply
behaviour is unchanged, and an AlreadyApplied result counts as
settled-and-applied (membership already in place, nothing duplicated).

Also fixed in this area (finding R05, flagged behavioural change):
the driver's employment id was `arrival-<personId>-<day>`, which
collides when one person takes jobs at two businesses on the same day
(the registry rejects the duplicate id first-wins, so the second job
would degrade to EmploymentPending with a misleading diagnostic).
The id is now `arrival-<personId>-<businessId>-<day>`. This changes
only newly created arrival employments: the driver has never run in
production, and no existing test asserted the old id format (verified
by grep); the new two-businesses-same-day test proves both jobs
register with distinct ids and exactly one membership.

Also fixed (finding R07, comment-only): the settlement docstring
still described the driver steps as "the exact missing link" and said
the driver must create through `HouseholdLifecycleManager` without
naming the pre-allocated-id rule; it now names
`ArrivalSettlementDriver`, the FormHousehold prohibition, and
`SettleAndApply` as the preferred entry point.

Codex-only remainder: residence assignment through the
HousingResidence runtime (string unit id vs int building id — phase 4
finding F02 remainder, unchanged). Settled arrivals still have
household + job but no residence until that exists.

### I. Outmigration and the departure gate (phase 3 + phase 4 + phase 5)

Claimed (phase 3): condition-gated departures only (work dried up,
unpaid wages, business closed, job lost, hardship — each checked
against supplied evidence, never a town-wide roll), mandatory named
destination (never randomised), identity/kinship/ownership/debts
preserved by deleting nothing, return via recruitment + settlement,
reputation penalty signal. Claimed (phase 4): the `AllowDepartures`
mechanism on `DemographicDriverContext` (default true, driver
respects it), NOT wired to the slice gate.

Actual (confirmed): both claims hold, with one integrity gap found
and partially closed. `Outmigration.Depart` reads exactly as claimed,
including the fail-closed default (`OutmigrationRequest.AllowDepartures`
defaults false, so a caller that forgets it gets a refusal — the
opposite default from the driver's fail-open true, an intentional
asymmetry documented in finding R13). The gap (finding R06):
`Depart` flips isOffMap but leaves household membership and employment
active, while `MigrationDriver.MigrateOut` ends both — and MigrateOut's
own comment warns no payroll path checks isOffMap, so a person
departed on the new channel alone would keep drawing wages and keep a
live membership while off map. Fixed in part: new
`OutmigrationCloseout.CloseAfterDeparture` mirrors MigrateOut for
exactly those two legs (`RemoveMember` + `EndEmploymentsForDeparture`
with `MigratedOut`, history retained, second close ends nothing twice,
undeparted records close nothing). It does NOT claim the full
post-departure execution: household dissolution, property
retain/transfer, and debt retention are still reported verbatim as
StillRequired (phase 4 finding F08, still open).

Also fixed (finding R07, comment-only): the outmigration docstring
still said the driver behaviour was "NOT changed in this pass" (true
in phase 3, stale after phase 4 added the mechanism); it now states
the mechanism exists and only slice activation is undecided.

Kennedy decisions, unchanged: slice-departure activation (F09),
retiring the old 4% arrival roll (F10/F35), unifying departure paths
with destinations (F11/F12). The old paths are NOT silently changed
in this pass: `MigrateIn` (smallest household, no job/housing/
reputation check) and the legacy departure draws remain live whenever
their gates allow, exactly as phase 4 left them.

### J. Items, tiers, suppliers, and the quote bridge (phase 3 + phase 4 + phase 5)

Claimed: tier lists (Priority1 / Secondary / Tertiary) are disjoint
and append-only, every tier id exists in `HouseholdItemCatalog`
(including phase 3 sugar/coffee/tea, which the authored staple_food
pool had but the catalog lacked); suppliers are caller-registered with
distance and goods price (test examples only, never canon); freight
quotes sort by delivered total; phase 4 `OffMapQuoteToOrder` validates
a quote and places the exact order, refusing SelfCollect.

Actual (confirmed): the claim holds. `MissingCatalogIds` and
`TiersAreDisjoint` are proven by phase 3 tests; the catalog additions
are append-only with stable ids; the supplier catalog registers
duplicates as ignored, rejects negative distance/price and empty SKU
lists, and computes SellerFreight as distance × caller rate and
FreightBusiness as seller freight + caller commission without
creating a freight business.

Fixed in this pass (finding R03): the bridge accepted a
`FreightOptionKind.Unspecified` quote — `QuoteAll` never emits it,
but a hand-built quote carrying freight cents under no freight option
would have ordered with a surcharge for an arrangement that does not
exist. Unspecified quotes are now refused before any order exists.
SellerFreight and FreightBusiness behaviour is unchanged; SelfCollect
refusal is unchanged.

Also fixed (finding R07, comment-only): the supplier catalog docstring
still said the driver "must take the chosen quote's total into"
the procurement service, as if no bridge existed; it now names
`OffMapQuoteToOrder`.

Codex/data remainder, unchanged: register real suppliers from a
Kennedy-approved source (F37); add tier ids to the
`GeneralStoreBusinessDefinition` asset (F07); implement SelfCollect
travel before any SelfCollect quote is orderable (F38).

### K. Main Scene manager audit (phase 3, re-verified)

Claimed: 61 MonoBehaviour classes; three GameObject managers own
almost all plain-C# simulation; Bakery/Fuel/procurement/decision
models need driver wiring, not GameObjects; D1/D2/D3 discrepancies
for Codex.

Actual (re-verified at 63b1c25): a scripted count finds 61 files
declaring a MonoBehaviour, consistent with the audit's 61 classes,
and no phase 4 file added a MonoBehaviour (all four phase 4 code
files are plain C#). The audit's rule still explains the tree: the
phase 4 bridges and the phase 5 additions (`OutmigrationCloseout`,
`SettleAndApply`) are plain C# driven by a hub or production driver,
never by their own GameObject, and no GameObject was invented for
them here.

Codex-only remainder, unchanged: D1
(`LogisticsTransportPresentationController` placement or retirement),
D2 (`LLFeedbackService` persistence), D3 (warn on absent FindUnique
targets in `PersistentRuntimeHierarchyAuthoring`), plus the audit's
CONFIRM list. Scene contents, prefab contents, and Inspector values
still cannot be verified from this repo.

### L. Bakery and Fuel Dealer (phase 1 item 3, phase 2 investigation, phase 4 F16/F17)

Claimed: both stay blocked from the old generic weekly category
converter; no fixed-ratio weekly output restored; Bakery still lacks
a Main Scene owner/driver, save DTO wiring, and BreadShelf-to-SKU
transfer; Fuel Dealer still lacks a complete Task → tree depletion →
wood lot → inventory/sale path.

Actual (confirmed): unchanged, and deliberately so. The phase 2
investigation comment is still present on
`BakeryStagedProductionChain.BreadShelf`, naming the exact blockers
(string staged lot id vs `EntityId` sellable lot, baked-day vs
transfer-day aging, no delivered mark for staged bread, lb vs SKU
units for flour). No transfer, flour feed, or consume API was faked
in this pass either: a `BakeryShopRuntime` Receive method cannot be
proven here because that runtime is outside the Roslyn harness
(Equipment/Workstations dependencies), and a flour bridge without a
driver owning bakery ordering would double-spend a lot routed to
both household sale and bakery stock. Findings F16/F17 stay open
with their phase 4 designs intact.

### M. Save DTO, coordinator, and bootstrapper forwarding (phase 2)

Claimed: additive `passXlSlice` section on the save DTO (old saves
read as defaults, newer readers ignore unknown fields); coordinator
capture/restore contract (null-safe, clear-then-load, never
regenerates population, restore after population load and before the
consistency snapshot); bootstrapper OR forwarding on New Game and
Continue.

Actual (confirmed): the claim holds. `SaveDtos.cs` carries the
additive field with its backward-compatibility docstring;
`SaveLoadManager` calls capture in build-save and restore after
population load; the coordinator code matches its contract comment
exactly. The phase 2 tests (13) pass here — see finding R15: they
were not in the phase 4 harness at all, so phase 4's full-set number
did not actually cover them.

Needs: nothing in the contract itself. The one related gap is the
registration caveat (F19, section B) and the unpersisted
`AllowDepartures` flag (R09, section C), both unchanged by design.

### N. Six-person diagnostics formatter (phase 1, phase 4 F25)

Claimed: a pure formatter that never invents a value (unknowns print
as "unknown", never fake zeros); the SimulationDrivers call site is
Codex wiring, gated by `SuppressMigrantArrivals`.

Actual (confirmed): the claim holds and the status is unchanged —
still no production caller, still a test utility by design (its 3
tests pass here, newly harnessed — see R15). One minor ambiguity,
left alone (finding R13): `BusinessCashCents` / `HouseholdCashCents`
are single caller-supplied aggregates, but the docstring does not say
summed across the slice's businesses/households or single-entity
values; the formatter cannot decide that, so the call-site guidance
should state it when Codex wires it. No code change was made on a
formatter-only ambiguity.

### O. Duplicate procurement authorities (phase 4 F26)

Claimed: `StoreProcurementChain` (category stock, Phase H path) and
`OffMapNamedSkuProcurementService` (standalone lots, Pass XL path)
are two order systems with separate ledgers and no shared guard
against ordering the same need twice; the single-SKU guard itself is
intact.

Actual (confirmed): unchanged in this pass, deliberately. The two
systems serve different paths and do not share a ledger, so there is
no double-stock today — but there is still no guard against
double-ordering the same need. A shared need/dedupe key remains a
Kennedy design decision (phase 4 design fork 11); inventing one here
would have been a silent behavioural change to both order paths.
No code change.

### P. Test coverage and the harness (phase 4 gap, found in this review)

The phase 4 full-set number (315 passed) did not include the phase 2
persistence/delivery tests (13) or the diagnostics tests (3): neither
file was listed in `harness-passxl-phase4.csproj`, although both are
plain C# and harness-safe. The Unity-only files stay excluded by
design (`PassXlPlayerAsNpcTests`, `SixPersonNoImmigrationTests` —
the latter's own header forbids harness inclusion). Finding R15:
the phase 5 harness (`harness-passxl-phase5.csproj`, outside the repo)
adds the four source files and two test files the phase 4 harness
omitted, so the phase 5 full-set number covers them. All 16 pass.

## 2. Composed paths, as they now stand (one documented path, not five fragments)

Recruitment path: `FamilyTreeWiring.ApplyAfterGeneration` (once, after
generation, before first save — Codex call site still open) creates
off-map relatives → `RecruitmentConversation.AskAboutMovers/Workers`
(known kinship only, legitimate channel required) returns a
`RecruitmentLead` → `BuildLetterRequest` (origin "general-store") →
`RecruitmentLetterPoster.PostLetter` (payer overload, origin
cross-checked since phase 5; General Store office registration still
Codex, F04) → the contact replies/arrives → `ArrivalSettlementDriver
.SettleAndApply` (gate + record + membership + employment, with
rollback on driver refusal; residence still Codex) → the resident is
on map with a household and a job (or an explicit job-seeking plan).

Procurement path: `OffMapSupplierCatalog.RegisterSupplier` (Codex/data,
F37) → `QuoteAll` (caller rates, test parameters only) →
`OffMapQuoteToOrder.PlaceOrderFromQuote` (totals recompute, divisible
goods, SelfCollect and Unspecified refused) →
`OffMapNamedSkuProcurementService.PlaceOrder` (market-validated
supplier, finite supply) → `ReceiveArrivals` (idempotent, CashOnArrival
retry / TradeCredit obligation) → `SellAndDeliverToInventory`
(atomic sale + cash + household lot, rollback on AddLot rejection) →
`PassXlSliceSaveCoordinator` capture/restore (production service
registration still Codex, F19).

Departure path: `Outmigration.Depart` (condition-gated, mandatory
destination, nothing deleted) → `OutmigrationCloseout
.CloseAfterDeparture` (membership + employment closed, history
retained; household dissolution, property, and debt decisions still
the driver's explicit steps, F08) → the departed person remains
findable by destination (`FindDepartedAtDestination`) and recruitable
in principle via the recruitment path above. The legacy
`MigrationDriver` departure path is separate and unchanged (F11/F12);
its departures end membership + employment inline via `MigrateOut`
but record no destination.

Daily-tick path (NOT composed here): nothing in this pass invents a
daily tick driver. Town reputation, traveler visits, outmigration
checks, and recruitment still need a SimulationDrivers/bootstrapper
driver to run them (F22 remainder, Codex). The bridges above are the
steps a driver calls; the driver itself is not claimed.

## 3. New review findings (R01–R17) with dispositions

| ID | Finding | Evidence | Severity | Disposition |
|----|---------|----------|----------|-------------|
| R01 | Settlement materialisation-before-driver-apply is non-atomic: Settle flips isOffMap + allocates a household id before the driver runs, so a driver refusal (dissolved/missing household, conflicting membership, join rejection) strands the person on map with no membership. Settle does not validate existing-household existence/dissolved state. | `ArrivalSettlement.cs` (materialise inside Settle, no household-existence check); `ArrivalSettlementDriver.cs` Apply refusal paths. | integrity risk | FIXED: new `SettleAndApply` with rollback of person flags on driver refusal; raw Settle/Apply unchanged. 5 new tests. |
| R02 | Letter poster ignores `request.Origin`: the origin office came purely from the caller parameter, so a letter could be posted from an office its request does not name. | `RecruitmentLetterPoster.cs` (pre-fix: `fromOfficeId` used directly); `RecruitmentConversation.cs` (Origin hardcoded "general-store"). | integrity risk | FIXED: blank caller origin falls back to request Origin; mismatch refused before any postage debit. 2 new tests. Existing phase 4 poster tests (matching office) unaffected. |
| R03 | Quote bridge accepts `FreightOptionKind.Unspecified`: QuoteAll never emits it, but a hand-built quote with freight cents under no option would order with a surcharge for a non-existent arrangement. | `OffMapQuoteToOrder.cs` (pre-fix: only SelfCollect refused). | integrity risk | FIXED: Unspecified refused before any order exists. 1 new test. SellerFreight/FreightBusiness/SelfCollect behaviour unchanged. |
| R04 | Traveler channel ignores reputation staleness while `ImmigrationConditions` has `EvaluateAtDay`: a stale snapshot could gate a stay or spread word. | `ThroughTraveler.cs` (pre-fix: no staleness check); `TownReputation.cs` `EvaluateAtDay`. | integrity risk | FIXED: additive `EvaluateVisitAtDay` (stale → passes on, no word of mouth; otherwise delegates). Original `EvaluateVisit` unchanged. 4 new tests (stale, fresh, max-age boundary, slice gate). |
| R05 | Arrival employment id `arrival-<person>-<day>` collides across businesses on the same day (registry first-wins → second job degrades to EmploymentPending). | `ArrivalSettlementDriver.cs` (pre-fix id format); `EmploymentRelationshipRegistry.Register` (duplicate rejection). | integrity risk | FIXED, flagged behavioural change: id is now `arrival-<person>-<business>-<day>`. Only newly created arrival employments are affected (driver never ran in production; no existing test asserted the old format). 1 new test. |
| R06 | `Outmigration.Depart` leaves membership + employment active, unlike `MigrateOut` which ends both; no payroll path checks isOffMap, so a new-channel departure could keep drawing wages. | `Outmigration.cs` (flips isOffMap only); `FamilyFormationDrivers.cs` `MigrateOut` (RemoveMember + EndEmploymentByReason + payroll comment). | integrity risk | PARTIALLY FIXED: new `OutmigrationCloseout.CloseAfterDeparture` closes exactly those two legs (history retained, second close duplicates nothing). Household dissolution, property, debt handling remain F08. 3 new tests. |
| R07 | Stale docstrings: seven phase 1–3 texts superseded by phase 2/4 — policy save-DTO paragraph, procurement PERSISTENCE + RECEIPT paragraphs, family-tree wiring point, recruitment posting paragraph, settlement driver-steps paragraph, outmigration slice-gate paragraph, supplier-catalog quote paragraph. | Files listed in Stage 2 notes below (pre-fix text quoted in review sections A, B, D, E, H, I, J). | consistency | FIXED: comment-only corrections naming the phase 2/4 bridges. Zero behaviour change. |
| R08 | Dead code in `FamilyTreeBuilder`: `seenParents` set built, never used (`_ = seenParents`). | `FamilyTreeBuilder.cs` (pre-fix). | dead code | FIXED: removed; duplicate-parent second passes documented as harmless no-ops. No behaviour change. |
| R09 | `AllowDepartures` is not persisted and not wired to the slice: `DriveDemographicDay` maps only `AllowNewArrivals`; the flag has no `PopulationManager` property, save field, or coordinator coverage. | `PopulationManager.cs` `DriveDemographicDay` (context built with `AllowNewArrivals` only); `PassXlSliceSaveDto.cs` (no departures field). | blocks slice (decision) | NOT CHANGED — Kennedy decision (F09). Wiring, if activated: `AllowDepartures = !SuppressMigrantArrivals` in `DriveDemographicDay`, then persisted indirectly via the suppress flag. |
| R10 | `SuppressMigrantArrivals` is set only by the fixture overload of `GeneratePopulationSnapshot`; the no-arg overload resets it false (including the `PopulationManager.Start` generate-on-start path). Continue is safe (load, not regenerate, + coordinator restore), but a future regenerate path would silently unseal a sealed game. | `PopulationManager.cs` `GeneratePopulationSnapshot` overloads + `Start`. | integrity risk | NOT CHANGED — documented. Preserving the flag across generation would change ordinary New Game semantics. Codex note: never regenerate after a sealed game without re-setting the flag. |
| R11 | Verified intact, no action: single-SKU guard (category-stock refusal, lot ledger never touches category stock); one cash writer (`HouseholdLedger`) / one debt authority (`FinancialObligationAuthority`) across phases 1–4 (no new direct cash mutation found); save backward compatibility (old saves → flags false, empty procurement); slice gate (section C); tier/catalog disjoint + complete; MonoBehaviour count still 61 files. | Grep + file reads in sections B, C, J, K. | — | No action (verified non-findings). |
| R12 | Carried items, not re-litigated: multi-SKU batch (F06), post-departure execution remainder (F08), bakery transfer (F16), bakery flour feed (F17), daily tick driver remainder (F22), postal registration (F04), item asset (F07), D1/D2/D3, procurement registration (F19), reputation reader (F23), traveler generator (F24), supplier registration (F37), SelfCollect travel (F38), double-ordering guard (F26), friend graph (F36), experience rate (F34), old arrival roll (F10/F35), departure unification (F11/F12). | Phase 4 findings doc (unchanged). | — | NOT DONE — see Codex handoff + design forks below, with exact locations. |
| R13 | Minor API inconsistencies, no change: drivers duplicate the policy's `ShouldAdvancePlayerWork` while travel/labour gates check the field inline (drivers file is MonoBehaviour, outside the harness); diagnostics cash fields are caller aggregates without a stated summed-vs-single rule; traveler null thresholds default to 1 while `ImmigrationConditions` refuses null thresholds; `OutmigrationRequest.AllowDepartures` defaults false (fail-closed) vs `DemographicDriverContext.AllowDepartures` defaults true (fail-open for ordinary scenarios) — the last is an intentional asymmetry. | `SimulationDrivers.cs`, `SixPersonSliceDiagnostics.cs`, `ThroughTraveler.cs`, `TownReputation.cs`, `Outmigration.cs`, `DemographicDrivers.cs`. | consistency | NOT CHANGED — documented. None is a behaviour defect; the drivers-file item cannot be proven in Roslyn. |
| R14 | `OffMapQuoteToOrder` total-mismatch branch is unreachable given the divisibility check (PlaceOrder recomputes the total exactly from divisible goods + freight). | `OffMapQuoteToOrder.cs` (mismatch diagnostic after PlaceOrder). | — | No action (defensive diagnostic kept). |
| R15 | Phase 4 harness omitted phase 2 + diagnostics tests: `harness-passxl-phase4.csproj` lists neither `PassXlPhase2PersistenceAndDeliveryTests` nor `SixPersonSliceDiagnosticsTests`, so the phase 4 full-set number (315) did not cover them, although both are plain C# and harness-safe. Unity-only files stay excluded by design. | `harness-passxl-phase4.csproj` file list vs repo test files. | test gap | FIXED: phase 5 harness adds the 4 source files + 2 test files; all 16 pass (13 phase 2 + 3 diagnostics). |
| R16 | Scope note, not a bug: `FamilyTreeBuilder` creates parents/grandparents only, never siblings, so recruitment sibling leads exist only when generation created the sibling. | `FamilyTreeBuilder.cs` (no sibling creation, only sibling edges among residents). | — | No action. A later pass must not invent siblings to "fix" this. |
| R17 | Pre-existing diagnostic typo outside passes 1–4: `PostalService.PostItemCore` says "mail is not accepted nowhere" (double negative). | `PostalService.cs` line ~366. | consistency | NOT CHANGED — outside passes 1–4 scope; noted so a later pass can fix the string without hunting for it. No test asserts the string. |

## 4. Stage 2 implementation notes

Stage 2 ran in priority order: integrity risks on composed paths
first (R01, R06, R02, R03, R04, R05), then consistency fixes (R07,
R08), then test gaps (R15). No Stage 2 change alters existing tested
behaviour silently, except the one flagged change below. No new
feature set was started: no daily tick driver, no boarding house, no
freight business, no friend graph, no multi-SKU batch, no bakery
transfer, no flour feed, no old-path retirement, no save-shape change.

### DONE

- R01 — `ArrivalSettlementDriver.SettleAndApply` + `ArrivalSettlementOutcome`
  (new, in `Domains/Population/ArrivalSettlementDriver.cs`). Composed
  settle-then-apply with rollback of person flags on driver refusal.
  Evidence: 5 new Roslyn tests (success, dissolved-household refusal
  with rollback, missing-household refusal with rollback, conflicting-
  membership refusal with rollback and original membership untouched,
  unsettled request with driver never run).
- R02 — `RecruitmentLetterPoster` origin cross-check
  (`Domains/Population/RecruitmentLetterPoster.cs`). Blank caller origin
  falls back to `request.Origin`; mismatch refused before any postage
  debit; success diagnostic now names the resolved origin. Evidence: 2
  new Roslyn tests (fallback posts with exactly one postage debit;
  mismatch refused with balance unchanged).
- R03 — `OffMapQuoteToOrder` Unspecified refusal
  (`Domains/Economy/Businesses/GeneralStore/OffMapQuoteToOrder.cs`).
  Evidence: 1 new Roslyn test (refused, no order, market supply untouched).
- R04 — `ThroughTravelerImmigration.EvaluateVisitAtDay`
  (`Domains/Population/ThroughTraveler.cs`). Stale snapshot → passes on
  with no word-of-mouth spread; otherwise delegates to `EvaluateVisit`.
  Evidence: 4 new Roslyn tests (stale, fresh, max-age boundary,
  slice gate through the new overload).
- R05 — Arrival employment id now includes the business
  (`Domains/Population/ArrivalSettlementDriver.cs`).
  BEHAVIOURAL CHANGE, FLAGGED: new employment ids differ from phase 4
  (`arrival-<person>-<business>-<day>` vs `arrival-<person>-<day>`).
  Safe because the driver has never run in production and no existing
  test asserted the old format. Evidence: 1 new Roslyn test (two
  businesses, same day, both jobs registered, distinct ids, exactly
  one membership).
- R06 (partial) — `OutmigrationCloseout.CloseAfterDeparture` (new,
  `Domains/Population/OutmigrationCloseout.cs`). Closes membership +
  employment after a departure, mirroring `MigrateOut`; household
  dissolution, property, and debt handling stay listed as
  StillRequired (F08 open). Evidence: 3 new Roslyn tests (close ends
  both legs with `MigratedOut` history and identity preserved; second
  close duplicates nothing; undeparted record closes nothing).
- R07 — Comment-only docstring corrections in
  `Orchestration/Player/PlayerNpcPlaytestPolicy.cs`,
  `Domains/Economy/Businesses/GeneralStore/OffMapNamedSkuProcurementService.cs`
  (PERSISTENCE + RECEIPT paragraphs),
  `Domains/Population/FamilyTreeBuilder.cs` (wiring point),
  `Domains/Population/RecruitmentConversation.cs` (posting paragraph),
  `Domains/Population/ArrivalSettlement.cs` (driver-steps paragraph +
  pre-allocated-id rule),
  `Domains/Population/Outmigration.cs` (slice-gate paragraph),
  `Domains/Economy/Businesses/GeneralStore/OffMapSupplierCatalog.cs`
  (quote paragraph). Zero behaviour change.
- R08 — `FamilyTreeBuilder` dead `seenParents` code removed
  (comment added for duplicate-parent passes). No behaviour change.
- R15 — Phase 5 harness adds the phase 2 + diagnostics sources and
  tests the phase 4 harness omitted. Evidence: 13 + 3 tests pass.

### PARTIAL

- R06 — membership + employment closeout done; household dissolution,
  property, and debt execution remain F08 (needs a driver owning those
  authorities; not faked).
- R09/R10 — documented with exact wiring locations; activation and
  any generation-preservation change are Kennedy decisions, not made.

### NOT DONE (with reason, not faked)

- F06 multi-SKU batch, F16 bakery transfer, F17 bakery flour feed,
  F22 daily tick driver, F08 remainder: same reasons as phase 4
  (persisted batch ledger; Receive API + scene owner; consume API +
  driver; tick driver ownership; dissolution/property/debt driver).
  Phase 5 adds one safe piece of F08 (R06 closeout) without claiming
  F08 itself.
- F09 activation, F10/F35 retirement, F11/F12 unification, F26
  double-ordering guard, F36 friend graph, F34 experience rate, all
  prices/rates/weights/frequencies: Kennedy decisions (see forks).
- F04, F07, F13/F14/F15, F19, F23, F24, F37, F38: Codex-only (scene,
  asset, registration, reader, generator, travel execution).
- R13, R14, R16, R17: documented, not changed (see dispositions).

### Roslyn results (harness `harness-passxl-phase5.csproj`, outside repo)

- New phase 5 tests: 16 passed, 0 failed
  (`PassXlPhase5ReviewRefinementsTests`: 5 composed settlement, 1
  employment-id, 2 letter poster, 1 quote, 4 traveler, 3 closeout).
- Phases 1–4 tests still passing (breakdown, all in the phase 5 harness):
  phase 1 procurement 14 passed; policy 3 passed; phase 2 persistence
  + delivery 13 passed (newly harnessed, R15); phase 3 family/
  immigration/items 18 passed; phase 4 findings fixes 19 passed;
  diagnostics 3 passed (newly harnessed, R15). Unity-only files
  (`PassXlPlayerAsNpcTests`, `SixPersonNoImmigrationTests`) are not in
  the harness by design and were not run here.
- Full set: 347 passed, 3 failed — the 3 failures are exactly the
  pre-existing `HouseholdVerticalSlicePhaseHTests` failures from the
  phase 4 baseline (InsufficientFunds, Hardship missed meals, Restock
  60 vs 59). No new failure. Arithmetic: 315 (phase 4 baseline) + 16
  (new phase 5) + 13 (phase 2, newly harnessed) + 3 (diagnostics,
  newly harnessed) = 347.
- Harness note: the phase 5 harness is the phase 4 harness retargeted
  from `repo-passxl4` to `repo-passxl5`, plus `OutmigrationCloseout.cs`,
  the new test file, and the four phase 2/diagnostics sources + two
  test files (R15). The phase 4 harness's `EstateServiceShim` caveat
  carries over unchanged (mortality/estate path untested here, not
  proven).
- NOT claimed: Unity compilation, Unity Test Runner, PlayMode, scene /
  prefab / Inspector wiring, Main Scene visual behaviour, or
  full-regression status beyond the Roslyn full-set result above.

## 5. Codex handoff (Editor / scene / asset tasks)

Exact names only; nothing here is claimed done. Items 1–15 carry over
from phase 4 (unchanged); items 16–18 are new or updated in phase 5.

1. Family-tree call site (F01): in `FirstLedgerSliceBootstrapper`
   (or the equivalent New Game path), immediately after
   `PopulationManager.GeneratePopulationSnapshot` returns and before
   the first save, call `FamilyTreeWiring.ApplyAfterGeneration`
   (population = PopulationManager state, kinship = PopulationManager
   kinship registry, seed = world seed, referenceYear = scenario start
   year from the calendar anchor). Verify in Editor that off-map
   relatives appear and that a second call adds nothing.
2. Residence assignment (F02 remainder): after
   `ArrivalSettlementDriver.SettleAndApply` (preferred since phase 5)
   or `Apply` returns, assign the record's `HousingUnitId` through
   the HousingResidence runtime and set the person's `homeBuildingId`
   (currently left at -1 by settlement). Until this exists, settled
   arrivals have household + job but no residence.
3. Postal office registration (F04): register the General Store office
   with `PostalService.RegisterOffice` (origin office id
   "general-store" per `RecruitmentLetterRequest.Origin`) in Main Scene
   bootstrap before any `RecruitmentLetterPoster.PostLetter` call, plus
   any destination offices; off-map contacts use
   `PostalService.OffMapOfficeId` ("offmap"). Until this exists, every
   posting is refused in production (proven by test).
4. Production suppliers (F37): populate `OffMapSupplierCatalog` via
   `RegisterSupplier` with real supplier ids, distances, offered SKUs,
   and goods prices from a Kennedy-approved source. The catalog is
   empty in production today; all existing examples are test parameters.
5. Daily tick driver (F22/F23/F24): a SimulationDrivers / bootstrapper
   driver that, every day, (a) aggregates `TownReputationInputs`
   (housing vacant/total, open/filled job slots, growth, opinion) from
   live runtimes and calls `TownReputationModel.Compute` (prefer
   `ThroughTravelerImmigration.EvaluateVisitAtDay` over `EvaluateVisit`
   when gating travelers on reputation, so stale snapshots refuse);
   (b) generates `ThroughTravelerVisit` records (route, boarding
   vacancies from `BoardingHouseShopRuntime`) and calls
   `EvaluateVisitAtDay`, then `ArrivalSettlementDriver.SettleAndApply`
   for stays; (c) runs `Outmigration.Depart` checks, then
   `OutmigrationCloseout.CloseAfterDeparture` for the membership +
   employment legs, then executes the StillRequired household /
   property / debt decisions (F08). Reputation weights stay
   caller-supplied until Kennedy sets them.
6. Post-departure execution remainder (F08): a driver that, for each
   `OutmigrationRecord` after `OutmigrationCloseout` has closed
   membership + employment, settles household dissolution, property
   retain/transfer, and debt retention through their own authorities,
   runs the return loop (`FindDepartedAtDestination`), and feeds the
   reputation penalty into `TownReputationModel` as
   `GoodOpinionScore01`.
7. Bakery transfer (F16): add a public Receive method to
   `BakeryShopRuntime` (duplicate staged-lot rejection, provenance
   carried over, `EntityId` allocated via `EntityIdRegistry` or
   honestly reported unregistered), plus a Main Scene owner/driver
   holding both `BakeryStagedProductionChain` and `BakeryShopRuntime`
   that moves finished staged lots to the sellable shelf; verify the
   transfer in Editor and confirm save wiring for both shelves.
8. Bakery flour feed (F17): add an additive ConsumeForProduction API
   to `OffMapNamedSkuProcurementService` (quantity + lot + provenance,
   no double-spend with `SellToHousehold`), a bridge into
   `BakeryFlourStock.ReceiveLot`, and a driver owning bakery ordering.
9. Multi-SKU batch (F06): design + implement a persisted batch ledger
   on the procurement service (batch id + per-SKU order ids in
   ExportState/ImportState) so a retried batch creates nothing twice;
   keep the single-SKU guard intact (staple_food still refused as a
   single-item offer).
10. Scene discrepancies (F13/F14/F15): D1 — place
    `LogisticsTransportPresentationController` on a Main Scene
    GameObject with its `LogisticsRuntimeManager` + wagon prefab
    references, or retire it on Kennedy's word. D2 — place
    `LLFeedbackService` on a Main Scene GameObject with clips
    configured, or keep PlayMode self-creation deliberately. D3 — in
    `Editor/PersistentRuntimeHierarchyAuthoring.cs`, log a warning
    naming the missing component type + property when a `FindUnique`
    target is absent instead of silently skipping it.
11. Procurement service registration (F19): set
    `SaveLoadManager.PassXlProcurementService` to the production
    `OffMapNamedSkuProcurementService` instance in Main Scene bootstrap
    before the first save, or procurement persists and restores empty.
12. Playtest flag (F30): set
    `FirstLedgerSliceBootstrapper.treatPlayerAsNpcPlaytest` in the Main
    Scene Inspector for playtest scenes; verify the Continue OR
    behaviour (true forces NPC mode, false never clears a saved true).
13. Item asset (F07): add the `GeneralStoreItemTiers` ids (Priority1 /
    Secondary / Tertiary, including sugar, coffee, tea) to the
    `GeneralStoreBusinessDefinition` asset in the Unity project;
    verify in Editor.
14. Unity-only verification (F18/F20/F21): compile `SaveLoadManager`
    and `FirstLedgerSliceBootstrapper` in Editor; run Unity Test Runner
    / PlayMode, including the Unity-only `PassXlPlayerAsNpcTests` and
    `SixPersonNoImmigrationTests`; run `ConstructionInputSupportTests`
    (109 tests) with per-test timing to find the 1800s-timeout
    pathology; diagnose the 3 Phase H failures in a dedicated pass
    (not attempted here).
15. SelfCollect journey (F38 remainder): implement player/NPC travel
    execution for procurement pickup before any SelfCollect quote is
    orderable; the bridge refuses SelfCollect until then.
16. NEW — Diagnostics call site (F25 + R13): when wiring
    `SixPersonSliceDiagnostics.Format` into `SimulationDrivers
    .OnDayChanged` (gated by `SuppressMigrantArrivals`), state whether
    `BusinessCashCents` / `HouseholdCashCents` are summed across the
    slice's businesses/households or single-entity values; the
    formatter cannot decide that.
17. NEW — Traveler staleness (R04): drivers gating travelers on
    reputation should call `EvaluateVisitAtDay` with the current day
    and a max age (7 days is the test parameter used in phase 5
    tests, not canon); a stale snapshot must trigger a recompute,
    not a stay.
18. NEW — Slice-gate regeneration guard (R10): never call the no-arg
    `PopulationManager.GeneratePopulationSnapshot()` after a sealed
    game without re-setting `SuppressMigrantArrivals`; the no-arg
    overload resets it false by design.

## 6. Design forks needing Kennedy's decision

Nothing here is guessed; every rate, price, weight, and frequency below
is a test parameter today, never canon. Items 1–13 carry over from
phase 4; item 14 is new in phase 5.

1. Slice-departure gate (F09/R09): wire `PopulationManager` to set
   `DemographicDriverContext.AllowDepartures = !SuppressMigrantArrivals`
   in `DriveDemographicDay`, or leave slice departures running? The
   mechanism is done and tested; activation is the decision.
2. Retire the old 4% arrival roll (F10/F35): delete or gate
   `MigrationDriver.MigrateIn` (4% monthly, smallest household, no
   job/housing/reputation check) in favour of the traveler +
   recruitment + settlement channels? Retiring it changes existing
   tests and the ordinary-scenario arrival rate.
3. Unify departure paths (F11/F12): route legacy
   `MigrationDriver.MigrateOut` departures through
   `Outmigration.Depart` with a caller-supplied destination, or accept
   that legacy departures stay untracked to any destination (and
   therefore unfindable / unrecruitable)? Destinations are never
   randomised; a legacy departure has no destination to record today.
4. Freight rates and supplier prices (F05/F37): cents-per-mile freight,
   freight-business commission, and every supplier goods price. The
   bridge passes quote cents through exactly; it invents none.
5. Reputation weights (F23): housing / work / growth / opinion weights
   for `TownReputationModel` (tests use 0.3 / 0.5 / 0.1 / 0.1, flagged
   test-only). Work is described as the biggest factor; the weight that
   makes it so is Kennedy's call.
6. Destination naming (F12): what counts as a valid destination record
   for a departure (family location, previous home, supplier/route
   town, named new destination + reason) and who supplies it.
7. Traveler frequency (F24): how often through-travelers arrive, by
   route and season if applicable. No generator exists until this is set.
8. D1 GameObject vs retirement (F13): place
   `LogisticsTransportPresentationController` in Main Scene or retire it.
9. Flour lot ledger home (F17): does bakery flour live in the
   procurement service's standalone lot ledger, in `BakeryFlourStock`,
   or in both with a consume bridge? The bridge design above assumes
   consume-from-procurement-into-stock; the ownership call is Kennedy's.
10. Bakery transfer ownership (F16): which Main Scene owner/driver holds
    both the staged chain and the shop runtime, and whether staged lots
    move wholesale or only on bake completion.
11. Double-ordering guard (F26): `StoreProcurementChain` (category stock)
    vs `OffMapNamedSkuProcurementService` (standalone lots) — is a
    shared need/dedupe key wanted, or do the two paths stay independent
    by design?
12. Friend graph (F36): build a friend graph so recruitment can offer
    friends as leads, or accept family-only recruitment?
13. Experience formation rate (F34): playtest-feel review; any rate
    change is canon-sensitive and was not made here.
14. NEW — Arrival employment id format (R05): the id changed in this
    pass to include the business id. No production arrival employment
    exists yet, so nothing needs migrating; confirm the new format is
    acceptable, or direct a different uniqueness scheme (e.g. a
    registry sequence) before the driver ever runs in production.
