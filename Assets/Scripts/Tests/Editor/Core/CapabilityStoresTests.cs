using LandLedgers.Population;
using LandLedgers.Skills;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Core
{
    /// <summary>
    /// Pass III Worker D: the four capability authorities and experience
    /// formation. Canon: Rev VII §§1.1-1.5, Rev XVII §§3-4, §12; XXXI §§3.3, 4.4.
    /// </summary>
    [TestFixture]
    public sealed class CapabilityStoresTests
    {
        [Test]
        public void Aptitude_FirstWriteWins_AndDefaultsToOne()
        {
            var store = new AptitudeStore();

            Assert.AreEqual(1.0, store.GetLearningRate("p1", CapabilityFamily.FoodProcessing), 1e-9);

            store.SetAptitude("p1", CapabilityFamily.FoodProcessing, 2.0, 10);
            Assert.AreEqual(2.0, store.GetLearningRate("p1", CapabilityFamily.FoodProcessing), 1e-9);

            // Aptitude is set at creation — later writes do not retrain it.
            store.SetAptitude("p1", CapabilityFamily.FoodProcessing, 0.5, 20);
            Assert.AreEqual(2.0, store.GetLearningRate("p1", CapabilityFamily.FoodProcessing), 1e-9);

            // Other families are unaffected.
            Assert.AreEqual(1.0, store.GetLearningRate("p1", CapabilityFamily.Construction), 1e-9);
        }

        [Test]
        public void ExperienceLedger_IsIdempotent_OnDuplicateEntries()
        {
            var ledger = new ExperienceLedger();

            var entry = new ExperienceEntry
            {
                PersonId = "p1",
                TaskCapabilityId = "stocking",
                Difficulty01 = 0.5,
                Novelty01 = 0.8,
                Day = 3,
                MinutesSpent = 120,
                Outcome = "Success",
            };

            Assert.IsTrue(ledger.TryRecord(entry));
            Assert.IsNotNull(entry.EntryId);
            Assert.AreEqual(1, ledger.Count);

            // Recording the SAME entry object again is refused (idempotency key).
            Assert.IsFalse(ledger.TryRecord(entry));
            Assert.AreEqual(1, ledger.Count);

            // A distinct episode records fine.
            var second = new ExperienceEntry
            {
                PersonId = "p1",
                TaskCapabilityId = "stocking",
                Difficulty01 = 0.5,
                Novelty01 = 0.2,
                Day = 4,
                MinutesSpent = 120,
                Outcome = "Success",
            };
            Assert.IsTrue(ledger.TryRecord(second));
            Assert.AreEqual(2, ledger.Count);
        }

        [Test]
        public void ExperienceFormation_RoutineWorkShowsDiminishingGains()
        {
            var formation = new ExperienceFormationService();

            var novel = new ExperienceFormationInputs
            {
                PersonId = "p1",
                TaskCapabilityId = "cooking",
                Difficulty01 = 0.6,
                Novelty01 = 1.0,
                Supervised = false,
                Outcome = "Success",
                AptitudeMultiplier = 1.0,
                MinutesSpent = 480,
                Day = 1,
            };
            var routine = new ExperienceFormationInputs
            {
                PersonId = "p1",
                TaskCapabilityId = "cooking",
                Difficulty01 = 0.6,
                Novelty01 = 0.0,
                Supervised = false,
                Outcome = "Success",
                AptitudeMultiplier = 1.0,
                MinutesSpent = 480,
                Day = 2,
            };

            double novelGain = formation.ComputeDelta(0.3, novel, recentSimilarCount: 0);
            double routineGain = formation.ComputeDelta(0.3, routine, recentSimilarCount: 0);
            // New exposure teaches strongly; routine mastery teaches little.
            Assert.Greater(novelGain, routineGain * 2.0);

            // And repetition diminishes further: the 20th recent repetition
            // teaches less than the first.
            double firstRepeat = formation.ComputeDelta(0.3, routine, recentSimilarCount: 0);
            double twentiethRepeat = formation.ComputeDelta(0.3, routine, recentSimilarCount: 19);
            Assert.Greater(firstRepeat, twentiethRepeat);
            Assert.Greater(twentiethRepeat, 0.0); // still refines slightly, never zero
        }

        [Test]
        public void ExperienceFormation_AptitudeScalesGain_AndHigherCapabilityGainsLess()
        {
            var formation = new ExperienceFormationService();

            var inputs = new ExperienceFormationInputs
            {
                PersonId = "p1",
                TaskCapabilityId = "cooking",
                Difficulty01 = 0.6,
                Novelty01 = 0.8,
                Supervised = false,
                Outcome = "Success",
                MinutesSpent = 480,
                Day = 1,
            };

            inputs.AptitudeMultiplier = 2.0;
            double quick = formation.ComputeDelta(0.3, inputs, 0);
            inputs.AptitudeMultiplier = 0.5;
            double slow = formation.ComputeDelta(0.3, inputs, 0);
            Assert.Greater(quick, slow);
            // Aptitude scales roughly proportionally (2.0 vs 0.5 = 4x).
            Assert.AreEqual(4.0, quick / slow, 0.01);

            // Asymptotic approach: the same episode teaches less near mastery.
            inputs.AptitudeMultiplier = 1.0;
            double lowCap = formation.ComputeDelta(0.2, inputs, 0);
            double highCap = formation.ComputeDelta(0.9, inputs, 0);
            Assert.Greater(lowCap, highCap * 2.0);
        }

        [Test]
        public void ExperienceFormation_NoGain_WithoutTaskEpisodes()
        {
            var formation = new ExperienceFormationService();

            // Calendar time alone contributes NOTHING: formation only runs on
            // recorded task episodes. Zero minutes = zero gain, regardless of days.
            var idle = new ExperienceFormationInputs
            {
                PersonId = "p1",
                TaskCapabilityId = "cooking",
                Difficulty01 = 0.6,
                Novelty01 = 1.0,
                Outcome = "Success",
                AptitudeMultiplier = 2.0,
                MinutesSpent = 0,
                Day = 365,
            };
            Assert.AreEqual(0.0, formation.ComputeDelta(0.3, idle, 0), 1e-12);
            Assert.AreEqual(0.0, formation.ComputeDelta(0.3, null, 0), 1e-12);
        }

        [Test]
        public void ExperienceFormation_FailureStillTeaches_LessThanSuccess()
        {
            var formation = new ExperienceFormationService();

            var inputs = new ExperienceFormationInputs
            {
                PersonId = "p1",
                TaskCapabilityId = "cooking",
                Difficulty01 = 0.6,
                Novelty01 = 0.8,
                Supervised = false,
                AptitudeMultiplier = 1.0,
                MinutesSpent = 480,
                Day = 1,
            };

            inputs.Outcome = "Success";
            double success = formation.ComputeDelta(0.3, inputs, 0);
            inputs.Outcome = "RecoverableFailure";
            double failure = formation.ComputeDelta(0.3, inputs, 0);
            inputs.Outcome = "SeriousFailure";
            double serious = formation.ComputeDelta(0.3, inputs, 0);

            Assert.Greater(success, failure);
            Assert.Greater(failure, serious);
            Assert.Greater(serious, 0.0); // failures teach caution, never un-learn
        }

        [Test]
        public void LegacyXp_MigratesIntoOrdinary_PreservingProvenance()
        {
            // 250 XP = level 3 (1 + 250/100), residual 50.
            // capability = 0.05 + 2*0.06 + 0.5*0.06 = 0.20 (Ordinary).
            var state = new PersonSkillState("cooking");
            // Simulate the legacy serialized field via a fresh state: a real
            // old save deserializes experience=250; here we verify the formula
            // through the public migration path on a hand-built legacy record.
            var legacy = LegacyXpRecord(250);
            legacy.EnsureMigrated();

            Assert.AreEqual(0.20, legacy.Capability01, 1e-9);
            Assert.AreEqual(250, legacy.MigratedFromXp);
            // Idempotent: second call changes nothing.
            legacy.EnsureMigrated();
            Assert.AreEqual(0.20, legacy.Capability01, 1e-9);

            // Even maxed legacy XP (999) stays Ordinary — anti-inflation:
            // time-served never certified excellence (Rev VII §1.3).
            var maxed = LegacyXpRecord(999);
            maxed.EnsureMigrated();
            Assert.Less(maxed.Capability01, 0.70);
            Assert.AreEqual(CapabilityBand.Ordinary,
                CapabilityPresentation.BandFromCapability(maxed.Capability01));
        }

        [Test]
        public void AntiInflation_SystemicUpperBandInflation_IsCaught()
        {
            // Rev VII §1.3 TECH LOCK: regression must detect systemic
            // upper-band inflation — Average/Ordinary must remain the central
            // band. A population of workers with ordinary careers must not
            // drift into Great+.
            var formation = new ExperienceFormationService();
            var store = new PersonCapabilityStore();

            // 50 workers, 2 years of ordinary mixed work (novelty decays as
            // work becomes routine — the honest career arc).
            for (int w = 0; w < 50; w++)
            {
                string person = "pop" + w;
                double capability = PersonCapabilityStore.NoviceCapability;
                for (int day = 1; day <= 500; day++)
                {
                    var inputs = new ExperienceFormationInputs
                    {
                        PersonId = person,
                        TaskCapabilityId = "stocking",
                        Difficulty01 = 0.5,
                        Novelty01 = day < 60 ? 0.8 : 0.1,
                        Supervised = false,
                        Outcome = "Success",
                        AptitudeMultiplier = 1.0,
                        MinutesSpent = 480,
                        Day = day,
                    };
                    int recent = day < 60 ? 0 : 20;
                    capability += formation.ComputeDelta(capability, inputs, recent);
                }
                store.SetCapability(person, "stocking", capability, 500);
            }

            int greatOrAbove = 0;
            for (int w = 0; w < 50; w++)
            {
                double cap = store.GetCapability("pop" + w, "stocking");
                if (cap >= 0.70) greatOrAbove++;
            }

            // Ordinary careers must not mass-produce Great+ workers.
            Assert.IsTrue(greatOrAbove <= 5);
        }

        [Test]
        public void Stores_SaveLoad_ClearThenLoad()
        {
            var aptitudes = new AptitudeStore();
            aptitudes.SetAptitude("p1", CapabilityFamily.Construction, 1.75, 3);

            var ledger = new ExperienceLedger();
            ledger.TryRecord(new ExperienceEntry
            {
                PersonId = "p1", TaskCapabilityId = "framing",
                Difficulty01 = 0.7, Novelty01 = 0.9, Day = 10,
                MinutesSpent = 300, Outcome = "Success",
            });
            int seq = ledger.CaptureSequence();

            var familiarity = new DomainFamiliarityStore();
            familiarity.SetFamiliarity("p1", "residential construction", 0.6);

            var capabilities = new PersonCapabilityStore();
            capabilities.SetCapability("p1", "framing", 0.42, 10);

            // Round-trip each store independently.
            var aptitudes2 = new AptitudeStore();
            aptitudes2.LoadRecords(aptitudes.CaptureRecords());
            Assert.AreEqual(1.75,
                aptitudes2.GetLearningRate("p1", CapabilityFamily.Construction), 1e-9);

            var ledger2 = new ExperienceLedger();
            ledger2.LoadEntries(ledger.CaptureEntries(), seq);
            Assert.AreEqual(1, ledger2.Count);
            // Re-loading the same entries does not duplicate (idempotency keys).
            ledger2.LoadEntries(ledger.CaptureEntries(), seq);
            Assert.AreEqual(1, ledger2.Count);

            var familiarity2 = new DomainFamiliarityStore();
            familiarity2.LoadRecords(familiarity.CaptureRecords());
            Assert.AreEqual(0.6,
                familiarity2.GetFamiliarity("p1", "residential construction"), 1e-9);

            var capabilities2 = new PersonCapabilityStore();
            capabilities2.LoadStates(capabilities.CaptureStates());
            Assert.AreEqual(0.42, capabilities2.GetCapability("p1", "framing"), 1e-9);
        }

        /// <summary>
        /// Builds a PersonSkillState with the legacy XP field populated, as an
        /// old save would deserialize it. Uses the serialized field name via
        /// the migration path (EnsureMigrated reads 'experience').
        /// </summary>
        private static PersonSkillState LegacyXpRecord(int xp)
        {
            // PersonSkillState's legacy 'experience' field is private; drive it
            // through a save-shaped round trip is overkill here — instead use
            // the documented formula directly via a test-local subclass is not
            // possible (sealed). We construct via the internal migration test
            // hook: create the state and set the legacy field through the
            // serialized-field-compatible path.
            var state = new PersonSkillState("cooking");
            SetLegacyExperience(state, xp);
            return state;
        }

        private static void SetLegacyExperience(PersonSkillState state, int xp)
        {
            // The legacy field is private; reach it the way the serializer
            // would (field name 'experience').
            var field = typeof(PersonSkillState).GetField("experience",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            field.SetValue(state, xp);
        }
    }
}
