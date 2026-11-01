using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Orchestration.Scenarios;
using LandLedgers.Orchestration.Scenarios.Generation;

namespace LandLedgers.EditorTests.Orchestration
{
    /// <summary>
    /// Pass V P1-1 (Worker 1): preset load/validate/version/reuse/copy/derive,
    /// deterministic preview, diagnostics, backward compatibility, and the
    /// partitioned RNG contract.
    /// </summary>
    [TestFixture]
    public sealed class ScenarioPresetLibraryTests
    {
        [Test]
        public void Validate_AcceptsSliceLPreset()
        {
            ScenarioPreset preset = SliceLPresetFactory.Build();
            var diagnostics = new List<string>();
            Assert.IsTrue(ScenarioPresetLibrary.Validate(preset, diagnostics),
                string.Join("; ", diagnostics));
            Assert.AreEqual(0, diagnostics.Count);
        }

        [Test]
        public void Validate_RejectsCanonViolation_Loudly()
        {
            ScenarioPreset preset = SliceLPresetFactory.BuildInvalidPopulationRange();
            var diagnostics = new List<string>();
            Assert.IsFalse(ScenarioPresetLibrary.Validate(preset, diagnostics));
            Assert.IsTrue(diagnostics.Count > 0);
            bool mentionsCanon = false;
            foreach (string diagnostic in diagnostics)
            {
                if (diagnostic.Contains("Canon"))
                {
                    mentionsCanon = true;
                }
            }

            Assert.IsTrue(mentionsCanon, "Expected a Canon calibration diagnostic, got: " + string.Join("; ", diagnostics));
        }

        [Test]
        public void Validate_RejectsInvertedResidentRange()
        {
            ScenarioPreset preset = SliceLPresetFactory.Build();
            preset.StartingWorld.SetPopulationRange(90, 78, 60);
            var diagnostics = new List<string>();
            Assert.IsFalse(ScenarioPresetLibrary.Validate(preset, diagnostics));
        }

        [Test]
        public void Validate_StartDate_LeapRules()
        {
            // 1900 is not a leap year; 1880 and 1904 are.
            Assert.IsFalse(ScenarioDateUtil.IsValidDate(1900, 2, 29));
            Assert.IsTrue(ScenarioDateUtil.IsValidDate(1880, 2, 29));
            Assert.IsTrue(ScenarioDateUtil.IsValidDate(1904, 2, 29));
            Assert.IsFalse(ScenarioDateUtil.IsValidDate(1880, 2, 30));
            Assert.IsFalse(ScenarioDateUtil.IsValidDate(1869, 12, 31));
            Assert.IsFalse(ScenarioDateUtil.IsValidDate(1906, 1, 1));

            ScenarioPreset preset = SliceLPresetFactory.Build(1900, 2, 29);
            var diagnostics = new List<string>();
            Assert.IsFalse(ScenarioPresetLibrary.Validate(preset, diagnostics),
                "1900-02-29 must be rejected as a scenario start date.");
        }

        [Test]
        public void Validate_RejectsDuplicateFixedBusinessIds()
        {
            ScenarioPreset preset = SliceLPresetFactory.Build();
            preset.StartingWorld.EditableFixedBusinesses.Add(
                new ScenarioFixedBusiness("general-store", "Copy", "general", 0, "npc"));
            var diagnostics = new List<string>();
            Assert.IsFalse(ScenarioPresetLibrary.Validate(preset, diagnostics));
        }

        [Test]
        public void Copy_IsDeep_IndependentAndLinked()
        {
            ScenarioPreset source = SliceLPresetFactory.Build();
            ScenarioPreset copy = ScenarioPresetLibrary.Copy(source, "copy-1", "Copy One");

            Assert.AreEqual("copy-1", copy.PresetId);
            Assert.AreEqual(source.PresetId, copy.DerivedFromPresetId);
            Assert.AreEqual(0, copy.Diagnostics.Count);

            copy.StartingWorld.EditableFixedBusinesses[0] =
                new ScenarioFixedBusiness("changed", "Changed", "general", 1, "npc");
            Assert.AreEqual("general-store", source.StartingWorld.FixedBusinesses[0].BusinessId,
                "Mutating the copy must not touch the source.");
        }

        [Test]
        public void Derive_BumpsPatchVersion_AndLinks()
        {
            ScenarioPreset source = SliceLPresetFactory.Build();
            ScenarioPreset derived = ScenarioPresetLibrary.Derive(source, "derived-1", "Derived One");

            Assert.AreEqual(source.PresetId, derived.DerivedFromPresetId);
            Assert.AreEqual("1.0.1", derived.PresetVersion);
        }

        [Test]
        public void MigrateToCurrent_BackfillsV100_Additively()
        {
            ScenarioPreset legacy = SliceLPresetFactory.Build();
            legacy.StartingWorld.SetDefinitionVersion("1.0.0");
            legacy.StartingWorld.EditableFixedBusinesses.Clear();

            var diagnostics = new List<string>();
            ScenarioPreset migrated = ScenarioPresetLibrary.MigrateToCurrent(legacy, diagnostics);

            Assert.AreEqual(ScenarioPresetLibrary.CurrentDefinitionVersion,
                migrated.StartingWorld.DefinitionVersion);
            // Required business ids were backfilled as fixed businesses.
            Assert.AreEqual(legacy.StartingWorld.RequiredBusinessIds.Count,
                migrated.StartingWorld.FixedBusinesses.Count);
            // The source is untouched.
            Assert.AreEqual("1.0.0", legacy.StartingWorld.DefinitionVersion);
            Assert.IsTrue(diagnostics.Count > 0);
        }

        [Test]
        public void MigrateToCurrent_LeavesCurrentAlone()
        {
            ScenarioPreset preset = SliceLPresetFactory.Build();
            var diagnostics = new List<string>();
            ScenarioPreset migrated = ScenarioPresetLibrary.MigrateToCurrent(preset, diagnostics);
            Assert.AreEqual(preset.StartingWorld.DefinitionVersion, migrated.StartingWorld.DefinitionVersion);
            Assert.AreEqual(0, diagnostics.Count);
        }

        [Test]
        public void Preview_IsDeterministic_AndSeedSensitive()
        {
            ScenarioPreset preset = SliceLPresetFactory.Build();
            ScenarioPreviewReport a = ScenarioPresetLibrary.BuildPreview(preset, 42);
            ScenarioPreviewReport b = ScenarioPresetLibrary.BuildPreview(preset, 42);
            ScenarioPreviewReport c = ScenarioPresetLibrary.BuildPreview(preset, 43);

            Assert.AreEqual(a.InputFingerprint, b.InputFingerprint);
            Assert.IsFalse(string.Equals(a.InputFingerprint, c.InputFingerprint),
                "Different seeds must produce different preview fingerprints.");
            Assert.AreEqual(60, a.MinResidents);
            Assert.AreEqual(78, a.PreferredResidents);
            Assert.AreEqual(90, a.MaxResidents);
            Assert.AreEqual(3, a.FixedBusinessIds.Count);
            Assert.AreEqual("1880-03-01", a.StartDateIso);
        }

        [Test]
        public void Fingerprint_ChangesWhenAuthoredValuesChange()
        {
            ScenarioPreset preset = SliceLPresetFactory.Build();
            string before = ScenarioPresetLibrary.ComputeFingerprint(preset, 7);
            preset.StartingWorld.EditableEconomyPolicy.SetCashBand(30000, 150000);
            string after = ScenarioPresetLibrary.ComputeFingerprint(preset, 7);
            Assert.IsFalse(string.Equals(before, after),
                "Any authored change must alter the reproducibility fingerprint.");
        }

        [Test]
        public void RngStreams_AreDeterministic_AndPartitioned()
        {
            var a = new GenerationRngStreams("1.1.0", 99, 3650);
            var b = new GenerationRngStreams("1.1.0", 99, 3650);

            int aPop1 = a.Stream(GenerationConcern.Population).Next();
            int bPop1 = b.Stream(GenerationConcern.Population).Next();
            Assert.AreEqual(aPop1, bPop1, "Same inputs must yield identical streams.");

            // Partitioning: drawing from Debt must not disturb Population.
            int aDebt = a.Stream(GenerationConcern.Debt).Next();
            int bDebt = b.Stream(GenerationConcern.Debt).Next();
            Assert.AreEqual(aDebt, bDebt);
            int aPop2 = a.Stream(GenerationConcern.Population).Next();
            int bPop2 = b.Stream(GenerationConcern.Population).Next();
            Assert.AreEqual(aPop2, bPop2, "Per-concern streams must be independent.");

            // Attempts repartition.
            var next = a.NextAttempt();
            Assert.AreEqual(1, next.Attempt);
            Assert.IsFalse(next.Stream(GenerationConcern.Population).Next() == aPop1
                && next.Stream(GenerationConcern.Population).Next() == aPop2,
                "A retry attempt must use different stream values.");
        }

        [Test]
        public void RngStreams_SeedSensitive()
        {
            var a = new GenerationRngStreams("1.1.0", 1, 3650);
            var b = new GenerationRngStreams("1.1.0", 2, 3650);
            Assert.IsFalse(a.Stream(GenerationConcern.Population).Next() == b.Stream(GenerationConcern.Population).Next()
                && a.Stream(GenerationConcern.Debt).Next() == b.Stream(GenerationConcern.Debt).Next(),
                "Different seeds must (overwhelmingly likely) differ.");
        }

        [Test]
        public void CompareVersions_OrdersCorrectly()
        {
            Assert.IsTrue(ScenarioPresetLibrary.CompareVersions("1.0.0", "1.1.0") < 0);
            Assert.IsTrue(ScenarioPresetLibrary.CompareVersions("1.1.0", "1.0.0") > 0);
            Assert.AreEqual(0, ScenarioPresetLibrary.CompareVersions("1.1.0", "1.1.0"));
        }
    }
}
