# Muse follow-up — Pass XL Phase 10 integration gaps

## Starting point

Work from `muse/passxl-slice-phase10` after fetching the latest remote tip. The Phase 10 Unity verification checkpoint is documented in `PassXlPhase10_UnityVerification.md`. Do not merge to `main`. Preserve the branch history and do not overwrite concurrent commits.

Canonical Bible XXXI and Technical Bible XIV (2026-10-07) govern. Proposals 001–105 are approved; 106–145 remain pending. Do not change Canon or promote test parameters into design rules. Seed cash, opening stock/assets, prices, rates, weights, distances and frequencies are caller-supplied test inputs. Where this handoff identifies a Kennedy decision, stop at the decision boundary and document the options; do not guess.

## What the Unity checkpoint established

- Unity 6000.4.2f1 compiled and generated fresh NUnit XML.
- Focused Phase 10 EditMode tests passed: player-as-NPC 2/2, six-person no-immigration 2/2, development fixture 2/2, phase 2–9 selection 97/97, FamilyTreeWiring 1/1, settlement reconciliation 4/4.
- Six-person Main Scene startup and 30-day PlayMode selections each passed 1/1. The controlled profile had six on-map Persons, three Households and three Businesses; 36 off-map kin identities were also present. The 30-day run is an identity/clock/callback proof, not proof of production: no completed Business production, no opening stock, zero Business cash and 750 cents household cash were observed.
- Ordinary profile PlayMode selection had one failure: `GeneralStoreDailySaleKeepsRealHouseholdAndPersonProvenance`, because weekly operations left zero staple stock. Do not inject stock or invent suppliers to green it.
- `ConstructionInputSupportTests` exceeded a 1,800-second run with no result XML. Bounded groups fail promptly in setup because the generated Default QA Town has no buildable empty owned plot. This does not establish the original stall’s cause.
- Main Scene’s six-person profile is an untracked Unity asset change outside this nested Scripts repository. Its recoverable checkpoint and NUnit XML are stored outside the project; they are not Git proof. Do not assume the scene is included in a source push.

## Priority work

### 1. Diagnose and repair the construction test fixture

Inspect `ConstructionInputSupportTests` setup and the Default QA Town generation/ownership data. Make the fixture deterministically create or select a legitimate buildable owned plot using existing authorities; do not change production rules or fabricate a successful construction result. Run bounded selections first, save fresh XML, then run the whole class with a reasonable timeout. Report any remaining timeout separately from setup failures.

### 2. Trace the ordinary General Store sale path

Trace authored item definitions, retail catalog/offer generation, lot inventory, pooled category stock, ownership, custody, reservations and settlement. The failed test shows zero eligible stock after normal weekly operations; it does not authorize free stock. Identify the missing production authority or data binding. Implement only if existing approved data and real supplier/transaction authorities support it. Otherwise document the exact required owner inputs and keep the sale unavailable honestly. Preserve the test’s buyer/person provenance assertions.

### 3. Continue the six-person vertical slice without invented economics

Use the opt-in development fixture, never ordinary scenario defaults, to verify each real authority end to end:

- stable Person/Household IDs and visual bindings;
- real Task time, employment, and schedule constraints;
- Business cash, input inventory, supplier purchase, freight custody, production and retail sale;
- household purchase, food consumption and nutrition;
- save/load while work or freight is active;
- 30 consecutive days with financial and physical reconciliation.

At present the 30-day test demonstrates time advancement and identity stability only. Do not label it an autonomous economy until actual work, purchases, production and consumption are evidenced. Add bounded tests around each causal transition and preserve negative cases.

### 4. Make progress on missing authority/data contracts

Review the handoff and branch source before proposing new architecture. In particular, determine the narrowest existing-authority integration for residence/housing and off-map supply. Do not create a parallel inventory, supplier, logistics, population or household registry. Do not register fictional supplier identities, prices, SKU availability, distances or freight rates.

## Kennedy decision gates — do not decide on his behalf

Use the exact decision list in `PassXlPhase10CodexPrep.md`. At minimum keep blocked: whether to place or retire the D1 transport-presentation controller and unify shipment sets; off-map supplier identities/catalogs/prices/distances/freight parameters; missing retail tier/item/price data; bakery staged receipt/aging and notions source; household/non-livery horse-feed policy; departure destinations and gates; traveler/reputation weights; and wage, owner draw, credit/writeoff policy. Present concise options and implications in a report; do not encode a guessed answer.

## Safe workflow and deliverables

1. Fetch before editing; confirm the branch and clean status, and inspect commits since the Phase 10 verification checkpoint.
2. Preserve existing work. Assign disjoint file ownership if collaborating; only one operator may edit/save the live Unity scene.
3. Keep changes focused, add regression tests, compile in Unity 6000.4.2f1, and save fresh XML outside the Unity project. Run EditMode then targeted PlayMode; do not repeatedly launch the known full-class timeout without a changed hypothesis.
4. Review every diff, test old-save fallbacks and idempotent restore where relevant, and state any Unity scene/Inspector wiring that remains local or unverified.
5. Commit coherent source/test/docs progress on this branch and push only as a fast-forward after verifying remote ancestry. Never force-push or merge to `main`.
6. Report exact SHAs, changed files, test counts/XML locations, gameplay evidence, unresolved blockers, and Kennedy decisions. Never present source presence or a clock-only 30-day run as working gameplay.
