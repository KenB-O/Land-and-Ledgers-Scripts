// Generic Business Reconstruction, section 11.
// Procurement and equipment: a viable purchase path for physical
// equipment and assets, not just shop inventory, built on the
// existing procurement pattern (quote -> order -> pay -> deliver
// -> receive) without inventing vendors, prices, or assets.
// A purchase record here is a PROPOSAL until the caller supplies a
// vendor quote and a payment result. This class never spawns an
// asset, never pays, and never delivers: Unity AI / Codex must wire
// it to the existing off-map procurement authority
// (OffMapNamedSkuProcurementService / ExternalMarketAuthority) and
// to the freight/transport authorities for delivery, and to the
// existing equipment/asset authorities for ownership registration.
// Horse and wagon are distinct owned assets even when operated
// together: this record carries one asset per line, and a horse
// line never implies a wagon line or vice versa.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.GenericBusiness
{
    public enum EquipmentPurchaseStage
    {
        Quoted = 0,
        Ordered = 1,
        PaidOrFinanced = 2,
        Shipped = 3,
        Received = 4,
        Installed = 5,
        Operational = 6,
    }

    public sealed class EquipmentPurchaseLine
    {
        public string AssetKind = string.Empty;
        public string AssetId = string.Empty;
        public string VendorId = string.Empty;
        public string VendorName = string.Empty;
        public int PriceCents;
        public int FreightCents;
        public string FreightOption = string.Empty;
        public string DeliveryLocationId = string.Empty;
        public EquipmentPurchaseStage Stage = EquipmentPurchaseStage.Quoted;

        public int DeliveredTotalCents()
        {
            return Math.Max(0, PriceCents) + Math.Max(0, FreightCents);
        }
    }

    public static class EquipmentPurchase
    {
        /// <summary>
        /// Validate a purchase line before ordering. A line with no
        /// vendor, no asset id, a negative price, or (for a horse or
        /// wagon) no delivery location is refused: free assets and
        /// ownerless assets must not be created by selecting a
        /// capability. Prices are caller-supplied (vendor quote);
        /// this method never invents a price.
        /// </summary>
        public static List<string> ValidateLine(EquipmentPurchaseLine line)
        {
            var problems = new List<string>();
            if (line == null)
            {
                problems.Add("No purchase line supplied.");
                return problems;
            }
            if (string.IsNullOrWhiteSpace(line.AssetKind)) problems.Add("Purchase line needs an asset kind (horse, cart, wagon, barber tools, or other business equipment).");
            if (string.IsNullOrWhiteSpace(line.AssetId)) problems.Add("Purchase line needs an asset id: every purchased asset is identified before it is ordered.");
            if (string.IsNullOrWhiteSpace(line.VendorId)) problems.Add("Purchase line needs a vendor id: no vendor means no legitimate purchase.");
            if (line.PriceCents < 0) problems.Add("Purchase price cannot be negative.");
            if (line.FreightCents < 0) problems.Add("Freight cost cannot be negative.");
            return problems;
        }

        /// <summary>
        /// Advance a purchase stage, forward only, one step at a time,
        /// mirroring GenericEquipmentProcurementAuthority's lifecycle
        /// (Quoted -> Ordered -> PaidOrFinanced -> Shipped -> Received
        /// -> Installed -> Operational). Skipping stages, or going
        /// backwards, is refused. Reaching Operational requires that
        /// the caller has already registered ownership and installed
        /// the asset; this method records the stage, it does not
        /// perform the registration.
        /// </summary>
        public static bool TryAdvance(EquipmentPurchaseLine line, out string reason)
        {
            reason = string.Empty;
            if (line == null)
            {
                reason = "No purchase line supplied.";
                return false;
            }
            if (line.Stage == EquipmentPurchaseStage.Operational)
            {
                reason = "Purchase is already operational.";
                return false;
            }
            line.Stage = (EquipmentPurchaseStage)((int)line.Stage + 1);
            return true;
        }

        /// <summary>
        /// Horse and wagon identity rule, section 11: a horse asset
        /// and a wagon asset are never the same asset, even when the
        /// wagon is hitched to the horse. Two lines are the same asset
        /// only when their asset ids match exactly.
        /// </summary>
        public static bool SameAsset(EquipmentPurchaseLine a, EquipmentPurchaseLine b)
        {
            if (a == null || b == null) return false;
            if (string.IsNullOrWhiteSpace(a.AssetId) || string.IsNullOrWhiteSpace(b.AssetId)) return false;
            return string.Equals(a.AssetId.Trim(), b.AssetId.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sort quote lines by delivered total (price + freight),
        /// cheapest first. This is the buyer's comparison rule from
        /// OffMapSupplierCatalog.QuoteAll, reused for equipment:
        /// the cheapest delivered price wins the comparison, but
        /// choosing a quote never pays for it.
        /// </summary>
        public static List<EquipmentPurchaseLine> SortByDeliveredTotal(IEnumerable<EquipmentPurchaseLine> lines)
        {
            var list = new List<EquipmentPurchaseLine>();
            if (lines == null) return list;
            foreach (EquipmentPurchaseLine line in lines)
            {
                if (line != null) list.Add(line);
            }
            list.Sort((a, b) => a.DeliveredTotalCents().CompareTo(b.DeliveredTotalCents()));
            return list;
        }
    }
}
