# Pass VII Workstream C: Estate Administration and Dependent Continuity

## What already existed

Death terminated employment (Pass VI fix) and opened an estate via `EstateService.OpenEstate` from the mortality path. `EstateService` could register decedent parcels, compute intestate shares, appoint executors, distribute parcels, and close estates. `Will`, `WillContest`, and `EstateAdministration` existed as source with no production callers. Pass VI (P1-2) confirmed the provider-death chain stopped at estate opening: the widow could not inherit, businesses were effectively immortal, and no runtime driver advanced an estate through its lifecycle.

## What was disconnected

Estate stages existed but nothing drove them. `RegisterDecedentParcel` had no callers. Business ownership was `OwnerKind`-keyed, never person-keyed, so ownership could not transfer to a real heir. The AutonomousSocietyDriver contained only a scripted demo (`OpenAndSettleEstate` with hardcoded heir and parcel), not a general driver.

## What was changed

Runtime estate lifecycle driver (worker 3, merge 22c6c77):
- `EstateLifecycleDriver` (Domains/Economy/Estates/EstateLifecycleDriver.cs) advances estates through the full lifecycle: Opened to Inventoried to ClaimsAdjudicated to Distributing to Closed.
- `InventoryEstate`: identifies estate assets with valid provenance (cash, deposits, real property, business interests, equipment, livestock, vehicles, inventory, receivables, personal possessions).
- `AdjudicateClaims`: processes legitimate obligations (loans, unpaid rent, supplier accounts, secured obligations, taxes). Secured creditors are ranked before unsecured. Valid claims affect distribution.
- `DistributeEstate`: transfers property through the ordinary `TitleAuthority`, settles debts through `FinancialObligationAuthority`, distributes to heirs without duplicating assets. Multiple heirs handled without duplicate assets. Sole-heir succession works.
- Insolvent estates invent no funds. Beneficiaries are not personally liable for estate debts. Minor dependents preserve their legal interests through custodial representation.
- Businesses owned by the deceased are handled consistently: ownership transfers or the business is wound down through the same authorities; assets and obligations are not erased.
- `CreditRegistry` updated to recognize estate claims.
- Save/load is idempotent: repeated save and load cannot duplicate distribution. Estate progress persists with legal holds recorded.

Dependent continuity:
- Surviving household finances reflect actual income loss (proven in SliceE).
- The surviving adult evaluates real options through the existing household decision authorities. No automatic replacement-provider mechanic. No widow income grant.
- Kinship history is never deleted. The deceased person's identity and life history persist.

## Why the correction follows Canon

Canon requires death to create persistent legal, economic, employment, and household consequences through ordinary authorities. Debts do not disappear on death. Property transfers only through legitimate transfer. The driver uses the existing title, obligation, and credit authorities rather than creating a parallel estate economy.

## Authoritative systems reused

MortalityDriver (death event), EstateService (estate records), TitleAuthority (property transfer), FinancialObligationAuthority (debt settlement), CreditRegistry (claims), AnimalRegistry (livestock), KinshipRegistry (heirs). No new estate registry, no new property authority.

## Source entry points

- `EstateLifecycleDriver.InventoryEstate(estateId, diag)`: asset identification with provenance.
- `EstateLifecycleDriver.AdjudicateClaims(estateId, diag)`: obligation processing with creditor ranking.
- `EstateLifecycleDriver.DistributeEstate(estateId, diag)`: transfer and distribution.
- `EstateLifecycleDriver.StageOf(estateId)` and `ProgressFor(estateId)`: read models for the current stage.
- `IEstateLifecycleDriver`: the interface for runtime integration.

## Tests that prove it

- EstateLifecycleDriverTests (20 passed): SliceE_MultipleHeirs_NoDuplicateAssets, SliceE_SaveLoad_CannotDuplicateDistribution, SliceE_SurvivingHouseholdFinancesReflectActualIncomeLoss, SliceE_ValidClaimsAffectDistribution, SliceF_BeneficiariesNotPersonallyLiable, SliceF_InsolventEstate_InventsNoFunds, SliceF_SecuredCreditor_RankedBeforeUnsecured, TestateEstate_ParcelBequest_DistributesByProbatedWill.

## What remains source-only

The driver is complete and tested but has no daily runtime caller. Estates advance when driven by tests or the scripted demo. A daily estate-processing step in the demographic driver is the designed next integration (recorded as PASS7-001 in the deferred register). Business ownership remains `OwnerKind`-keyed in `BusinessInstanceState`; the person-keyed ownership path is designed but not yet implemented.

## What Codex must connect in Unity

- Add a daily estate-processing step that advances open estates through the driver stages.
- Wire the person-keyed business ownership transfer in the live business manager.
- Validate in PlayMode: a provider death over several days produces estate opening, claim settlement, distribution, and closure with the surviving household's finances reflecting the loss.
