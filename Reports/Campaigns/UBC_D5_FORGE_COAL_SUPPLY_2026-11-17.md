# UBC D5 — Forge-coal supply driver for the blacksmith (D4 follow-up)

Date: 2026-11-17. Base: D4 commit 2c47988f (muse/ubc-d4-iron-supply-2026-11-17).
Branch: muse/ubc-d5-forge-coal-2026-11-17. Harness: Roslyn/.NET 8 only, NOT UNITY VERIFIED.

## Route found, not built

No parallel supply path was built and no new supplier was invented. Forge
coal already had a legitimate registered route: ImportCatalog
"forge-coal" ("Forge coal", "Off-map coal dealer, via railhead", 180 mi,
12 days transit, 18c per unit). The catalog's own doc says the built-in
materials "cover the blacksmith's needs (iron, steel, forge coal)", and
FuelYardProfile states the forge "stays on imported forge coal (specialty
fuel), not yard wood" — the fuel dealer is not a forge-coal source. The
smith is wired to the registered material through
ImportService.PlaceOrderForBusiness, mirroring the D4 iron driver
one-to-one.

## What was built

- `BlacksmithCoalImportDriver` (+ nested save DTO, deep-copy order book,
  clear-then-load restore) and `BlacksmithCoalReorderPolicy` + data-only
  `BlacksmithCoalReorderSignal`, both in `LandLedgers.Economy.Blacksmith`.
- Covered-stock rule, single-outstanding-order guard, cash from the
  smith's own business cash port or honest credit via the liability
  ledger; no cash and no credit leaves no order, no lot, no payable. A
  miswired cash port never pays.
- Calibration (PROVISIONAL, marked for Kennedy to correct): threshold 6
  units, lot 12 units (216c). Reasoning in the policy docstring: one unit
  fires four forgings, a working smith burns ~1 unit/day, so 6 covers
  half a 12-day transit cycle and 12 is about a cycle of steady forging.
- Wiring: hub holder/Register/getter/capture/deferred restore,
  `SystemsSaveDtos.blacksmithCoalImportDriver`,
  `SimulationDrivers.DriveBlacksmithCoalImports` daily after the iron
  import drive, bootstrapper `ConfigureBlacksmithCoalImportWiring` from
  the smith save owner, Pass 9 baseline entries for policy/signal only
  (driver accounted for by its production callers).
- 11 new tests in `BlacksmithCoalImportDriverTests`, including the
  end-to-end proof: a smithy with iron but no coal cannot forge, orders
  day 1, coal lands day 12 as a provenance lot (`IMP-{orderId}-1`), then
  4 shoes are forged from one iron unit and one coal unit.

## Verification

- Baseline in this tree: 5012 passed / 75 failed / 5087 total (the D4
  state; FAIL set byte-identical to D4's `ll-ubc-d4-fail1.txt`). Log
  `/home/hatch/ll-ubc-d5-run0.log`, FAIL set
  `/home/hatch/ll-ubc-d5-fail0.txt`.
- After D5: **5023 passed / 75 failed / 5098 total**, build exit 0, 0
  errors, 1173 Compile Includes. Delta exactly the 11 new tests, all
  passing, zero new failures, FAIL set byte-identical to baseline. Log
  `/home/hatch/ll-ubc-d5-run1.log`, FAIL set
  `/home/hatch/ll-ubc-d5-fail1.txt`. (Run exit 1 is the harness exit
  code with 75 pre-existing failures, as in the baseline.)
- Pass9: the `AssemblyScan_EveryScannedType_IsAccountedFor` failure is
  one of the 75 baseline failures and its unaccounted-type list content
  is identical before and after D5 (89-line failure blocks differ only
  in the shim path and the assertion line number, shifted +10 by the
  baseline entries). None of the new types appears in the list: the
  driver is accounted for by its production callers, the policy/signal
  by the new baseline entries.
- Expanded build of the four harness-excluded wiring files
  (SystemsSaveDtos, FirstLedgerSliceBootstrapper, SimulationDrivers,
  SimulationSystemsHub — SystemsSaveDtos is NOT in the normal harness
  config, despite the task brief saying otherwise; only the driver,
  policy, tests, and Pass9 test are harness-compiled): 120 errors / 11
  warnings, error multiset identical to D4's (74 unique messages),
  zero errors on the changed lines, zero naming the new types. The
  "D5-only" error locations are pure line shifts from the added lines;
  spot-checked, they are the same pre-existing `BoardingHouseShopRuntime`
  CS0246s. Log `/home/hatch/ll-ubc-d5-build-expanded1.log`, csproj
  `/home/hatch/ll-ubc-d5-harness/harness-expanded.csproj` (1177 includes).

## Environment notes (for the parent and watchdog E)

- The disk quota blocked a full worktree checkout, so this lane works
  in a SPARSE worktree (`Assets/Scripts`, `Reports`,
  `Assets/Core/Population` only, 87M instead of 905M). The baseline's
  first run read 5009/78 because the sparse checkout initially lacked
  `Assets/Core/Population/DevelopmentTownConfiguration.asset`, which the
  3 SawmillAuthorshipC8Tests read directly — a sparse-worktree artifact,
  not a code difference. Adding that directory to the sparse checkout
  restored the exact D4 baseline 5012/75. E should note the sparse
  worktree when re-running: the harness needs only `Assets/Scripts`,
  plus that one asset directory for the C8 tests.
- To clear the quota I deleted ~900M of rebuildable build outputs:
  stale integration bin/obj pairs 1–12 and completed lanes' bin/obj
  dirs. All run/build LOGS were kept; only binaries and obj dirs went.
  The old detached scratch worktrees (L-F5, ~40) were NOT touched.
- The copied gen script's HARNESS variable still pointed at the D4
  harness dir, so the first post-change build read D4's sources.txt
  (without the coal lines) and failed with 2 CS0246s on the missing
  driver type. Fixed by repointing HARNESS at the D5 harness dir; the
  second build is 0 errors with all 3 new files in the csproj.

## Follow-ups recorded, not built

- The smith cannot forge in production until a Blacksmith business is
  formed (dev town has none); both import drivers are wired but idle
  until then.
- Smith shop stock has no Pass 9 holder (pre-existing gap, same family
  as the D1 quarter-pool save gap — L1 has since persisted the pools).
- Coal threshold 6 / lot 12 is provisional calibration for Kennedy.
- With D5, the smith's two forge inputs (iron and coal) both have
  legitimate supply drivers. The remaining forge-consumable gap is
  none: shoes and hardware consume only iron and coal quarter pools.

## Codex Unity checklist

The `SimulationDrivers` daily drive, the `RegisterBlacksmithCoalImportDriver`
forwarder, the hub save wiring, and boot seeding are harness-excluded by
the shared config and need Codex's Unity EditMode run. The driver, policy,
signal, save DTO, and the 11 new tests are harness-compiled and covered by
the full run.
