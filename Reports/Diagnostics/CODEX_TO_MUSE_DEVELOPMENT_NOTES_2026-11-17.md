# Codex to Muse — Development Notes (2026-11-17)

These notes come from the expanded integration and the 60-day Unity run. They are intended to make the next implementation loop faster and more reliable, not to prescribe a new architecture.

## Patterns that repeatedly cost time

### 1. Source-only tests miss production wiring seams

Several high-impact failures appeared only when Unity used the production bootstrapper and `SimulationSystemsHub`:

- The 60-day run showed a sawmill station reported as established, but task execution could not find it because TaskAuthority supplied a hashed numeric `EntityId`, while the station registry was keyed by business instance string.
- Earlier integration failures appeared in `SimulationDrivers`, `FirstLedgerSliceBootstrapper`, runtime hub accessors, and actual ScriptableObject read-back. These are exactly the places excluded or stubbed in some source harnesses.

Efficiency suggestion: for each feature, keep its pure service tests, then add one small test that instantiates the real hub/driver/bootstrap adapter and passes the same owner IDs and references the production task path passes. Test the production wiring seam directly rather than reconstructing it with a custom fixture callback.

### 2. Business identity has two representations

Business runtime code commonly carries stable string instance IDs, while task ownership uses `EntityId` with a stable numeric value. A registry keyed only by the string will not be found by a task gate that receives the number. The sawmill regression now covers this mapping and collision fail-closed behavior.

Efficiency suggestion: document the canonical conversion at each boundary and test `business instance ID → task owner EntityId → runtime registry lookup` once for each shared authority. Avoid adding one-off string parsing to each business driver.

### 3. Scene registration and runtime presence are different claims

A startup log that says “registered” or “established” is not sufficient proof that a later driver uses the same holder. Keep the startup gate checking holder identity, dependencies, and driver references after all bootstrap passes. Include counts and stable identities in diagnostics.

The completed run had 14 authored people in configuration but 98 runtime residents. The current gate treats configured residents as a minimum, and generated town population may account for the difference; the report records this rather than assuming it is a defect. Future run summaries should distinguish authored residents, generated residents, and total population explicitly.

### 4. One stuck physical journey can occupy shared equipment indefinitely

The paid Millbrook flour shipment dispatched, but its assigned carrier repeatedly remained in the “walking to staging” state. Its shipment held the opening wagon, and logging-camp supply could not create its own shipment because that wagon was already bound. That is a useful general diagnostic pattern: report the blocking shipment identity, current movement owner/state, bound vehicle, and which other work is being excluded.

Efficiency suggestion: test both the positive path and a recoverable movement failure. A timeout must not pretend delivery occurred; it should leave cargo and payment obligations intact while exposing a legitimate retry/recovery path.

### 5. Stock is split across several real authorities

In this run, bakery production and wholesale settlement progressed, but household demand still saw repeated store stockouts. The logs also show physical item lots, live sellable category pools, external supplier lots, storage capacity refusals, and household receipts. These are distinct states. Keep diagnostics explicit about which authority and quantity a number describes.

A capacity refusal that restores the source lot is a valid protected failure, not inventory loss. Replenishment logs should separate “supplier order,” “cash settled,” “goods in transit,” “goods received,” and “saleable stock.”

## Unity iteration practices

- Compile the entire Unity project after edits to bootstrapper, hub, daily-driver, save/load, or ScriptableObject integration. Source-harness success alone did not catch their runtime wiring problems.
- Run focused EditMode tests first, then one controlled PlayMode check through the actual scene and authorities.
- The full Test Runner displayed thousands of tests and stalled Unity during a repaint when the long run was starting. Closing that window did not prevent a later independent Editor stall. Prefer filtered tests and close the large Test Runner view before a long simulation; do not force-restart Unity if it becomes unresponsive.
- The runner now records logs and snapshots incrementally. A full 60-day pass at 250x with 98 residents took about 7m10s after startup. Start with a passing startup gate, then avoid repeating the long run after unrelated changes.
- Keep a short run for physical route observation. High-speed runs expose recurring economic blockers but cannot prove that a person visibly walked to the right position.
- Treat repeated `Incident` and daily `SimulationDrivers` warnings as evidence to group by stable incident/transaction identity. Suppress only duplicate unchanged messages; preserve real stockouts, route failures, refused capacity, and state transitions.

## Test authoring details

- On Unity 6, `UnityEngine.EntityId` can collide by name with `LandLedgers.Primitives.EntityId`; alias the game type in test source where needed.
- Tests for intentional `Debug.LogError` fail unless they explicitly expect the log (`LogAssert.Expect`). Keep the test focused on its contract and avoid allowing an expected failure log to look like an unrelated test regression.
- Do not edit serialized Unity YAML comments by hand. Use Unity serialization APIs and verify loaded-object counts and references.
- Keep owner-authored scene/prefab changes separate from source commits. This run did not require saving or editing the Main Scene.

## Current run-specific follow-up

See [`CODEX_UBC_UNITY_INTEGRATION_2026-11-17.md`](CODEX_UBC_UNITY_INTEGRATION_2026-11-17.md) for the exact two completed 60-day runs, console artifacts, four A1 test failures, and the ranked Muse queue. The second completed run confirmed that the workstation ID bridge now gets the sawmill task to labor assignment; P11 had no remaining scheduled minutes for the 81-minute job, so sawmill output is still blocked. Highest-value follow-ups are the assigned-worker schedule/task-time seam, pending freight arrival/recovery, transaction-level food charge matching, and the generic business closure audit.

### Correction to the handoff chronology

The run `20261117T220225Z-e0f61aea` completed 60/60 days at 250x with a passing startup gate. Its `metadata.json` and final-state summary are authoritative; an earlier interim note incorrectly described the run as stalled before PlayMode. The Editor process was responsive after completion. Preserve this distinction when triaging logs: the registry identity defect was fixed and passed focused tests, while labor availability remained a separate real blocker.
