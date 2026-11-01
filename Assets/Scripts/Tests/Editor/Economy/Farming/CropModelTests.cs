using System.Collections.Generic;
using LandLedgers.Economy.Farming.Crops;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy.Farming
{
    /// <summary>
    /// CRP-1: the crop field authority. Fields register with real acres,
    /// transitions follow the legal crop cycle, biological potential separates
    /// from work completion (Canon §7.4C), and missed harvest windows fail
    /// through the field state (Canon §7.4D).
    /// </summary>
    [TestFixture]
    public sealed class CropModelTests
    {
        private CropFieldAuthority AuthorityWithField()
        {
            var authority = new CropFieldAuthority();
            var field = new CropFieldState("north-40", "farm-1", 40f)
            {
                LinkedPlotName = "north crop field",
                Suitability01 = 0.8f,
                Moisture01 = 0.7f,
            };
            string problem = authority.RegisterField(field);
            Assert.IsNull(problem, "Field registration was refused: " + problem);
            return authority;
        }

        [Test]
        public void RegisterField_RequiresAcresAndUniqueId()
        {
            var authority = new CropFieldAuthority();
            Assert.IsNotNull(authority.RegisterField(new CropFieldState("f1", "farm-1", 0f)),
                "A field with no acres must be refused.");
            Assert.IsNull(authority.RegisterField(new CropFieldState("f1", "farm-1", 10f)));
            Assert.IsNotNull(authority.RegisterField(new CropFieldState("f1", "farm-1", 10f)),
                "Duplicate field ids must be refused — ids are never reused.");
        }

        [Test]
        public void LegalTransitions_FollowTheCropCycle()
        {
            Assert.IsTrue(CropFieldAuthority.IsLegalTransition(CropGrowthState.Fallow, CropGrowthState.Prepared));
            Assert.IsTrue(CropFieldAuthority.IsLegalTransition(CropGrowthState.Prepared, CropGrowthState.Planted));
            Assert.IsTrue(CropFieldAuthority.IsLegalTransition(CropGrowthState.Ready, CropGrowthState.Harvested));
            Assert.IsFalse(CropFieldAuthority.IsLegalTransition(CropGrowthState.Fallow, CropGrowthState.Harvested),
                "Nothing skips the crop cycle — work must happen in order.");
            Assert.IsFalse(CropFieldAuthority.IsLegalTransition(CropGrowthState.Planted, CropGrowthState.Ready),
                "Crops do not ripen without growing.");
        }

        [Test]
        public void BiologicalPotential_SeparatesFromWorkCompletion()
        {
            var authority = AuthorityWithField();
            CropFieldState field = authority.GetField("north-40");

            // Fallow land has no potential regardless of soil.
            Assert.AreEqual(0f, authority.BiologicalPotential(field), 0.001f);

            field.GrowthState = CropGrowthState.Growing;
            field.CurrentCrop = CropKind.Wheat;
            float potential = authority.BiologicalPotential(field);
            Assert.Greater(potential, 0f, "Growing wheat on good soil must have real potential.");
            Assert.LessOrEqual(potential, 1f);

            // Drought drags potential down hard (Canon §7.4C: water is binding).
            field.Moisture01 = 0.1f;
            float dryPotential = authority.BiologicalPotential(field);
            Assert.Less(dryPotential, potential * 0.6f, "Dry soil must cut biological potential.");
        }

        [Test]
        public void AdvanceDay_RipensThenFailsMissedHarvest()
        {
            var authority = AuthorityWithField();
            CropFieldState field = authority.GetField("north-40");
            field.CurrentCrop = CropKind.Oats; // 85 grow days, 21-day window
            field.GrowthState = CropGrowthState.Planted;
            field.PlantedDayIndex = 0;
            var diagnostics = new List<string>();

            authority.AdvanceDay(50, diagnostics);
            Assert.AreEqual(CropGrowthState.Growing, field.GrowthState);

            authority.AdvanceDay(85, diagnostics);
            Assert.AreEqual(CropGrowthState.Ready, field.GrowthState, "Oats must be ready at 85 days.");

            authority.AdvanceDay(85 + 21, diagnostics);
            Assert.AreEqual(CropGrowthState.Failed, field.GrowthState,
                "An unharvested crop fails when its window closes (Canon §7.4D).");
            Assert.AreEqual(0f, authority.BiologicalPotential(field), 0.001f,
                "Failed fields carry no potential.");
        }

        [Test]
        public void CompleteOperation_EnforcesLegalTransitions()
        {
            var authority = AuthorityWithField();
            var diagnostics = new List<string>();

            string problem = authority.CompleteOperation("north-40", CropGrowthState.Harvested, diagnostics);
            Assert.IsNotNull(problem, "Fallow → Harvested must be refused.");

            Assert.IsNull(authority.CompleteOperation("north-40", CropGrowthState.Prepared, diagnostics));
            Assert.AreEqual(CropGrowthState.Prepared, authority.GetField("north-40").GrowthState);
        }
    }
}
