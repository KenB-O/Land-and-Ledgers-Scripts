# F2 — Roofing review: rough boards vs wood shingles vs other (2026-11-17)

## Verdict: interim scope documented, no distinct roofing good built

All three production designs specify a **log roof with lumber only**.
The roof phase ("Framing - roof") consumes lumber — the rough-board
covering folded into the same lumber quantity as the framing — plus
door hardware for the openings, and no roofing material at all. Wood
shingles are the documented future option, not a current requirement:
E1 §8/§9 rules that "if a design later uses wood shingles, the §8
shingle rate applies and the roof package must consume shingles as a
real item". No design uses them today, so no shingle supply chain is
built in this lane. A shingle import driver, yard lots, price path and
reorder policy with zero consuming designs would be supply without
demand — the exact speculative industry the campaign rules forbid
(workstream J: "no new industries merely because definition files
exist"; M3 F-M3-3: "do not invent suppliers"). The system already
fails closed honestly on the missing good, and this lane pins that
boundary in tests so a future shingled design — or a future shingle
supply chain — must change them deliberately, not silently.

## What the roof phase consumes today (per design)

From `ProductionDesignPresets.cs` (read, not modified). Every design
has three phases (Sills, Framing - walls, Framing - roof); the only
tool named anywhere is "saw".

| Design | Sills | Walls | Roof ("Framing - roof") | Total lumber | Roof labour |
|---|---|---|---|---|---|
| Settler cabin | 40 lumber | 300 lumber | 300 lumber + 9 door hardware | 640 | 2,100 min |
| Family cabin | 60 lumber | 450 lumber | 450 lumber + 18 door hardware | 960 | 6,600 min |
| Carpenter's workshop | 80 lumber | 600 lumber | 500 lumber + 18 door hardware | 1,180 | 13,200 min |

Roof-phase notes (E2, folded): settler "log roof 1500 + door and
window openings 600"; family "log roof 4200 + doors and windows 1200
+ interior partition and finish 1200"; workshop "log roof 7200 +
doors and windows 2400 + interior fit-out 3600". The openings labour
(and the hardware that hangs the doors) rides in the roof phase until
a later H-lane splits a dedicated openings phase; glass rides there
too when a build is glazed (F1). No phase in any design names a
roofing material. The Def011 tests already pin these lists exactly
(`NpcConstructionDesignsDef011Tests.AssertDesignMaterials` fails on
any unexpected material kind, and the per-design tests pin the roof
phase at exactly two materials) — this lane does not re-pin them.

## Rough boards vs wood shingles vs other

- **Rough boards (current scope).** The roof covering today is
  sawn lumber boards, folded into the roof phase's lumber quantity.
  One lumber unit is one milled white-pine board, 1 in x 12 in x 16
  ft = 16 board feet (G1, DEF-016), so the settler roof's 300 units
  are 4,800 board feet. That is a LUMPED quantity — framing (rafters,
  purlins, ridge, plates) plus the board covering plus the openings
  timber — not a precise covering takeoff. It is honest as a lumped
  authored quantity in the DEF-011 lineage, and E1's "log roof"
  framing assumes exactly this scope. A future design that wants a
  separated covering quantity must do a real takeoff; it cannot
  recover one from the lumped number.
- **Wood shingles (documented future option).** The only alternative
  roofing in the evidence. Canon names shingles among the
  manufactured goods rail access improves ("dimension lumber,
  shingles, doors, windows, hardware"), and roofer/shingler is a
  named occupation with tools (shingling hatchet/hammer, knife, saw,
  rule, marking/chalk tools, ladder/scaffold). E1 §8 (Part VI §6.1;
  C1, C2): ordinary straight work 1,800–2,500 shingles per capable
  worker-day; claims of 3,000–4,000 treated skeptically in the March
  1880 *Carpentry and Building* discussion and not the default;
  complex roofs (cuts, valleys, poor material) reduce the rate by
  geometry, not by a flat modifier. The 07 prices source (CAL-006)
  agrees at ~2,000 shingles per capable worker per 10-hour day,
  1880. No shingle PRICE is sourced anywhere in the evidence — a
  future shingle good needs a priced supply (import or yard), not
  just a labour rate.
- **Other (not in the evidence).** No other period roofing — slate,
  metal/tin, sod, thatch, tar paper — appears in Canon, the
  Technical Bible, the E1 report, or the prices source for this
  setting. None is modelled, none is proposed. If a future design
  wants one, it needs its own historical anchor first; this lane
  invents none.

## What already exists for roofing (and what does not)

Exists as DATA, with no good behind it:

- `ConstructionMaterialKind.Roofing = 6` (D4G,
  `ConstructionContractRecords.cs`) — the kind exists in the enum.
- `ConstructionMaterialFreight.CategoryKeyFor(Roofing)` returns
  `"construction-roofing"` — the freight layer knows the category
  key, and `DiscoverSupplier` refuses when no real supplier exists
  for it (no phantom supplier).
- The price port refuses it: `LumberYardConstructionPricePort`
  prices only lumber (yard retail), hardware (first stocked lot),
  and glass (first stocked lot, F1); "masonry, paint, roofing and
  anything else return minus one; they are never mapped to lumber,
  hardware, or glass." That refusal is already pinned by
  `LumberYardConstructionPricePortTests.NonStockedKind_ReturnsMinusOne`
  (which includes Roofing in its loop).

Does NOT exist: no shingle definition, no yard lot type, no import
registration (`ImportCatalog` carries iron-stock, forge-coal,
nails-hardware, glass-panes — no shingles), no price, no reorder
policy, no design requirement, no yard sale bucket. The D3/F1
import pattern (`ImportService.PlaceOrderForBusiness`, paid from
real business cash or honest credit, delivered into identifiable
inventory) could be reused when a consumer exists; nothing about
the pattern needs inventing.

## What a future shingled design would need

When a design (e.g. the two-story storefront in workstream E's
envelope, 8–14 weeks elapsed) is authored with a wood-shingle roof,
in this order:

1. An authored roof area and a shingle count derived from it (a
   real takeoff — the lumped roof lumber cannot be reused as a
   covering quantity), plus shingling labour at the E1 §8 rate
   (1,800–2,500 shingles per capable worker-day, straight work).
2. A priced shingle supply through a legitimate route: a shingle
   definition, yard lots with provenance and unit cost, a price-port
   branch (same first-stocked-lot rule as hardware/glass), and an
   import driver on the D3/F1 pattern (registered material, paid
   order, transit, delivery into identifiable inventory). No
   phantom merchant, no free shingles.
3. A `RoofingRequirement` helper beside `LumberRequirement` /
   `HardwareRequirement` in the design presets, with the yard as
   supplier and "wood shingles" as the display name.
4. The Def011 pins updated deliberately (roof phase gains a third
   material; the money pins move) and the F2 tests in this lane
   updated to the new scope — the refusal tests exist precisely so
   this change is a decision, not an accident.

## Owner decision (open, not decided here)

When the two-story storefront (or any finer design) is authored,
does it use a wood-shingle roof — triggering the shingle supply
chain above — or a board/log roof within the current scope? E1
already rules the conditional (shingles ⇒ §8 rate + real-item
consumption); the remaining call is which roof the future design
gets. It is tied to authoring that design, so it is recorded here,
not decided now. No owner decision is needed for the current three
designs: their log roofs are the authored scope.

## Stale-doc note (follow-up, not fixed here)

The `ProductionDesignPresets` class doc still says "lumber and
hardware are the only materials the production price port can price
... (LumberYardConstructionPricePort returns -1 for masonry, paint,
glass, roofing and anything else)". F1 added glass pricing, so the
"glass" half of that sentence is stale. This lane could not touch
that file (hard constraint), so the one-line doc correction is left
for a later lane.

## Tests (new, 3)

`Tests/Editor/Economy/RoofingScopeF2Tests.cs` (+ fresh .meta GUID
`029ab82ba75641c8b9e572cf0ce6fbf4`):

1. `PresetDesigns_RoofPhase_HasNoRoofingRequirement` — no phase in
   any of the three preset designs carries a Roofing-kind
   requirement (the log-roof scope, in F2's own terms; Def011 pins
   the exact lists).
2. `ShingledDesign_EvaluateCommission_RefusesOnUnpricedRoofing` —
   a shingled settler variant (settler + a "wood shingles" Roofing
   requirement in the roof phase) refuses at `EvaluateCommission`
   with "material 'wood shingles' has no price — cost unknowable,
   refused", while the plain settler evaluates viable with the same
   lumber/hardware prices (control). The shingle count (1500) is
   marked illustrative in the test — no authored takeoff exists,
   and the refusal holds for any positive count.
3. `RoofingRequirement_DiscoverSupplier_RefusesNoPhantomSupplier` —
   `CategoryKeyFor(Roofing)` is "construction-roofing", and
   `DiscoverSupplier` against an empty directory returns null with
   "no real supplier found".

One test-authoring correction during the lane: the first run failed
the refusal test because the assertion looked for "roofing" in the
refusal text, but the refusal names the material by display name
("wood shingles"), not by kind. The behaviour was correct; the
assertion was fixed to match the real refusal text. No production
code changed.

## Verification

- Base: integration `2c027016` (B3 merge), sparse worktree
  `/home/hatch/ll-ubc-f2`, harness `/home/hatch/ll-ubc-f2-harness`.
- Baseline run0 (`/home/hatch/ll-ubc-f2-run0.log`): **5232 passed /
  0 failed / 5232 total**, build exit 0, 1194 includes.
- Post-change run1 (`/home/hatch/ll-ubc-f2-run1.log`): 5234 passed /
  1 failed — the 1 was the test-authoring correction above (wrong
  assertion string, behaviour correct).
- Post-fix run2 (`/home/hatch/ll-ubc-f2-run2.log`): **5235 passed /
  0 failed / 5235 total**, build exit 0, 0 FAIL lines, all 3 new
  tests pass. FAIL sets `/home/hatch/ll-ubc-f2-fail0.txt` and
  `fail1.txt` are both empty and identical (delta zero).
- No production code changed; no money pins, labour values,
  hardware requirements, or drivers touched.

## Codex Unity checklist

Nothing harness-excluded in this lane: no production file was
changed, so there is nothing for Codex to verify in Unity from F2.
The three new tests are harness-compiled source tests only, as
always — never Unity PlayMode evidence.

## Follow-ups

- F2-1 (owner, future): the storefront roof decision above.
- F2-2 (later lane): the one-line `ProductionDesignPresets` class-doc
  correction (glass is now priced; the "returns -1 for ... glass ..."
  sentence is stale).
- F2-3 (future shingled design): the four-step spec under "What a
  future shingled design would need", including a sourced shingle
  PRICE (the evidence sources a labour rate, not a price).
