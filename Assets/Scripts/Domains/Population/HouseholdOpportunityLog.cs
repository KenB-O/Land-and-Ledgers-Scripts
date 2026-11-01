using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Worker C (Pass III): opportunities a household legitimately knows about.
    ///
    /// Canon: XXXI Part IV §4.4 — known information is evidence, never an
    /// omniscient list. An opportunity enters this log only through a documented
    /// channel (a member's contact is employed there — provenance recorded).
    /// The decision service reads UnpursuedCount; nothing here invents work.
    /// </summary>
    [Serializable]
    public sealed class DiscoveredOpportunity
    {
        public string OpportunityId = string.Empty;
        public int HouseholdId;
        public string BusinessId = string.Empty;
        /// <summary>The contact's actual role there, when known; may be empty.</summary>
        public string RoleHint = string.Empty;
        /// <summary>Contact-graph provenance, e.g. "P3↔P7 FormerCoworker: worked together at store-1".</summary>
        public string Provenance = string.Empty;
        public int DiscoveredDayIndex;
        public bool Pursued;
    }

    public sealed class HouseholdOpportunityLog
    {
        private readonly List<DiscoveredOpportunity> opportunities = new List<DiscoveredOpportunity>();
        private readonly List<string> diagnostics = new List<string>();
        private int sequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<DiscoveredOpportunity> Opportunities => opportunities;

        /// <summary>
        /// Records a discovered opportunity. Idempotent per (household, business):
        /// repeat discovery returns false and keeps the first record.
        /// </summary>
        public bool RecordIfNew(int householdId, string businessId, string roleHint,
            string provenance, int dayIndex)
        {
            if (householdId < 0 || string.IsNullOrWhiteSpace(businessId))
                return false;
            for (int i = 0; i < opportunities.Count; i++)
            {
                DiscoveredOpportunity o = opportunities[i];
                if (o != null && o.HouseholdId == householdId
                    && string.Equals(o.BusinessId, businessId, StringComparison.Ordinal))
                    return false;
            }
            opportunities.Add(new DiscoveredOpportunity
            {
                OpportunityId = "opp-" + householdId + "-" + sequence++,
                HouseholdId = householdId,
                BusinessId = businessId,
                RoleHint = roleHint ?? string.Empty,
                Provenance = provenance ?? string.Empty,
                DiscoveredDayIndex = dayIndex,
                Pursued = false,
            });
            return true;
        }

        public void MarkPursued(int householdId, string businessId)
        {
            for (int i = 0; i < opportunities.Count; i++)
            {
                DiscoveredOpportunity o = opportunities[i];
                if (o != null && o.HouseholdId == householdId
                    && string.Equals(o.BusinessId, businessId, StringComparison.Ordinal))
                    o.Pursued = true;
            }
        }

        public int UnpursuedCount(int householdId)
        {
            int n = 0;
            for (int i = 0; i < opportunities.Count; i++)
            {
                DiscoveredOpportunity o = opportunities[i];
                if (o != null && o.HouseholdId == householdId && !o.Pursued)
                    n++;
            }
            return n;
        }

        public List<DiscoveredOpportunity> GetForHousehold(int householdId)
        {
            var result = new List<DiscoveredOpportunity>();
            for (int i = 0; i < opportunities.Count; i++)
            {
                DiscoveredOpportunity o = opportunities[i];
                if (o != null && o.HouseholdId == householdId)
                    result.Add(o);
            }
            return result;
        }

        /// <summary>Captures the log for the save pipeline.</summary>
        public HouseholdOpportunityLogSaveDto CaptureSaveDto()
        {
            return new HouseholdOpportunityLogSaveDto
            {
                opportunities = new List<DiscoveredOpportunity>(opportunities),
                sequence = sequence,
            };
        }

        /// <summary>Clear-then-load. Opportunities are knowledge records —
        /// reload restores them without re-discovering or re-pursuing.</summary>
        public void LoadFromSaveDto(HouseholdOpportunityLogSaveDto dto)
        {
            opportunities.Clear();
            sequence = 0;
            if (dto == null) return;
            sequence = Math.Max(0, dto.sequence);
            if (dto.opportunities == null) return;
            foreach (DiscoveredOpportunity o in dto.opportunities)
            {
                if (o == null) continue;
                opportunities.Add(o);
            }
        }
    }

    /// <summary>Save data for the household opportunity log.</summary>
    [Serializable]
    public sealed class HouseholdOpportunityLogSaveDto
    {
        public List<DiscoveredOpportunity> opportunities = new List<DiscoveredOpportunity>();
        public int sequence;
    }
}
