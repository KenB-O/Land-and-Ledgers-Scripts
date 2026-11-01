using System;
using System.Collections.Generic;

namespace LandLedgers.Economy
{
    /// <summary>
    /// D4G: building contracts — bids, specs, acceptance for the Construction
    /// &amp; Contracting business archetype. Record types live here; the
    /// lifecycle state machine is <see cref="ConstructionContract"/> and the
    /// town registry is <see cref="ConstructionContractBook"/>.
    ///
    /// Canon: Part VI §7 Construction &amp; Contracting Business Archetype —
    /// §7.1 (supported work: new buildings, additions, repairs, structural
    /// work, painting, roofing, renovations, outbuildings, property
    /// maintenance), §7.2 (bidding and estimating — quotes built from labor,
    /// materials, transport, subcontracting, tools/equipment burden, duration,
    /// contingency, desired return), §7.3 (backlog is not revenue; contracts
    /// may use deposits, delivered-material payments, milestone/progress
    /// payments, completion balances — exact percentages are contract/project
    /// data, never canon values), §7.4 (subcontractors as normal practice),
    /// §7.5 (organic scaling — capacity comes from real people, tools, cash,
    /// never a Contractor Level). Audit 04 locks honored throughout:
    /// Project Estimate is not Final Cost; Materials are consumed when work
    /// occurs; Construction quality persists into property history; Project
    /// Authorization is not Completion.
    ///
    /// Historical basis for price practice (web research, Oct 2026 — canon is
    /// silent on price basis): in the 1870s US, defined building work was
    /// overwhelmingly let LUMP SUM, or trade-by-trade on measure-and-value
    /// (unit rates); cost-plus-percentage existed but was rare outside railway
    /// contracting. The price basis is therefore contract DATA — the book
    /// records whichever basis the parties agreed, it never steers them.
    /// </summary>

    /// <summary>D4G: the kind of described works a contract or bid covers (Canon §7.1).</summary>
    public enum ConstructionWorksKind
    {
        Unspecified = 0,
        NewBuilding = 1,
        Addition = 2,
        Repair = 3,
        Renovation = 4,
        Outbuilding = 5,
        StructuralWork = 6,
        PaintingWork = 7,
        RoofingWork = 8,
        PropertyMaintenance = 9,
    }

    /// <summary>
    /// D4G: how the contract price is set. Research-grounded (see file doc),
    /// recorded as data — never steered by the book.
    /// </summary>
    public enum ConstructionContractPriceBasis
    {
        Unspecified = 0,
        /// <summary>A fixed price for the described works; adjustments come only through accepted change orders.</summary>
        LumpSum = 1,
        /// <summary>Documented cost reimbursed plus an agreed percentage fee. Rare in period building work; attested in railway contracting.</summary>
        CostPlusPercentage = 2,
        /// <summary>Documented cost reimbursed plus an agreed fixed fee.</summary>
        CostPlusFixedFee = 3,
    }

    /// <summary>D4G: which side of the contract a party sits on.</summary>
    public enum ConstructionContractSide
    {
        Unspecified = 0,
        Owner = 1,   // the party commissioning the works
        Builder = 2, // the Builder business performing the works
    }

    /// <summary>D4G: one party to a contract or bid — a real person or a real business. No anonymous parties.</summary>
    [Serializable]
    public sealed class ConstructionContractParty
    {
        public bool IsPerson;             // true = person, false = business
        public int PersonId = -1;         // real person id when IsPerson (mirrors BusinessOwnerIdentity)
        public string BusinessInstanceId = string.Empty; // real business instance id when !IsPerson
        public BusinessType BusinessType = BusinessType.GeneralStore; // meaningful when !IsPerson
        public string DisplayName = string.Empty;

        public ConstructionContractParty() { }

        public static ConstructionContractParty Person(int personId, string displayName)
        {
            return new ConstructionContractParty
            {
                IsPerson = true,
                PersonId = personId,
                DisplayName = displayName ?? string.Empty,
            };
        }

        public static ConstructionContractParty Business(string instanceId, BusinessType businessType, string displayName)
        {
            return new ConstructionContractParty
            {
                IsPerson = false,
                BusinessInstanceId = instanceId ?? string.Empty,
                BusinessType = businessType,
                DisplayName = displayName ?? string.Empty,
            };
        }

        /// <summary>D4G: a party is real only when it names someone or something that exists.</summary>
        public bool IsReal =>
            !string.IsNullOrWhiteSpace(DisplayName)
            && (IsPerson ? PersonId >= 0 : !string.IsNullOrWhiteSpace(BusinessInstanceId));

        public string Describe()
        {
            if (IsPerson) return $"person {DisplayName} (id {PersonId})";
            return $"business {DisplayName} ({BusinessType}, {BusinessInstanceId})";
        }
    }

    /// <summary>D4G: how a material requirement constrains provenance. Links to
    /// existing material/lot systems by reference — never duplicates them.</summary>
    public enum ConstructionMaterialProvenanceKind
    {
        Unspecified = 0,
        /// <summary>Any real supplier; the builder sources honestly and the provenance is recorded when material arrives.</summary>
        AnyRealSource = 1,
        /// <summary>Must come from a named lumber yard (links LumberYardLumberLot by lot reference).</summary>
        NamedYard = 2,
        /// <summary>Must come from a named sawmill (links the sawmill lumber lot by lot reference).</summary>
        NamedSawmill = 3,
        /// <summary>Must come from a named supplier business of another kind.</summary>
        NamedSupplier = 4,
        /// <summary>Already earmarked for this project in a yard project stockpile (Canon §6.4; links the earmark lot).</summary>
        StockpileEarmark = 5,
    }

    /// <summary>D4G: material kinds a spec line can require. Kept coarse — the
    /// lot systems (sawmill W4A, lumber-yard W4B) carry the fine detail.</summary>
    public enum ConstructionMaterialKind
    {
        Unspecified = 0,
        Lumber = 1,
        Hardware = 2,   // nails, hinges, fasteners
        Masonry = 3,    // stone, brick, lime, mortar
        Paint = 4,
        Glass = 5,
        Roofing = 6,
        Other = 7,
    }

    /// <summary>
    /// D4G: one material requirement inside a spec section — DATA. The
    /// provenance requirement names real suppliers or lot references; the
    /// yard/sawmill lot systems remain the authority for what exists.
    /// </summary>
    [Serializable]
    public sealed class ConstructionMaterialRequirement
    {
        public string RequirementId = string.Empty;   // REQ-0001 within the contract
        public ConstructionMaterialKind MaterialKind = ConstructionMaterialKind.Unspecified;
        public string MaterialDisplayName = string.Empty;
        public int RequiredUnits;
        public string UnitLabel = string.Empty;        // "board feet", "pounds", "squares"...
        public string GradeId = string.Empty;         // lumber grade id (LumberYardGradeCatalog) when MaterialKind == Lumber
        public ConstructionMaterialProvenanceKind ProvenanceKind = ConstructionMaterialProvenanceKind.Unspecified;
        public string SupplierBusinessId = string.Empty; // the named real supplier for Named* kinds
        public BusinessType SupplierBusinessType = BusinessType.GeneralStore;
        public string SupplierDisplayName = string.Empty;
        public string LotReferenceId = string.Empty;  // references an existing lot (yard/sawmill), never copied data
        public string Notes = string.Empty;

        public ConstructionMaterialRequirement() { }

        public bool HasNamedSupplier =>
            ProvenanceKind == ConstructionMaterialProvenanceKind.NamedYard
            || ProvenanceKind == ConstructionMaterialProvenanceKind.NamedSawmill
            || ProvenanceKind == ConstructionMaterialProvenanceKind.NamedSupplier;

        /// <summary>D4G: a named-supplier requirement without a named supplier is not a requirement.</summary>
        public bool IsCoherent =>
            RequiredUnits > 0
            && !string.IsNullOrWhiteSpace(MaterialDisplayName)
            && (!HasNamedSupplier || !string.IsNullOrWhiteSpace(SupplierBusinessId));
    }

    /// <summary>
    /// D4G: one spec section of the written contract — materials, dimensions,
    /// standards as DATA. The acceptance stages test against these sections.
    /// </summary>
    [Serializable]
    public sealed class ConstructionSpecSection
    {
        public string SectionId = string.Empty;       // SPEC-0001 within the contract
        public string SectionName = string.Empty;     // "Framing", "Roofing"...
        public string Description = string.Empty;
        public string Dimensions = string.Empty;      // free text, e.g. "24 by 36 ft, 10 ft walls"
        public string Standards = string.Empty;       // free text, e.g. "clear grade finish lumber"
        public int EstimatedLaborCrewDays;
        public List<ConstructionMaterialRequirement> Materials = new List<ConstructionMaterialRequirement>();

        public ConstructionSpecSection() { }

        public int TotalMaterialLines => Materials != null ? Materials.Count : 0;
    }

    /// <summary>D4G: one line of the builder's estimate behind a bid (Canon §7.2:
    /// labor, materials, transport, subcontracting, tools/equipment burden,
    /// duration, contingency, desired return). The bid's PriceCents is the
    /// binding ask; these lines are the recorded reasoning.</summary>
    [Serializable]
    public sealed class ConstructionBidEstimateLine
    {
        public string ComponentName = string.Empty; // "labor", "materials", "transport", ...
        public int AmountCents;

        public ConstructionBidEstimateLine() { }
    }

    /// <summary>D4G: bid lifecycle. Rejected bids are RETAINED as history — never deleted.</summary>
    public enum ConstructionBidStatus
    {
        Unspecified = 0,
        Submitted = 1,  // on the table, awaiting the owner's decision
        Accepted = 2,   // the owner accepted — this bid became a contract
        Rejected = 3,   // the owner chose another bid (retained as history)
        Withdrawn = 4,  // the builder withdrew before a decision
    }

    /// <summary>
    /// D4G: one competing bid on a bid request — the builder, its price, its
    /// terms. Named bidders only.
    /// </summary>
    [Serializable]
    public sealed class ConstructionBid
    {
        public string BidId = string.Empty;           // BID-0001 within the book
        public string RequestId = string.Empty;
        public string BuilderBusinessId = string.Empty; // real Builder business
        public string BuilderDisplayName = string.Empty;
        public ConstructionBidStatus Status = ConstructionBidStatus.Unspecified;
        public ConstructionContractPriceBasis PriceBasis = ConstructionContractPriceBasis.Unspecified;
        public int PriceCents;                        // lump-sum ask, or estimated total for cost-plus
        public int CostPlusFeePercent;                // whole percent, for CostPlusPercentage
        public int CostPlusFixedFeeCents;             // for CostPlusFixedFee
        public int EstimatedCostCents;                // builder's cost estimate behind a cost-plus bid
        public int OfferedStartDayIndex = -1;
        public int OfferedDurationDays;
        public ConstructionPaymentTerms PaymentTerms = new ConstructionPaymentTerms();
        public List<ConstructionBidEstimateLine> EstimateLines = new List<ConstructionBidEstimateLine>();
        public int SubmittedDayIndex;
        public string RejectionReason = string.Empty;

        public ConstructionBid() { }

        public int OfferedCompletionDayIndex => OfferedStartDayIndex >= 0
            ? OfferedStartDayIndex + Math.Max(0, OfferedDurationDays)
            : -1;

        public bool IsLive => Status == ConstructionBidStatus.Submitted;

        public int EstimateLinesTotalCents()
        {
            int total = 0;
            if (EstimateLines != null)
            {
                foreach (ConstructionBidEstimateLine line in EstimateLines)
                {
                    if (line != null) total += Math.Max(0, line.AmountCents);
                }
            }
            return total;
        }
    }

    /// <summary>D4G: bid-request lifecycle. No automatic expiry — the book's
    /// SweepExpiredRequests marks them explicitly.</summary>
    public enum ConstructionBidRequestStatus
    {
        Unspecified = 0,
        Open = 1,      // accepting bids
        Awarded = 2,   // a bid was accepted — the request is closed
        Cancelled = 3, // the owner withdrew the request
        Expired = 4,   // bid deadline passed with no award (explicit sweep)
    }

    /// <summary>
    /// D4G: an owner's written invitation for builders to bid — the works
    /// described, the spec summary, the invited builders (empty = open call),
    /// the bid deadline.
    /// </summary>
    [Serializable]
    public sealed class ConstructionBidRequest
    {
        public string RequestId = string.Empty;       // REQ-BID-0001 within the book
        public ConstructionContractParty OwnerParty = new ConstructionContractParty();
        public ConstructionWorksKind WorksKind = ConstructionWorksKind.Unspecified;
        public string WorksDescription = string.Empty;
        public string SpecSummary = string.Empty;     // free text; the full spec sections live on the formed contract
        public int EstimatedValueCents;               // owner's expectation; gates the repair threshold
        public int CreatedDayIndex;
        public int BidDeadlineDayIndex = -1;
        public List<string> InvitedBuilderBusinessIds = new List<string>(); // empty = open to any builder
        public ConstructionBidRequestStatus Status = ConstructionBidRequestStatus.Unspecified;
        public string AwardedBidId = string.Empty;

        public ConstructionBidRequest() { }

        public bool InvitesBuilder(string builderBusinessId)
        {
            if (InvitedBuilderBusinessIds == null || InvitedBuilderBusinessIds.Count == 0) return true;
            if (string.IsNullOrWhiteSpace(builderBusinessId)) return false;
            foreach (string id in InvitedBuilderBusinessIds)
            {
                if (string.Equals(id, builderBusinessId, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// D4G: the payment terms the parties agreed — DATA for the D4H
    /// progress-billing package to consume. Canon §7.3: deposits,
    /// delivered-material payments, milestone/progress payments, completion
    /// balances are all legitimate; exact percentages are contract data.
    /// </summary>
    [Serializable]
    public sealed class ConstructionPaymentTerms
    {
        public int DepositCents;                      // due at signing
        public List<ConstructionPaymentMilestone> Milestones = new List<ConstructionPaymentMilestone>();
        public int CompletionBalanceCents;            // due on final acceptance
        public string Notes = string.Empty;           // e.g. "delivered-material payments against yard receipts"

        public ConstructionPaymentTerms() { }

        public int ScheduledTotalCents()
        {
            int total = Math.Max(0, DepositCents) + Math.Max(0, CompletionBalanceCents);
            if (Milestones != null)
            {
                foreach (ConstructionPaymentMilestone m in Milestones)
                {
                    if (m != null) total += Math.Max(0, m.AmountCents);
                }
            }
            return total;
        }
    }

    /// <summary>D4G: one named milestone payment inside the agreed terms.</summary>
    [Serializable]
    public sealed class ConstructionPaymentMilestone
    {
        public string MilestoneId = string.Empty;     // MILE-0001 within the contract
        public string Name = string.Empty;            // "foundation complete"
        public string TriggerDescription = string.Empty; // what earns it, in words
        public int AmountCents;

        public ConstructionPaymentMilestone() { }
    }

    /// <summary>D4G: payment-obligation lifecycle. Obligations are RECORDED,
    /// never auto-paid — settlement happens in the ledgers, outside this book.</summary>
    public enum ConstructionPaymentObligationStatus
    {
        Unspecified = 0,
        Recorded = 1,        // the obligation exists; not yet due/released
        Released = 2,        // due and released to the payee (still not auto-paid)
        SettledExternally = 3, // the ledgers report it settled — recorded here, never moved by this book
    }

    /// <summary>D4G: one payment obligation on a contract (deposit, milestone, final balance).</summary>
    [Serializable]
    public sealed class ConstructionPaymentObligation
    {
        public string ObligationId = string.Empty;    // PAY-0001 within the contract
        public string ContractId = string.Empty;
        public string Kind = string.Empty;            // "deposit", "milestone:<id>", "final-balance"
        public string Description = string.Empty;
        public int AmountCents;
        public string PayeeDisplayName = string.Empty;
        public int RecordedDayIndex = -1;
        public int ReleasedDayIndex = -1;
        public ConstructionPaymentObligationStatus Status = ConstructionPaymentObligationStatus.Unspecified;

        public ConstructionPaymentObligation() { }

        public bool IsReleased => Status == ConstructionPaymentObligationStatus.Released
            || Status == ConstructionPaymentObligationStatus.SettledExternally;
    }

    /// <summary>D4G: change-order lifecycle. A change order adjusts price/time
    /// only when BOTH parties have accepted it.</summary>
    public enum ConstructionChangeOrderStatus
    {
        Unspecified = 0,
        Proposed = 1,   // one side proposed; awaiting the other side
        Accepted = 2,   // both sides accepted — price/time adjusted
        Rejected = 3,   // a side rejected — retained as history
    }

    /// <summary>
    /// D4G: a spec amendment — price and time adjustments accepted by both
    /// parties. Canon: Project Estimate is not Final Cost.
    /// </summary>
    [Serializable]
    public sealed class ConstructionChangeOrder
    {
        public string ChangeOrderId = string.Empty;   // CO-0001 within the contract
        public string ContractId = string.Empty;
        public string Description = string.Empty;
        public int PriceDeltaCents;                   // may be negative (a deletion)
        public int TimeDeltaDays;                     // may be negative
        public ConstructionContractSide ProposedBy = ConstructionContractSide.Unspecified;
        public int ProposedDayIndex = -1;
        public int OwnerAcceptedDayIndex = -1;
        public int BuilderAcceptedDayIndex = -1;
        public ConstructionChangeOrderStatus Status = ConstructionChangeOrderStatus.Unspecified;

        public ConstructionChangeOrder() { }

        public bool OwnerAccepted => OwnerAcceptedDayIndex >= 0;
        public bool BuilderAccepted => BuilderAcceptedDayIndex >= 0;
        public bool BothAccepted => OwnerAccepted && BuilderAccepted;
    }

    /// <summary>D4G: acceptance-stage lifecycle. Stages are accepted in order;
    /// defects are recorded against stages, never auto-fixed.</summary>
    public enum ConstructionAcceptanceStageStatus
    {
        Unspecified = 0,
        Pending = 1,
        Accepted = 2,
    }

    /// <summary>D4G: one staged acceptance against the specs.</summary>
    [Serializable]
    public sealed class ConstructionAcceptanceStage
    {
        public string StageId = string.Empty;         // STAGE-0001 within the contract
        public string StageName = string.Empty;
        public string SpecCriteria = string.Empty;    // what "done" means, in words
        public ConstructionAcceptanceStageStatus Status = ConstructionAcceptanceStageStatus.Unspecified;
        public int AcceptedDayIndex = -1;
        public string AcceptedBy = string.Empty;

        public ConstructionAcceptanceStage() { }

        public bool IsAccepted => Status == ConstructionAcceptanceStageStatus.Accepted;
    }

    /// <summary>D4G: defect lifecycle. Defects are RECORDED — remedy happens
    /// outside this book (a repair works order, a later pass); nothing here
    /// auto-fixes them.</summary>
    public enum ConstructionDefectStatus
    {
        Unspecified = 0,
        Open = 1,        // recorded; the builder has not acknowledged it
        Acknowledged = 2, // the builder acknowledges the defect (still outstanding)
        Remedied = 3,    // the owner is satisfied with the remedy — recorded, not performed here
        Waived = 4,      // the owner waives the defect — recorded
    }

    /// <summary>D4G: one defect recorded against an acceptance stage.</summary>
    [Serializable]
    public sealed class ConstructionDefectRecord
    {
        public string DefectId = string.Empty;        // DEF-0001 within the contract
        public string ContractId = string.Empty;
        public string StageId = string.Empty;
        public string Description = string.Empty;
        public int RecordedDayIndex = -1;
        public string RecordedBy = string.Empty;
        public ConstructionDefectStatus Status = ConstructionDefectStatus.Unspecified;
        public int StatusDayIndex = -1;

        public ConstructionDefectRecord() { }

        /// <summary>D4G: a defect blocks final acceptance while it is still outstanding.</summary>
        public bool BlocksFinalAcceptance =>
            Status == ConstructionDefectStatus.Open || Status == ConstructionDefectStatus.Acknowledged;
    }

    /// <summary>
    /// D4G: a capacity reading for one builder business — real crew numbers
    /// from the existing workforce model (BusinessRuntimeState worker slots:
    /// ActiveWorkerCount = paid-active workers, TargetWorkerCount = slots).
    /// The book never invents crew; an adapter reads the real runtime state.
    /// </summary>
    [Serializable]
    public sealed class ConstructionContractCapacityReading
    {
        public string BuilderBusinessId = string.Empty;
        public string BuilderDisplayName = string.Empty;
        public int ActiveCrewCount;
        public int TargetCrewCount;
        public string SourceLabel = string.Empty; // e.g. "BusinessRuntimeState worker slots"

        public ConstructionContractCapacityReading() { }

        public bool HasReading =>
            !string.IsNullOrWhiteSpace(BuilderBusinessId)
            && !string.IsNullOrWhiteSpace(SourceLabel);
    }

    /// <summary>
    /// D4G: the crew/throughput capacity rule. Canon §7.5 — scale comes from
    /// real people, never a Contractor Level. The per-contract crew minimum is
    /// CALIBRATION (the canon gives no number): a building contract ties up at
    /// least a foreman and a hand.
    /// </summary>
    public static class ConstructionContractCapacityRules
    {
        public const int MinActiveWorkersPerContract = 2;

        public static int MaxConcurrentContracts(ConstructionContractCapacityReading reading)
        {
            if (reading == null || !reading.HasReading) return 0;
            return Math.Max(0, reading.ActiveCrewCount) / MinActiveWorkersPerContract;
        }

        /// <summary>
        /// D4G: may this builder take one more contract? Refusal text, or null.
        /// </summary>
        public static string CheckCapacity(ConstructionContractCapacityReading reading, int activeContractCount)
        {
            if (reading == null || !reading.HasReading)
            {
                return "ConstructionContractCapacityRules: no real crew reading for the builder — capacity cannot be invented, contract refused.";
            }
            int max = MaxConcurrentContracts(reading);
            if (max <= 0)
            {
                return $"ConstructionContractCapacityRules: {reading.BuilderDisplayName} has {Math.Max(0, reading.ActiveCrewCount)} active crew — no building contract can be crewed (source: {reading.SourceLabel}).";
            }
            if (activeContractCount >= max)
            {
                return $"ConstructionContractCapacityRules: {reading.BuilderDisplayName} already holds {activeContractCount} active contract(s) against a crew of {reading.ActiveCrewCount} (max {max}) — overbooking refused (Canon §7.5).";
            }
            return null;
        }
    }

    /// <summary>
    /// D4G: one billing event the D4H progress-billing implementation records
    /// against a contract. D4G defines the carrier; D4H owns the behavior.
    /// </summary>
    [Serializable]
    public sealed class ConstructionBillingEvent
    {
        public string EventId = string.Empty;
        public string ContractId = string.Empty;
        public string ObligationId = string.Empty;
        public string Kind = string.Empty; // "deposit", "milestone", "final-balance", "settlement"
        public int AmountCents;
        public int DayIndex = -1;
        public string Note = string.Empty;

        public ConstructionBillingEvent() { }
    }
}
