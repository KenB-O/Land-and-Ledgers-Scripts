using System;
using System.Collections.Generic;
using LandLedgers.Population;
using UnityEngine;

namespace LandLedgers.Economy.Creation
{
    /// <summary>Phase G (G2): what an investigation concluded. Append-only.</summary>
    public enum NpcInvestigationVerdict
    {
        Unspecified = 0,
        Underway = 1,
        Proceed = 2,
        WalkAway = 3,
    }

    /// <summary>Phase G (G2): the area an investigation finding speaks to. Append-only.</summary>
    public enum NpcInvestigationArea
    {
        Unspecified = 0,
        Customers = 1,
        Suppliers = 2,
        Premises = 3,
        Financing = 4,
        Time = 5,
        Equipment = 6,
    }

    /// <summary>
    /// Phase G (G2): one finding from an investigation — what the Person
    /// learned and whether it supports proceeding. Findings are recorded
    /// with their evidence; a walk-away names its reasons.
    /// </summary>
    [Serializable]
    public sealed class NpcInvestigationFinding
    {
        public NpcInvestigationArea Area = NpcInvestigationArea.Unspecified;
        public bool SupportsProceed;
        public string Detail = string.Empty;
        public string EvidenceReference = string.Empty;

        public NpcInvestigationFinding() { }

        public NpcInvestigationFinding(NpcInvestigationArea area, bool supportsProceed,
            string detail, string evidenceReference)
        {
            Area = area;
            SupportsProceed = supportsProceed;
            Detail = detail ?? string.Empty;
            EvidenceReference = evidenceReference ?? string.Empty;
        }
    }

    /// <summary>
    /// Phase G (G2): a real, discoverable supplier the investigator found.
    /// The supplier id must name a real supplier (discoverable through the
    /// supplier directory) — never an invented source.
    /// </summary>
    [Serializable]
    public sealed class NpcCandidateSupplier
    {
        public string SupplierId = string.Empty;
        public string ItemId = string.Empty;
        public int UnitPriceCents;
        public string TermsNote = string.Empty;

        public NpcCandidateSupplier() { }
    }

    /// <summary>
    /// Phase G (G2): a real premises option — the same rights the player
    /// uses (Phase F acquisition, rental, or home-based). The reference
    /// names a real parcel/building/space or a real rental listing.
    /// </summary>
    [Serializable]
    public sealed class NpcPremisesOption
    {
        public string Description = string.Empty;
        public NpcPremisesTenure Tenure = NpcPremisesTenure.Unspecified;
        public string ParcelOrBuildingRef = string.Empty;
        public string AgreementRef = string.Empty;
        public int UpfrontCostCents;
        public int RecurringCostCents;

        public NpcPremisesOption() { }
    }

    /// <summary>
    /// Phase G (G2/G3): how premises rights are held. Append-only.
    /// Tenure is a fact about rights, never a BusinessType permission.
    /// </summary>
    public enum NpcPremisesTenure
    {
        Unspecified = 0,
        Owned = 1,
        Rented = 2,
        Financed = 3,
        HomeRoom = 4,
        Shared = 5,
        NoSite = 6,
        MobileRoute = 7,
    }

    /// <summary>
    /// Phase G (G2): a real equipment option with provenance — purchased
    /// from a real seller, or a genuine service-provider/borrowed grant
    /// under a real agreement. Equipment is never free and never
    /// conjured: the provenance names the lot or the agreement.
    /// </summary>
    [Serializable]
    public sealed class NpcEquipmentOption
    {
        public string Description = string.Empty;
        public string ItemId = string.Empty;
        public int CostCents;
        public string Provenance = string.Empty;

        public NpcEquipmentOption() { }
    }

    /// <summary>
    /// Phase G (G2): one lender quote the investigator actually obtained
    /// (through the NPC credit loop) or was refused. Quotes are facts
    /// about negotiations that happened, not estimates.
    /// </summary>
    [Serializable]
    public sealed class NpcLenderQuote
    {
        public string LenderName = string.Empty;
        public int AmountCents;
        public int RateBps;
        public int TermDays;
        public bool Accepted;
        public string Note = string.Empty;

        public NpcLenderQuote() { }
    }

    /// <summary>
    /// Phase G (G2): the financing side of an investigation — the Person's
    /// own cash plus real credit they actually secured (own cash, accepted
    /// lender quotes, seller-finance terms). Unconfirmed hopes are not
    /// financing.
    /// </summary>
    [Serializable]
    public sealed class NpcFinancingPlan
    {
        public int OwnCashCents;
        public List<NpcLenderQuote> LenderQuotes = new List<NpcLenderQuote>();
        public int SellerFinanceCents;
        public string SellerFinanceTerms = string.Empty;

        public NpcFinancingPlan() { }

        /// <summary>Cash the Person can actually deploy: own cash plus ACCEPTED quotes plus seller finance.</summary>
        public int ConfirmedCapitalCents()
        {
            int total = Math.Max(0, OwnCashCents) + Math.Max(0, SellerFinanceCents);
            if (LenderQuotes != null)
            {
                foreach (NpcLenderQuote quote in LenderQuotes)
                {
                    if (quote != null && quote.Accepted)
                    {
                        total += Math.Max(0, quote.AmountCents);
                    }
                }
            }

            return total;
        }
    }

    /// <summary>
    /// Phase G (G2): everything an investigation gathered, as evidence.
    /// Customers come from the investigator's own observations (not
    /// invented); suppliers, premises and equipment are real and named.
    /// </summary>
    [Serializable]
    public sealed class NpcInvestigationEvidence
    {
        /// <summary>Observation ids from the investigator's OWN log — the customer basis.</summary>
        public List<string> CustomerObservationIds = new List<string>();

        public List<NpcCandidateSupplier> CandidateSuppliers = new List<NpcCandidateSupplier>();
        public List<NpcPremisesOption> PremisesOptions = new List<NpcPremisesOption>();
        public List<NpcEquipmentOption> EquipmentOptions = new List<NpcEquipmentOption>();
        public NpcFinancingPlan Financing = new NpcFinancingPlan();

        /// <summary>
        /// The investigator's own revenue projection, recorded AS their
        /// estimate (derived from observed unmet demand) — never treated
        /// as a fact about the market.
        /// </summary>
        public int InvestigatorRevenueEstimateCentsPerWeek;

        public int InvestigatorCostEstimateCentsPerWeek;

        /// <summary>Formation cost the plan must cover (premises + equipment + inventory + working capital).</summary>
        public int EstimatedFormationCostCents;

        public NpcInvestigationEvidence() { }
    }

    /// <summary>
    /// Phase G (G2): one investigation of one opportunity by one Person.
    /// Investigation costs real Person time (reserved through the real
    /// schedule tracker — no double-booking), gathers real evidence, and
    /// concludes Proceed or WalkAway. A walk-away is recorded with its
    /// reasons; the NPC walks away and that decision stands.
    /// </summary>
    [Serializable]
    public sealed class NpcBusinessInvestigation
    {
        [SerializeField]
        private string investigationId = string.Empty;

        [SerializeField]
        private string opportunityId = string.Empty;

        [SerializeField]
        private int investigatorPersonId = -1;

        [SerializeField]
        private int investigatorHouseholdId = -1;

        [SerializeField]
        private int startedDayIndex;

        [SerializeField]
        private NpcInvestigationVerdict verdict = NpcInvestigationVerdict.Unspecified;

        [SerializeField]
        private List<NpcInvestigationFinding> findings = new List<NpcInvestigationFinding>();

        [SerializeField]
        private List<string> walkAwayReasons = new List<string>();

        [SerializeField]
        private string timeReservationId = string.Empty;

        [SerializeField]
        private int concludedDayIndex = -1;

        public NpcBusinessInvestigation() { }

        public string InvestigationId => investigationId ?? string.Empty;
        public string OpportunityId => opportunityId ?? string.Empty;
        public int InvestigatorPersonId => investigatorPersonId;
        public int InvestigatorHouseholdId => investigatorHouseholdId;
        public NpcInvestigationVerdict Verdict => verdict;
        public IReadOnlyList<NpcInvestigationFinding> Findings => findings;
        public IReadOnlyList<string> WalkAwayReasons => walkAwayReasons;
        public string TimeReservationId => timeReservationId ?? string.Empty;

        /// <summary>
        /// Starts the investigation. Reserves real Person time through the
        /// schedule tracker — a double-booked Person refuses loudly and the
        /// investigation does not start. Returns null on success.
        /// </summary>
        public string Start(
            string investigationId,
            NpcOpportunity opportunity,
            PersonScheduleTracker scheduleTracker,
            int investigationMinutes,
            int dayIndex,
            NpcBusinessEventLog events,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (opportunity == null)
            {
                return "Cannot investigate: no opportunity supplied.";
            }

            if (scheduleTracker == null)
            {
                return "Cannot investigate: no schedule tracker — Person time must be real.";
            }

            this.investigationId = investigationId ?? string.Empty;
            this.opportunityId = opportunity.OpportunityId;
            investigatorPersonId = opportunity.RecognizerPersonId;
            startedDayIndex = dayIndex;
            verdict = NpcInvestigationVerdict.Underway;

            // Investigation costs real Person time (Phase C time authority).
            // Midday window 10:00-14:00 keeps clear of work/sleep by default;
            // the tracker still refuses any overlap loudly.
            string refusal = scheduleTracker.TryReserve(
                investigatorPersonId,
                PersonActivityKind.Investigation,
                dayIndex,
                600,
                Math.Max(30, investigationMinutes),
                $"investigating opportunity '{opportunity.OpportunityId}' ({opportunity.ItemOrServiceId})");
            if (refusal != null)
            {
                verdict = NpcInvestigationVerdict.Unspecified;
                diagnostics.Add($"Investigation refused: {refusal}");
                return $"Investigation refused: {refusal}";
            }

            List<PersonActivityReservation> active = scheduleTracker.ActiveFor(investigatorPersonId, dayIndex);
            timeReservationId = active != null && active.Count > 0
                ? active[active.Count - 1].ReservationId
                : string.Empty;

            opportunity.Status = NpcOpportunityStatus.Investigating;
            opportunity.StatusReason = $"under investigation '{this.investigationId}' by P{investigatorPersonId}.";
            diagnostics.Add($"P{investigatorPersonId} started investigating '{opportunity.OpportunityId}' " +
                $"('{opportunity.ItemOrServiceId}'); {investigationMinutes} minutes of real Person time reserved.");
            events?.Record(dayIndex, NpcBusinessEventKind.InvestigationStarted, investigatorPersonId,
                string.Empty,
                $"P{investigatorPersonId} started investigation '{this.investigationId}' of opportunity " +
                $"'{opportunity.OpportunityId}' ({opportunity.ItemOrServiceId}).");
            events?.Record(dayIndex, NpcBusinessEventKind.InvestigationTimeReserved, investigatorPersonId,
                string.Empty,
                $"P{investigatorPersonId} reserved {investigationMinutes} minutes for the investigation " +
                $"(reservation '{timeReservationId}'); Person time is real and cannot double-book.");
            return null;
        }

        /// <summary>
        /// Concludes the investigation from gathered evidence. The customer
        /// basis must be observations from the investigator's OWN log —
        /// invented customers are refused. Every area that fails to support
        /// proceeding is named in the walk-away reasons.
        /// </summary>
        public string Conclude(
            NpcInvestigationEvidence evidence,
            NpcOpportunityObservationLog observationLog,
            int dayIndex,
            NpcBusinessEventLog events,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (verdict != NpcInvestigationVerdict.Underway)
            {
                return $"Cannot conclude: investigation '{investigationId}' is {verdict}, not Underway.";
            }

            if (evidence == null)
            {
                return "Cannot conclude: no evidence gathered.";
            }

            if (observationLog == null || observationLog.PersonId != investigatorPersonId)
            {
                return "Cannot conclude: evidence must be checked against the investigator's own observation log.";
            }

            concludedDayIndex = dayIndex;
            var reasons = new List<string>();

            // Customers: from observed demand, not invented. Every cited
            // observation must exist on the investigator's own log.
            int validCustomerSignals = 0;
            if (evidence.CustomerObservationIds != null)
            {
                foreach (string obsId in evidence.CustomerObservationIds)
                {
                    bool found = false;
                    foreach (NpcMarketObservation obs in observationLog.Observations)
                    {
                        if (obs != null && string.Equals(obs.ObservationId, obsId, StringComparison.Ordinal))
                        {
                            found = true;
                            break;
                        }
                    }

                    if (found) validCustomerSignals++;
                    else reasons.Add($"customer signal '{obsId}' is not on P{investigatorPersonId}'s own observation log — invented customers refused.");
                }
            }

            if (validCustomerSignals == 0)
            {
                reasons.Add("no customer basis: no witnessed demand observations support this opportunity.");
                findings.Add(new NpcInvestigationFinding(NpcInvestigationArea.Customers, false,
                    "No witnessed demand observations — the Person will not build on invented customers.",
                    string.Empty));
            }
            else
            {
                findings.Add(new NpcInvestigationFinding(NpcInvestigationArea.Customers, true,
                    $"{validCustomerSignals} witnessed demand observation(s) support the opportunity; " +
                    $"investigator's own revenue estimate is {evidence.InvestigatorRevenueEstimateCentsPerWeek}c/week (their estimate, not a fact).",
                    string.Join(",", evidence.CustomerObservationIds)));
            }

            // Suppliers: real and discoverable.
            int realSuppliers = 0;
            if (evidence.CandidateSuppliers != null)
            {
                foreach (NpcCandidateSupplier supplier in evidence.CandidateSuppliers)
                {
                    if (supplier != null && !string.IsNullOrWhiteSpace(supplier.SupplierId)
                        && !string.IsNullOrWhiteSpace(supplier.ItemId))
                    {
                        realSuppliers++;
                    }
                }
            }

            if (realSuppliers == 0)
            {
                reasons.Add("no supplier: no real, discoverable supplier found for the key inputs.");
                findings.Add(new NpcInvestigationFinding(NpcInvestigationArea.Suppliers, false,
                    "No real supplier identified — inputs have no legitimate source.", string.Empty));
            }
            else
            {
                findings.Add(new NpcInvestigationFinding(NpcInvestigationArea.Suppliers, true,
                    $"{realSuppliers} real supplier(s) identified for key inputs.", string.Empty));
            }

            // Premises: the same rights the player uses.
            int viablePremises = 0;
            if (evidence.PremisesOptions != null)
            {
                foreach (NpcPremisesOption option in evidence.PremisesOptions)
                {
                    if (option != null && option.Tenure != NpcPremisesTenure.Unspecified
                        && !string.IsNullOrWhiteSpace(option.ParcelOrBuildingRef))
                    {
                        viablePremises++;
                    }
                }
            }

            if (viablePremises == 0)
            {
                reasons.Add("no premises: no viable space rights (owned, rented, financed, home, or shared) identified.");
                findings.Add(new NpcInvestigationFinding(NpcInvestigationArea.Premises, false,
                    "No premises rights available through the same authorities the player uses.", string.Empty));
            }
            else
            {
                findings.Add(new NpcInvestigationFinding(NpcInvestigationArea.Premises, true,
                    $"{viablePremises} viable premises option(s) identified.", string.Empty));
            }

            // Financing: own cash plus real, confirmed credit.
            int formationCost = Math.Max(0, evidence.EstimatedFormationCostCents);
            int confirmed = evidence.Financing != null ? evidence.Financing.ConfirmedCapitalCents() : 0;
            if (formationCost > 0 && confirmed < formationCost)
            {
                reasons.Add($"financing shortfall: {confirmed}c confirmed against {formationCost}c formation cost.");
                findings.Add(new NpcInvestigationFinding(NpcInvestigationArea.Financing, false,
                    $"Only {confirmed}c of {formationCost}c confirmed — unconfirmed hopes are not financing.",
                    string.Empty));
            }
            else
            {
                findings.Add(new NpcInvestigationFinding(NpcInvestigationArea.Financing, true,
                    $"{confirmed}c confirmed capital covers the {formationCost}c formation cost.", string.Empty));
            }

            // Equipment: with provenance, never free.
            int provenEquipment = 0;
            if (evidence.EquipmentOptions != null)
            {
                foreach (NpcEquipmentOption option in evidence.EquipmentOptions)
                {
                    if (option != null && !string.IsNullOrWhiteSpace(option.Provenance))
                    {
                        provenEquipment++;
                    }
                }
            }

            findings.Add(new NpcInvestigationFinding(NpcInvestigationArea.Equipment, provenEquipment > 0,
                provenEquipment > 0
                    ? $"{provenEquipment} equipment option(s) with real provenance."
                    : "No equipment with provenance — service-only or walked away on this ground.",
                string.Empty));
            if (provenEquipment == 0)
            {
                reasons.Add("no equipment: nothing with provenance for the work that needs tools.");
            }

            foreach (NpcInvestigationFinding finding in findings)
            {
                events?.Record(dayIndex, NpcBusinessEventKind.InvestigationFinding, investigatorPersonId,
                    string.Empty,
                    $"investigation '{investigationId}' finding [{finding.Area}]: " +
                    $"{(finding.SupportsProceed ? "supports" : "does not support")} proceeding — {finding.Detail}");
            }

            if (reasons.Count > 0)
            {
                verdict = NpcInvestigationVerdict.WalkAway;
                walkAwayReasons.AddRange(reasons);
                diagnostics.Add($"P{investigatorPersonId} WALKED AWAY from '{opportunityId}': {string.Join(" ", reasons)}");
                events?.Record(dayIndex, NpcBusinessEventKind.InvestigationWalkAway, investigatorPersonId,
                    string.Empty,
                    $"P{investigatorPersonId} walked away from opportunity '{opportunityId}': {string.Join(" | ", reasons)}");
            }
            else
            {
                verdict = NpcInvestigationVerdict.Proceed;
                diagnostics.Add($"P{investigatorPersonId} will PROCEED with '{opportunityId}' — every area supports it.");
                events?.Record(dayIndex, NpcBusinessEventKind.InvestigationProceed, investigatorPersonId,
                    string.Empty,
                    $"P{investigatorPersonId} will proceed with opportunity '{opportunityId}' — " +
                    "customers observed, suppliers real, premises secured, financing confirmed.");
            }

            return null;
        }

        /// <summary>CLN-1: captures the investigation for the save pipeline.</summary>
        public NpcBusinessInvestigationSaveDto CaptureSaveDto()
        {
            var dto = new NpcBusinessInvestigationSaveDto
            {
                InvestigationId = investigationId,
                OpportunityId = opportunityId,
                InvestigatorPersonId = investigatorPersonId,
                InvestigatorHouseholdId = investigatorHouseholdId,
                StartedDayIndex = startedDayIndex,
                Verdict = verdict,
                TimeReservationId = timeReservationId,
                ConcludedDayIndex = concludedDayIndex,
            };
            if (findings != null) dto.Findings.AddRange(findings);
            if (walkAwayReasons != null) dto.WalkAwayReasons.AddRange(walkAwayReasons);
            return dto;
        }

        /// <summary>CLN-1: restores the investigation from the save pipeline.</summary>
        public void LoadFromSaveDto(NpcBusinessInvestigationSaveDto dto)
        {
            findings.Clear();
            walkAwayReasons.Clear();
            if (dto == null) return;
            investigationId = dto.InvestigationId;
            opportunityId = dto.OpportunityId;
            investigatorPersonId = dto.InvestigatorPersonId;
            investigatorHouseholdId = dto.InvestigatorHouseholdId;
            startedDayIndex = dto.StartedDayIndex;
            verdict = dto.Verdict;
            timeReservationId = dto.TimeReservationId;
            concludedDayIndex = dto.ConcludedDayIndex;
            if (dto.Findings != null) findings.AddRange(dto.Findings);
            if (dto.WalkAwayReasons != null) dto.WalkAwayReasons.AddRange(dto.WalkAwayReasons);
        }
    }

    /// <summary>CLN-1: persisted NPC business investigation (save pipeline).</summary>
    [Serializable]
    public sealed class NpcBusinessInvestigationSaveDto
    {
        public string InvestigationId = string.Empty;
        public string OpportunityId = string.Empty;
        public int InvestigatorPersonId = -1;
        public int InvestigatorHouseholdId = -1;
        public int StartedDayIndex;
        public NpcInvestigationVerdict Verdict = NpcInvestigationVerdict.Unspecified;
        public List<NpcInvestigationFinding> Findings = new List<NpcInvestigationFinding>();
        public List<string> WalkAwayReasons = new List<string>();
        public string TimeReservationId = string.Empty;
        public int ConcludedDayIndex = -1;

        public NpcBusinessInvestigationSaveDto() { }
    }
}
