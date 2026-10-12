using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass V P2-2: the hardship asset-sale port. A distressed household sells
    /// surplus non-essential goods to another REAL household that has an open
    /// need for them. No guaranteed buyer: the sale only happens when a buyer
    /// household genuinely needs the item and can genuinely pay.
    /// </summary>
    public interface IHardshipAssetSalePort
    {
        /// <summary>
        /// Sells the household's surplus goods to real buyer households.
        /// Returns the total proceeds in cents (0 when nothing could be sold).
        /// </summary>
        int TrySellSurplus(int householdId, int dayIndex, List<string> diagnostics);
    }

    /// <summary>
    /// Pass V P2-2: household-to-household asset sales. Goods move as real
    /// inventory lots (conserved, provenance recorded); cash moves through an
    /// atomic ledger transfer (buyer outflow == seller inflow — no money is
    /// created). Essential consumption goods (food, fuel) are never sold: a
    /// household does not sell what it needs to survive the week.
    /// </summary>
    public sealed class HouseholdAssetSaleService
    {
        private static readonly HashSet<string> EssentialItemIds = new HashSet<string>(StringComparer.Ordinal)
        {
            HouseholdItemCatalog.FlourId,
            HouseholdItemCatalog.BreadId,
            HouseholdItemCatalog.PotatoesId,
            HouseholdItemCatalog.PreservedFoodId,
            HouseholdItemCatalog.PreservedMeatId,
            HouseholdItemCatalog.FuelWoodId,
        };

        private readonly HouseholdLedgerRegistry ledgers;
        private readonly HouseholdInventoryRegistry inventories;
        private readonly HouseholdNeedRegistry needs;
        private readonly Func<int, bool> householdExists;
        private readonly Func<IEnumerable<int>> allHouseholdIds;
        private readonly Func<string, int> pricePerUnitCents;

        public HouseholdAssetSaleService(
            HouseholdLedgerRegistry ledgers,
            HouseholdInventoryRegistry inventories,
            HouseholdNeedRegistry needs,
            Func<int, bool> householdExists,
            Func<IEnumerable<int>> allHouseholdIds,
            Func<string, int> pricePerUnitCents)
        {
            this.ledgers = ledgers;
            this.inventories = inventories;
            this.needs = needs;
            this.householdExists = householdExists;
            this.allHouseholdIds = allHouseholdIds;
            this.pricePerUnitCents = pricePerUnitCents;
        }

        /// <summary>
        /// Values the household's saleable surplus at distress pricing
        /// (asking 60% of the reference price). Used as the pressure-decision
        /// input; the actual sale may realize less.
        /// </summary>
        public int EvaluateSaleableValue(int householdId, List<string> diagnostics)
        {
            int total = 0;
            HouseholdInventory inventory = inventories != null ? inventories.Get(householdId) : null;
            if (inventory == null) return 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (HouseholdItemLot lot in inventory.Lots)
            {
                if (lot == null || !seen.Add(lot.ItemId)) continue;
                if (EssentialItemIds.Contains(lot.ItemId)) continue;
                int available = inventory.GetAvailableUnits(lot.ItemId);
                if (available <= 0) continue;
                int unitPrice = DistressPrice(lot.ItemId);
                if (unitPrice <= 0) continue;
                total += unitPrice * available;
            }

            return Math.Max(0, total);
        }

        /// <summary>
        /// Executes the sale: for each saleable item, finds a real buyer
        /// household with an open need for the item and a ledger that covers
        /// the price, then moves goods and cash. Returns total proceeds.
        /// </summary>
        public int SellSurplus(int sellerHouseholdId, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            HouseholdInventory sellerInventory = inventories != null ? inventories.Get(sellerHouseholdId) : null;
            if (sellerInventory == null)
            {
                diagnostics.Add($"AssetSale: no inventory for household {sellerHouseholdId} — nothing to sell.");
                return 0;
            }

            int proceeds = 0;
            // Iterate a snapshot of distinct item ids: lots mutate as we sell.
            var itemIds = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (HouseholdItemLot lot in sellerInventory.Lots)
            {
                if (lot != null && seen.Add(lot.ItemId)) itemIds.Add(lot.ItemId);
            }

            foreach (string itemId in itemIds)
            {
                if (EssentialItemIds.Contains(itemId)) continue;
                int available = sellerInventory.GetAvailableUnits(itemId);
                if (available <= 0) continue;
                int unitPrice = DistressPrice(itemId);
                if (unitPrice <= 0) continue;
                int price = available * unitPrice;

                int buyerId = FindBuyer(sellerHouseholdId, itemId, price);
                if (buyerId < 0) continue;

                HouseholdLedger buyerLedger = ledgers != null ? ledgers.Get(buyerId) : null;
                HouseholdLedger sellerLedger = ledgers != null ? ledgers.Get(sellerHouseholdId) : null;
                if (buyerLedger == null || sellerLedger == null) continue;

                // Atomicity: hold the goods, move the cash, then release the
                // goods to the buyer. Any failure unwinds the earlier legs —
                // cash and goods never drift apart.
                string commitment = $"asset sale to household {buyerId}";
                string holdRefusal = sellerInventory.HoldForCommitment(itemId, available, commitment);
                if (holdRefusal != null)
                {
                    diagnostics.Add($"AssetSale: could not hold {available}x {itemId} — {holdRefusal}");
                    continue;
                }

                // Cash: atomic ledger-to-ledger transfer. No money created.
                string transferRefusal = buyerLedger.TryTransferTo(sellerLedger, dayIndex, price,
                    $"distress purchase of {available}x {itemId} from household {sellerHouseholdId}",
                    HouseholdIncomeSource.SaleProceeds,
                    "household:" + buyerId,
                    $"asset sale: {available}x {itemId} to household {buyerId} at distress price",
                    "household:" + buyerId);
                if (transferRefusal != null)
                {
                    sellerInventory.ReleaseHold(itemId, available);
                    diagnostics.Add($"AssetSale: cash transfer for {available}x {itemId} failed — {transferRefusal} (hold released, nothing moved).");
                    continue;
                }

                string fulfillRefusal = sellerInventory.FulfillCommitment(itemId, available, commitment);
                if (fulfillRefusal != null)
                {
                    sellerLedger.TryTransferTo(buyerLedger, dayIndex, price,
                        $"reversal of failed asset sale ({available}x {itemId})",
                        HouseholdIncomeSource.OtherDocumented,
                        "asset-sale-reversal",
                        $"reversal: asset sale of {available}x {itemId} to household {buyerId} failed",
                        "household:" + sellerHouseholdId);
                    sellerInventory.ReleaseHold(itemId, available);
                    diagnostics.Add($"AssetSale: commitment fulfillment failed — {fulfillRefusal} (cash reversed).");
                    continue;
                }

                HouseholdInventory buyerInventory = inventories.GetOrCreate(buyerId);
                string addRefusal = buyerInventory.AddLot(itemId, available, dayIndex,
                    $"purchased from household {sellerHouseholdId} (distress sale, {price}c)");
                if (addRefusal != null)
                {
                    // The buyer paid and the seller's goods are gone: restore
                    // the goods to the seller and reverse the cash so neither
                    // side is left short.
                    sellerInventory.AddLot(itemId, available, dayIndex,
                        $"restored after failed asset sale to household {buyerId}");
                    sellerLedger.TryTransferTo(buyerLedger, dayIndex, price,
                        $"reversal of failed asset sale ({available}x {itemId})",
                        HouseholdIncomeSource.OtherDocumented,
                        "asset-sale-reversal",
                        $"reversal: asset sale of {available}x {itemId} to household {buyerId} failed",
                        "household:" + sellerHouseholdId);
                    diagnostics.Add($"AssetSale: buyer household {buyerId} could not receive {available}x {itemId} — {addRefusal} (goods restored, cash reversed).");
                    continue;
                }

                proceeds += price;
                diagnostics.Add($"AssetSale: household {sellerHouseholdId} sold {available}x {itemId} to household {buyerId} for {price}c.");
            }

            if (proceeds == 0)
                diagnostics.Add($"AssetSale: household {sellerHouseholdId} found no buyer for its surplus — nothing sold, nothing created.");
            return proceeds;
        }

        private int DistressPrice(string itemId)
        {
            int reference = pricePerUnitCents != null ? Math.Max(0, pricePerUnitCents(itemId)) : 0;
            if (reference <= 0) return 0;
            // Distress pricing: the seller takes 60% of the reference price.
            return Math.Max(1, reference * 60 / 100);
        }

        /// <summary>
        /// Real demand only: a buyer must be a different real household, hold
        /// an OPEN need for the item, and have a ledger covering the price.
        /// </summary>
        private int FindBuyer(int sellerHouseholdId, string itemId, int totalPriceCents)
        {
            if (allHouseholdIds == null || needs == null || ledgers == null) return -1;
            int bestBuyer = -1;
            float bestUrgency = -1f;
            foreach (int candidateId in allHouseholdIds())
            {
                if (candidateId == sellerHouseholdId) continue;
                if (householdExists != null && !householdExists(candidateId)) continue;
                bool hasOpenNeed = false;
                float urgency = 0f;
                foreach (HouseholdPurchasingNeed need in needs.GetOpenNeeds(candidateId))
                {
                    if (need == null || need.Status != PurchasingNeedStatus.Open) continue;
                    if (!string.Equals(need.ItemId, itemId, StringComparison.Ordinal)) continue;
                    hasOpenNeed = true;
                    urgency = Math.Max(urgency, need.Urgency01);
                }

                if (!hasOpenNeed) continue;
                HouseholdLedger ledger = ledgers.Get(candidateId);
                if (ledger == null || ledger.GetBalanceCents() < totalPriceCents) continue;
                if (urgency > bestUrgency)
                {
                    bestUrgency = urgency;
                    bestBuyer = candidateId;
                }
            }

            return bestBuyer;
        }
    }

    /// <summary>
    /// Pass V P2-2: production asset-sale port — the household sells surplus
    /// to real neighboring households through <see cref="HouseholdAssetSaleService"/>.
    /// </summary>
    public sealed class HouseholdToHouseholdAssetSalePort : IHardshipAssetSalePort
    {
        private readonly HouseholdAssetSaleService service;

        public HouseholdToHouseholdAssetSalePort(HouseholdAssetSaleService service)
        {
            this.service = service;
        }

        public int TrySellSurplus(int householdId, int dayIndex, List<string> diagnostics)
        {
            if (service == null)
            {
                (diagnostics ?? new List<string>()).Add("AssetSale: no sale service wired — nothing sold.");
                return 0;
            }

            return service.SellSurplus(householdId, dayIndex, diagnostics);
        }
    }
}
