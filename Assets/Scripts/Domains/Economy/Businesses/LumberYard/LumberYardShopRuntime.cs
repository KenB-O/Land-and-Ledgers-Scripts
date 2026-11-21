using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Equipment;
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
    ///
    /// D2C depth on top of the W4B width:
    /// - project stockpiles (Canon §6.4): lumber earmarked for a named
    ///   project leaves ordinary sale inventory until deliberately released;
    ///   construction sales draw the project's earmark first;
    /// - delivery obligations (Canon §6.5, Part VI §6.2): named destinations,
    ///   named carriers, policy-based delivery charges carried for the ledger;
    ///   freight execution stays with LogisticsRuntimeManager;
    /// - contractor credit (Canon Part VI §6.4, §7.3): named contractor
    ///   accounts with credit postures and per-account settlement terms,
    ///   receivables invoicing, payments that only shrink balances, and
    ///   bad-debt write-off that erases nothing but collectability;
    /// - landed-cost pricing (Canon §5.1, §5.3): freight/handling charges
    ///   recorded on lots, grade-policy quotes derived from real lots;
    /// - cull disposal (Canon §4.5): audited burn/discard/give-away of
    ///   cull-grade stock only, always recorded.
    /// </summary>
    public sealed class LumberYardShopRuntime
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly string businessInstanceId;
        private readonly EntityIdRegistry idRegistry;
        private readonly LumberYardLumberStock lumberStock;
        private readonly LumberYardHardwareStock hardwareStock;
        private readonly LumberYardGlassStock glassStock;
        private readonly LumberYardProjectStockpile stockpile;
        private readonly LumberYardDeliveryRegister deliveryRegister;
        private readonly LumberYardContractorLedger contractorLedger;
        private readonly LumberYardCullDisposalRegister cullDisposal;
        private readonly List<LumberYardSaleRecord> salesHistory = new List<LumberYardSaleRecord>();
        private bool openingStockApplied;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public string BusinessInstanceId => businessInstanceId;
        public EntityIdRegistry IdRegistry => idRegistry;
        public LumberYardLumberStock LumberStock => lumberStock;
        public LumberYardHardwareStock HardwareStock => hardwareStock;
        public LumberYardGlassStock GlassStock => glassStock;
        public LumberYardProjectStockpile Stockpile => stockpile;
        public LumberYardDeliveryRegister DeliveryRegister => deliveryRegister;
        public LumberYardContractorLedger ContractorLedger => contractorLedger;
        public LumberYardCullDisposalRegister CullDisposal => cullDisposal;
        public IReadOnlyList<LumberYardSaleRecord> SalesHistory => salesHistory;

        /// <summary>
        /// Lane 9e: the unified reservation authority for the quote/sale
        /// split. Null = unwired: quotes hold nothing and sales draw live
        /// stock (today's behavior). Bound by the hub (LumberYardRuntimeFor).
        /// </summary>
        public IUnifiedReservationAuthority ReservationAuthority { get; set; }
        public bool OpeningStockApplied => openingStockApplied;

        public LumberYardShopRuntime(string businessInstanceId, EntityIdRegistry idRegistry)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
            this.idRegistry = idRegistry;
            this.lumberStock = new LumberYardLumberStock(this.businessInstanceId);
            this.hardwareStock = new LumberYardHardwareStock(this.businessInstanceId);
            this.glassStock = new LumberYardGlassStock(this.businessInstanceId);
            this.stockpile = new LumberYardProjectStockpile(this.businessInstanceId);
            this.deliveryRegister = new LumberYardDeliveryRegister(this.businessInstanceId);
            this.contractorLedger = new LumberYardContractorLedger(this.businessInstanceId);
            this.cullDisposal = new LumberYardCullDisposalRegister(this.businessInstanceId);
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

        /// <summary>W4B: nails/simple hardware forged by a named blacksmith — the primary honest supply path.
        /// The optional supplier note carries the delivery's piece breakdown (D3: the delivery driver
        /// records which pieces make up the lot).</summary>
        public string ReceiveHardwareFromBlacksmith(
            LumberYardHardwareKind kind, int units, string blacksmithBusinessId, int forgedDayIndex,
            string ironImportOrderId, int acquiredDayIndex, int unitCostCents, List<string> diag,
            string supplierNote = "")
        {
            diag = diag ?? diagnostics;
            EntityId yardLotId = AllocateYardLotId(diag);
            if (yardLotId == EntityId.Invalid) return "LumberYardShopRuntime: no lot id available.";
            return hardwareStock.ReceiveFromBlacksmith(yardLotId, kind, units, blacksmithBusinessId, forgedDayIndex,
                ironImportOrderId, acquiredDayIndex, unitCostCents, diag, supplierNote);
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

        /// <summary>
        /// F1: window-glass panes arriving on a named import order
        /// (LumberYardSupply.GlassImportMaterialId). The ONLY glass
        /// intake — no blacksmith path (glass is not forged) and no
        /// bootstrap endowment (glass never appears without a paid
        /// import order behind it).
        /// </summary>
        public string ReceiveGlassImportArrival(
            int panes, string importOrderId, string originName,
            int acquiredDayIndex, int unitCostCents, List<string> diag)
        {
            diag = diag ?? diagnostics;
            EntityId yardLotId = AllocateYardLotId(diag);
            if (yardLotId == EntityId.Invalid) return "LumberYardShopRuntime: no lot id available.";
            return glassStock.ReceiveImportArrival(yardLotId, panes, importOrderId, originName,
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
        ///
        /// D2C: stockpile-aware (Canon §6.4) — the named project's earmarked
        /// lumber is available to THIS project and drawn first; ordinary sale
        /// inventory covers the remainder. Earmarked stock is invisible to
        /// retail and to other projects.
        /// </summary>
        public LumberYardSaleRecord TrySellToConstructionProject(
            string projectId,
            int lumberUnits, int lumberUnitPriceCents,
            int nailsUnits, int nailsUnitPriceCents,
            int dayIndex,
            List<string> diag)
        {
            return ExecuteConstructionSale(projectId, lumberUnits, lumberUnitPriceCents,
                nailsUnits, nailsUnitPriceCents, 0, 0, dayIndex, diag);
        }

        /// <summary>
        /// F1: the three-bucket construction sale (lumber; nails and
        /// hardware; window glass). Same atomic guarantees as the
        /// two-bucket sale: every bucket is checked first, and a
        /// shortfall in ANY bucket refuses the whole sale loudly with
        /// nothing consumed. A glazed package's glass is sold here, from
        /// real glass lots, never from thin air.
        /// </summary>
        public LumberYardSaleRecord TrySellToConstructionProject(
            string projectId,
            int lumberUnits, int lumberUnitPriceCents,
            int nailsUnits, int nailsUnitPriceCents,
            int glassPanes, int glassUnitPriceCents,
            int dayIndex,
            List<string> diag)
        {
            return ExecuteConstructionSale(projectId, lumberUnits, lumberUnitPriceCents,
                nailsUnits, nailsUnitPriceCents, glassPanes, glassUnitPriceCents, dayIndex, diag);
        }

        /// <summary>
        /// D6 (F-D2-3): the kind-level construction sale — same atomicity
        /// and loud refusal as the bucket entry, but the hardware leg is
        /// fulfilled PER KIND: every kind line must be covered in full by
        /// real lots of that kind, or the whole sale is refused and nothing
        /// is consumed. A shortfall in one kind never borrows from another
        /// kind or from the mixed SimpleHardware bucket. The sale record
        /// carries one dispense line per kind lot, so the adapter can
        /// record one sale line per kind sold. The bucket entry above is
        /// kept for direct yard callers (and the on-account path, which
        /// still bucket-fills — follow-up); construction procurement goes
        /// through this entry via the sale adapter.
        /// </summary>
        public LumberYardSaleRecord TrySellToConstructionProject(
            string projectId,
            int lumberUnits, int lumberUnitPriceCents,
            List<LumberYardHardwareKindLine> hardwareKindLines, int hardwareUnitPriceCents,
            int dayIndex,
            List<string> diag)
        {
            return ExecuteConstructionSalePerKind(projectId, lumberUnits, lumberUnitPriceCents,
                hardwareKindLines, hardwareUnitPriceCents, 0, 0, dayIndex, diag);
        }

        /// <summary>
        /// D6+F1 merge: the per-kind construction sale plus window glass.
        /// The adapter sells lumber + hardware kinds + glass in one atomic
        /// sale, so this overload carries the glass panes through to the
        /// per-kind executor. A shortfall in any bucket refuses the whole
        /// sale, the same three-bucket rule as the bucket overload.
        /// </summary>
        public LumberYardSaleRecord TrySellToConstructionProject(
            string projectId,
            int lumberUnits, int lumberUnitPriceCents,
            List<LumberYardHardwareKindLine> hardwareKindLines, int hardwareUnitPriceCents,
            int glassPanes, int glassUnitPriceCents,
            int dayIndex,
            List<string> diag)
        {
            return ExecuteConstructionSalePerKind(projectId, lumberUnits, lumberUnitPriceCents,
                hardwareKindLines, hardwareUnitPriceCents, glassPanes, glassUnitPriceCents, dayIndex, diag);
        }

        /// <summary>
        /// D6: the lumber leg of a construction sale, shared by the bucket
        /// and kind-level entries — the named project's earmarked lumber
        /// first, ordinary sale inventory for the remainder. Returns the
        /// sale line, or null when nothing was requested.
        /// </summary>
        private LumberYardMaterialSaleLine WithdrawLumberSaleLine(
            string projectId, int lumberUnits, int lumberUnitPriceCents, List<string> diag)
        {
            if (lumberUnits <= 0) return null;
            var lines = new List<LumberYardLumberDispenseLine>();
            lines.AddRange(lumberStock.TryWithdrawProjectStock(lumberUnits, projectId, null, null, diag));
            int takenSoFar = 0;
            foreach (var line in lines) takenSoFar += line.UnitsTaken;
            int remainder = lumberUnits - takenSoFar;
            if (remainder > 0)
                lines.AddRange(lumberStock.TryWithdrawUnits(remainder, null, null, diag));
            int taken = 0;
            foreach (var line in lines) taken += line.UnitsTaken;
            return new LumberYardMaterialSaleLine
            {
                ResourceKind = ConstructionResourceKind.Lumber,
                UnitsSold = taken,
                UnitPriceCents = Math.Max(0, lumberUnitPriceCents),
                LumberLines = lines,
            };
        }

        private LumberYardSaleRecord ExecuteConstructionSale(
            string projectId,
            int lumberUnits, int lumberUnitPriceCents,
            int nailsUnits, int nailsUnitPriceCents,
            int glassPanes, int glassUnitPriceCents,
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
            glassPanes = Math.Max(0, glassPanes);
            if (lumberUnits == 0 && nailsUnits == 0 && glassPanes == 0)
            {
                diag.Add("LumberYardShopRuntime: nothing requested — no sale recorded.");
                return null;
            }

            // D2C: the named project's earmarked stock (Canon §6.4) is available
            // to THIS project, drawn first; ordinary sale inventory covers the
            // remainder. Other projects and retail never see earmarked stock.
            int projectReserved = lumberStock.EarmarkedUnitsForProject(projectId, null, null);
            int lumberAvailable = lumberStock.AvailableUnitsForSale(null, null) + projectReserved;
            int nailsAvailable = hardwareStock.TotalHardwareUnits;
            int glassAvailable = glassStock.TotalGlassPanes;
            if (lumberAvailable < lumberUnits || nailsAvailable < nailsUnits || glassAvailable < glassPanes)
            {
                diag.Add($"LumberYardShopRuntime ({businessInstanceId}): SALE REFUSED to project '{projectId}' — "
                    + $"shortfall (need {lumberUnits} lumber / have {lumberAvailable}; "
                    + $"need {nailsUnits} nails-hardware / have {nailsAvailable}; "
                    + $"need {glassPanes} glass panes / have {glassAvailable}). "
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

            var lumberLine = WithdrawLumberSaleLine(projectId, lumberUnits, lumberUnitPriceCents, diag);
            if (lumberLine != null) record.Lines.Add(lumberLine);

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

            if (glassPanes > 0)
            {
                var lines = glassStock.TryWithdrawUnits(glassPanes, diag);
                int taken = 0;
                foreach (var line in lines) taken += line.UnitsTaken;
                record.Lines.Add(new LumberYardMaterialSaleLine
                {
                    ResourceKind = ConstructionResourceKind.Glass,
                    UnitsSold = taken,
                    UnitPriceCents = Math.Max(0, glassUnitPriceCents),
                    GlassLines = lines,
                });
            }

            salesHistory.Add(record);
            diag.Add($"LumberYardShopRuntime ({businessInstanceId}): sold to project '{projectId}' — "
                + $"{record.UnitsSoldFor(ConstructionResourceKind.Lumber)} lumber, "
                + $"{record.UnitsSoldFor(ConstructionResourceKind.Nails)} nails/hardware, "
                + $"{record.UnitsSoldFor(ConstructionResourceKind.Glass)} glass panes, "
                + $"total {record.TotalCents}¢ (sale {record.SaleId}). Provenance on every lot.");
            return record;
        }

        /// <summary>
        /// G1: voids a recorded yard sale in full — every dispensed unit
        /// returns to saleable stock (drained lots recreated with their
        /// provenance intact), the record is marked voided and kept for
        /// audit, and the voided value is returned for the caller to
        /// refund or un-bill. A voided sale never sells, invoices, or
        /// delivers again: voiding twice refuses, and invoicing or
        /// scheduling delivery against a voided sale refuses. A sale with
        /// an open (undelivered) delivery obligation refuses — resolve
        /// the delivery first. All-or-nothing: every line is validated
        /// before any unit moves, so a refused void changes nothing. The
        /// yard itself moves no money here; the caller refunds the buyer
        /// from the returned value. Returns the refusal, or null.
        /// </summary>
        public string VoidSale(string saleId, int dayIndex, List<string> diag, out int voidedCents)
        {
            diag = diag ?? diagnostics;
            voidedCents = 0;
            if (string.IsNullOrWhiteSpace(saleId))
                return "LumberYardShopRuntime: voiding a sale requires its sale id.";
            LumberYardSaleRecord record = null;
            foreach (var candidate in salesHistory)
            {
                if (candidate != null && string.Equals(candidate.SaleId.ToString(), saleId.Trim(), StringComparison.Ordinal))
                { record = candidate; break; }
            }
            if (record == null)
                return $"LumberYardShopRuntime: no recorded sale '{saleId}' — nothing to void.";
            if (record.Voided)
                return $"LumberYardShopRuntime: sale '{saleId}' is already voided (day {record.VoidedDayIndex}) — never voided twice.";
            foreach (var order in deliveryRegister.Orders)
            {
                if (order != null && order.SaleId.Equals(record.SaleId) && order.DeliveredDayIndex < 0)
                    return $"LumberYardShopRuntime: sale '{saleId}' has an open delivery obligation '{order.OrderId}' — resolve the delivery before voiding.";
            }
            // Validate every line before any unit moves.
            foreach (var line in record.Lines)
            {
                if (line == null) continue;
                foreach (var dispense in line.LumberLines)
                {
                    if (dispense == null)
                        return $"LumberYardShopRuntime: sale '{saleId}' has a corrupt lumber line — void refused, nothing restocked.";
                    if (dispense.LotId == EntityId.Invalid || dispense.UnitsTaken <= 0)
                        return $"LumberYardShopRuntime: sale '{saleId}' lumber line for lot {dispense.LotId} is not restockable — void refused, nothing restocked.";
                }
                foreach (var dispense in line.HardwareLines)
                {
                    if (dispense == null)
                        return $"LumberYardShopRuntime: sale '{saleId}' has a corrupt hardware line — void refused, nothing restocked.";
                    if (dispense.LotId == EntityId.Invalid || dispense.UnitsTaken <= 0)
                        return $"LumberYardShopRuntime: sale '{saleId}' hardware line for lot {dispense.LotId} is not restockable — void refused, nothing restocked.";
                }
                foreach (var dispense in line.GlassLines)
                {
                    if (dispense == null)
                        return $"LumberYardShopRuntime: sale '{saleId}' has a corrupt glass line — void refused, nothing restocked.";
                    if (dispense.LotId == EntityId.Invalid || dispense.UnitsTaken <= 0)
                        return $"LumberYardShopRuntime: sale '{saleId}' glass line for lot {dispense.LotId} is not restockable — void refused, nothing restocked.";
                }
            }
            foreach (var line in record.Lines)
            {
                if (line == null) continue;
                foreach (var dispense in line.LumberLines)
                {
                    if (dispense == null) continue;
                    string restockProblem = lumberStock.ReturnVoidedUnits(dispense, dispense.UnitsTaken, diag);
                    if (restockProblem != null)
                        return $"LumberYardShopRuntime: void of sale '{saleId}' failed restocking lumber — {restockProblem}";
                }
                foreach (var dispense in line.HardwareLines)
                {
                    if (dispense == null) continue;
                    string restockProblem = hardwareStock.ReturnVoidedUnits(dispense, dispense.UnitsTaken, diag);
                    if (restockProblem != null)
                        return $"LumberYardShopRuntime: void of sale '{saleId}' failed restocking hardware — {restockProblem}";
                }
                foreach (var dispense in line.GlassLines)
                {
                    if (dispense == null) continue;
                    string restockProblem = glassStock.ReturnVoidedUnits(dispense, dispense.UnitsTaken, diag);
                    if (restockProblem != null)
                        return $"LumberYardShopRuntime: void of sale '{saleId}' failed restocking glass — {restockProblem}";
                }
            }
            voidedCents = record.TotalCents;
            record.Voided = true;
            record.VoidedDayIndex = dayIndex;
            diag.Add($"LumberYardShopRuntime ({businessInstanceId}): sale '{saleId}' VOIDED — "
                + $"{record.UnitsSoldFor(ConstructionResourceKind.Lumber)} lumber, "
                + $"{record.UnitsSoldFor(ConstructionResourceKind.Nails)} nails/hardware, "
                + $"{record.UnitsSoldFor(ConstructionResourceKind.Glass)} glass panes returned to stock, "
                + $"{voidedCents}c to refund or un-bill. The record stays for audit.");
            return null;
        }
        /// D6: the kind-level construction sale behind the per-kind entry.
        /// ATOMIC per kind: every kind line is checked first — on any
        /// kind shortfall the sale is refused LOUDLY and nothing is
        /// consumed. On success, real lots are withdrawn per kind (FIFO
        /// within the kind) and the sale record carries every lot's
        /// provenance with its kind, so the adapter records one sale line
        /// per kind sold. Money moves through ledger authorities; the
        /// record carries the agreed prices for posting.
        /// </summary>
        private LumberYardSaleRecord ExecuteConstructionSalePerKind(
            string projectId,
            int lumberUnits, int lumberUnitPriceCents,
            List<LumberYardHardwareKindLine> hardwareKindLines, int hardwareUnitPriceCents,
            int glassPanes, int glassUnitPriceCents,
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
            glassPanes = Math.Max(0, glassPanes);
            var wanted = new List<LumberYardHardwareKindLine>();
            int hardwareUnits = 0;
            if (hardwareKindLines != null)
            {
                var seenKinds = new HashSet<LumberYardHardwareKind>();
                foreach (LumberYardHardwareKindLine line in hardwareKindLines)
                {
                    if (line == null)
                    {
                        diag.Add($"LumberYardShopRuntime ({businessInstanceId}): kind-level sale to project '{projectId}' refused — a null kind line names no kind.");
                        return null;
                    }
                    if (line.Units <= 0)
                    {
                        diag.Add($"LumberYardShopRuntime ({businessInstanceId}): kind-level sale to project '{projectId}' refused — kind '{line.Kind}' names {line.Units} units; a kind line needs positive units.");
                        return null;
                    }
                    if (line.Kind == LumberYardHardwareKind.SimpleHardware)
                    {
                        diag.Add($"LumberYardShopRuntime ({businessInstanceId}): kind-level sale to project '{projectId}' refused — mixed SimpleHardware is not a fulfillable kind; a kind-level sale names real piece kinds (door hinge, iron latch, door handle) or nails.");
                        return null;
                    }
                    if (!seenKinds.Add(line.Kind))
                    {
                        diag.Add($"LumberYardShopRuntime ({businessInstanceId}): kind-level sale to project '{projectId}' refused — kind '{line.Kind}' is named twice; one line per kind.");
                        return null;
                    }
                    wanted.Add(line);
                    hardwareUnits += line.Units;
                }
            }
            if (lumberUnits == 0 && hardwareUnits == 0 && glassPanes == 0)
            {
                diag.Add("LumberYardShopRuntime: nothing requested — no sale recorded.");
                return null;
            }

            // D2C: the named project's earmarked stock (Canon §6.4) is available
            // to THIS project, drawn first; ordinary sale inventory covers the
            // remainder. Other projects and retail never see earmarked stock.
            int projectReserved = lumberStock.EarmarkedUnitsForProject(projectId, null, null);
            int lumberAvailable = lumberStock.AvailableUnitsForSale(null, null) + projectReserved;
            int glassAvailable = glassStock.TotalGlassPanes;
            var shortKinds = new List<string>();
            foreach (LumberYardHardwareKindLine line in wanted)
            {
                int have = hardwareStock.AvailableUnits(line.Kind);
                if (have < line.Units)
                    shortKinds.Add($"need {line.Units} {LumberYardHardwareKindNames.DisplayNameFor(line.Kind)} / have {have}");
            }
            if (lumberAvailable < lumberUnits || shortKinds.Count > 0 || glassAvailable < glassPanes)
            {
                diag.Add($"LumberYardShopRuntime ({businessInstanceId}): SALE REFUSED to project '{projectId}' — "
                    + $"shortfall (need {lumberUnits} lumber / have {lumberAvailable}; "
                    + (shortKinds.Count > 0
                        ? "hardware kinds short: " + string.Join("; ", shortKinds.ToArray())
                        : "hardware kinds covered")
                    + $"; need {glassPanes} glass panes / have {glassAvailable}"
                    + "). Empty shelves stay empty; nothing invented, nothing consumed.");
                return null;
            }

            var record = new LumberYardSaleRecord
            {
                SaleId = idRegistry.Allocate(EntityKind.Contract),
                BuyerLabel = projectId.Trim(),
                IsConstructionSale = true,
                DayIndex = dayIndex,
            };

            var lumberLine = WithdrawLumberSaleLine(projectId, lumberUnits, lumberUnitPriceCents, diag);
            if (lumberLine != null) record.Lines.Add(lumberLine);

            if (hardwareUnits > 0)
            {
                var lines = new List<LumberYardHardwareDispenseLine>();
                foreach (LumberYardHardwareKindLine line in wanted)
                    lines.AddRange(hardwareStock.TryWithdrawUnits(line.Units, line.Kind, diag));
                int taken = 0;
                foreach (var line in lines) taken += line.UnitsTaken;
                record.Lines.Add(new LumberYardMaterialSaleLine
                {
                    ResourceKind = ConstructionResourceKind.Nails,
                    UnitsSold = taken,
                    UnitPriceCents = Math.Max(0, hardwareUnitPriceCents),
                    HardwareLines = lines,
                });
            }

            if (glassPanes > 0)
            {
                var lines = glassStock.TryWithdrawUnits(glassPanes, diag);
                int taken = 0;
                foreach (var line in lines) taken += line.UnitsTaken;
                record.Lines.Add(new LumberYardMaterialSaleLine
                {
                    ResourceKind = ConstructionResourceKind.Glass,
                    UnitsSold = taken,
                    UnitPriceCents = Math.Max(0, glassUnitPriceCents),
                    GlassLines = lines,
                });
            }

            salesHistory.Add(record);
            var kindSummary = new List<string>();
            foreach (LumberYardHardwareKindLine line in wanted)
                kindSummary.Add($"{line.Units} {LumberYardHardwareKindNames.DisplayNameFor(line.Kind)}");
            diag.Add($"LumberYardShopRuntime ({businessInstanceId}): sold to project '{projectId}' — "
                + $"{record.UnitsSoldFor(ConstructionResourceKind.Lumber)} lumber, "
                + $"{record.UnitsSoldFor(ConstructionResourceKind.Nails)} nails/hardware "
                + (kindSummary.Count > 0 ? "(" + string.Join(", ", kindSummary.ToArray()) + "), " : string.Empty)
                + $"{record.UnitsSoldFor(ConstructionResourceKind.Glass)} glass panes, "
                + $"total {record.TotalCents}¢ (sale {record.SaleId}). Provenance on every lot.");
            return record;
        }

        /// <summary>
        /// W4B: town retail — lumber to the wheelwright, households, and other
        /// local buyers (the recurring-order paths lumber_yard_lumber_to_*).
        /// Same real-lot rails and loud refusal as construction sales.
        /// Returns null on refusal.
        /// </summary>
        /// <summary>
        /// Lane 9e: holds quoted yard-lot units on the unified authority
        /// from quote until the sale attempt settles (released after every
        /// sale attempt — success consumes the units, refusal refunds the
        /// buyer). One reservation per lot, keyed lot:&lt;yard lot id&gt;,
        /// capacity = lot units, window = the whole quote day, FIFO order
        /// (the same lots the sale would withdraw). Returns the reservation
        /// ids (empty when unwired); on an authority refusal releases what
        /// was already held and returns null, so a half-held quote never
        /// stands.
        /// </summary>
        public List<string> TryReserveQuotedUnits(
            int units, string speciesFilter, string gradeFilter, int dayIndex,
            string ownerTaskId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var ids = new List<string>();
            if (ReservationAuthority == null || units <= 0 || string.IsNullOrWhiteSpace(ownerTaskId))
            {
                return ids;
            }

            float start = RetailStockReservation.GameSeconds(dayIndex, 0);
            float end = RetailStockReservation.GameSeconds(dayIndex + 1, 0);
            float atNoon = RetailStockReservation.GameSeconds(dayIndex, 720);
            int remaining = units;
            foreach (LumberYardLumberLot lot in lumberStock.LotsMatching(speciesFilter, gradeFilter))
            {
                if (remaining <= 0) break;
                if (lot == null || lot.LumberUnits <= 0) continue;
                int free = Math.Max(0, lot.LumberUnits) - Math.Max(0, lot.EarmarkedUnits)
                    - RetailStockReservation.ReservedUnitsForLot(ReservationAuthority, lot.LotId.ToString(), lot.LumberUnits, atNoon);
                int take = Math.Min(remaining, Math.Max(0, free));
                if (take <= 0) continue;

                string key = ReservationKeyConventions.Lot(lot.LotId.ToString());
                RegisterLotCapacity(key, lot.LumberUnits);
                var request = new ReservationRequest(ownerTaskId, $"yard quote for '{ownerTaskId}'");
                request.Items.Add(new ReservationItem(key, take, start, end));
                string refusal = ReservationAuthority.TryReserveAll(request, out ReservationHandle handle);
                if (refusal != null || handle == null)
                {
                    ReleaseQuotedUnits(ids);
                    diag.Add($"LumberYardShopRuntime ({businessInstanceId}): quote hold refused for lot {lot.LotId} — {refusal ?? "no handle"}. No units held.");
                    return null;
                }

                ids.Add(handle.ReservationId);
                remaining -= take;
            }

            if (remaining > 0)
            {
                ReleaseQuotedUnits(ids);
                diag.Add($"LumberYardShopRuntime ({businessInstanceId}): quote hold short — {units - remaining} of {units} units could be held; no units held.");
                return null;
            }

            return ids;
        }

        /// <summary>Lane 9e: releases quoted-unit holds by id. No-op when unknown or unwired (idempotent).</summary>
        public void ReleaseQuotedUnits(List<string> reservationIds)
        {
            if (reservationIds == null) return;
            foreach (string id in reservationIds)
            {
                RetailStockReservation.Release(ReservationAuthority, id);
            }
        }

        private void RegisterLotCapacity(string lotKey, float capacity)
        {
            if (capacity <= 0f) return;
            if (ReservationAuthority is EquipmentReservationService concrete)
            {
                concrete.RegisterResourceCapacity(lotKey, capacity);
            }
        }

        public LumberYardSaleRecord TrySellLumberRetail(
            string buyerLabel,
            int units,
            string speciesFilter,
            string gradeFilter,
            int unitPriceCents,
            int dayIndex,
            List<string> diag,
            string reservationOwnerTaskId = null)
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

            // D2C: retail draws only ordinary sale inventory — earmarked project
            // stock (Canon §6.4) is never sold to walk-in buyers.
            // Lane 9e: an open quote's held units are likewise not saleable
            // to a second buyer until the quote releases; the buyer's own
            // quote hold never blocks its own sale (excluded by owner).
            // The overlay reads holds at midday of the sale day — inside
            // the quote's whole-day window.
            Func<LumberYardLumberLot, int> reservedOverlay = ReservationAuthority != null
                ? lot => RetailStockReservation.ReservedUnitsForLotExcludingOwner(
                    ReservationAuthority, lot.LotId.ToString(), lot.LumberUnits,
                    RetailStockReservation.GameSeconds(dayIndex, 720), reservationOwnerTaskId)
                : null;
            int available = lumberStock.AvailableUnitsForSale(speciesFilter, gradeFilter, reservedOverlay);
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

            var lines = lumberStock.TryWithdrawUnits(units, speciesFilter, gradeFilter, diag, reservedOverlay);
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

        // ---- D2C: project stockpile ----

        /// <summary>
        /// D2C: reserves yard lumber for a named construction project (Canon
        /// §6.4 project stockpile). Reserved units leave ordinary sale
        /// inventory until deliberately released. Returns the refusal, or
        /// null.
        /// </summary>
        public string EarmarkLumberForProject(
            EntityId lotId, string projectId, int units, int dayIndex, List<string> diag)
        {
            return stockpile.Earmark(lumberStock, lotId, projectId, units, dayIndex, diag ?? diagnostics);
        }

        /// <summary>
        /// D2C: releases earmarked lumber back to ordinary sale inventory.
        /// Returns the refusal, or null.
        /// </summary>
        public string ReleaseProjectEarmark(
            EntityId lotId, int units, int dayIndex, List<string> diag)
        {
            return stockpile.Release(lumberStock, lotId, units, dayIndex, diag ?? diagnostics);
        }

        // ---- D2C: delivery ----

        /// <summary>
        /// D2C: schedules delivery of a recorded yard sale to a named
        /// destination (Canon §6.5 direct-to-project; Part VI §6.2 delivery
        /// obligation). Refuses sales the yard never recorded. Returns the
        /// refusal, or null.
        /// </summary>
        public string ScheduleDeliveryForSale(
            LumberYardSaleRecord sale,
            string destinationLabel,
            int miles,
            bool yardDelivers,
            string carrierLabel,
            bool buyerProvidesUnloading,
            LumberYardDeliveryPolicy policy,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (sale == null || !salesHistory.Contains(sale))
                return "LumberYardShopRuntime: deliveries fulfill recorded yard sales — unknown sale refused.";
            if (sale.Voided)
                return $"LumberYardShopRuntime: sale '{sale.SaleId}' is voided — its goods were returned to stock, nothing to deliver.";
            return deliveryRegister.ScheduleDelivery(sale, destinationLabel, miles, yardDelivers,
                carrierLabel, buyerProvidesUnloading, policy, idRegistry, dayIndex, diag);
        }

        /// <summary>D2C: marks a delivery obligation complete. Returns the refusal, or null.</summary>
        public string MarkDeliveryComplete(EntityId orderId, int dayIndex, List<string> diag)
        {
            return deliveryRegister.MarkDelivered(orderId, dayIndex, diag ?? diagnostics);
        }

        // ---- D2C: contractor credit ----

        /// <summary>
        /// D2C: sells construction materials to a project ON ACCOUNT to a
        /// named contractor (Canon Part VI §6.4: credit sale creates
        /// revenue/receivable without cash). ATOMIC with the same loud
        /// refusal as the cash sale, plus the account gates: no account, no
        /// account sale; strict posture, no account sale; over the credit
        /// limit, no account sale. The invoice is a receivable for the ledger
        /// authority to post — money never moves here. Returns the invoice,
        /// or null on refusal.
        /// </summary>
        public LumberYardContractorInvoice TrySellToConstructionProjectOnAccount(
            string projectId,
            string contractorBusinessId,
            int lumberUnits, int lumberUnitPriceCents,
            int nailsUnits, int nailsUnitPriceCents,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(contractorBusinessId))
            {
                diag.Add("LumberYardShopRuntime: account sales name their contractor — anonymous accounts refused.");
                return null;
            }
            var account = contractorLedger.FindAccount(contractorBusinessId);
            if (account == null)
            {
                diag.Add($"LumberYardShopRuntime: no contractor account for '{contractorBusinessId}' — open one before selling on account.");
                return null;
            }
            if (account.CreditPosture == LumberYardCreditPosture.Strict)
            {
                diag.Add($"LumberYardShopRuntime: contractor '{contractorBusinessId}' is on strict credit posture — cash terms, account sale refused.");
                return null;
            }
            int estimatedCents = Math.Max(0, lumberUnits) * Math.Max(0, lumberUnitPriceCents)
                + Math.Max(0, nailsUnits) * Math.Max(0, nailsUnitPriceCents);
            if (estimatedCents <= 0)
            {
                diag.Add("LumberYardShopRuntime: nothing chargeable — no account sale recorded.");
                return null;
            }
            int outstanding = contractorLedger.OutstandingForContractor(contractorBusinessId);
            if (outstanding + estimatedCents > Math.Max(0, account.Terms.CreditLimitCents))
            {
                diag.Add($"LumberYardShopRuntime: account sale refused for '{contractorBusinessId}' — over credit limit "
                    + $"(outstanding {outstanding}c + {estimatedCents}c > limit {account.Terms.CreditLimitCents}c). "
                    + "The yard never invents money.");
                return null;
            }

            var record = ExecuteConstructionSale(projectId, lumberUnits, lumberUnitPriceCents,
                nailsUnits, nailsUnitPriceCents, 0, 0, dayIndex, diag);
            if (record == null) return null;

            return IssueContractorInvoiceForSale(contractorBusinessId, record, null, dayIndex, diag);
        }

        /// <summary>
        /// D2C: invoices a recorded yard sale to a contractor account,
        /// optionally including a delivery charge (Canon §7.3:
        /// delivered-material payments are a real settlement form). The sale
        /// must be one the yard recorded. Returns the invoice, or null on
        /// refusal.
        /// </summary>
        public LumberYardContractorInvoice IssueContractorInvoiceForSale(
            string contractorBusinessId,
            LumberYardSaleRecord sale,
            LumberYardDeliveryOrder deliveryOrder,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (sale == null || !salesHistory.Contains(sale))
            {
                diag.Add("LumberYardShopRuntime: invoices bill recorded yard sales — unknown sale refused.");
                return null;
            }
            if (sale.Voided)
            {
                diag.Add($"LumberYardShopRuntime: sale '{sale.SaleId}' is voided — its value was returned, never invoiced.");
                return null;
            }
            var invoice = new LumberYardContractorInvoice
            {
                ContractorBusinessId = contractorBusinessId ?? string.Empty,
                SaleId = sale.SaleId,
                DeliveryOrderId = deliveryOrder != null ? deliveryOrder.OrderId : EntityId.Invalid,
                IssuedDayIndex = dayIndex,
            };
            foreach (var line in sale.Lines)
            {
                if (line == null) continue;
                string what = line.ResourceKind == ConstructionResourceKind.Lumber
                    ? $"lumber — {line.UnitsSold} units @ {line.UnitPriceCents}c"
                    : line.ResourceKind == ConstructionResourceKind.Glass
                        ? $"window glass — {line.UnitsSold} panes @ {line.UnitPriceCents}c"
                        : $"nails/hardware — {line.UnitsSold} units @ {line.UnitPriceCents}c";
                invoice.Lines.Add(new LumberYardContractorInvoiceLine(what, line.LineTotalCents));
            }
            if (deliveryOrder != null && deliveryOrder.ChargeCents > 0)
            {
                invoice.Lines.Add(new LumberYardContractorInvoiceLine(
                    $"delivery to {deliveryOrder.DestinationLabel} ({deliveryOrder.Miles} mi)", deliveryOrder.ChargeCents));
            }

            string refusal = contractorLedger.IssueInvoice(invoice, idRegistry, diag);
            return refusal == null ? invoice : null;
        }

        /// <summary>
        /// D2C: records a contractor's payment against an invoice. The
        /// caller's real money moves alongside this call; the ledger only
        /// shrinks the receivable. Returns the refusal, or null.
        /// </summary>
        public string RecordContractorPayment(EntityId invoiceId, int cents, int dayIndex, List<string> diag)
        {
            return contractorLedger.RecordPayment(invoiceId, cents, dayIndex, diag ?? diagnostics);
        }

        // ---- D2C: landed-cost pricing ----

        /// <summary>
        /// D2C: quotes a per-unit retail price derived from the FIFO landed
        /// cost of the yard's actual sale-available lots (Canon §5.3: retail
        /// pricing follows landed cost). Returns -1 when nothing matching is
        /// in stock — no stock, no quote.
        /// </summary>
        public int QuoteRetailLumberPrice(
            string speciesFilter,
            string gradeFilter,
            LumberYardGradePricePolicy policy,
            List<string> diag)
        {
            return LumberYardPricing.QuoteRetailUnitPriceCents(lumberStock, speciesFilter, gradeFilter, policy, diag ?? diagnostics);
        }

        // ---- D2C: cull disposal ----

        /// <summary>
        /// D2C: disposes cull-grade lumber (burned, discarded, given away)
        /// with a full audit record (Canon §4.5: losses and discards stay
        /// visible). ATOMIC: on insufficient cull stock, refused loudly and
        /// nothing is disposed. Returns the refusal, or null.
        /// </summary>
        public string DisposeCullUnits(
            int units,
            LumberYardCullDisposalReason reason,
            string note,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (idRegistry == null)
            {
                diag.Add("LumberYardShopRuntime: no EntityIdRegistry — disposal refused.");
                return "LumberYardShopRuntime: no EntityIdRegistry — disposal refused.";
            }
            return cullDisposal.DisposeCull(lumberStock, units, reason, note, idRegistry, dayIndex, diag);
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
            public LumberYardGlassStock.LumberYardGlassStockSaveDto GlassStock =
                new LumberYardGlassStock.LumberYardGlassStockSaveDto();
            public List<LumberYardSaleRecord> SalesHistory = new List<LumberYardSaleRecord>();
            public LumberYardProjectStockpile.LumberYardProjectStockpileSaveDto Stockpile =
                new LumberYardProjectStockpile.LumberYardProjectStockpileSaveDto();
            public LumberYardDeliveryRegister.LumberYardDeliveryRegisterSaveDto DeliveryRegister =
                new LumberYardDeliveryRegister.LumberYardDeliveryRegisterSaveDto();
            public LumberYardContractorLedger.LumberYardContractorLedgerSaveDto ContractorLedger =
                new LumberYardContractorLedger.LumberYardContractorLedgerSaveDto();
            public LumberYardCullDisposalRegister.LumberYardCullDisposalRegisterSaveDto CullDisposal =
                new LumberYardCullDisposalRegister.LumberYardCullDisposalRegisterSaveDto();
        }

        public LumberYardShopRuntimeSaveDto CaptureSaveDto()
        {
            var dto = new LumberYardShopRuntimeSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                OpeningStockApplied = openingStockApplied,
                LumberStock = lumberStock.CaptureSaveDto(),
                HardwareStock = hardwareStock.CaptureSaveDto(),
                GlassStock = glassStock.CaptureSaveDto(),
                Stockpile = stockpile.CaptureSaveDto(),
                DeliveryRegister = deliveryRegister.CaptureSaveDto(),
                ContractorLedger = contractorLedger.CaptureSaveDto(),
                CullDisposal = cullDisposal.CaptureSaveDto(),
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
            glassStock.LoadFromSaveDto(dto != null ? dto.GlassStock : null);
            stockpile.LoadFromSaveDto(dto != null ? dto.Stockpile : null);
            deliveryRegister.LoadFromSaveDto(dto != null ? dto.DeliveryRegister : null);
            contractorLedger.LoadFromSaveDto(dto != null ? dto.ContractorLedger : null);
            cullDisposal.LoadFromSaveDto(dto != null ? dto.CullDisposal : null);
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
                Voided = sale.Voided,
                VoidedDayIndex = sale.VoidedDayIndex,
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
                        SourceKind = l.SourceKind,
                        AcquiredDayIndex = l.AcquiredDayIndex,
                        SourceSawmillLotId = l.SourceSawmillLotId,
                        SourceMillBusinessId = l.SourceMillBusinessId,
                        SourceLogLotId = l.SourceLogLotId,
                        StandId = l.StandId,
                        SawedBy = l.SawedBy,
                        SawedDayIndex = l.SawedDayIndex,
                        ConversionProfileId = l.ConversionProfileId,
                        SellerNote = l.SellerNote,
                        ImportOrderId = l.ImportOrderId,
                        OriginName = l.OriginName,
                        IsBootstrapEndowment = l.IsBootstrapEndowment,
                        UnitCostCents = l.UnitCostCents,
                        LandedFreightCents = l.LandedFreightCents,
                        LandedHandlingCents = l.LandedHandlingCents,
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
                        AcquiredDayIndex = h.AcquiredDayIndex,
                        SourceBlacksmithBusinessId = h.SourceBlacksmithBusinessId,
                        ForgedDayIndex = h.ForgedDayIndex,
                        IronImportOrderId = h.IronImportOrderId,
                        ImportOrderId = h.ImportOrderId,
                        OriginName = h.OriginName,
                        IsBootstrapEndowment = h.IsBootstrapEndowment,
                        SupplierNote = h.SupplierNote,
                        UnitCostCents = h.UnitCostCents,
                    });
                }
                foreach (var g in line.GlassLines)
                {
                    if (g == null) continue;
                    lineClone.GlassLines.Add(new LumberYardGlassDispenseLine
                    {
                        LotId = g.LotId,
                        UnitsTaken = g.UnitsTaken,
                        ProvenanceChain = g.ProvenanceChain,
                        AcquiredDayIndex = g.AcquiredDayIndex,
                        ImportOrderId = g.ImportOrderId,
                        OriginName = g.OriginName,
                        UnitCostCents = g.UnitCostCents,
                    });
                }
                clone.Lines.Add(lineClone);
            }
            return clone;
        }
    }
}
