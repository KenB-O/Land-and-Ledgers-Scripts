using System.Collections.Generic;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// EQP-5: fuel yard equipment profile (Kennedy decision 2026-10-03). The town's
    /// fuel yard — "turns slabs, offcuts, and cordwood into reliable household
    /// fuel supply" (existing in-game profile). In 1870 everyone burns wood or
    /// coal for heat and cooking, so this is who keeps the town warm.
    ///
    /// Profiled by analogy with the feed merchant (the canon has no fuel-dealer
    /// profile): sold by weight/cord, so the platform scale is the hard gate.
    /// Forge coal stays on the EQU-1 import path (specialty fuel); the yard may
    /// retail imported coal later as a capability.
    /// </summary>
    public static class FuelYardProfile
    {
        /// <summary>Equipment kinds the yard needs (as EquipmentAsset.Kind strings).</summary>
        public static readonly List<string> EquipmentKinds = new List<string>
        {
            "platform-scale",  // hard gate: fuel is sold by weight/cord
            "storage-bins",    // sheds/bins for graded fuel
            "buck-saw",        // cutting cordwood/slabs to stove length
            "delivery-wagon",  // household delivery
        };

        /// <summary>Supply links: input → honest source. Every link names its supplier.</summary>
        public static readonly List<(string input, string source)> SupplyLinks =
            new List<(string input, string source)>
        {
            ("sawmill slabs/offcuts", "sawmill, purchased via the embodied-purchase executor (T1A)"),
            ("cordwood", "local woodcutters / timber chain when built (deferred); off-map import meanwhile"),
            ("forge coal (retail)", "EQU-1 import path, retailed as a later capability — not stocked today"),
        };

        /// <summary>Demand: who buys fuel, and when.</summary>
        public static readonly List<string> DemandNotes = new List<string>
        {
            "Households: winter heating — the canon tracks firewood cords seasonally; demand spikes in winter.",
            "Bakery ovens, boarding-house ranges, and other stove-heavy businesses: year-round baseline.",
            "Blacksmith forge: stays on imported forge coal (specialty fuel), not yard wood.",
        };

        /// <summary>Equipment requirement codes for the yard's tasks.</summary>
        public static string RequirementForTask(string taskId)
        {
            switch (taskId)
            {
                case "sell-fuel": return EquipmentRequirementCodes.Asset("platform-scale");
                case "cut-stovewood": return EquipmentRequirementCodes.Asset("buck-saw");
                case "deliver-fuel": return EquipmentRequirementCodes.Asset("delivery-wagon");
                default: return null;
            }
        }
    }
}
