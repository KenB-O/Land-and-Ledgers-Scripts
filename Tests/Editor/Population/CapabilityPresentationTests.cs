using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    public sealed class CapabilityPresentationTests
    {
        [Test]
        public void SevenBandsUseTheLockedLabelsAndKeepOrdinaryAsTheCentre()
        {
            Assert.AreEqual("Abysmal", CapabilityPresentation.Label(CapabilityBand.Abysmal));
            Assert.AreEqual("Bad", CapabilityPresentation.Label(CapabilityBand.Bad));
            Assert.AreEqual("Poor", CapabilityPresentation.Label(CapabilityBand.Poor));
            Assert.AreEqual("Ordinary", CapabilityPresentation.Label(CapabilityBand.Ordinary));
            Assert.AreEqual("Great", CapabilityPresentation.Label(CapabilityBand.Great));
            Assert.AreEqual("Excellent", CapabilityPresentation.Label(CapabilityBand.Excellent));
            Assert.AreEqual("Mastered", CapabilityPresentation.Label(CapabilityBand.Mastered));
            Assert.AreEqual(CapabilityBand.Ordinary, CapabilityPresentation.FromValue(55));
        }

        [Test]
        public void ConfidenceControlsTheNumberOfAdjacentBands()
        {
            CapabilityReading high = CapabilityPresentation.FromGeneralSkill(72, CapabilityConfidence.High);
            CapabilityReading moderate = CapabilityPresentation.FromGeneralSkill(72, CapabilityConfidence.Moderate);
            CapabilityReading low = CapabilityPresentation.FromGeneralSkill(72, CapabilityConfidence.Low);

            Assert.AreEqual(CapabilityBand.Great, high.LowerBand);
            Assert.AreEqual(high.LowerBand, high.UpperBand);
            Assert.AreEqual(1, (int)moderate.UpperBand - (int)moderate.LowerBand);
            Assert.AreEqual(2, (int)low.UpperBand - (int)low.LowerBand);
        }

        [Test]
        public void InsufficientEvidenceUsesUnknownAndDashWithoutExposingAFalseBand()
        {
            CapabilityReading reading = CapabilityPresentation.FromGeneralSkill(88, CapabilityConfidence.InsufficientEvidence);

            Assert.IsFalse(reading.IsKnown);
            Assert.AreEqual("UNKNOWN", reading.DisplayBand);
            Assert.AreEqual("—", reading.DisplayConfidence);
        }

        [Test]
        public void RoutineHistoryStartsWithLowEvidenceAndSupportedWeeksIncreaseConfidence()
        {
            PersonState person = new PersonState
            {
                visibleWorkerProfile = WorkerVisibleProfile.Create(55, 1200, "Former store clerk"),
                apprenticeship = new WorkerApprenticeshipState()
            };

            Assert.AreEqual(CapabilityConfidence.Low, CapabilityPresentation.FromPerson(person).Confidence);
            for (int i = 0; i < 4; i++)
                person.apprenticeship.RecordPaidWeek(1, 1, 1, "slot", ApprenticeshipStage.Apprentice);
            Assert.AreEqual(CapabilityConfidence.Moderate, CapabilityPresentation.FromPerson(person).Confidence);
        }
    }
}
