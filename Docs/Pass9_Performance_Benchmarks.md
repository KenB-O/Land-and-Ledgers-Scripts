# Pass IX — Performance Benchmarks (PASS8-009)

Method: Roslyn harness on this VM, shared with other work, so small absolute numbers fluctuate; the before/after deltas are orders of magnitude and stable across repeated runs. Benchmarks are the repeatable tests in Tests/Editor/Integration/Pass9PerformanceIndexingTests.cs. BEFORE numbers were measured with the same test file against the unmodified stores at the post-plan commit; AFTER numbers with the indexed implementations. Every change is semantics-preserving only.

## Before / after

| Benchmark | Before | After | Speedup |
|---|---|---|---|
| Schedule build: 29,200 reservations (20 persons x 730 days x 2/day, each overlap-checked) | 5,549 ms | 27–35 ms | about 165x |
| Schedule queries: 30,000 mixed ActiveFor/IsFree/DescribeActivity over a 29,200-reservation history | 20,922 ms | 29–40 ms | about 550x |
| Knowledge: 12,000 RecordTransmission inserts | 3,205 ms | 46–85 ms | about 45x |
| Knowledge: 3,000 PacketsFor and IndependentSourceCount queries over 12,000 packets | 876 ms | 46–57 ms | about 17x |
| Due events: schedule 40,000 plus 2,000 reschedule/cancel | 450 ms | 75–203 ms | about 3x |
| Due events: one PopDue draining a 39,000-event cohort | 258 ms | 97–172 ms | about 2x |
| AdvanceShipments: 730 daily calls over a 120,025-shipment ledger (25 active) | 2,253 ms | under 1 ms | over 2,000x |

## What changed

- PersonScheduleTracker: (person, day) dictionary buckets preserving per-bucket insertion order (the order the old full scan visited), plus a reservation-id map for O(1) release. A custom key comparer (splitmix64 finalizer) is used because the default hash of the packed key has about 13 bits of entropy for realistic ids; the first indexed build degraded about 10x per 4x data from bucket-chain collisions, and with the mixed hash restore is flat. Reservations cannot span midnight, so every query is intrinsically (person, day)-scoped and bucket order equals the old restricted scan order.
- KnowledgeTransmissionStore: three indexes, packet id to packet list (duplicate-id refusal), a hash set of telling keys reproducing the old comparison exactly (recipient exact, subject OrdinalIgnoreCase, content Ordinal, chain root Ordinal), and recipient to packets in insertion order for PacketsFor.
- ManagerDecisionExecution: the ledger maintains the in-transit shipment subset in ledger order, updated at the only status-mutation points (the three transitions inside AdvanceShipments, verified by grep) and rebuilt on load. Full shipment history remains in the ledger and the DTO.
- DueEventScheduler: the sorted list with RemoveAt(0) drain became a binary min-heap keyed by (dueGameSeconds, sequence), with lazy stale-entry discard (replace and cancel update the id map only; a heap entry is live iff the map points at that exact object). CaptureSaveDto re-sorts the live set, so DTO order is unchanged.

## Equivalence guards

- Golden serializations captured from the pre-index code for the schedule, knowledge, and manager-ledger DTOs, plus a canonical event string for the due-event DTO; the indexed code reproduces them byte for byte, including after a load-then-capture round trip. No DTO format changed.
- A seeded 400-step operation stream (schedule, reschedule, cancel, CancelForPayload, PopDue) is checked step by step against an independent sorted-list reference model: identical pop sequences, counts, and peeks.
- No history was deleted, no event is skipped, and no economic rule changed. The independent review re-verified the equivalence arguments, including the injective (person, day) key packing and the first-wins release order.

## Save-restore profile (audit A4-16, measure first)

| Store restore plus capture | Small | Large (4x) | Shape |
|---|---|---|---|
| Person schedule (25k to 100k reservations) | 0 ms | 24 ms | linear |
| Knowledge (10k to 40k packets) | about 0 ms | about 0 ms | linear |
| Manager ledger (25k to 100k shipments) | 0 ms | 2 ms | linear |
| Due events (10k to 40k events), before | 16 ms | 175 ms | superlinear, about 11x for 4x |
| Due events, after the heap change | 8–14 ms | 42–317 ms (noisy VM) | heap push O(n log n), no quadratic term |

The only superlinear restore isolated was DueEventScheduler.LoadFromSaveDto (ordered insert per event), fixed by the heap change. The other touched stores restore linearly; SaveReferenceResolver already resolves through dictionaries and SaveLoadManager restore orchestration is linear per-system loops, so no change was made to any other restore path.
