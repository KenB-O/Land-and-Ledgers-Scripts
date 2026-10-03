using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Businesses.Butcher;
using LandLedgers.Economy.Farming;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Farming.Integration
{
    /// <summary>
    /// FVS-4: the cull loop that closes the circle — spent dairy cow →
    /// designated cull → sold to the butcher (FRM-2) → slaughtered (BIZ-4) →
    /// wholesale cuts → general store butchery counter (FRM-3). Every step is
    /// an ordinary transaction with provenance; the animal's ID is retired,
    /// never reused (Tech X §3.2).
    /// </summary>
    public static class CullFlow
    {
        /// <summary>
        /// Designates a cow as cull and sells her to the butcher in one honest
        /// motion. The designation reason is recorded (age, dried off, injury).
        /// Refuses animals that are not farm-owned cattle.
        /// </summary>
        public static FarmLivestockSale CullAndSellCow(
            AnimalRegistry registry,
            EntityId cowId,
            string reason,
            string farmBusinessId,
            string farmBusinessName,
            ButcherRuntime butcher,
            string butcherBusinessId,
            int priceCents,
            int dayIndex,
            HouseholdLedger sellerLedger,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (registry == null)
            {
                diagnostics.Add("CullFlow: no animal registry.");
                return null;
            }

            AnimalState cow = registry.GetAnimal(cowId);
            if (cow == null || !cow.IsActive)
            {
                diagnostics.Add($"CullFlow: unknown or inactive animal {cowId}.");
                return null;
            }
            if (cow.Species != AnimalSpecies.Cattle)
            {
                diagnostics.Add($"CullFlow: {cowId} is {cow.Species}, not cattle — cull flow is cattle-only in this slice.");
                return null;
            }
            if (cow.OwnerKind != AnimalOwnerKind.Business || cow.OwnerId != "farm:" + farmBusinessId)
            {
                diagnostics.Add($"CullFlow: {cowId} is not owned by farm '{farmBusinessId}' — cannot cull another's animal.");
                return null;
            }

            cow.CommercialStatus = AnimalCommercialStatus.Cull;
            cow.Disposition = $"culled: {reason} (day {dayIndex})";

            FarmLivestockSale sale = FarmLivestockMarket.ExecuteSale(
                registry, cowId, farmBusinessId, farmBusinessName,
                butcher, butcherBusinessId, priceCents, dayIndex, sellerLedger, diagnostics);

            if (sale != null)
            {
                diagnostics.Add($"CullFlow: cow {cowId} culled ({reason}) and sold to the butcher — " +
                    "slaughter, carcass balance, and wholesale flow through BIZ-4/FRM-3.");
            }
            return sale;
        }
    }
}
