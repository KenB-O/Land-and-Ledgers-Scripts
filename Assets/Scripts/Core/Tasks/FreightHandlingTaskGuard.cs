// Plan 12 (2026-11-13, Slice Request 3 / QA-05 hardening): the
// single definition of a freight load/unload handling task, shared
// by the AdvanceAutonomousWork accrual skip, the
// TryStartBusinessPersonTask start refusal, and
// StaleInProgressSweep.Decide step 1 — so the three guards can never
// rot apart (Plan 06 rot class).
//
// A handling task is a task owned by the freight business (literal
// business id 3) OR carrying a load/unload definition id
// ("freight:load:<shipmentId>" / "freight:unload:<shipmentId>" from
// FreightShipmentService.BeginLoading/BeginUnloading, or the exact
// "fr.load-freight" / "fr.unload-freight" from
// FreightCompanyRuntime.TryDispatchShipment). Such tasks accrue only
// via WorkLoading/WorkUnloading, never via AdvanceAutonomousWork,
// and must never start through TryStartBusinessPersonTask (that
// funnel reserves with TaskReservationLabel, which would drop the
// second QA-05 barrier).
//
// Deliberately NOT handling tasks: "freight-dispatch-assignment"
// (the 1-minute dispatch handshake, which starts through
// TryStartBusinessPersonTask by design), "freight-service-meeting",
// "gs.unload-freight" (ordinary general-store work), and
// "fr.repair-wagon" (registered only, not a load/unload task; the
// owner clause covers it only when owned by business 3). Widening
// this set is a new decision, not a silent edit.
using System;
using LandLedgers.Primitives;

namespace LandLedgers.Tasks
{
    public static class FreightHandlingTaskGuard
    {
        public static bool IsFreightHandlingTask(WorkTask task)
        {
            if (task == null) return false;
            // Owner clause (load-bearing): survives a definition-id rename.
            // Kind-safe ==, never .Id == 3 (kind-blind, Plan 06).
            if (task.OwnerId == EntityId.For(EntityKind.Business, 3)) return true;
            // Definition-id clause (belt-and-braces): survives an owner
            // renumbering; covers a delivery-flow task whose owner is
            // caller-supplied rather than 3. Literals only, no dependency
            // on FreightCompanyRuntime from this Core/Tasks helper.
            string definitionId = task.DefinitionId;
            if (definitionId == null) return false;
            return definitionId.StartsWith("freight:load:", StringComparison.Ordinal)
                || definitionId.StartsWith("freight:unload:", StringComparison.Ordinal)
                || definitionId == "fr.load-freight"
                || definitionId == "fr.unload-freight";
        }
    }
}
