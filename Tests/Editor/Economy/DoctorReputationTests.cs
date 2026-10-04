using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Doctor;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1A: the practice's reputation ledger — Canon §13.3E: trust is earned
    /// through treatment and lost through absence and poor outcomes.
    /// </summary>
    [TestFixture]
    public sealed class DoctorReputationTests
    {
        [Test]
        public void OpeningTrust_IsFifty()
        {
            var ledger = new DoctorReputationLedger();
            Assert.AreEqual(50, ledger.Trust);
            Assert.AreEqual(1.0f, ledger.DemandPullMultiplier(), 0.001f,
                "At opening trust the practice draws no extra demand.");
        }

        [Test]
        public void RecordTreatmentOutcome_MovesTrustByTier()
        {
            var ledger = new DoctorReputationLedger();
            var diag = new List<string>();

            ledger.RecordTreatmentOutcome(DoctorOutcomeTier.FullBenefit, 300, diag);
            Assert.AreEqual(52, ledger.Trust);

            ledger.RecordTreatmentOutcome(DoctorOutcomeTier.PartialBenefit, 300, diag);
            Assert.AreEqual(53, ledger.Trust);

            ledger.RecordTreatmentOutcome(DoctorOutcomeTier.NoBenefit, 300, diag);
            Assert.AreEqual(52, ledger.Trust);

            ledger.RecordTreatmentOutcome(DoctorOutcomeTier.Untreated, 300, diag);
            Assert.AreEqual(50, ledger.Trust);

            Assert.AreEqual(4, ledger.Events.Count, "Every movement leaves a record.");
        }

        [Test]
        public void Trust_ClampsAtZeroAndOneHundred()
        {
            var ledger = new DoctorReputationLedger();
            var diag = new List<string>();

            for (int i = 0; i < 60; i++) ledger.RecordTreatmentOutcome(DoctorOutcomeTier.FullBenefit, 300, diag);
            Assert.AreEqual(100, ledger.Trust);

            for (int i = 0; i < 120; i++) ledger.RecordTreatmentOutcome(DoctorOutcomeTier.Untreated, 300, diag);
            Assert.AreEqual(0, ledger.Trust);
        }

        [Test]
        public void DemandPullMultiplier_RewardsReputation()
        {
            var ledger = new DoctorReputationLedger();
            var diag = new List<string>();

            for (int i = 0; i < 25; i++) ledger.RecordTreatmentOutcome(DoctorOutcomeTier.FullBenefit, 300, diag);
            Assert.AreEqual(100, ledger.Trust);
            Assert.AreEqual(1.2f, ledger.DemandPullMultiplier(), 0.001f,
                "A reputable practice draws patients from nearby nodes (Canon §13.3E).");
        }

        [Test]
        public void RecordUnansweredCall_AndDeferredUntreated_WeakenTrust()
        {
            var ledger = new DoctorReputationLedger();
            var diag = new List<string>();

            ledger.RecordUnansweredCall(300, diag);
            Assert.AreEqual(49, ledger.Trust);

            ledger.RecordDeferredUntreated(301, diag);
            Assert.AreEqual(47, ledger.Trust);

            Assert.IsTrue(diag.Count >= 2, "Trust movements must be loud.");
        }

        [Test]
        public void GenerousCredit_ImprovesTrust_BadDebt_CostsIt()
        {
            var ledger = new DoctorReputationLedger();
            var diag = new List<string>();

            ledger.RecordGenerousCredit(300, diag);
            Assert.AreEqual(51, ledger.Trust, "Generous credit improves access and reputation (Canon §13.3C).");

            ledger.RecordBadDebt(320, diag);
            Assert.AreEqual(50, ledger.Trust, "Bad debt is the exposure that comes with it (Canon §13.3C).");
        }

        [Test]
        public void SaveLoad_RoundTripsTrustAndEvents()
        {
            var ledger = new DoctorReputationLedger();
            var diag = new List<string>();
            ledger.RecordTreatmentOutcome(DoctorOutcomeTier.FullBenefit, 300, diag);

            var dto = ledger.CaptureSaveDto();
            var restored = new DoctorReputationLedger();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(52, restored.Trust);
            Assert.AreEqual(1, restored.Events.Count);
        }
    }
}
