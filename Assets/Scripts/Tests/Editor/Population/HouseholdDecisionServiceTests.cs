using System.Collections.Generic;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// Worker C (Pass III): household pressure -> decision. Proves:
    /// pressure yields SeekIncome with a real reason; adequate resources yield
    /// NoAction (no forced job-seeking, no timer); rent obligations change the
    /// decision; shelter risk routes to NegotiateTerms/RelocateCheaper; credit
    /// enables Borrow; save/load preserves history without duplication.
    /// Canon: XXXI Parts I/V (real reasons, no universal highest-wage rule).
    /// </summary>
    [TestFixture]
    public sealed class HouseholdDecisionServiceTests
    {
        private static HouseholdPressureInput BaseInput()
        {
            return new HouseholdPressureInput
            {
                HouseholdId = "7",
                Day = 100,
                ReservesCents = 400,
                ObligationsDueCents = 6000,
                ExpectedIncomeCentsPerDay = 100,
                Dependents = 2,
                KnownOpportunityCount = 0,
                HasShelterRisk = false,
            };
        }

        [Test]
        public void Shortfall_WithKnownOpportunities_SeekIncomeWithReason()
        {
            var service = new HouseholdDecisionService();
            HouseholdPressureInput input = BaseInput();
            input.KnownOpportunityCount = 2; // coverage = 400 + 3000 = 3400 < 6000

            HouseholdDecision decision = service.Evaluate(input);

            Assert.AreEqual(HouseholdDecisionKind.SeekIncome, decision.Kind);
            Assert.IsTrue(decision.Reason.Contains("2600c"), "reason must name the shortfall, got: " + decision.Reason);
            Assert.IsTrue(decision.Reason.Contains("2"), "reason must name the opportunity count, got: " + decision.Reason);
        }

        [Test]
        public void AdequateResources_NoAction_NoForcedJobSeeking()
        {
            var service = new HouseholdDecisionService();
            HouseholdPressureInput input = BaseInput();
            input.ReservesCents = 9000; // coverage = 9000 + 3000 = 12000 >= 1.25 * 6000 = 7500
            input.KnownOpportunityCount = 5; // opportunities exist but are not needed

            HouseholdDecision decision = service.Evaluate(input);

            Assert.AreEqual(HouseholdDecisionKind.NoAction, decision.Kind);
            Assert.IsTrue(decision.Reason.Contains("cushion"), "reason must explain adequacy, got: " + decision.Reason);
        }

        [Test]
        public void RentObligationInPressureInput_ChangesDecision()
        {
            var service = new HouseholdDecisionService();

            // Same reserves and income, no obligations -> NoAction.
            HouseholdPressureInput noRent = BaseInput();
            noRent.ReservesCents = 5000;
            noRent.ObligationsDueCents = 0;
            HouseholdDecision d1 = service.Evaluate(noRent);
            Assert.AreEqual(HouseholdDecisionKind.NoAction, d1.Kind);

            // Worker B's rent terms arrive as obligations -> pressure is real.
            HouseholdPressureInput withRent = BaseInput();
            withRent.ReservesCents = 5000;
            withRent.ObligationsDueCents = 9000; // coverage 8000 < 9000, thin but short
            withRent.KnownOpportunityCount = 1;
            HouseholdDecision d2 = service.Evaluate(withRent);
            Assert.AreEqual(HouseholdDecisionKind.SeekIncome, d2.Kind);
            Assert.IsTrue(d2.Reason.Contains("9000c"), "reason must name the rent obligation, got: " + d2.Reason);
        }

        [Test]
        public void ShelterRisk_StructuralDeficit_RelocateCheaper()
        {
            var service = new HouseholdDecisionService();
            HouseholdPressureInput input = BaseInput();
            input.HasShelterRisk = true;
            input.ExpectedIncomeCentsPerDay = 50; // 30d income = 1500 < 6000 obligations

            HouseholdDecision decision = service.Evaluate(input);

            Assert.AreEqual(HouseholdDecisionKind.RelocateCheaper, decision.Kind);
            Assert.IsTrue(decision.Reason.Contains("structurally"), "got: " + decision.Reason);
        }

        [Test]
        public void ShelterRisk_TemporaryShortfall_NegotiateTerms()
        {
            var service = new HouseholdDecisionService();
            HouseholdPressureInput input = BaseInput();
            input.HasShelterRisk = true;
            input.ReservesCents = 400;
            input.ExpectedIncomeCentsPerDay = 200; // coverage 6400 < 7500 (not comfortable), 30d income 6000 >= 6000: temporary

            HouseholdDecision decision = service.Evaluate(input);

            Assert.AreEqual(HouseholdDecisionKind.NegotiateTerms, decision.Kind);
        }

        [Test]
        public void Shortfall_NoOpportunities_WithCredit_Borrow()
        {
            var service = new HouseholdDecisionService();
            HouseholdPressureInput input = BaseInput(); // shortfall 2600c
            input.AvailableCreditCents = 5000;
            input.KnownCreditorId = "P9";

            HouseholdDecision decision = service.Evaluate(input);

            Assert.AreEqual(HouseholdDecisionKind.Borrow, decision.Kind);
            Assert.IsTrue(decision.Reason.Contains("5000c"), "got: " + decision.Reason);
        }

        [Test]
        public void Shortfall_NoOpportunities_NoCredit_Economize()
        {
            var service = new HouseholdDecisionService();
            HouseholdPressureInput input = BaseInput(); // shortfall 2600c, no credit

            HouseholdDecision decision = service.Evaluate(input);

            Assert.AreEqual(HouseholdDecisionKind.Economize, decision.Kind);
        }

        [Test]
        public void ThinCushion_Economize_NotSeekIncome()
        {
            var service = new HouseholdDecisionService();
            HouseholdPressureInput input = BaseInput();
            input.ReservesCents = 4000; // coverage 7000 in [6000, 7500): thin
            input.KnownOpportunityCount = 3;

            HouseholdDecision decision = service.Evaluate(input);

            Assert.AreEqual(HouseholdDecisionKind.Economize, decision.Kind);
        }

        [Test]
        public void JobLoss_WithKnownOpportunities_SeekIncome()
        {
            var service = new HouseholdDecisionService();
            HouseholdPressureInput input = BaseInput();
            input.ReservesCents = 3000; // coverage 6000 < 7500: not comfortable
            input.RecentJobLoss = true;
            input.JobLossNote = "P3 dismissed from store-1";
            input.KnownOpportunityCount = 1;

            HouseholdDecision decision = service.Evaluate(input);

            Assert.AreEqual(HouseholdDecisionKind.SeekIncome, decision.Kind);
            Assert.IsTrue(decision.Reason.Contains("P3 dismissed from store-1"), "got: " + decision.Reason);
        }

        [Test]
        public void JobLoss_WithAdequateResources_NoAction_RelyOnReserves()
        {
            var service = new HouseholdDecisionService();
            HouseholdPressureInput input = BaseInput();
            input.ReservesCents = 9000; // comfortable
            input.RecentJobLoss = true;
            input.JobLossNote = "P3 dismissed from store-1";

            HouseholdDecision decision = service.Evaluate(input);

            Assert.AreEqual(HouseholdDecisionKind.NoAction, decision.Kind);
            Assert.IsTrue(decision.Reason.Contains("relying on household reserves"), "got: " + decision.Reason);
        }

        [Test]
        public void Shortfall_SearchableContacts_SeekIncomeToSearch()
        {
            var service = new HouseholdDecisionService();
            HouseholdPressureInput input = BaseInput(); // shortfall 2600c
            input.HasSearchableContacts = true;

            HouseholdDecision decision = service.Evaluate(input);

            Assert.AreEqual(HouseholdDecisionKind.SeekIncome, decision.Kind);
            Assert.IsTrue(decision.Reason.Contains("documented contacts"), "got: " + decision.Reason);
        }

        [Test]
        public void NullInput_NoAction_DoesNotThrow()
        {
            var service = new HouseholdDecisionService();
            HouseholdDecision decision = service.Evaluate(null);
            Assert.AreEqual(HouseholdDecisionKind.NoAction, decision.Kind);
        }

        [Test]
        public void SaveLoad_PreservesHistory_NoDuplication()
        {
            var service = new HouseholdDecisionService();
            service.Evaluate(BaseInput());
            HouseholdPressureInput second = BaseInput();
            second.Day = 101;
            second.KnownOpportunityCount = 1;
            service.Evaluate(second);
            Assert.AreEqual(2, service.History.Count);

            HouseholdDecisionServiceSaveDto dto = service.CaptureSaveDto();
            var restored = new HouseholdDecisionService();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(2, restored.History.Count);
            Assert.AreEqual(HouseholdDecisionKind.Economize, restored.History[0].Kind);
            Assert.AreEqual(HouseholdDecisionKind.SeekIncome, restored.History[1].Kind);

            // Loading again into the same instance replaces, never appends.
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(2, restored.History.Count);
        }
    }
}
