using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.ImplementDealer
{
    /// <summary>
    /// D4L: trade-ins at the T1H implement dealer. The dealer takes a used
    /// machine off a customer's hands and credits an AGREED amount against a
    /// new purchase. Two numbers, kept deliberately separate:
    ///   - DealerValuationCents: the dealer's valuation through the EXISTING
    ///     valuation service (BusinessEquipmentRegister.TransferableValueCents
    ///     — condition-weighted, Canon §11.6). Informational.
    ///   - AgreedCreditCents: the negotiated credit actually granted. RECORDED,
    ///     never auto-valued (Canon §7.2D pricing freedom — the valuation is
    ///     the dealer's reference, not the price).
    /// The trade-in asset transfers to the dealer (title + provenance intact),
    /// where the Canon §7.2F loop applies: refurbish and resell, or dismantle
    /// uneconomic assets for parts/scrap. The dealer's book never mints money:
    /// credit is applied against a real ImplementSale as a recorded amount.
    /// </summary>

    /// <summary>D4L: lifecycle of one dealer trade-in.</summary>
    public enum DealerTradeInStatus
    {
        Unspecified = 0,
        /// <summary>Taken in; credit available against a new purchase.</summary>
        Recorded = 1,
        /// <summary>Credit fully applied to a sale (or sales).</summary>
        CreditApplied = 2,
    }

    /// <summary>
    /// D4L: one recorded trade-in — the used asset, the dealer's valuation
    /// (existing service), and the agreed credit (negotiated, recorded).
    /// </summary>
    [Serializable]
    public sealed class DealerTradeInRecord
    {
        public EntityId TradeInId = EntityId.Invalid; // EntityKind.Contract (SWN-3 precedent)
        public string AssetId = string.Empty;
        public string Kind = string.Empty;
        public string DisplayName = string.Empty;
        public float Condition01AtTradeIn;

        public string CustomerKind = string.Empty; // "business", "household"
        public string CustomerId = string.Empty;
        public string CustomerName = string.Empty;

        public string DealerBusinessId = string.Empty;
        public string DealerName = string.Empty;

        public int ReplacementCostCents;   // real price data the valuation ran on
        public int DealerValuationCents;   // existing valuation service (Canon §11.6)
        public int AgreedCreditCents;      // negotiated — recorded, never auto-valued
        public int AppliedCreditCents;     // how much has been applied to sales

        public int DayIndex;
        public DealerTradeInStatus Status = DealerTradeInStatus.Recorded;

        public int RemainingCreditCents => Math.Max(0, AgreedCreditCents - AppliedCreditCents);

        public DealerTradeInRecord() { }
    }

    /// <summary>
    /// D4L: one application of trade-in credit against a new implement sale —
    /// the credit as a recorded amount, tied to a real ImplementSale.
    /// </summary>
    [Serializable]
    public sealed class TradeInCreditApplication
    {
        public EntityId TradeInId = EntityId.Invalid;
        public EntityId SaleId = EntityId.Invalid; // the ImplementSale it offsets
        public int CreditCents;
        public int DayIndex;

        public TradeInCreditApplication() { }
    }

    /// <summary>
    /// D4L: the dealer's trade-in book. Owned by the ImplementDealer (additive
    /// field). Save DTO lives here with its owning book.
    /// </summary>
    public sealed class ImplementDealerTradeInBook
    {
        private readonly Dictionary<string, DealerTradeInRecord> tradeIns =
            new Dictionary<string, DealerTradeInRecord>(StringComparer.Ordinal);
        private readonly List<TradeInCreditApplication> applications =
            new List<TradeInCreditApplication>();

        public IReadOnlyDictionary<string, DealerTradeInRecord> TradeIns => tradeIns;
        public IReadOnlyList<TradeInCreditApplication> Applications => applications;

        public ImplementDealerTradeInBook() { }

        private static string TradeInKeyOf(EntityId id) => id.Kind + ":" + id.Id;

        public DealerTradeInRecord Find(string tradeInKey)
        {
            if (string.IsNullOrWhiteSpace(tradeInKey)) return null;
            tradeIns.TryGetValue(tradeInKey, out DealerTradeInRecord record);
            return record;
        }

        /// <summary>
        /// Runs the dealer's valuation through the EXISTING valuation service
        /// (the condition-weighted transferable floor, Canon §11.6) on a
        /// single-asset register. Needs real replacement-cost data — refuses
        /// without it rather than inventing a value.
        /// </summary>
        public static int DealerValuationCents(EquipmentAsset asset, int replacementCostCents)
        {
            if (asset == null || replacementCostCents <= 0) return 0;
            var register = new BusinessEquipmentRegister { BusinessInstanceId = "dealer-valuation" };
            register.RegisterAsset(asset);
            return register.TransferableValueCents(
                kind => string.Equals(kind, asset.Kind, StringComparison.Ordinal) ? replacementCostCents : 0,
                kitId => 0);
        }

        /// <summary>
        /// Takes a used machine in trade. The customer must own it; the asset
        /// must not be reserved. The dealer values it through the existing
        /// valuation service (needs real replacement-cost data), the CREDIT is
        /// the negotiated recorded amount, and title transfers to the dealer
        /// with provenance intact.
        /// </summary>
        public DealerTradeInRecord TakeTradeIn(
            EquipmentAsset asset,
            string customerKind,
            string customerId,
            string customerName,
            string dealerBusinessId,
            string dealerName,
            int replacementCostCents,
            int agreedCreditCents,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (asset == null) { diagnostics.Add("ImplementDealerTradeInBook: no asset supplied."); return null; }
            if (string.IsNullOrWhiteSpace(asset.AssetId))
            { diagnostics.Add("ImplementDealerTradeInBook: the asset has no identity — registry assets only."); return null; }
            if (string.IsNullOrWhiteSpace(customerId))
            { diagnostics.Add("ImplementDealerTradeInBook: trade-ins name their customer."); return null; }
            if (!string.Equals(asset.OwnerKind, customerKind, StringComparison.Ordinal) ||
                !string.Equals(asset.OwnerId, customerId, StringComparison.Ordinal))
            {
                diagnostics.Add(
                    $"ImplementDealerTradeInBook: {customerId} does not own {asset.AssetId} " +
                    $"(owner is {asset.OwnerKind}:{asset.OwnerId}) — no trading what you don't hold.");
                return null;
            }
            if (asset.IsReserved)
            {
                diagnostics.Add(
                    $"ImplementDealerTradeInBook: {asset.AssetId} is reserved by {asset.ReservedBy} — " +
                    "no double-dealing (Tech X §3.8).");
                return null;
            }
            if (replacementCostCents <= 0)
            {
                diagnostics.Add(
                    $"ImplementDealerTradeInBook: no replacement price data for kind '{asset.Kind}' — " +
                    "the valuation service needs real price data; nothing is invented.");
                return null;
            }
            if (agreedCreditCents < 0)
            { diagnostics.Add("ImplementDealerTradeInBook: the agreed credit cannot be negative."); return null; }

            int valuation = DealerValuationCents(asset, replacementCostCents);

            string transferRejection = asset.TransferOwnership(
                "business", dealerBusinessId,
                $"trade-in from {customerName ?? customerId}", dayIndex);
            if (transferRejection != null)
            {
                diagnostics.Add($"ImplementDealerTradeInBook: {transferRejection}");
                return null;
            }

            var record = new DealerTradeInRecord
            {
                TradeInId = idRegistry != null ? idRegistry.Allocate(EntityKind.Contract) : EntityId.Invalid,
                AssetId = asset.AssetId,
                Kind = asset.Kind,
                DisplayName = asset.DisplayName,
                Condition01AtTradeIn = asset.Condition01,
                CustomerKind = customerKind ?? string.Empty,
                CustomerId = customerId,
                CustomerName = customerName ?? string.Empty,
                DealerBusinessId = dealerBusinessId ?? string.Empty,
                DealerName = dealerName ?? string.Empty,
                ReplacementCostCents = replacementCostCents,
                DealerValuationCents = valuation,
                AgreedCreditCents = agreedCreditCents,
                AppliedCreditCents = 0,
                DayIndex = dayIndex,
                Status = DealerTradeInStatus.Recorded,
            };
            tradeIns[TradeInKeyOf(record.TradeInId)] = record;

            diagnostics.Add(
                $"ImplementDealerTradeInBook: took {asset.DisplayName} ({asset.AssetId}) in trade from " +
                $"{customerName ?? customerId} — dealer valuation {valuation}c (condition-weighted, Canon §11.6), " +
                $"agreed credit {agreedCreditCents}c (negotiated, recorded).");
            if (agreedCreditCents != valuation)
            {
                diagnostics.Add(
                    "ImplementDealerTradeInBook: agreed credit differs from dealer valuation — " +
                    "that is the parties' business (Canon §7.2D pricing freedom).");
            }
            return record;
        }

        /// <summary>
        /// Applies trade-in credit against a new implement sale as a recorded
        /// amount. Refuses when the trade-in is unknown, the credit would
        /// exceed what was agreed, or the amount is not positive. The credit
        /// offsets the sale's consideration — the ImplementSale's notes stand
        /// as recorded; the application is the offset record.
        /// </summary>
        public string ApplyCreditToSale(
            string tradeInKey, EntityId saleId, int creditCents,
            int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            DealerTradeInRecord record = Find(tradeInKey);
            if (record == null) return $"ImplementDealerTradeInBook: no trade-in '{tradeInKey}'.";
            if (creditCents <= 0)
                return "ImplementDealerTradeInBook: the applied credit must be positive.";
            if (creditCents > record.RemainingCreditCents)
            {
                return $"ImplementDealerTradeInBook: {creditCents}c exceeds the remaining agreed credit " +
                       $"({record.RemainingCreditCents}c of {record.AgreedCreditCents}c) — the book never mints credit.";
            }

            applications.Add(new TradeInCreditApplication
            {
                TradeInId = record.TradeInId,
                SaleId = saleId,
                CreditCents = creditCents,
                DayIndex = dayIndex,
            });
            record.AppliedCreditCents += creditCents;
            if (record.RemainingCreditCents <= 0) record.Status = DealerTradeInStatus.CreditApplied;

            diagnostics.Add(
                $"ImplementDealerTradeInBook: applied {creditCents}c trade-in credit from " +
                $"{record.AssetId} against sale {saleId.Kind}:{saleId.Id} " +
                $"({record.RemainingCreditCents}c remaining).");
            return null;
        }

        // ---------- save DTO (inside the owning book class) ----------

        [Serializable]
        public sealed class ImplementDealerTradeInBookDto
        {
            public List<DealerTradeInRecord> TradeIns = new List<DealerTradeInRecord>();
            public List<TradeInCreditApplication> Applications = new List<TradeInCreditApplication>();
        }

        public ImplementDealerTradeInBookDto ToSaveDto()
        {
            var dto = new ImplementDealerTradeInBookDto();
            foreach (var kv in tradeIns) dto.TradeIns.Add(kv.Value);
            dto.Applications.AddRange(applications);
            return dto;
        }

        public void LoadFromSaveDto(ImplementDealerTradeInBookDto dto)
        {
            tradeIns.Clear();
            applications.Clear();
            if (dto == null) return;
            foreach (DealerTradeInRecord r in dto.TradeIns)
            {
                if (r == null || r.TradeInId.Equals(EntityId.Invalid)) continue;
                tradeIns[TradeInKeyOf(r.TradeInId)] = r;
            }
            if (dto.Applications != null) applications.AddRange(dto.Applications);
        }
    }
}
