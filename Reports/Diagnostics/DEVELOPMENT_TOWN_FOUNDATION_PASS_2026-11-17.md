# Development Town Foundation Pass — 2026-11-17

## Result

The three development-town assets now use schema 2, with residents, households, businesses and business-owned opening resources organized beneath their owning records. Unity serialized the assets, reloaded them and validated them successfully. The authoritative asset retains its existing GUID (`f5820dc040b04928a91079ae0c36b461`).

The owner-authored local asset was treated as authoritative. Its current 14-person roster and eight households were preserved; no removed resident was restored. Richard Sithole retains runtime person ID 10, and the Millward household has Don and Grace exactly once. Enzo Kwindt owns the sawmill; its two opening employees are Richard Sithole and Josh Saunders. Shane Bryan remains a resident but is no longer described or registered as a sawmill employee.

## Serialization investigation

The previous discrepancy (10/6/1 loaded from the old Git asset versus 15/8/3 after removing human-written YAML comments from that copy) was reproduced in a controlled probe. Removing only those unsupported comment lines yielded the expected old-baseline values. The current owner-local configuration had no such comment lines before this migration, so the probe confirms the hazard but does not attribute every prior count discrepancy to the comments.

Unity Editor serialization APIs were used for migration and save. The authoritative asset was reloaded and read back as 14 residents, eight households and seven businesses, with valid business-owner references. The medium and large configurations were also read back as schema 2 and validated. Their existing asset GUIDs were retained.

The pre-migration owner asset backup is at `Reports/Diagnostics/Backups/DevelopmentTownConfiguration.20261117-pre-schema.asset.backup` (SHA256 `F368381E0CC3B56BB86935241FB54DAEF8F346612A4E7707A932BEA88BC0E98D`). Its matching meta backup SHA256 is `16D900F4EDDAAAD42DED9604A18C64CDF200C23FA467EE3137A77DFB03A46E89`.

The byte-exact backup and its meta backup are retained in the local project but excluded from Git publication. The backup file's original Unity YAML whitespace trips `git diff --check`; preserving its exact bytes is preferable to normalizing the recovery copy.

The invalid pre-migration business record was the sawmill: it named owner key `enzo`, but the local resident list had no Enzo record. Its opening employee also referenced `silas`, which did not match the authored Richard Sithole resident key. Validation therefore rejected the configuration. The migration adds the owner and Josh as residents, maps the prior worker reference to Richard, and uses the owner's direction to configure Richard and Josh as the two workers.

## Data organization and reconciliation

- Resident and household identity keys are readable and stable. Runtime IDs remain explicit, preserving the established person and household IDs; no identity is derived from list order.
- Household opening cash is stored once on each household: eight allocations of 40,000 cents (320,000 cents total), preserving the existing authorized targets.
- Business opening cash is stored once on each business: General Store 50,000; Bakery 35,000; Fuel Dealer 20,000; Freight 20,000; Sawmill 15,000; Lumber Yard 12,000; Carpentry Workshop 10,000 cents.
- Opening inventory, equipment, employees, supplies, transport identifiers, spare blades and wholesale agreements are nested under their business. Existing finite quantities and equipment identifiers were retained.
- The Kwindt household contains Enzo and Josh. The Millward household contains Don and Grace. Millward retains the existing 40,000-cent household opening allocation.
- The sawmill has Enzo as owner and Richard (logger, 750 cents/week) plus Josh (labourer, 750 cents/week) as its two opening employees. No third employee or additional wage obligation was added.

The tests found that the legacy `spendingMoneyCents` compatibility field was receiving a 250-cent minimum after the configured 40,000-cent ledger grant. The manager then swept that legacy amount into the ledger, producing 40,250 cents. Development-town generation now clears that retired compatibility wallet after income calculation, leaving the configured ledger grant at exactly 40,000 cents. Normal generated-population cash handling was not changed.

## Migration artifacts

- `Assets/Scripts/Editor/Population/DevelopmentTownConfigurationMigration.cs` performs the guarded, versioned migration using `SerializedObject`, validates before saving, reimports and reloads, and writes before/after snapshots.
- The one-time migration is idempotent: schema-2 configurations return without changing the asset.
- Migration read-back records are in `Reports/Diagnostics/DevelopmentTownConfigurationMigration-2026-11-17.json` and the corresponding Medium/Large schema migration JSON files.
- The controlled YAML-comment probe and Unity-loaded serialization probe are retained in `Reports/Diagnostics/DevelopmentTownYamlCommentProbe.txt` and `Reports/Diagnostics/DevelopmentTownSerializationProbe.json`.

## Verification

- Unity 6000.4.2f1 is open on the original project in Edit Mode. Main Scene was not intentionally modified by this pass. It was already owner-modified in the working tree; the Unity test runner restored it after its test run.
- Latest Unity script compilation: completed, zero compiler errors.
- Focused EditMode configuration/validation suite: 8 passed, 0 failed.
- Focused EditMode population-generation suite: 7 passed, 0 failed. It covers configured IDs and memberships, opening-cash idempotence, save/load identity round-trip, and refusal before partial population when homes are insufficient, as well as the retained population-authority checks.
- Focused EditMode serialized-authorship suite: 3 passed, 0 failed.
- Total focused EditMode evidence: 18 passed, 0 failed.
- One PlayMode test was attempted against the actual Main Scene. The Unity Test Runner reported failure because scene restoration emitted an unhandled error: `Failed to load 'Assets/Environment/Buildings/Building Animations/Single Story Business Building White Plaster Animation 1.anim'. File may be corrupted or was serialized with a newer version of Unity.` The stack points into `RestoreSceneSetupTask`; the run is therefore inconclusive for New Game startup, and this report does not claim a successful live startup. The commercial animation asset was not changed.
- The full 8,083-test inventory was not run. The previous 162 Unity failures remain unclassified in this pass.
- The startup validator now reports invalid businesses with the failing business key and field. The current three configurations pass the validator. Runtime New Game initialization remains unverified because the PlayMode run did not complete cleanly.

## Foundation observations and follow-up

The owner-organized serialized schema removes flat duplicate cash/resource authoring. A small set of production consumers still reads deprecated computed compatibility views (`BusinessCashTargets`, `HouseholdCashTargets`, `OpeningInventory`, `OpeningEquipment`, and `OpeningEmployees`). These views derive from the new owner records and are not serialized sources of truth, but should be replaced by direct owner-record iteration in a later focused cleanup.

The existing business architecture remains intentionally unchanged: `BusinessType`, `BusinessProfileDefinition`, `GenericBusinessConfiguration`, `SharedBusinessRuntimeManager`, and specialized business runtimes remain in place. The next campaign may assess the approved universal NPC/player business direction, but should preserve one business identity, inventory, premises, equipment and contract state during an ownership transition. No universal-business migration was implemented here.

## Files changed by this pass

- `Assets/Core/Population/DevelopmentTownConfiguration.asset`
- `Assets/Core/Population/MediumDevelopmentTownConfiguration.asset`
- `Assets/Core/Population/LargeDevelopmentTownConfiguration.asset`
- `Assets/Scripts/Domains/Population/DevelopmentTownConfiguration.cs`
- `Assets/Scripts/Domains/Population/PopulationGenerator.cs`
- `Assets/Scripts/Editor/Population/DevelopmentTownConfigurationMigration.cs` and `.meta`
- `Assets/Scripts/Tests/Editor/Population/DevelopmentTownConfigurationTests.cs`
- `Assets/Scripts/Tests/Editor/Population/DevelopmentTownConfigurationValidationTests.cs`
- `Assets/Scripts/Tests/Editor/Population/DevelopmentTownPopulationGenerationTests.cs`
- `Assets/Scripts/Tests/Editor/Population/SawmillAuthorshipC8Tests.cs`
- `Assets/Scripts/Tests/PlayMode/PlayableScenarioStartupPlayModeTests.cs`
- The migration snapshots, serialization probes and protected backup listed above.

Owner-local scene, prefab, terrain, font and asset-pack modifications were preserved and excluded from this pass's change set.

## Publication

The scoped checkpoint is published on `codex/development-town-foundation-2026-11-17` at `3ff1d0909f4d6d683f8620ad0d4a14deac4ead0e`. The GitHub remote branch SHA was queried after push and matched this commit. The local pre-migration backup remains available only in the authoritative project workspace.
