# Universal Business and Construction Campaign - Final Report

Land and Ledgers - Muse source development - 2026-11-17

Integration branch muse/universal-business-construction-2026-11-17 at d2b92b19

Harness run33 - 5272 passed, 0 failed, 5272 total

Base a1c20ad9 - 4934 passed, 78 failed, 5012 total

All 78 baseline failures resolved, plus 338 tests net

30 packages merged, every one watchdog E verified

Source tests only - no Unity compilation, Test Runner, or PlayMode results are claimed anywhere in this report

## 1 - Summary

Kennedy launched this campaign at 10:02 EST on 2026-11-17 from the Codex foundation checkpoint a1c20ad9, with no Unity readiness gate. The mandate was to unify NPC and player business operations on shared economic authorities, preserve business identity and assets and agreements across ownership changes, give NPCs capability parity with autonomous decisions, build blacksmith production and repair and individual horseshoes, recalibrate construction labour from historical references, wire real materials including basic ironwork and selectively imported window glass, connect the disconnected household and commerce and manufacturing and employment integrations, and run a broad A-Z audit with real repairs.

The campaign ran one working day, 10:02 EST to 14:24 EST. Thirty packages merged into the integration branch, each one verified independently by watchdog E before merging. The harness line stayed green at every integration step except four real merge interactions (run9, run18, run25, run29), each fixed test-side or with a small production repair and re-run green. The final state is 5272 passed and 0 failed.

What was built - the acquisition continuity system with business identity preserved, individual horseshoes as the manufacturing and inventory unit, the forestry to sawmill to yard to construction chain with a failed-procurement unwind, smith supply drivers for iron and coal and nails and glass, kind-level yard hardware sales, NPC autonomy with an 8-week close rule, commercial lease authority, all seven derived opening views replaced with direct owner-record iteration, the household chain fix, multi-capability formation, the manufacturing network audit, and the roofing scope review.

What was not built - anything that needed an owner decision first. The campaign recorded those as open questions in section 19 and did not silently decide them. The largest open item is the construction completion gap, where the player weekly queue completes shells by elapsed weeks alone and the shells never register in the housing authority, both blocked on the shell sleeping capacity decision F-D11-2.

Publication - the branch is local-only. The push is blocked on auth, and a verified bundle carries the whole campaign for Codex to pull.

## 2 - Baseline and starting point

The starting point was the Codex foundation branch codex/development-town-foundation-2026-11-17 at a1c20ad9e8e8320cec68fb476616e88daa9457ce, verified from disk at 08:56 EST on 2026-11-17. The foundation pass brought schema 2 DevelopmentTownConfiguration, 14 residents, 8 households, 7 businesses, 18 of 18 focused EditMode tests passed, Unity compile clean, and PlayMode startup inconclusive on a scene-restoration animation error. That PlayMode gap is still open and is not claimed as verified anywhere in this report.

The fresh Roslyn harness baseline from a1c20ad9 read 4934 passed, 78 failed, 5012 total (log ll-ubc-baseline-run1.log, FAIL set ll-ubc-baseline-fails.txt). The FAIL set was 66 schema-2 test-helper NREs, 3 SawmillAuthorshipC8 harness path artifacts, 1 CampaignBFuelConversion, and 8 expanded-campaign carry-over assertions. All 78 are resolved in the final state. The effective baseline moved to 4937 passed and 75 failed after S3 fixed the shared harness shim dataPath, which fixed the 3 C8 tests at the shim level.

The development town roster at the foundation pass was 14 residents in 8 households. Kennedy Baldwin-Ooms 35, General Store owner, with Emma Baldwin-Ooms 32. Glen Pierce 31, bakery owner, with Tannis Pierce 29. Jacob Boniecki 34, fuel dealer, with Grace Boniecki 30, lumber-yard owner. Ethan Boniecki 34, the player, freight-company owner with one horse and one wagon, with Tia Boniecki 30. Richard Sithole, person ID 10. Shane Bryan 27, resident, no longer a sawmill employee. Enzo, sawmill owner, with Emma, home ordinal 6. Josh Saunders 26, sawmill labourer, boarding with the Kwints. Don Millward, carpenter, with Grace Millward, home ordinal 7. Grace Boniecki is a different person from Grace Millward, and the NPC Emma is a second Emma alongside Emma Baldwin-Ooms.

One spelling conflict is unresolved and must not be silently settled. The foundation report and the campaign brief write Enzo Kwindt, while the earlier memory and C-8 write Enzo Kwint. Both spellings stay attributed to their sources until Kennedy or a source check resolves it.

Opening cash in the foundation pass was 40,000c per household across 8 households, and business opening cash of General Store 50,000, Bakery 35,000, Fuel 20,000, Freight 20,000, Sawmill 15,000, Yard 12,000, Carpentry 10,000 cents.

Governing authority for the campaign was Canon XXXI above Technical Bible XIV above approved proposals 001 to 105. Proposals 106 to 145 remain pending and were not silently implemented. In-repo docs are v3.1, with v3.2 F-drive-only, and that gap is recorded. Kennedy's real-life rule governed decisions - decide about 95 percent of decisions by what would happen in real life or what actually happened in the past, documenting assumptions for correction, and do not get stuck waiting for him. Day length is 24 real minutes (1440 seconds) at 1x, speeds 100x and 250x, with 25x replaced by 250x. Opening year is scenario-dependent, 1870 for sandbox and open frontier. Generic business is Start Business, then Retail, then an optional General Store template populating only an inventory set, with no permanent business-type locking. QA-15 is ruled Option B, availability wins. Off-map towns authorized are Ashford at 50 miles, where Millbrook Grain Company operates (Millbrook is the company, not the town), and Redfield at 100 miles on a different corridor, broadly opposite the Ashford direction. Timberline Fuel Supply does not exist and was never invented. Five-metre staging and coupling tolerance is approved. Ethan starts with exactly one horse and one wagon.

## 3 - Workstream A - universal business identity and acquisition

### A1 - acquisition continuity - merge f9e43292 - lane commit f52a45bd - E VERIFIED

The audit found the acquired General Store was never bound to the store runtime, so it had no weekly payroll, no reorder, no owner distribution, and no daily-sales resolution, while the NPC restock driver kept spending its cash. The repair binds the acquired store in TryTransferBusinessToPlayer through TryBindFormedPlayerBusiness. Four files, 627 insertions and 1 deletion. Harness 4934/78/5012 in the lane tree, FAIL set byte-identical to baseline. Limitation - the repair sits in SharedBusinessRuntimeManager.cs, which the Roslyn harness does not compile, and its 8 new tests are Unity-only, so the fix needs Codex Unity EditMode. The lane added one exclusion line to the shared harness sources.txt for the new Unity-only test file.

### A2 - acquisition follow-ups - merge cb222ae4 - lane commit 31bdd8ed - E VERIFIED

All four follow-ups from A1. Seller proceeds go through CreditSellerProceeds (price plus pre-close withdrawal to the seller's household ledger as SaleProceeds, no household means a named Payable, no debt authority means fail-loud with cash staying in the business). The former owner is no longer silently inserted as an employee (EnsureOwnerOperatorStaffing call removed, RemoveOwnerOperator clears only the owner-operator slot). Terms are recorded on the player path through RecordPlayerAcquisitionTerms and survive save and load. The to-Town unbind runs through TryUnbindPlayerBusiness. Ten files, 1082 insertions and 28 deletions. Harness 4940/75/5015 in the lane tree, 3 new BusinessSaleTermsTests pass, plus 6 new Unity tests. The real SaveDtos.cs gained 4 fields on the acquisition-deal DTO, and the shared shim carries the BusinessSaleTermsSaveDto mirror, confirmed field-identical by E. Key interpretation for the owner - the motive-based opening target is the seller's agreed pre-close withdrawal, and if the buyer should keep all business cash by default, the target goes away. That interpretation is recorded as an open question in section 19. Adjacent gaps - land and vacant-shell property sales pay the seller nothing (out of scope), and ObligationAuthority bootstrap wiring is a follow-up.

The campaign brief direction for this workstream stands in full - a player purchase of an NPC business must preserve the existing business identity, with business ID unchanged and inventory, cash, employees, equipment condition, liabilities, and applicable agreements preserved, with no deleting or recreating the business, no duplicate cash, inventory, or employees, and no invented replacement contracts.

## 4 - Workstream B - NPC business autonomy and strategy

### B1 - NPC autonomy and 8-week close rule - merge 679a2b7b - lane commit aded62a4 - E VERIFIED

The audit found NPC autonomy mostly wired (weekly lifecycle evaluation, entrepreneurship observation and investigation, formation adapter, NPC weekly payroll and reorder and restock, boarding-house-plus-meals already operating as one business). The top break B1-1 was not repaired - NPC formation is impossible in production, because DriveEntrepreneurshipWeek leaves the premises and equipment finders unbound by design, no commercial lease listings exist (property acquisition is housing-only), and the UsedEquipmentMarketBook is unwired and unpopulated. That became B2. The repair B1-2 - EvaluateWeek never called Pivot or CloseOrderly, so ConsecutiveStruggleWeeks grew without consequence. After 8 consecutive struggle weeks the business now closes through the existing CloseOrderly wind-down (debts settled or assumed, assets liquidated to real buyers, staff released, residual to the founder). The threshold is a public const CloseAfterConsecutiveStruggleWeeks equal to 8, documented PROVISIONAL for Kennedy to correct. Recovery before week 8 resets the count. Pivot stays unrepaired and needs a capability-demand policy (F-B1-2). Four files, 648 insertions. Harness 5053/1/5054 in the lane tree, 4 new tests pass. Consequence flagged for the owner - the weekly driver evaluates ALL NPC-owned businesses including the 7 authored development-town businesses, so a genuinely insolvent authored business would now close after 8 weeks in a live run. That is intended behaviour and needs Unity verification.

### B2 - commercial lease authority - merge 518aa31d - lane commit d651a69a - E VERIFIED

The commercial lease authority for F-B1-1 - lease listings and agreements with real terms (1500c per month rent, 1500c deposit, 12-month term, fitted fixtures, all PROVISIONAL), seeded only from the town's actual unassigned commercial buildings. Monthly rent settlement runs full from real cash, partial plus a real Payable, full arrears, idempotent per period. Formation against a lease pays deposit plus first month (3000c) before creation, signs after, full refund on refusal. Leased fixtures never liquidate. Closed shops release their building and re-list. Thirteen files, 1904 insertions and 8 deletions. Harness 5077/0/5077 in the lane tree, delta exactly the 17 new tests. Honest findings - zero vacant shops at boot (no invented buildings, the market gains listings from closures), landlord is the last rights-holder, never-occupied buildings' listings are recorded but never offered (F-B2-6, needs Kennedy), used-equipment market stays unwired (F-B2-5), formation working-capital gap F-B2-2 recorded (a new business starts with no opening cash, period-1 rent may go to arrears). Limitation - B1's 8-week close was not in the B2 tree, so the positive close to release to re-list path was F-B2-1 and needed B1 merged first. One merge-time interaction followed - run18 failed on the B2 guard test because 7 struggle weeks plus one evaluation hits the B1 close threshold at 8, and the fix (commit 27494fdb, test-only) pins the guard below the threshold at 6 to 7 weeks.

### B3 - close to release to re-list - merge 2c027016 - lane commit 2699d5e1 - E VERIFIED

The F-B2-1 positive path, now possible with both B1's close and B2's lease merged. E confirmed the extraction VERBATIM against B2's original code at 518aa31d (same order, decisions, diagnostic strings, with one wording difference where the handler's missing-market string fires only in a case B2 would have NullReferenceException'd on, a strict improvement). The merge had exactly one conflict, comment-only, in CommercialLeaseMarketTests.cs, resolved to B3's wording as E predicted. Run27 on the merge read 5232/0/5232 (5225 plus 7 B3 tests).

## 5 - Workstream C - retail, services, multi-capability operations

### C1 - multi-capability formation repair - merge d2b92b19 - lane commit 9f2b5a2b - E VERIFIED

The audit found all five compatible-activity pairs wired to the one entity with no competing parallel runtime - store retail plus hardware, boarding plus meals (B1 drivers), bakery plus retail (SellBread plus the generic production driver), carpentry plus furniture sales (workshop driver step d), sawmill plus wholesale lumber (transfer driver, G1 conservation). The repair F-C1-2 - player formation dropped capabilities. CreateBusinessIntent.capabilityIds was a dead field (private, no setter, no ctor param), TryCreate never attached them, ToIntent dropped the form's choices, TryApply left CapabilityIds empty, while only the NPC adapter attached them manually. The repair adds an optional ctor param (deduped, blanks ignored), TryCreate Step 5b attaches the intent's capabilities, ToIntent passes the form's ids through, and TryApply records the installed vertical via AddCapability (bakery_retail_setup, prairie_fork_smithy_setup, carpentry_setup, nothing for Blank or General Merchandise). Recording grants no operation - nothing gates on capabilities at execution. Six files, 513 insertions and 2 deletions. Harness 5268/0/5268 in the lane tree, 7 new tests pass. The expanded-build check showed exactly 2 unique errors, both pre-existing CS0246 OperatingLedgerSaveDto at BusinessCreation.cs lines 704 and 730 in untouched CaptureSaveDto, and zero errors in the edited regions. The top finding F-C1-1 was recorded not repaired - BusinessExpansion.TryAddOperation has zero production callers, the hub registry is never populated (StoreButchery.RegisterAll and GrainChain.RegisterAll never called), HasCapability is never consulted at execution. Populating the registry alone would change no behaviour, so the lane did not do a cosmetic population. The actor for Add Operation is an owner decision in section 19.

## 6 - Workstream D - blacksmiths, horseshoes, equipment

### D1 - individual horseshoes - merge 883273a6 - lane commit 281dc2e2 - E VERIFIED

One shoe is the manufacturing and inventory unit (quarter-pool forge model, whole-unit draws, fractional recipes refused). The journeyman gate was removed for shoe forging only. SkillOutcomeCurves grades outcomes. FitShoes is a separate per-hoof service (1, 2, or 4 hoof orders). No horseshoe set remains in production code. Thirteen files, 1335 insertions and 10 deletions. Harness 4959/78/5037 in the lane tree, 25 new Horseshoe tests pass, FAIL set byte-identical to baseline. The campaign brief direction stands - horseshoes are individual physical items, one horseshoe is the manufacturing and inventory unit, a four-hoof shoeing is a service order, forging is separated from fitting, and the existing four-shoe recipe (two iron units, 60 minutes) must not be rescaled blindly without checking actual quantities and service boundaries.

### D2 - blacksmith construction hardware - merge 9957be0a - lane commit eb1489f9 - E VERIFIED

Built ForgeHardware plus TakeHardware (3 recipes, shared quarter pools with shoes, SkillOutcomeCurves grading, no journeyman gate), a door hardware requirement in each roof phase (settler 9, family 18, workshop 18), a yard SimpleHardware lot at 45c from real cost, and refusal of anonymous smith delivery. Seven files, 1184 insertions and 44 deletions. Harness 4983/75/5058 in the lane tree, 21 new tests pass (19 hardware, 2 end-to-end). Nails have no recipe and import at 30c via Pittsburgh ironworks railhead. Provisional calibrations are documented in section 18. Follow-ups were F-D2-1 no production delivery driver, F-D2-2 nails import driver, F-D2-3 yard hardware sale is bucket-level. Merge order was E2 first then D2, because both edited ProductionDesignPresets.cs and NpcConstructionDesignsDef011Tests.cs. The merge had conflicts in both files (4 hunks and 3 hunks), resolved keeping both the E2 labour pins and the D2 hardware pins. One integration interaction followed - run9 failed on 2 D2 end-to-end tests because the test helper assumed one worker-day completes a package, while under E2 the Sills package needs 1200 labour minutes. Fixed test-side by commit 0d8f1760, with no production code changed.

### D3 - hardware delivery and nails import drivers - merge e2097ab8 - lane commit e721118d - E VERIFIED

F-D2-1 smith-to-yard hardware delivery driver (45c per piece, yard debit before intake, smith credited only on verified delivery, failure restores pieces plus refunds, job stays Planned) and F-D2-2 nails import driver (paid import orders through ImportService.PlaceOrderForBusiness, Pittsburgh ironworks via railhead, 220 miles, 14 days, 30c, covered-stock policy threshold 12 and order lot 60, single-outstanding-order guard, no phantom merchant, no free nails, no local nail industry). Seventeen files, 2852 insertions and 8 deletions. Harness 5001/75/5076 in the lane tree, 18 new tests pass, FAIL set byte-identical to the D2 baseline. Merge-time action - the shared harness sources.txt gained the BlacksmithToYardHardwareTransferDriver.cs line before the post-D3 integration run.

### D4 - iron supply driver - merge a79b483c - lane commit 2c47988f - E VERIFIED

The smith is wired to the registered iron-stock material (Pittsburgh ironworks via railhead, 220 miles, 14 days, 45c per unit) through ImportService.PlaceOrderForBusiness, mirroring the D3 nails driver. Covered-stock rule, single-outstanding-order guard, paid from the smith's own cash or honest credit, no cash and no credit leaves no order. Calibration PROVISIONAL (threshold 8 units, lot 16 units, 720c). Eleven files, 928 insertions and 8 deletions. Harness 5012/75/5087 in the lane tree, 11 new tests pass, FAIL set byte-identical to the D3 baseline. End-to-end proof - an empty smithy cannot forge, iron lands day 14 as a provenance lot, then 4 shoes are forged from one iron unit. E's first independent re-run aborted on the disk quota fault, so a focused E re-run was dispatched and reproduced 5012/75 exactly with all 11 new tests passing, closing that limitation.

### D5 - forge-coal supply driver - merge 3fd8b82b - lane commit 452c153e - E VERIFIED

The smith is wired to the registered forge-coal material (off-map coal dealer via railhead, 180 miles, 12 days, 18c per unit) through ImportService.PlaceOrderForBusiness, mirroring D4. Covered-stock rule, single-outstanding-order guard, cash or honest credit, no cash and no credit leaves no order. Calibration PROVISIONAL (threshold 6 units, lot 12 units, 216c). Twelve files, 1041 insertions and 1 deletion. Harness 5023/75/5098 in the lane tree, 11 new tests pass, FAIL set byte-identical to the D4 baseline. End-to-end proof - a smithy with iron but no coal cannot forge, coal lands day 12 as a provenance lot, then 4 shoes are forged from one iron and one coal. With D5 both forge inputs have legitimate supply drivers and no forge-consumable gap remains. Correction carried - SystemsSaveDtos.cs has 0 includes in the normal csproj, it is harness-excluded and verified only through the expanded build, like the other three wiring files.

### D6 - kind-level yard hardware sale lines - merge e3fbea06 - lane commit f1ca9423 - E VERIFIED

The yard hardware sale is now per-kind at every link. LumberYardHardwareKind gains DoorHinge equal to 2, IronLatch equal to 3, DoorHandle equal to 4 (append-only). One lot holds one kind. Mixed SimpleHardware stays for legacy lots but is not fulfillable. The D3 driver lands one lot per kind with a partial-delivery failure path. The yard sale overload is atomic per kind and refuses loudly naming the short kind. The adapter records one sale line per kind. The requirement gains explicit HardwareKindLines with a shared breakdown helper (explicit lines, else the D2 pair rule 9 equals 3 hinges plus 2 latches plus 1 handle plus 3 nails, non-multiples refuse rather than guess). The site stockpile stages and consumes per kind. Kind lines price at the bucket price, so money is unchanged and the 26,005c end-to-end pin holds. Thirteen files, 1429 insertions and 91 deletions. Harness 5036/75 in the lane tree, 13 new tests pass, FAIL set byte-identical to D5's. ProductionDesignPresets.cs and NpcConstructionDesignsDef011Tests.cs untouched, no new sources.txt lines needed, no changed file is harness-excluded, so D6 adds no Codex Unity checklist items. The merge had exactly 3 conflicts (LumberYardConstructionSaleAdapter.cs, LumberYardShopRuntime.cs, Pass9RegistryAssemblyScanTests.cs). The shop-runtime conflict interleaved two complete methods at one anchor (HEAD VoidSale with F1 GlassLines, D6 ExecuteConstructionSalePerKind), both kept, and a new per-kind-plus-glass TrySellToConstructionProject overload carries glass through the executor (any-bucket shortfall refuses the whole sale, the F1 three-bucket rule). Two merge-fix commits followed - 1fbd645a restores VoidSale's closing brace, which the splice had dropped (brace count 198 over 197 before, 198 over 198 after, no logic changed), and badf9a23 fixes a genuine production gap found by run29 - the G1 unwind test sells a nails-only requirement (20 nails, display name nails), and D6's breakdown helper only knew explicit kind lines or whole door-plus-window pairs, so it refused (20 is not a multiple of 9). TryBreakdown now resolves a requirement whose own display name is a known kind name (nails, door-hinge, iron-latch, door-handle) to a single kind line for its full total, before the pair rule. The bucket name (door hardware) is not a kind name, so an ambiguous bucket total still refuses on a non-multiple.

## 7 - Workstream E - historical construction calibration

### E1 - construction calibration research - commit 6b762825 - E VERIFIED

One file, 537 insertions - the report at Reports/Research/HISTORICAL_CONSTRUCTION_AND_BLACKSMITH_CALIBRATION_2026-11-17.md. Headlines - LaborMinutes is total person-minutes, recalibrated cabin 600 to 5400, family cabin 900 to 14400, workshop 1140 to 28800 person-minutes (all PROVISIONAL), and the horseshoe defect is the output unit (set asset versus integer shoe consumption, no converter). The harness read 4934/78/5012, FAIL set byte-identical to baseline.

### E2 - construction labour implementation - merge ab81d0bf - lane commit 68a304c2 - E VERIFIED

Implements the E1 values in BuildingDesignCatalog, NpcCommissionedBuild, NpcHomeConstruction, ProductionDesignPresets, plus the new ConstructionLabourRecalibrationE2Tests and Def011 driver tests. Seven files, 662 insertions and 40 deletions. Harness run3 4945/75/5020 in the lane tree (6 new E2 tests pass, plus 2 new Def011 driver tests). The 3 fixed baseline failures are all SawmillAuthorshipC8Tests, a harness-shim artifact, not E2 code. Presets carry the E1 provisional values 5400/14400/28800 with phase folding verified arithmetically. LaborMinutes is total person-minutes. Chunking is real (CommitLabor clamps to requested, remaining, minutes-to-midnight, daily budget). Funding records the actual chunk. Section 18 regressions N, O, P, S all present and passing. Def011 totals updated without weakening. No fixed timer, no calendar override. Wage and hay untouched.

## 8 - Workstream F - construction hardware and imported materials

### F1 - window glass import - merge 204b3393 - lane commits debc98cf and fbdedbe6 - E VERIFIED

Glass is now a real paid good. Defined (ConstructionResourceKind.Glass equal to 3, the shim already carried it), priced from real lots, FIFO provenance lots in the yard, ordered through the D3 import pattern (cash or honest credit, 14 days from Pittsburgh glassworks via railhead), delivered into identifiable inventory, consumed by glazed window assembly. No phantom merchant, no free glass, no stock at a building automatically, no local glassmaking, no universal mandatory glass. Unglazed is the default. Provisional - 25c per pane (BLS Bulletin 440 anchor), threshold 4, lot 12, window counts 1/2/2 mirroring D2, openings phase is the roof phase until a later H-lane splits it. Twenty-five files, 2239 insertions and 40 deletions, plus an 80-line report. Harness 5096/0/5096 in the lane tree, delta exactly the 28 new WindowGlassImportTests. E confirmed the D3 ImportService port VERBATIM (160 added lines identical to D3's, in order), confirmed the Pass9 scan genuinely covered the worktree (the lane's fallback claim was wrong, Docs/UnwiredSystems.md does exist in the tree), confirmed the hand accounting for all 9 new types, and ran a proper expanded-build comparison (120 unique errors, multiset identical to D5's, zero errors on F1-changed lines). The merge had conflicts in 5 files, all resolved keeping both sides - SystemsSaveDtos, Pass9, SimulationSystemsHub, SimulationDrivers, FirstLedgerSliceBootstrapper. The WindowGlassRequirements.cs line was added to the shared sources.txt first. One integration interaction followed - run25 failed on 4 F1 Commission tests because the fixtures priced only lumber and glass while D2's merged door-hardware requirement (9 units in the roof phase) now applies. Fixed test-side by commit 49e93428, pricing door hardware at the D2 yard convention (45c) in the 4 Commission fixtures, with no production code changed. Lesson recorded - the merge tool's text matching failed on em-dash characters in code comments, because the file's literal U+2014 bytes did not match plain-hyphen old text, and Python line-splicing by marker lines was the reliable resolver.

### F2 - roofing review - merge 32f74b2f - lane commit 356776ed - E VERIFIED

Interim scope documented, no distinct roofing good built. All three designs specify a log roof with lumber only - settler roof 300 lumber plus 9 door hardware (2,100 labour-min), family 450 plus 18 (6,600 min), workshop 500 plus 18 (13,200 min). Lumber totals are settler 640, family 960, workshop 1,180. Wood shingles are the documented future option (E1 sections 8 and 9), not a current requirement, so no shingle supply chain was built (a good with zero consuming designs would be supply without demand). The system already fails closed honestly, pinned by 3 new tests - no phase carries a Roofing-kind requirement, a shingled settler variant refuses at EvaluateCommission naming wood shingles while the plain settler evaluates viable (the 1500-shingle count is marked illustrative), and discovery against an empty directory returns null with "no real supplier found". Three files, 362 insertions, zero production files changed. Harness 5235/0 in the lane tree. E confirmed the word shingle appears in exactly one .cs file in the whole tree, the new F2 test file, so no shingle definition, yard lots, import registration, price, or design requirement exists in production. One NIT (not verdict-breaking) - the report's "no other period roofing appears in Canon" sentence was literally overbroad, because Canon mentions tar paper once (period-change availability input, not a roofing good) and earth/sod once (general local building material, not roofing), and the slate hits are school slates and the substring inside translate. The sentence was softened in commit 247b3865 at merge time. A second follow-up commit baa20909 corrects the stale glass line in the ProductionDesignPresets class doc (F2-2 - it still said the price port returns -1 for masonry, paint, glass, roofing, but F1 added glass pricing, comment only, no test depends on it). Note - baa20909's commit message claims both corrections, but it only contains the presets fix, and the report softening landed separately in 247b3865. That is cosmetic only and not worth rewriting history over. F2 adds no Codex Unity checklist items.

## 9 - Workstream G - forestry, lumber, procurement

### G1 - forestry chain and procurement unwind - merge 1cba10e5 - lane commit f2d256a6 - E VERIFIED

The happy-path chain is genuinely wired end to end. Lumber unit verified - 1 milled white-pine board, 1x12x16 ft, 16 board feet, 16 kg, 0.04 m3, opening wagon 450 kg mass-bound at 28 units per trip. Conversion is a yield abstraction, not mass-conserving, left as is. The broken edge fixed - post-sale failure strand, where paid-for lumber left every inventory on freight, staging, or payment failure. The fix is a failed-procurement unwind (UnwindFailedProcurement plus TryVoidSale port) - sale voided in full with provenance intact, cash refunded, payable cancelled, double-void refused, retry still gated. Twelve files, 1334 insertions and 64 deletions. Harness 4950/75/5025 in the lane tree, 13 new unwind tests pass, zero new failures. Residuals recorded - sawmill remainder re-begin sweep missing (harness-excluded), ConsumeForPackage partial-decrement bug unreachable behind CoversAll, stale docstrings, owner question (r) retry policy untouched.

## 10 - Workstream H - construction labour and building completion

No package merged in this workstream during the campaign. The work is held on the owner decision F-D11-2, recorded in section 19. The M4 audit (section 15) found the two top breaks that define the H work - F-M4-1, the player weekly queue completes shells by elapsed weeks alone, a fixed building timer with no worker, no work packages, and no labour commitment, and F-M4-2, queue-completed shells never register in HousingAuthority, so no household can ever occupy a player-built shell through the housing system. Both gaps bottom out on the same missing piece, F-D11-2 (no definition to design mapping, no sleeping capacity on a shell). The repair is a multi-lane job - first the owner decision on F-D11-2 (author sleeping capacity, rule a mapping, rule a default, or rule shells are never inhabited), then a queue-to-executor bridge that starts a real executor project from a queued shell and drives it with committed labour. Inventing the mapping in a lane would be silently deciding F-D11-2, so no lane did. The store panel routing itself is verified - D-13 holds, every build action routes through the weekly queue, and no live caller uses the instant path.

## 11 - Workstream I - households, shopping, consumption

### I1 - household chain and over-demand repair - merge bd695046 - lane commit 98ee1521 - E VERIFIED

The household chain is wired end to end and honest at every step (12 verdicts hold). One confirmed break repaired, I1-M1 - the shortage monitor sized every meal-priority item's need for the full 7-day target independently while meals eat one item per serving in priority order. An empty 2-member larder demanded 168 servings for 42 eaten per week (about 6x over-demand), stripping the 68 to 136-unit opening store stock on day 1 to 2. DEF-067 had flagged the symptom, this repairs the cause. The repair allocates food needs in priority order against one shared cover total, with a stockout falling back to the next food. The lane caught and fixed its own first-version flaw (top food never saw lower-priority held stock) and pinned it with a dedicated test. Five files, 888 insertions and 5 deletions. Harness 5037/8/5045 in the lane tree, 8 new tests pass, 42-loaf pin 4 of 4 still passes. Secondary findings documented, not repaired - F1 NPC-port docstring overclaims, F2 off-map members eat from the larder (needs an owner decision), F3 dead StartingHouseholdEconomy seeder, F4 store closed Sundays by design, F5 store lookup hardcodes owner person id 1, F6 physical arrival NOT UNITY VERIFIED, F7 dead HouseholdErrandCoordinator. Caveat - SimulationDrivers.cs wiring is harness-excluded and needs Codex Unity EditMode. E verified from the lane's saved logs because the .NET runtime segfaulted on startup in E's environment (exit 139, the environment fault in section 22).

### I2 - household follow-ups and store lookup fix - merge e5bd3058 - lane commit 120193ce - E VERIFIED

F5 fixed - the store lookup no longer hardcodes owner person id 1. A new harness-compiled predicate BusinessInstanceState.IsDevelopmentNpcGeneralStore (NPC-owned General Store with a live runtime) replaces it, and the bootstrapper delegates to it. Six new regression tests. F1 fixed (comment only) - the NPC-port docstring now states the real gates. F3 (StartingHouseholdEconomy) and F7 (HouseholdErrandCoordinator) deliberately left in place - F3 has dedicated test coverage, F7 is save and loaded by the hub. F2 (off-map members eat from the larder) recorded as an open owner question with options (a) status quo, (b) trail rations, (c) present members only, (d) separate off-map ledger. The lane recommends (a) until trail rations are modelled. Five files, 121 insertions and 6 deletions. Harness 5066/0 in the lane tree, 6 new tests pass.

## 12 - Workstream J - manufacturing and economic network

### J1 - manufacturing network audit - commit 30f9990c, merged by fast-forward - E VERIFIED

All 23 edges among the seven dev-town businesses verified live with real drivers, cash, and custody - forestry to sawmill to yard to construction (G1), sawmill fuel-wood byproduct to fuel dealer (SimulationDrivers.cs line 802, TransferByproductsToFuelDealer), sawmill to yard wholesale transfer with the yard paying the mill on actual delta, yard retail plus kind-level hardware sales, smith supply drivers iron, coal, nails, glass (D3, D4, D5, F1, live but idle, no smith), bakery flour from Millbrook, bread to General Store wholesale (authored agreement, 24 loaves per day at 4c), notions from the store, fuel from the dealer, carpentry lumber and supplies in, furniture sold to households, freight hauling with paid revenue (CollectAndPostUncollectedRevenue), horse and store feed procurement, household shopping and consumption (I1, I2), NPC payroll, 8-week close, leases, farm cycle driven weekly (no farm in town, idle), hired construction labour paid 750c per week on package completion, logging camp supply, horse care, store restock including Ashford furniture reorder, boarding leg.

The top missing edge is wagon repair (F-J1-1, owner decision, no production connection made). Wagons wear on every haul in production (FreightResourcePool.ReleaseTeam calls wagon.Damage, 0.02 per trip), but ScanFleetForRepair, TrySendToRepair, TryCompleteRepairJob, FlagHaulingBlockedOrders, EscalateOverdueOrders, and ComputeTripWear01 have zero production callers (tests only), and SelectAvailableWagon never checks condition, so a wagon at 0.05 still hauls and wear is currently cosmetic. The wheelwright path needs a wheelwright business (owner must provide the name first per the naming rule) plus a smith for ironwork. The freight company's internal path cannot be driven honestly as-is - TryCompleteRepairJob records internalRepairCostCents but it is never deducted from any cash and no materials are consumed, so driving it would be a free repair. Making it honest needs a real materials leg (yard lumber 40c plus store nails 30c exist, ironwork has no source at all), a repair duration with saved state, and a cash debit, which is designing the maintenance subsystem, not connecting an edge. The lane deliberately did not add a fail-closed dispatch gate, because with no repair path a gate would eventually strand Ethan's only wagon with no remedy.

What J1 built instead, following the F2 precedent - no production file changed. Four boundary tests pin the wagon-repair boundary so a future repair connection is deliberate - (1) wear accumulates 1.00 to 0.90 over 5 real reserve and release hauls with no auto-repair, (2) ScanFleetForRepair with a null shop fails closed (0 orders, diagnostic names the missing wheelwright shop), (3) a wagon at 0.05 condition still dispatches, (4) the dev-town asset holds exactly the 7 businesses and no blacksmith, wheelwright, farm, dairy, grain-mill, tannery, tailor, bank, doctor, or butcher. Three files, 388 insertions. Harness 5265/0/5265 in the lane tree, 4 new tests pass. One NIT (not verdict-breaking) - the report's "zero production callers for all of it" sentence was imprecise, because TrySendToRepair's sole production caller is the undriven ScanFleetForRepair itself (WagonRepairEconomy.cs line 300). The sentence was softened in commit a72defcf at merge time. J1 adds no Codex Unity checklist items. Other missing or dormant edges, all owner-gated, none connected - F-J1-2 farrier and horseshoeing (FarrierService.FitShoes plus D1 shoe mechanics, zero callers, needs smith plus a demand-model decision), F-J1-3 toll sawing (fully built, zero callers, no live customer, construction buys from the yard), F-J1-4 grain mill and elevator (no grain, no farm), F-J1-5 dairy, creamery, cheese (no cows or livestock in the asset), F-J1-6 tailor, tannery, bank, doctor, butcher (no such businesses, butcher wired but no livestock), F-J1-7 freight agreements (unused by design, freight works per-trip), F-J1-8 generic supplier comparison plus off-map catalog (dormant, live routes registered elsewhere).

## 13 - Workstream K - finance, contracts, business continuity

### K1 - derived-view replacement - merge e29484e7 - lane commit 9e2befe1 - E VERIFIED

All five derived opening views replaced with direct owner-record iteration (OpeningInventory, BusinessCashTargets, HouseholdCashTargets, OpeningEquipment, OpeningEmployees deleted, TryResolveOpeningInventory and all Validate loops converted, the generated-household fallback preserved). Eleven files, 802 insertions and 130 deletions. Harness 5072/0/5072 in the lane tree, delta exactly the 12 new tests. The stock endowment and the cash views have no harness-compiled consumer, so their replacements were verified by a scratch build (42 pre-existing errors elsewhere in the file, zero in the changed region). Honest gap recorded - none of the three K invariants (acquisition payment conservation, debts distinct from personal obligations, title non-transfer) has a harness-compiled regression test, because the acquisition tests are harness-excluded and a stock apply-twice harness test is impossible. That gap became K2.

### K2 - derived-view sweep and invariant regression coverage - merge c4194c7e - lane commit 2beb25f6 - E VERIFIED

Deletes the last two derived views (OpeningSpareBlades, WholesaleAccounts), so all seven are now gone, pinned absent by a reflection test. Adds 12 harness-compiled invariant regression tests - acquisition conservation at the terms level (5,400c plus 30,000c withdrawal reconstructs 35,400c, proceeds 250,000c plus withdrawal equals 280,000c, deal balances, negatives clamp to zero, DTO round-trip holds), debts distinct (business loan 50,000c versus personal loan 12,000c, payment isolation, novation needs creditor consent, separation survives save and load), and title non-transfer (owner change leaves the plot title with the seller, explicit bridge transfer with instrument moves it, purchase without instrument refused). Nine files, 695 insertions and 25 deletions. Harness 5183/0 in the lane tree, delta exactly 13 tests (12 new plus one intermediate fix), zero new failures. Two corrections from the lane, both checked hard by E and both holding - (1) the brief's suggestion to pin the proceeds credit through AcquisitionMarketManager.CreditSellerProceeds in the harness is wrong, because that file is harness-excluded (0 includes), so the ledger credit step stays Unity-only, pinned by the three named A1 tests, and (2) the title invariant as phrased is too broad, and this is a REAL finding for the owner. The market close DOES transfer the premises plot title on a business purchase, explicitly, with listing id as instrument and final price, at AcquisitionMarketManager.cs lines 9058 (inside CompleteBusinessAcquisition) and 9540 (inside TryPurchaseBusiness), plus land-close analogues at 9008 and 9464, and RecordTitleAcquisition itself at line 9639 is the documented single choke point. TryTransferBusinessToPlayer contains zero title calls, and the whole SharedBusinessRuntimeManager.cs contains zero occurrences of the word title. The precise invariant as pinned is correct (separation - the runtime transfer never touches title, title moves only through the explicit choke point), but whether a business purchase should carry the premises title at all is an owner decision about listing terms, not a defect. It is recorded in the report and in the test file's own doc comment, not decided. The merge had zero conflicts, confirmed by simulation and by the real merge.

## 14 - Workstream L - save, load, time, simulation determinism

### L1 - blacksmith save and load continuity - merge bd0b17cc - lane commit de22396b - E VERIFIED

The top break repaired - BlacksmithRuntime had no save or load at all. Now a nested BlacksmithRuntimeSaveDto plus CaptureSaveDto plus clear-then-load LoadFromSaveDto, with a Series2Pass9Save section following the bakery precedent (identity guard, old-save clears, missing owner diagnosed). Five files, 743 insertions. Harness 5055/1/5056 in the lane tree, delta 6 new tests, zero new failures (the single failure is Pass9 on the 7 VetServices types, S4's scope). Audit - L1-2 through L1-7 verified OK, L1-8 not applicable (no glass import system yet), L1-9 recorded not repaired (ForgeRepairChain has a DTO but no persistence wiring or production owner). Follow-ups - F-L1-1 (D3's driver must hand the live BlacksmithRuntime to Series2Pass9SaveState), F-L1-2 (wire ForgeRepairChain when its driver lands). No SaveDtos.cs change, shim untouched.

### F-L1-1 - one-line smith save-owner fix - commit 3cb52219 - parent commit on integration

D3's ConfigureLiveBlacksmithSaveOwner obtains the live runtime through the hub but never assigned pass9.BlacksmithRuntime. One line after the hub lookup succeeds, plus a stale-comment correction. The holder is Series2Pass9SaveState.BlacksmithRuntime at Series2Pass9Save.cs line 503, confirmed assignable from disk. Run24 on this commit read 5197/0 again, as expected, because the file is harness-excluded and the count is unchanged. This is a parent follow-up commit, not counted among the 30 lane packages.

## 15 - Workstream M - A-Z foundation and regression audit

### M1 - A-Z first slice, foundation - merge ecf64a2a - lane commit 703c3289 - E VERIFIED

The foundation audit found the household housing loop is the large dead area - 9 confirmed defects (F-M1-1 to F-M1-9, property acquisition, decision engine, housing search executor, household move service, rental with no rent or payee, boarding bridge with no callers, needs lost on save and load, newcomer settlement bypassing occupancy, no live opportunity source). The repair M1-R1 wires the boarding leg only (new HousingSearchDrive, daily, boarding-only, two guards, rental waits on a rent source, purchase waits on listings and financing). Eight files, 1007 lines. Harness in the lane tree - the lane's headline numbers were inflated by 16 against its own logs and E's re-run, and the verified final state is 5036/8/5044, delta exactly the 7 new tests. That reporting error is recorded here, not a code defect. Caveat - the SimulationDrivers daily call, bootstrapper feeder call, and bridge are harness-excluded and need Codex Unity EditMode.

### M2 - A-Z second slice, manufacturing and finance - merge d5661552 - lane commit d7c241ea - E VERIFIED

Findings F-M2-1 to F-M2-9. F-M2-1 (highest impact, recorded NOT repaired) - construction progress billing never settles in production. CommissionBuilderContract has zero production callers, AcceptBuilderStageAndReleaseDraw and RecordDeposit have zero production callers, the hub constructs the billing service with a null cash port, and the only IConstructionBillingCashPort implementation is test-only. Repair needs a production cash port plus a driver hired-builder path plus an owner decision on stage acceptance. This is the expanded-campaign F-D11-7 item, reconfirmed. F-M2-2 (repaired) - the sawmill drive never re-begins lots that miss their delivery-day begin. A sweep FindStrandedLotIds plus BeginStrandedRemainders (one attempt per lot per day, oldest first, today's deliveries excluded) plus a drive call site, with 10 new tests. F-M2-3 to F-M2-6 - dormant runtimes (Bank, Tailor, Tannery, GrainMill), source-implemented without production callers, no such businesses in the dev town. F-M2-7 - completion handoff published but never consumed. F-M2-8 - no inspection or acceptance stage anywhere (possibly by design). F-M2-9 - business loans monthly versus player debt weekly, by design. Verified wired - bakery, carpentry, butcher, fuel dealer, freight, construction completion and occupancy including the player weekly queue, commissioner, finance authorities. Five files, 626 insertions and 4 deletions. Harness 5063/0 in the lane tree, 10 new tests pass.

### M3 - A-Z third slice, trade, animals, markets - merge 3e31921c - lane commit 562d4d7a - E VERIFIED

Two files, 154 insertions and 17 deletions, no production source changed. The M3-R1 repair is an independent implementation of the same multi-day labour fix already on integration as 0d8f1760, and the merge kept the integration version of the test file and took only the 129-line audit report from M3. Findings - F-M3-1 ImportService is a dead parallel import authority (zero production constructions, the live route is the standing-order procurement chain, 8 production doc comments falsely claim commodities reorder through ImportService, needs a decision), F-M3-2 animal services fully implemented and entirely undriven (no DriveVet or DriveFarrier, no shoe-wear or hoof-decay driver, LivestockDiseaseService never ticked, DoctorPracticeRuntime also never constructed), F-M3-3 off-map supplier catalog empty in production (QuoteAll and RegisterSupplier zero production callers), F-M3-4 no NPC pricing decisions (B1-7 confirmed), F-M3-5 freight agreement book unused, non-blocking by design, F-M3-6 generic SupplierComparison dormant, F-M3-7 entrepreneurship chain walks away at the finder gate (owned by B2). New owner questions - whether animals should ever need vet or farrier care, the single import authority, NPC repricing policy, freight contracts. Harness 5089/0 in the lane tree, both Scenario 4 tests pass.

### M4 - A-Z fourth slice, completion, determinism, ledger - merge bb2b3bee - lane commit 026f48db - E VERIFIED

The three areas left open by the brief - (1) construction completion and occupancy, the D-11 three clocks, the store-panel routing, and the completion to registration to occupancy to equipment-install chain, (2) save and load, time, and determinism beyond L1's blacksmith slice (clock and speed presets, deterministic seeds, DTO round-trips for the new campaign packages, opening-resources-once), (3) the follow-up ledger, every finding from M1, M2, M3 and the lane follow-ups, reconciled to closed or open with one evidence check each. Headline - two things are genuinely wired that the brief worried about, the store panel really does route every build action through the weekly queue (D-13 holds, no live caller uses the instant path), and opening resources really do apply once (the development-town cash endowment is NewGame-gated and marker-gated). But the weekly queue itself is the top break in this slice - it completes shells by elapsed weeks alone, a fixed building timer with no worker, no work packages, and no labour commitment, and the shells it completes never register in the HousingAuthority, so no household can ever occupy a player-built shell through the housing system. Both gaps bottom out on F-D11-2. The one repair in this slice is M4-R1 (F-M1-7) - open housing-search needs were lost on save and load, fixed by mirroring the purchasing-needs pattern (DTO field, hub export and import, SaveLoadManager capture and restore, clear-then-load ImportState with id-sequence restore). The PopulationSaveDto diff between the real SaveDtos.cs and the shared harness shim shows zero differing lines. Seven files, 374 insertions. Harness 5112/0 in the lane tree, 5 new tests pass. The A-Z audit is complete with M4 - no fifth slice exists.

## 16 - Workstream S - schema and carry-over repair

### S2 - schema-2 test repair - merge ab9dfd50 - lane commit 95848f5e - E VERIFIED

The first repair target - 66 schema-2 test-helper NREs. Ten test-only files, 211 insertions and 72 deletions. Harness 5001/11/5012 in the lane tree, 67 fixed, 0 new failures, the remaining 11 exactly the out-of-scope set. E confirmed both subtleties against code. The shared harness shim dataPath fix at UnityEngine.cs line 893 was part of this work and fixed the 3 C8 path artifacts at the shim level, moving the effective baseline to 4937/75.

### S3 - carry-over FAIL repair - merge 5b181cc0 - lane commit a9f55691 - E VERIFIED

The remaining 11 baseline failures - 8 expanded-campaign carry-over assertions plus the C8 artifacts. Fixes - PhysicalItemSpec.cs seeded 18 catalog SKUs, SharedBusinessDemandWiringTests fixture merges suppliers, 5 PurchasedRetailLotReceiptTests rebuilt on the bare BusinessRuntimeState fixture, Pass9 70 types classified (E reconciled the counts - 70 unaccounted second pass, 74 FeatureInternal total including 4 first fix, 76 classified overall including 2 SourceOnly). Four files, 252 insertions and 70 deletions. Harness 5012/0/5012 in the lane tree, all 11 fixed, zero new failures. The integration harness run3 on the S3 merge read 5036/1, and the single failure was Pass9 with exactly 7 VetServices types unaccounted (VetPractitioner, VetServiceAgreement, AnimalTreatment, MedicineLot, VetInvoice, VetService, FarrierService). Root cause - D1 added VetServices.cs to the harness and classified only its 3 new DTOs, so the 7 pre-existing types became newly visible to the scan, and the FAIL-set comparison by test name hid the content change because Pass9 was already failing in the baseline. Lesson recorded - for Pass9, compare the unaccounted-list CONTENT, not just the FAIL-set test names.

### S4 - Pass9 VetServices classification - merge 4d2e78ff - lane commit a6227d05 - E VERIFIED

Classifications - 5 records feature-internal (VetPractitioner, VetServiceAgreement, AnimalTreatment, VetInvoice, MedicineLot, with MedicineLot currently unreferenced outside its declaration, stated in evidence), 2 services registered unwired in Docs/UnwiredSystems.md with candidate-list entries in both registry tests (VetService, FarrierService - FarrierService has no production caller, and D1's "deliberately wired" claim was about BlacksmithRuntime gaining a reference FROM FarrierService, not a caller). Three files, 74 insertions. Harness 5037/0/5037 in the lane tree. The integration harness run5 on the S4 merge read 5050/0/5050, the first fully green integration run.

## 17 - Harness history

All numbers below are read from the saved logs in /home/hatch, run by the Roslyn harness on .NET 8, with the cleaned environment including DOTNET_ROOT and LAND_LEDGERS_REPO pointed at the worktree Scripts checkout. The FAIL set at every step is compared against the a1c20ad9 baseline (4934 passed, 78 failed, 5012 total).

- Baseline from a1c20ad9 - 4934 passed, 78 failed, 5012 total
- Run2 on 883273a6 (E1, S2, D1 merged) - 5029 passed, 8 failed
- Run3 on 5b181cc0 (S3 merged) - 5036 passed, 1 failed (Pass9, 7 VetServices types)
- Run4 on 1cba10e5 (G1 merged) - 5049 passed, 1 failed (Pass9)
- Run5 on 4d2e78ff (S4 merged) - 5050 passed, 0 failed - first fully green run
- Run6 on cb222ae4 (A2 merged) - 5053 passed, 0 failed
- Run7 on ecf64a2a (M1 merged) - 5060 passed, 0 failed
- Run8 on ab81d0bf (E2 merged) - 5068 passed, 0 failed
- Run9 on 9957be0a (D2 merged) - 5087 passed, 2 failed - real E2 and D2 test interaction, fixed test-side by 0d8f1760
- Run10 on 0d8f1760 - 5089 passed, 0 failed
- Run11 on bd0b17cc (L1 merged) - 5095 passed, 0 failed
- Run12 on bd695046 (I1 merged) - 5103 passed, 0 failed
- Run13 on 679a2b7b (B1 merged) - 5107 passed, 0 failed - after two aborted attempts on the disk quota fault, third attempt with the cleaned environment
- Run14 on e5bd3058 (I2 merged) - 5113 passed, 0 failed
- Run15 on e2097ab8 (D3 merged) - 5131 passed, 0 failed
- Run16 on d5661552 (M2 merged) - 5141 passed, 0 failed
- Run17 on 3e31921c (M3 merged) - 5141 passed, 0 failed (M3 adds no tests)
- Run18 on 518aa31d (B2 merged) - 5157 passed, 1 failed - real B2 and B1 guard interaction, fixed test-side by 27494fdb
- Run19 on 27494fdb - 5158 passed, 0 failed
- Run20 on e29484e7 (K1 merged) - 5170 passed, 0 failed
- Run21 on a79b483c (D4 merged) - 5181 passed, 0 failed
- Run22 on 3fd8b82b (D5 merged) - 5192 passed, 0 failed
- Run23 on bb2b3bee (M4 merged) - 5197 passed, 0 failed
- Run24 on 3cb52219 (F-L1-1) - 5197 passed, 0 failed (harness-excluded file, count unchanged)
- Run25 on 204b3393 (F1 merged) - 5221 passed, 4 failed - real F1 and D2 Commission interaction, fixed test-side by 49e93428
- Run26 on 49e93428 - 5225 passed, 0 failed
- Run27 on 2c027016 (B3 merged) - 5232 passed, 0 failed
- Run28 on c4194c7e (K2 merged) - 5245 passed, 0 failed
- Run29 on e3fbea06 (D6 merged) - 5257 passed, 1 failed - real D6 and G1 nails interaction, fixed by badf9a23
- Run30 on badf9a23 - 5258 passed, 0 failed
- Run31 on 247b3865 (F2 merged) - 5261 passed, 0 failed
- Run32 on a72defcf (J1 merged) - 5265 passed, 0 failed
- Run33 on d2b92b19 (C1 merged) - 5272 passed, 0 failed - final state

The pattern across the campaign is consistent. Merges of two verified lanes can still fail at integration when one lane's tests embed the other's pre-change state (run9, run18, run25, run29). Every one of the four was a real interaction, every one was fixed, and every fix was re-run green before the next merge. No integration run ever introduced a new failure that was not caught and fixed in the same step. The FAIL set at the final state is empty, and all 78 baseline failures are resolved.

## 18 - Calibrations - all PROVISIONAL, owner to correct

Every value in this section is provisional and marked for Kennedy to correct. None of them is presented as a final historical fact.

- Construction labour - settler cabin 5400, family cabin 14400, carpenter's workshop 28800 person-minutes. One worker-day is 600 minutes.
- Door hardware - 9 units settler, 18 family, 18 workshop, at 45c per unit.
- Settler end-to-end build cost 26,005c. D2 money estimates 26005, 39210, 48010.
- Kind breakdown per door-plus-window pair - 3 hinges, 2 latches, 1 handle, 3 nails.
- Glass - 25c per pane (BLS Bulletin 440 anchor), Pittsburgh glassworks via railhead, 14 days. Threshold 4, lot 12. Window counts 1, 2, 2.
- Iron - 45c per unit, Pittsburgh ironworks via railhead, 220 miles, 14 days. Threshold 8, lot 16 (720c).
- Forge coal - 18c per unit, off-map coal dealer via railhead, 180 miles, 12 days. Threshold 6, lot 12 (216c).
- Nails import - 30c, Pittsburgh ironworks via railhead, 220 miles, 14 days. Threshold 12, lot 60.
- Smith to yard hardware delivery - 45c per piece.
- Commercial lease - 1500c per month rent, 1500c deposit, 12-month term. Formation pays 3000c (deposit plus first month).
- NPC close - after 8 consecutive struggle weeks.
- Lumber - 1 unit is 16 board feet white pine, a 1x12x16 ft board, 16 kg, 0.04 m3. Mill to yard wholesale 32c, yard retail 40c.
- Flour - 48c per sack, 18 bakery units. Freight - 25c base plus 1c per mile round trip. Feed - 15c per sack, 2 units.
- Bakery bread wholesale - 24 loaves per day at 4c.
- Day length - 1440 real seconds at 1x. Speeds 100x and 250x.
- Opening cash - 8 households at 40,000c each. General Store 50,000, Bakery 35,000, Fuel 20,000, Freight 20,000, Sawmill 15,000, Yard 12,000, Carpentry 10,000.
- Wagon wear - 0.02 per trip. Repair thresholds 0.60 and 0.25. Shop rate 25c per hour. Markup 20 percent. 480 shop-minutes per day. Escalation 7 and 14 days. 0.5 condition per internal repair.
- Sawmill wages - Richard 750c per week, Josh 750c per week. Hired construction labour 750c per week.
- Roof phase materials - settler 300 lumber plus 9 door hardware (2,100 labour-min), family 450 plus 18 (6,600 min), workshop 500 plus 18 (13,200 min). Lumber totals 640, 960, 1,180.
- Wood shingle rate - 1,800 to 2,500 per worker-day (E1, no shingle price sourced anywhere).

## 19 - Open owner questions - recorded, NOT decided

Nothing in this section is decided. Each item needs Kennedy's call, and no lane silently chose any of them.

- F-D11-2 - shell sleeping capacity. Options (a) author sleeping capacity, (b) rule a definition to design mapping, (c) rule a default, (d) shells are never inhabited. This blocks the H workstream (F-M4-1 and F-M4-2).
- F-J1-1 - wagon repair. Options (a) wheelwright business, name first, (b) ruled freight-company self-maintenance with a real parts source, (c) wear stays cosmetic.
- F-C1-1 - Add Operation actor. Player UI action, NPC expansion policy, or weekly driver.
- F-M2-1 - construction progress billing never settles in production. Needs a stage-acceptance decision, plus a production cash port and a driver hired-builder path.
- K2 - premises title on purchase. The market close transfers the premises plot title on a business purchase, explicitly, with listing id as instrument and final price. Whether purchases should exclude the premises title is an owner decision about listing terms, not a defect.
- A2 - opening-cash target interpretation. The motive-based opening target is the seller's agreed pre-close withdrawal. If the buyer should keep all business cash by default, the target goes away.
- Carpenter wage - unruled. Do not retune the 300c bed baseline first.
- Hay - no priced supply. Bed and bedroll recipes cannot run in production.
- I2 F2 - off-map members eat from the larder. Options (a) status quo, (b) trail rations, (c) present members only, (d) separate off-map ledger. The lane recommends (a) until trail rations are modelled.
- M3 - animal vet and farrier demand model, single import authority, NPC repricing policy, freight under agreed contracts.
- B2 F-B2-6 - never-occupied buildings have no landlord on record. F-B2-2 - formation working-capital gap, a new business starts with no opening cash and period-1 rent may go to arrears. F-B2-3 - deposit offset against arrears.
- F-M1-5 - residential rent has no source.
- F-M4-1 - player weekly queue completes shells by elapsed weeks alone. F-M4-2 - queue shells never register in HousingAuthority. Both blocked on F-D11-2.
- F-M2-8 - inspection and acceptance stage (possibly by design).
- F-J1-2 - farrier demand model plus smith. F-J1-3 - toll-sawing for commissioned builds. F-J1-4, F-J1-5, F-J1-6 - farm and grain, livestock and dairy, remaining service businesses need names plus supply before any lane wires them.
- F-C1-7 - persist CapabilityIds. F-C1-8 - canonicalize the id vocabulary before any runtime gates on ids. F-C1-4 - reconcile the two generic models. F-C1-5 and F-C1-6 - wire only when a butcher or grain business exists.
- F-D6-1 to F-D6-5 - bootstrap SimpleHardware stranded until re-authored per-kind, per-kind pricing needs a deliberate decision, unequal doors and windows need explicit kind lines, on-account sales still bucket-fill, D3 same-day ambiguity now spans kinds.
- F2-1 - shingle versus board or log roof for a future two-story storefront. F2-3 - future shingled design four-step spec.
- Carried items - F-D11-4 Q1, F-D11-7 progress billing null cash port, F-D11-8 documentation, C-6, C-9, NPC property-acquisition production caller, D-10 lodger-path driver, G-2, G-3, G-4 follow-ups, evaluator loan-awareness, D-12 deferred, DEF-009, DEF-067, DEF-011 await live Unity verification, generic BusinessType scope fork (full enum removal now versus phased), 162 Unity EditMode failures unclassified, Enzo spelling conflict (Kwindt versus Kwint).

## 20 - Codex Unity checklist

Everything in this section needs Codex in Unity. None of it is verified by the source harness. Source tests are never PlayMode evidence.

- A1 - run UBC_A1_AcquisitionContinuityTests in Unity EditMode, including A2's 6 new tests. Verify the A1 binding fix and the A2 Payable hub path (the Payable path is code-reviewed only).
- M1, I1, I2 - verify the harness-excluded call sites (SimulationDrivers daily calls, bootstrapper feeder and predicate).
- B1 - run the close path live with the 7 authored businesses. A genuinely insolvent authored business closes after 8 weeks by design.
- B3 - compile the SimulationDrivers adapter and run the live close checks (leased, owner-held, unsettled-Payable variants).
- M2 - verify the stranded-lot call site.
- D3, D4, D5, F1 - verify the harness-excluded wiring (daily drives, hub save and restore, bootstrapper).
- B2 - verify the finder lambdas, close callback, hub save wiring, and boot seeding.
- K1 - verify the bootstrapper seeding plus TryApply plus generated-array assertions.
- K2 - run the proceeds-credit tests plus the market-close title split plus the spare-blade and wholesale bootstrapper sites.
- M4-R1 - verify the open housing needs restore on Continue.
- F-L1-1 - verify the formed smith's stock survives save and Continue.
- C1 - compile the formation half and run the Test Runner for the harness-excluded BusinessCreationTests and GenericBusinessEconomyTests. Check the panel-path template capability. Don's workshop template now records carpentry_setup. Recorded capabilities do NOT yet persist (F-C1-7, expected). Do NOT test TryAddOperation or registry population (needs the F-C1-1 owner decision).
- D6, F2, J1 - no checklist items. No production file changed in F2 or J1, and no changed file in D6 is harness-excluded.
- General - startup, shopping and meals, construction and occupancy, 60-day run, conservation (money, goods, and custody move together, nothing invented, nothing duplicated).

## 21 - Publication state

- Integration branch muse/universal-business-construction-2026-11-17 at d2b92b19, local-only.
- Remote HEAD verified at 7b1ca29c (main). The integration branch is confirmed absent from GitHub by explicit-URL ls-remote probe.
- Push is blocked on auth. No credential helper is configured. The Nov 2 push credential LandLedgersScriptsPush-2026-11-02 must be revoked before any push retry, because its token was visible in a captured settings screenshot. Protect or delete the screenshot.
- Verified bundle - /home/hatch/ll-ubc-integration-2026-11-17.bundle, 592,438 bytes, SHA256 a4dee0da3c68230e19b60bcb4b6939a08810b05062c8176fa3f2548c94823a17. It contains ref d2b92b19 and requires base a1c20ad9, which is already on GitHub. Codex can pull it onto the foundation branch.
- Never merge to main without Kennedy's explicit say-so. No force-push, no history rewriting, no hard reset. Main is 7b1ca29c, unchanged.
- A possible Crates.io API token in commit 1aaf7269 was flagged Nov 4 and is unresolved.
- Steamworks is PAUSED by Kennedy since 2026-11-09 at 15:11 EST. No sign-in retry, no account checks, no fee decision until he says resume.

## 22 - Environment faults

Two transient sandbox faults hit the campaign. Neither was a code problem. Both cleared on their own.

- Fault 1, 11:44 to about 11:52 EST - the .NET runtime segfaulted on startup (exit 139, MAP_SHARED mmap EPERM, SIGSEGV), and git writes failed with EPERM (loose object writes and ref lock writes). No merge, commit, ref update, or harness run of any kind was possible in any worktree for about 8 minutes. Sibling build and run logs from that window are 0 bytes, which is missing evidence, not passing runs. E-for-I1 and E-for-B1 verified from saved logs because their independent re-runs were impossible during the fault.
- Fault 2, EDQUOT, recurring from about 12:36 EST - harness runs abort with System.IO.IOException "Disk quota exceeded" while writing test output. This is NOT a full disk (df showed about 47 to 57G free). It is a home-subvolume quota that fills as lane worktrees accumulate (about 905M per full worktree). A FAIL line whose failure text is that IOException is an artifact, not a test failure, and every such line in the campaign was read and confirmed as an artifact. Workarounds that work - run the harness binary with the cleaned environment INCLUDING DOTNET_ROOT, and redirect harness stdout to /tmp (tmpfs) and copy the log to /home/hatch afterward. Space actions taken - D5 deleted about 900M of stale bin and obj, and the parent removed 17 merged lanes' worktrees at about 13:09 EST (about 15G, branches preserved, worktrees regenerable). Kept worktrees were ll-ubc-base, k1, b2, d4, d5, f1, m4, b3, d6, k2, f2, plus c1 and j1 added later. The 486M bundle at /home/hatch/expanded-integration-2026-11-16.bundle and the about 40 detached scratch worktrees (L-F5) were NOT touched.

## 23 - Lessons

- For Pass9, compare the unaccounted-list CONTENT, not just the FAIL-set test names. The S3 merge hid a real content change (7 VetServices types) because Pass9 was already failing by name in the baseline.
- Merges of two verified lanes can still fail at integration when one lane's tests embed the other's pre-change state (run9, run18, run25, run29). Always run the integration harness after EVERY merge and read the failure text before assuming a resolution error.
- Check brace balance after splicing interleaved methods. The D6 merge interleaved two complete methods at one anchor and silently dropped VoidSale's closing brace (fixed by 1fbd645a).
- A helper that only knows two shapes (explicit lines or pair multiples) breaks pre-existing single-kind sellers. D6's breakdown helper needed a third shape for single-kind requirements (fixed by badf9a23).
- Em-dash characters in code comments break text-matching edits. The file's literal U+2014 bytes did not match plain-hyphen old text. Python line-splicing by marker lines is the reliable resolver.
- Lane harness copies - VERIFY the REPO and HARNESS lines on disk before regenerating, never trust a summary that says they are set (C1 found template paths in its copy).
- The cleaned environment for apphost runs must include DOTNET_ROOT (libhostfxr failure, exit 131, otherwise).
- Running from outside the worktree or pointing LAND_LEDGERS_REPO at the repo root causes about 139 to 213 bogus failures. All such runs were discarded.
- Never put a trailing comment on a sources line. Confirm new files appear in the regenerated csproj before building, because a stale csproj compiles 0 errors with none of the new code.
- Assert.False does NOT exist (CS0117). Assert.IsFalse does.
- Never git stash or pop in a linked worktree, because refs/stash is SHARED across worktrees.
- Build into isolated per-worker paths. /tmp/bin and /tmp/obj are shared across workers.
- Never git checkout -- to revert.
- FAIL files are inconsistent across lanes (some bare names, some "FAIL " prefixed). Normalize before diffing.
- Strict grep -c '^PASS ' matches the runner summary. A naive '^PASS' overcounts.
- The shared harness sources.txt now carries all staged lines (D3 transfer driver, D4 iron lines, D5 coal lines, F1 WindowGlassRequirements line), plus the A1 test exclusion and the D1 VetServices addition. Files under individually-listed directories need their own line.

## 24 - What happens next

The campaign is wrapped up on Kennedy's call at 14:40 EST on 2026-11-17. No lane is running and the merge queue is empty. The integration branch sits at d2b92b19 with the harness green at 5272 passed and 0 failed.

For Codex - pull the verified bundle onto the foundation branch and work the Unity checklist in section 20. Start with the A1 acquisition continuity tests and the A2 Payable hub path, then the harness-excluded wiring (D3, D4, D5, F1 daily drives and hub save and restore), then the B1 close path live with the 7 authored businesses, then startup, shopping and meals, construction and occupancy, the 60-day run, and conservation. Do not present source tests as PlayMode evidence, and do not touch the scene-restoration animation asset or the Main Scene, prefabs, terrain, fonts, or asset packs.

For Kennedy - the decisions in section 19 are the next fork. The largest is F-D11-2, which unblocks the whole H workstream (queue-to-executor bridge, real committed labour for player builds, shells registering in the housing authority). After that, F-J1-1 (wagon repair), F-C1-1 (Add Operation actor), and F-M2-1 (progress billing stage acceptance) are the next three. The push credential needs revoking before any push retry, and the bundle is the publication route until a push credential exists.

For the next Muse session - the session brief at workspace/land-and-ledgers/campaign-2026-11-17/00-campaign-brief.md is the full record. Lane reports live under Reports/Campaigns as UBC_*.md. Harness logs live under /home/hatch as ll-ubc-int-run*.log. The 30-minute checkpoint cron is still running and should be disabled now that the campaign has wrapped up, per the standing rule that reminders stop on wrap-up.
