using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.GrainElevator
{
    /// <summary>
    /// W5B: the per-instance grain elevator runtime — the detailed layer
    /// behind a GrainElevator business instance (BusinessType.GrainElevator
    /// = 25). One instance per business.
    ///
    /// What this owns (the detailed layer):
    /// - the two-book grain stock (GrainElevatorGrainStock): CUSTODY lots
    ///   (bailment — held FOR named customers under receipts) and DEALER
    ///   lots (elevator-owned dealing stock bought from named farms /
    ///   dealers). Canon R6 §1.4 CANON LOCK: physical possession is not
    ///   automatic ownership — the books never mix;
    /// - grading (GrainElevatorGradeCatalog): grade as data, assigned by
    ///   the elevator at intake; the honest default sort recorded loudly;
    /// - the obligation book (GrainElevatorObligations): warehouse
    ///   receipts naming real stored custody lots, and forward-sale lots
    ///   naming real stored dealer grain — the elevator never sells grain
    ///   it does not hold (MR-P001 doctrine);
    /// - storage policy as data (capacity shared across both books, fees
    ///   on custody grain only; exact charges are a Canon R6 §6
    ///   calibration hold);
    /// - D2F: the storage-fee ledger (GrainElevatorFeeLedger) — per-customer
    ///   fee accounts. Charges (period accruals, intake/outturn handling)
    ///   and payments are recorded honestly; money moves only through
    ///   ledger authorities;
    /// - D2F: the grade-dispute book (GrainElevatorGradeDisputeBook) — the
    ///   formal record of grader-vs-storer disagreements. Canon is silent
    ///   on regrade process and dockage; the book records claims, responses,
    ///   settlements and escalations as DATA and never auto-decides.
    ///
    /// Links: farms and CRP-3 grain dealers deliver into custody or sell
    /// into the dealer book (farm -> dealer -> elevator); the W5A mill's
    /// merchant intake buys from elevator forward-lot deliveries. Money
    /// moves only through ledger authorities — this runtime computes fees
    /// and sale values owed; it never posts them.
    /// </summary>
    public sealed class GrainElevatorShopRuntime
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly string businessInstanceId;
        private readonly EntityIdRegistry idRegistry;
        private readonly GrainElevatorGrainStock grainStock;
        private readonly GrainElevatorObligations obligations;
        private readonly GrainElevatorFeeLedger feeLedger;
        private readonly GrainElevatorGradeDisputeBook disputeBook;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public string BusinessInstanceId => businessInstanceId;
        public BusinessType BusinessType => BusinessType.GrainElevator;
        public GrainElevatorGrainStock GrainStock => grainStock;
        public GrainElevatorObligations Obligations => obligations;
        public GrainElevatorFeeLedger FeeLedger => feeLedger;
        public GrainElevatorGradeDisputeBook DisputeBook => disputeBook;

        public GrainElevatorShopRuntime(string businessInstanceId, EntityIdRegistry idRegistry)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
            this.idRegistry = idRegistry;
            this.grainStock = new GrainElevatorGrainStock(this.businessInstanceId);
            this.obligations = new GrainElevatorObligations(this.grainStock, this.businessInstanceId);
            this.feeLedger = new GrainElevatorFeeLedger();
            this.disputeBook = new GrainElevatorGradeDisputeBook();
            this.obligations.FeeLedger = this.feeLedger;
        }

        public void SetStoragePolicy(GrainElevatorStoragePolicy policy)
        {
            grainStock.SetPolicy(policy);
        }

        // ---------- custody intake (bailment) ----------

        /// <summary>
        /// W5B: a farm or dealer delivers grain FOR STORAGE. The customer is
        /// named, the grain is graded at intake, and a warehouse receipt is
        /// issued naming the stored lot. Returns the receipt, or null with
        /// diagnostics on refusal.
        /// </summary>
        public GrainElevatorReceipt ReceiveForStorage(
            CropLot lot, string customerId, string customerName,
            string declaredGradeId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            string refusal = grainStock.ReceiveCustodyLot(
                idRegistry, lot, customerId, customerName, declaredGradeId, dayIndex, diag);
            if (refusal != null)
            {
                diag.Add(refusal);
                return null;
            }
            var stored = grainStock.Lots[grainStock.Lots.Count - 1];
            var receipt = obligations.IssueReceipt(idRegistry, stored.ElevatorLotId, dayIndex, diag);

            // D2F: receiving (intake handling) is a policy-data charge on the
            // named storer's fee account — the ledger authority posts the
            // money; this only records what is owed.
            int handlingRate = grainStock.Policy.IntakeHandlingCentsPerUnit;
            if (receipt != null && handlingRate > 0)
            {
                feeLedger.PostCharge(
                    idRegistry, stored.CustomerId, stored.CustomerName,
                    stored.ElevatorLotId.ToString(), GrainElevatorFeeChargeKind.IntakeHandling,
                    stored.GrainUnits, 0, handlingRate, stored.GrainUnits * handlingRate, dayIndex,
                    "receiving charge on custody intake", diag);
            }
            return receipt;
        }

        /// <summary>W5B: the named holder redeems a receipt for their grain.</summary>
        public GrainElevatorObligations.ReceiptRedemption RedeemReceipt(
            EntityId receiptId, string holderId, int requestedUnits, int dayIndex, List<string> diag)
        {
            return obligations.RedeemReceipt(idRegistry, receiptId, holderId, requestedUnits, dayIndex, diag ?? diagnostics);
        }

        // ---------- dealer intake (elevator-owned dealing) ----------

        /// <summary>
        /// W5B: the elevator BUYS grain from a named farm or dealer for its
        /// own dealing stock — the source for forward-sale lots. Returns the
        /// refusal string, or null.
        /// </summary>
        public string BuyForDealing(
            CropLot lot, string sellerBusinessId, int unitCostCents,
            string declaredGradeId, int dayIndex, List<string> diag)
        {
            return grainStock.ReceiveDealerLot(
                idRegistry, lot, sellerBusinessId, unitCostCents, declaredGradeId, dayIndex, diag ?? diagnostics);
        }

        /// <summary>W5B: re-grades a stored lot (the elevator grades what it holds).</summary>
        public string ReGradeLot(EntityId elevatorLotId, string gradeId, List<string> diag)
        {
            return grainStock.ReGradeLot(elevatorLotId, gradeId, diag ?? diagnostics);
        }

        // ---------- forward lots (sales of held grain) ----------

        /// <summary>
        /// W5B: contracts a forward sale of REAL stored dealer grain to a
        /// named buyer (the mill buys from elevator lots through these).
        /// Creation reserves the units — double-selling is refused loudly.
        /// </summary>
        public GrainElevatorForwardLot CreateForwardLot(
            CropKind crop, string gradeId, int units, int pricePerUnitCents,
            string buyerId, string buyerName, int dayIndex, List<string> diag)
        {
            return obligations.CreateForwardLot(
                idRegistry, crop, gradeId, units, pricePerUnitCents, buyerId, buyerName, dayIndex, diag ?? diagnostics);
        }

        /// <summary>W5B: delivers units against an open forward lot (dealer stock only, never custody).</summary>
        public GrainElevatorObligations.ForwardDelivery DeliverForwardLot(
            EntityId forwardLotId, int requestedUnits, int dayIndex, List<string> diag)
        {
            return obligations.DeliverForwardLot(idRegistry, forwardLotId, requestedUnits, dayIndex, diag ?? diagnostics);
        }

        /// <summary>W5B: cancels an open forward lot, releasing its reservation.</summary>
        public string CancelForwardLot(EntityId forwardLotId, List<string> diag)
        {
            return obligations.CancelForwardLot(forwardLotId, diag ?? diagnostics);
        }

        // ---------- D2F: storage-fee billing and payments ----------

        /// <summary>
        /// D2F: closes out storage billing through the given day — posts one
        /// PeriodAccrual charge per custody lot with completed, unbilled
        /// periods to the fee ledger. Driven by the runtime or an
        /// orchestration tick; the ledger authority posts the money.
        /// Returns the number of charges posted.
        /// </summary>
        public int BillStoragePeriods(int dayIndex, List<string> diag)
        {
            return grainStock.BillPeriods(idRegistry, dayIndex, feeLedger, diag ?? diagnostics);
        }

        /// <summary>
        /// D2F: records a storage-fee payment from a named customer (the
        /// ledger authority posts the money; this only records it against
        /// the customer's fee account). Returns the payment, or null on
        /// refusal.
        /// </summary>
        public GrainElevatorFeePayment RecordStoragePayment(
            string customerId, string customerName, int amountCents, int dayIndex, string note, List<string> diag)
        {
            return feeLedger.RecordPayment(
                idRegistry, customerId, customerName, amountCents, dayIndex, note, diag ?? diagnostics);
        }

        /// <summary>D2F: a named customer's unpaid storage-fee balance in cents (negative = credit).</summary>
        public int StorageUnpaidBalance(string customerId)
        {
            return feeLedger.UnpaidBalanceCents(customerId);
        }

        // ---------- D2F: grade disputes ----------

        /// <summary>
        /// D2F: files a grading dispute — the named storer claims a stored
        /// lot deserves a different grade. Recorded loudly; the runtime
        /// never auto-decides.
        /// </summary>
        public GrainElevatorGradeDispute FileGradeDispute(
            EntityId elevatorLotId, EntityId receiptId,
            string customerId, string customerName,
            string claimedGradeId, int claimedDockageUnits,
            string reasonNote, int dayIndex, List<string> diag)
        {
            return disputeBook.FileDispute(
                idRegistry, grainStock, elevatorLotId, receiptId,
                customerId, customerName, claimedGradeId, claimedDockageUnits,
                reasonNote, dayIndex, diag ?? diagnostics);
        }

        /// <summary>D2F: records the elevator's response to a filed dispute (a counter-position — the stored grade is unchanged).</summary>
        public string RespondToGradeDispute(
            EntityId disputeId, string responseGradeId, string responseNote, int dayIndex, List<string> diag)
        {
            return disputeBook.RecordResponse(disputeId, responseGradeId, responseNote, dayIndex, diag ?? diagnostics);
        }

        /// <summary>
        /// D2F: settles a dispute — records the agreed grade AND who decided
        /// it (the fork captured as data), then applies the grade through
        /// the stock's re-grade authority.
        /// </summary>
        public string SettleGradeDispute(
            EntityId disputeId, string settledGradeId, string decidedBy, int dayIndex, List<string> diag)
        {
            return disputeBook.SettleDispute(disputeId, settledGradeId, decidedBy, dayIndex, grainStock, diag ?? diagnostics);
        }

        /// <summary>D2F: the filer withdraws their claim — recorded, not erased.</summary>
        public string WithdrawGradeDispute(EntityId disputeId, int dayIndex, List<string> diag)
        {
            return disputeBook.WithdrawDispute(disputeId, dayIndex, diag ?? diagnostics);
        }

        /// <summary>
        /// D2F: escalates a dispute to a formal proceeding. Recorded loudly
        /// and NOT resolved — the outcome arrives later as data via
        /// SettleGradeDispute (canon: exact court procedure is dated
        /// research).
        /// </summary>
        public string EscalateGradeDispute(EntityId disputeId, string note, int dayIndex, List<string> diag)
        {
            return disputeBook.EscalateDispute(disputeId, note, dayIndex, diag ?? diagnostics);
        }

        // ---------- save / load ----------

        /// <summary>W5B save contract: lives inside the owning runtime class.</summary>
        [Serializable]
        public sealed class GrainElevatorShopRuntimeSaveDto
        {
            public string BusinessInstanceId = string.Empty;
            public GrainElevatorGrainStock.GrainElevatorGrainStockSaveDto GrainStock = new GrainElevatorGrainStock.GrainElevatorGrainStockSaveDto();
            public GrainElevatorObligations.GrainElevatorObligationsSaveDto Obligations = new GrainElevatorObligations.GrainElevatorObligationsSaveDto();
            public GrainElevatorFeeLedger.GrainElevatorFeeLedgerSaveDto FeeLedger = new GrainElevatorFeeLedger.GrainElevatorFeeLedgerSaveDto();
            public GrainElevatorGradeDisputeBook.GrainElevatorGradeDisputeBookSaveDto DisputeBook = new GrainElevatorGradeDisputeBook.GrainElevatorGradeDisputeBookSaveDto();
        }

        public GrainElevatorShopRuntimeSaveDto CaptureSaveDto()
        {
            return new GrainElevatorShopRuntimeSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                GrainStock = grainStock.CaptureSaveDto(),
                Obligations = obligations.CaptureSaveDto(),
                FeeLedger = feeLedger.CaptureSaveDto(),
                DisputeBook = disputeBook.CaptureSaveDto(),
            };
        }

        public void LoadFromSaveDto(GrainElevatorShopRuntimeSaveDto dto)
        {
            if (dto == null) return;
            grainStock.LoadFromSaveDto(dto.GrainStock);
            obligations.LoadFromSaveDto(dto.Obligations);
            feeLedger.LoadFromSaveDto(dto.FeeLedger);
            disputeBook.LoadFromSaveDto(dto.DisputeBook);
        }
    }
}
