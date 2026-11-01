using LandLedgers.Economy.Labor;
using LandLedgers.Skills;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Core
{
    /// <summary>
    /// Pass III Worker D: observer-side capability evidence.
    /// Canon: XXXI §§4.4-4.5 (evidence vs truth; confidence invariant);
    /// Tech XIV Part III §§3.4-3.5.
    /// </summary>
    [TestFixture]
    public sealed class CapabilityEvidenceTests
    {
        [Test]
        public void NoEvidence_YieldsInsufficientReading()
        {
            var service = new CapabilityEvidenceService();

            CapabilityEvidenceReading reading =
                service.ReadCapability("employer", "candidate", "stocking");

            Assert.AreEqual(CapabilityConfidence.Insufficient, reading.Confidence);
            Assert.AreEqual(0, reading.Bands.Count);
            Assert.IsNull(CapabilityEvidenceService.ValidateReading(reading));
        }

        [Test]
        public void EvidenceWeight_DrivesConfidence_OneToThreeBands()
        {
            var service = new CapabilityEvidenceService();

            // One reference (weight 1) -> Low -> 3 bands.
            service.RecordEvidence("employer", "candidate", "stocking",
                EvidenceKind.Reference, CapabilityBand.Great, "letter from former boss", 5);
            CapabilityEvidenceReading low = service.ReadCapability("employer", "candidate", "stocking");
            Assert.AreEqual(CapabilityConfidence.Low, low.Confidence);
            Assert.AreEqual(3, low.Bands.Count);
            Assert.IsNull(CapabilityEvidenceService.ValidateReading(low));

            // Add work history (weight 2, total 3) -> Moderate -> 2 bands.
            service.RecordEvidence("employer", "candidate", "stocking",
                EvidenceKind.WorkHistory, CapabilityBand.Great, "worked together 2 years", 6);
            CapabilityEvidenceReading moderate = service.ReadCapability("employer", "candidate", "stocking");
            Assert.AreEqual(CapabilityConfidence.Moderate, moderate.Confidence);
            Assert.AreEqual(2, moderate.Bands.Count);
            Assert.IsNull(CapabilityEvidenceService.ValidateReading(moderate));

            // Add a demonstration (weight 3, total 6) -> High -> 1 band.
            service.RecordEvidence("employer", "candidate", "stocking",
                EvidenceKind.Demonstration, CapabilityBand.Excellent, "trial shift observed", 7);
            CapabilityEvidenceReading high = service.ReadCapability("employer", "candidate", "stocking");
            Assert.AreEqual(CapabilityConfidence.High, high.Confidence);
            Assert.AreEqual(1, high.Bands.Count);
            Assert.IsNull(CapabilityEvidenceService.ValidateReading(high));
        }

        [Test]
        public void EvidenceNeverExposesHiddenTruth()
        {
            var service = new CapabilityEvidenceService();
            var capabilities = new PersonCapabilityStore();
            // Hidden truth: the candidate is actually Excellent (0.9).
            capabilities.SetCapability("candidate", "stocking", 0.9, 1);

            // The observer only has a weak reference hinting at Great.
            service.RecordEvidence("employer", "candidate", "stocking",
                EvidenceKind.Reference, CapabilityBand.Great, "vague letter", 5);

            CapabilityEvidenceReading reading =
                service.ReadCapability("employer", "candidate", "stocking");

            // The reading reflects the evidence (Great-centred, Low), NOT the
            // hidden 0.9/Excellent truth.
            Assert.AreEqual(CapabilityConfidence.Low, reading.Confidence);
            Assert.IsTrue(reading.Bands.Contains(CapabilityBand.Great));
            Assert.IsFalse(reading.Bands.Contains(CapabilityBand.Excellent) &&
                           reading.Bands.Count == 1);
        }

        [Test]
        public void Evidence_IsObserverSpecific()
        {
            var service = new CapabilityEvidenceService();
            service.RecordEvidence("employerA", "candidate", "stocking",
                EvidenceKind.Observation, CapabilityBand.Great, "saw them work", 5);

            CapabilityEvidenceReading forA =
                service.ReadCapability("employerA", "candidate", "stocking");
            CapabilityEvidenceReading forB =
                service.ReadCapability("employerB", "candidate", "stocking");

            Assert.AreNotEqual(CapabilityConfidence.Insufficient, forA.Confidence);
            Assert.AreEqual(CapabilityConfidence.Insufficient, forB.Confidence);
        }

        [Test]
        public void ContactGraph_EvaluateCandidate_ConsumesEvidenceReadings()
        {
            var skills = new SkillService();
            skills.RegisterStarterSet();

            var graph = new ContactGraphService();
            // P1 (shopkeeper) knows P2 (candidate) via a documented link.
            graph.SeedHouseholdLinks(1, 7, new[] { 2 });

            // Without an evidence reader: no reading (null), no crash.
            Assert.IsNull(graph.EvaluateCandidateCapability(2, SkillIds.Stocking, 1));

            // Wire the evidence reader (additive — the graph itself unchanged).
            graph.CapabilityEvidence = skills.Evidence;

            // Still no evidence recorded: Insufficient reading.
            CapabilityEvidenceReading noEvidence =
                graph.EvaluateCandidateCapability(2, SkillIds.Stocking, 1);
            Assert.IsNotNull(noEvidence);
            Assert.AreEqual(CapabilityConfidence.Insufficient, noEvidence.Confidence);

            // Record real evidence: P1 observed P2 stocking shelves.
            skills.Evidence.RecordEvidence("1", "2", SkillIds.Stocking,
                EvidenceKind.Observation, CapabilityBand.Great,
                "P1 watched P2 stock shelves for a week", 12);

            CapabilityEvidenceReading reading =
                graph.EvaluateCandidateCapability(2, SkillIds.Stocking, 1);
            Assert.IsNotNull(reading);
            Assert.AreEqual(CapabilityConfidence.Low, reading.Confidence);
            Assert.IsTrue(reading.Bands.Contains(CapabilityBand.Great));

            // A stranger with no documented link gets no evaluation —
            // evidence requires contact (brief §5).
            Assert.IsNull(graph.EvaluateCandidateCapability(99, SkillIds.Stocking, 1));
        }

        [Test]
        public void EmploymentNegotiation_GetCandidateEvidence_UsesOptionalReader()
        {
            var skills = new SkillService();
            skills.RegisterStarterSet();

            var negotiation = new EmploymentNegotiationService();

            // No reader wired: null, no crash.
            Assert.IsNull(negotiation.GetCandidateEvidence(2, SkillIds.Cooking, 1));

            negotiation.CapabilityEvidence = skills.Evidence;
            skills.Evidence.RecordEvidence("1", "2", SkillIds.Cooking,
                EvidenceKind.Reference, CapabilityBand.Ordinary,
                "reference letter", 3);
            skills.Evidence.RecordEvidence("1", "2", SkillIds.Cooking,
                EvidenceKind.WorkHistory, CapabilityBand.Great,
                "cooked at the boarding house", 4);

            CapabilityEvidenceReading reading =
                negotiation.GetCandidateEvidence(2, SkillIds.Cooking, 1);
            Assert.IsNotNull(reading);
            Assert.AreEqual(CapabilityConfidence.Moderate, reading.Confidence);
            Assert.AreEqual(2, reading.Bands.Count);
        }

        [Test]
        public void Evidence_SaveLoad_RoundTrips()
        {
            var service = new CapabilityEvidenceService();
            service.RecordEvidence("e1", "c1", "framing",
                EvidenceKind.Demonstration, CapabilityBand.Excellent, "trial barn raising", 20);

            var restored = new CapabilityEvidenceService();
            restored.LoadItems(service.CaptureItems());

            CapabilityEvidenceReading reading =
                restored.ReadCapability("e1", "c1", "framing");
            Assert.AreEqual(CapabilityConfidence.Moderate, reading.Confidence);
            Assert.AreEqual(2, reading.Bands.Count);
        }
    }
}
