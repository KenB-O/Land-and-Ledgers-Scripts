using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.GrainElevator
{
    /// <summary>
    /// W5B: which book a stored lot sits on. Canon R6 §1.4 CANON LOCK:
    /// PHYSICAL POSSESSION IS NOT AUTOMATIC OWNERSHIP. Custody grain is a
    /// bailment (the elevator holds it for a NAMED owner under receipt);
    /// dealer grain is elevator-owned dealing stock ("a warehouse/elevator
    /// may also conduct dealing where its actual business configuration
    /// supports it"). The two books are never mixed.
    /// </summary>
    public enum GrainElevatorLotKind
    {
        Custody = 0, // stored FOR a named customer — bailment, never the elevator's to sell
        Dealer = 1,  // bought BY the elevator — elevator-owned, eligible for forward lots
    }

    /// <summary>
    /// W5B: the elevator's own stored-lot record — elevator-side custody of
    /// one grain lot. The upstream CropLot's provenance (lot id, farm, field,
    /// seed source, harvest day, harvest labor) is COPIED, never re-authored:
    /// the CRP-2 CropLot stays the authority for what the farm produced.
    /// Elevator-side fields (grade, book, customer/seller, cost, received
    /// day) belong to the elevator alone.
    ///
    /// Note: this intentionally does not wrap the CRP-3 GrainElevator
    /// storage class (Farming.Crops). That class is the capability-level
    /// storage primitive; it accepts any lot silently and carries no
    /// provenance gate, no grade data, and no save contract. The business
    /// runtime needs all three, so it owns its storage natively.
    /// </summary>
    [Serializable]
    public sealed class GrainElevatorStoredLot
    {
        public EntityId ElevatorLotId = EntityId.Invalid; // elevator-side custody id (EntityKind.Lot)
        public GrainElevatorLotKind Kind = GrainElevatorLotKind.Custody;
        public int GrainUnits;
        public string ElevatorGradeId = string.Empty; // GrainElevatorGradeCatalog id, assigned at intake
        public int ReceivedDayIndex;                 // custody starts at DELIVERY (bailment), not harvest
        public int UnitCostCents;                    // dealer book only: what the elevator paid

        // Custody book: the named owner the elevator holds FOR (bailment needs an owner).
        public string CustomerId = string.Empty;
        public string CustomerName = string.Empty;

        // Dealer book: the named seller the elevator bought FROM.
        public string SellerBusinessId = string.Empty;

        // Copied upstream provenance (CRP-2 authority).
        public string SourceLotId = string.Empty;    // the CropLot this was received from
        public CropKind Crop = CropKind.Unspecified;
        public string FarmId = string.Empty;
        public string FieldId = string.Empty;
        public string SeedSource = string.Empty;
        public int HarvestDayIndex;
        public EntityId HarvestedBy = EntityId.Invalid;

        public GrainElevatorStoredLot() { }

        public string ProvenanceChain()
        {
            string book = Kind == GrainElevatorLotKind.Custody
                ? $"custody for {CustomerName} ({CustomerId})"
                : $"dealer stock, bought from {SellerBusinessId}";
            return $"{Crop} | {GrainUnits}u | grade {GrainElevatorGradeCatalog.DisplayName(ElevatorGradeId)}"
                + $" | farm {FarmId} | source lot {SourceLotId} | {book} | elevator lot {ElevatorLotId}";
        }
    }

    /// <summary>
    /// W5B: the elevator's storage policy — capacity and fees as DATA, not
    /// canon. Exact elevator capacity and storage charges are an explicit
    /// Canon R6 §6 calibration hold; the fee default follows the CRP-3
    /// calibration (2c per unit per 30 days). Fees accrue ONLY on custody
    /// grain (owed by the named storer); the elevator charges itself no fee
    /// on its own dealer stock.
    /// </summary>
    [Serializable]
    public sealed class GrainElevatorStoragePolicy
    {
        public int CapacityUnits = 5000;
        public int CustodyFeeCentsPerUnitPer30Days = 2;

        public GrainElevatorStoragePolicy() { }

        public GrainElevatorStoragePolicy(int capacityUnits, int custodyFeeCentsPerUnitPer30Days)
        {
            CapacityUnits = Math.Max(0, capacityUnits);
            CustodyFeeCentsPerUnitPer30Days = Math.Max(0, custodyFeeCentsPerUnitPer30Days);
        }
    }

    /// <summary>
    /// W5B: one withdrawal of grain units — the audit line of what left the
    /// elevator, preserving each source lot's provenance.
    /// </summary>
    [Serializable]
    public sealed class GrainElevatorDispenseLine
    {
        public EntityId ElevatorLotId = EntityId.Invalid;
        public int UnitsTaken;
        public string ElevatorGradeId = string.Empty;
        public CropKind Crop = CropKind.Unspecified;
        public string ProvenanceChain = string.Empty;

        public GrainElevatorDispenseLine() { }
    }

    /// <summary>
    /// W5B: the elevator's two-book grain stock — custody and dealer lots in,
    /// released/redemption units out, FIFO so stock ages honestly (oldest
    /// receipt first). Capacity is physical and SHARED across both books.
    /// Intake rules (all loud refusals):
    /// - lots must be real threshed "grain" with a valid HF-1 lot id and a
    ///   NAMED farm (anonymous grain is refused — every stored lot traces
    ///   to a real farm/dealer lot; upstream-provenance doctrine);
    /// - custody intake requires a NAMED customer (bailment needs an owner
    ///   to return the grain to);
    /// - dealer intake requires a NAMED seller (the elevator buys from
    ///   named farms/dealers, never thin air);
    /// - the same lot id is never double-intaked (a lot is in exactly one
    ///   place — custody discipline);
    /// - grade is assigned by the elevator at intake; undeclared grain
    ///   takes the honest default loudly; unknown grade ids are refused
    ///   (grades are data).
    /// </summary>
    public sealed class GrainElevatorGrainStock
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<GrainElevatorStoredLot> lots = new List<GrainElevatorStoredLot>();
        private readonly string elevatorBusinessId;
        private GrainElevatorStoragePolicy policy = new GrainElevatorStoragePolicy();

        public GrainElevatorGrainStock(string elevatorBusinessId)
        {
            this.elevatorBusinessId = elevatorBusinessId ?? string.Empty;
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<GrainElevatorStoredLot> Lots => lots;
        public string ElevatorBusinessId => elevatorBusinessId;
        public GrainElevatorStoragePolicy Policy => policy;

        public void SetPolicy(GrainElevatorStoragePolicy newPolicy)
        {
            policy = newPolicy ?? new GrainElevatorStoragePolicy();
        }

        public int StoredUnits
        {
            get
            {
                int total = 0;
                foreach (var lot in lots) total += Math.Max(0, lot.GrainUnits);
                return total;
            }
        }

        public int UnitsOnBook(GrainElevatorLotKind kind)
        {
            int total = 0;
            foreach (var lot in lots)
            {
                if (lot.Kind == kind) total += Math.Max(0, lot.GrainUnits);
            }
            return total;
        }

        public int DealerUnitsAvailable(CropKind crop, string gradeId)
        {
            int total = 0;
            foreach (var lot in lots)
            {
                if (lot.Kind != GrainElevatorLotKind.Dealer) continue;
                if (lot.Crop != crop) continue;
                if (!string.IsNullOrWhiteSpace(gradeId)
                    && !string.Equals(lot.ElevatorGradeId, gradeId.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                total += Math.Max(0, lot.GrainUnits);
            }
            return total;
        }

        public GrainElevatorStoredLot FindLot(EntityId elevatorLotId)
        {
            foreach (var lot in lots)
            {
                if (lot.ElevatorLotId.Equals(elevatorLotId)) return lot;
            }
            return null;
        }

        /// <summary>Removes an emptied lot from custody (redemption path).</summary>
        public void RemoveLot(GrainElevatorStoredLot lot)
        {
            if (lot != null) lots.Remove(lot);
        }

        private string ValidateUpstream(CropLot lot, string path)
        {
            if (lot == null)
                return $"GrainElevatorGrainStock: null lot refused on {path} — no grain without a lot record.";
            if (lot.LotId == EntityId.Invalid || !lot.LotId.IsValid)
                return $"GrainElevatorGrainStock: {path} refused — a grain lot needs a real EntityId (HF-1).";
            if (!string.Equals(lot.ProductKind, "grain", StringComparison.OrdinalIgnoreCase))
                return $"GrainElevatorGrainStock: {path} refused — lot {lot.LotId} is '{lot.ProductKind}', not threshed grain.";
            if (lot.QuantityUnits <= 0)
                return $"GrainElevatorGrainStock: {path} refused — lot {lot.LotId} needs a positive unit count.";
            if (string.IsNullOrWhiteSpace(lot.FarmId))
                return $"GrainElevatorGrainStock: {path} refused — lot {lot.LotId} names no farm. Anonymous grain is refused: every stored lot traces to a real farm/dealer lot.";
            foreach (var existing in lots)
            {
                if (string.Equals(existing.SourceLotId, lot.LotId.ToString(), StringComparison.Ordinal))
                    return $"GrainElevatorGrainStock: {path} refused — lot {lot.LotId} is already in elevator custody (lot {existing.ElevatorLotId}). A lot is in exactly one place.";
            }
            return null;
        }

        private string AssignGrade(string gradeId, List<string> diag)
        {
            if (string.IsNullOrWhiteSpace(gradeId))
            {
                diag.Add("GrainElevatorGrainStock: no grade declared at intake — "
                    + $"graded as '{GrainElevatorGradeCatalog.DefaultGradeId}' (the honest default contract grade), recorded loudly.");
                return GrainElevatorGradeCatalog.DefaultGradeId;
            }
            string trimmed = gradeId.Trim();
            if (!GrainElevatorGradeCatalog.IsKnownGrade(trimmed))
                return null; // caller refuses: grades are data, unknown ids are data errors
            return trimmed;
        }

        private GrainElevatorStoredLot BuildLot(
            EntityId elevatorLotId, CropLot source, GrainElevatorLotKind kind,
            string gradeId, int receivedDayIndex, int unitCostCents,
            string customerId, string customerName, string sellerBusinessId)
        {
            return new GrainElevatorStoredLot
            {
                ElevatorLotId = elevatorLotId,
                Kind = kind,
                GrainUnits = source.QuantityUnits,
                ElevatorGradeId = gradeId,
                ReceivedDayIndex = receivedDayIndex,
                UnitCostCents = Math.Max(0, unitCostCents),
                CustomerId = customerId ?? string.Empty,
                CustomerName = customerName ?? string.Empty,
                SellerBusinessId = sellerBusinessId ?? string.Empty,
                SourceLotId = source.LotId.ToString(),
                Crop = source.Crop,
                FarmId = source.FarmId,
                FieldId = source.FieldId,
                SeedSource = source.SeedSource,
                HarvestDayIndex = source.HarvestDayIndex,
                HarvestedBy = source.HarvestedBy,
            };
        }

        /// <summary>
        /// Receives grain a NAMED customer delivers for STORAGE — bailment.
        /// The elevator holds it, owns none of it; custody fees accrue to
        /// the customer. Returns the elevator lot id in diagnostics, or the
        /// refusal string.
        /// </summary>
        public string ReceiveCustodyLot(
            EntityIdRegistry idRegistry, CropLot lot,
            string customerId, string customerName,
            string declaredGradeId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (idRegistry == null) return "GrainElevatorGrainStock.ReceiveCustodyLot: no id registry — custody refused.";
            string refusal = ValidateUpstream(lot, "custody");
            if (refusal != null) return refusal;
            if (string.IsNullOrWhiteSpace(customerId) || string.IsNullOrWhiteSpace(customerName))
                return $"GrainElevatorGrainStock: custody lot {lot.LotId} refused — the customer must be NAMED (bailment needs an owner to return the grain to).";
            if (StoredUnits + lot.QuantityUnits > policy.CapacityUnits)
                return $"GrainElevatorGrainStock: custody lot {lot.LotId} refused — {lot.QuantityUnits}u would exceed capacity {policy.CapacityUnits} (stored {StoredUnits}).";

            string grade = AssignGrade(declaredGradeId, diag);
            if (grade == null)
                return $"GrainElevatorGrainStock.ReceiveCustodyLot: unknown grade '{declaredGradeId}' — grades are data, fix the id.";

            var stored = BuildLot(idRegistry.Allocate(EntityKind.Lot), lot, GrainElevatorLotKind.Custody,
                grade, dayIndex, 0, customerId.Trim(), customerName.Trim(), string.Empty);
            lots.Add(stored);
            diag.Add($"GrainElevatorGrainStock ({elevatorBusinessId}): received CUSTODY lot — {stored.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Receives grain the elevator BUYS for its own dealing — booked as
        /// elevator-owned dealer stock (Canon R6 §1.4: dealing where the
        /// configuration supports it). The seller is named; the cost is
        /// recorded honestly. Returns the refusal string, or null.
        /// </summary>
        public string ReceiveDealerLot(
            EntityIdRegistry idRegistry, CropLot lot,
            string sellerBusinessId, int unitCostCents,
            string declaredGradeId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (idRegistry == null) return "GrainElevatorGrainStock.ReceiveDealerLot: no id registry — dealing intake refused.";
            string refusal = ValidateUpstream(lot, "dealer intake");
            if (refusal != null) return refusal;
            if (string.IsNullOrWhiteSpace(sellerBusinessId))
                return $"GrainElevatorGrainStock: dealer lot {lot.LotId} refused — the seller must be NAMED (the elevator buys from named farms/dealers, never thin air).";
            if (StoredUnits + lot.QuantityUnits > policy.CapacityUnits)
                return $"GrainElevatorGrainStock: dealer lot {lot.LotId} refused — {lot.QuantityUnits}u would exceed capacity {policy.CapacityUnits} (stored {StoredUnits}).";

            string grade = AssignGrade(declaredGradeId, diag);
            if (grade == null)
                return $"GrainElevatorGrainStock.ReceiveDealerLot: unknown grade '{declaredGradeId}' — grades are data, fix the id.";

            var stored = BuildLot(idRegistry.Allocate(EntityKind.Lot), lot, GrainElevatorLotKind.Dealer,
                grade, dayIndex, unitCostCents, string.Empty, string.Empty, sellerBusinessId.Trim());
            lots.Add(stored);
            diag.Add($"GrainElevatorGrainStock ({elevatorBusinessId}): received DEALER lot — {stored.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Re-grades a stored lot (the elevator is the grading authority for
        /// what it holds). Unknown grade ids are refused; the re-grade is
        /// recorded loudly. Returns the refusal string, or null.
        /// </summary>
        public string ReGradeLot(EntityId elevatorLotId, string gradeId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var lot = FindLot(elevatorLotId);
            if (lot == null)
                return $"GrainElevatorGrainStock.ReGradeLot: lot {elevatorLotId} is not in elevator custody — grading refused.";
            string trimmed = (gradeId ?? string.Empty).Trim();
            if (!GrainElevatorGradeCatalog.IsKnownGrade(trimmed))
                return $"GrainElevatorGrainStock.ReGradeLot: unknown grade '{gradeId}' — grades are data, fix the id.";
            string old = lot.ElevatorGradeId;
            lot.ElevatorGradeId = trimmed;
            diag.Add($"GrainElevatorGrainStock ({elevatorBusinessId}): re-graded lot {elevatorLotId} from '{old}' to '{trimmed}' — {lot.ProvenanceChain()}.");
            return null;
        }

        private void SortFifo()
        {
            lots.Sort((a, b) =>
            {
                int day = a.ReceivedDayIndex.CompareTo(b.ReceivedDayIndex);
                return day != 0 ? day : a.ElevatorLotId.ToString().CompareTo(b.ElevatorLotId.ToString());
            });
        }

        /// <summary>
        /// Withdraws up to the requested units from the named book, oldest
        /// receipts first, recording dispense lines with provenance. Used by
        /// custody redemptions (Custody book) and forward-lot deliveries
        /// (Dealer book). A shortfall returns fewer lines — never invented
        /// units. Lots are removed when emptied (a lot is in exactly one
        /// place).
        /// </summary>
        public List<GrainElevatorDispenseLine> WithdrawUnits(
            GrainElevatorLotKind kind, CropKind crop, string gradeFilter, int units, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var lines = new List<GrainElevatorDispenseLine>();
            if (units <= 0) return lines;

            SortFifo();

            int remaining = units;
            foreach (var lot in lots)
            {
                if (remaining <= 0) break;
                if (lot.Kind != kind) continue;
                if (lot.Crop != crop) continue;
                if (!string.IsNullOrWhiteSpace(gradeFilter)
                    && !string.Equals(lot.ElevatorGradeId, gradeFilter.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                int available = Math.Max(0, lot.GrainUnits);
                if (available <= 0) continue;
                int take = Math.Min(remaining, available);
                lot.GrainUnits -= take;
                remaining -= take;
                lines.Add(new GrainElevatorDispenseLine
                {
                    ElevatorLotId = lot.ElevatorLotId,
                    UnitsTaken = take,
                    ElevatorGradeId = lot.ElevatorGradeId,
                    Crop = lot.Crop,
                    ProvenanceChain = lot.ProvenanceChain(),
                });
            }

            lots.RemoveAll(l => l.GrainUnits <= 0);

            if (remaining > 0)
            {
                string book = kind == GrainElevatorLotKind.Custody ? "custody" : "dealer";
                diag.Add($"GrainElevatorGrainStock ({elevatorBusinessId}): shortfall — requested {units} {book} {crop} units, withdrew {units - remaining}. Empty bins stay empty; nothing invented.");
            }
            return lines;
        }

        /// <summary>
        /// Storage fee owed on one custody lot at the given day: units x
        /// fee-per-unit-per-30-days x 30-day periods (minimum one period).
        /// Aging runs from RECEIPT day — the bailment obligation starts at
        /// delivery, not harvest. Dealer stock accrues no fee (the elevator
        /// charges itself nothing).
        /// </summary>
        public int AccruedFeeCents(GrainElevatorStoredLot lot, int dayIndex)
        {
            if (lot == null || lot.Kind != GrainElevatorLotKind.Custody) return 0;
            int days = Math.Max(0, dayIndex - lot.ReceivedDayIndex);
            int periods = Math.Max(1, days / 30);
            return Math.Max(0, lot.GrainUnits) * policy.CustodyFeeCentsPerUnitPer30Days * periods;
        }

        /// <summary>W5B save contract: lives inside the owning stock class.</summary>
        [Serializable]
        public sealed class GrainElevatorGrainStockSaveDto
        {
            public GrainElevatorStoragePolicy Policy = new GrainElevatorStoragePolicy();
            public List<GrainElevatorStoredLot> Lots = new List<GrainElevatorStoredLot>();
        }

        public GrainElevatorGrainStockSaveDto CaptureSaveDto()
        {
            var dto = new GrainElevatorGrainStockSaveDto
            {
                Policy = new GrainElevatorStoragePolicy(policy.CapacityUnits, policy.CustodyFeeCentsPerUnitPer30Days),
            };
            foreach (var lot in lots)
            {
                dto.Lots.Add(new GrainElevatorStoredLot
                {
                    ElevatorLotId = lot.ElevatorLotId,
                    Kind = lot.Kind,
                    GrainUnits = lot.GrainUnits,
                    ElevatorGradeId = lot.ElevatorGradeId,
                    ReceivedDayIndex = lot.ReceivedDayIndex,
                    UnitCostCents = lot.UnitCostCents,
                    CustomerId = lot.CustomerId,
                    CustomerName = lot.CustomerName,
                    SellerBusinessId = lot.SellerBusinessId,
                    SourceLotId = lot.SourceLotId,
                    Crop = lot.Crop,
                    FarmId = lot.FarmId,
                    FieldId = lot.FieldId,
                    SeedSource = lot.SeedSource,
                    HarvestDayIndex = lot.HarvestDayIndex,
                    HarvestedBy = lot.HarvestedBy,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(GrainElevatorGrainStockSaveDto dto)
        {
            lots.Clear();
            if (dto == null) return;
            if (dto.Policy != null) policy = dto.Policy;
            if (dto.Lots != null)
            {
                foreach (var lot in dto.Lots)
                {
                    if (lot == null) continue;
                    lots.Add(lot);
                }
            }
        }
    }
}
