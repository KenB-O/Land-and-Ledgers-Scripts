# Muse–Codex semantic integration checkpoint

## Recovery and source history

The isolated Unity project is `F:\LandLedgers_SemanticIntegration_2026-11-21` on `codex/muse-semantic-integration-2026-11-21`. Its merge checkpoint is `10e7fe2234c516850a47b38e44c60788095d5719`. Git confirmed ancestry from both published histories.

| Source | Commit |
|---|---|
| Original owner checkout HEAD | `8537f52fb7dea263fa3ba62da6cbd4d6b5aefbf8` |
| Published Codex handoff (includes the four local commits and publishable work) | `6aa7f21d4c760e714c61dfff90ad00c458f600ca` |
| Published Muse Pass (exact ref fetched into the isolated project) | `0fc97de07465df7364316b4f318c6f172ef6a5ef` |
| Verified common ancestor | `49e9c82fd5b6b4c01be5a482ab6e8c53e83a5a9c` |
| Existing Muse integration checkpoint located during recovery | `68cfcd143e1f0c1cce13e176e3f6194f40320f69` |
| Merge parent with unpublished Muse work | `ce2bf7e99459ca6ca79ff259a1f20f573e003ba3` |
| Merge parent with unpublished Codex work | `e9420106a1db5c02fe7e8dc0ed0d8f9245c763c4` |

The original repository's `git fsck` reported corrupt loose objects. The recovery copy at `F:\LandLedgers_SemanticRecovery_20261121` contains the original project (120,057 files, 50.699 GiB), the existing Muse integration worktree (6,420 files, 1.595 GiB), the healthy common Git database, and the retained owner Editor log. Robocopy reported zero failed files for both project copies. The healthy history passed Git connectivity checks. Cache and build directories `Library`, `Library_recovery_20261114`, `Temp`, `obj`, `obj_verify`, `bin_verify`, and `.vs` remain in the original location. The linked Muse worktree's Git common directory is backed up separately because its .git file points outside the worktree.

Recovery also holds premerge, SHA-256-verified snapshots of 28 Codex files and 72 Muse follow-up files. The recovery commits preserve their origins. Separate normalization commits restored repository line endings before the merge. The original project and existing Muse workspace were treated as read-only. The original damaged animation is preserved in recovery. The merged project carries the valid Unity YAML clip from the handoff with its existing .meta GUID. The owner version of .gitattributes is also preserved in recovery. The integration retains the handoff's broader Git diff settings. The owner-local AI Assistant settings file and files excluded by the published handoff manifest remain local. Neither historical branch nor main was changed at the remote.

See [three-way path inventory](MUSE_CODEX_THREE_WAY_PATHS_2026-11-21.csv) for all 1,038 tracked paths observed across the comparison legs. The inventory includes the 799 changes from owner HEAD to the common implementation checkpoint, which are shared ancestry, 245 paths changed in published Muse, two publication-only Codex paths, 27 owner changes relative to the published handoff, and the committed and uncommitted Muse follow-ups. The comparison found 15 overlapping tracked paths after the common checkpoint and four textual conflicts in the merge. Untracked source relative to the original Codex index was checked again against the published handoff. Only the machine-local AI Assistant setting remained outside that handoff among the scanned script and project settings paths.

## Semantic decisions

| Area | Resolution and evidence required |
|---|---|
| Physical furniture installation | Retained Muse's `FurnishingInstallTaskDriver` and the later reachable placement approach. Removed the superseded Codex warning branch that said there was no install-task binding. Both implementations prevent immediate installation on receipt. Verify a physical arrival, task completion, and exact placement in Main Scene. |
| Fuel and heating | Kept temperature-driven burn and sleep-outcome verification together with Codex's stove and inventory custody accounting. Preserved both regression pairs. The stock tests use the same explicit cold-day input as the thermal tests. Verify actual fuel debit, return, and Save/Continue. |
| Movement | Retained Muse's logistics integration and un-hide behavior alongside Codex's invariant route coordinates and route failure evidence. Verify approach and return in Main Scene. |
| Freight and bakery inputs | Retained Codex's physical shipment lookup for flour coverage alongside Muse's bakery policy and task-backed retail production. Terminal paid flour must not count as deliverable input. Verify focused freight and bakery suites, then Main Scene receipt. |
| Bakery opening | Preserved explicit Retail-only and retail-protection settings for Glenn in the development town. The asset has an empty bakery wholesale agreement list. The bootstrap resolves the sales channels before seeding explicitly authored contracts. The legacy automatic fallback is absent. Existing accepted saved contracts retain their obligations. Main Scene references the config GUID `f5820dc040b04928a91079ae0c36b461` in source. A fresh startup report and the actual loaded object still require verification. |
| SKU settlement | Preserved identified lot receipt and exact item sale routes. The category settlement and consumption paths now share the same remaining unidentified stock check. Identified or reserved units cannot fund a category-only sale. `Pass12CSettledSaleStockTests.SettledSale_IdentifiedStockCannotBeSoldThroughCategorySettlement` checks refusal, cash, legacy stock, and later exact lot consumption. The category reporting projection remains in use. |
| Historical diagnostics | The Incident Investigator displays live bakery accounts and active policy when a runtime is connected. The Economy Integrity Auditor reads the current project asset and excludes historical fixture findings from its current-source audit. Historical cases remain available under a separate command. Verify both states inside Unity. |
| Counter wait incidents | Terminal waits now emit the same incident signature for deadline expiry and queue balk. The source preserves the actual last availability refusal and records trip, household, shopper, seller, location, deadline and any service task. The production driver feeds the shared authority. The existing absent-clerk regression now checks a single notification and continued refusal to settle. Actual service routing and clerk arrival remain separate work. |
| Issue register | Preserved both appendices from the conflict. The [running checklist](CONSOLE_INVESTIGATION_CHECKLIST_2026-11-19.md), [Muse reconstruction note](MUSE_REGISTER_RECOVERY_2026-11-21.md), and [354-signature reconstruction](MUSE_CI_APPENDIX_A_RECONSTRUCTION_2026-11-21.csv) carry accessible issue identities and provenance. The original 5,798-line Muse register and the two referenced Muse PDFs were not found among accessible attachments. No missing records were inferred. |

## Verification and outstanding work

Git ancestry, connectivity, merge conflict checks, and whitespace checks passed. A Roslyn syntax check of 184 changed C# files passed before semantic compilation. The standalone compile at source SHA `56ede6ed21da7dc8b55c6b7a165059e2c727ecfa` used Unity 6000.4.2f1's compiler and the existing Editor's generated reference lists, remapped to the isolated project's source files. The runtime assembly finished with 0 errors and 45 warnings. The Editor assembly finished with 0 errors and 118 warnings. Corrections covered the timing-observer interface, Unity's EntityId collision, a shadowed bootstrap variable, task ID formatting, stale Editor API and enum names, and fixture compile errors. Two identical bakery gate test definitions were consolidated into the existing stronger definition, which retains both behaviors and checks the refusal reason.

This check did not exercise Unity's asset import, independent assembly discovery, or PlayMode. The running Editor remained on the owner project. See the [Unity verification backlog](MUSE_UNITY_VERIFICATION_BACKLOG_2026-11-21.md) for named fixtures and the older checkpoint's evidence boundary.

### Standalone focused regression evidence

The compiled fixtures below ran through Unity's bundled Mono as standalone managed code, with NUnit assertions and without Unity Editor or Test Runner services. Full method names, results, and failures are in `F:\LandLedgers_SemanticRecovery_20261121\ReferenceCompile`.

| Fixture or filter | Passed | Blocked by native Unity dependency |
|---|---:|---:|
| `Pass12CSettledSaleStockTests`, including the F-2 mixed-stock case | 6 | 0 |
| `FreightServiceAgreementTests` | 19 | 7 |
| `ExteriorTemperatureTests` | 8 | 0 |
| `HouseholdThermalModelTests` | 7 | 0 |
| `SawmillToYardTransferDriverTests.F7_*` | 3 | 0 |
| `DomesticLaborDriverTests.MissingFirewoodAnchor_*` | 3 | 0 |
| `ShoppingTripDriverTests.RequiredPhysicalCounterService_AbsentClerkEventuallyReturnsWithoutPurchase` | 1 | 0 |
| `IncidentCoreAdapterTests` | 15 | 0 |
| `Lane10aBakeryChannelChoiceTests` selected tests for no implicit contract, channel permissions, existing contract DTO round-trip and policy evaluator | 13 | 0 |
| Total | 75 | 7 |

The seven freight cases reached an unavailable native `JsonUtility.ToJsonInternal_Injected` call during save capture in standalone Mono. The full freight fixture remains pending in the actual Unity Editor. The G3 default-driver test, Unity JSON persistence tests, and Main Scene tests were not executed.

Source checkpoint `f6ecc9d5d2fe1e30171b9486c0b4b60d031ba9f8` adds Playtest Runner provenance. The runner reads the bootstrapper's serialized configuration before falling back to the same Editor asset path. New runs capture its GUID, disk hash and dirty state, loaded assembly module IDs and file hashes, tracked Git status, and startup intent and configuration GUID during startup. These fingerprints identify the objects and binaries observed and do not assert a source-to-binary mapping.

The owner's original Unity 6000.4.2f1 Editor remains open. Windows reported as little as 366,520 KiB free physical memory during this session and around 5.8–6.2 GiB free commit. The isolated project requires a fresh import. Unity compilation, Test Runner, diagnostic windows, PlayMode startup, physical shopping and installation, and extended runs are pending. There is no new runtime economy or sustainability result. The previous day-19 run and earlier 5/60-day evidence remain historical.

| Issue | Status at this checkpoint | Next required action |
|---|---|---|
| Bakery opening discrepancy / F-5 | IMPLEMENTED UNVERIFIED | Run fresh New Game, record active config GUID/hash, no unwanted agreement, selected channel policy, first real demand and production. Verify Continue retains legitimate obligations. Evaluate R4 and shipping economics with actual quotes. |
| F-2 category settlement bypass | IMPLEMENTED UNVERIFIED | Run current-source stock/cash tests, including old saves with mixed identified and unidentified stock. Review the category reporting projection when applying F-3. |
| F-3 shared staple capacity | UNRESOLVED | Assign received units against exact SKU headroom across direct stocked lots, paid procurement and the bakery receiving shelf. Preserve unknown legacy identity and distinguish reorder targets from physical storage capacity. |
| F-4 stale bakery findings | IMPLEMENTED UNVERIFIED | Verify the two Editor windows with no live game and after a current-source New Game. |
| F-6 counter wait and physical service | IMPLEMENTED UNVERIFIED for terminal incident feed. UNRESOLVED for physical clerk approach and service completion. | Run the timeout regression and trace actual worker route, task ownership, queue wait, settlement and return in Main Scene. The existing counter callback remains intentionally unbound until clerk routing can establish presence. |
| DEF-046 duplicate production consumer regression | UNRESOLVED | Run the original fixture. Preserve its original expectation and investigate actual production authority usage. The merge did not change its assertion. |
| Furniture paid failed-delivery disposition | BLOCKED for commercial policy | Preserve recorded cargo, pay and obligations pending ownership, return and refund terms. Verify unaffected lifecycle. |
| Spillover expectations and Scenario Lab acceptance bars | OWNER DECISION PENDING | Preserve existing test expectations and report provisional scenario outcomes. |

Next verification should be a Unity import and compile on the isolated project after the owner Editor is safely closed. Follow with focused regressions, then New Game seed 1886 bakery initialization and physical service at observation speed. Record branch and full SHA, dirty files, Unity version, config GUID and hash, compiled assembly fingerprints, seed, speed and wall time before collecting evidence. Advance to longer tests after short runs demonstrate actual household replenishment and movement. Publish the dedicated branch after its checkpoint is verified and reviewed.
