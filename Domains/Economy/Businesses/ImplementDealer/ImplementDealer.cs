using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.ImplementDealer
{
    /// <summary>
    /// T1H: one implement model line the dealer carries. Stock comes from a NAMED
    /// supplier — the manufacturer via the EQU-1 import path, or the local blacksmith.
    /// </summary>
    [Serializable]
    public sealed class ImplementModel
    {
        public string ModelId = string.Empty;
        public string DisplayName = string.Empty;
        public string Kind = string.Empty; // "plow", "harrow", "reaper", "wagon", "cultivator"
        public int PriceCents;
        public int UnitsOnHand;
        public string SupplierName = string.Empty; // upstream provenance
        public string SupplierVia = string.Empty; // "equ-1 import", "local blacksmith", ...
        /// <summary>
        /// EQP-5: the TRUE maker when the dealer did not make it — e.g. the local
        /// blacksmith's business id when stocked "via local blacksmith". Empty =
        /// the dealer made it (legacy stock).
        /// </summary>
        public string MakerBusinessId = string.Empty;
        public string MakerBusinessName = string.Empty;

        public ImplementModel() { }
    }

    /// <summary>
    /// T1H: one spare-part line. The historical dealer stocked parts and did field
    /// service — parts with provenance, priced per unit.
    /// </summary>
    [Serializable]
    public sealed class PartLine
    {
        public string PartId = string.Empty;
        public string DisplayName = string.Empty;
        public string FitsModelId = string.Empty;
        public int PriceCents;
        public int UnitsOnHand;
        public string SupplierName = string.Empty; // upstream provenance

        public PartLine() { }
    }

    /// <summary>
    /// T1H: one implement sale. Historical norm: sold on notes — down payment plus
    /// installments the dealer collects. The sold machine becomes an EQU-2
    /// EquipmentAsset with full provenance (maker → dealer → buyer).
    /// </summary>
    [Serializable]
    public sealed class ImplementSale
    {
        public EntityId SaleId = EntityId.Invalid; // EntityKind.Contract (SWN-3 precedent)
        public string AssetId = string.Empty; // the EquipmentAsset created
        public string ModelId = string.Empty;
        public string BuyerKind = string.Empty; // "business", "household"
        public string BuyerId = string.Empty;
        public int DayIndex;
        public int PriceCents;
        public int DownPaymentCents;
        public int InstallmentCents;
        public int InstallmentsRemaining;
        public bool Settled => InstallmentsRemaining <= 0;

        public ImplementSale() { }

        /// <summary>Collects one installment. Returns the amount collected (0 when settled).</summary>
        public int CollectInstallment()
        {
            if (InstallmentsRemaining <= 0) return 0;
            InstallmentsRemaining--;
            return InstallmentCents;
        }
    }

    /// <summary>
    /// T1H: the agricultural-implement dealer as a real business. The honest source of
    /// equipment beyond the smith's forge: buys from manufacturers/importers, retails
    /// machines on notes, stocks parts, does simple repairs in-house, and refers heavy
    /// work to the blacksmith's queue — creating the repair demand the EQU-2 smith
    /// feeds on. Physical truth holds: machines and parts are finite stock.
    /// </summary>
    public sealed class ImplementDealer
    {
        public string BusinessId = string.Empty;
        public string DisplayName = string.Empty;
        public string LocationId = string.Empty;

        private readonly List<ImplementModel> models = new List<ImplementModel>();
        private readonly List<PartLine> parts = new List<PartLine>();
        private readonly List<ImplementSale> sales = new List<ImplementSale>();
        /// <summary>EQP-5: the equipment assets this dealer has created on sale (maker → dealer → buyer provenance).</summary>
        private readonly List<EquipmentAsset> soldAssets = new List<EquipmentAsset>();
        /// <summary>D4L: trade-ins this dealer has taken — valuation via the existing service, credit recorded.</summary>
        private readonly ImplementDealerTradeInBook tradeInBook = new ImplementDealerTradeInBook();

        /// <summary>Calibration: simple dealer repairs (adjustments, part swaps) in-house.</summary>
        public int SimpleRepairLaborCents = 75;

        public ImplementDealer() { }

        public ImplementDealer(string businessId, string displayName, string locationId)
        {
            BusinessId = businessId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            LocationId = locationId ?? string.Empty;
        }

        public IReadOnlyList<ImplementModel> Models => models;
        public IReadOnlyList<PartLine> Parts => parts;
        public IReadOnlyList<ImplementSale> Sales => sales;
        /// <summary>EQP-5: assets created on sale, with true maker provenance.</summary>
        public IReadOnlyList<EquipmentAsset> SoldAssets => soldAssets;
        /// <summary>D4L: the dealer's trade-in book (valuation via the existing service; credit recorded, never auto-valued).</summary>
        public ImplementDealerTradeInBook TradeInBook => tradeInBook;

        /// <summary>Stocks a model line. Supplier must be named — no orphan inventory.</summary>
        public string StockModel(ImplementModel model, int units, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (model == null || units <= 0) return "ImplementDealer: nothing to stock.";
            if (string.IsNullOrWhiteSpace(model.SupplierName))
            {
                return "ImplementDealer: implements must name their supplier (manufacturer/importer/smith) — no orphan inputs.";
            }

            ImplementModel line = models.Find(m => string.Equals(m.ModelId, model.ModelId, StringComparison.Ordinal));
            if (line == null)
            {
                model.UnitsOnHand = units;
                models.Add(model);
            }
            else
            {
                line.UnitsOnHand += units;
                line.SupplierName = model.SupplierName;
            }

            diagnostics.Add($"ImplementDealer {DisplayName}: stocked {units}x {model.DisplayName} from {model.SupplierName}.");
            return null;
        }

        /// <summary>Stocks a spare-part line. Same provenance rule.</summary>
        public string StockPart(PartLine part, int units, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (part == null || units <= 0) return "ImplementDealer: no parts to stock.";
            if (string.IsNullOrWhiteSpace(part.SupplierName))
            {
                return "ImplementDealer: parts must name their supplier — no orphan inputs.";
            }

            PartLine line = parts.Find(p => string.Equals(p.PartId, part.PartId, StringComparison.Ordinal));
            if (line == null)
            {
                part.UnitsOnHand = units;
                parts.Add(part);
            }
            else
            {
                line.UnitsOnHand += units;
            }

            return null;
        }

        /// <summary>
        /// Sells one machine. Creates the EQU-2 EquipmentAsset with provenance
        /// (maker → dealer → buyer), transfers ownership, and records the notes.
        /// </summary>
        public ImplementSale SellImplement(
            EntityIdRegistry idRegistry,
            string modelId,
            string buyerKind,
            string buyerId,
            int downPaymentCents,
            int installmentCount,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            ImplementModel line = models.Find(m => string.Equals(m.ModelId, modelId, StringComparison.Ordinal));
            if (line == null || line.UnitsOnHand <= 0)
            {
                diagnostics.Add($"ImplementDealer: no {modelId} in stock — finite supply.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(buyerId))
            {
                diagnostics.Add("ImplementDealer: no anonymous buyers — every sale names its buyer.");
                return null;
            }

            line.UnitsOnHand--;

            var asset = new EquipmentAsset
            {
                AssetId = $"{BusinessId}-{line.Kind}-{dayIndex}-{line.UnitsOnHand}",
                Kind = line.Kind,
                DisplayName = line.DisplayName,
                Condition01 = 1f,
                OwnerKind = buyerKind,
                OwnerId = buyerId,
                LocationId = LocationId,
                // EQP-5: the TRUE maker — the smithy when stocked "via local
                // blacksmith", the dealer only for legacy/own-make stock.
                MadeByBusinessId = string.IsNullOrWhiteSpace(line.MakerBusinessId) ? BusinessId : line.MakerBusinessId,
                MadeByBusinessName = string.IsNullOrWhiteSpace(line.MakerBusinessName) ? DisplayName : line.MakerBusinessName,
                MadeDayIndex = dayIndex,
            };
            asset.MaterialLotIds.Add($"dealer-stock:{line.SupplierName}");
            soldAssets.Add(asset);

            int financed = Math.Max(0, line.PriceCents - Math.Max(0, downPaymentCents));
            int count = Math.Max(1, installmentCount);
            var sale = new ImplementSale
            {
                SaleId = idRegistry != null ? idRegistry.Allocate(EntityKind.Contract) : EntityId.Invalid,
                AssetId = asset.AssetId,
                ModelId = line.ModelId,
                BuyerKind = buyerKind ?? string.Empty,
                BuyerId = buyerId,
                DayIndex = dayIndex,
                PriceCents = line.PriceCents,
                DownPaymentCents = Math.Max(0, downPaymentCents),
                InstallmentCents = Mathf.CeilToInt(financed / (float)count),
                InstallmentsRemaining = financed > 0 ? count : 0,
            };
            sales.Add(sale);

            diagnostics.Add($"ImplementDealer {DisplayName}: sold {line.DisplayName} to {buyerId} for {line.PriceCents}c ({sale.DownPaymentCents}c down, {sale.InstallmentsRemaining} notes of {sale.InstallmentCents}c). Asset {asset.AssetId}.");
            return sale;
        }

        /// <summary>
        /// Sells spare parts over the counter. Finite stock, named buyer.
        /// Returns units actually sold.
        /// </summary>
        public int SellPart(string partId, int units, string buyerId, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            PartLine line = parts.Find(p => string.Equals(p.PartId, partId, StringComparison.Ordinal));
            if (line == null || line.UnitsOnHand <= 0)
            {
                diagnostics.Add($"ImplementDealer: part '{partId}' not in stock.");
                return 0;
            }

            if (string.IsNullOrWhiteSpace(buyerId))
            {
                diagnostics.Add("ImplementDealer: no anonymous buyers.");
                return 0;
            }

            int sold = Math.Min(units, line.UnitsOnHand);
            line.UnitsOnHand -= sold;
            return sold;
        }

        /// <summary>
        /// D4L: takes a used machine in trade from a customer. The dealer values
        /// it through the existing valuation service (needs real
        /// replacement-cost data); the CREDIT is the negotiated recorded
        /// amount. Title transfers to the dealer with provenance intact.
        /// </summary>
        public DealerTradeInRecord TakeTradeIn(
            EquipmentAsset asset,
            string customerKind,
            string customerId,
            string customerName,
            int replacementCostCents,
            int agreedCreditCents,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diagnostics)
        {
            return tradeInBook.TakeTradeIn(
                asset, customerKind, customerId, customerName,
                BusinessId, DisplayName,
                replacementCostCents, agreedCreditCents, dayIndex,
                idRegistry, diagnostics ?? new List<string>());
        }

        /// <summary>
        /// D4L: applies trade-in credit against a new implement sale as a
        /// recorded amount. The ImplementSale's notes stand as recorded; the
        /// application is the offset record.
        /// </summary>
        public string ApplyTradeInCredit(
            string tradeInKey, EntityId saleId, int creditCents,
            int dayIndex, List<string> diagnostics)
        {
            return tradeInBook.ApplyCreditToSale(
                tradeInKey, saleId, creditCents, dayIndex, diagnostics ?? new List<string>());
        }

        /// <summary>
        /// The repair desk: simple work (adjustments, part swaps) is done in-house for
        /// the labor fee; heavy work (forging, major rebuilds) is referred into the
        /// blacksmith's queue — the repair demand the EQU-2 smith feeds on.
        /// Returns the in-house cost, or null when referred (the work order is in the queue).
        /// </summary>
        public int? DiagnoseRepair(
            string assetId,
            string assetDescription,
            string reportedProblem,
            bool isHeavyWork,
            string customerName,
            string customerBusinessId,
            RepairQueue smithQueue,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (string.IsNullOrWhiteSpace(assetId))
            {
                diagnostics.Add("ImplementDealer: repair needs the asset's id.");
                return null;
            }

            if (!isHeavyWork)
            {
                diagnostics.Add($"ImplementDealer {DisplayName}: simple repair on '{assetDescription}' done in-house for {SimpleRepairLaborCents}c.");
                return SimpleRepairLaborCents;
            }

            if (smithQueue == null)
            {
                diagnostics.Add("ImplementDealer: heavy work needs a blacksmith queue to refer into — none provided.");
                return null;
            }

            RepairWorkOrder order = smithQueue.Intake(
                customerName, customerBusinessId, assetId, assetDescription,
                reportedProblem, "smithing", LocationId, RepairUrgency.Routine,
                dayIndex, diagnostics);
            diagnostics.Add($"ImplementDealer {DisplayName}: heavy repair on '{assetDescription}' referred to the blacksmith ({order.WorkOrderId}) — the smith's queue feeds on dealer referrals.");
            return null;
        }
    }
}
