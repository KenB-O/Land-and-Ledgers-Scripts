using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.GrainElevator
{
    /// <summary>
    /// D2F: a grade dispute's lifecycle state. Follows canon §9.1's
    /// Commercial Dispute Lifecycle (claim/demand -> response ->
    /// negotiation/settlement -> formal proceeding -> decision) as a DATA
    /// record: the book records the claim, the response, and the settlement,
    /// but the runtime never auto-decides a dispute.
    /// </summary>
    public enum GrainElevatorGradeDisputeStatus
    {
        Filed = 0, // the customer has claimed a different grade; awaiting the elevator's response
        Responded = 1, // the elevator has recorded its counter-position; awaiting settlement
        Settled = 2, // an agreed grade was recorded and applied
        Withdrawn = 3, // the filer withdrew the claim
        Escalated = 4, // sent to a formal proceeding — the book waits for the outcome as DATA
    }

    /// <summary>
    /// D2F: one grading dispute — the formal record of a grader-vs-storer
    /// disagreement. Canon is silent on regrade process and dockage, and
    /// grade discounts are a Canon R6 §6 calibration hold, so this class
    /// records the dispute MECHANISM as data and deliberately does NOT
    /// decide outcomes:
    ///
    /// - the customer files a claim naming the grade they believe the grain
    ///   deserves (and may claim dockage units — recorded ONLY, never
    ///   auto-applied, per the calibration hold);
    /// - the elevator records its response (its grade is not changed by a
    ///   response — only a settlement changes the stored grade);
    /// - a settlement names the agreed grade AND who decided it (the
    ///   "who decides" question is an explicit design fork captured as
    ///   data — e.g. "elevator operator", "board inspector", "mutual
    ///   agreement" — never auto-resolved by the runtime);
    /// - escalation to a formal proceeding is recorded, but canon says
    ///   "exact court procedure ... remain dated research," so the runtime
    ///   does not resolve escalated disputes — the outcome arrives later as
    ///   data via SettleDispute.
    ///
    /// A settled grade is applied through GrainElevatorGrainStock.ReGradeLot
    /// (the elevator remains the grading authority for what it holds) and
    /// is recorded loudly.
    /// </summary>
    [Serializable]
    public sealed class GrainElevatorGradeDispute
    {
        public EntityId DisputeId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public EntityId ElevatorLotId = EntityId.Invalid; // the stored lot under dispute
        public EntityId ReceiptId = EntityId.Invalid; // the custody receipt, when the lot is custody grain
        public string FiledByCustomerId = string.Empty;
        public string FiledByCustomerName = string.Empty;
        public int DayFiled;
        public string AssignedGradeId = string.Empty; // the elevator's grade at filing time
        public string ClaimedGradeId = string.Empty; // the grade the filer claims the grain deserves
        public int ClaimedDockageUnits; // CLAIMED dockage — recorded only, never auto-applied (§6 hold)
        public string ReasonNote = string.Empty;
        public string ResponseGradeId = string.Empty; // the elevator's counter-position
        public string ResponseNote = string.Empty;
        public int DayResponded;
        public string SettledGradeId = string.Empty; // the agreed grade
        public string DecidedBy = string.Empty; // WHO decided — fork captured as data, never auto-resolved
        public int DayResolved;
        public GrainElevatorGradeDisputeStatus Status = GrainElevatorGradeDisputeStatus.Filed;

        public GrainElevatorGradeDispute() { }

        public bool IsOpen =>
            Status == GrainElevatorGradeDisputeStatus.Filed
            || Status == GrainElevatorGradeDisputeStatus.Responded
            || Status == GrainElevatorGradeDisputeStatus.Escalated;
    }

    /// <summary>
    /// D2F: the elevator's grade-dispute book — formal records of
    /// grader-vs-storer disagreements, owned by the business runtime. The
    /// book records claims, responses, settlements, withdrawals and
    /// escalations loudly; it never auto-decides an outcome.
    /// </summary>
    public sealed class GrainElevatorGradeDisputeBook
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<GrainElevatorGradeDispute> disputes = new List<GrainElevatorGradeDispute>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<GrainElevatorGradeDispute> Disputes => disputes;

        public GrainElevatorGradeDispute FindDispute(EntityId disputeId)
        {
            foreach (var dispute in disputes)
            {
                if (dispute.DisputeId.Equals(disputeId)) return dispute;
            }
            return null;
        }

        public List<GrainElevatorGradeDispute> OpenDisputes()
        {
            var open = new List<GrainElevatorGradeDispute>();
            foreach (var dispute in disputes)
            {
                if (dispute.IsOpen) open.Add(dispute);
            }
            return open;
        }

        public GrainElevatorGradeDispute OpenDisputeForLot(EntityId elevatorLotId)
        {
            foreach (var dispute in disputes)
            {
                if (dispute.IsOpen && dispute.ElevatorLotId.Equals(elevatorLotId)) return dispute;
            }
            return null;
        }

        /// <summary>
        /// Files a grading dispute: the named storer claims their stored lot
        /// deserves a different grade. The filer must be the lot's customer
        /// (custody) or the lot's seller (dealer stock). Returns the
        /// dispute, or null with a diagnostic.
        /// </summary>
        public GrainElevatorGradeDispute FileDispute(
            EntityIdRegistry idRegistry, GrainElevatorGrainStock stock,
            EntityId elevatorLotId, EntityId receiptId,
            string customerId, string customerName,
            string claimedGradeId, int claimedDockageUnits,
            string reasonNote, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (idRegistry == null)
            {
                diag.Add("GrainElevatorGradeDisputeBook.FileDispute: no id registry — dispute refused.");
                return null;
            }
            if (stock == null)
            {
                diag.Add("GrainElevatorGradeDisputeBook.FileDispute: no grain stock — dispute refused.");
                return null;
            }
            var lot = stock.FindLot(elevatorLotId);
            if (lot == null)
            {
                diag.Add($"GrainElevatorGradeDisputeBook.FileDispute: lot {elevatorLotId} is not in elevator custody — no dispute over thin air.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(customerId) || string.IsNullOrWhiteSpace(customerName))
            {
                diag.Add("GrainElevatorGradeDisputeBook.FileDispute: the filer must be NAMED.");
                return null;
            }
            bool authorized = lot.Kind == GrainElevatorLotKind.Custody
                ? string.Equals(lot.CustomerId, customerId.Trim(), StringComparison.Ordinal)
                : string.Equals(lot.SellerBusinessId, customerId.Trim(), StringComparison.Ordinal);
            if (!authorized)
            {
                diag.Add($"GrainElevatorGradeDisputeBook.FileDispute: {customerName} ({customerId}) is not the storer of lot {elevatorLotId} — only the named storer disputes the grade.");
                return null;
            }
            string claimed = (claimedGradeId ?? string.Empty).Trim();
            if (!GrainElevatorGradeCatalog.IsKnownGrade(claimed))
            {
                diag.Add($"GrainElevatorGradeDisputeBook.FileDispute: claimed grade '{claimedGradeId}' is not a known grade — grades are data, fix the id.");
                return null;
            }
            if (string.Equals(claimed, lot.ElevatorGradeId, StringComparison.OrdinalIgnoreCase))
            {
                diag.Add($"GrainElevatorGradeDisputeBook.FileDispute: lot {elevatorLotId} is already graded '{claimed}' — no disagreement to dispute.");
                return null;
            }
            if (claimedDockageUnits < 0)
            {
                diag.Add("GrainElevatorGradeDisputeBook.FileDispute: claimed dockage cannot be negative.");
                return null;
            }
            if (OpenDisputeForLot(elevatorLotId) != null)
            {
                diag.Add($"GrainElevatorGradeDisputeBook.FileDispute: lot {elevatorLotId} already has an open dispute — settle or withdraw it first.");
                return null;
            }

            var dispute = new GrainElevatorGradeDispute
            {
                DisputeId = idRegistry.Allocate(EntityKind.Lot),
                ElevatorLotId = elevatorLotId,
                ReceiptId = receiptId,
                FiledByCustomerId = customerId.Trim(),
                FiledByCustomerName = customerName.Trim(),
                DayFiled = dayIndex,
                AssignedGradeId = lot.ElevatorGradeId,
                ClaimedGradeId = claimed,
                ClaimedDockageUnits = claimedDockageUnits,
                ReasonNote = reasonNote ?? string.Empty,
            };
            disputes.Add(dispute);
            diag.Add($"GrainElevatorGradeDisputeBook: filed dispute {dispute.DisputeId} — "
                + $"{dispute.FiledByCustomerName} claims lot {elevatorLotId} ({lot.Crop}, {lot.GrainUnits}u) "
                + $"deserves grade '{GrainElevatorGradeCatalog.DisplayName(claimed)}' "
                + $"against the elevator's '{GrainElevatorGradeCatalog.DisplayName(lot.ElevatorGradeId)}'"
                + (claimedDockageUnits > 0 ? $"; claims {claimedDockageUnits}u dockage (RECORDED ONLY — dockage is a Canon R6 §6 calibration hold, never auto-applied)" : string.Empty)
                + (string.IsNullOrWhiteSpace(dispute.ReasonNote) ? string.Empty : $". Reason: {dispute.ReasonNote}"));
            return dispute;
        }

        /// <summary>
        /// Records the elevator's response to a filed dispute. The response
        /// is a counter-position, recorded loudly — it does NOT change the
        /// stored lot's grade (only a settlement does).
        /// </summary>
        public string RecordResponse(
            EntityId disputeId, string responseGradeId, string responseNote, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var dispute = FindDispute(disputeId);
            if (dispute == null)
                return $"GrainElevatorGradeDisputeBook.RecordResponse: dispute {disputeId} is not on the books.";
            if (dispute.Status != GrainElevatorGradeDisputeStatus.Filed)
                return $"GrainElevatorGradeDisputeBook.RecordResponse: dispute {disputeId} is {dispute.Status} — a response is recorded only on a filed dispute.";
            string response = (responseGradeId ?? string.Empty).Trim();
            if (!GrainElevatorGradeCatalog.IsKnownGrade(response))
                return $"GrainElevatorGradeDisputeBook.RecordResponse: response grade '{responseGradeId}' is not a known grade — grades are data, fix the id.";

            dispute.ResponseGradeId = response;
            dispute.ResponseNote = responseNote ?? string.Empty;
            dispute.DayResponded = dayIndex;
            dispute.Status = GrainElevatorGradeDisputeStatus.Responded;
            diag.Add($"GrainElevatorGradeDisputeBook: responded to dispute {disputeId} — "
                + $"the elevator holds grade '{GrainElevatorGradeCatalog.DisplayName(response)}' on lot {dispute.ElevatorLotId}; "
                + "the stored grade is UNCHANGED until a settlement is recorded"
                + (string.IsNullOrWhiteSpace(dispute.ResponseNote) ? string.Empty : $". Note: {dispute.ResponseNote}"));
            return null;
        }

        /// <summary>
        /// Settles a dispute: records the AGREED grade and WHO decided it,
        /// then applies the grade through the stock's re-grade authority
        /// (recorded loudly). DecidedBy is required — the "who decides" fork
        /// is captured as data, never auto-resolved by the runtime.
        /// </summary>
        public string SettleDispute(
            EntityId disputeId, string settledGradeId, string decidedBy,
            int dayIndex, GrainElevatorGrainStock stock, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var dispute = FindDispute(disputeId);
            if (dispute == null)
                return $"GrainElevatorGradeDisputeBook.SettleDispute: dispute {disputeId} is not on the books.";
            if (dispute.Status == GrainElevatorGradeDisputeStatus.Settled
                || dispute.Status == GrainElevatorGradeDisputeStatus.Withdrawn)
                return $"GrainElevatorGradeDisputeBook.SettleDispute: dispute {disputeId} is already {dispute.Status} — it cannot be settled again.";
            string settled = (settledGradeId ?? string.Empty).Trim();
            if (!GrainElevatorGradeCatalog.IsKnownGrade(settled))
                return $"GrainElevatorGradeDisputeBook.SettleDispute: settled grade '{settledGradeId}' is not a known grade — grades are data, fix the id.";
            if (string.IsNullOrWhiteSpace(decidedBy))
                return $"GrainElevatorGradeDisputeBook.SettleDispute: 'who decided' is a design fork that must be recorded as data — name the decider (e.g. 'elevator operator', 'board inspector', 'mutual agreement').";
            if (stock == null)
                return $"GrainElevatorGradeDisputeBook.SettleDispute: no grain stock — the settlement cannot be applied.";

            string refusal = stock.ReGradeLot(dispute.ElevatorLotId, settled, diag);
            if (refusal != null) return refusal;

            dispute.SettledGradeId = settled;
            dispute.DecidedBy = decidedBy.Trim();
            dispute.DayResolved = dayIndex;
            dispute.Status = GrainElevatorGradeDisputeStatus.Settled;
            diag.Add($"GrainElevatorGradeDisputeBook: settled dispute {disputeId} — "
                + $"lot {dispute.ElevatorLotId} now graded '{GrainElevatorGradeCatalog.DisplayName(settled)}', "
                + $"decided by '{dispute.DecidedBy}' (the decider is recorded as data — canon does not settle this fork).");
            return null;
        }

        /// <summary>The filer withdraws their claim — recorded, not erased.</summary>
        public string WithdrawDispute(EntityId disputeId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var dispute = FindDispute(disputeId);
            if (dispute == null)
                return $"GrainElevatorGradeDisputeBook.WithdrawDispute: dispute {disputeId} is not on the books.";
            if (!dispute.IsOpen)
                return $"GrainElevatorGradeDisputeBook.WithdrawDispute: dispute {disputeId} is {dispute.Status} — nothing open to withdraw.";
            dispute.Status = GrainElevatorGradeDisputeStatus.Withdrawn;
            dispute.DayResolved = dayIndex;
            diag.Add($"GrainElevatorGradeDisputeBook: dispute {disputeId} withdrawn by {dispute.FiledByCustomerName} — "
                + $"the elevator's grade '{GrainElevatorGradeCatalog.DisplayName(dispute.AssignedGradeId)}' stands.");
            return null;
        }

        /// <summary>
        /// Escalates a dispute to a formal proceeding. Recorded loudly — and
        /// NOT resolved: canon says "exact court procedure ... remain dated
        /// research," so the outcome must arrive later as data via
        /// SettleDispute.
        /// </summary>
        public string EscalateDispute(EntityId disputeId, string note, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var dispute = FindDispute(disputeId);
            if (dispute == null)
                return $"GrainElevatorGradeDisputeBook.EscalateDispute: dispute {disputeId} is not on the books.";
            if (dispute.Status != GrainElevatorGradeDisputeStatus.Filed
                && dispute.Status != GrainElevatorGradeDisputeStatus.Responded)
                return $"GrainElevatorGradeDisputeBook.EscalateDispute: dispute {disputeId} is {dispute.Status} — only a filed or responded dispute can be escalated.";
            dispute.Status = GrainElevatorGradeDisputeStatus.Escalated;
            diag.Add($"GrainElevatorGradeDisputeBook: dispute {disputeId} ESCALATED to a formal proceeding on day {dayIndex}"
                + (string.IsNullOrWhiteSpace(note) ? string.Empty : $" — {note.Trim()}")
                + ". The runtime does not resolve formal proceedings (canon: exact court procedure is dated research); "
                + "the outcome arrives later as data via SettleDispute.");
            return null;
        }

        /// <summary>D2F save contract: lives inside the owning dispute-book class.</summary>
        [Serializable]
        public sealed class GrainElevatorGradeDisputeBookSaveDto
        {
            public List<GrainElevatorGradeDispute> Disputes = new List<GrainElevatorGradeDispute>();
        }

        public GrainElevatorGradeDisputeBookSaveDto CaptureSaveDto()
        {
            var dto = new GrainElevatorGradeDisputeBookSaveDto();
            foreach (var dispute in disputes)
            {
                dto.Disputes.Add(new GrainElevatorGradeDispute
                {
                    DisputeId = dispute.DisputeId,
                    ElevatorLotId = dispute.ElevatorLotId,
                    ReceiptId = dispute.ReceiptId,
                    FiledByCustomerId = dispute.FiledByCustomerId,
                    FiledByCustomerName = dispute.FiledByCustomerName,
                    DayFiled = dispute.DayFiled,
                    AssignedGradeId = dispute.AssignedGradeId,
                    ClaimedGradeId = dispute.ClaimedGradeId,
                    ClaimedDockageUnits = dispute.ClaimedDockageUnits,
                    ReasonNote = dispute.ReasonNote,
                    ResponseGradeId = dispute.ResponseGradeId,
                    ResponseNote = dispute.ResponseNote,
                    DayResponded = dispute.DayResponded,
                    SettledGradeId = dispute.SettledGradeId,
                    DecidedBy = dispute.DecidedBy,
                    DayResolved = dispute.DayResolved,
                    Status = dispute.Status,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(GrainElevatorGradeDisputeBookSaveDto dto)
        {
            disputes.Clear();
            if (dto == null) return;
            if (dto.Disputes != null)
            {
                foreach (var dispute in dto.Disputes)
                {
                    if (dispute == null) continue;
                    disputes.Add(dispute);
                }
            }
        }
    }
}
