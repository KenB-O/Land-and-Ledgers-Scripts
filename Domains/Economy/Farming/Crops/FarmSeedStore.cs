using System;
using System.Collections.Generic;
using System.Text;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Crops
{
    /// <summary>
    /// W5C: one purchase of seed by a farm. The store never moves money itself —
    /// the caller settles <see cref="TotalCents"/> through whatever ledger the
    /// farm uses. No hidden cash flows.
    /// </summary>
    [Serializable]
    public sealed class SeedPurchase
    {
        public string MerchantBusinessId = string.Empty;
        public string MerchantName = string.Empty;
        public CropKind Crop;
        public string VarietyId = string.Empty;
        public int Units;
        public int PricePerUnitCents;
        public int TotalCents;
        public int DayIndex;
        public List<string> LotIds = new List<string>();

        public SeedPurchase() { }
    }

    /// <summary>
    /// W5C: one line of a seed consumption — which lot the units came from.
    /// </summary>
    [Serializable]
    public sealed class SeedConsumptionLine
    {
        public EntityId LotId = EntityId.Invalid;
        public string VarietyId = string.Empty;
        public int UnitsTaken;
        public string ProvenanceRendered = string.Empty;

        public SeedConsumptionLine() { }
    }

    /// <summary>
    /// W5C: the result of consuming seed for planting — the lots drawn (FIFO)
    /// and the source string the planting records.
    /// </summary>
    [Serializable]
    public sealed class SeedConsumption
    {
        public CropKind Crop;
        public int Units;
        public List<SeedConsumptionLine> Lines = new List<SeedConsumptionLine>();

        public SeedConsumption() { }

        /// <summary>Renders what was sown, e.g. "80u Red Fife wheat seed: breeder: David Fife → ...".</summary>
        public string RenderSource()
        {
            var sb = new StringBuilder();
            foreach (var line in Lines)
            {
                if (line == null) continue;
                if (sb.Length > 0) sb.Append(" + ");
                sb.Append(line.UnitsTaken).Append("u ")
                  .Append(CropVarietyCatalog.DisplayNameOf(line.VarietyId))
                  .Append(" seed: ").Append(line.ProvenanceRendered);
            }
            return sb.ToString();
        }

        /// <summary>Distinct variety ids sown, in draw order.</summary>
        public string JoinedVarietyIds()
        {
            var seen = new List<string>();
            foreach (var line in Lines)
            {
                if (line == null || string.IsNullOrWhiteSpace(line.VarietyId)) continue;
                if (!seen.Contains(line.VarietyId)) seen.Add(line.VarietyId);
            }
            return string.Join("+", seen.ToArray());
        }
    }

    /// <summary>
    /// W5C: a farm's seed inventory — the missing piece below the store. Seed
    /// arrives only three honest ways: bought from a seed merchant
    /// (<see cref="PurchaseSeed"/>), held back from the farm's own harvest
    /// (<see cref="HoldBackOwnSeed"/>, the historical norm per T1E), or an
    /// explicit one-time bootstrap endowment (flagged, never a supplier).
    ///
    /// Consumption is FIFO (oldest seed sown first) and refuses LOUDLY on
    /// shortfall: farms never plant conjured seed. <see cref="PlantFieldFromStore"/>
    /// is the honest link to the CRP planting flow — it draws real seed from
    /// this store and hands the provenance to <see cref="CropChain.PlantField"/>,
    /// without re-litigating planting's own rules (window, field state), which
    /// remain the CropChain's authority.
    /// </summary>
    [Serializable]
    public sealed class FarmSeedStore
    {
        public string FarmId = string.Empty;

        private readonly List<SeedLot> lots = new List<SeedLot>();

        public FarmSeedStore() { }

        public FarmSeedStore(string farmId)
        {
            FarmId = farmId ?? string.Empty;
        }

        public IReadOnlyList<SeedLot> Lots => lots;

        /// <summary>
        /// Receives a seed lot into the farm's store. Lots must carry variety
        /// (or the unknown-variety sentinel) and provenance, or be an explicit
        /// bootstrap endowment. Returns a rejection string, or null on success.
        /// </summary>
        public string ReceiveSeedLot(SeedLot lot, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (lot == null) return "FarmSeedStore: null lot refused — no seed without a lot record.";
            if (lot.Units <= 0)
                return $"FarmSeedStore: lot refused — {lot.Crop} seed needs a positive unit count.";
            if (string.IsNullOrWhiteSpace(lot.VarietyId))
                return $"FarmSeedStore: lot refused — {lot.Crop} seed must name its variety (or '{CropVarietyCatalog.UnknownVarietyId}').";
            bool hasProvenance = (lot.Provenance != null && lot.Provenance.HasHops)
                || !string.IsNullOrWhiteSpace(lot.SourceDescription);
            if (!hasProvenance && !lot.IsBootstrapEndowment)
                return $"FarmSeedStore: lot refused — {lot.Crop} seed names no provenance and is not a flagged bootstrap endowment. No orphan seed.";
            if (lot.LotId.IsValid)
            {
                foreach (var existing in lots)
                {
                    if (existing != null && existing.LotId.Equals(lot.LotId))
                        return $"FarmSeedStore: lot refused — lot {lot.LotId} is already in the store.";
                }
            }

            lots.Add(lot);
            diagnostics.Add($"FarmSeedStore: farm '{FarmId}' received {lot.Units}u {lot.Crop} seed ({CropVarietyCatalog.DisplayNameOf(lot.VarietyId)}).");
            return null;
        }

        public int SeedUnitsOnHand(CropKind crop, string varietyId)
        {
            int total = 0;
            foreach (var lot in lots)
            {
                if (lot == null || lot.Crop != crop || lot.Units <= 0) continue;
                if (!string.IsNullOrWhiteSpace(varietyId)
                    && !string.Equals(lot.VarietyId, varietyId, StringComparison.OrdinalIgnoreCase))
                    continue;
                total += lot.Units;
            }
            return total;
        }

        /// <summary>
        /// D2G: the farm's honest record of what came up. Stamps the observed
        /// germination rate on a lot the farm holds, for the incident book
        /// (<see cref="SeedQualityIncidentBook"/>) to file against the
        /// merchant's declaration. A report needs a real lot and a real
        /// number 0–100; anything else is refused loudly. Returns null on
        /// success, or the refusal string.
        /// </summary>
        public string ReportSeedQuality(
            EntityId lotId,
            int reportedGerminationRatePct,
            string note,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (reportedGerminationRatePct < 0 || reportedGerminationRatePct > 100)
            {
                return $"FarmSeedStore: farm '{FarmId}' reports no germination number {reportedGerminationRatePct} — a quality report needs an observed rate 0–100.";
            }
            foreach (var lot in lots)
            {
                if (lot == null || !lot.LotId.Equals(lotId)) continue;
                lot.ReportedGerminationRatePct = reportedGerminationRatePct;
                diagnostics.Add(
                    $"FarmSeedStore: farm '{FarmId}' reports lot {lotId} ({CropVarietyCatalog.DisplayNameOf(lot.VarietyId)} {lot.Crop}) " +
                    $"at {reportedGerminationRatePct}% observed germination" +
                    (string.IsNullOrWhiteSpace(note) ? "." : $": {note}"));
                return null;
            }
            return $"FarmSeedStore: farm '{FarmId}' holds no lot {lotId} — reports attach to real lots only.";
        }

        /// <summary>
        /// Buys seed from a seed merchant. The merchant's lot-level provenance
        /// (breeder/grower → merchant) travels into the farm's store. Returns
        /// the purchase record (caller settles the price), or null with a LOUD
        /// diagnostic when the merchant cannot fill the order.
        /// </summary>
        public SeedPurchase PurchaseSeed(
            SeedMerchant merchant,
            CropKind crop,
            string varietyId,
            int units,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (merchant == null)
            {
                diagnostics.Add("FarmSeedStore: no merchant — seed is never conjured.");
                return null;
            }
            if (units <= 0)
            {
                diagnostics.Add("FarmSeedStore: purchase needs a positive unit count.");
                return null;
            }

            List<SeedLot> bought = merchant.SellSeedLots(crop, varietyId, units, dayIndex, idRegistry, diagnostics);
            if (bought == null)
            {
                diagnostics.Add($"FarmSeedStore: farm '{FarmId}' could not buy {units}u {crop} seed — planting waits for real seed.");
                return null;
            }

            var purchase = new SeedPurchase
            {
                MerchantBusinessId = merchant.SupplierBusinessId,
                MerchantName = merchant.SupplierName,
                Crop = crop,
                VarietyId = varietyId ?? string.Empty,
                Units = units,
                PricePerUnitCents = merchant.PricePerUnitCents(crop),
                DayIndex = dayIndex,
            };
            purchase.TotalCents = purchase.Units * purchase.PricePerUnitCents;

            foreach (var lot in bought)
            {
                string rejection = ReceiveSeedLot(lot, diagnostics);
                if (rejection != null)
                {
                    diagnostics.Add($"FarmSeedStore: bought lot rejected ({rejection}) — merchant sale is unwound in the diagnostics, lots returned.");
                    return null;
                }
                purchase.LotIds.Add(lot.LotId.ToString());
            }

            diagnostics.Add(
                $"FarmSeedStore: farm '{FarmId}' bought {units}u {crop} seed from {merchant.SupplierName} " +
                $"for {purchase.TotalCents}c — settle through the farm's ledger.");
            return purchase;
        }

        /// <summary>
        /// Holds back seed from the farm's own harvest (the historical norm,
        /// T1E's SeedSaving). The provenance chain is built honestly: the
        /// variety's originator as the breeder hop when known, this farm as the
        /// grower hop. Returns the seed lot now in the store, or null with a
        /// diagnostic when the harvest cannot cover it.
        /// </summary>
        public SeedLot HoldBackOwnSeed(
            EntityIdRegistry idRegistry,
            CropLot harvestLot,
            string varietyId,
            int units,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            SeedLot seed = SeedSaving.HoldBackSeed(idRegistry, harvestLot, units, dayIndex, diagnostics);
            if (seed == null) return null;

            seed.VarietyId = string.IsNullOrWhiteSpace(varietyId)
                ? CropVarietyCatalog.UnknownVarietyId
                : varietyId;

            var chain = new SeedProvenanceChain();
            CropVariety variety = CropVarietyCatalog.Get(seed.VarietyId);
            if (variety != null && !string.IsNullOrWhiteSpace(variety.OriginatorName))
            {
                // The originator's day is unrecorded history; the variety's OriginNote carries the year.
                chain.AppendHop(variety.OriginatorKind, variety.OriginatorName, string.Empty, -1);
            }
            chain.AppendHop(SeedProvenanceRoles.Grower, $"farm {FarmId} (own harvest)", "farm:" + FarmId, dayIndex);
            seed.Provenance = chain;
            seed.SourceDescription = seed.RenderSource();

            string rejection = ReceiveSeedLot(seed, diagnostics);
            if (rejection != null)
            {
                diagnostics.Add($"FarmSeedStore: held-back seed rejected ({rejection}).");
                return null;
            }
            return seed;
        }

        private List<SeedLot> OrderedLots(CropKind crop, string varietyId)
        {
            var ordered = new List<SeedLot>();
            foreach (var lot in lots)
            {
                if (lot == null || lot.Crop != crop || lot.Units <= 0) continue;
                if (!string.IsNullOrWhiteSpace(varietyId)
                    && !string.Equals(lot.VarietyId, varietyId, StringComparison.OrdinalIgnoreCase))
                    continue;
                ordered.Add(lot);
            }
            // FIFO: oldest acquisition first, then lot id for stability.
            ordered.Sort((a, b) =>
            {
                int c = a.AcquiredDayIndex.CompareTo(b.AcquiredDayIndex);
                return c != 0 ? c : string.Compare(a.LotId.ToString(), b.LotId.ToString(), StringComparison.Ordinal);
            });
            return ordered;
        }

        /// <summary>
        /// Peeks the lots that would be consumed, FIFO, without touching the
        /// store. Returns null with a LOUD diagnostic on shortfall — the
        /// refusal happens before anything is consumed.
        /// </summary>
        public SeedConsumption PeekConsumption(CropKind crop, string varietyId, int units, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (units <= 0)
            {
                diagnostics.Add("FarmSeedStore: consumption needs a positive unit count.");
                return null;
            }

            int available = SeedUnitsOnHand(crop, varietyId);
            string varietyNote = string.IsNullOrWhiteSpace(varietyId)
                ? string.Empty
                : $" of variety '{CropVarietyCatalog.DisplayNameOf(varietyId)}'";
            if (available < units)
            {
                diagnostics.Add(
                    $"FarmSeedStore: farm '{FarmId}' holds {available}u {crop} seed{varietyNote}, " +
                    $"needs {units}u — SHORTFALL. No seed is conjured; the field stays unplanted.");
                return null;
            }

            var consumption = new SeedConsumption { Crop = crop, Units = units };
            int remaining = units;
            foreach (var lot in OrderedLots(crop, varietyId))
            {
                if (remaining <= 0) break;
                int take = Math.Min(remaining, lot.Units);
                remaining -= take;
                consumption.Lines.Add(new SeedConsumptionLine
                {
                    LotId = lot.LotId,
                    VarietyId = lot.VarietyId,
                    UnitsTaken = take,
                    ProvenanceRendered = lot.RenderSource(),
                });
            }
            return consumption;
        }

        /// <summary>Commits a peeked consumption to the store (drops emptied lots).</summary>
        public void CommitConsumption(SeedConsumption consumption)
        {
            if (consumption == null || consumption.Lines == null) return;
            foreach (var line in consumption.Lines)
            {
                if (line == null) continue;
                for (int i = 0; i < lots.Count; i++)
                {
                    var lot = lots[i];
                    if (lot != null && lot.LotId.Equals(line.LotId))
                    {
                        lot.Units -= line.UnitsTaken;
                        break;
                    }
                }
            }
            for (int i = lots.Count - 1; i >= 0; i--)
            {
                if (lots[i] == null || lots[i].Units <= 0) lots.RemoveAt(i);
            }
        }

        /// <summary>
        /// Consumes seed for planting in one step: FIFO, loud refusal on
        /// shortfall (nothing consumed). Returns the consumption, or null.
        /// </summary>
        public SeedConsumption TryConsumeForPlanting(CropKind crop, string varietyId, int units,
            int dayIndex, List<string> diagnostics)
        {
            SeedConsumption consumption = PeekConsumption(crop, varietyId, units, diagnostics);
            if (consumption == null) return null;
            CommitConsumption(consumption);
            return consumption;
        }

        /// <summary>
        /// W5C: the honest link from the farm's seed store to CRP planting.
        /// Computes the seed need from the field's acres and the crop
        /// calibration (SeedUnitsPerAcre), draws it FIFO from this store, and
        /// hands the real provenance to <see cref="CropChain.PlantField"/>.
        ///
        /// Routing note (the 148 ambiguous behaviors are NOT re-litigated):
        /// the planting window, field-state transitions, and seed-source rules
        /// inside PlantField remain its authority. This method only guarantees
        /// the seed is real: on shortfall the field is never planted and the
        /// store is untouched; if PlantField refuses (e.g. wrong season), the
        /// peeked seed is NOT consumed.
        ///
        /// Returns null on success, or the refusal string.
        /// </summary>
        public string PlantFieldFromStore(
            CropChain cropChain,
            string fieldId,
            CropKind crop,
            string varietyId,
            EntityId workerId,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (cropChain == null) return "FarmSeedStore.PlantFieldFromStore: no crop chain.";
            CropFieldState field = cropChain.Fields.GetField(fieldId);
            if (field == null) return $"FarmSeedStore.PlantFieldFromStore: unknown field '{fieldId}'.";
            CropCalibration calibration = cropChain.Fields.GetCalibration(crop);
            if (calibration == null)
                return $"FarmSeedStore.PlantFieldFromStore: no calibration for {crop} — seed need cannot be computed.";

            int required = Mathf.CeilToInt(field.Acres * calibration.SeedUnitsPerAcre);
            if (required <= 0)
                return $"FarmSeedStore.PlantFieldFromStore: field '{fieldId}' needs no seed (acres {field.Acres}) — nothing planted.";

            SeedConsumption consumption = PeekConsumption(crop, varietyId, required, diagnostics);
            if (consumption == null)
            {
                return $"FarmSeedStore.PlantFieldFromStore: field '{fieldId}' not planted — the store cannot cover {required}u {crop} seed.";
            }

            string problem = cropChain.PlantField(
                fieldId, crop, required, consumption.RenderSource(), workerId, dayIndex, diagnostics);
            if (problem != null) return problem; // seed not consumed; store untouched

            CommitConsumption(consumption);
            field.SeedVarietyId = consumption.JoinedVarietyIds();
            diagnostics.Add(
                $"FarmSeedStore: farm '{FarmId}' planted {crop} on field '{fieldId}' with {required}u real seed " +
                $"({field.SeedVarietyId}).");
            return null;
        }

        /// <summary>Save support (CLN-1 pattern).</summary>
        public FarmSeedStoreSaveDto CaptureSaveDto()
        {
            return new FarmSeedStoreSaveDto
            {
                farmId = FarmId,
                lots = new List<SeedLot>(lots),
            };
        }

        /// <summary>Save support (CLN-1 pattern).</summary>
        public void LoadFromSaveDto(FarmSeedStoreSaveDto dto)
        {
            lots.Clear();
            if (dto == null) return;
            FarmId = dto.farmId ?? string.Empty;
            if (dto.lots != null)
            {
                foreach (var lot in dto.lots)
                {
                    if (lot != null) lots.Add(lot);
                }
            }
        }
    }

    /// <summary>Save DTO for the farm seed store (CLN-1 pattern).</summary>
    [Serializable]
    public sealed class FarmSeedStoreSaveDto
    {
        public string farmId = string.Empty;
        public List<SeedLot> lots = new List<SeedLot>();
    }
}
