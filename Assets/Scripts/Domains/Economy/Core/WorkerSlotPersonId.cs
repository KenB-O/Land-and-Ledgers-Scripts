using System;

namespace LandLedgers.Economy
{
    /// <summary>CAMPAIGN-B (DEF-042): the one rule for reading a worker-slot
    /// assignment as a person id. Plain integer ids parse as before;
    /// owner-occupied slots carry "owner:{personId}" (written by
    /// BusinessRuntimeState.TryAssignOwnerOperator) and resolve to that
    /// person. An owner recorded by display name ("owner:{name}") has no
    /// person id to resolve, and any other value is unresolvable — both
    /// return false rather than inventing a person. Person ids are never
    /// negative, so a negative parse is also unresolvable.
    ///
    /// Pure string logic, zero dependencies, so the rule is testable in the
    /// focused Roslyn harness without dragging in BusinessRuntimeState.cs
    /// (4,000+ lines, deliberately stubbed out of the harness). The
    /// GeneralStore clerk scan (GeneralStoreRuntimeManager.
    /// TryGetServiceClerkPersonId) calls this; FindPersonForWorkerSlot
    /// still resolves plain integer ids only (labour-availability seam,
    /// intentionally untouched by the DEF-042 fix).</summary>
    public static class WorkerSlotPersonId
    {
        public static bool TryParse(string assignedWorkerId, out int personId)
        {
            personId = -1;
            if (string.IsNullOrWhiteSpace(assignedWorkerId))
            {
                return false;
            }

            if (int.TryParse(assignedWorkerId, out int parsed) && parsed >= 0)
            {
                personId = parsed;
                return true;
            }

            const string ownerPrefix = "owner:";
            if (assignedWorkerId.StartsWith(ownerPrefix, StringComparison.Ordinal)
                && int.TryParse(assignedWorkerId.Substring(ownerPrefix.Length), out int ownerPersonId)
                && ownerPersonId >= 0)
            {
                personId = ownerPersonId;
                return true;
            }

            return false;
        }
    }
}
