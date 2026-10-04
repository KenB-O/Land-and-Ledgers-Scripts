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
        public LumberYardProjectStockpile Stockpile => stockpile;
        public LumberYardDeliveryRegister DeliveryRegister => deliveryRegister;
        public LumberYardContractorLedger ContractorLedger => contractorLedger;
        public LumberYardCullDisposalRegister CullDisposal => cullDisposal;
        public IReadOnlyList<LumberYardSaleRecord> SalesHistory => salesHistory;
        public bool OpeningStockApplied => openingStockApplied;

        public LumberYardShopRuntime(string businessInstanceId, EntityIdRegistry idRegistry)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
            this.idRegistry = idRegistry;
            this.lumberStock = new LumberYardLumberStock(this.businessInstanceId);
            this.hardwareStock = new LumberYardHardwareStock(this.businessInstanceId);
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
                nailsUnits, nailsUnitPriceCents, dayIndex, diag);
        }

        private LumberYardSaleRecord ExecuteConstructionSale(
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

            // D2C: the named project's earmarked stock (Canon §6.4) is available
            // to THIS project, drawn first; ordinary sale inventory covers the
            // remainder. Other projects and retail never see earmarked stock.
            int projectReserved = lumberStock.EarmarkedUnitsForProject(projectId, null, null);
            int lumberAvailable = lumberStock.AvailableUnitsForSale(null, null) + projectReserved;
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
                var lines = new List<LumberYardLumberDispenseLine>();
                lines.AddRange(lumberStock.TryWithdrawProjectStock(lumberUnits, projectId, null, null, diag));
                int takenSoFar = 0;
                foreach (var line in lines) takenSoFar += line.UnitsTaken;
                int remainder = lumberUnits - takenSoFar;
                if (remainder > 0)
                    lines.AddRange(lumberStock.TryWithdrawUnits(remainder, null, null, diag));
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

            // D2C: retail draws only ordinary sale inventory — earmarked project
            // stock (Canon §6.4) is never sold to walk-in buyers.
            int available = lumberStock.AvailableUnitsForSale(speciesFilter, gradeFilter);
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
                nailsUnits, nailsUnitPriceCents, dayIndex, diag);
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
