# UBC D6 — Kind-level yard hardware sale lines (F-D2-3)

Date: 2026-11-17. Base: D5 commit `452c153e` (muse/ubc-d5-forge-coal-2026-11-17).
Branch: muse/ubc-d6-kind-level-yard-sales-2026-11-17. One commit.

## What changed

The yard hardware sale was bucket-level: a construction package's hardware
requirement ("door hardware", 9/18/18 units) was filled nails-first from a
mixed total, and the sale recorded one line for the whole bucket. The piece
kinds a package actually needs (door hinges, iron latches, door handles,
nails) were never tracked — a sale of 9 units could be 9 hinges and the
package would still stage. That is fixed at every link:

- **Yard lots carry real piece kinds now.** `LumberYardHardwareKind`
  gains `DoorHinge = 2, IronLatch = 3, DoorHandle = 4` (append-only;
  `Nails = 0, SimpleHardware = 1` unchanged). One lot holds ONE kind.
  `LumberYardHardwareKindNames` is the single mapping between a kind and
  its canonical display name (`"door-hinge"`, `"iron-latch"`,
  `"door-handle"`, `"nails"` — the smith's own kind strings). The mixed
  `SimpleHardware` kind remains for legacy and bootstrap lots, but it is
  NOT a fulfillable kind: no kind-level sale ever draws a hinge from a
  mixed lot, because the lot cannot prove it holds one.
- **The D3 transfer driver lands one lot per kind.** A delivery of mixed
  forged pieces now calls the yard intake once per piece kind (kinds in
  display-name order for determinism), each lot carrying the job's full
  breakdown in its SupplierNote. The job records every lot
  (`YardLotIds`; `YardLotId` stays the first). A later kind's intake
  failing after earlier kinds landed is handled as a partial delivery:
  the job FAILS, the debit stands, the smith is not credited, only the
  unlanded pieces return — a retry can never double-deliver the landed
  kinds. The load-time already-delivered check now matches lots across
  kinds by (smith, forged day, supplier note) with the unit total.
- **The yard sells per kind.** New per-kind overload of
  `TrySellToConstructionProject` (kind lines + one unit price). Atomic
  per kind: every kind line must be covered in full by real lots of that
  kind, or the whole sale is refused loudly naming the short kind(s) and
  nothing is consumed. The sale record keeps one material line whose
  dispense lines carry the kind per lot, so the adapter can record one
  sale line per kind. The bucket entry is UNCHANGED and kept for direct
  yard callers (the on-account path still bucket-fills — follow-up below).
- **The adapter sells per kind.** Each hardware requirement is resolved
  to kind lines (`ConstructionHardwareKindBreakdown`, below), the yard
  fulfills them, and the `ConstructionMaterialSale` carries one line per
  kind sold (display name, units, bucket unit price, per-kind
  provenance). The sale total is unchanged by the split, and the
  adapter's total check still holds.
- **The requirement can state its mix.** `ConstructionMaterialRequirement`
  gains `HardwareKindLines` (explicit per-kind lines, summing to
  `RequiredUnits`). Empty means the mix is derived at sale/stage time
  (next point). ProductionDesignPresets.cs and
  NpcConstructionDesignsDef011Tests.cs were NOT touched.
- **One breakdown rule, used by both sides.**
  `ConstructionHardwareKindBreakdown.TryBreakdown` resolves a hardware
  requirement to per-kind lines from (1) its explicit kind lines, when
  present (validated: known kinds only, positive units, no kind twice,
  summing exactly to `RequiredUnits`), or (2) the D2 door+window
  calibration: one door + one window is 9 units = 3 hinges + 2 latches +
  1 handle + 3 nails. All three production designs pair doors and
  windows equally (settler 1+1 = 9, family and workshop 2+2 = 18). A
  bucket total that is not a whole number of pairs refuses with the mix
  named unknown — the sale and the staging both call this helper, so
  both sides always agree and neither ever guesses.
- **The site stockpile stages and consumes per kind.**
  `ConstructionSiteStockpile.CoversAll` requires every kind on site in
  full; `ConsumeForPackage` draws each kind from the matching on-site
  lines (FIFO within the kind). A package needing 2 hinges + 1 latch +
  1 handle takes exactly those kinds. The consumed line recorded per
  requirement keeps the requirement's own display name and total (the D2
  audit shape), with the per-kind provenance joined — so the D2
  end-to-end assertions (4 consumed lines, "door hardware" x9) pass
  unchanged. A bucket "door hardware" line on site does NOT cover a
  kind need.

## Money — unchanged by design

Kind lines are priced at the requirement's bucket price (the price port
prices the whole hardware requirement from the first stocked lot). The
settler roof is still 9 hardware units at 45c = 405c, inside the 26,005c
end-to-end pin (640 lumber at 40c + 9 hardware at 45c). Per-kind pricing
(nails at their own 30c cost, pieces at 45c) would change the money and
is a follow-up, not smuggled in here. The D2 end-to-end test's forged
mix was corrected to the calibration mix its own comment describes
(3 hinges + 2 latches + 1 handle, was 2/2/2 — the bucket never cared,
the kinds do); every other assertion in that test is untouched, and the
empty-yard negative test is untouched.

## What still cannot be distinguished

- A `SimpleHardware` lot (mixed pieces, bootstrap endowment, or a
  direct mixed intake) cannot fill any kind-level sale, even though its
  SupplierNote may name the pieces in text. The note is prose, not data;
  parsing it would be guessing. Bootstrap SimpleHardware is therefore
  stranded for construction sales until it is re-authored as per-kind
  lots (follow-up).
- The pair derivation assumes doors == windows. A future design with
  unequal doors and windows must carry explicit kind lines — the pair
  rule refuses its bucket total rather than guessing.
- The direct bucket yard entry and the on-account sale still fill from
  the bucket. Only construction procurement (adapter path) is kind-level.
- The D3 driver's already-delivered scan still has the pre-existing
  same-day identical-delivery ambiguity (two identical deliveries the
  same day match the same lots); the job record remains the
  exactly-once authority.

## Verification (Roslyn harness, .NET 8)

- Baseline (base `452c153e`, before changes): 5023 passed / 75 failed /
  5098 total. Log `/home/hatch/ll-ubc-d6-run-baseline.log`.
- Post-change: **5036 passed / 75 failed / 5111 total** — delta exactly
  the 13 new tests. Log `/home/hatch/ll-ubc-d6-run-post2.log` (build
  `/home/hatch/ll-ubc-d6-build-post2.log`, 0 errors). FAIL set
  byte-identical to the D5 baseline (75/75, same test names).
- Pass9 assembly scan: the unaccounted-type list is byte-identical to
  the D5 baseline (only the assertion line number shifted +9, as with
  D5's +10). One new type needed classification: `LumberYardHardwareKindLine`
  is now in the feature-internal baseline with a D6 comment (same
  precedent as D3/D4/D5); the other new types are accounted for by
  production callers.
- Expanded build (the four harness-excluded wiring files):
  120 errors / 11 warnings, error list identical to D5's (modulo lane
  path); zero errors on the changed lines or the new types. Log
  `/home/hatch/ll-ubc-d6-build-expanded.log`, csproj
  `/home/hatch/ll-ubc-d6-harness/harness-expanded.csproj` (1178 includes).
- Harness coverage: all 10 changed/new code files are compiled by the
  normal harness (verified in `harness.csproj`, 1174 includes). No
  changed file is harness-excluded, so no new item for the Codex Unity
  checklist beyond the existing D3/D4/D5 wiring items.
- Per-kind pins: settler roof 3 hinges + 2 latches + 1 handle + 3 nails
  = 9 units at 45c = 405c; family/workshop 6 + 4 + 2 + 6 = 18 units.
  A package staged from per-kind lines consumes exactly 2 hinges +
  1 latch + 1 handle when that is its requirement; a one-hinge
  shortfall refuses naming `door-hinge` with nothing consumed; six
  hinges never stand in for latches and handles.

## Files changed (12 + this report)

Production (6):

- `Domains/Economy/Businesses/LumberYard/LumberYardHardwareLots.cs` —
  piece kinds on the enum, `LumberYardHardwareKindLine`,
  `LumberYardHardwareKindNames`, unfiltered withdrawal now
  nails → identified pieces → SimpleHardware.
- `Domains/Economy/Businesses/Blacksmith/BlacksmithToYardHardwareTransferDriver.cs` —
  per-kind intake, `YardLotIds`, multi-lot verification, partial-delivery
  handling, load-time multi-lot already-delivered check.
- `Domains/Economy/Businesses/LumberYard/LumberYardShopRuntime.cs` —
  per-kind sale overload + `ExecuteConstructionSalePerKind`; lumber
  withdrawal factored into `WithdrawLumberSaleLine` shared by both paths.
- `Domains/Economy/Businesses/LumberYard/LumberYardConstructionSaleAdapter.cs` —
  breakdown resolution, per-kind yard call, one sale line per kind.
- `Domains/Economy/Businesses/Construction/ConstructionContractRecords.cs` —
  `ConstructionHardwareKindLine` + `HardwareKindLines` on the requirement.
- `Domains/Economy/Businesses/Construction/ConstructionMaterialFreight.cs` —
  `ConstructionHardwareKindBreakdown` + per-kind `CoversAll` /
  `ConsumeForPackage`.

Tests (5 modified + 1 new + 1 .meta):

- `Tests/Editor/Economy/KindLevelYardHardwareSaleTests.cs` (new, 13 tests)
  + `.meta` (fresh GUID).
- `Tests/Editor/Economy/HardwareDeliveryDriversTests.cs` — end-to-end
  delivery now expects 3 per-kind lots (1 handle, 4 hinges, 2 latches)
  and `job.YardLotIds`.
- `Tests/Editor/Economy/ConstructionHardwareEndToEndTests.cs` — smith
  stocking now lands per-kind lots with the calibration mix (3/2/1);
  money pin and all other assertions untouched.
- `Tests/Editor/Economy/BlacksmithHardwareTests.cs` — comment only (the
  mixed-lot intake note now says kind-level lots exist for driver
  deliveries).
- `Tests/Editor/Integration/Pass9RegistryAssemblyScanTests.cs` — D6
  baseline entry for `LumberYardHardwareKindLine`.

## Follow-ups (recorded, not decided)

- F-D6-1: re-author the bootstrap SimpleHardware endowment as per-kind
  lots (needs an authoring decision on the mix); until then it cannot
  fill kind-level construction sales.
- F-D6-2: per-kind pricing (nails at 30c, forged pieces at 45c) — changes
  the money pins; needs a deliberate decision and re-pin, not a silent
  change.
- F-D6-3: designs should carry explicit door/window counts (or explicit
  kind lines) so the mix is data, not the provisional pair derivation;
  a design with unequal doors and windows currently refuses.
- F-D6-4: the on-account construction sale
  (`TrySellToConstructionProjectOnAccount`) still bucket-fills; it needs
  kind lines from the adapter path too.
- F-D6-5: the D3 already-delivered scan's same-day identical-delivery
  ambiguity now spans kinds; a partial already-delivered load (lots
  summing to fewer units than the job) keeps the link, which risks a
  double delivery of the landed kinds on re-run.

## Codex Unity checklist (no new items)

No changed file is harness-excluded, so D6 adds nothing to the Unity
checklist. The existing D3/D4/D5 wiring items still stand (daily drives,
bootstrapper wiring, hub restore — verified only via the expanded build
above, whose error list is unchanged from D5).
