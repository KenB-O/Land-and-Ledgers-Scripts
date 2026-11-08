using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// EQP-1: legitimate custody kinds for equipment access (Tech X §3.7, Canon 4.4).
    /// Requirement resolution asks whether the operating actor has legitimate
    /// ACCESS — not only ownership.
    /// </summary>
    public enum EquipmentCustodyKind
    {
        Owned = 0,
        EmployerProvided = 1,
        Rented = 2,
        Leased = 3,
        Borrowed = 4,
        CustomerSupplied = 5,
        ContractorSupplied = 6,
        /// <summary>
        /// The canonical case: a traveling threshing outfit serves the farm with
        /// its own separator (Canon 6.3) — the farm never owns the machine.
        /// </summary>
        ServiceProvider = 7,
    }

    /// <summary>
    /// EQP-1: a recorded grant of equipment access that is not ownership —
    /// a rental agreement, a borrow, a service contract. Grants expire; expired
    /// grants satisfy nothing.
    /// </summary>
    [Serializable]
    public sealed class EquipmentAccessGrant
    {
        public string GrantId = string.Empty;
        public string EquipmentKind = string.Empty; // required kind, e.g. "threshing-separator"
        public string AssetId = string.Empty;       // optional: a specific asset
        public string HolderKind = string.Empty;    // "business", "person", "household"
        public string HolderId = string.Empty;
        public EquipmentCustodyKind Custody = EquipmentCustodyKind.Borrowed;
        public string GranterName = string.Empty;
        public int ExpiryDayIndex = -1;             // -1 = no expiry
        public string TermsNote = string.Empty;     // e.g. "threshing outfit: $2/acre, week of Sep 10"

        public bool IsActive(int dayIndex) => ExpiryDayIndex < 0 || dayIndex <= ExpiryDayIndex;
    }

    /// <summary>
    /// EQP-1: resolves whether an actor legitimately holds a required equipment
    /// kind — owned assets first, then unexpired access grants (Tech X §3.7).
    /// Never invents access; every non-owned path needs a recorded grant.
    /// </summary>
    public sealed class EquipmentAccessResolver
    {
        private readonly List<EquipmentAccessGrant> grants = new List<EquipmentAccessGrant>();

        public void AddGrant(EquipmentAccessGrant grant)
        {
            if (grant == null) return;
            if (string.IsNullOrWhiteSpace(grant.GrantId))
                grant.GrantId = "grant-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            grants.Add(grant);
        }

        /// <summary>
        /// Priority 9 Slice 3 (person inventory read model): read-only
        /// enumeration of the recorded grants for one holder. Returns a new
        /// list each call (the grant objects themselves are the authority's
        /// live records, as elsewhere in this codebase); adding, removing,
        /// or expiring a grant is still only possible through
        /// <see cref="AddGrant"/> and the grant's own expiry. A holder with
        /// no grants gets an empty list, never null and never an error.
        /// Owned equipment is NOT listed here: ownership is answered by
        /// <see cref="HasAccess"/> through the caller's owned-kinds
        /// function, and this resolver holds no owned-kinds record of its
        /// own to enumerate.
        /// </summary>
        public IReadOnlyList<EquipmentAccessGrant> GrantsFor(string holderKind, string holderId)
        {
            var result = new List<EquipmentAccessGrant>();
            foreach (EquipmentAccessGrant grant in grants)
            {
                if (grant == null) continue;
                if (!string.Equals(grant.HolderKind, holderKind, StringComparison.Ordinal)) continue;
                if (!string.Equals(grant.HolderId, holderId, StringComparison.Ordinal)) continue;
                result.Add(grant);
            }

            return result;
        }

        /// <param name="ownedKindsFor">
        /// (holderKind, holderId) → equipment kinds the holder owns and can field.
        /// </param>
        /// <returns>True when access exists; custodyDescription names how ("owned" / "borrowed from X" / ...).</returns>
        public bool HasAccess(
            string holderKind,
            string holderId,
            string equipmentKind,
            Func<string, string, List<string>> ownedKindsFor,
            int dayIndex,
            out string custodyDescription)
        {
            custodyDescription = "none";
            if (string.IsNullOrWhiteSpace(equipmentKind))
                return false;

            var owned = ownedKindsFor != null ? ownedKindsFor(holderKind, holderId) : null;
            if (owned != null)
            {
                foreach (string kind in owned)
                {
                    if (string.Equals(kind, equipmentKind, StringComparison.Ordinal))
                    {
                        custodyDescription = "owned";
                        return true;
                    }
                }
            }

            foreach (var grant in grants)
            {
                if (!string.Equals(grant.HolderKind, holderKind, StringComparison.Ordinal)) continue;
                if (!string.Equals(grant.HolderId, holderId, StringComparison.Ordinal)) continue;
                if (!string.Equals(grant.EquipmentKind, equipmentKind, StringComparison.Ordinal)) continue;
                if (!grant.IsActive(dayIndex)) continue;
                custodyDescription = $"{grant.Custody.ToString().ToLowerInvariant()} from {grant.GranterName}";
                return true;
            }

            return false;
        }
    }
}
