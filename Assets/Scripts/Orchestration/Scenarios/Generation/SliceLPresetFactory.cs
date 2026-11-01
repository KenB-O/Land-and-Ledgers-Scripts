using System;

namespace LandLedgers.Orchestration.Scenarios.Generation
{
    /// <summary>
    /// Pass V slice L (Worker 1): the authored slice-L scenario preset —
    /// "Hearth and Ledger". Configurable start date, population range honoring
    /// the Canon 55-95 calibration, fixed + variable businesses, household
    /// composition, assets/property/credit policy, and an authored player start.
    ///
    /// Authored in code (not a Unity .asset) so the Roslyn harness can build
    /// and verify it; the Unity editor path authors the same fields on
    /// ScenarioAsset.StartingWorld and converts via the preset bridge.
    /// </summary>
    public static class SliceLPresetFactory
    {
        public const string PresetId = "slice-l-hearth-and-ledger";
        public const string ScenarioId = "first-ledger-slice-l";

        /// <summary>
        /// Builds the slice-L preset. The start date is configurable; every
        /// other authored value is fixed by the scenario author.
        /// </summary>
        public static ScenarioPreset Build(int startYear = 1880, int startMonth = 3, int startDay = 1)
        {
            var preset = new ScenarioPreset();
            preset.SetIdentity(PresetId, "Slice L: Hearth and Ledger", "1.0.0");

            StartingWorldConfiguration world = preset.StartingWorld;
            world.SetScenarioId(ScenarioId);
            world.SetDefinitionVersion(ScenarioPresetLibrary.CurrentDefinitionVersion);
            world.SetStartDate(startYear, startMonth, startDay);
            world.SetPopulationRange(60, 78, 90);

            world.EditableRequiredBusinessIds.Add("general-store");
            world.EditableRequiredBusinessIds.Add("blacksmith-forge");
            world.EditableRequiredBusinessIds.Add("saloon");

            world.EditableFixedBusinesses.Add(new ScenarioFixedBusiness(
                "general-store", "The First Ledger General Store", "general-store", 250000, "npc"));
            world.EditableFixedBusinesses.Add(new ScenarioFixedBusiness(
                "blacksmith-forge", "Hale & Sons Forge", "blacksmith", 120000, "npc"));
            world.EditableFixedBusinesses.Add(new ScenarioFixedBusiness(
                "saloon", "The Copper Queen Saloon", "saloon", 90000, "npc"));

            ScenarioPlayerStart playerStart = world.EditablePlayerStart;
            playerStart.SetHouseholdName("Millward");
            playerStart.SetFounderCount(2);
            playerStart.SetStartingCashCents(250000);
            playerStart.SetStartingDebtCents(0);
            playerStart.SetProfessionId("shopkeeper");
            playerStart.SetOwnsHomeBuilding(true);

            ScenarioEconomyPolicy policy = world.EditableEconomyPolicy;
            policy.SetCashBand(25000, 150000);
            policy.SetEmploymentTargetRate(0.75f);
            policy.SetDebtPolicy(0.25f, 150000);

            return preset;
        }

        /// <summary>
        /// A deliberately broken variant used to prove that validation fails
        /// loudly instead of inventing world state to compensate.
        /// </summary>
        public static ScenarioPreset BuildInvalidPopulationRange()
        {
            ScenarioPreset preset = Build();
            preset.StartingWorld.SetPopulationRange(10, 30, 40);
            return preset;
        }
    }
}
