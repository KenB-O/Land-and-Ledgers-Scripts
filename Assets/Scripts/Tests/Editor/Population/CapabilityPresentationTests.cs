using LandLedgers.Population;
using LandLedgers.Skills;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// Pass III Worker D: the governing 4-band capability display.
    ///
    /// CANON CORRECTION (XXXI §4.5 CANON LOCK — documented per task): the old
    /// seven-band display (Abysmal/Bad/Poor/Ordinary/Great/Excellent/Mastered)
    /// is superseded and removed. The governing bands are Ordinary → Great →
    /// Excellent → Mastered. High confidence = exactly one band; Moderate = two
    /// adjacent; Low = three adjacent; Insufficient = "Not enough information".
    /// A single band paired with Moderate/Low confidence is INVALID.
    /// </summary>
    public sealed class CapabilityPresentationTests
    {
        [Test]
        public void FourBandsUseTheGoverningLabelsAndKeepOrdinaryAsTheCentre()
        {
            Assert.AreEqual("Ordinary", CapabilityPresentation.Label(CapabilityBand.Ordinary));
            Assert.AreEqual("Great", CapabilityPresentation.Label(CapabilityBand.Great));
            Assert.AreEqual("Excellent", CapabilityPresentation.Label(CapabilityBand.Excellent));
            Assert.AreEqual("Mastered", CapabilityPresentation.Label(CapabilityBand.Mastered));

            // Ordinary is the broad centre (Rev VII §1.3 anti-inflation).
            Assert.AreEqual(CapabilityBand.Ordinary, CapabilityPresentation.BandFromCapability(0.10));
            Assert.AreEqual(CapabilityBand.Ordinary, CapabilityPresentation.BandFromCapability(0.55));
            Assert.AreEqual(CapabilityBand.Ordinary, CapabilityPresentation.BandFromCapability(0.69));
            Assert.AreEqual(CapabilityBand.Great, CapabilityPresentation.BandFromCapability(0.70));
            Assert.AreEqual(CapabilityBand.Excellent, CapabilityPresentation.BandFromCapability(0.85));
            Assert.AreEqual(CapabilityBand.Mastered, CapabilityPresentation.BandFromCapability(0.95));
        }

        [Test]
        public void ConfidenceControlsTheNumberOfAdjacentBands()
        {
            CapabilityEvidenceReading high = CapabilityPresentation.ReadingFromCentre(
                "p1", "stocking", CapabilityBand.Great, CapabilityConfidence.High, "test");
            CapabilityEvidenceReading moderate = CapabilityPresentation.ReadingFromCentre(
                "p1", "stocking", CapabilityBand.Great, CapabilityConfidence.Moderate, "test");
            CapabilityEvidenceReading low = CapabilityPresentation.ReadingFromCentre(
                "p1", "stocking", CapabilityBand.Great, CapabilityConfidence.Low, "test");

            Assert.AreEqual(1, high.Bands.Count);
            Assert.AreEqual(CapabilityBand.Great, high.Bands[0]);
            Assert.AreEqual(2, moderate.Bands.Count);
            Assert.AreEqual(3, low.Bands.Count);
            // Adjacency: bands are consecutive.
            for (int i = 1; i < low.Bands.Count; i++)
            {
                Assert.AreEqual((int)low.Bands[i - 1] + 1, (int)low.Bands[i]);
            }
        }

        [Test]
        public void SingleBandWithModerateOrLowConfidence_IsInvalid()
        {
            // XXXI §4.5: a single band paired with Moderate/Low is invalid.
            var badModerate = new CapabilityEvidenceReading
            {
                PersonId = "p1",
                TaskCapabilityId = "stocking",
                Confidence = CapabilityConfidence.Moderate,
                Bands = new System.Collections.Generic.List<CapabilityBand> { CapabilityBand.Great },
            };
            Assert.IsNotNull(CapabilityEvidenceService.ValidateReading(badModerate));

            var badLow = new CapabilityEvidenceReading
            {
                PersonId = "p1",
                TaskCapabilityId = "stocking",
                Confidence = CapabilityConfidence.Low,
                Bands = new System.Collections.Generic.List<CapabilityBand> { CapabilityBand.Great },
            };
            Assert.IsNotNull(CapabilityEvidenceService.ValidateReading(badLow));

            // And the presentation renderer refuses to show a false band.
            string rendered = CapabilityPresentation.FormatReading(badModerate);
            Assert.IsTrue(rendered.StartsWith("Invalid reading"));
        }

        [Test]
        public void InsufficientEvidenceRenders_NotEnoughInformation()
        {
            CapabilityEvidenceReading reading = CapabilityPresentation.ReadingFromCentre(
                "p1", "stocking", CapabilityBand.Ordinary,
                CapabilityConfidence.Insufficient, "test");

            Assert.AreEqual(0, reading.Bands.Count);
            Assert.AreEqual("Not enough information",
                CapabilityPresentation.FormatReading(reading));
            Assert.AreEqual("Not enough information",
                CapabilityPresentation.ConfidenceLabel(CapabilityConfidence.Insufficient));
        }

        [Test]
        public void RoutineHistoryStartsWithLowEvidenceAndSupportedWeeksIncreaseConfidence()
        {
            PersonState person = new PersonState
            {
                visibleWorkerProfile = WorkerVisibleProfile.Create(55, 1200, "Former store clerk"),
                apprenticeship = new WorkerApprenticeshipState()
            };

            CapabilityEvidenceReading initial = CapabilityPresentation.FromPerson(person);
            Assert.AreEqual(CapabilityConfidence.Low, initial.Confidence);
            // generalSkill 55 -> capability 0.55 -> Ordinary centre; Low = 3 bands.
            Assert.AreEqual(3, initial.Bands.Count);
            Assert.IsTrue(initial.Bands.Contains(CapabilityBand.Ordinary));

            for (int i = 0; i < 4; i++)
                person.apprenticeship.RecordPaidWeek(1, 1, 1, "slot", ApprenticeshipStage.Apprentice);
            CapabilityEvidenceReading after = CapabilityPresentation.FromPerson(person);
            Assert.AreEqual(CapabilityConfidence.Moderate, after.Confidence);
            Assert.AreEqual(2, after.Bands.Count);
        }

        [Test]
        public void NoUsableHistory_RendersNotEnoughInformation()
        {
            PersonState person = new PersonState
            {
                visibleWorkerProfile = WorkerVisibleProfile.Create(80, 2000, "No paid work history"),
                apprenticeship = new WorkerApprenticeshipState()
            };

            CapabilityEvidenceReading reading = CapabilityPresentation.FromPerson(person);
            Assert.AreEqual(CapabilityConfidence.Insufficient, reading.Confidence);
            Assert.AreEqual("Not enough information",
                CapabilityPresentation.FormatReading(reading));
        }
    }
}
