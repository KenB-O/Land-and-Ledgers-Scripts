// DEF-050: pure wording for a shipment stranded on its prepared dispatch-task link.
// Terminal or pruned tasks are candidates for bounded re-preparation under the
// original accepted agreement. An unrelated task id remains a fail-closed
// integrity condition. If bounded recovery is exhausted before loading, the
// existing FreightShipmentService failure path preserves the source inventory
// and FreightProductionDriver settles the agreement cancellation.
using System;

namespace LandLedgers.Economy.Transport
{
    public static class DispatchHoldStrandReason
    {
        /// <summary>Snapshot of one agreement's recorded dispatch task, as
        /// read by the driver method. Status and outcome are carried as
        /// their enum names ("Failed" / "Cancelled" / "Complete",
        /// "RecoverableFailure" / "SeriousFailure" / "Success", ...) so
        /// this file stays free of the TaskAuthority types and their Unity
        /// dependencies. TerminalDayIndex follows the prune's anchor:
        /// CompletedDayIndex when recorded, else CreatedDayIndex.</summary>
        public readonly struct DispatchHoldTaskState
        {
            public DispatchHoldTaskState(string shipmentId, string agreementId,
                string taskId, bool taskRecordExists, string recordedCustomerRef,
                string expectedCustomerRef, bool taskIsOpen, string taskStatusName,
                string taskOutcomeName, int terminalDayIndex, int acceptedDayIndex)
            {
                ShipmentId = shipmentId;
                AgreementId = agreementId;
                TaskId = taskId;
                TaskRecordExists = taskRecordExists;
                RecordedCustomerRef = recordedCustomerRef;
                ExpectedCustomerRef = expectedCustomerRef;
                TaskIsOpen = taskIsOpen;
                TaskStatusName = taskStatusName;
                TaskOutcomeName = taskOutcomeName;
                TerminalDayIndex = terminalDayIndex;
                AcceptedDayIndex = acceptedDayIndex;
            }

            public string ShipmentId { get; }
            public string AgreementId { get; }
            public string TaskId { get; }
            public bool TaskRecordExists { get; }
            public string RecordedCustomerRef { get; }
            public string ExpectedCustomerRef { get; }
            public bool TaskIsOpen { get; }
            public string TaskStatusName { get; }
            public string TaskOutcomeName { get; }
            public int TerminalDayIndex { get; }
            public int AcceptedDayIndex { get; }
        }

        /// <summary>Returns null when the hold is NOT stranded on its
        /// recorded dispatch task: no task id is recorded yet (the task was
        /// never created — the prerequisite branches describe that stall),
        /// or the recorded task still resolves to this shipment's task and
        /// is still open (the live path). Otherwise a one-line reason
        /// naming the strand: a terminal recorded task, a recorded id that
        /// resolves to a different task, or a recorded id whose TaskAuthority
        /// record is gone. Never returns an empty string.</summary>
        public static string Describe(DispatchHoldTaskState state, int dayIndex)
        {
            if (string.IsNullOrWhiteSpace(state.TaskId))
                return null;
            if (!state.TaskRecordExists)
            {
                int heldDays = Math.Max(0, dayIndex - state.AcceptedDayIndex);
                return "[FreightDispatch] Shipment '" + state.ShipmentId
                    + "' stays Planned with its dispatch hold: recorded dispatch task '"
                    + state.TaskId + "' is no longer in TaskAuthority (pruned or lost after terminalizing); "
                    + "the original prepared agreement is eligible for bounded task re-preparation. Held since acceptance (day "
                    + state.AcceptedDayIndex + ", " + heldDays + " " + DayWord(heldDays) + " ago).";
            }
            if (!string.Equals(state.RecordedCustomerRef, state.ExpectedCustomerRef, StringComparison.Ordinal))
            {
                return "[FreightDispatch] Shipment '" + state.ShipmentId
                    + "' stays Planned with its dispatch hold: recorded dispatch task '"
                    + state.TaskId + "' resolves to a different task ('"
                    + state.RecordedCustomerRef + "'); no replacement was created.";
            }
            if (state.TaskIsOpen)
                return null;
            int strandedDays = Math.Max(0, dayIndex - state.TerminalDayIndex);
            return "[FreightDispatch] Shipment '" + state.ShipmentId
                + "' stays Planned with its dispatch hold: dispatch task '"
                + state.TaskId + "' terminalized " + state.TaskStatusName
                + " (" + state.TaskOutcomeName + ") on day " + state.TerminalDayIndex
                + " (" + strandedDays + " " + DayWord(strandedDays) + " ago); "
                + "the same accepted agreement may receive a bounded dispatch-task re-preparation "
                + "while its cargo remains untouched before loading.";
        }

        private static string DayWord(int days)
        {
            return days == 1 ? "day" : "days";
        }
    }
}
