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
    ///   calibration hold).
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

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public string BusinessInstanceId => businessInstanceId;
        public BusinessType BusinessType => BusinessType.GrainElevator;
        public GrainElevatorGrainStock GrainStock => grainStock;
        public GrainElevatorObligations Obligations => obligations;

        public GrainElevatorShopRuntime(string businessInstanceId, EntityIdRegistry idRegistry)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
            this.idRegistry = idRegistry;
            this.grainStock = new GrainElevatorGrainStock(this.businessInstanceId);
            this.obligations = new GrainElevatorObligations(this.grainStock, this.businessInstanceId);
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
            return obligations.IssueReceipt(idRegistry, stored.ElevatorLotId, dayIndex, diag);
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

        // ---------- save / load ----------

        /// <summary>W5B save contract: lives inside the owning runtime class.</summary>
        [Serializable]
        public sealed class GrainElevatorShopRuntimeSaveDto
        {
            public string BusinessInstanceId = string.Empty;
            public GrainElevatorGrainStock.GrainElevatorGrainStockSaveDto GrainStock = new GrainElevatorGrainStock.GrainElevatorGrainStockSaveDto();
            public GrainElevatorObligations.GrainElevatorObligationsSaveDto Obligations = new GrainElevatorObligations.GrainElevatorObligationsSaveDto();
        }

        public GrainElevatorShopRuntimeSaveDto CaptureSaveDto()
        {
            return new GrainElevatorShopRuntimeSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                GrainStock = grainStock.CaptureSaveDto(),
                Obligations = obligations.CaptureSaveDto(),
            };
        }

        public void LoadFromSaveDto(GrainElevatorShopRuntimeSaveDto dto)
        {
            if (dto == null) return;
            grainStock.LoadFromSaveDto(dto.GrainStock);
            obligations.LoadFromSaveDto(dto.Obligations);
        }
    }
}
