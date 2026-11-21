using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Lane 9e: marker for a goods supplier that is a fuel dealer storefront.
    /// The production adapter (Series2FuelDealerSupplierAdapter) implements
    /// this so seller-knowledge seeding can recognise the dealer without
    /// depending on the adapter type (which lives outside the harness
    /// compile closure). Precedent: the adapter already implements
    /// Population interfaces (IItemResolvingSupplier, IOffersTradeCredit).
    /// </summary>
    public interface IFuelDealerSupplier : IGoodsSupplier
    {
    }

    /// <summary>
    /// Lane 9e: seeds local residents' seller knowledge for the fuel dealer,
    /// mirroring SeedLocalBakeryKnowledge (SimulationDrivers) exactly:
    /// live residents only, a specifically identified building-backed
    /// supplier, idempotent through ShopperSellerKnowledge. The dealer was
    /// fully wired as a supplier but had no seeding, so no household ever
    /// learned of it and it had no live retail customers.
    /// </summary>
    public static class FuelDealerKnowledgeSeeding
    {
        /// <summary>A fuel dealer storefront with a real business id and a real building location.</summary>
        public static bool IsEligibleLocalFuelDealerSupplier(IGoodsSupplier supplier)
        {
            if (supplier is not IFuelDealerSupplier dealer
                || string.IsNullOrWhiteSpace(dealer.SupplierBusinessId)
                || string.IsNullOrWhiteSpace(dealer.LocationId)
                || !dealer.LocationId.StartsWith("building:", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        /// <summary>Idempotent, narrowly scoped local-dealer knowledge policy.</summary>
        public static void SeedLocalFuelDealerKnowledge(
            IEnumerable<PersonState> people,
            IEnumerable<IGoodsSupplier> suppliers,
            ShopperSellerKnowledge knowledge,
            int dayIndex)
        {
            if (people == null || suppliers == null || knowledge == null) return;
            foreach (PersonState person in people)
            {
                if (person == null || person.id < 0 || person.deathDayIndex >= 0 || person.isOffMap) continue;
                foreach (IGoodsSupplier supplier in suppliers)
                {
                    if (!IsEligibleLocalFuelDealerSupplier(supplier)) continue;
                    knowledge.LearnSupplier(person.id, supplier.SupplierBusinessId,
                        "local fuel dealer", dayIndex);
                }
            }
        }
    }
}
