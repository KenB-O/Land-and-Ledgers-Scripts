using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.GrainMill
{
    /// <summary>
    /// W5A: one toll-custody grain lot. The customer's grain in the mill's
    /// custody — the mill NEVER books it as inventory value. It is ground
    /// for the named customer only, and the products belong to the customer
    /// (minus the mill's toll). Toll grain is not fungible with merchant
    /// grain: it lives in this custody wrapper, not the mill's inventory.
    /// </summary>
    [Serializable]
    public sealed class TollGrainCustodyLot
    {
        public CropLot Lot;
        public string CustomerId = string.Empty;   // farmer / household id
        public string CustomerName = string.Empty; // named customer — never anonymous
        public int ReceivedDayIndex;

        public TollGrainCustodyLot() { }

        public string ProvenanceChain()
        {
            string lot = Lot != null ? Lot.LotId.ToString() : "no-lot";
            string farm = Lot != null && !string.IsNullOrWhiteSpace(Lot.FarmId) ? Lot.FarmId : "unnamed-farm";
            return $"toll custody: lot {lot} ({Lot?.Crop}, {Lot?.QuantityUnits}u) / farm {farm} / customer {CustomerName} ({CustomerId}) / day {ReceivedDayIndex}";
        }
    }

    /// <summary>
    /// W5A: the mill's grain intake. Two books, never mixed:
    ///
    /// (1) MERCHANT grain — bought by the mill (dealer, farm gate, elevator
    /// release), booked as mill inventory. Grist batches in merchant mode
    /// grind this grain; the products are the mill's to sell.
    /// (2) TOLL grain — the customer's grain in the mill's CUSTODY for
    /// custom (toll) grinding. Never booked as mill inventory, never ground
    /// for anyone but the named customer.
    ///
    /// Lots must be real: threshed "grain" lots with a valid lot id (HF-1).
    /// Anonymous or non-grain lots are refused loudly. Grist batches consume
    /// from exactly ONE grain lot, so every product lot traces to exactly
    /// one farm's grain.
    /// </summary>
    public sealed class GrainMillGrainStock
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<CropLot> merchantLots = new List<CropLot>();
        private readonly List<TollGrainCustodyLot> tollCustody = new List<TollGrainCustodyLot>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<CropLot> MerchantLots => merchantLots;
        public IReadOnlyList<TollGrainCustodyLot> TollCustodyLots => tollCustody;

        /// <summary>Receives grain the mill BOUGHT — booked as mill inventory.</summary>
        public string ReceiveMerchantLot(CropLot lot, List<string> diag)
        {
            diag = diag ?? diagnostics;
            string refusal = ValidateGrainLot(lot, "merchant");
            if (refusal != null) return refusal;

            foreach (var existing in merchantLots)
            {
                if (existing.LotId.Equals(lot.LotId))
                    return $"GrainMillGrainStock: merchant lot {lot.LotId} already in inventory — double intake refused.";
            }

            merchantLots.Add(lot);
            diag.Add($"GrainMillGrainStock: received MERCHANT lot {lot.LotId} — {lot.QuantityUnits}u {lot.Crop} grain from farm {lot.FarmId} (mill-owned).");
            return null;
        }

        /// <summary>
        /// Receives grain a customer brings for TOLL grinding — custody, not
        /// inventory. The customer must be named; anonymous toll grain is
        /// refused (the products must have an owner to return to).
        /// </summary>
        public string ReceiveTollLot(CropLot lot, string customerId, string customerName, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            string refusal = ValidateGrainLot(lot, "toll");
            if (refusal != null) return refusal;
            if (string.IsNullOrWhiteSpace(customerId) || string.IsNullOrWhiteSpace(customerName))
                return $"GrainMillGrainStock: toll lot {lot.LotId} refused — the customer must be named (toll grain needs an owner to return to).";

            foreach (var existing in tollCustody)
            {
                if (existing.Lot != null && existing.Lot.LotId.Equals(lot.LotId))
                    return $"GrainMillGrainStock: toll lot {lot.LotId} already in custody — double intake refused.";
            }

            var custody = new TollGrainCustodyLot
            {
                Lot = lot,
                CustomerId = customerId,
                CustomerName = customerName,
                ReceivedDayIndex = dayIndex,
            };
            tollCustody.Add(custody);
            diag.Add($"GrainMillGrainStock: received TOLL lot — {custody.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Takes `units` grain of the requested crop from the OLDEST merchant
        /// lot of that crop. A grist batch consumes from exactly ONE grain
        /// lot, so every product lot traces to exactly one farm's grain —
        /// when the oldest single lot cannot cover the request the batch is
        /// refused (the caller re-requests per lot), never silently
        /// short-filled. Returns the (lot, taken) pair, or null with a
        /// diagnostic — the mill cannot grind grain it does not hold.
        /// </summary>
        public KeyValuePair<CropLot, int>? TryTakeMerchantGrain(CropKind crop, int units, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (units <= 0)
            {
                diag.Add("GrainMillGrainStock: no grain requested.");
                return null;
            }

            CropLot oldest = null;
            foreach (var lot in merchantLots)
            {
                if (lot.Crop == crop && lot.QuantityUnits > 0)
                {
                    oldest = lot;
                    break;
                }
            }
            if (oldest == null)
            {
                diag.Add($"GrainMillGrainStock: no merchant {crop} grain on hand — cannot grind what the mill does not hold (MR-P001).");
                return null;
            }
            if (oldest.QuantityUnits < units)
            {
                diag.Add($"GrainMillGrainStock: oldest merchant {crop} lot {oldest.LotId} holds {oldest.QuantityUnits}u, {units}u requested — "
                    + "a grist batch consumes from exactly one grain lot; re-request per lot (provenance, cf. W4A).");
                return null;
            }

            oldest.QuantityUnits -= units;
            if (oldest.QuantityUnits <= 0) merchantLots.Remove(oldest);
            return new KeyValuePair<CropLot, int>(oldest, units);
        }

        /// <summary>
        /// Takes up to `units` from the named toll custody lot (partial takes
        /// allowed). Returns the custody lot and units taken, or null when
        /// the lot is not in custody or holds too little.
        /// </summary>
        public KeyValuePair<TollGrainCustodyLot, int>? TryTakeTollGrain(string tollLotId, int units, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (units <= 0)
            {
                diag.Add("GrainMillGrainStock: no grain requested.");
                return null;
            }

            TollGrainCustodyLot custody = null;
            foreach (var entry in tollCustody)
            {
                if (entry.Lot != null && entry.Lot.LotId.ToString() == tollLotId)
                {
                    custody = entry;
                    break;
                }
            }
            if (custody == null || custody.Lot == null)
            {
                diag.Add($"GrainMillGrainStock: toll lot '{tollLotId}' is not in the mill's custody — toll batches grind only named customer grain.");
                return null;
            }
            if (custody.Lot.QuantityUnits < units)
            {
                diag.Add($"GrainMillGrainStock: toll lot {tollLotId} holds {custody.Lot.QuantityUnits}u, {units}u requested — toll batches never borrow from merchant stock.");
                return null;
            }

            custody.Lot.QuantityUnits -= units;
            if (custody.Lot.QuantityUnits <= 0) tollCustody.Remove(custody);
            return new KeyValuePair<TollGrainCustodyLot, int>(custody, units);
        }

        public int MerchantUnitsOnHand(CropKind crop)
        {
            int total = 0;
            foreach (var lot in merchantLots)
            {
                if (lot.Crop == crop) total += Math.Max(0, lot.QuantityUnits);
            }
            return total;
        }

        public int TollUnitsInCustody()
        {
            int total = 0;
            foreach (var entry in tollCustody)
            {
                if (entry.Lot != null) total += Math.Max(0, entry.Lot.QuantityUnits);
            }
            return total;
        }

        private static string ValidateGrainLot(CropLot lot, string book)
        {
            if (lot == null)
                return $"GrainMillGrainStock: null {book} lot refused — no grain without a lot record.";
            if (lot.LotId == EntityId.Invalid || !lot.LotId.IsValid)
                return $"GrainMillGrainStock: {book} lot refused — a grain lot needs a real EntityId (HF-1).";
            if (!string.Equals(lot.ProductKind, "grain", StringComparison.OrdinalIgnoreCase))
                return $"GrainMillGrainStock: {book} lot {lot.LotId} is '{lot.ProductKind}', not threshed grain — refused.";
            if (lot.QuantityUnits <= 0)
                return $"GrainMillGrainStock: {book} lot {lot.LotId} refused — positive unit count required.";
            if (GristMillConversionData.ProfileFor(lot.Crop) == null)
                return $"GrainMillGrainStock: {book} lot {lot.LotId} is {lot.Crop} — the mill has no grist profile for it (hay is not ground).";
            return null;
        }

        /// <summary>W5A save contract: lives inside the owning stock class.</summary>
        [Serializable]
        public sealed class GrainMillGrainStockSaveDto
        {
            public List<CropLot> MerchantLots = new List<CropLot>();
            public List<TollGrainCustodyLot> TollCustodyLots = new List<TollGrainCustodyLot>();
        }

        public GrainMillGrainStockSaveDto CaptureSaveDto()
        {
            var dto = new GrainMillGrainStockSaveDto();
            foreach (var lot in merchantLots)
            {
                dto.MerchantLots.Add(new CropLot
                {
                    LotId = lot.LotId,
                    Crop = lot.Crop,
                    ProductKind = lot.ProductKind,
                    QuantityUnits = lot.QuantityUnits,
                    FieldId = lot.FieldId,
                    FarmId = lot.FarmId,
                    HarvestDayIndex = lot.HarvestDayIndex,
                    HarvestedBy = lot.HarvestedBy,
                    SeedSource = lot.SeedSource,
                    StorageNote = lot.StorageNote,
                });
            }
            foreach (var entry in tollCustody)
            {
                if (entry == null || entry.Lot == null) continue;
                dto.TollCustodyLots.Add(new TollGrainCustodyLot
                {
                    Lot = new CropLot
                    {
                        LotId = entry.Lot.LotId,
                        Crop = entry.Lot.Crop,
                        ProductKind = entry.Lot.ProductKind,
                        QuantityUnits = entry.Lot.QuantityUnits,
                        FieldId = entry.Lot.FieldId,
                        FarmId = entry.Lot.FarmId,
                        HarvestDayIndex = entry.Lot.HarvestDayIndex,
                        HarvestedBy = entry.Lot.HarvestedBy,
                        SeedSource = entry.Lot.SeedSource,
                        StorageNote = entry.Lot.StorageNote,
                    },
                    CustomerId = entry.CustomerId,
                    CustomerName = entry.CustomerName,
                    ReceivedDayIndex = entry.ReceivedDayIndex,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(GrainMillGrainStockSaveDto dto)
        {
            merchantLots.Clear();
            tollCustody.Clear();
            if (dto == null) return;
            if (dto.MerchantLots != null)
            {
                foreach (var lot in dto.MerchantLots)
                {
                    if (lot == null) continue;
                    merchantLots.Add(lot);
                }
            }
            if (dto.TollCustodyLots != null)
            {
                foreach (var entry in dto.TollCustodyLots)
                {
                    if (entry == null || entry.Lot == null) continue;
                    tollCustody.Add(entry);
                }
            }
        }
    }
}
