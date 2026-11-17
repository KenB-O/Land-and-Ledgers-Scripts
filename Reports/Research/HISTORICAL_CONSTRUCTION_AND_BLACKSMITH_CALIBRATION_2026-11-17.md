# Historical Construction and Blacksmith Calibration — 2026-11-17

**Workstream E, lane E1 — research report. Source-only; no production code changed by this package.**
**Base:** `muse/universal-business-construction-2026-11-17` at `a1c20ad9`. **Status:** NOT UNITY VERIFIED. All
values marked PROVISIONAL are proposed calibrations for the owner and the implementation lanes to correct, not
Canon locks. This report does not change any production design; it gives the evidence and the numbers the
implementation lanes should build from.

## 0. How to read this report

**Evidence classes** (from the construction reference, Part I §1.3): A — direct dated record; B — strong
regional history; C — comparable period practice; D — synthesis range (calibration guidance only).
Every number below carries its class. A range beats fake precision where crew size or scope is unknown.

**Four clocks** (Part I §1.2): active work (crews actually working), elapsed project time (first work to
usable completion, idle included), lead/queue time (waiting for materials, transport, specialists, finance),
interruption time (weather, breakdown, dispute, rework). Never convert one clock into another.

**Game-time conversion (verified in code, not assumed).** `BuildingDesignPhase.LaborMinutes` is **total
person-minutes**, not elapsed minutes: `ConstructionProjectExecutor.WorkCommittedLabor`
(`Domains/Economy/Businesses/Construction/NpcHomeConstruction.cs`) adds each worker's task minutes into
`WorkPackage.LaborMinutesCommitted`, and `AdvancePackage` completes a package only when committed minutes
reach the required total. One worker-day is 600 work-minutes
(`WorkTimeBudget.DefaultDailyWorkMinutes`, `Core/Time/WorkTimeBudget.cs:78`, a 10-hour day). Crew size
therefore changes elapsed time, never the labour total. All person-hour values below are total
person-minutes ÷ 60; elapsed working days are stated at an explicit crew size.

**PROVISIONAL** marks every numerical calibration this report proposes. The historical envelopes are quoted
evidence; the per-design person-minute proposals are this report's synthesis from those envelopes.

## 1. Sources used

| # | Source | Version / date | Pages cited | Class | Covers |
|---|--------|---------------|-------------|-------|--------|
| S1 | `03_CURRENT_SPECIALIST_REFERENCES/06_Historical_Construction_and_Infrastructure.pdf` | Compatibility edition, rev. 5 Oct 2026; research body preserved verbatim | 18 pp. (footer pp. 1–18) | A–D by anchor | Building, barn, roofing, rail, earthwork envelopes; anchor register |
| S2 | `03_CURRENT_SPECIALIST_REFERENCES/ORIGINAL_RESEARCH_SOURCES/Original_Construction_Source_2026-10-05.pdf` | Original research body, 5 Oct 2026 | 18 pp. | A–D by anchor | Same body as S1 (verified by reading both: one source, two copies) |
| S3 | `03_CURRENT_SPECIALIST_REFERENCES/05_Occupations_Demography_and_Workforce.pdf` | 25 Sep 2026, 19 pp. | PDF p. 4 | — | Blacksmith / Farrier occupation profile (tier 1); smithing and farriery as separate capabilities in one profile |
| S4 | `03_CURRENT_SPECIALIST_REFERENCES/07_Historical_Economy_and_Prices.md` | Current in-repo edition | HOLD-001, HOLD-008 | — | Explicit gaps: no wage series, no forge input yields (see §11) |
| S5 | Harold B. Gill, *The Blacksmith in Eighteenth-Century Williamsburg*, ed. Thomas K. Ford (Colonial Williamsburg, 1971/1978), Project Gutenberg eBook 58318 | 18th-century Virginia practice | Horseshoes section; nail-making passage | C | Bar-iron custom shoes; farrier as a distinct trade; nail output anecdote |
| S6 | Powerhouse Collection object 214354, "Miniature horseshoes and blacksmithing tools by Frederick Ivory" (Sydney, made before 1938; curator note July 2015) | Australian practice, pre-1938 | Object record, full text fetched 2026-11-17 | C | Skilled shoe-forging rate; fitting sequence; shoe weights and sizes; ready-made shoes |
| S7 | W&M ScholarWorks thesis, *A Shop in the Back Street: Late Eighteenth Century Williamsburg Through the Ledgers of Blacksmith James Anderson* (via search excerpts) | Anderson ledgers B (1778–1784) and C (1785–1798) | Ch. V (Blacksmith and Farrier) | C | 6 nails per horseshoe, 4 per pony shoe (Watson 1968); re-shoeing about every 3 months |

**Documentation gap (recorded, not worked around).** In-repo documentation is v3.1; the campaign brief
directs v3.2, which is F-drive-only and was not available to this lane. The construction reference S1/S2
itself cites Canon XXVIII and Technical XI on its cover while current governing authority is Canon XXXI and
Technical XIV; S1's compatibility cover states those citations show historical derivation, not the newest
governing versions. Nothing in this report amends Canon or Technical authority.

**Source-access note.** Project Gutenberg was down for maintenance on 2026-11-17, so the full text of S5
could not be fetched directly. The Gill passages quoted in §9 are verbatim excerpts returned by search
against the Gutenberg text and its mirrors (dev.gutenberg.org, mirrorservice.org, readingroo.ms, archive.org).
S6 was fetched in full from collection.powerhouse.com.au.

## 2. Historical period and geographic applicability

**Period:** 1870–1905, Dakota Territory / South Dakota / North Dakota, for the construction envelopes (S1).
Pre-1870 precedents are used only where labelled (telegraph 1861, class U1). The blacksmith material is
comparable-period practice, not Dakota-direct: S5 is 18th-century Virginia (class C), S6 is Australian
practice before 1938 (class C), S7 is late-18th-century Virginia ledger evidence (class C). No Dakota
1870–1905 blacksmith shop record was available in-repo; the horseshoe calibrations below are therefore
class C/D and carry lower confidence than the construction envelopes. Do not present them as Dakota
constants.

**Geography:** the construction envelopes assume Dakota prairie / frontier-town conditions: timber hauled or
milled locally, frontier labour markets, severe winters, no rail-served industrial supply unless stated.
The storefront and dwelling anchors are De Smet and Sioux Falls, South Dakota. Rates from industrial
railroad or urban contexts (R2, R5, R9) are upper bands only.

## 3. Current production designs as built

From `Domains/Economy/Businesses/Construction/ProductionDesignPresets.cs` (D-5, DEF-011 lineage).
All three designs are lumber-only: the production price port returns −1 for masonry, paint, glass, roofing
and anything else, so a design naming any other material refuses at evaluation. Labour is the only other
input; the only tool named is "saw".

| Design | Phases (labour min) | Total labour | Total (person-hours) | Worker-days (600 min) | Lumber (board ft) | Sleeping |
|--------|--------------------|--------------|----------------------|-----------------------|-------------------|----------|
| Settler cabin | Sills 120, Walls 240, Roof 240 | 600 min | 10 | 1.0 | 640 | 4 (one room) |
| Family cabin | Sills 180, Walls 360, Roof 360 | 900 min | 15 | 1.5 | 960 | 6 (two rooms) |
| Carpenter's workshop | Sills 240, Walls 480, Roof 420 | 1,140 min | 19 | 1.9 | 1,180 | 2 (quarters; shop floor 0) |

Design scope as authored: the settler cabin is a one-room log-wall, log-roof shelter on grade-laid sills
(verbatim the DEF-011 production design); the family cabin is ~1.5× the settler cabin in lumber and
labour, a two-room cabin; the workshop is a frontier workshop with living quarters (shopkeeper lives on
the premises, as period shops did). No design includes a cellar, chimney, floor, partitions, finish,
doors, windows, nails, hardware or glass as separate requirements. That scope matters: the historical
analogues in §5–§6 were chosen to match this scope, not a finished house.

## 4. Historical envelopes (S1, Part II table and Parts III–VI)

| Project / task | Scope assumption | Active rate | Elapsed range | Anchors | Confidence |
|---|---|---|---|---|---|
| Very small claim shanty / rough cabin | ~80–250 sq ft, simple frame/board shelter | 2–10 working days | 2 days to 2 weeks | B1, B2 | Medium |
| Simple small frame house | ~3-room frontier house | 15–35 working days | 3–6 weeks | B3 | Medium-high |
| Two-story frame storefront / store-dwelling | Modest Main Street wood frame, no major masonry | — | 8–14 weeks | B4 | Medium |
| Substantial finished dwelling | Contractor-supplied labour/materials, multiple finish stages | — | 16–24 weeks | B5 | High for elapsed order |
| 40 × 60 plank-framed barn | 5-person crew, material staged | 8–15 working days | 2–4 weeks | B6 | High |
| Small brick institutional / commercial | Brick walls, finished interior, modest tower/detail | — | 14–24 weeks | B7 | Medium-high |
| Funding-constrained small church | 44 × 30 ft example | — | ~9 months observed | B8 | High for example |
| Wood-shingle roofing, straight work | Capable worker, 10-hour day | 1,800–2,500 shingles/worker-day | task rate | C1, C2 | Medium-high |
| Manual earth loading | Average labourer, 10-hour day | 15–25 yd³/man-day | task rate | R6 | High |
| Wheel-scraper earth haul | 200–300 ft haul | 45–50 yd³/team-day | task rate | R7 | High |

S1's own warning (Part II, footer p. 3): a two-story storefront at 8–14 weeks and a crude claim shanty
at days "should not share one building-time formula". The worked examples in Part XII–XIII repeat the
point: a one-mile railway segment can require weeks of calendar work even though the visible rail-laying
gang places the rail in roughly one day (footer p. 14). The same four-clock discipline applies to houses.

## 5. The two anchor cases, with scope differences made explicit

### 5.1 The 1882 South Dakota claim shanty — about two days (B1, B2; S1 Part III §3.1, footer p. 4)

A South Dakota pioneer recollection ("South Dakota – 1882 / Dakota Days", OSU eHistory) describes
building a claim shanty in early May 1882 and states it required about two days of work. Museum sources
confirm how small these shelters were: the Mydland claim shanty was 8 × 10 ft and another 1882 claim-house
example was 16 × 10 ft (S1 §3.1, citing South Dakota State University Agricultural Heritage Museum and
the W. H. Over Museum). S1's calibration case for a bare claim shanty (8 × 10 to ~12 × 12 ft) is 2–5
active working days, assuming materials at site, one or two capable builders/helpers, minimal floor/finish,
no cellar and no substantial masonry. Its worked example (Part XII §12.1, footer p. 13) is a 10 × 14 ft
rough frame/board claim shelter with one door and one small window: one capable builder plus one helper,
lumber/nails/roofing already delivered, 3–6 active days and 3–10 calendar days in favourable weather —
explicitly excluding cellar, chimney, plaster, insulation, interior partitions and furniture unless the
authored building requires them.

**What the two-day case is not evidence for.** S1 states it directly (footer p. 4): a two-day claim
shanty is evidence that minimal shelter can be very fast. It is not evidence that a finished year-round
cottage with floor, chimney, partitions, insulation and proper windows should also take two days.

### 5.2 The 1889 North Dakota frame house — twenty-two days (B3; S1 Part III §3.2, footer p. 4)

A first-person account ("Fifteen Years in North Dakota, 1883–1898", CARLI / North Park University) reports
that after a cellar was dug the household hired a carpenter to help, and states: "Our house was built in
twenty-two days" — a simple frame house with one large room and two smaller rooms. S1's synthesis for a
simple small frame house with materials and financing already available is a 15–35 working-day active
envelope and roughly 3–6 weeks elapsed.

**Scope differences between the two cases (do not collapse them).**

| | 1882 shanty (B1) | 1889 house (B3) |
|---|---|---|
| Structure | 8 × 10 to 16 × 10 ft board shelter | ~3-room frame house (one large room, two smaller rooms) |
| Cellar | None | Dug separately, before the 22 days began |
| Crew | One or two builders/helpers | Household plus a hired carpenter |
| Finish | Minimal floor/finish, one door, one small window | Simple frame-house finish (scope not itemised in the account) |
| Weather | Early May, no interference recorded | Spring work; the same account separately describes a 16 × 24 ft, 10-ft-studded house inconvenienced by cold, wet spring weather |
| What the number counts | ~2 days of work on the shelter itself | 22 days of house-raising after the cellar, with hired help |

Converted to the game's person-minute unit at the stated crews: the shanty is roughly 1,200–2,400
person-minutes (2 days × 1–2 workers × 600); the house is at least 26,400 person-minutes (22 days × 2
workers × 600, cellar excluded) and more if the household supplied more hands. The factor between them
is an order of magnitude, and it is scope, not speed.

### 5.3 The crew anchor — 40 × 60 plank barn (B6; S1 Part V §5.1, footer p. 6)

The South Dakota State Historic Preservation Office farm-barn study records general agreement among barn
experts that a journeyman carpenter with a crew of four men could raise a 40 × 60 ft plank-framed barn in
about one week, with roughly another week for finishing: 8–15 working days active, 2–4 weeks elapsed once
site and materials are ready. In person-minutes at the stated 5-person crew: 24,000–45,000 (400–750
person-hours). This is the strongest crew-form anchor in the reference because it gives structure size,
crew composition and staging together. It also fixes the crew-composition rule used in §7: one skilled
lead directing helpers is the productive frontier form, not five interchangeable labourers.

## 6. Per-design evaluation and proposed recalibration

All proposals are PROVISIONAL, ordinary preset (competent ordinary operation, materials staged, no funding
interruption, favourable-to-ordinary weather). Fast/slow presets should sit near the ends of the stated
ranges, not at record rates (S1 Part XIV §14.4). Elapsed working days below count active work only;
lead/queue and interruption time add on top through the real game systems. Do not hardcode any calendar
range as an override for real work progress: the design value is total person-minutes, and elapsed time
must emerge from crew size, daily budgets, dependencies and interruptions.

### 6.1 Settler cabin — current 600 person-minutes (10 person-hours)

**Closest analogue:** small rough cabin / S1 worked example §12.1 (10 × 14 ft, 1 builder + 1 helper,
3–6 active days). The design sleeps 4 in one room, so it sits at the upper end of the rough-cabin class,
above the bare 8 × 10 shanty and below the 3-room frame house.

**Verdict:** the current value is about one-ninth of the historical envelope. 600 person-minutes is a
single worker-day for a one-room log shelter with walls and roof; the worked example alone requires
3–6 days for two workers (3,600–7,200 person-minutes) on a smaller shelter.

**Proposed recalibration (PROVISIONAL, confidence medium):**

| | Person-minutes | Person-hours | Elapsed active at 2 workers |
|---|---|---|---|
| Low end | 3,600 | 60 | 3.0 working days |
| **Ordinary (recommended design value)** | **5,400** | **90** | **4.5 working days** |
| High end | 7,200 | 120 | 6.0 working days |

Recommended phase allocation (H-lane may fold into the existing three phases; totals must be preserved):
site preparation 600, sills 600, walls 2,100, roof 1,500, openings (door + window) 600 — total 5,400.
The current design has no site-preparation or openings phase; a door and a window are real work and real
materials, and the allocation makes them visible rather than free.

### 6.2 Family cabin — current 900 person-minutes (15 person-hours)

**Closest analogue:** none direct. The design is a two-room log cabin sleeping 6, ~1.5× the settler cabin
in lumber. It sits between the rough cabin (4–10 active days) and the simple 3-room frame house (15–35
active days, B3). It has no cellar and less finish than the B3 house, so it must sit below the frame
house, but a second room adds walls, roof area, a door and finish well beyond a 1.5× labour ratio — the
current design's 1.5× labour scaling understates the second room.

**Verdict:** the current value is about one-sixteenth of the historical envelope. 900 person-minutes is
1.5 worker-days for a two-room dwelling; the B3 house alone is at least 26,400 person-minutes with a
hired carpenter.

**Proposed recalibration (PROVISIONAL, confidence medium-low):**

| | Person-minutes | Person-hours | Elapsed active at 2 workers |
|---|---|---|---|
| Low end | 9,600 | 160 | 8.0 working days |
| **Ordinary (recommended design value)** | **14,400** | **240** | **12.0 working days** |
| High end | 19,200 | 320 | 16.0 working days |

Recommended phase allocation: site preparation 1,200, sills 1,200, walls 5,400, roof 4,200, openings
(doors + windows) 1,200, interior partition and finish 1,200 — total 14,400. Remaining unknown: the
design does not state floor area, wall height or finish level; a firmer value needs those dimensions
authored. If the family cabin is later defined as a 16 × 24 ft two-room house, the ordinary value should
move toward the high end.

### 6.3 Carpenter's workshop — current 1,140 person-minutes (19 person-hours)

**Closest analogue:** none direct. The design is a combined commercial/residential structure (workshop
floor sleeping 0, living quarters sleeping 2), 1,180 board feet of lumber. The nearest anchors are the
simple frame house (15–35 active days, B3) below and the modest two-story frame storefront (8–14 weeks
elapsed, B4) above. A single-storey frontier workshop is materially smaller than the De Smet two-storey
store (May–July 20, 1880, ~80 calendar days, ~11.4 weeks) but larger and more heavily framed than a
family cabin, with workbench/fixture space and living quarters.

**Verdict:** the current value is about one twenty-fifth of the historical envelope. 1,140 person-minutes
is under two worker-days for a combined shop and dwelling; the B4 storefront alone took roughly eleven
weeks elapsed.

**Proposed recalibration (PROVISIONAL, confidence low-medium):**

| | Person-minutes | Person-hours | Elapsed active |
|---|---|---|---|
| Low end | 21,600 | 360 | 18.0 working days at 2 workers |
| **Ordinary (recommended design value)** | **28,800** | **480** | **24.0 working days at 2 workers / 16.0 at 3 workers** |
| High end | 36,000 | 600 | 30.0 working days at 2 workers |

Recommended phase allocation: site preparation 2,400, sills/foundation 3,600, walls 9,600, roof 7,200,
openings (doors + windows) 2,400, interior fit-out (workbench framing, quarters finish) 3,600 — total
28,800. Remaining unknowns: the design does not state storeys, footprint, or whether the quarters are
under the same roof; a two-storey workshop should move toward the storefront envelope (8–14 weeks
elapsed). The business operation itself (equipment installation, trade from the building) is out of scope
for the building design, as the current file already notes — the labour above is the building only.

### 6.4 Summary table (all PROVISIONAL)

| Design | Current | Proposed ordinary | Factor | Proposed range | Elapsed active, ordinary crew |
|---|---|---|---|---|---|
| Settler cabin | 600 min (10 ph) | **5,400 min (90 ph)** | 9.0× | 3,600–7,200 min | 4.5 days at 2 workers |
| Family cabin | 900 min (15 ph) | **14,400 min (240 ph)** | 16.0× | 9,600–19,200 min | 12 days at 2 workers |
| Carpenter's workshop | 1,140 min (19 ph) | **28,800 min (480 ph)** | 25.3× | 21,600–36,000 min | 24 days at 2 workers / 16 at 3 |

## 7. Crew composition, work fronts and diminishing returns

S1 Part VI §6.2 (footer p. 7): skilled and helper labour are not interchangeable. The barn anchor uses
one journeyman directing four men; helpers gain output by handling staging, lifting and repetitive work,
but specialist tasks remain limited by skilled capacity, and additional labour eventually meets congestion
and limited work fronts. The implementation rules that follow are already present in the reference (Part
XIV §14.1–§14.2) and should be honoured by the H-lane:

- Crew size helps only while there are independent work fronts and enough tools/material.
- Specialist tasks (framing layout, roofing, glazing, ironwork) are capped by skilled capacity, not by
  headcount.
- Stochastic variation, if used, must be drawn around identifiable causes (quantity uncertainty,
  productivity, material arrival, weather, coordination), never a single hidden ±X% "construction RNG"
  (S1 §14.2).
- Report three times per project (S1 §14.3): planned active work, forecast elapsed completion, actual
  elapsed completion. The current executor already tracks committed vs required labour; forecast elapsed
  is the missing read model.

## 8. Component rates for future work packages

These are not needed for the three current designs (which are lumber-only with log roofs) but bound the
packages workstreams F and H will add. All from S1.

- **Wood shingles** (Part VI §6.1, footer p. 7; C1, C2): ordinary straight work 1,800–2,500 shingles
  per capable worker-day; claims of 3,000–4,000 treated skeptically in the March 1880 *Carpentry and
  Building* discussion and should not be the default; complex roofs (cuts, valleys, poor material) reduce
  the rate materially by geometry, not by a flat modifier.
- **Manual earth loading** (Part IX §9.1, footer p. 10; R6, Webb 1900): 15–25 yd³ per labourer per
  10-hour day.
- **Wheel-scraper haul** (Part IX §9.2; R7, Baker 1903): ~50 yd³ per scraper-day at a 200-ft haul,
  ~45 yd³ per team-day at 300 ft.
- **Funding delay** (Part V §5.3, B8; footer p. 6): a 44 × 30 ft Huron church begun 19 Oct 1881 was not
  completed until 30 Jul 1882 explicitly "for lack of funds" (~284 days, 40.6 weeks). Project duration
  must never be derived from labour-hours alone; financing interruption is a real clock.

## 9. Material-scope gaps in the current designs (for workstreams F, G, H)

Recorded here because the labour calibration assumes the designs' stated scope; the gaps do not change the
labour proposals, but they limit what the designs can honestly claim to build.

- **Lumber unit vs log construction.** The designs name "log walls" and "log roof" but price lumber in
  "board feet" (640/960/1,180). Board feet are a sawn-lumber measure; a log-wall cabin is built of logs.
  Whether one construction lumber unit is a board foot, a log, or a sawn piece is a workstream G audit
  item (unit coherence across designs, freight mass/volume and yard stock). The labour values in §6 assume
  the stated shelter scope, not a resolved lumber unit.
- **No fasteners, hardware, doors or glass.** No design requires nails, hinges, latches, handles, doors
  or window glass. A cabin needs them; the current designs consume lumber only. Workstream F owns the
  hardware/glass supply chains; the phase allocations in §6 include openings labour but the material
  requirements must be added by F, not invented here.
- **Roofing material.** The designs specify a "log roof" with lumber only. If a design later uses wood
  shingles, the §8 shingle rate applies and the roof package must consume shingles as a real item.
- **No site preparation or foundation phase.** All three designs start at sills "laid on grade". Site
  clearing, levelling and any foundation work are currently free. The §6 allocations add site preparation
  as a phase; H-lane should confirm whether "sills on grade" is the intended permanent scope or an
  omission.
- **Tool scope.** The only tool named is "saw". Framing, roofing and finish need hammers, levels,
  squares and (for hardware) forge work; the executor already verifies tool custody, so the designs
  should name what they actually require.

## 10. Horseshoes — forging, fitting, nails and supply

### 10.1 Current code state (verified on disk at a1c20ad9)

- **Forging:** `BlacksmithRuntime.DefaultRecipes()` (`Domains/Economy/Businesses/Blacksmith/
  BlacksmithRuntime.cs:183`) defines `SmithingRecipe("horseshoe-set", "Horseshoe set (4)",
  ImportCatalog.IronStockId, 2, 1, 60, FarrierSkillId)` — 2 iron units + 1 forge-coal unit + 60
  minutes (at skill level 1; `SmithingRecipe.MinutesAtLevel1`, "TTS-1 quantum; skill scales it") to
  produce **one asset** of kind `horseshoe-set`. Other recipes for scale: plow 8 iron / 2 coal / 240
  min; harrow 6 / 2 / 180; wagon ironwork 4 / 1 / 120; hand tool 1 / 1 / 45; plowshare 2 steel / 1 / 90.
- **Fitting:** `FarrierService.ShoeHorse()` (`Domains/Economy/AnimalServices/VetServices.cs`) already
  works per horse and per hoof: it takes `shoesNeeded` (1–4), consumes that many units from a named
  shoe stock with a named source, and charges material + labour per shoe. `ShoeMinutesPerHorse = 45`
  (a flat constant, not skill-scaled) for a four-hoof job.
- **Iron and coal units:** `ImportCatalog` prices iron stock (bars, Pittsburgh ironworks via railhead)
  at 220 miles / 14 transit days / 45c per unit, and forge coal at 180 miles / 12 days / 18c per unit.
  Neither unit carries a physical mass in code; §10.4 proposes an explicit anchor.
- **Skill gate:** `BlacksmithRuntime.JourneymanLevelFloor = 2`; crafting and repair refuse below level 2
  in the required skill. The code comment itself says "Calibration: journeyman-equivalent floor. Tuning,
  not canon."
- **Unit mismatch (finding for the D-lane, not a code change here):** the smith's output is a single
  indivisible `horseshoe-set` asset, while the farrier's input is integer individual shoes
  (`shoesNeeded` 1–4). No code in the tree converts a set asset into four shoe units (verified by
  searching all `horseshoe-set` references: recipe, `EquipmentAsset` kind comment, and opening stock in
  `ScenarioWorldBuilder`). A set can therefore never feed a one-shoe replacement as written. The
  fitting side is already correct in shape; the forging side is the defect.
- **Occupation model (S3, PDF p. 4):** Blacksmith / Farrier is a tier-1 occupation with smithing and
  farriery tracked as separate capabilities within one profile. S3 also records Painter / Glazier as the
  occupation that cuts, fits and repairs window glass — glass installation is glazier work, not smith
  work (relevant to workstream F).

### 10.2 Forging one shoe, a pair, and a set of four — distinguished

**Historical evidence.** S6 (Powerhouse, pre-1938 Australian practice): "Until ready-made horseshoes
became available, blacksmiths made them from bar steel. It was said that a skilled blacksmith could make
four horse shoes in 15 minutes." That is 3.75 minutes per shoe **for a skilled smith, as a reported
saying** — an exceptional skilled-production record (class C), and the brief and S1 both forbid turning it
into universal throughput. S5 (Gill): horseshoes "were made from bar iron, and they were normally custom
made to fit not just a particular horse, but a particular one of his feet. Each shoe of a set of four will
differ in one or more respects — size, shape, or weight — from its fellows." A set of four is therefore
four distinct forgings, not four copies of one item. S6 gives shoe weights: a carriage-horse shoe about
500 g, a draught-horse shoe as much as 2 kg; shoes and nails came in sizes 1 to 14.

**Proposed forging calibration (PROVISIONAL):**

| Item | Ordinary (journeyman) | Level 1 (current recipe basis) | Skilled exceptional record |
|---|---|---|---|
| Forging one shoe | 8–15 min | 15 min | ~4 min (S6 record, not a default) |
| Forging a pair | 16–30 min | 30 min | ~8 min |
| Forging four shoes (one forge session) | 32–60 min | **60 min (current recipe value — retain)** | 15 min (S6 record) |

The current 60-minute session time is defensible as a **level-1** rate and should be retained; the
defect is the output unit, not the clock. Skill scaling (already the recipe's `MinutesAtLevel1` design)
should move a journeyman toward the 32–40 minute session and a master toward the record only as an
upper band, never as the default.

**Recommended structural change (for the D-lane):** one forge session (60 min at level 1, skill-scaled)
consumes 2 iron + 1 coal and yields **four individual horseshoe items**, not one set asset. The session
is the batch; the output is discrete shoes. This keeps the existing material and time quantities, removes
the set/individual mismatch, and lets the farrier consume 1–4 shoes for real replacements.

### 10.3 Fitting one shoe and fitting four shoes — distinguished from forging

**Historical evidence.** S6 describes the fitting sequence: remove the old shoe, trim and file the hoof,
heat the new shoe until red hot, hammer it into shape, place it on the hoof hot to burn a mark as a
reshaping guide, reheat and reshape repeatedly until it fits, nail it on, trim and file with a rasp.
"Fitting horseshoes was highly skilled work. Horse shoe nails inserted too close to the outside of the
hoof could split it, while nails too near the sensitive part could hurt or incur lameness." S5: "A smith
who made, fitted, and applied shoes to horses, mules, and oxen was properly called a farrier. The trade
demands knowledge and skill in handling iron, and also knowledge and skill in handling the animals being
shod. More often, however, it was the blacksmith who also served as farrier."

**Proposed fitting calibration (PROVISIONAL):** 10–15 minutes per hoof for an ordinary farrier,
40–60 minutes for a four-hoof horse. The current `ShoeMinutesPerHorse = 45` sits inside that band and
should be **retained as the ordinary rate**, with two corrections for the D-lane: (a) make it skill-scaled
like the forging minutes instead of a flat constant; (b) charge per hoof actually shod, which the
`shoesNeeded` parameter already supports. **Manufacturing duration must never be equated with the
complete shoeing service:** a full job from bar iron to a shod horse is the forge session (32–60 min
ordinary) **plus** the fitting (40–60 min ordinary), plus hoof trimming and nails — roughly 75–105
minutes of skilled labour plus materials for a four-hoof shoeing, not the 60-minute recipe alone.

### 10.4 Iron and coal per shoe — the fractional-unit problem, resolved

**The problem.** The current recipe spends 2 iron units and 1 coal unit per four shoes: 0.5 iron and 0.25
coal per shoe. The inventory model uses integer units, so a per-shoe recipe cannot spend half a unit
without fractional accounting. The campaign brief anticipates exactly this and permits either a more
precise authoritative unit or a narrow compatible quantity representation.

**Proposed anchors (PROVISIONAL, for the owner to correct).** One iron-stock unit = 5 lb of bar iron;
one forge-coal unit = 10 lb of coal. The iron anchor is consistent across the existing recipes: a plow at
8 units ≈ 40 lb of iron, a hand tool at 1 unit ≈ 5 lb, a horseshoe session at 2 units ≈ 10 lb of bar.
Finished shoe weights (S6) run ~1.1 lb (carriage, 500 g) to ~4.4 lb (draught, 2 kg); with scale loss
and trimming, bar needed per shoe is roughly 1.5 lb (light) to 5.5 lb (heavy). A four-shoe session at
2 iron units (10 lb bar) therefore covers light/medium shoes and is light for a heavy draught set, which
should cost 3–4 iron units. One coal unit (10 lb) per four-shoe session covers repeated heats at an
ordinary rate.

**Recommendation.** Keep the session as the costing unit (2 iron + 1 coal per four shoes) and yield four
individual shoes, as in §10.2. The per-shoe fraction then never enters inventory accounting. If the
D-lane instead wants a per-shoe recipe, it must first re-anchor the iron unit finer (for example 1 unit
= 1 lb bar) and re-scale every smithing recipe consistently — a larger change that should not be done
silently inside the horseshoe work. **Do not** simply divide the recipe's inputs by four: 0.5 iron and
0.25 coal per shoe are fractional bugs, and dividing the 60 minutes by four while keeping four shoes'
worth of input cost (which the brief explicitly forbids) would overcharge every shoe.

**Material cost check (current prices):** 2 × 45c iron + 1 × 18c coal = 108c per four-shoe session,
27c of materials per shoe, before labour. No shoe sale price exists in code; the farrier's
`pricePerShoeCents` is a caller parameter. Pricing is a D/J-lane matter, not a calibration lock here.

### 10.5 Horseshoe nails and fasteners

**Historical evidence.** S7 (Anderson thesis, citing Watson 1968): six nails were needed for each
horseshoe and four for each pony shoe. S5 (Gill): "In addition to the horseshoes, the blacksmith also
forged the horseshoe nails, which were made from a soft metal and ended in a sharp point"; and "James
Anderson estimated that eight boys could turn out twenty-five thousand nails in a week." The nail figure
is specialised child nailer labour (about 3,100 nails per boy-week) — an exceptional volume for dedicated
nailers, **not** a blacksmith's rate, and must not be used as one.

**Current state and recommendation.** No horseshoe-nail item is consumed by fitting today, although a
`nails-keg` already exists as a blacksmith opening-stock item (`ScenarioWorldBuilder`). A full shoeing
consumes 24 nails (6 × 4); a single replacement consumes 6. Recommendation for the D/F lanes: make
horseshoe nails a real consumable in the fitting (6 per shoe, 4 per pony shoe), supplied from a nails-keg
stock. **Nail-forging rate is UNKNOWN in the available sources** (S4 HOLD-008 lists iron-to-nails
throughput as an explicit research hold) — do not invent one. The honest primary supply is manufactured
cut nails by import: cut nails were an established factory product well before 1870, and a frontier
smith buying kegs is historically ordinary. Smith-forged nails remain a later option once a rate is
sourced. Do not create a nail recipe with an invented throughput to make a test pass.

### 10.6 Ready-made or imported shoes

S6 states ready-made horseshoes eventually became available, without a date and in an Australian context.
Manufactured horseshoes were produced in the United States from the mid-19th century, but **no source in
the available set dates ready-made availability for Dakota in 1870**, so this report does not lock it.
Recommendation: custom-forged shoes are the default; a ready-made imported shoe is a legitimate premium
import option only if the D/J lanes source an availability date and a real supplier route (the existing
import catalogue already supports named off-map origins with distance, transit days and price). Do not mint
free or placeless ready-made shoes.

### 10.7 Which horses need shoes, and how often

- **Not every horse must always wear four shoes.** S5 (Gill, quoting Hugh Jones 1724): horseshoes were
  "seldom used in the lower part of the country, where there are few stones", because sandy soil let
  horses go unshod much of the time. Need depends on surface, use and hoof condition — the farrier
  service should shoe horses that need it (working on hard/rocky surfaces, heavy draught), not every
  horse automatically. A lost or worn shoe should have meaningful consequences where the animal and
  equipment-condition authorities support it (workstream D/J decision, not a calibration value).
- **Re-shoeing interval:** about every three months (S7, Watson 1968), the time a hoof takes to grow
  out enough to need trimming again. That implies roughly 16 shoes per horse per year of demand — the
  demand rate the blacksmith economy should be sized against.
- **Skill gate:** the current level-2 refusal floor is tuning, not canon (the code says so). The campaign
  brief directs that ordinary physically feasible work be attemptable without an arbitrary occupational
  permission gate, with skill changing quality, failure probability, speed, injury risk or access to truly
  specialised work. Recommendation: allow ordinary shoeing attempts below level 2 with quality/failure
  consequences, and reserve hard refusal for corrective or specialist shoeing. The D-lane owns the change;
  this report records the calibration basis only.

## 11. Source gaps and open research questions

1. **Wage series (S4 HOLD-001).** No daily cash wage by trade/season for Dakota 1870–1905 is in the
   available sources. The carpenter wage remains unruled (as the campaign brief already records); this
   report proposes labour *quantities* only and takes no position on wage rates.
2. **Forge input yields (S4 HOLD-008).** Log-to-lumber and iron-to-nails throughput are explicit holds.
   The nail-forging rate is therefore unknown (§10.5); the iron anchor in §10.4 is a documented
   provisional, not a sourced yield.
3. **Two-room log cabin anchor.** No direct historical anchor for a two-room log cabin was found; the
   family-cabin value is interpolated between the rough cabin and the 3-room frame house (confidence
   medium-low). Authoring the cabin's floor area, wall height and finish level would firm it up.
4. **Workshop anchor.** No direct historical anchor for a frontier carpenter's workshop was found; the
   value is interpolated between the frame house and the storefront (confidence low-medium). Storeys and
   footprint would firm it up.
5. **Two-storey storefront labour-days by trade.** S1 Part XVII §17.2: the elapsed regional example
   exists, but crew size and detailed quantities remain incomplete. The 8–14 week envelope is a
   synthesis, not a decomposed labour total.
6. **Ready-made shoe availability date for Dakota 1870.** Open (§10.6).
7. **Masonry, trenching, macadam and winter-productivity rates.** S1 Part XVII §17.2 lists these as
   gaps; this edition does not claim unit rates for them. Do not invent them in later lanes.
8. **Documentation version.** v3.2 is F-drive-only (§1). If v3.2 revises any envelope used here, this
   report should be re-checked against it.

## 12. What this report does not decide

- No production design values are changed by this package (research only).
- No owner decision is made here: carpenter wage, hay supply, F-D11-2 shell sleeping capacity, glass
  supplier, nail supply route and the iron-unit anchor all remain owner-correctable; the PROVISIONAL
  marks say so on each value.
- No Unity verification is claimed. The harness run below validates C# simulation logic only.
- The historical envelopes are not Canon locks and do not supersede Canon XXXI or Technical XIV (S1
  status statement, footer p. 1).

## Appendix A — Anchor register (page numbers are the S1 footer pagination, 18 pp.)

| Anchor | Source | S1 location |
|---|---|---|
| B1 — 1882 claim shanty, ~2 days | OSU eHistory, "South Dakota – 1882 / Dakota Days" | Part III §3.1, footer p. 4; register footer p. 16 |
| B2 — Mydland 8 × 10 ft shanty; 1882 16 × 10 ft example | SDSU Agricultural Heritage Museum; W. H. Over Museum | Part III §3.1, footer p. 4; register footer p. 16 |
| B3 — 1889 North Dakota house, 22 days after cellar | "Fifteen Years in North Dakota, 1883–1898" (CARLI/North Park) | Part III §3.2, footer p. 4; register footer p. 16 |
| B4 — De Smet two-storey store, May–20 Jul 1880 (~11.4 weeks) | Kurz / Cleaveland, "Pa Ingalls' Store" | Part IV §4.1, footer p. 5; register footer p. 16 |
| B5 — Sioux Falls dwelling contract, 3 Apr 1882, due 1 Sep, $4,770 | McCormack v. Phillips (1887) | Part IV §4.2, footer p. 5; register footer p. 16 |
| B6 — 40 × 60 barn, 1 journeyman + 4 men, ~1 week raising + ~1 week finishing | SD State Historic Preservation Office, "Common Farm Barns of South Dakota" | Part V §5.1, footer p. 6; register footer p. 16 |
| B7 — Milbank brick church, May–Sep 1883 | SAH Archipedia | Part V §5.2, footer p. 6; register footer p. 16 |
| B8 — Huron church, 19 Oct 1881–30 Jul 1882, "for lack of funds" | Andreas' 1884 History of Beadle County | Part V §5.3, footer p. 6; register footer p. 16 |
| C1 — Shingling ~2,000/day (quick active man); 2,500/day large straight roof | Carpentry and Building, Mar 1880 | Part VI §6.1, footer p. 7; register footer p. 16 |
| C2 — Shingling ~2,000/day; estimating guidance | Carpentry and Building, Mar 1884 | Part VI §6.1, footer p. 7; register footer p. 16 |
| R6 — Hand loading 15–25 yd³/10-hr day; steam shovel ~3,000 yd³/10 hr | Webb, Railroad Construction (1900) | Part IX §9.1–9.3, footer p. 10; register footer p. 17 |
| R7 — Wheel scraper 45–50 yd³/team-day | Baker, Treatise on Roads and Pavements (1903) | Part IX §9.2, footer p. 10; register footer p. 17 |
| R2/R3 — ~2,500–2,640 ties/mile | US govt Union Pacific report; Vose, Handbook of Railroad Construction | Part VII §7.2, footer p. 8; register footer p. 17 |
| U1 — Telegraph 3–8 mi/day (1861, pre-window precedent) | US National Park Service | Part XI §11.2, footer p. 12; register footer p. 17 |
| Gill horseshoes (bar iron, custom per foot; farrier distinct trade) | Gill, Blacksmith in 18th-Century Williamsburg (Gutenberg 58318) | Horseshoes section (class C) |
| Ivory skilled rate, fitting sequence, weights, sizes 1–14 | Powerhouse object 214354 | Object record (class C) |
| 6 nails/shoe, 4/pony shoe; re-shoe ~3 months | Anderson thesis, ch. V (Watson 1968) | Ch. V (class C) |

## Appendix B — Verification performed by this package

- Worktree `/home/hatch/ll-ubc-e1`, branch `muse/ubc-e1-construction-calibration-2026-11-17`, HEAD
  `a1c20ad9` (clean at start).
- Harness: own copy of `gen-csproj-ll-ubc.sh` with only the REPO line changed to the E1 worktree;
  shared `/home/hatch/ll-ubc-harness/sources.txt`; csproj 1,157 Compile Include lines (1,149 from the
  E1 worktree, 0 from the base worktree); build 0 errors, 107 warnings; full run 4,934 passed /
  78 failed / 5,012 total, FAIL set byte-identical to the fresh baseline at
  `/home/hatch/ll-ubc-baseline-fails.txt` (zero new failures from this docs-only change).
- Report path: `Reports/Research/HISTORICAL_CONSTRUCTION_AND_BLACKSMITH_CALIBRATION_2026-11-17.md`
  (repo-root Reports, no `.meta` required — sibling report files carry none).
