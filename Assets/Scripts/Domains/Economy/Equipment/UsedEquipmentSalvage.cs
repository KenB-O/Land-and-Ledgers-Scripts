using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// D4L: scrap and part-out sales for worn-out equipment. Canon Part VI §53:
    /// "Failed assets remain physical objects that can be repaired, stored,
    /// sold, parted out, scrapped, abandoned or used as limited backup."
    /// Canon §7.2F: a shop may "dismantle uneconomic assets for parts/scrap".
    ///
    /// EXISTING-SYSTEM LINKS ONLY — no synthetic scrap values anywhere here:
    /// - Wagons have the D3A dismantle path (WheelwrightUsedWagonYard +
    ///   WheelwrightRuntime.DismantleWreck): real parts to the shelf plus the
    ///   calibrated 0.10 scrap fraction of acquisition cost. This ledger does
    ///   NOT duplicate it; a diagnostic nudges wagon sellers toward it.
    /// - Everything else: a scrap/part-out sale is a REAL negotiated sale to
    ///   a NAMED buyer (the smith, a scrapper, another shop) at a RECORDED
    ///   agreed price. The ledger never invents what scrap is worth.
    ///
    /// The asset is not deleted on sale (Canon §53 — it persists as a physical
    /// object); ownership transfers to the buyer with the disposition recorded.
    /// </summary>

    /// <summary>D4L: how a worn-out asset left circulation.</summary>
    public enum EquipmentSalvageDisposition
    {
        Unspecified = 0,
        /// <summary>Sold whole to a scrapper for material value.</summary>
        Scrapped = 1,
        /// <summary>Sold to be broken for usable parts.</summary>
        PartedOut = 2,
    }

    /// <summary>
    /// D4L: one recorded scrap/part-out sale. The price is the AGREED amount
    /// between real parties — recorded, never computed by this ledger.
    /// </summary>
    [Serializable]
    public sealed class EquipmentScrapSale
    {
        public EntityId SaleId = EntityId.Invalid; // EntityKind.Contract (SWN-3 precedent)
        public string AssetId = string.Empty;
        public string Kind = string.Empty;
        public string DisplayName = string.Empty;
        public float Condition01AtSale;

        public string SellerKind = string.Empty; // "business", "household"
        public string SellerId = string.Empty;
        public string SellerName = string.Empty;
        public string BuyerKind = string.Empty;
        public string BuyerId = string.Empty;
        public string BuyerName = string.Empty; // the named scrapper — no anonymous scrap

        public int AgreedPriceCents; // recorded, never auto-valued
        public EquipmentSalvageDisposition Disposition = EquipmentSalvageDisposition.Unspecified;
        public int DayIndex;
        public string Notes = string.Empty;

        public EquipmentScrapSale() { }
    }

    /// <summary>
    /// D4L: the salvage ledger — records scrap/part-out sales of below-usable
    /// equipment. Refuses usable assets (repair it, list it, or keep it — a
    /// working plow is not scrap), reserved assets, and anonymous parties.
    /// </summary>
    public sealed class EquipmentSalvageLedger
    {
        private readonly Dictionary<string, EquipmentScrapSale> sales =
            new Dictionary<string, EquipmentScrapSale>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, EquipmentScrapSale> Sales => sales;
        public int Count => sales.Count;

        public EquipmentSalvageLedger() { }

        private static string SaleKeyOf(EntityId id) => id.Kind + ":" + id.Id;

        public EquipmentScrapSale Find(string saleKey)
        {
            if (string.IsNullOrWhiteSpace(saleKey)) return null;
            sales.TryGetValue(saleKey, out EquipmentScrapSale sale);
            return sale;
        }

        /// <summary>
        /// Records a scrap/part-out sale. The asset must be BELOW usable
        /// condition (EquipmentAsset.IsUsable) — selling a working asset "for
        /// scrap" is refused loudly. The price is recorded as agreed; this
        /// ledger never values scrap.
        /// </summary>
        public EquipmentScrapSale RecordScrapSale(
            EquipmentAsset asset,
            string sellerKind,
            string sellerId,
            string sellerName,
            string buyerKind,
            string buyerId,
            string buyerName,
            int agreedPriceCents,
            EquipmentSalvageDisposition disposition,
            int dayIndex,
            string notes,
            EntityIdRegistry idRegistry,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (asset == null) { diagnostics.Add("EquipmentSalvageLedger: no asset supplied."); return null; }
            if (string.IsNullOrWhiteSpace(asset.AssetId))
            { diagnostics.Add("EquipmentSalvageLedger: the asset has no identity — registry assets only."); return null; }
            if (asset.IsUsable)
            {
                diagnostics.Add(
                    $"EquipmentSalvageLedger: {asset.AssetId} is at {asset.Condition01:P0} — usable equipment " +
                    "is not scrap. Repair it, list it on the used market, or keep it.");
                return null;
            }
            if (!string.Equals(asset.OwnerKind, sellerKind, StringComparison.Ordinal) ||
                !string.Equals(asset.OwnerId, sellerId, StringComparison.Ordinal))
            {
                diagnostics.Add(
                    $"EquipmentSalvageLedger: {sellerId} does not own {asset.AssetId} " +
                    $"(owner is {asset.OwnerKind}:{asset.OwnerId}) — no scrapping what you don't hold.");
                return null;
            }
            if (asset.IsReserved)
            {
                diagnostics.Add(
                    $"EquipmentSalvageLedger: {asset.AssetId} is reserved by {asset.ReservedBy} — " +
                    "no double-dealing (Tech X §3.8).");
                return null;
            }
            if (string.IsNullOrWhiteSpace(sellerId) || string.IsNullOrWhiteSpace(buyerId))
            { diagnostics.Add("EquipmentSalvageLedger: scrap sales name both parties — no anonymous scrap."); return null; }
            if (agreedPriceCents < 0)
            { diagnostics.Add("EquipmentSalvageLedger: the agreed price cannot be negative."); return null; }
            if (disposition == EquipmentSalvageDisposition.Unspecified)
            { diagnostics.Add("EquipmentSalvageLedger: name the disposition — scrapped or parted-out."); return null; }

            string transferRejection = asset.TransferOwnership(
                buyerKind, buyerId, $"scrap/part-out sale ({disposition})", dayIndex);
            if (transferRejection != null)
            {
                diagnostics.Add($"EquipmentSalvageLedger: {transferRejection}");
                return null;
            }
            // The asset is dismantled: condition goes to zero. It persists as a
            // physical object (Canon §53) — it is not deleted.
            asset.RepairTo(0f);
            asset.RecordMaintenance(
                $"Dismantled: {disposition} sale to {buyerName ?? buyerId} for {agreedPriceCents}c.", dayIndex);

            var sale = new EquipmentScrapSale
            {
                SaleId = idRegistry != null ? idRegistry.Allocate(EntityKind.Contract) : EntityId.Invalid,
                AssetId = asset.AssetId,
                Kind = asset.Kind,
                DisplayName = asset.DisplayName,
                Condition01AtSale = 0f,
                SellerKind = sellerKind ?? string.Empty,
                SellerId = sellerId,
                SellerName = sellerName ?? string.Empty,
                BuyerKind = buyerKind ?? string.Empty,
                BuyerId = buyerId,
                BuyerName = buyerName ?? string.Empty,
                AgreedPriceCents = agreedPriceCents,
                Disposition = disposition,
                DayIndex = dayIndex,
                Notes = notes ?? string.Empty,
            };
            sales[SaleKeyOf(sale.SaleId)] = sale;

            diagnostics.Add(
                $"EquipmentSalvageLedger: {asset.DisplayName} ({asset.AssetId}) {disposition.ToString().ToLower()} " +
                $"to {buyerName ?? buyerId} for {agreedPriceCents}c (agreed price, recorded).");
            if (IsWagonKind(asset.Kind))
            {
                diagnostics.Add(
                    "EquipmentSalvageLedger: note — wagons also have the wheelwright's D3A dismantle path " +
                    "(WheelwrightUsedWagonYard: real parts to the shelf + calibrated scrap fraction).");
            }
            return sale;
        }

        /// <summary>
        /// The wagon kinds the D3A wheelwright yard trades — kept in sync with
        /// WheelwrightUsedWagonYard.WagonKinds (referenced, not duplicated as
        /// authority; the yard remains the authority for wagon dismantling).
        /// </summary>
        public static bool IsWagonKind(string kind)
        {
            return string.Equals(kind, "farm-wagon", StringComparison.Ordinal)
                || string.Equals(kind, "freight-wagon", StringComparison.Ordinal)
                || string.Equals(kind, "spring-wagon", StringComparison.Ordinal)
                || string.Equals(kind, "dray", StringComparison.Ordinal);
        }

        // ---------- save DTO (inside the owning ledger class) ----------

        [Serializable]
        public sealed class EquipmentSalvageLedgerDto
        {
            public List<EquipmentScrapSale> Sales = new List<EquipmentScrapSale>();
        }

        public EquipmentSalvageLedgerDto ToSaveDto()
        {
            var dto = new EquipmentSalvageLedgerDto();
            foreach (var kv in sales) dto.Sales.Add(kv.Value);
            return dto;
        }

        public void LoadFromSaveDto(EquipmentSalvageLedgerDto dto)
        {
            sales.Clear();
            if (dto == null || dto.Sales == null) return;
            foreach (EquipmentScrapSale s in dto.Sales)
            {
                if (s == null || s.SaleId.Equals(EntityId.Invalid)) continue;
                sales[SaleKeyOf(s.SaleId)] = s;
            }
        }
    }
}
