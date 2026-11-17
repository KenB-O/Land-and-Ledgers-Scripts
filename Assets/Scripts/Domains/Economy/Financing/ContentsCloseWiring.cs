using System.Collections.Generic;
using LandLedgers.Population;
using UnityEngine;

namespace LandLedgers.Economy.Financing
{
    /// <summary>
    /// G-2 (W-G4 Workstream 2, expanded campaign 2026-11-16): the
    /// Unity-free contents-close wiring helpers. The acquisition market
    /// manager (a MonoBehaviour) binds its D-6/D-7 seams to these
    /// helpers; the helpers themselves take only plain state, so they
    /// are harness-compiled and harness-tested. No contract semantics
    /// live here — these resolve production state for the seams,
    /// nothing more.
    /// </summary>
    public static class ContentsCloseWiring
    {
        /// <summary>
        /// The seller's furnishing register for one building: the unique
        /// hub register holding a live binding in that building. The
        /// binding's BuildingInstanceId is the register-level building
        /// key (a BuildingFootprintAuthority carries no int instance id,
        /// so the register binding is the only place the int id is bound
        /// to a building's contents). Null when no register holds the
        /// building — the caller refuses loudly. Null also when SEVERAL
        /// registers hold bindings in the same building: two households
        /// cannot furnish the same building, so an ambiguous match is a
        /// data defect and the close must fail closed, not pick one.
        /// The ambiguity is logged loudly so it is visible instead of
        /// silent.
        /// </summary>
        public static HouseholdFurnishingRegister ResolveSellerRegisterForBuilding(
            int buildingInstanceId,
            IReadOnlyList<HouseholdFurnishingRegister> registers)
        {
            if (registers == null) return null;
            HouseholdFurnishingRegister match = null;
            int matches = 0;
            foreach (HouseholdFurnishingRegister register in registers)
            {
                if (register == null || register.Bindings == null) continue;
                bool holdsBuilding = false;
                foreach (FurnishingPlacementBinding binding in register.Bindings)
                {
                    if (binding != null && binding.BuildingInstanceId == buildingInstanceId)
                    {
                        holdsBuilding = true;
                        break;
                    }
                }
                if (holdsBuilding)
                {
                    match = register;
                    matches++;
                }
            }
            if (matches > 1)
            {
                Debug.LogWarning("ContentsCloseWiring: " + matches
                    + " furnishing registers hold bindings in building " + buildingInstanceId
                    + " — ambiguous seller, refusing (no register returned).");
                return null;
            }
            return match;
        }

        /// <summary>
        /// The player household id from population state, or -1 when no
        /// household is flagged the player's. The buyer seam resolves the
        /// hub's register for this household; the building id is not part
        /// of the buyer lookup — the buyer register is the player's
        /// register whatever building is being bought (the seam doc on
        /// BuyerFurnishingRegisterForBuilding).
        /// </summary>
        public static int FindPlayerHouseholdId(PopulationState population)
        {
            if (population == null || population.households == null) return -1;
            foreach (HouseholdState household in population.households)
            {
                if (household != null && household.isPlayerHousehold) return household.id;
            }
            return -1;
        }
    }
}
