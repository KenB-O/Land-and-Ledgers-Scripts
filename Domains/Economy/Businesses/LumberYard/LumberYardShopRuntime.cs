using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.LumberYard
{
    /// <summary>
    /// W4B: the per-instance lumber yard runtime — the detailed layer behind a
    /// LumberYard business instance (BusinessType.LumberYard = 7). One instance
    /// per business. Canon §8.2: "Lumber Yard is the town-facing storage and
    /// retail arm, either independent or paired with an owned mill."
    ///
    /// What this owns (the detailed layer):
    /// - lumber inventory with FIFO, grades (yard-assigned sorts) and species;
    ///   lots received from named sawmills (ANY named mill — the yard is
    ///   trade, not production), named purchases, named import orders, or the
    ///   one-time opening endowment. Anonymous lots are refused loudly on
    ///   every path;
    /// - nails/simple-hardware inventory (the Canon "nails and hardware" cost
    ///   bucket) with lots tracing to a named forging blacksmith, a named
    ///   import order, or the one-time opening endowment;
    /// - construction-material sales in the Canon cost buckets (Canon §5.2:
    ///   lumber, nails and hardware): real lots are withdrawn FIFO, and a
    ///   shortfall is refused LOUDLY with nothing consumed — empty shelves
    ///   stay empty;
    /// - town retail sales (wheelwright repair inputs, household lumber) on
    ///   the same real-lot rails;
    /// - a sale history with per-lot provenance, so every board and nail can
    ///   be traced back to its stand, mill, smith, or import order.
    ///
    /// Money moves only through ledger authorities, never here: sale records
    /// carry the AGREED unit prices and totals for the ledger to post. This
    /// sits ALONGSIDE the existing construction quote/consumption path
    /// (AcquisitionMarketManager + ConstructionSupportNodeState) — no
    /// rewrite, no rename, no renumber; the yard is a new honest supplier the
    /// construction system can draw on.
    /// </summary>
    public sealed class LumberYardShopRuntime
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly string businessInstanceId;
        private readonly EntityIdRegistry idRegistry;
        private readonly LumberYardLumberStock lumberStock;
        private readonly LumberYardHardwareStock hardwareStock;
        private readonly List<LumberYardSaleRecord> salesHistory = new List<LumberYardSaleRecord>();
        private bool openingStockApplied;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public string BusinessInstanceId => businessInstanceId;
        public EntityIdRegistry IdRegistry => idRegistry;
        public LumberYardLumberStock LumberStock => lumberStock;
        public LumberYardHardwareStock HardwareStock => hardwareStock;
        public IReadOnlyList<LumberYardSaleRecord> SalesHistory => salesHistory;
        public bool OpeningStockApplied => openingStockApplied;

        public LumberYardShopRuntime(string businessInstanceId, EntityIdRegistry idRegistry)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
            this.idRegistry = idRegistry;
            this.lumberStock = new LumberYardLumberStock(this.businessInstanceId);
            this.hardwareStock = new LumberYardHardwareStock(this.businessInstanceId);
        }

        private EntityId AllocateYardLotId(List<string> diag)
        {
            if (idRegistry == null)
            {
                diag.Add("LumberYardShopRuntime: no EntityIdRegistry — intake refused.");
                return EntityId.Invalid;
            }
            return idRegistry.Allocate(EntityKind.Lot);
        }

        // ---- Lumber intake ----

        /// <summary>W4B: lumber in from a named sawmill (any named mill — Canon §8.2 independent yard).</summary>
        public string ReceiveLumberFromSawmill(
            SawmillLumberLot millLot, string yardGradeId, int acquiredDayIndex, int unitCostCents, List<string> diag)
        {
            diag = diag ?? diagnostics;
            EntityId yardLotId = AllocateYardLotId(diag);
            if (yardLotId == EntityId.Invalid) return "LumberYardShopRuntime: no lot id available.";
            return lumberStock.ReceiveFromSawmill(millLot, yardGradeId, acquiredDayIndex, unitCostCents, yardLotId, diag);
        }

        /// <summary>W4B: lumber bought from a named seller (another yard, regional freight).</summary>
        public string ReceiveLumberPurchase(
            int units, string species, string yardGradeId, string sellerBusinessId, string sellerProvenanceNote,
            int acquiredDayIndex, int unitCostCents, List<string> diag)
        {
            diag = diag ?? diagnostics;
            EntityId yardLotId = AllocateYardLotId(diag);
            if (yardLotId == EntityId.Invalid) return "LumberYardShopRuntime: no lot id available.";
            return lumberStock.ReceivePurchaseLot(yardLotId, units, species, yardGradeId, sellerBusinessId,
                sellerProvenanceNote, acquiredDayIndex, unitCostCents, diag);
        }

        /// <summary>W4B: lumber arriving on a named import order (ImportCatalog "lumber").</summary>
        public string ReceiveLumberImportArrival(
            int units, string species, string yardGradeId, string importOrderId, string originName,
            int acquiredDayIndex, int unitCostCents, List<string> diag)
        {
            diag = diag ?? diagnostics;
            EntityId yardLotId = AllocateYardLotId(diag);
            if (yardLotId == EntityId.Invalid) return "LumberYardShopRuntime: no lot id available.";
            return lumberStock.ReceiveImportArrival(yardLotId, units, species, yardGradeId, importOrderId,
                originName, acquiredDayIndex, unitCostCents, diag);
        }

        // ---- Hardware intake ----

        /// <summary>W4B: nails/simple hardware forged by a named blacksmith — the primary honest supply path.</summary>
        public string ReceiveHardwareFromBlacksmith(
            LumberYardHardwareKind kind, int units, string blacksmithBusinessId, int forgedDayIndex,
            string ironImportOrderId, int acquiredDayIndex, int unitCostCents, List<string> diag)
        {
            diag = diag ?? diagnostics;
            EntityId yardLotId = AllocateYardLotId(diag);
            if (yardLotId == EntityId.Invalid) return "LumberYardShopRuntime: no lot id available.";
            return hardwareStock.ReceiveFromBlacksmith(yardLotId, kind, units, blacksmithBusinessId, forgedDayIndex,
                ironImportOrderId, acquiredDayIndex, unitCostCents, diag);
        }

        /// <summary>W4B: hardware arriving on a named import order (LumberYardSupply.HardwareImportMaterialId).</summary>
        public string ReceiveHardwareImportArrival(
            LumberYardHardwareKind kind, int units, string importOrderId, string originName,
            int acquiredDayIndex, int unitCostCents, List<string> diag)
        {
            diag = diag ?? diagnostics;
            EntityId yardLotId = AllocateYardLotId(diag);
            if (yardLotId == EntityId.Invalid) return "LumberYardShopRuntime: no lot id available.";
            return hardwareStock.ReceiveImportArrival(yardLotId, kind, units, importOrderId, originName,
                acquiredDayIndex, unitCostCents, diag);
        }

        /// <summary>W4B: marks the one-time opening endowment as applied. Only
        /// LumberYardOpeningStock calls this, after its lots land.</summary>
        public void MarkOpeningStockApplied()
        {
            openingStockApplied = true;
        }

        // ---- Sales ----

        /// <summary>
        /// W4B: sells construction materials to a project in the Canon cost
        /// buckets (lumber; nails and hardware). ATOMIC: both buckets are
        /// checked first — on any shortfall the sale is refused LOUDLY and
        /// nothing is consumed. On success, real lots are withdrawn FIFO and
        /// the sale record carries every lot's provenance. Returns null on
        /// refusal. Money moves through ledger authorities; the record carries
        /// the agreed prices for posting.
        /// </summary>
        public LumberYardSaleRecord TrySellToConstructionProject(
            string projectId,
            int lumberUnits, int lumberUnitPriceCents,
            int nailsUnits, int nailsUnitPriceCents,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (idRegistry == null)
            {
                diag.Add("LumberYardShopRuntime: no EntityIdRegistry — sale refused.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(projectId))
            {
                diag.Add("LumberYardShopRuntime: construction sales name their project — anonymous sales refused.");
                return null;
            }
            lumberUnits = Math.Max(0, lumberUnits);
            nailsUnits = Math.Max(0, nailsUnits);
            if (lumberUnits == 0 && nailsUnits == 0)
            {
                diag.Add("LumberYardShopRuntime: nothing requested — no sale recorded.");
                return null;
            }

            int lumberAvailable = lumberStock.AvailableUnits(null, null);
            int nailsAvailable = hardwareStock.TotalHardwareUnits;
            if (lumberAvailable < lumberUnits || nailsAvailable < nailsUnits)
            {
                diag.Add($"LumberYardShopRuntime ({businessInstanceId}): SALE REFUSED to project '{projectId}' — "
                    + $"shortfall (need {lumberUnits} lumber / have {lumberAvailable}; "
                    + $"need {nailsUnits} nails-hardware / have {nailsAvailable}). "
                    + "Empty shelves stay empty; nothing invented, nothing consumed.");
                return null;
            }

            var record = new LumberYardSaleRecord
            {
                SaleId = idRegistry.Allocate(EntityKind.Contract),
                BuyerLabel = projectId.Trim(),
                IsConstructionSale = true,
                DayIndex = dayIndex,
            };

            if (lumberUnits > 0)
            {
                var lines = lumberStock.TryWithdrawUnits(lumberUnits, null, null, diag);
                int taken = 0;
                foreach (var line in lines) taken += line.UnitsTaken;
                record.Lines.Add(new LumberYardMaterialSaleLine
                {
                    ResourceKind = ConstructionResourceKind.Lumber,
                    UnitsSold = taken,
                    UnitPriceCents = Math.Max(0, lumberUnitPriceCents),
                    LumberLines = lines,
                });
            }

            if (nailsUnits > 0)
            {
                var lines = hardwareStock.TryWithdrawUnits(nailsUnits, null, diag);
                int taken = 0;
                foreach (var line in lines) taken += line.UnitsTaken;
                record.Lines.Add(new LumberYardMaterialSaleLine
                {
                    ResourceKind = ConstructionResourceKind.Nails,
                    UnitsSold = taken,
                    UnitPriceCents = Math.Max(0, nailsUnitPriceCents),
                    HardwareLines = lines,
                });
            }

            salesHistory.Add(record);
            diag.Add($"LumberYardShopRuntime ({businessInstanceId}): sold to project '{projectId}' — "
                + $"{record.UnitsSoldFor(ConstructionResourceKind.Lumber)} lumber, "
                + $"{record.UnitsSoldFor(ConstructionResourceKind.Nails)} nails/hardware, "
                + $"total {record.TotalCents}¢ (sale {record.SaleId}). Provenance on every lot.");
            return record;
        }

        /// <summary>
        /// W4B: town retail — lumber to the wheelwright, households, and other
        /// local buyers (the recurring-order paths lumber_yard_lumber_to_*).
        /// Same real-lot rails and loud refusal as construction sales.
        /// Returns null on refusal.
        /// </summary>
        public LumberYardSaleRecord TrySellLumberRetail(
            string buyerLabel,
            int units,
            string speciesFilter,
            string gradeFilter,
            int unitPriceCents,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (idRegistry == null)
            {
                diag.Add("LumberYardShopRuntime: no EntityIdRegistry — sale refused.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(buyerLabel))
            {
                diag.Add("LumberYardShopRuntime: retail sales name their buyer — anonymous sales refused.");
                return null;
            }
            units = Math.Max(0, units);
            if (units == 0)
            {
                diag.Add("LumberYardShopRuntime: nothing requested — no sale recorded.");
                return null;
            }

            int available = lumberStock.AvailableUnits(speciesFilter, gradeFilter);
            if (available < units)
            {
                diag.Add($"LumberYardShopRuntime ({businessInstanceId}): RETAIL SALE REFUSED to '{buyerLabel}' — "
                    + $"shortfall (need {units} lumber / have {available}"
                    + (string.IsNullOrWhiteSpace(speciesFilter) && string.IsNullOrWhiteSpace(gradeFilter)
                        ? string.Empty
                        : $" under filter species '{speciesFilter}' grade '{gradeFilter}'")
                    + "). Empty shelves stay empty; nothing invented, nothing consumed.");
                return null;
            }

            var lines = lumberStock.TryWithdrawUnits(units, speciesFilter, gradeFilter, diag);
            int taken = 0;
            foreach (var line in lines) taken += line.UnitsTaken;

            var record = new LumberYardSaleRecord
            {
                SaleId = idRegistry.Allocate(EntityKind.Contract),
                BuyerLabel = buyerLabel.Trim(),
                IsConstructionSale = false,
                DayIndex = dayIndex,
            };
            record.Lines.Add(new LumberYardMaterialSaleLine
            {
                ResourceKind = ConstructionResourceKind.Lumber,
                UnitsSold = taken,
                UnitPriceCents = Math.Max(0, unitPriceCents),
                LumberLines = lines,
            });

            salesHistory.Add(record);
            diag.Add($"LumberYardShopRuntime ({businessInstanceId}): retail sale to '{buyerLabel}' — "
                + $"{taken} lumber, total {record.TotalCents}¢ (sale {record.SaleId}).");
            return record;
        }

        /// <summary>W4B save contract: lives inside the owning runtime class.</summary>
        [Serializable]
        public sealed class LumberYardShopRuntimeSaveDto
        {
            public string BusinessInstanceId = string.Empty;
            public bool OpeningStockApplied;
            public LumberYardLumberStock.LumberYardLumberStockSaveDto LumberStock =
                new LumberYardLumberStock.LumberYardLumberStockSaveDto();
            public LumberYardHardwareStock.LumberYardHardwareStockSaveDto HardwareStock =
                new LumberYardHardwareStock.LumberYardHardwareStockSaveDto();
            public List<LumberYardSaleRecord> SalesHistory = new List<LumberYardSaleRecord>();
        }

        public LumberYardShopRuntimeSaveDto CaptureSaveDto()
        {
            var dto = new LumberYardShopRuntimeSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                OpeningStockApplied = openingStockApplied,
                LumberStock = lumberStock.CaptureSaveDto(),
                HardwareStock = hardwareStock.CaptureSaveDto(),
            };
            foreach (var sale in salesHistory)
            {
                if (sale == null) continue;
                dto.SalesHistory.Add(CloneSaleRecord(sale));
            }
            return dto;
        }

        public void LoadFromSaveDto(LumberYardShopRuntimeSaveDto dto)
        {
            lumberStock.LoadFromSaveDto(dto != null ? dto.LumberStock : null);
            hardwareStock.LoadFromSaveDto(dto != null ? dto.HardwareStock : null);
            salesHistory.Clear();
            openingStockApplied = dto != null && dto.OpeningStockApplied;
            if (dto != null && dto.SalesHistory != null)
            {
                foreach (var sale in dto.SalesHistory)
                {
                    if (sale == null) continue;
                    salesHistory.Add(CloneSaleRecord(sale));
                }
            }
        }

        private static LumberYardSaleRecord CloneSaleRecord(LumberYardSaleRecord sale)
        {
            var clone = new LumberYardSaleRecord
            {
                SaleId = sale.SaleId,
                BuyerLabel = sale.BuyerLabel,
                IsConstructionSale = sale.IsConstructionSale,
                DayIndex = sale.DayIndex,
            };
            foreach (var line in sale.Lines)
            {
                if (line == null) continue;
                var lineClone = new LumberYardMaterialSaleLine
                {
                    ResourceKind = line.ResourceKind,
                    UnitsSold = line.UnitsSold,
                    UnitPriceCents = line.UnitPriceCents,
                };
                foreach (var l in line.LumberLines)
                {
                    if (l == null) continue;
                    lineClone.LumberLines.Add(new LumberYardLumberDispenseLine
                    {
                        LotId = l.LotId,
                        UnitsTaken = l.UnitsTaken,
                        YardGradeId = l.YardGradeId,
                        Species = l.Species,
                        ProvenanceChain = l.ProvenanceChain,
                    });
                }
                foreach (var h in line.HardwareLines)
                {
                    if (h == null) continue;
                    lineClone.HardwareLines.Add(new LumberYardHardwareDispenseLine
                    {
                        LotId = h.LotId,
                        UnitsTaken = h.UnitsTaken,
                        HardwareKind = h.HardwareKind,
                        ProvenanceChain = h.ProvenanceChain,
                    });
                }
                clone.Lines.Add(lineClone);
            }
            return clone;
        }
    }
}
