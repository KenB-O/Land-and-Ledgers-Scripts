using System;
using System.Collections.Generic;

namespace LandLedgers.Economy
{
    /// <summary>D4G: contract lifecycle. No completion without acceptance.</summary>
    public enum ConstructionContractStatus
    {
        Unspecified = 0,
        Draft = 1,              // written up, not yet binding
        Executed = 2,           // both real parties bound; work authorized but not started
        InProgress = 3,         // work underway
        AcceptanceInProgress = 4, // at least one stage accepted
        Accepted = 5,           // final acceptance recorded — final payment obligation released
        Closed = 6,             // settled and archived
        Cancelled = 7,          // withdrawn before completion
    }

    /// <summary>
    /// D4G: the written building contract — one owner (person or business)
    /// commissions one Builder business for described works. Specs, price,
    /// timeline, and payment terms are DATA on the record; staged acceptance,
    /// change orders, and defects are recorded here, never auto-resolved.
    ///
    /// Canon: Part VI §7 (Construction &amp; Contracting archetype), §7.3
    /// (payment timing as contract data), Audit 04 locks (Project Estimate is
    /// not Final Cost; Project Authorization is not Completion).
    /// </summary>
    [Serializable]
    public sealed class ConstructionContract
    {
        public string ContractId = string.Empty;      // CTR-0001 within the book
        public ConstructionContractParty OwnerParty = new ConstructionContractParty();
        public ConstructionContractParty BuilderParty = new ConstructionContractParty(); // must be a Builder business
        public ConstructionWorksKind WorksKind = ConstructionWorksKind.Unspecified;
        public string WorksDescription = string.Empty;
        public List<ConstructionSpecSection> SpecSections = new List<ConstructionSpecSection>();
        public ConstructionContractPriceBasis PriceBasis = ConstructionContractPriceBasis.Unspecified;
        public int AgreedPriceCents;                  // lump-sum price, or current estimated total for cost-plus
        public int CostPlusFeePercent;                // whole percent, for CostPlusPercentage
        public int CostPlusFixedFeeCents;             // for CostPlusFixedFee
        public int EstimatedCostCents;                // cost estimate behind a cost-plus price
        public int AgreedStartDayIndex = -1;
        public int AgreedCompletionDayIndex = -1;
        public ConstructionPaymentTerms PaymentTerms = new ConstructionPaymentTerms();
        public List<ConstructionAcceptanceStage> AcceptanceStages = new List<ConstructionAcceptanceStage>();
        public List<ConstructionDefectRecord> Defects = new List<ConstructionDefectRecord>();
        public List<ConstructionChangeOrder> ChangeOrders = new List<ConstructionChangeOrder>();
        public List<ConstructionPaymentObligation> PaymentObligations = new List<ConstructionPaymentObligation>();
        public ConstructionContractStatus Status = ConstructionContractStatus.Unspecified;
        public string AcceptedBidId = string.Empty;   // the bid that became this contract
        public int FormedDayIndex = -1;
        public int WorkStartedDayIndex = -1;
        public int FinalAcceptedDayIndex = -1;

        private int nextChangeOrderNumber = 1;
        private int nextDefectNumber = 1;
        private int nextObligationNumber = 1;

        public ConstructionContract() { }

        public bool IsActive =>
            Status == ConstructionContractStatus.Executed
            || Status == ConstructionContractStatus.InProgress
            || Status == ConstructionContractStatus.AcceptanceInProgress;

        public bool IsTerminal =>
            Status == ConstructionContractStatus.Accepted
            || Status == ConstructionContractStatus.Closed
            || Status == ConstructionContractStatus.Cancelled;

        /// <summary>
        /// D4G: operator-policy default acceptance stages per works kind.
        /// CALIBRATION — the canon mandates staged acceptance against specs
        /// but prescribes no stage list.
        /// </summary>
        public static List<string> BuildDefaultStageNames(ConstructionWorksKind worksKind)
        {
            switch (worksKind)
            {
                case ConstructionWorksKind.NewBuilding:
                    return new List<string> { "Site and foundation", "Framing", "Enclosed and roofed", "Finish and fit-out" };
                case ConstructionWorksKind.Addition:
                    return new List<string> { "Tie-in and structure", "Enclosed", "Finish" };
                case ConstructionWorksKind.Renovation:
                    return new List<string> { "Stripped and prepared", "Rebuilt", "Finished" };
                case ConstructionWorksKind.Outbuilding:
                    return new List<string> { "Foundation and framing", "Enclosed and finished" };
                case ConstructionWorksKind.StructuralWork:
                    return new List<string> { "Structural work complete" };
                case ConstructionWorksKind.RoofingWork:
                    return new List<string> { "Roofing complete" };
                case ConstructionWorksKind.PaintingWork:
                    return new List<string> { "Painting complete" };
                case ConstructionWorksKind.Repair:
                    return new List<string> { "Repair complete per specs" };
                case ConstructionWorksKind.PropertyMaintenance:
                    return new List<string> { "Maintenance round complete" };
                default:
                    return new List<string> { "Works complete per specs" };
            }
        }

        /// <summary>
        /// D4G: forms the written contract from an accepted bid and the owner's
        /// bid request. Refuses unless BOTH parties are real and the builder
        /// party is a Builder business — no fiat contracts.
        /// </summary>
        public static string FormFromAcceptedBid(
            ConstructionContract contract,
            ConstructionBidRequest request,
            ConstructionBid bid,
            List<string> stageNames,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (contract == null) return "ConstructionContract: no contract record supplied.";
            if (request == null) return "ConstructionContract: no bid request supplied.";
            if (bid == null) return "ConstructionContract: no accepted bid supplied.";
            if (request.OwnerParty == null || !request.OwnerParty.IsReal)
                return "ConstructionContract: the owner party is not real — no contract without two real parties.";
            if (bid.BuilderBusinessId == null || string.IsNullOrWhiteSpace(bid.BuilderBusinessId)
                || string.IsNullOrWhiteSpace(bid.BuilderDisplayName))
                return "ConstructionContract: the builder party is not real — no contract without two real parties.";

            contract.ContractId = contract.ContractId ?? string.Empty;
            contract.OwnerParty = ConstructionContractPartyClone(request.OwnerParty);
            contract.BuilderParty = ConstructionContractParty.Business(
                bid.BuilderBusinessId, BusinessType.Builder, bid.BuilderDisplayName);
            contract.WorksKind = request.WorksKind;
            contract.WorksDescription = request.WorksDescription ?? string.Empty;
            contract.PriceBasis = bid.PriceBasis;
            contract.AgreedPriceCents = Math.Max(0, bid.PriceCents);
            contract.CostPlusFeePercent = Math.Max(0, bid.CostPlusFeePercent);
            contract.CostPlusFixedFeeCents = Math.Max(0, bid.CostPlusFixedFeeCents);
            contract.EstimatedCostCents = Math.Max(0, bid.EstimatedCostCents);
            contract.AgreedStartDayIndex = bid.OfferedStartDayIndex;
            contract.AgreedCompletionDayIndex = bid.OfferedCompletionDayIndex;
            contract.PaymentTerms = ClonePaymentTerms(bid.PaymentTerms);
            contract.AcceptedBidId = bid.BidId ?? string.Empty;
            contract.FormedDayIndex = dayIndex;
            contract.Status = ConstructionContractStatus.Executed;

            List<string> names = stageNames ?? BuildDefaultStageNames(request.WorksKind);
            contract.AcceptanceStages.Clear();
            for (int i = 0; i < names.Count; i++)
            {
                contract.AcceptanceStages.Add(new ConstructionAcceptanceStage
                {
                    StageId = $"STAGE-{(i + 1):D4}",
                    StageName = names[i] ?? $"Stage {i + 1}",
                    SpecCriteria = request.SpecSummary ?? string.Empty,
                    Status = ConstructionAcceptanceStageStatus.Pending,
                });
            }

            if (contract.PaymentTerms != null && contract.PaymentTerms.DepositCents > 0)
            {
                contract.PaymentObligations.Add(new ConstructionPaymentObligation
                {
                    ObligationId = $"PAY-{contract.nextObligationNumber++:D4}",
                    ContractId = contract.ContractId,
                    Kind = "deposit",
                    Description = "Contract deposit due at signing",
                    AmountCents = Math.Max(0, contract.PaymentTerms.DepositCents),
                    PayeeDisplayName = bid.BuilderDisplayName,
                    RecordedDayIndex = dayIndex,
                    Status = ConstructionPaymentObligationStatus.Recorded,
                });
            }

            diag.Add($"ConstructionContract: {contract.ContractId} executed — " +
                $"{contract.OwnerParty.Describe()} commissions {contract.BuilderParty.Describe()} " +
                $"for {contract.WorksDescription} at {FormatMoney(contract.AgreedPriceCents)} ({contract.PriceBasis}).");
            return null;
        }

        private static ConstructionContractParty ConstructionContractPartyClone(ConstructionContractParty party)
        {
            if (party == null) return new ConstructionContractParty();
            return party.IsPerson
                ? ConstructionContractParty.Person(party.PersonId, party.DisplayName)
                : ConstructionContractParty.Business(party.BusinessInstanceId, party.BusinessType, party.DisplayName);
        }

        private static ConstructionPaymentTerms ClonePaymentTerms(ConstructionPaymentTerms terms)
        {
            var clone = new ConstructionPaymentTerms();
            if (terms == null) return clone;
            clone.DepositCents = Math.Max(0, terms.DepositCents);
            clone.CompletionBalanceCents = Math.Max(0, terms.CompletionBalanceCents);
            clone.Notes = terms.Notes ?? string.Empty;
            if (terms.Milestones != null)
            {
                foreach (ConstructionPaymentMilestone m in terms.Milestones)
                {
                    if (m == null) continue;
                    clone.Milestones.Add(new ConstructionPaymentMilestone
                    {
                        MilestoneId = m.MilestoneId ?? string.Empty,
                        Name = m.Name ?? string.Empty,
                        TriggerDescription = m.TriggerDescription ?? string.Empty,
                        AmountCents = Math.Max(0, m.AmountCents),
                    });
                }
            }
            return clone;
        }

        public string StartWork(int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (Status != ConstructionContractStatus.Executed)
                return $"ConstructionContract: {ContractId} is {Status}, not Executed — work cannot start.";
            Status = ConstructionContractStatus.InProgress;
            WorkStartedDayIndex = dayIndex;
            diag.Add($"ConstructionContract: {ContractId} work started on day {dayIndex}.");
            return null;
        }

        /// <summary>
        /// D4G: accepts one stage against the specs. Stages are accepted in
        /// order (operator policy) — a later stage cannot be accepted while an
        /// earlier one is still pending.
        /// </summary>
        public string AcceptStage(string stageId, int dayIndex, string acceptedBy, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (Status != ConstructionContractStatus.InProgress
                && Status != ConstructionContractStatus.AcceptanceInProgress)
                return $"ConstructionContract: {ContractId} is {Status} — stages are accepted only while work is underway.";
            ConstructionAcceptanceStage stage = FindStage(stageId);
            if (stage == null) return $"ConstructionContract: {ContractId} has no stage '{stageId}'.";
            if (stage.IsAccepted) return $"ConstructionContract: {ContractId} stage '{stageId}' is already accepted.";
            int index = AcceptanceStages.IndexOf(stage);
            for (int i = 0; i < index; i++)
            {
                if (!AcceptanceStages[i].IsAccepted)
                    return $"ConstructionContract: {ContractId} stage '{stageId}' cannot be accepted before '{AcceptanceStages[i].StageId}'.";
            }
            stage.Status = ConstructionAcceptanceStageStatus.Accepted;
            stage.AcceptedDayIndex = dayIndex;
            stage.AcceptedBy = acceptedBy ?? string.Empty;
            Status = ConstructionContractStatus.AcceptanceInProgress;
            diag.Add($"ConstructionContract: {ContractId} stage '{stage.StageName}' accepted on day {dayIndex} by {stage.AcceptedBy}.");
            return null;
        }

        /// <summary>D4G: records a defect against a stage. Recorded — never auto-fixed.</summary>
        public string RecordDefect(string stageId, string description, string recordedBy, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (IsTerminal)
                return $"ConstructionContract: {ContractId} is {Status} — defects cannot be recorded on a closed contract.";
            if (string.IsNullOrWhiteSpace(description))
                return $"ConstructionContract: {ContractId} — a defect needs a description.";
            ConstructionAcceptanceStage stage = FindStage(stageId);
            if (stage == null) return $"ConstructionContract: {ContractId} has no stage '{stageId}'.";
            var defect = new ConstructionDefectRecord
            {
                DefectId = $"DEF-{nextDefectNumber++:D4}",
                ContractId = ContractId,
                StageId = stageId,
                Description = description,
                RecordedDayIndex = dayIndex,
                RecordedBy = recordedBy ?? string.Empty,
                Status = ConstructionDefectStatus.Open,
                StatusDayIndex = dayIndex,
            };
            Defects.Add(defect);
            diag.Add($"ConstructionContract: {ContractId} defect {defect.DefectId} recorded against '{stage.StageName}': {description}");
            return null;
        }

        public string SetDefectStatus(string defectId, ConstructionDefectStatus newStatus, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            ConstructionDefectRecord defect = FindDefect(defectId);
            if (defect == null) return $"ConstructionContract: {ContractId} has no defect '{defectId}'.";
            if (newStatus == ConstructionDefectStatus.Unspecified || newStatus == ConstructionDefectStatus.Open)
                return $"ConstructionContract: {ContractId} defect '{defectId}' cannot move to {newStatus}.";
            defect.Status = newStatus;
            defect.StatusDayIndex = dayIndex;
            diag.Add($"ConstructionContract: {ContractId} defect {defectId} is now {newStatus} (recorded, not performed here).");
            return null;
        }

        /// <summary>
        /// D4G: proposes a spec amendment. Price/time move only when BOTH
        /// parties accept — see <see cref="RecordChangeOrderAcceptance"/>.
        /// </summary>
        public string ProposeChangeOrder(string description, int priceDeltaCents, int timeDeltaDays,
            ConstructionContractSide proposedBy, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (IsTerminal)
                return $"ConstructionContract: {ContractId} is {Status} — no amendments on a closed contract.";
            if (proposedBy != ConstructionContractSide.Owner && proposedBy != ConstructionContractSide.Builder)
                return $"ConstructionContract: {ContractId} — a change order must be proposed by the owner or the builder.";
            if (string.IsNullOrWhiteSpace(description))
                return $"ConstructionContract: {ContractId} — a change order needs a description.";
            var order = new ConstructionChangeOrder
            {
                ChangeOrderId = $"CO-{nextChangeOrderNumber++:D4}",
                ContractId = ContractId,
                Description = description,
                PriceDeltaCents = priceDeltaCents,
                TimeDeltaDays = timeDeltaDays,
                ProposedBy = proposedBy,
                ProposedDayIndex = dayIndex,
                Status = ConstructionChangeOrderStatus.Proposed,
            };
            ChangeOrders.Add(order);
            diag.Add($"ConstructionContract: {ContractId} change order {order.ChangeOrderId} proposed by {proposedBy}: {description} " +
                $"({FormatMoney(priceDeltaCents)}, {(timeDeltaDays >= 0 ? "+" : "")}{timeDeltaDays}d).");
            return null;
        }

        public string RecordChangeOrderAcceptance(string changeOrderId, ConstructionContractSide side, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            ConstructionChangeOrder order = FindChangeOrder(changeOrderId);
            if (order == null) return $"ConstructionContract: {ContractId} has no change order '{changeOrderId}'.";
            if (order.Status != ConstructionChangeOrderStatus.Proposed)
                return $"ConstructionContract: {ContractId} change order '{changeOrderId}' is {order.Status}, not Proposed.";
            if (side == ConstructionContractSide.Owner) order.OwnerAcceptedDayIndex = dayIndex;
            else if (side == ConstructionContractSide.Builder) order.BuilderAcceptedDayIndex = dayIndex;
            else return $"ConstructionContract: {ContractId} — acceptance must come from the owner or the builder.";

            if (order.BothAccepted)
            {
                order.Status = ConstructionChangeOrderStatus.Accepted;
                AgreedPriceCents = Math.Max(0, AgreedPriceCents + order.PriceDeltaCents);
                if (PriceBasis != ConstructionContractPriceBasis.LumpSum)
                {
                    EstimatedCostCents = Math.Max(0, EstimatedCostCents + order.PriceDeltaCents);
                }
                if (AgreedCompletionDayIndex >= 0)
                {
                    AgreedCompletionDayIndex = Math.Max(AgreedStartDayIndex, AgreedCompletionDayIndex + order.TimeDeltaDays);
                }
                diag.Add($"ConstructionContract: {ContractId} change order {order.ChangeOrderId} accepted by both parties — " +
                    $"price now {FormatMoney(AgreedPriceCents)}, completion day {AgreedCompletionDayIndex}.");
            }
            else
            {
                diag.Add($"ConstructionContract: {ContractId} change order {order.ChangeOrderId} accepted by {side} — awaiting the other side.");
            }
            return null;
        }

        public string RejectChangeOrder(string changeOrderId, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            ConstructionChangeOrder order = FindChangeOrder(changeOrderId);
            if (order == null) return $"ConstructionContract: {ContractId} has no change order '{changeOrderId}'.";
            if (order.Status != ConstructionChangeOrderStatus.Proposed)
                return $"ConstructionContract: {ContractId} change order '{changeOrderId}' is {order.Status}, not Proposed.";
            order.Status = ConstructionChangeOrderStatus.Rejected;
            diag.Add($"ConstructionContract: {ContractId} change order {order.ChangeOrderId} rejected — retained as history.");
            return null;
        }

        /// <summary>
        /// D4G: final acceptance — the owner's sign-off that the works meet the
        /// specs. Requires every stage accepted and no outstanding defects.
        /// Releases the final payment obligation as RECORDED (never auto-paid).
        /// No completion without acceptance.
        /// </summary>
        public string FinalAccept(int dayIndex, string acceptedBy, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (Status != ConstructionContractStatus.InProgress
                && Status != ConstructionContractStatus.AcceptanceInProgress)
                return $"ConstructionContract: {ContractId} is {Status} — final acceptance needs work underway.";
            for (int i = 0; i < AcceptanceStages.Count; i++)
            {
                if (!AcceptanceStages[i].IsAccepted)
                    return $"ConstructionContract: {ContractId} — stage '{AcceptanceStages[i].StageName}' is not accepted; no completion without acceptance.";
            }
            for (int i = 0; i < Defects.Count; i++)
            {
                if (Defects[i].BlocksFinalAcceptance)
                    return $"ConstructionContract: {ContractId} — defect {Defects[i].DefectId} is still {Defects[i].Status}; no completion without acceptance.";
            }
            Status = ConstructionContractStatus.Accepted;
            FinalAcceptedDayIndex = dayIndex;

            int finalBalance = PaymentTerms != null ? Math.Max(0, PaymentTerms.CompletionBalanceCents) : 0;
            if (finalBalance > 0)
            {
                PaymentObligations.Add(new ConstructionPaymentObligation
                {
                    ObligationId = $"PAY-{nextObligationNumber++:D4}",
                    ContractId = ContractId,
                    Kind = "final-balance",
                    Description = "Completion balance released on final acceptance",
                    AmountCents = finalBalance,
                    PayeeDisplayName = BuilderParty != null ? BuilderParty.DisplayName : string.Empty,
                    RecordedDayIndex = dayIndex,
                    ReleasedDayIndex = dayIndex,
                    Status = ConstructionPaymentObligationStatus.Released,
                });
                diag.Add($"ConstructionContract: {ContractId} final acceptance recorded by {acceptedBy} on day {dayIndex} — " +
                    $"final balance {FormatMoney(finalBalance)} RELEASED as an obligation (recorded, not auto-paid).");
            }
            else
            {
                diag.Add($"ConstructionContract: {ContractId} final acceptance recorded by {acceptedBy} on day {dayIndex} — no completion balance in the terms.");
            }
            return null;
        }

        public string Cancel(int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (IsTerminal)
                return $"ConstructionContract: {ContractId} is {Status} — nothing to cancel.";
            Status = ConstructionContractStatus.Cancelled;
            diag.Add($"ConstructionContract: {ContractId} cancelled on day {dayIndex}.");
            return null;
        }

        public ConstructionAcceptanceStage FindStage(string stageId)
        {
            if (string.IsNullOrWhiteSpace(stageId) || AcceptanceStages == null) return null;
            foreach (ConstructionAcceptanceStage stage in AcceptanceStages)
            {
                if (stage != null && string.Equals(stage.StageId, stageId, StringComparison.Ordinal)) return stage;
            }
            return null;
        }

        public ConstructionDefectRecord FindDefect(string defectId)
        {
            if (string.IsNullOrWhiteSpace(defectId) || Defects == null) return null;
            foreach (ConstructionDefectRecord defect in Defects)
            {
                if (defect != null && string.Equals(defect.DefectId, defectId, StringComparison.Ordinal)) return defect;
            }
            return null;
        }

        public ConstructionChangeOrder FindChangeOrder(string changeOrderId)
        {
            if (string.IsNullOrWhiteSpace(changeOrderId) || ChangeOrders == null) return null;
            foreach (ConstructionChangeOrder order in ChangeOrders)
            {
                if (order != null && string.Equals(order.ChangeOrderId, changeOrderId, StringComparison.Ordinal)) return order;
            }
            return null;
        }

        public int OutstandingDefectCount()
        {
            int count = 0;
            if (Defects != null)
            {
                foreach (ConstructionDefectRecord defect in Defects)
                {
                    if (defect != null && defect.BlocksFinalAcceptance) count++;
                }
            }
            return count;
        }

        public int AcceptedStageCount()
        {
            int count = 0;
            if (AcceptanceStages != null)
            {
                foreach (ConstructionAcceptanceStage stage in AcceptanceStages)
                {
                    if (stage != null && stage.IsAccepted) count++;
                }
            }
            return count;
        }

        /// <summary>
        /// D4G: the payment-terms summary the D4H progress-billing package
        /// consumes through <see cref="IConstructionProgressBilling"/>.
        /// </summary>
        public string BuildD4HPaymentSummary()
        {
            int deposit = PaymentTerms != null ? Math.Max(0, PaymentTerms.DepositCents) : 0;
            int milestones = 0;
            int milestoneCount = 0;
            if (PaymentTerms != null && PaymentTerms.Milestones != null)
            {
                foreach (ConstructionPaymentMilestone m in PaymentTerms.Milestones)
                {
                    if (m == null) continue;
                    milestoneCount++;
                    milestones += Math.Max(0, m.AmountCents);
                }
            }
            int balance = PaymentTerms != null ? Math.Max(0, PaymentTerms.CompletionBalanceCents) : 0;
            return $"{ContractId}: price {FormatMoney(AgreedPriceCents)} ({PriceBasis}); " +
                $"deposit {FormatMoney(deposit)}, {milestoneCount} milestone(s) {FormatMoney(milestones)}, " +
                $"completion balance {FormatMoney(balance)}; obligations on record {PaymentObligations.Count}.";
        }

        private static string FormatMoney(int cents)
        {
            return "$" + (Math.Max(0, cents) / 100f).ToString("N2");
        }

        // ---------- save DTO (inside the owning contract class) ----------

        [Serializable]
        public sealed class ConstructionContractSaveDto
        {
            public string ContractId = string.Empty;
            public ConstructionContractParty OwnerParty = new ConstructionContractParty();
            public ConstructionContractParty BuilderParty = new ConstructionContractParty();
            public ConstructionWorksKind WorksKind = ConstructionWorksKind.Unspecified;
            public string WorksDescription = string.Empty;
            public List<ConstructionSpecSection> SpecSections = new List<ConstructionSpecSection>();
            public ConstructionContractPriceBasis PriceBasis = ConstructionContractPriceBasis.Unspecified;
            public int AgreedPriceCents;
            public int CostPlusFeePercent;
            public int CostPlusFixedFeeCents;
            public int EstimatedCostCents;
            public int AgreedStartDayIndex = -1;
            public int AgreedCompletionDayIndex = -1;
            public ConstructionPaymentTerms PaymentTerms = new ConstructionPaymentTerms();
            public List<ConstructionAcceptanceStage> AcceptanceStages = new List<ConstructionAcceptanceStage>();
            public List<ConstructionDefectRecord> Defects = new List<ConstructionDefectRecord>();
            public List<ConstructionChangeOrder> ChangeOrders = new List<ConstructionChangeOrder>();
            public List<ConstructionPaymentObligation> PaymentObligations = new List<ConstructionPaymentObligation>();
            public ConstructionContractStatus Status = ConstructionContractStatus.Unspecified;
            public string AcceptedBidId = string.Empty;
            public int FormedDayIndex = -1;
            public int WorkStartedDayIndex = -1;
            public int FinalAcceptedDayIndex = -1;
            public int NextChangeOrderNumber = 1;
            public int NextDefectNumber = 1;
            public int NextObligationNumber = 1;
        }

        public ConstructionContractSaveDto CaptureSaveDto()
        {
            return new ConstructionContractSaveDto
            {
                ContractId = ContractId,
                OwnerParty = ConstructionContractPartyClone(OwnerParty),
                BuilderParty = ConstructionContractPartyClone(BuilderParty),
                WorksKind = WorksKind,
                WorksDescription = WorksDescription,
                SpecSections = new List<ConstructionSpecSection>(SpecSections ?? new List<ConstructionSpecSection>()),
                PriceBasis = PriceBasis,
                AgreedPriceCents = AgreedPriceCents,
                CostPlusFeePercent = CostPlusFeePercent,
                CostPlusFixedFeeCents = CostPlusFixedFeeCents,
                EstimatedCostCents = EstimatedCostCents,
                AgreedStartDayIndex = AgreedStartDayIndex,
                AgreedCompletionDayIndex = AgreedCompletionDayIndex,
                PaymentTerms = ClonePaymentTerms(PaymentTerms),
                AcceptanceStages = new List<ConstructionAcceptanceStage>(AcceptanceStages ?? new List<ConstructionAcceptanceStage>()),
                Defects = new List<ConstructionDefectRecord>(Defects ?? new List<ConstructionDefectRecord>()),
                ChangeOrders = new List<ConstructionChangeOrder>(ChangeOrders ?? new List<ConstructionChangeOrder>()),
                PaymentObligations = new List<ConstructionPaymentObligation>(PaymentObligations ?? new List<ConstructionPaymentObligation>()),
                Status = Status,
                AcceptedBidId = AcceptedBidId,
                FormedDayIndex = FormedDayIndex,
                WorkStartedDayIndex = WorkStartedDayIndex,
                FinalAcceptedDayIndex = FinalAcceptedDayIndex,
                NextChangeOrderNumber = nextChangeOrderNumber,
                NextDefectNumber = nextDefectNumber,
                NextObligationNumber = nextObligationNumber,
            };
        }

        public static ConstructionContract FromSaveDto(ConstructionContractSaveDto dto)
        {
            if (dto == null) return null;
            return new ConstructionContract
            {
                ContractId = dto.ContractId ?? string.Empty,
                OwnerParty = dto.OwnerParty ?? new ConstructionContractParty(),
                BuilderParty = dto.BuilderParty ?? new ConstructionContractParty(),
                WorksKind = dto.WorksKind,
                WorksDescription = dto.WorksDescription ?? string.Empty,
                SpecSections = dto.SpecSections ?? new List<ConstructionSpecSection>(),
                PriceBasis = dto.PriceBasis,
                AgreedPriceCents = Math.Max(0, dto.AgreedPriceCents),
                CostPlusFeePercent = Math.Max(0, dto.CostPlusFeePercent),
                CostPlusFixedFeeCents = Math.Max(0, dto.CostPlusFixedFeeCents),
                EstimatedCostCents = Math.Max(0, dto.EstimatedCostCents),
                AgreedStartDayIndex = dto.AgreedStartDayIndex,
                AgreedCompletionDayIndex = dto.AgreedCompletionDayIndex,
                PaymentTerms = dto.PaymentTerms ?? new ConstructionPaymentTerms(),
                AcceptanceStages = dto.AcceptanceStages ?? new List<ConstructionAcceptanceStage>(),
                Defects = dto.Defects ?? new List<ConstructionDefectRecord>(),
                ChangeOrders = dto.ChangeOrders ?? new List<ConstructionChangeOrder>(),
                PaymentObligations = dto.PaymentObligations ?? new List<ConstructionPaymentObligation>(),
                Status = dto.Status,
                AcceptedBidId = dto.AcceptedBidId ?? string.Empty,
                FormedDayIndex = dto.FormedDayIndex,
                WorkStartedDayIndex = dto.WorkStartedDayIndex,
                FinalAcceptedDayIndex = dto.FinalAcceptedDayIndex,
                nextChangeOrderNumber = Math.Max(1, dto.NextChangeOrderNumber),
                nextDefectNumber = Math.Max(1, dto.NextDefectNumber),
                nextObligationNumber = Math.Max(1, dto.NextObligationNumber),
            };
        }
    }
}
