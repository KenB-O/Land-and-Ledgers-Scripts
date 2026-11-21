# Muse Integration Forensic Investigation — 2026-11-21

## Scope and conclusion

Read-only investigation of the local Unity project and retained November 21 Console evidence. No gameplay code, authored assets, scene configuration, Git history, or Unity runtime state was changed. No Playtest was started.

**Conclusion: the published Muse Pass is not integrated into the local HEAD.** The active Main Scene points to the expected `DevelopmentTownConfiguration` asset GUID, but that local asset contains a standing bakery-to-store agreement that the published Muse asset does not. The local bootstrap also retains the no-agreements fallback and has no initial bakery channel-policy resolution call. The exact 24-loaf/day, 4¢ transaction in the retained run matches the explicit local asset entry. The local fallback would produce a 24-loaf/day account at its separate fallback price if the configuration had no agreements; it does not explain this run's 4¢ price.

The source and asset mismatch is confirmed. The exact assembly/source revision that produced the retained run is **unresolved** because the run has no source SHA or assembly-hash manifest. The log's stack location and behavior match the local implementation, but that is not a cryptographic build provenance record.

## 1. Local Git provenance and working tree

| Field | Finding |
|---|---|
| Repository | `KenB-O/Land-and-Ledgers-Scripts` |
| Local branch | `codex/player-economic-participation-2026-11-18` |
| Local HEAD | `8537f52fb7dea263fa3ba62da6cbd4d6b5aefbf8` |
| Upstream tracking ref | `origin/codex/player-economic-participation-2026-11-18` at `7180a2f94ea1949146b502f274b8503107c7d404` |
| Published Muse ref | `muse/muse-pass-2026-11-20` at `0fc97de07465df7364316b4f318c6f172ef6a5ef` (confirmed by remote ref lookup) |
| Muse object locally available | No. `git cat-file` could not resolve the published Muse commit in the local object database. |
| Ancestry | Local HEAD descends from the Codex branch, not the published Muse commit. The Codex remote is the merge-base reported by the remote comparison; the Muse branch is 60 commits beyond it. |
| Working tree | Heavily modified and contains owner-local source, scene, asset, test, and generated/untracked work. No reset, cleanup, staging, or checkout was performed. |

The worktree is not a clean representation of HEAD. Its local file blobs include substantial changes to the bootstrapper, bakery runtime, simulation drivers, Main Scene, and town configuration. This means “local HEAD” and “current on-disk project” are distinct provenance descriptions.

### Critical local worktree file identities

Git blob IDs below are computed from the current worktree contents. SHA-256 values provide an independent content fingerprint. They are not the Muse commit's Git blob IDs.

| Local file | Worktree blob | SHA-256 | Published Muse Git blob |
|---|---|---|---|
| `Assets/Scripts/Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs` | `a2306ec5cd85b27dfafe565cdd9879bcf2d486aa` | `866ACBD6EF0E02A893048C11D5950386AC8B809D9C900186C2B0BB059019527A` | `95cda8733ab25d9fc9d8972e67eed61cd0d45915` |
| `Assets/Scripts/Domains/Population/DevelopmentTownConfiguration.cs` | `3adb50aac733b2df0824c036474577a91792e3c6` | `D8CA46C5E5D42AD3B2C7DE42C30376C34EB96EF6420E7744466404A48C97B0CA` | `9815fccbfaf6164634413e7b9a4f9e25c5166d79` |
| `Assets/Scripts/Domains/Economy/Equipment/ScenarioOpeningWholesaleAccounts.cs` | `6c4fc1e59843748c5f0eb1a8f0269a38157126fa` | `4D9ABAD76B1CFE2DE216A748DBCF972E497534EB2F4FDCC2DDCC5F877B048D9C` | `538227a956a98371d9074b620ac210986bb5d4ac` |
| `Assets/Scripts/Domains/Economy/Businesses/Bakery/BakeryShopRuntime.cs` | `0d698aac897ae298ac17d0f6ae02c9ec913b4e9c` | `CC7A954A8B38563D33A05AC0D540B2A958E483C66436B70D65D4A409AC3EA0DB` | `f81d21a6b3883498202700394b49fb77e2216c98` |
| `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs` | `030d2a0b5db7a3a97458ec01b6971d33f867c6ae` | `C68F60BBABF3785F627220C1BACF4A63B28E2D8D2CBAD4E3059D71E509A69F8A` | `6e32b366387467bbb68e8336e15732d0b2aee280` |
| `Assets/Core/Population/DevelopmentTownConfiguration.asset` | `90ab8e742485a0652387de2b8b415a299dee893a` | `5A28D7043F11B18888EFC567C9577993E88A8F477F24DCD61D6F9AA9B4957BB2` | `f0eece8817fb55d451790b24048ed20228188808` |
| `Assets/Main Scene.unity` | `e3961da9e4c951fe7720f8660b22832864c0bb7f` | `7FD5EBC066DD5BF08F2FE30D006A744F34B6D295CB6DC43F2EF42249C3F52753` | Not compared; this is a local scene/worktree file. |

The current worktree versions of `FirstLedgerSliceBootstrapper.cs` and `BakeryShopRuntime.cs` differ from both their HEAD blobs and the published Muse blobs. `DevelopmentTownConfiguration.cs` and `ScenarioOpeningWholesaleAccounts.cs` have the same blob as local HEAD, but their content differs from the published Muse commit. The serialized configuration asset and Main Scene are also modified from local HEAD.

The reported original Codex handoff is `6aa7f21d4c760e714c61dfff90ad00c458f600ca`; it is not the local HEAD. It is part of the older Codex lineage, while the local checkout has later Codex-branch commits and substantial working-tree edits. The Muse branch is not an ancestor of local HEAD.

## 2. Active Unity configuration identity

The active `Assets/Main Scene.unity` serializes the bootstrapper component with:

```yaml
enableDevelopmentTownConfiguration: 1
developmentTownConfiguration: {fileID: 11400000, guid: f5820dc040b04928a91079ae0c36b461, type: 2}
```

The referenced GUID belongs to `Assets/Core/Population/DevelopmentTownConfiguration.asset`. The published Muse commit's `.meta` for that asset has the same GUID (`f5820dc040b04928a91079ae0c36b461`). The scene therefore references the expected asset identity; it does **not** point to an alternate same-named asset. The content at that identity has diverged locally.

`FirstLedgerSliceBootstrapper.ResolveDevelopmentTownConfiguration()` returns the serialized field immediately when non-null. Its Editor `AssetDatabase.LoadAssetAtPath` fallback is only used when the serialized field is null. No runtime clone/copy is made by that resolver. The Main Scene's serialized reference is direct and enabled. No relevant Resources/Addressables lookup or competing scene override was found on this configuration path.

**Finding:** `CONFIRMED CONFIGURATION OR SERIALIZATION ERROR` for the active local asset contents, not for the asset GUID/reference. The scene references the right object; the object's local serialized bakery data is not the published Muse data.

## 3. Bakery order creation and startup order

### Local startup chain

1. The retained log records `[WorldStartup] Startup intent: NewGame` and the one-pass fresh-world startup path.
2. The local `FirstLedgerSliceBootstrapper` performs business opening inventory, bakery fuel, bakery inputs, and equipment setup, then calls `SeedBakeryWholesaleAccounts()` during the New Game path.
3. `SeedBakeryWholesaleAccounts()` resolves the enabled configuration, finds the bakery runtime and General Store, gets the Pass 9 saved-state/runtime holder, and delegates to `ScenarioOpeningWholesaleAccounts.TryApply(...)`.
4. `TryApply` reads the bakery's `opening.wholesaleAgreements`, validates the buyer, product, quantity and price, then calls `BakeryShopRuntime.AddWholesaleAccount` once. The existing endowment/account checks avoid duplicating an already-booked account.
5. **The local bootstrap does not call `ResolveBakeryInitialChannelPolicy()` or an equivalent configuration-to-runtime policy resolver.** That method is absent from local `FirstLedgerSliceBootstrapper`, local configuration source, and local `BakeryShopRuntime`.
6. The local runtime defaults `SupplyProtection` to `ProtectWholesaleContracts`; restoring a persisted runtime DTO restores its saved setting. The New Game bootstrap does not replace that default with a configured initial policy.
7. The daily bakery production/wholesale drivers consume the runtime account. No daily account-creation path was found. New Game seeds the account; Continue restores the existing runtime state and does not invoke this New Game seed path.

### Exact local account configuration

The active local asset contains the bakery record at `Assets/Core/Population/DevelopmentTownConfiguration.asset` around lines 1724–1817:

```yaml
wholesaleAgreements:
- buyerBusinessKey: general-store
  productId: bake.bread-loaf
  unitsPerDelivery: 24
  deliveryCadenceDays: 1
  pricePerUnitCents: 4
  freshness: 0
  firstDeliveryDayIndex: 0
```

The published Muse version of this asset has no bakery wholesale account; the seller's agreement list is empty. Its account class also adds a `supplyProtection` choice, which the local configuration class lacks.

### Separate fallback path in local source

Local `ScenarioOpeningWholesaleAccounts.TryApply()` has a whole-configuration `hasAnyEntry` check. When **no** opening wholesale agreement exists anywhere in the configuration, it creates a demand-derived fallback account: one 24-loaf batch each day at `FallbackPricePerUnitCents` (2¢ per loaf). This is a New Game fallback. It is not a daily re-creator and is not called on Continue. It is a separate local policy defect/risk because an empty asset alone would still yield an unwanted account under this source.

The published Muse `ScenarioOpeningWholesaleAccounts` explicitly removes that fallback: if no seller account is configured, it returns “No wholesale accounts are configured … nothing was booked.”

### Published Muse ordering and scope of policy

The published Muse bootstrap calls `SeedBakeryWholesaleAccounts()` and then `ResolveBakeryInitialChannelPolicy()` on New Game. Its bakery runtime resolves the configured `OpeningBakerySupplyProtection` (including decision history) after accounts are seeded. This policy is a **production-allocation preference** (`ProtectRetailCounter`, `ProtectWholesaleContracts`, or proportional sharing); it does not itself erase an already-booked wholesale obligation. With an explicit account plus `ProtectRetailCounter`, retail gets production priority, but a configured wholesale contract remains a valid obligation. The Muse sample config avoids an undesired agreement by having no bakery account, and its no-fallback rule preserves that choice.

**Exact source condition responsible for the observed 24 loaf / 4¢ agreement:** the active local serialized `bakery.opening.wholesaleAgreements` entry above, read and booked by the local New Game `SeedBakeryWholesaleAccounts()` → `ScenarioOpeningWholesaleAccounts.TryApply()` path. The local fallback is **not** responsible for the observed 4¢ price; its configured fallback price is 2¢.

## 4. Runtime evidence and first divergence

The retained `C:\Users\kenne\AppData\Local\Unity\Editor\Editor.log` contains:

- New Game startup at log line 1059.
- Direct bakery seller registration for `Glen Pierce Bakery (bakery_001)` at line 1492.
- Booking of `bakery_001-wholesale-0`, 24 `bake.bread-loaf` every day to `general_store_000` at 4¢/unit, first due day 0, at line 2620. The nearby stack trace names `FirstLedgerSliceBootstrapper:SeedBakeryWholesaleAccounts`.
- General Store supplier diagnostics at line 2712: `ItemStockUnits(bread)=27`, `ItemStockUnits(flour)=8`, `CategoryStockUnits(staple_food)=90`.
- Day 1 wholesale production at line 3564: one account, one batch, one transfer, 24 units stocked.
- Day 1 detail at line 3580: 96¢ settled from the General Store to the bakery; 24 units of SKU `bread` were accepted into the live `staple_food` category; bakery retail sales were 0 units/0¢ and wholesale revenue was 96¢. The retail planner stated there had been no recorded sale history and no open household bread need when it made its day-one plan.
- Later retained day records show continued wholesale settlements and zero actual retail sales. Day 19 also reports stale bread written off and household need evidence. This sequence is consistent with the user's day-19 capture.

The first source/configuration divergence occurs **before** daily production: the active local config supplies a standing agreement absent from the published Muse asset; the local bootstrap books it; the local integration then lacks the Muse initial channel-policy resolution step. On day 1 the wholesale driver fulfills that account and settles the configured cash transfer. The direct-seller registration is a separate capability registration; its presence does not suppress a standing wholesale agreement.

The broader household meal deterioration has multiple possible causes. These logs establish a bakery allocation/stocking discrepancy and zero bakery retail sales, but they do not prove that this alone caused all missed meals or every counter-service failure. That remains for a later gameplay investigation.

## 5. SKU and `staple_food` provenance

The bakery contract carries product identity `bake.bread-loaf`. The local mapping/wholesale path translates this to store SKU `bread` while retaining the catalog product ID in the wholesale transfer record. The driver can call the item-level receiving port with both `productId` and category `staple_food`; the category is a broader shelf/category pool, not itself proof that the SKU was discarded.

The observed startup diagnostics expose item-level stock (`bread`, `flour`) alongside category totals. Day 1 explicitly says `stocked 24u 'bread' into live category 'staple_food'`. Thus, the category total is at least an aggregate/category projection while the log retains the distinct `bread` item. The available runtime log does not print the receiving-port capability flag, so the exact selected method overload cannot be proven from that line alone. It does provide positive evidence that the received goods were reported as `bread`, not solely as anonymous category stock.

**Finding:** `INTENDED BEHAVIOR` for a category projection coexisting with exact SKU stock, based on current evidence. The Muse SKU code is not integrated as a revision, but the local branch already has some item-level stock/mapping behavior. No conclusion that SKU identity was lost is supported by the capture.

## 6. Unity assembly and PlayMode provenance

- Unity Editor 6000.4.2f1 was open and responsive; current process PID 25468 started at 07:50:53 on November 21. Current Main Scene is open in Edit Mode. The day-19 log is retained Console history; it is not a running simulation now.
- Unity ScriptAssemblies timestamps observed: `Assembly-CSharp.dll` 07:39:08 and `Assembly-CSharp-Editor.dll` 07:40:09. The Editor log contains a script compilation/reload event and no `error CS` lines in the inspected compile entries. This establishes that Unity had loadable assemblies; it does **not** identify their Git source SHA.
- Current on-disk assembly fingerprints: `Assembly-CSharp.dll` (8,967,168 bytes, SHA-256 `294A1CFF4E66C80E6B8A63AFC09B62DEB8882B5EEFC6F2BAE607D34B627BDABF`) and `Assembly-CSharp-Editor.dll` (8,089,696 bytes, SHA-256 `1E5D107C9A09828DD45043E426F73C8CE2CC703BEEAC581490C4F1EA968A91CB`). No build manifest maps these binaries to Git source or to the retained run.
- The captured run has no source SHA, assembly hash, build manifest, or per-run configuration hash. Its log stack/method names and the 4¢ account output align with the current local source and active config, but this is not an exact binary provenance proof.
- The current Console screenshot showed one asset-load error for `Assets/Environment/Buildings/Building Animations/Single Story Business Building White Plaster Animation 1.anim`, along with many warnings/information entries. This is not a C# compiler error and is outside the cause of the bakery discrepancy. It remains an unrelated observed Editor issue.
- No assembly reload, test, or PlayMode run was triggered for this investigation; owner testing was not interrupted.

**Run classification:** `UNRESOLVED` with respect to exact compiled revision. The retained capture is not proven stale, and it is not proven to have run Muse commit `0fc97de...`. Calling it “Muse runtime” would be unsupported.

## 7. Focused Muse component identity audit

“Present in local source” below means the local worktree, not the published Muse branch. “Compiled” means only that Unity had loadable assemblies and no compiler diagnostics were present in the inspected log portion; it does not certify that every current dirty file was included in those exact assemblies. “Observed” refers to the retained run, not a newly executed test.

| Muse component | Local source evidence | Unity/runtime evidence | Status |
|---|---|---|---|
| Shared incident warning authority | No shared incident-core/window implementation matching the Muse pass was found in the local source inventory. The older `LogRefusalDayKeyed` helper exists in `SimulationDrivers` and emits `Debug.LogWarning` once per key/day; it is not itself proof of shared incident records. | The retained capture contains ordinary Console logs; no structured incident IDs or authority export is evidenced. | Muse core not integrated; runtime incidence not verified. |
| NPC refusal funnel | Local `SimulationDrivers.LogRefusalDayKeyed` exists and has numerous call sites. | Refusal messages in Console can be seen, but the capture does not establish Muse's bounded incident funnel, stable signature identities, or incident recovery navigation. | Local predecessor/pattern present; Muse incident wiring not verified. |
| A-to-Z audit wiring | No `EvaluateRunChecks` / equivalent Scenario Lab invocation was found in local source. | No audit output in the retained session establishes an A-to-Z run. | Not integrated / not observed. |
| Exterior temperature and heating | No matching Muse exterior-temperature authority was found by focused local source search. Household fuel/heating-related source exists in the older simulation. | The retained evidence provided here does not show Muse exterior-temperature event records. | Muse component not found locally; runtime not verified. |
| Household Firewood fallback | Local domestic labor carries `FirewoodActivityLocationAvailable` and retains a capability-blocked firewood task while an authored location is absent. | This source path is not evidence that the Muse-specific household acquisition/fallback event system was compiled or observed. | Related local capability-wait behavior present; Muse feature not verified. |
| Furniture staged-delivery default | Local purchase driver constructor has `bool stagedDelivery = false`; no wired `stagedDelivery = true` call was found in the focused orchestration/runtime search. | No freight/install observation is relevant to the retained bakery run. | Muse default not integrated in local source; runtime not verified. |
| Bakery channel policy | Local runtime supports supply-protection branches, but its default is `ProtectWholesaleContracts`; local config lacks `supplyProtection`; bootstrap does not resolve an initial policy. | The retained run books and fulfills the wholesale account; it does not establish that a Muse initial policy ran. | Partial predecessor mechanics; Muse policy integration absent. |
| SKU identity | Local bakery product mapping and item stock diagnostics exist. | Day 1 reports the `bread` item SKU stocked under category `staple_food`. | Some exact SKU behavior is observed; Muse commit-level identity is not integrated. |
| Scenario Lab | No Muse Scenario Lab implementation was found in the local source inventory. | No Scenario Lab execution appears in the retained run evidence. | Not integrated / not observed. |
| Muse diagnostic Editor windows | Local `Assets/Scripts/Editor/Diagnostics` contains `NpcActivityMonitorWindow`; no inventory of the twelve Muse tools or their menu paths was found. | No evidence these tools were opened or exercised in the current Editor. | Muse diagnostic suite not integrated / not validated. |

## 8. Findings classified

| Finding | Classification | Evidence and limit |
|---|---|---|
| Published Muse commit is absent from local HEAD ancestry and local object database. | **CONFIRMED LOCAL INTEGRATION ERROR** | Local branch/HEAD, remote Muse ref, ancestry comparison, and differing source blobs. |
| Active scene uses the expected configuration GUID but local asset content includes the 24/day, 4¢ agreement absent from the published asset. | **CONFIRMED CONFIGURATION OR SERIALIZATION ERROR** | Direct serialized scene pointer; matching asset GUID; explicit local YAML entry; published asset differs. |
| Local no-agreements fallback would create 24/day at 2¢ if all config agreement lists are empty. | **CONFIRMED LOCAL INTEGRATION ERROR** | Local `TryApply` fallback condition and fallback price; published Muse implementation removes this fallback. This was not the source of the observed 4¢ agreement. |
| Local bootstrap does not resolve the configured initial bakery channel policy after seeding. | **CONFIRMED LOCAL INTEGRATION ERROR** | Method/call absent locally; published Muse bootstrap contains the call after seeding. |
| A direct bakery counter seller and a wholesale contract can coexist. | **INTENDED BEHAVIOR** | Seller registration and wholesale contract are separate authorities. Muse policy prioritizes allocation; it does not cancel a configured contract. The Muse sample config's empty account list is what prevents the unwanted contract. |
| `staple_food` means the observed bread lost its SKU. | **INTENDED BEHAVIOR** (no loss established) | Day 1 records `bread` received under `staple_food`; item-level metrics coexist with category totals. Exact receiving overload flag is not logged. |
| Retained day-19 runtime used Muse commit `0fc97de...`. | **UNRESOLVED** | No source/build/config hash or run metadata in the captured session. Do not assert either Muse or stale source provenance. |
| Missed meals and shopping failures were caused solely by bakery wholesale. | **UNRESOLVED** | Wholesale behavior and zero retail sales are proven; the complete household causal chain is not. |
| Published Muse source itself contains the observed 24@4¢ configuration. | **Not supported**; no Muse defect confirmed | Published asset is empty for bakery wholesale; the observed exact values match the local asset. Independent Muse reinspection is still recommended. |

## 9. Recommended correction sequence (not performed)

1. Preserve this forensic state and compare with Muse's independent finding before changing files.
2. On the approved integration branch, reconcile the relevant Muse code and asset deliberately rather than replacing owner-local scene/config edits wholesale. Confirm intended bakery opening agreements first.
3. Port/verify the no-implicit-wholesale fallback behavior and initial policy-resolution wiring as a coherent change; preserve any explicit authored contracts as obligations and do not silently cancel them through a policy choice.
4. Verify active serialized asset content and the New Game versus Continue paths separately. Add a run/build provenance record before future live comparisons.
5. Only then investigate the observed day-7–19 household food decline, counter service, and the unrelated animation asset error as separate issues, using causal evidence rather than assuming the bakery discrepancy explains all symptoms.

No correction is made here, and no gameplay test was run.

## 10. Specific questions for Muse's independent review

1. Confirm the published `muse/muse-pass-2026-11-20` commit and exact source/asset blobs used in Muse's inspection.
2. Confirm that the bakery list is empty in the published `DevelopmentTownConfiguration.asset` and that the no-agreement branch books no fallback account.
3. Explain why `SeedBakeryWholesaleAccounts()` precedes `ResolveBakeryInitialChannelPolicy()` in the published bootstrap. Confirm policy is allocation priority and does not cancel standing accounts.
4. Confirm the desired handling of the local explicit General Store agreement (24 loaves/day at 4¢) when reconciling the dirty owner-local asset. This investigation does not authorize removing/canceling it.
5. Confirm the exact item-level store intake path and whether `staple_food` is an aggregate/category projection for the day-1 transfer.
6. Check whether Muse's build/session metadata can establish the source revision that produced the user's retained November 21 run; no such marker was found in the local captured logs.
7. Independently classify the bakery divergence as local unintegrated source/configuration versus any defect still present in the published Muse implementation.

## Evidence locations

- Local scene: `Assets/Main Scene.unity` (bootstrapper serialized configuration pointer).
- Local configuration: `Assets/Core/Population/DevelopmentTownConfiguration.asset` (bakery opening agreement).
- Local source: `Assets/Scripts/Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs`.
- Local source: `Assets/Scripts/Domains/Population/DevelopmentTownConfiguration.cs`.
- Local source: `Assets/Scripts/Domains/Economy/Equipment/ScenarioOpeningWholesaleAccounts.cs`.
- Local source: `Assets/Scripts/Domains/Economy/Businesses/Bakery/BakeryShopRuntime.cs`.
- Local source: `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs`.
- Retained Unity log: `%LOCALAPPDATA%\Unity\Editor\Editor.log` (captured session excerpt described above).
- Published reference: `https://github.com/KenB-O/Land-and-Ledgers-Scripts/tree/0fc97de07465df7364316b4f318c6f172ef6a5ef`.
