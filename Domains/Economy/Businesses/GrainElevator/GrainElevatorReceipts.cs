using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.GrainElevator
{
    /// <summary>
    /// W5B: a warehouse/elevator receipt — the custody claim instrument.
    /// Canon R6 §1.4: receipts "represent a delivery/custody obligation
    /// rather than magically converting customer grain into merchant
    /// stock." The receipt NAMES the stored custody lot (crop, grade,
    /// units) and the NAMED holder; it obligates the elevator to release
    /// that grain to the holder, minus accrued storage fees.
    ///
    /// OPEN FORK (not guessed): historical warehouse receipts were often
    /// negotiable instruments (transferable by endorsement). Canon does not
    /// settle negotiability, so this pass keeps redemption holder-only
    /// (strict bailment); negotiability is an explicit design decision for
    /// later, not silent behavior.
    /// </summary>
    [Serializable]
    public sealed class GrainElevatorReceipt
    {
        public EntityId ReceiptId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public EntityId ElevatorLotId = EntityId.Invalid; // the custody lot this receipt names
        public CropKind Crop = CropKind.Unspecified;
        public string ElevatorGradeId = string.Empty;
        public int UnitsIssued;
        public int UnitsRemaining;
        public string HolderId = string.Empty;   // named holder — bailment needs an owner
        public string HolderName = string.Empty;
        public int IssuedDayIndex;
        public bool Closed;

        public GrainElevatorReceipt() { }
    }

    /// <summary>W5B: forward-lot lifecycle. Open until fulfilled or cancelled.</summary>
    public enum GrainElevatorForwardLotStatus
    {
        Open = 0,
        Fulfilled = 1,
        Cancelled = 2,
    }

    /// <summary>
    /// W5B: a forward-sale lot — the elevator's contracted sale of grain it
    /// HOLDS. Names real stored DEALER grain (crop + grade + the elevator
    /// lots it was promised against), the contracted and delivered units,
    /// the price, and the NAMED buyer (the mill buys from elevator lots
    /// through these). Creation RESERVES the units: two forward lots can
    /// never promise the same grain — the elevator never sells grain it
    /// does not hold (MR-P001 doctrine). Custody grain is never eligible
    /// (canon bailment lock).
    /// </summary>
    [Serializable]
    public sealed class GrainElevatorForwardLot
    {
        public EntityId ForwardLotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public CropKind Crop = CropKind.Unspecified;
        public string ElevatorGradeId = string.Empty;
        public int UnitsContracted;
        public int UnitsDelivered;
        public int PricePerUnitCents;
        public string BuyerId = string.Empty;
        public string BuyerName = string.Empty;
        public List<string> PromisedElevatorLotIds = new List<string>(); // named stored dealer lots
        public int CreatedDayIndex;
        public GrainElevatorForwardLotStatus Status = GrainElevatorForwardLotStatus.Open;

        public GrainElevatorForwardLot() { }

        public int UnitsRemaining => Math.Max(0, UnitsContracted - UnitsDelivered);
    }

    /// <summary>
    /// W5B: the elevator's obligation book — receipts issued against custody
    /// lots, forward-sale lots against dealer stock. Owns no grain itself;
    /// grain movement goes through the GrainElevatorGrainStock (custody
    /// discipline: a lot is in exactly one place).
    /// </summary>
    public sealed class GrainElevatorObligations
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<GrainElevatorReceipt> receipts = new List<GrainElevatorReceipt>();
        private readonly List<GrainElevatorForwardLot> forwardLots = new List<GrainElevatorForwardLot>();
        private readonly GrainElevatorGrainStock stock;
        private readonly string elevatorBusinessId;

        public GrainElevatorObligations(GrainElevatorGrainStock stock, string elevatorBusinessId)
        {
            this.stock = stock;
            this.elevatorBusinessId = elevatorBusinessId ?? string.Empty;
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<GrainElevatorReceipt> Receipts => receipts;
        public IReadOnlyList<GrainElevatorForwardLot> ForwardLots => forwardLots;

        public GrainElevatorReceipt FindReceipt(EntityId receiptId)
        {
            foreach (var r in receipts)
            {
                if (r.ReceiptId.Equals(receiptId)) return r;
            }
            return null;
        }

        public GrainElevatorForwardLot FindForwardLot(EntityId forwardLotId)
        {
            foreach (var f in forwardLots)
            {
                if (f.ForwardLotId.Equals(forwardLotId)) return f;
            }
            return null;
        }

        /// <summary>
        /// Issues a receipt naming one custody lot. The lot must be a custody
        /// lot actually in stock; the holder is the lot's named customer.
        /// Returns the receipt, or null with a diagnostic.
        /// </summary>
        public GrainElevatorReceipt IssueReceipt(
            EntityIdRegistry idRegistry, EntityId elevatorLotId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (idRegistry == null)
            {
                diag.Add("GrainElevatorObligations.IssueReceipt: no id registry — receipt refused.");
                return null;
            }
            var lot = stock != null ? stock.FindLot(elevatorLotId) : null;
            if (lot == null)
            {
                diag.Add($"GrainElevatorObligations.IssueReceipt: lot {elevatorLotId} is not in elevator stock — a receipt names real stored grain.");
                return null;
            }
            if (lot.Kind != GrainElevatorLotKind.Custody)
            {
                diag.Add($"GrainElevatorObligations.IssueReceipt: lot {elevatorLotId} is dealer stock, not custody — receipts are custody obligations (Canon R6 §1.4).");
                return null;
            }
            foreach (var existing in receipts)
            {
                if (!existing.Closed && existing.ElevatorLotId.Equals(elevatorLotId))
                {
                    diag.Add($"GrainElevatorObligations.IssueReceipt: lot {elevatorLotId} already has open receipt {existing.ReceiptId} — one obligation per custody lot.");
                    return null;
                }
            }

            var receipt = new GrainElevatorReceipt
            {
                ReceiptId = idRegistry.Allocate(EntityKind.Lot),
                ElevatorLotId = elevatorLotId,
                Crop = lot.Crop,
                ElevatorGradeId = lot.ElevatorGradeId,
                UnitsIssued = Math.Max(0, lot.GrainUnits),
                UnitsRemaining = Math.Max(0, lot.GrainUnits),
                HolderId = lot.CustomerId,
                HolderName = lot.CustomerName,
                IssuedDayIndex = dayIndex,
            };
            receipts.Add(receipt);
            diag.Add($"GrainElevatorObligations ({elevatorBusinessId}): issued receipt {receipt.ReceiptId} — "
                + $"{receipt.UnitsIssued}u {lot.Crop} (grade {GrainElevatorGradeCatalog.DisplayName(lot.ElevatorGradeId)}) "
                + $"for {lot.CustomerName} ({lot.CustomerId}), naming lot {elevatorLotId}.");
            return receipt;
        }

        /// <summary>
        /// The outcome of one receipt redemption — the released grain lot
        /// (full provenance: farm <- elevator custody) plus the storage fee
        /// owed by the holder (the ledger authority posts the fee; money
        /// never moves here).
        /// </summary>
        public sealed class ReceiptRedemption
        {
            public CropLot ReleasedLot;
            public int FeeCentsOwed;
        }

        /// <summary>
        /// Redeems a receipt: releases up to the requested units from the
        /// NAMED custody lot to the NAMED holder, accruing the storage fee.
        /// Partial redemptions split the lot honestly; the receipt closes
        /// when nothing remains. Anyone but the named holder is refused.
        /// Returns the redemption, or null with a diagnostic.
        /// </summary>
        public ReceiptRedemption RedeemReceipt(
            EntityIdRegistry idRegistry, EntityId receiptId, string holderId,
            int requestedUnits, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var receipt = FindReceipt(receiptId);
            if (receipt == null || receipt.Closed)
            {
                diag.Add($"GrainElevatorObligations.RedeemReceipt: receipt {receiptId} is not open — cannot release against a closed obligation.");
                return null;
            }
            if (!string.Equals(receipt.HolderId, holderId ?? string.Empty, StringComparison.Ordinal))
            {
                diag.Add($"GrainElevatorObligations.RedeemReceipt: receipt {receiptId} belongs to {receipt.HolderName} ({receipt.HolderId}) — the holder must claim their own grain (bailment).");
                return null;
            }
            if (requestedUnits <= 0)
            {
                diag.Add("GrainElevatorObligations.RedeemReceipt: no units requested.");
                return null;
            }

            var lot = stock.FindLot(receipt.ElevatorLotId);
            if (lot == null || lot.Kind != GrainElevatorLotKind.Custody || lot.GrainUnits <= 0)
            {
                diag.Add($"GrainElevatorObligations.RedeemReceipt: receipt {receiptId} names lot {receipt.ElevatorLotId}, which is no longer in custody — the obligation cannot be honored from thin air.");
                return null;
            }

            int released = Math.Min(requestedUnits, Math.Min(receipt.UnitsRemaining, lot.GrainUnits));
            if (released <= 0)
            {
                diag.Add($"GrainElevatorObligations.RedeemReceipt: receipt {receiptId} has nothing left to release.");
                return null;
            }

            // Fee accrues on the units actually released, aging from receipt
            // of custody (the bailment obligation starts at delivery).
            int days = Math.Max(0, dayIndex - lot.ReceivedDayIndex);
            int periods = Math.Max(1, days / 30);
            int feeCents = released * stock.Policy.CustodyFeeCentsPerUnitPer30Days * periods;

            lot.GrainUnits -= released;
            if (lot.GrainUnits <= 0) stock.RemoveLot(lot);
            receipt.UnitsRemaining -= released;
            if (receipt.UnitsRemaining <= 0 || lot.GrainUnits <= 0)
            {
                receipt.Closed = true;
                receipt.UnitsRemaining = 0;
            }

            var releasedLot = BuildReleasedCropLot(idRegistry, lot, released, receipt, diag);
            diag.Add($"GrainElevatorObligations ({elevatorBusinessId}): redeemed receipt {receiptId} — "
                + $"released {released}u to {receipt.HolderName}; fee {feeCents}c for {days} days ({periods} period(s)).");
            return new ReceiptRedemption { ReleasedLot = releasedLot, FeeCentsOwed = feeCents };
        }

        private CropLot BuildReleasedCropLot(
            EntityIdRegistry idRegistry, GrainElevatorStoredLot custodyLot,
            int units, GrainElevatorReceipt receipt, List<string> diag)
        {
            diag = diag ?? diagnostics;
            return new CropLot
            {
                LotId = idRegistry != null ? idRegistry.Allocate(EntityKind.Lot) : EntityId.Invalid,
                Crop = custodyLot.Crop,
                ProductKind = "grain",
                QuantityUnits = units,
                FieldId = custodyLot.FieldId,
                FarmId = custodyLot.FarmId,
                HarvestDayIndex = custodyLot.HarvestDayIndex,
                HarvestedBy = custodyLot.HarvestedBy,
                SeedSource = custodyLot.SeedSource,
                StorageNote = $"released from elevator {elevatorBusinessId} custody (lot {custodyLot.ElevatorLotId}) on receipt {receipt.ReceiptId}; grade {GrainElevatorGradeCatalog.DisplayName(custodyLot.ElevatorGradeId)}",
            };
        }

        /// <summary>
        /// Units of dealer stock still unpromised to any open forward lot —
        /// the creation-time honesty check. Forward lots reserve what they
        /// promise, so the elevator can never sell grain it does not hold.
        /// </summary>
        public int UnpromisedDealerUnits(CropKind crop, string gradeId)
        {
            int held = stock != null ? stock.DealerUnitsAvailable(crop, gradeId) : 0;
            int promised = 0;
            foreach (var f in forwardLots)
            {
                if (f.Status != GrainElevatorForwardLotStatus.Open) continue;
                if (f.Crop != crop) continue;
                if (!string.IsNullOrWhiteSpace(gradeId)
                    && !string.Equals(f.ElevatorGradeId, gradeId.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                promised += f.UnitsRemaining;
            }
            return Math.Max(0, held - promised);
        }

        /// <summary>
        /// Creates a forward-sale lot against REAL stored dealer grain: the
        /// named buyer, the crop/grade, the contracted units, and the price.
        /// Refuses loudly when the unpromised dealer stock cannot cover the
        /// contract — the elevator never sells grain it does not hold.
        /// Custody grain is never eligible (canon bailment lock).
        /// </summary>
        public GrainElevatorForwardLot CreateForwardLot(
            EntityIdRegistry idRegistry, CropKind crop, string gradeId, int units,
            int pricePerUnitCents, string buyerId, string buyerName, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (idRegistry == null)
            {
                diag.Add("GrainElevatorObligations.CreateForwardLot: no id registry — forward lot refused.");
                return null;
            }
            if (units <= 0)
            {
                diag.Add("GrainElevatorObligations.CreateForwardLot: no units contracted.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(buyerId) || string.IsNullOrWhiteSpace(buyerName))
            {
                diag.Add("GrainElevatorObligations.CreateForwardLot: the buyer must be NAMED — anonymous forward sales are refused.");
                return null;
            }
            string grade = string.IsNullOrWhiteSpace(gradeId) ? GrainElevatorGradeCatalog.DefaultGradeId : gradeId.Trim();
            if (!GrainElevatorGradeCatalog.IsKnownGrade(grade))
            {
                diag.Add($"GrainElevatorObligations.CreateForwardLot: unknown grade '{gradeId}' — grades are data, fix the id.");
                return null;
            }

            int unpromised = UnpromisedDealerUnits(crop, grade);
            if (units > unpromised)
            {
                diag.Add($"GrainElevatorObligations.CreateForwardLot: {units}u of {crop} ({grade}) contracted, but only {unpromised}u of unpromised dealer stock is held — the elevator never sells grain it does not hold (MR-P001).");
                return null;
            }

            var forward = new GrainElevatorForwardLot
            {
                ForwardLotId = idRegistry.Allocate(EntityKind.Lot),
                Crop = crop,
                ElevatorGradeId = grade,
                UnitsContracted = units,
                PricePerUnitCents = Math.Max(0, pricePerUnitCents),
                BuyerId = buyerId.Trim(),
                BuyerName = buyerName.Trim(),
                CreatedDayIndex = dayIndex,
            };
            foreach (var lot in stock.Lots)
            {
                if (lot.Kind == GrainElevatorLotKind.Dealer && lot.Crop == crop
                    && string.Equals(lot.ElevatorGradeId, grade, StringComparison.OrdinalIgnoreCase)
                    && lot.GrainUnits > 0)
                {
                    forward.PromisedElevatorLotIds.Add(lot.ElevatorLotId.ToString());
                }
            }
            forwardLots.Add(forward);
            diag.Add($"GrainElevatorObligations ({elevatorBusinessId}): forward lot {forward.ForwardLotId} — "
                + $"{units}u {crop} ({GrainElevatorGradeCatalog.DisplayName(grade)}) for {buyerName} at {pricePerUnitCents}c/u, "
                + $"naming stored dealer lots [{string.Join(", ", forward.PromisedElevatorLotIds.ToArray())}].");
            return forward;
        }

        /// <summary>
        /// Cancels an open forward lot, releasing its reservation. Delivered
        /// units stay delivered — cancellation only frees the undelivered
        /// remainder.
        /// </summary>
        public string CancelForwardLot(EntityId forwardLotId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var forward = FindForwardLot(forwardLotId);
            if (forward == null || forward.Status != GrainElevatorForwardLotStatus.Open)
                return $"GrainElevatorObligations.CancelForwardLot: forward lot {forwardLotId} is not open — nothing to cancel.";
            forward.Status = GrainElevatorForwardLotStatus.Cancelled;
            diag.Add($"GrainElevatorObligations ({elevatorBusinessId}): forward lot {forwardLotId} cancelled — {forward.UnitsRemaining}u of reservation released, {forward.UnitsDelivered}u already delivered stand.");
            return null;
        }

        /// <summary>
        /// The outcome of one forward-lot delivery — the buyer's grain lot
        /// (full provenance: farm <- dealer <- elevator, ready for the W5A
        /// mill merchant intake) plus the dispense lines naming every
        /// contributing stored lot.
        /// </summary>
        public sealed class ForwardDelivery
        {
            public CropLot BuyerLot;
            public List<GrainElevatorDispenseLine> DispenseLines = new List<GrainElevatorDispenseLine>();
            public int SaleValueCents;
        }

        /// <summary>
        /// Delivers units against an open forward lot: withdraws from DEALER
        /// stock (FIFO, crop+grade match), builds the buyer's lot with the
        /// full provenance chain, and values the sale at the contracted
        /// price. Shortfalls deliver what is held — never invented units —
        /// and are recorded loudly. Custody grain is never touched.
        /// </summary>
        public ForwardDelivery DeliverForwardLot(
            EntityIdRegistry idRegistry, EntityId forwardLotId, int requestedUnits, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var forward = FindForwardLot(forwardLotId);
            if (forward == null || forward.Status != GrainElevatorForwardLotStatus.Open)
            {
                diag.Add($"GrainElevatorObligations.DeliverForwardLot: forward lot {forwardLotId} is not open — delivery refused.");
                return null;
            }
            if (requestedUnits <= 0)
            {
                diag.Add("GrainElevatorObligations.DeliverForwardLot: no units requested.");
                return null;
            }

            int due = Math.Min(requestedUnits, forward.UnitsRemaining);
            if (due <= 0)
            {
                diag.Add($"GrainElevatorObligations.DeliverForwardLot: forward lot {forwardLotId} has nothing left to deliver.");
                return null;
            }

            // Snapshot contributor provenance BEFORE withdrawal — emptied lots
            // leave the stock and could not be looked up afterwards.
            var contributors = new Dictionary<string, GrainElevatorStoredLot>();
            foreach (var stored in stock.Lots)
            {
                if (stored.Kind == GrainElevatorLotKind.Dealer && stored.Crop == forward.Crop
                    && string.Equals(stored.ElevatorGradeId, forward.ElevatorGradeId, StringComparison.OrdinalIgnoreCase)
                    && stored.GrainUnits > 0)
                {
                    contributors[stored.ElevatorLotId.ToString()] = stored;
                }
            }

            var lines = stock.WithdrawUnits(GrainElevatorLotKind.Dealer, forward.Crop, forward.ElevatorGradeId, due, diag);
            int delivered = 0;
            foreach (var line in lines) delivered += Math.Max(0, line.UnitsTaken);
            if (delivered <= 0)
            {
                diag.Add($"GrainElevatorObligations.DeliverForwardLot: dealer stock has no {forward.Crop} ({forward.ElevatorGradeId}) to deliver — the elevator cannot sell what it does not hold.");
                return null;
            }

            // Aggregate the buyer's lot from the first contributing lot's
            // provenance (cf. CRP-3 GrainDealer.SellGrain); every
            // contributing lot is named in the dispense lines.
            GrainElevatorStoredLot first = null;
            contributors.TryGetValue(lines[0].ElevatorLotId.ToString(), out first);
            string chainNote = string.Join(" | ", DispenseChains(lines).ToArray());
            var buyerLot = new CropLot
            {
                LotId = idRegistry != null ? idRegistry.Allocate(EntityKind.Lot) : EntityId.Invalid,
                Crop = forward.Crop,
                ProductKind = "grain",
                QuantityUnits = delivered,
                FieldId = first != null ? first.FieldId : string.Empty,
                FarmId = first != null ? first.FarmId : string.Empty,
                HarvestDayIndex = first != null ? first.HarvestDayIndex : dayIndex,
                HarvestedBy = first != null ? first.HarvestedBy : EntityId.Invalid,
                SeedSource = first != null ? first.SeedSource : string.Empty,
                StorageNote = $"forward lot {forward.ForwardLotId} from elevator {elevatorBusinessId} to {forward.BuyerName} ({forward.BuyerId}); grade {GrainElevatorGradeCatalog.DisplayName(forward.ElevatorGradeId)}; sources: {chainNote}",
            };

            forward.UnitsDelivered += delivered;
            if (forward.UnitsRemaining <= 0) forward.Status = GrainElevatorForwardLotStatus.Fulfilled;

            diag.Add($"GrainElevatorObligations ({elevatorBusinessId}): delivered {delivered}u on forward lot {forward.ForwardLotId} "
                + $"to {forward.BuyerName} ({delivered}/{forward.UnitsContracted}u contracted) — status {forward.Status}.");
            return new ForwardDelivery
            {
                BuyerLot = buyerLot,
                DispenseLines = lines,
                SaleValueCents = delivered * forward.PricePerUnitCents,
            };
        }

        private static List<string> DispenseChains(List<GrainElevatorDispenseLine> lines)
        {
            var chains = new List<string>();
            foreach (var line in lines) chains.Add($"lot {line.ElevatorLotId}: {line.ProvenanceChain}");
            return chains;
        }

        /// <summary>W5B save contract: lives inside the owning obligations class.</summary>
        [Serializable]
        public sealed class GrainElevatorObligationsSaveDto
        {
            public List<GrainElevatorReceipt> Receipts = new List<GrainElevatorReceipt>();
            public List<GrainElevatorForwardLot> ForwardLots = new List<GrainElevatorForwardLot>();
        }

        public GrainElevatorObligationsSaveDto CaptureSaveDto()
        {
            var dto = new GrainElevatorObligationsSaveDto();
            foreach (var receipt in receipts)
            {
                dto.Receipts.Add(new GrainElevatorReceipt
                {
                    ReceiptId = receipt.ReceiptId,
                    ElevatorLotId = receipt.ElevatorLotId,
                    Crop = receipt.Crop,
                    ElevatorGradeId = receipt.ElevatorGradeId,
                    UnitsIssued = receipt.UnitsIssued,
                    UnitsRemaining = receipt.UnitsRemaining,
                    HolderId = receipt.HolderId,
                    HolderName = receipt.HolderName,
                    IssuedDayIndex = receipt.IssuedDayIndex,
                    Closed = receipt.Closed,
                });
            }
            foreach (var forward in forwardLots)
            {
                dto.ForwardLots.Add(new GrainElevatorForwardLot
                {
                    ForwardLotId = forward.ForwardLotId,
                    Crop = forward.Crop,
                    ElevatorGradeId = forward.ElevatorGradeId,
                    UnitsContracted = forward.UnitsContracted,
                    UnitsDelivered = forward.UnitsDelivered,
                    PricePerUnitCents = forward.PricePerUnitCents,
                    BuyerId = forward.BuyerId,
                    BuyerName = forward.BuyerName,
                    PromisedElevatorLotIds = new List<string>(forward.PromisedElevatorLotIds),
                    CreatedDayIndex = forward.CreatedDayIndex,
                    Status = forward.Status,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(GrainElevatorObligationsSaveDto dto)
        {
            receipts.Clear();
            forwardLots.Clear();
            if (dto == null) return;
            if (dto.Receipts != null)
            {
                foreach (var receipt in dto.Receipts)
                {
                    if (receipt == null) continue;
                    receipts.Add(receipt);
                }
            }
            if (dto.ForwardLots != null)
            {
                foreach (var forward in dto.ForwardLots)
                {
                    if (forward == null) continue;
                    forwardLots.Add(forward);
                }
            }
        }
    }
}
