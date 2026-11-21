using System.Collections.Generic;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// Heating lane (Nov 20 2026): the building thermal model. Fuel burn is
    /// driven by the indoor-outdoor differential and the building's heat
    /// loss (size, storeys, insulation tier, stove tier), not a flat rate.
    /// No test claims Unity compilation or PlayMode.
    /// </summary>
    [TestFixture]
    public sealed class HouseholdThermalModelTests
    {
        private static HouseholdThermalModel.HouseholdThermalProfile Profile(
            int members = 4,
            float floorAreaM2 = 60f,
            int storeys = 1,
            HouseholdHeatRetentionTier retention = HouseholdHeatRetentionTier.Basic,
            HouseholdStoveTier stove = HouseholdStoveTier.Basic)
        {
            return new HouseholdThermalModel.HouseholdThermalProfile
            {
                FloorAreaM2 = floorAreaM2,
                Storeys = storeys,
                RetentionTier = retention,
                StoveTier = stove,
                MemberCount = members
            };
        }

        [Test]
        public void TemperatureFactor_IsZeroAtOrAboveHeatingBase_AndOneAtJanuaryMean()
        {
            Assert.AreEqual(0f, HouseholdThermalModel.TemperatureFactor(15f), "At the 15 C heating base there is no demand.");
            Assert.AreEqual(0f, HouseholdThermalModel.TemperatureFactor(21.8f), "A July mean needs no heating.");
            Assert.AreEqual(0f, HouseholdThermalModel.TemperatureFactor(30f));
            Assert.AreEqual(1.025f, HouseholdThermalModel.TemperatureFactor(-10.7f), 0.01f,
                "At the January mean (-10.7 C) the factor is ~1.0 — the old full-strain value.");
            Assert.AreEqual(1.536f, HouseholdThermalModel.TemperatureFactor(-25f), 0.01f,
                "A -25 C mean day burns ~1.5x the January mean.");
            Assert.AreEqual(1.6f, HouseholdThermalModel.TemperatureFactor(-45f),
                "The factor caps at 1.6 in extreme cold.");
        }

        [Test]
        public void CalibrationAnchors_JanuaryMeanBurnMatchesVerifiedBaseline()
        {
            // The old B2 pin, preserved: a 4-person Basic household at the
            // January mean burns 3. The size factor (84 m2 derived area)
            // is part of the pin: 1.025 x 2.05 x 1.4 = 2.94 -> 3.
            Assert.AreEqual(3, HouseholdThermalModel.CalculateDailyFuelUseUnits(Profile(members: 4), -10.7f));
            Assert.AreEqual(2, HouseholdThermalModel.CalculateDailyFuelUseUnits(Profile(members: 2), -10.7f),
                "A 2-person household burns 2 at the January mean.");
            Assert.AreEqual(1, HouseholdThermalModel.CalculateDailyFuelUseUnits(Profile(members: 1), -10.7f),
                "A 1-person household burns 1 at the January mean.");
            Assert.AreEqual(1, HouseholdThermalModel.CalculateDailyFuelUseUnits(Profile(members: 2), 6.2f),
                "A 2-person household burns 1 at the April mean — spring mornings need a fire.");
            Assert.AreEqual(0, HouseholdThermalModel.CalculateDailyFuelUseUnits(Profile(members: 4), 21.8f),
                "Nobody burns at the July mean.");
        }

        [Test]
        public void BurnScalesWithTemperatureDifferential()
        {
            var profile = Profile(members: 4);
            int brutal = HouseholdThermalModel.CalculateDailyFuelUseUnits(profile, -25f);
            int januaryMean = HouseholdThermalModel.CalculateDailyFuelUseUnits(profile, -10.7f);
            int aprilMean = HouseholdThermalModel.CalculateDailyFuelUseUnits(profile, 6.2f);
            int julyMean = HouseholdThermalModel.CalculateDailyFuelUseUnits(profile, 21.8f);
            Assert.Greater(brutal, januaryMean, "A brutal day burns more than the January mean.");
            Assert.Greater(januaryMean, aprilMean, "The January mean burns more than the April mean.");
            Assert.Greater(aprilMean, julyMean, "The April mean burns more than the July mean (zero).");
            Assert.AreEqual(0, julyMean);
        }

        [Test]
        public void BurnScalesWithSizeAndStoreys()
        {
            // 4-person Basic at the January mean: 60 m2 burns 3, 120 m2
            // burns 5; a 2-storey 60 m2 home (120 m2 total) burns the same
            // as a 1-storey 120 m2 home.
            Assert.AreEqual(3, HouseholdThermalModel.CalculateDailyFuelUseUnits(Profile(floorAreaM2: 60f), -10.7f));
            Assert.AreEqual(5, HouseholdThermalModel.CalculateDailyFuelUseUnits(Profile(floorAreaM2: 120f), -10.7f));
            Assert.AreEqual(5, HouseholdThermalModel.CalculateDailyFuelUseUnits(Profile(floorAreaM2: 60f, storeys: 2), -10.7f),
                "Storeys multiply floor area: a 2-storey 60 m2 home heats 120 m2.");
            Assert.AreEqual(2, HouseholdThermalModel.CalculateDailyFuelUseUnits(Profile(floorAreaM2: 30f), -10.7f),
                "The size factor floors at 0.5: a 30 m2 cabin burns 2 at the January mean (1.025 x 2.05 x 0.5 = 1.05 -> 2).");
        }

        [Test]
        public void BurnScalesWithInsulationAndStoveTier()
        {
            // At a cold January day (-20 C) the tier differences are visible
            // in integers: Drafty 5, Basic 4, Tight 3; Efficient 3 vs Basic 4.
            // (At the January mean the size factor pushes Basic and Tight to
            // the same integer — the multipliers are real, the rounding hides it.)
            Assert.AreEqual(5, HouseholdThermalModel.CalculateDailyFuelUseUnits(
                Profile(retention: HouseholdHeatRetentionTier.Drafty), -20f));
            Assert.AreEqual(4, HouseholdThermalModel.CalculateDailyFuelUseUnits(
                Profile(retention: HouseholdHeatRetentionTier.Basic), -20f));
            Assert.AreEqual(3, HouseholdThermalModel.CalculateDailyFuelUseUnits(
                Profile(retention: HouseholdHeatRetentionTier.Tight), -20f));
            Assert.AreEqual(3, HouseholdThermalModel.CalculateDailyFuelUseUnits(
                Profile(stove: HouseholdStoveTier.Efficient), -20f),
                "An Efficient stove burns less than a Basic stove on the same cold day.");
        }

        [Test]
        public void ProfileFor_DerivesAreaFromMembers_AndHonoursStoredAreaAndStoreys()
        {
            var twoPerson = new HouseholdState { memberIds = new List<int> { 1, 2 } };
            var profile = HouseholdThermalModel.ProfileFor(twoPerson);
            Assert.AreEqual(60f, profile.FloorAreaM2, "A 2-person household derives 60 m2 (36 + 12 x 2).");
            Assert.AreEqual(1, profile.Storeys);
            Assert.AreEqual(2, profile.MemberCount);

            var onePerson = new HouseholdState { memberIds = new List<int> { 1 } };
            Assert.AreEqual(48f, HouseholdThermalModel.ProfileFor(onePerson).FloorAreaM2,
                "A 1-person household derives 48 m2 (size factor 0.8) — real variation from real composition.");

            var stored = new HouseholdState { memberIds = new List<int> { 1, 2 }, homeFloorAreaM2 = 90f, homeStoreys = 2 };
            var storedProfile = HouseholdThermalModel.ProfileFor(stored);
            Assert.AreEqual(90f, storedProfile.FloorAreaM2, "A stored floor area wins over the derivation.");
            Assert.AreEqual(2, storedProfile.Storeys);
        }

        [Test]
        public void DerivedFloorArea_ClampsToPlausibleRange()
        {
            Assert.AreEqual(48f, HouseholdThermalModel.DerivedFloorAreaM2(1));
            Assert.AreEqual(60f, HouseholdThermalModel.DerivedFloorAreaM2(2));
            Assert.AreEqual(84f, HouseholdThermalModel.DerivedFloorAreaM2(4));
            Assert.AreEqual(120f, HouseholdThermalModel.DerivedFloorAreaM2(10), "Ten members clamp at 120 m2.");
            Assert.AreEqual(36f, HouseholdThermalModel.DerivedFloorAreaM2(0), "Zero members clamp at 36 m2.");
        }
    }
}
