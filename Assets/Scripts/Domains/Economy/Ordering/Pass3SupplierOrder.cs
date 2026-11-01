// Pass 3 (economy ordering): supplier choice, multi-product orders,
// freight, minimums, reorder vs new order, and price randomisation.
// Plain C#, no UnityEngine references, Roslyn-testable. This model
// produces VALIDATED ORDER REQUESTS; it does not pay, deliver, or
// create stock by itself. A caller maps each request line onto the
// existing OffMapNamedSkuProcurementService (one SKU per procurement
// order, freight surcharge = the allocated freight on that line) and
// ExternalMarketAuthority, exactly as Pass XL did for flour. Do NOT
// invent a second procurement authority, and do NOT wire a driver
// here: drivers, daily ticks, and scene wiring remain Codex work.
//
// Supplier model, from Kennedy's vision:
//   - Off-map for the slice (only three businesses on-map); on-map
//     suppliers come later, when more towns/businesses supply things.
//     This model is supplier-location agnostic: a supplier record
//     carries a distance, and an on-map supplier is simply a supplier
//     with distance 0 and seller-freight 0. No code path here assumes
//     off-map, and no on-map supplier is faked into existence.
//   - Different suppliers may sell the same goods at different
//     prices and distances (e.g. cigarettes 100 miles away vs 200
//     miles away, a cent or five cents a pack apart). Freight is
//     usually the biggest factor, which is why quotes are compared
//     on DELIVERED total, not goods price alone. This model quotes;
//     it does not pick a cheapest supplier for the player.
//   - Freight options: seller brings it (SellerFreight, distance x
//     caller cents-per-mile, explicit surcharge), you pick it up
//     yourself (SelfCollect, freight 0 in the quote, RequiresJourney
//     true, and the order is AwaitingCollection, never delivered),
//     you commission someone / third-party freight (FreightBusiness,
//     seller freight + caller commission). Some suppliers are
//     pickup-only: for them any non-SelfCollect option is refused.
//     Freight is a caller-supplied surcharge/commission, never a free
//     wagon, and a wagon-journey version of freight is a later scope
//     decision, not something this model fakes.
//   - Minimums are refusals, never silent round-ups. If a supplier
//     requires a minimum amount, quantity, or poundage, an order
//     below it is refused with every violated minimum named. The
//     caller must add goods deliberately; this model never charges
//     for unasked goods.
//   - Grouping inclines, never hard-locks. A draft with lines from
//     more than one supplier gets a consolidation note suggesting a
//     single supplier, but it is NOT refused: categories are not
//     hard-locked to suppliers, and plausible groupings (tomatoes /
//     potatoes / carrots together; cigars / cigarettes together;
//     shirts / pants together and possibly shoes, since a shirt and
//     pants maker totally could make boots; crackers vs boots/shoes
//     as different suppliers) are supplier data, not code rules.
//   - Reorder vs new order are different actions. Reorder copies a
//     previous order's supplier and lines, then RE-QUOTES every line
//     at current offer prices; a price change is visible, never
//     hidden, and a line the supplier no longer offers blocks the
//     reorder. A new order starts from a fresh supplier choice.
//   - Randomisation: prices may vary by supplier with pricing that
//     makes sense. The randomiser here is deterministic on a seed
//     string (same seed, same price, so tests are reproducible) and
//     its variance is a caller/test parameter, never canon.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Ordering
{
    public enum Pass3FreightOption
    {
        Unspecified = 0,
        SellerFreight = 1,
        SelfCollect = 2,
        FreightBusiness = 3,
    }

    /// <summary>One SKU a supplier offers, at a caller-supplied unit price and weight. Weight is per unit, in pounds, caller data.</summary>
    public sealed class Pass3SkuOffer
    {
        public string SkuId = string.Empty;
        public int UnitPriceCents;
        public double UnitWeightLb;
    }

    /// <summary>
    /// A supplier: id, name, distance in miles, the SKUs it offers,
    /// and its minimum order (amount, quantity, or poundage; any
    /// minimum of 0 means that minimum is not set). IsPickupOnly
    /// suppliers accept SelfCollect only.
    /// </summary>
    public sealed class Pass3SupplierOffer
    {
        public string SupplierId = string.Empty;
        public string SupplierName = string.Empty;
        public int DistanceMiles;
        public bool IsPickupOnly;
        public int MinimumAmountCents;
        public int MinimumQuantityUnits;
        public double MinimumPoundageLb;
        public List<Pass3SkuOffer> SkuOffers = new List<Pass3SkuOffer>();

        public Pass3SkuOffer FindSku(string skuId)
        {
            if (string.IsNullOrWhiteSpace(skuId) || SkuOffers == null) return null;
            foreach (Pass3SkuOffer offer in SkuOffers)
            {
                if (string.Equals(offer.SkuId, skuId.Trim(), StringComparison.OrdinalIgnoreCase)) return offer;
            }
            return null;
        }
    }

    public sealed class Pass3OrderLine
    {
        public string SkuId = string.Empty;
        public int QuantityUnits;
        public int UnitPriceCents;
        public double UnitWeightLb;

        public int GoodsCents()
        {
            return QuantityUnits * UnitPriceCents;
        }

        public double WeightLb()
        {
            return QuantityUnits * UnitWeightLb;
        }
    }

    /// <summary>
    /// One line of a placed order, after freight allocation: the exact
    /// request a caller maps onto a procurement-service order for
    /// that SKU. FreightAllocatedCents sums to the draft's total
    /// freight exactly; the last line absorbs integer rounding.
    /// </summary>
    public sealed class Pass3OrderRequest
    {
        public string SupplierId = string.Empty;
        public string SkuId = string.Empty;
        public int QuantityUnits;
        public int GoodsCents;
        public int FreightAllocatedCents;
        public int TotalCents;
        public Pass3FreightOption FreightOption;
        public bool RequiresJourney;
        public string Status = string.Empty; // OrderedAwaitingArrival or AwaitingCollection
    }

    /// <summary>
    /// A draft order: one supplier, one or more product lines, one
    /// freight option, and the ordering session that guards automatic
    /// player ordering. A draft creates no stock, no cash movement,
    /// and no procurement order until Place() validates it.
    /// </summary>
    public sealed class Pass3OrderDraft
    {
        public Pass3SupplierOffer Supplier;
        public List<Pass3OrderLine> Lines = new List<Pass3OrderLine>();
        public Pass3FreightOption FreightOption = Pass3FreightOption.Unspecified;
        public int FreightCentsPerMile;
        public int FreightBusinessCommissionCents;
        public Pass3ActorKind Actor = Pass3ActorKind.Player;
        public bool IsAutomatic;
        public bool DelegationAllowed;

        public int GoodsTotalCents()
        {
            int total = 0;
            foreach (Pass3OrderLine line in Lines) total += line.GoodsCents();
            return total;
        }

        public int TotalQuantityUnits()
        {
            int total = 0;
            foreach (Pass3OrderLine line in Lines) total += line.QuantityUnits;
            return total;
        }

        public double TotalWeightLb()
        {
            double total = 0.0;
            foreach (Pass3OrderLine line in Lines) total += line.WeightLb();
            return total;
        }

        public int FreightTotalCents()
        {
            if (Supplier == null) return 0;
            switch (FreightOption)
            {
                case Pass3FreightOption.SellerFreight:
                    return Math.Max(0, Supplier.DistanceMiles) * Math.Max(0, FreightCentsPerMile);
                case Pass3FreightOption.FreightBusiness:
                    return Math.Max(0, Supplier.DistanceMiles) * Math.Max(0, FreightCentsPerMile)
                        + Math.Max(0, FreightBusinessCommissionCents);
                default:
                    return 0;
            }
        }

        /// <summary>Add a line at the supplier's current offer price. Refuses unknown SKUs, non-positive quantities, and duplicate SKU lines (combine lines deliberately, do not let a draft silently merge them).</summary>
        public bool AddLine(string skuId, int quantityUnits, List<string> diag)
        {
            if (Supplier == null)
            {
                if (diag != null) diag.Add("Pass3Ordering: order draft needs a supplier before lines can be added.");
                return false;
            }
            Pass3SkuOffer offer = Supplier.FindSku(skuId);
            if (offer == null)
            {
                if (diag != null) diag.Add("Pass3Ordering: supplier '" + Supplier.SupplierId + "' does not offer SKU '" + skuId + "'.");
                return false;
            }
            if (quantityUnits <= 0)
            {
                if (diag != null) diag.Add("Pass3Ordering: quantity for '" + skuId + "' must be positive.");
                return false;
            }
            foreach (Pass3OrderLine existing in Lines)
            {
                if (string.Equals(existing.SkuId, skuId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    if (diag != null) diag.Add("Pass3Ordering: SKU '" + skuId + "' is already on this draft; combine the quantities into one line.");
                    return false;
                }
            }
            Lines.Add(new Pass3OrderLine
            {
                SkuId = skuId.Trim(),
                QuantityUnits = quantityUnits,
                UnitPriceCents = offer.UnitPriceCents,
                UnitWeightLb = offer.UnitWeightLb,
            });
            return true;
        }

        /// <summary>
        /// Validate the whole draft. Every failure is reported; the
        /// draft is placed only when this returns true. Minimums are
        /// checked as refusals, never rounded up.
        /// </summary>
        public bool Validate(List<string> diag)
        {
            bool ok = true;
            if (Supplier == null || string.IsNullOrWhiteSpace(Supplier.SupplierId))
            {
                if (diag != null) diag.Add("Pass3Ordering: order draft needs a supplier.");
                return false;
            }
            if (Lines == null || Lines.Count == 0)
            {
                if (diag != null) diag.Add("Pass3Ordering: order draft has no lines.");
                return false;
            }
            if (FreightOption == Pass3FreightOption.Unspecified)
            {
                if (diag != null) diag.Add("Pass3Ordering: order draft needs a freight option.");
                ok = false;
            }
            if (Supplier.IsPickupOnly && FreightOption != Pass3FreightOption.SelfCollect)
            {
                if (diag != null) diag.Add("Pass3Ordering: supplier '" + Supplier.SupplierId + "' is pickup-only; seller or third-party freight is not offered.");
                ok = false;
            }
            if (IsAutomatic)
            {
                if (Actor == Pass3ActorKind.Player)
                {
                    if (diag != null) diag.Add("Pass3Ordering: player ordering is never automatic; a player order must be placed deliberately.");
                    ok = false;
                }
                else if (!DelegationAllowed)
                {
                    if (diag != null) diag.Add("Pass3Ordering: automatic ordering by an employee delegate needs delegation permission.");
                    ok = false;
                }
            }
            if (Supplier.MinimumAmountCents > 0 && GoodsTotalCents() < Supplier.MinimumAmountCents)
            {
                if (diag != null) diag.Add("Pass3Ordering: order below supplier minimum amount: goods " + GoodsTotalCents() + "c, minimum " + Supplier.MinimumAmountCents + "c.");
                ok = false;
            }
            if (Supplier.MinimumQuantityUnits > 0 && TotalQuantityUnits() < Supplier.MinimumQuantityUnits)
            {
                if (diag != null) diag.Add("Pass3Ordering: order below supplier minimum quantity: " + TotalQuantityUnits() + " units, minimum " + Supplier.MinimumQuantityUnits + ".");
                ok = false;
            }
            if (Supplier.MinimumPoundageLb > 0 && TotalWeightLb() + 0.000001 < Supplier.MinimumPoundageLb)
            {
                if (diag != null) diag.Add("Pass3Ordering: order below supplier minimum poundage: " + TotalWeightLb() + " lb, minimum " + Supplier.MinimumPoundageLb + " lb.");
                ok = false;
            }
            return ok;
        }

        /// <summary>
        /// Place the draft: validate, then return one request per line
        /// with freight allocated so the allocations sum exactly to
        /// the draft freight total. Returns null on any refusal, and
        /// creates no procurement order, cash movement, or stock.
        /// </summary>
        public List<Pass3OrderRequest> Place(List<string> diag)
        {
            if (!Validate(diag)) return null;
            int freightTotal = FreightTotalCents();
            var requests = new List<Pass3OrderRequest>();
            int allocatedSoFar = 0;
            for (int i = 0; i < Lines.Count; i++)
            {
                Pass3OrderLine line = Lines[i];
                int allocated;
                if (i == Lines.Count - 1)
                {
                    allocated = freightTotal - allocatedSoFar;
                }
                else if (GoodsTotalCents() > 0)
                {
                    allocated = (int)Math.Floor((double)freightTotal * line.GoodsCents() / GoodsTotalCents());
                    allocatedSoFar += allocated;
                }
                else
                {
                    allocated = 0;
                }
                requests.Add(new Pass3OrderRequest
                {
                    SupplierId = Supplier.SupplierId,
                    SkuId = line.SkuId,
                    QuantityUnits = line.QuantityUnits,
                    GoodsCents = line.GoodsCents(),
                    FreightAllocatedCents = allocated,
                    TotalCents = line.GoodsCents() + allocated,
                    FreightOption = FreightOption,
                    RequiresJourney = FreightOption == Pass3FreightOption.SelfCollect,
                    Status = FreightOption == Pass3FreightOption.SelfCollect ? "AwaitingCollection" : "OrderedAwaitingArrival",
                });
            }
            return requests;
        }

        /// <summary>
        /// Reorder: same supplier, same SKUs and quantities as a
        /// previous placed order, re-quoted at the supplier's CURRENT
        /// offer prices. A line the supplier no longer offers, or a
        /// supplier that cannot be found in the current offers, blocks
        /// the reorder entirely, so a price or availability change is
        /// never hidden behind a previous order's prices.
        /// </summary>
        public static Pass3OrderDraft ReorderFrom(
            Pass3SupplierOffer currentSupplier, List<Pass3OrderRequest> previousRequests,
            Pass3FreightOption freightOption, int freightCentsPerMile, int freightBusinessCommissionCents,
            Pass3ActorKind actor, bool isAutomatic, bool delegationAllowed, List<string> diag)
        {
            if (currentSupplier == null || previousRequests == null || previousRequests.Count == 0)
            {
                if (diag != null) diag.Add("Pass3Ordering: reorder needs a current supplier and a previous order to copy.");
                return null;
            }
            var draft = new Pass3OrderDraft
            {
                Supplier = currentSupplier,
                FreightOption = freightOption,
                FreightCentsPerMile = freightCentsPerMile,
                FreightBusinessCommissionCents = freightBusinessCommissionCents,
                Actor = actor,
                IsAutomatic = isAutomatic,
                DelegationAllowed = delegationAllowed,
            };
            foreach (Pass3OrderRequest previous in previousRequests)
            {
                if (!draft.AddLine(previous.SkuId, previous.QuantityUnits, diag)) return null;
            }
            return draft;
        }
    }

    /// <summary>
    /// Grouping guidance, never a hard lock: a set of drafts (or one
    /// draft's lines across suppliers, represented here as drafts)
    /// gets a consolidation note, not a refusal, when it spans more
    /// than one supplier.
    /// </summary>
    public static class Pass3SupplierGrouping
    {
        public static string ConsolidationNote(List<Pass3OrderDraft> drafts)
        {
            if (drafts == null || drafts.Count == 0) return "Pass3Ordering: no orders to group.";
            var suppliers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Pass3OrderDraft draft in drafts)
            {
                if (draft != null && draft.Supplier != null && !string.IsNullOrWhiteSpace(draft.Supplier.SupplierId))
                    suppliers.Add(draft.Supplier.SupplierId);
            }
            if (suppliers.Count <= 1) return "Pass3Ordering: single-supplier order.";
            return "Pass3Ordering: multi-supplier order (" + suppliers.Count + " suppliers); consider consolidating onto one supplier to avoid many small orders, but this is guidance, not a refusal.";
        }
    }

    /// <summary>
    /// Deterministic price variation: same base price, variance, and
    /// seed string always produce the same price, so quotes are
    /// reproducible in tests and playtests. Variance is a caller/test
    /// parameter, never canon, and the result never goes negative.
    /// </summary>
    public static class Pass3PriceRandomiser
    {
        public static int VariedPriceCents(int basePriceCents, double variancePercent01, string seed)
        {
            if (basePriceCents <= 0) return 0;
            if (variancePercent01 <= 0) return basePriceCents;
            uint hash = 2166136261u;
            string text = seed ?? string.Empty;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            double unit = (hash % 10000) / 10000.0; // 0..0.9999
            double factor = (unit * 2.0 - 1.0) * variancePercent01; // -variance..+variance
            int varied = (int)Math.Round(basePriceCents * (1.0 + factor), MidpointRounding.AwayFromZero);
            return Math.Max(0, varied);
        }
    }
}
