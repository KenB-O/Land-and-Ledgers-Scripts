using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Creation
{
    /// <summary>
    /// Phase G (G1): how a Person came by a market observation. Append-only.
    /// Every kind requires the Person to have been party to, or a direct
    /// witness of, the underlying event — there is no omniscient market
    /// scanner. A Person's knowledge is bounded by what they could
    /// legitimately have seen or heard.
    /// </summary>
    public enum NpcObservationKind
    {
        Unspecified = 0,

        /// <summary>
        /// The Person's OWN household could not buy something (Phase C
        /// shopping left it unfulfilled). They were party to it.
        /// </summary>
        OwnHouseholdUnfulfilledDemand = 1,

        /// <summary>
        /// The Person directly witnessed another household's unfulfilled
        /// demand (e.g. present at the store when stock ran out). Requires a
        /// witness event reference — hearsay does not count.
        /// </summary>
        WitnessedUnfulfilledDemand = 2,

        /// <summary>Visible queues the Person stood in or watched form.</summary>
        VisibleQueue = 3,

        /// <summary>A need the Person overheard stated directly to them or in
        /// their presence. Requires a witness event reference.</summary>
        OverheardNeed = 4,

        /// <summary>A supplier gap the Person ran into themselves while trying
        /// to buy or restock (their own failed purchase).</summary>
        SupplierGap = 5,

        /// <summary>A premises opening the Person saw themselves
        /// (vacant shop, farm for sale sign, landlord offering space).</summary>
        PremisesOpening = 6,
    }

    /// <summary>
    /// Phase G (G1): one genuine market observation by one Person. The
    /// evidence reference names the real event (a Phase C need sequence, a
    /// shopping trip id, a market-day visit) — never a synthesized signal.
    /// </summary>
    [Serializable]
    public sealed class NpcMarketObservation
    {
        public string ObservationId = string.Empty;
        public int ObserverPersonId = -1;
        public int ObserverHouseholdId = -1;
        public int DayIndex;
        public NpcObservationKind Kind = NpcObservationKind.Unspecified;
        public string ItemOrServiceId = string.Empty;
        public int UnmetUnitsPerDay;
        public string Detail = string.Empty;

        /// <summary>
        /// The real event this observation came from (Phase C need
        /// sequence, shopping trip id, market visit). Empty only for
        /// OwnHouseholdUnfulfilledDemand, which is witnessed by membership.
        /// </summary>
        public string EvidenceReference = string.Empty;

        public NpcMarketObservation() { }
    }

    /// <summary>
    /// Phase G (G1): an opportunity a Person recognized from their own
    /// repeated observations. The unmet-units estimate is derived ONLY from
    /// observed events (sum of witnessed unmet units over distinct witness
    /// days) — never from market-wide data the Person could not have.
    /// </summary>
    [Serializable]
    public sealed class NpcOpportunity
    {
        public string OpportunityId = string.Empty;
        public int RecognizerPersonId = -1;
        public string ItemOrServiceId = string.Empty;
        public int FirstObservedDayIndex;
        public int LastObservedDayIndex;
        public int ObservationCount;
        public int DistinctWitnessDays;
        public int EstimatedUnmetUnitsPerDay;
        public NpcOpportunityStatus Status = NpcOpportunityStatus.Watch;
        public string StatusReason = string.Empty;

        public NpcOpportunity() { }
    }

    /// <summary>Phase G (G1): what happened to a recognized opportunity. Append-only.</summary>
    public enum NpcOpportunityStatus
    {
        Unspecified = 0,
        Watch = 1,
        Investigating = 2,
        Dismissed = 3,
        ActedOn = 4,
    }

    /// <summary>
    /// Phase G (G1): one Person's accumulated market observations. This is
    /// the anti-omniscience boundary: a Person reasons ONLY over their own
    /// log. There is deliberately no method that scans other households'
    /// needs, no market-wide query, and no path from observation to
    /// business creation — recognition only ever produces an
    /// <see cref="NpcOpportunity"/> for this Person to investigate.
    /// </summary>
    [Serializable]
    public sealed class NpcOpportunityObservationLog
    {
        [SerializeField]
        private int personId = -1;

        [SerializeField]
        private int householdId = -1;

        [SerializeField]
        private List<NpcMarketObservation> observations = new List<NpcMarketObservation>();

        [SerializeField]
        private List<NpcOpportunity> opportunities = new List<NpcOpportunity>();

        [SerializeField]
        private int nextObservationSequence;

        [SerializeField]
        private int nextOpportunitySequence;

        public NpcOpportunityObservationLog() { }

        public NpcOpportunityObservationLog(int personId, int householdId)
        {
            this.personId = personId;
            this.householdId = householdId;
        }

        public int PersonId => personId;
        public int HouseholdId => householdId;
        public IReadOnlyList<NpcMarketObservation> Observations => observations;
        public IReadOnlyList<NpcOpportunity> Opportunities => opportunities;

        /// <summary>
        /// Records one observation. Provenance is enforced, not assumed:
        /// own-household demand is legitimate by membership; every other
        /// kind requires a non-empty witness event reference. Returns null
        /// on success, a loud refusal otherwise.
        /// </summary>
        public string RecordObservation(NpcMarketObservation observation, NpcBusinessEventLog events)
        {
            if (observation == null)
            {
                return "Refused: no observation supplied.";
            }

            if (observation.ObserverPersonId != personId)
            {
                return $"Refused: observation names P{observation.ObserverPersonId} but this log belongs to P{personId} — " +
                    "a Person cannot borrow another's eyes (Phase G anti-omniscience).";
            }

            if (observation.Kind == NpcObservationKind.Unspecified)
            {
                return "Refused: observation kind is Unspecified.";
            }

            if (string.IsNullOrWhiteSpace(observation.ItemOrServiceId))
            {
                return "Refused: observation names no item or service.";
            }

            if (observation.Kind == NpcObservationKind.OwnHouseholdUnfulfilledDemand)
            {
                if (observation.ObserverHouseholdId != householdId)
                {
                    return $"Refused: own-household demand for household {observation.ObserverHouseholdId} " +
                        $"recorded on P{personId}'s log (member of household {householdId}) — not their household to observe.";
                }
            }
            else if (string.IsNullOrWhiteSpace(observation.EvidenceReference))
            {
                return $"Refused: {observation.Kind} needs a witness event reference — " +
                    "a Person only knows what they were party to or directly witnessed.";
            }

            if (string.IsNullOrWhiteSpace(observation.ObservationId))
            {
                observation.ObservationId = $"obs-p{personId}-{nextObservationSequence++}";
            }

            observations.Add(observation);
            events?.Record(observation.DayIndex, NpcBusinessEventKind.OpportunityObserved,
                personId, string.Empty,
                $"P{personId} observed {observation.Kind} for '{observation.ItemOrServiceId}' " +
                $"(evidence: {observation.EvidenceReference}).");
            return null;
        }

        /// <summary>
        /// Maps a real Phase C unfulfilled household need into an
        /// observation for a Person who legitimately saw it. The caller
        /// supplies the witness facts; this method only maps, never invents.
        /// </summary>
        public static NpcMarketObservation FromHouseholdNeed(
            int needSequence,
            int needHouseholdId,
            string itemId,
            int unitsNeeded,
            int createdDayIndex,
            int observerPersonId,
            int observerHouseholdId,
            string witnessEventReference)
        {
            bool ownHousehold = needHouseholdId == observerHouseholdId;
            return new NpcMarketObservation
            {
                ObserverPersonId = observerPersonId,
                ObserverHouseholdId = observerHouseholdId,
                DayIndex = createdDayIndex,
                Kind = ownHousehold
                    ? NpcObservationKind.OwnHouseholdUnfulfilledDemand
                    : NpcObservationKind.WitnessedUnfulfilledDemand,
                ItemOrServiceId = itemId ?? string.Empty,
                UnmetUnitsPerDay = Math.Max(0, unitsNeeded),
                Detail = $"household {needHouseholdId} need #{needSequence} for '{itemId}' unfulfilled",
                EvidenceReference = ownHousehold
                    ? $"need:{needSequence}"
                    : (witnessEventReference ?? string.Empty),
            };
        }

        /// <summary>
        /// Turns this Person's repeated observations into recognized
        /// opportunities. Scans ONLY this log. A business is NEVER created
        /// here — recognition produces an opportunity to investigate, and
        /// the Opportunity &amp; Pressure runtime is forbidden from using
        /// this to manufacture businesses or sales (§29 regression guard).
        /// </summary>
        public List<NpcOpportunity> RecognizeOpportunities(
            int minObservations,
            int minDistinctWitnessDays,
            int dayIndex,
            NpcBusinessEventLog events,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            var newlyRecognized = new List<NpcOpportunity>();

            var byItem = new Dictionary<string, List<NpcMarketObservation>>(StringComparer.Ordinal);
            foreach (NpcMarketObservation obs in observations)
            {
                if (obs == null || string.IsNullOrWhiteSpace(obs.ItemOrServiceId)) continue;
                if (!byItem.TryGetValue(obs.ItemOrServiceId, out List<NpcMarketObservation> list))
                {
                    list = new List<NpcMarketObservation>();
                    byItem[obs.ItemOrServiceId] = list;
                }

                list.Add(obs);
            }

            foreach (KeyValuePair<string, List<NpcMarketObservation>> pair in byItem)
            {
                if (AlreadyRecognized(pair.Key)) continue;

                List<NpcMarketObservation> list = pair.Value;
                if (list.Count < Math.Max(1, minObservations)) continue;

                var witnessDays = new HashSet<int>();
                int totalUnmet = 0;
                int firstDay = int.MaxValue;
                int lastDay = int.MinValue;
                foreach (NpcMarketObservation obs in list)
                {
                    witnessDays.Add(obs.DayIndex);
                    totalUnmet += Math.Max(0, obs.UnmetUnitsPerDay);
                    if (obs.DayIndex < firstDay) firstDay = obs.DayIndex;
                    if (obs.DayIndex > lastDay) lastDay = obs.DayIndex;
                }

                if (witnessDays.Count < Math.Max(1, minDistinctWitnessDays)) continue;

                var opportunity = new NpcOpportunity
                {
                    OpportunityId = $"opp-p{personId}-{nextOpportunitySequence++}",
                    RecognizerPersonId = personId,
                    ItemOrServiceId = pair.Key,
                    FirstObservedDayIndex = firstDay,
                    LastObservedDayIndex = lastDay,
                    ObservationCount = list.Count,
                    DistinctWitnessDays = witnessDays.Count,
                    // Derived from witnessed events only — never market-wide data.
                    EstimatedUnmetUnitsPerDay = witnessDays.Count > 0 ? totalUnmet / witnessDays.Count : 0,
                    Status = NpcOpportunityStatus.Watch,
                    StatusReason = $"recognized from {list.Count} observation(s) over {witnessDays.Count} day(s), all witnessed by P{personId}.",
                };
                opportunities.Add(opportunity);
                newlyRecognized.Add(opportunity);
                diagnostics.Add($"P{personId} recognized opportunity '{opportunity.OpportunityId}' for " +
                    $"'{pair.Key}' (~{opportunity.EstimatedUnmetUnitsPerDay} unmet units/day witnessed).");
                events?.Record(dayIndex, NpcBusinessEventKind.OpportunityRecognized, personId,
                    string.Empty,
                    $"P{personId} recognized opportunity '{opportunity.OpportunityId}' for '{pair.Key}' " +
                    $"from {list.Count} witnessed observation(s). No business created — investigation comes first.");
            }

            return newlyRecognized;
        }

        private bool AlreadyRecognized(string itemOrServiceId)
        {
            foreach (NpcOpportunity opp in opportunities)
            {
                if (opp != null && string.Equals(opp.ItemOrServiceId, itemOrServiceId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public NpcOpportunity FindOpportunity(string opportunityId)
        {
            foreach (NpcOpportunity opp in opportunities)
            {
                if (opp != null && string.Equals(opp.OpportunityId, opportunityId, StringComparison.Ordinal))
                {
                    return opp;
                }
            }

            return null;
        }

        /// <summary>Marks an opportunity dismissed with its reason — recorded, never silent.</summary>
        public string DismissOpportunity(string opportunityId, string reason, int dayIndex, NpcBusinessEventLog events)
        {
            NpcOpportunity opp = FindOpportunity(opportunityId);
            if (opp == null) return $"Cannot dismiss: no opportunity '{opportunityId}' on P{personId}'s log.";
            if (opp.Status == NpcOpportunityStatus.ActedOn)
            {
                return $"Cannot dismiss '{opportunityId}': already acted on.";
            }

            opp.Status = NpcOpportunityStatus.Dismissed;
            opp.StatusReason = reason ?? string.Empty;
            events?.Record(dayIndex, NpcBusinessEventKind.OpportunityDismissed, personId, string.Empty,
                $"P{personId} dismissed opportunity '{opportunityId}' for '{opp.ItemOrServiceId}': {opp.StatusReason}");
            return null;
        }

        /// <summary>CLN-1: captures the observation log for the save pipeline.</summary>
        public NpcOpportunityObservationLogSaveDto CaptureSaveDto()
        {
            var dto = new NpcOpportunityObservationLogSaveDto
            {
                PersonId = personId,
                HouseholdId = householdId,
                NextObservationSequence = nextObservationSequence,
                NextOpportunitySequence = nextOpportunitySequence,
            };
            if (observations != null) dto.Observations.AddRange(observations);
            if (opportunities != null) dto.Opportunities.AddRange(opportunities);
            return dto;
        }

        /// <summary>CLN-1: restores the observation log from the save pipeline.</summary>
        public void LoadFromSaveDto(NpcOpportunityObservationLogSaveDto dto)
        {
            observations.Clear();
            opportunities.Clear();
            if (dto == null) return;
            personId = dto.PersonId;
            householdId = dto.HouseholdId;
            nextObservationSequence = dto.NextObservationSequence;
            nextOpportunitySequence = dto.NextOpportunitySequence;
            if (dto.Observations != null) observations.AddRange(dto.Observations);
            if (dto.Opportunities != null) opportunities.AddRange(dto.Opportunities);
        }
    }

    /// <summary>CLN-1: persisted per-Person opportunity observations (save pipeline).</summary>
    [Serializable]
    public sealed class NpcOpportunityObservationLogSaveDto
    {
        public int PersonId = -1;
        public int HouseholdId = -1;
        public int NextObservationSequence;
        public int NextOpportunitySequence;
        public List<NpcMarketObservation> Observations = new List<NpcMarketObservation>();
        public List<NpcOpportunity> Opportunities = new List<NpcOpportunity>();

        public NpcOpportunityObservationLogSaveDto() { }
    }
}
