# Land & Ledgers — recovery handoff for Luna Medium

## Workspace and safety

- Source worktree: `F:/LLEcon`, branch `codex/economy-recovery-2026-11-09`.
- Live Unity project: `F:/Land & Ledgers`, Unity6000.4.2f1 HDRP. The live Git branch remains the older integration checkout; reviewed recovery source is copied into its dirty worktree. Do not reset, merge, clean or replace that checkout.
- Preserve all authored scene/terrain/prefab/audio/UI work and every recovery worktree. No push is authorized.
- Live copies must match reviewed source before testing. Use the open Editor only; never launch a second batch instance against it. Domain reload and scene reload remain enabled.
- Approved owner locks:1870 opening;1440real-second Normal day. Full NPC development autonomy uses shared property/construction/business authorities and legitimate funding. Do not manufacture sellers, income, equipment or supplies.

## Completed local changes

| Commit | Result | Verification |
|---|---|---|
| `84e6d8e5` | Daily paid receiving validates/accepts stock before its single cash debit; refusals preserve funds | Earlier48focused Unity cases and production receipt/Continue evidence; do not repeat unnecessarily |
| `1cbd92fb` | Identified retail lots carry actual acquisition cost; partial sales conserve cent remainders; old unknown costs stay unknown | Native Store/receipt/wholesale tests; actual retail activity below |
| `74b52df1` | Procurement saves external market trades, finite supplier stock and receipt sequence | Native regressions; actual notions arrivals15/30after Continue |
| `9d0465b5` | Procurement comparison and retail consult the same configured SKU price | Two native override/unit-conversion cases; no authored prices changed |
| `f9159d9f` | Replaces detached supplier adapters and rebinds procurement cash to rebuilt businesses |8Store,1cash-rebind,6embodied-purchase native cases; production replay |
| `cafed7fd` | Refused loading/unloading starts cancel through TaskAuthority, releasing assignment/budget |2native refusal/retry cases; real delivered/collected shipment and released transport |
| `ca1907a0` | Restored work-budget lookup is invalidated even when resident count is unchanged; save snapshots no longer alias runtime |10native budget cases; resumed household purchasing from existing save |

Additional verified source checkpoints:

- `846e6566`: whole-lot mill-to-yard handover now debits the live source and rejects repeated source identity.26native Lumber Yard tests passed, including receiving refusal/conservation and existing save tests. No live town caller exists; this is not a playable forestry claim.
- `4db92d9b5889151d12c34ac2e37d5fc57781bb51`: player bootstrap ports promote to identified inventory/prices once a real business is formed, and explicit player ports follow a restored instance of the same owner. Production retail receipt IDs now use an additive, persisted business sequence; new `retail-v2` namespace avoids collisions with old unsaved adapter counters.9native Store cases passed, including fresh adapters after same-day serialization, real stock/cash/COGS and player/NPC parity.

The subsequent handoff/tool commit changes reporting only. Query `git rev-parse HEAD` for the final documentation checkpoint. No C# work remains uncommitted after this handoff is committed.

## Production evidence — do not conflate the runs

Raw evidence: `F:/LandLedgersEvidence/retail-cost-recovery/`. Console captures, disposable production saves, result summaries and focused-test results are retained outside Git. Owner save slots were not overwritten.

- Original completed60-day run remains `F:/LandLedgersEvidence/economy-60day-2026-11-10/`:837eaten/243missed/1080opportunities, four Bakery batches. It was not repeated.
- Seed189417423, market1545439901: a replay with stale Store bindings exposed purchases against detached stock. Its profitability/conservation results are invalid; do not present it as success.
- Corrected replay from the valid day7 save through31 (`binding-production/`):341eaten/217missed/558opportunities,0duplicate meal keys,0runtime errors/exceptions. First24-loaf wholesale lot L4 sold out with48c cost exhausted; another24-loaf lot L7 received with48c cost. Actual Save/Continue preserved business state.
- Short freight replay through11 (`handling-production/`):committed cargo delivered, `freightRevenueCollected=true`, transport binding released, no open freight-handling tasks,0runtime errors/exceptions. It is not proof of a second independent freight job.
- That31-day replay exposed shoppers reporting1–2work minutes despite serialized budgets being reset. Root cause: `WorkTimeBudgetStore.LoadFromSaveDto` replaced the list but its same-count lookup retained old budget objects.
- After `ca1907a0`, continued the existing31-day save to35 (`budget-production/`):42additional meals, cumulative383eaten/247missed/630opportunities. L7 sold all24loaves and its remaining cost reached0. Store recorded acquisition cost rose1266->1440c with0unknown-cost sales; business DTO state survived production Continue.0runtime errors/exceptions. Probe timings include startup/queued work and are not steady throughput benchmarks.
- Receipt-identity continuation35->40 (`receipt-identity-production/`):450eaten/270missed/720opportunities.14actual flour purchases: household cash-406c, business cash+406c, identified Store flour-14packages, recorded COGS+276c, saved retail receipt sequence14. Actual production Continue preserved business DTO state including the counter.0runtime errors/exceptions. This verifies the changed NPC receipt path; player business-creation UI itself was not PlayMode-tested.

The economy is still not sustainable. H1 has2c and no approved recurring income; do not grant funds. Other households previously had cash but could not shop because of the budget-cache bug. Raw flour still reaches the existing meal route without a complete preparation bridge. Horse-feed opening supply is finite; no approved standing replenishment quote was available.

## Priority checklist

- DONE for tested scope: paid daily receipts, known-lot COGS, receipt persistence, Store adapter/cash restoration, work-budget restoration, refused handling cleanup.
- SOURCE COMPLETE / UNITY EDITMODE TESTED: whole-lot Sawmill-to-Lumber-Yard handover conserves live quantities and rejects repeated source receipts. No live town caller exists yet; do not call it playable forestry. Payment and physical hauling remain caller responsibilities.
- NEXT P1: migrate remaining category-only weekly receipts/ordering and reserve-sale paths toward the existing exact-SKU daily procurement/trading system. Player and NPC daily restocking already use `StoreRestockDriver`. Do not order cheap sugar to satisfy a specific flour need; do not treat category stock as additional identified stock.
- NEXT P2: one genuine General Store freight request/receipt through current agreements, carrier, cargo and transport. Existing Millbrook production issuer is Bakery-specific. Do not change intentional return `deliverCargo:null` or reopen verified pickup without evidence.
- P3/P4/P5: integrate recovered Muse components selectively; then one real harvest/log haul/sawmill/yard/construction chain. Do not rebuild missing campaigns from their reports.
- P6: entrepreneurship is driven weekly, but production premises/equipment finders remain deliberately unbound. Bind only real rights/equipment; new owner autonomy approval does not supply capital or property.
- P7: meaningful POIs should reuse existing journey/building destinations. Horse lateral alignment, low/clipping driver seat remain deferred; do not disturb working travel.
- P8: use short affected transitions first. Another60-day run is not needed for each small repair. Prefer production Save/Continue plus dangerous-invariant regressions.

## Muse dependency — bounded investigation closed

Campaign A `fc73e8e8d21090784e439c7d2ddf557d50a87848` is absent from the common Git object database shared by the listed worktrees, checked remote refs, available bundles, Downloads and attachments. Owner confirmed no verified final A URL is available. Do not ask repeatedly or substitute the incomplete five-commit branch.

Campaign B is safely retained outside Unity:

`F:/LandLedgersRecovery/MuseBundles/forestry-construction-2026-11-10-final.bundle`

115966bytes; SHA256 `d88e0ecadfb85e028724a8d04a867fd65d504e6e13a9c865358e44623e87fa27`; header HEAD `346d8746ada91e4a824a89a47286b39744ce21e5`. Git bundle verification explicitly requires missing Campaign A. Neither campaign is integrated. When A arrives: verify its reported checksum/history, fetch review refs, compare semantic overlap, selectively integrate and Unity-verify.

Owner provisional forestry defaults:300kg usable green-log material per ordinary mature tree, existing450kg wagon capacity,10game-minute genuinely two-person loading. Existing higher-authority tree-specific rules take precedence. Do not adopt the600kg adapter assumption.

## Files and workflow notes

Run the read-only snapshot health tool without another Unity simulation:

```powershell
& 'F:/LLEcon/Reports/Get-TownHealthFromSave.ps1' -SavePath 'F:/LandLedgersEvidence/retail-cost-recovery/receipt-identity-production/day40-save.json' -OutputPath 'F:/LandLedgersEvidence/retail-cost-recovery/town-health-day40.json'
```

Validated on the actual35/40day saves:9local living residents versus63registered people,4real businesses,4households with bread/flour at35, formal employment records0versus3assigned workplaces, household retail ledger payments, SKU stock, needs, tasks, freight, parcels and construction. Do not call all non-employed people idle: ownership/workplace/activity and formal employment are separate. The report explicitly marks unmeasured rest, exceptions and stall causes rather than guessing. Source-save overwrite is refused. Cash totals alone are not global conservation proof.

Current source changes are in existing authorities and tests; no asset GUIDs or scene contents were changed during this recovery slice. Principal overlap areas: GeneralStore trading/receiving, BusinessRuntimeState/SaveDtos, procurement persistence, bootstrap restoration, SupplierDirectory, WorkTimeBudget, freight handling, lumber handover.

Pipeline commands must target `--project-path 'F:/Land & Ledgers'` explicitly. The source worktree can appear as a stale/non-Pipeline instance. Wait for compilation completion before starting tests. One test request made during compilation left a stale run; cancellation and reissue after compile completed successfully. Keep Unity active when background updates stall. Do not restart or repeatedly launch tests blindly.

No broad Roslyn harness was run here; historical Muse33failures were not reclassified. Financial UI text compiled but has not been visually verified in the business panel. No complete construction, smithing, property purchase or NPC business-formation gameplay proof is claimed.
